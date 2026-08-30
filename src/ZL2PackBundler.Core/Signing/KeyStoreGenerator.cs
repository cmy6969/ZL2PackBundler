using System.Diagnostics;

namespace ZL2PackBundler.Core.Signing;

public static class KeyStoreGenerator
{
    public static string Create(string outputPath, string? alias = null, string? password = null, Action<string>? log = null)
    {
        if (File.Exists(outputPath)) return outputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var aliasName = alias ?? "zl2packbundler";
        var pass = password ?? ApkSigner.AutoKeyStorePassword;
        var psi = new ProcessStartInfo(LocateKeytool())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[]
        {
            "-genkeypair", "-keystore", outputPath, "-alias", aliasName,
            "-keyalg", "RSA", "-keysize", "2048", "-validity", "10950",
            "-storepass", pass,
            "-keypass", pass,
            "-dname", "CN=ZL2PackBundler, OU=PackBundler, O=ZL2PackBundler, L=Unknown, ST=Unknown, C=CN"
        }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        log?.Invoke(stdout + stderr);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"keytool 失败：{stderr}");
        return outputPath;
    }

    private static string LocateKeytool()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Path.Combine(javaHome, "bin", "keytool.exe");
            if (File.Exists(p)) return p;
        }
        return "keytool";
    }
}
