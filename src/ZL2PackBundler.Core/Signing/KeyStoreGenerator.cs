using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZL2PackBundler.Core.Signing;

/// <summary>
/// 生成自动测试 keystore（RSA-2048，30 年有效，PKCS12）。
/// 优先使用 keytool（JAVA_HOME → 便携 JRE → PATH），保证产出的 keystore 带别名私钥、
/// apksigner 一定能加载；无任何 keytool 时退回 C# 自签导出。
/// 检测到旧的无私钥 keystore 时自动重新生成。
/// </summary>
public static class KeyStoreGenerator
{
    public static string Create(string outputPath, string? alias = null, string? password = null, Action<string>? log = null)
    {
        var aliasName = alias ?? "zl2packbundler";
        var pass = password ?? ApkSigner.AutoKeyStorePassword;

        if (File.Exists(outputPath) && !IsUsable(outputPath, aliasName, pass))
        {
            log?.Invoke("检测到损坏的 keystore（别名无私钥），自动重新生成…");
            File.Delete(outputPath);
        }
        if (File.Exists(outputPath)) return outputPath;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        log?.Invoke("生成 PKCS12 keystore（RSA-2048，30 年有效）…");

        var keytool = LocateKeytool();
        if (keytool != null)
        {
            ProcessRunner.Run(keytool, new[]
            {
                "-genkeypair", "-keystore", outputPath, "-storetype", "PKCS12",
                "-alias", aliasName,
                "-keyalg", "RSA", "-keysize", "2048", "-validity", "10950",
                "-storepass", pass, "-keypass", pass,
                "-dname", "CN=ZL2PackBundler, OU=PackBundler, O=ZL2PackBundler, C=CN"
            }, log, TimeSpan.FromMinutes(5));
            return outputPath;
        }

        // 兜底：纯 C# 自签（部分机器上 Export(Pfx) 可能不挂私钥别名，所以只是最后手段）
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ZL2PackBundler, OU=PackBundler, O=ZL2PackBundler, C=CN",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(30));
        File.WriteAllBytes(outputPath, cert.Export(X509ContentType.Pfx, pass));
        return outputPath;
    }

    private static string? LocateKeytool()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Path.Combine(javaHome, "bin", "keytool.exe");
            if (File.Exists(p)) return p;
        }
        var bundled = Path.Combine(Apk.BundledTools.RootDir, "runtime", "jre", "bin", "keytool.exe");
        if (File.Exists(bundled)) return bundled;
        try
        {
            using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("keytool", "-help")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (probe != null) return "keytool";
        }
        catch
        {
            // keytool 不在 PATH
        }
        return null;
    }

    private static bool IsUsable(string path, string alias, string password)
    {
        // 决定性校验：keytool -list 的条目类型——PrivateKeyEntry 才带私钥（cert-only 是 trustedCertEntry）
        var keytool = LocateKeytool();
        if (keytool != null)
        {
            try
            {
                var output = ProcessRunner.Run(keytool, new[]
                {
                    "-list", "-keystore", path,
                    "-storepass", password,
                    "-alias", alias
                }, null, TimeSpan.FromMinutes(2));
                return output.Contains("PrivateKeyEntry", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        // 无 keytool 时退化为 .NET 检查（不如 keytool 精确，但聊胜于无）
        try
        {
            using var cert = new X509Certificate2(path, password, X509KeyStorageFlags.Exportable);
            return cert.HasPrivateKey;
        }
        catch
        {
            return false;
        }
    }
}
