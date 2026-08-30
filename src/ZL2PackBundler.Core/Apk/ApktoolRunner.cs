namespace ZL2PackBundler.Core.Apk;

/// <summary>apktool 的下载缓存与 decode/build 封装（仅用于官方原版 APK 的清单注入）。</summary>
public static class ApktoolRunner
{
    private const string ApktoolVersion = "2.9.3";
    private const string ApktoolUrl =
        "https://github.com/iBotPeaches/Apktool/releases/download/v" + ApktoolVersion + "/apktool_" + ApktoolVersion + ".jar";

    public static string ToolsDir =>
        Environment.GetEnvironmentVariable("ZL2PB_TOOLS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZL2PackBundler", "tools");

    public static string ApktoolJarPath => Path.Combine(ToolsDir, "apktool_" + ApktoolVersion + ".jar");

    public static string EnsureApktool(Action<string>? log = null)
    {
        // 便携运行时内置的 apktool 优先
        var bundledJar = BundledTools.TryLocateBundledApktool(log);
        if (bundledJar != null) return bundledJar;

        Directory.CreateDirectory(ToolsDir);
        if (File.Exists(ApktoolJarPath) && new FileInfo(ApktoolJarPath).Length > 10_000_000)
            return ApktoolJarPath;

        log?.Invoke("首次使用需下载 apktool（约 22MB）…");
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                using var resp = client.GetAsync(ApktoolUrl, HttpCompletionOption.ResponseHeadersRead)
                    .GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(ApktoolJarPath);
                resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                if (new FileInfo(ApktoolJarPath).Length > 10_000_000) return ApktoolJarPath;
                throw new IOException("apktool 下载不完整");
            }
            catch (Exception e)
            {
                last = e;
                log?.Invoke($"下载失败（第 {attempt} 次）：{e.Message}");
                try { File.Delete(ApktoolJarPath); } catch { /* 尽力清理 */ }
            }
        }

        // 兜底：用系统 curl.exe（对部分网络环境更稳）
        log?.Invoke("改用 curl.exe 下载…");
        ProcessRunner.Run("curl.exe",
            new[] { "-L", "--fail", "--retry", "3", "-o", ApktoolJarPath, ApktoolUrl },
            log, TimeSpan.FromMinutes(15));

        if (!File.Exists(ApktoolJarPath) || new FileInfo(ApktoolJarPath).Length <= 10_000_000)
            throw new InvalidOperationException(
                "apktool 下载失败（" + (last?.Message ?? "curl 失败") + "）。" +
                "可手动下载 apktool_" + ApktoolVersion + ".jar 放到：" + ApktoolJarPath);
        return ApktoolJarPath;
    }

    /// <summary>
    /// 解码 APK（-s 跳过 smali，速度较快；必须解码资源，否则 AndroidManifest.xml 保持二进制无法修补）。
    /// </summary>
    public static void Decode(string apkPath, string outDir, string sdkBuildToolsDir, Action<string>? log = null)
    {
        Run(new[] { "d", "-f", "-s", apkPath, "-o", outDir }, sdkBuildToolsDir, log);
    }

    public static void Build(string projectDir, string outputApk, string sdkBuildToolsDir, Action<string>? log = null)
    {
        Run(new[] { "b", projectDir, "-o", outputApk }, sdkBuildToolsDir, log);
    }

    private static void Run(string[] args, string sdkBuildToolsDir, Action<string>? log)
    {
        var jar = EnsureApktool(log);
        var java = Signing.ApkSigner.LocateJava();
        var allArgs = new List<string> { "-jar", jar };
        allArgs.AddRange(args);
        // apktool 某些操作需要 aapt/aapt2：把 build-tools 目录放进 PATH；大 APK 重建较慢，放宽超时
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        ProcessRunner.Run(java, allArgs, log, TimeSpan.FromMinutes(30),
            new Dictionary<string, string> { ["PATH"] = sdkBuildToolsDir + ";" + path });
    }
}
