using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ZL2PackBundler.Core.Signing;

/// <summary>纯 C# 生成 PKCS12 keystore（自签名 RSA-2048，30 年有效），不再依赖 keytool。</summary>
public static class KeyStoreGenerator
{
    public static string Create(string outputPath, string? alias = null, string? password = null, Action<string>? log = null)
    {
        if (File.Exists(outputPath)) return outputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var aliasName = alias ?? "zl2packbundler";
        var pass = password ?? ApkSigner.AutoKeyStorePassword;

        log?.Invoke("生成 PKCS12 keystore（RSA-2048，30 年有效）…");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ZL2PackBundler, OU=PackBundler, O=ZL2PackBundler, C=CN",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(30));
        cert.FriendlyName = aliasName; // 作为 PKCS12 条目别名
        File.WriteAllBytes(outputPath, cert.Export(X509ContentType.Pfx, pass));
        return outputPath;
    }
}
