using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ZL2PackBundler.Core.Apk;

/// <summary>Android 构建工具辅助（仅 aapt2 badging；安装器 dex 已预编译内嵌，无需 javac/d8）。</summary>
public static class AndroidBuildTools
{
    /// <summary>用 aapt2 dump badging 读取 APK 的包名（AGP 8 的清单里可能没有 package 属性）。</summary>
    public static string GetPackageName(string apkPath, string sdkBuildToolsDir)
    {
        var aapt2 = Path.Combine(sdkBuildToolsDir, "aapt2.exe");
        if (!File.Exists(aapt2))
            throw new InvalidOperationException("aapt2.exe 不存在（build-tools 不完整）。");
        var output = RunCaptured(aapt2, new[] { "dump", "badging", apkPath }, null);
        var m = Regex.Match(output, "package: name='([^']+)'");
        if (!m.Success)
            throw new InvalidDataException("aapt2 无法读取 APK 包名。");
        return m.Groups[1].Value;
    }

    private static string RunCaptured(string fileName, IEnumerable<string> args, Action<string>? log)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 " + fileName);
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (stdout.Length > 0) log?.Invoke(stdout.TrimEnd());
        if (stderr.Length > 0) log?.Invoke(stderr.TrimEnd());
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                Path.GetFileName(fileName) + " 退出码 " + p.ExitCode + "：\n" + stdout + "\n" + stderr);
        return stdout + stderr;
    }
}
