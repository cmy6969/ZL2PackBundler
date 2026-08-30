using System.IO.Compression;

namespace ZL2PackBundler.Core.Apk;

/// <summary>
/// 便携运行时：把内嵌的 Bundled/runtime.zip（jre + apktool + zipalign + aapt2 + apksigner）
/// 解包到 %LOCALAPPDATA%/ZL2PackBundler/runtime。未内置时回退系统 JDK / Android SDK。
/// </summary>
public static class BundledTools
{
    public const string InstallerConfigZipEntry = "assets/zl2packbundler/installer-config.json";

    private const string RuntimeResourceName = "ZL2PackBundler.Core.Bundled.runtime.zip";

    public static string RootDir =>
        Environment.GetEnvironmentVariable("ZL2PB_TOOLS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZL2PackBundler");

    public static string RuntimeDir => Path.Combine(RootDir, "runtime");

    public static bool HasEmbeddedRuntime()
    {
        using var s = typeof(BundledTools).Assembly.GetManifestResourceStream(RuntimeResourceName);
        return s != null;
    }

    /// <summary>解包内嵌运行时（幂等）。未内置时抛异常，调用方应回退系统工具。</summary>
    public static string EnsureRuntime(Action<string>? log = null)
    {
        var java = Path.Combine(RuntimeDir, "jre", "bin", "java.exe");
        if (File.Exists(java)) return RuntimeDir;

        using var s = typeof(BundledTools).Assembly.GetManifestResourceStream(RuntimeResourceName)
            ?? throw new InvalidOperationException(
                "此版本未内置便携运行时。请安装 JDK 17+ 与 Android SDK build-tools，或使用 Releases 里的便携版。");
        log?.Invoke("首次运行解包便携运行时（JRE/apktool/签名工具）…");
        Directory.CreateDirectory(RuntimeDir);
        ExtractZip(s, RuntimeDir);
        if (!File.Exists(java))
            throw new InvalidOperationException("便携运行时解包不完整（缺 JRE）。");
        return RuntimeDir;
    }

    /// <summary>解包 zip（兼容 Compress-Archive 的反斜杠条目名与尾部分隔符）。</summary>
    public static void ExtractZip(Stream stream, string destDir)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var dest = Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar));
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(dest);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var es = entry.Open();
            using var fs = File.Create(dest);
            es.CopyTo(fs);
        }
    }

    /// <summary>java：JAVA_HOME 优先，其次便携 JRE，最后 PATH 上的 java。</summary>
    public static string LocateJava(Action<string>? log = null)
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(p)) return p;
        }
        if (HasEmbeddedRuntime())
        {
            var bundled = Path.Combine(EnsureRuntime(log), "jre", "bin", "java.exe");
            if (File.Exists(bundled)) return bundled;
        }
        return "java";
    }

    /// <summary>便携的 build-tools 布局目录（zipalign.exe / aapt2.exe / lib/apksigner.jar）。</summary>
    public static string? TryLocateBundledBuildTools(Action<string>? log = null)
    {
        if (!HasEmbeddedRuntime()) return null;
        var dir = EnsureRuntime(log);
        return File.Exists(Path.Combine(dir, "zipalign.exe"))
            && File.Exists(Path.Combine(dir, "aapt2.exe"))
            && File.Exists(Path.Combine(dir, "lib", "apksigner.jar"))
            ? dir
            : null;
    }

    /// <summary>apktool.jar：便携运行时优先，其次已缓存下载，最后联网下载。</summary>
    public static string? TryLocateBundledApktool(Action<string>? log = null)
    {
        if (!HasEmbeddedRuntime()) return null;
        var jar = Path.Combine(EnsureRuntime(log), "apktool.jar");
        return File.Exists(jar) ? jar : null;
    }

    /// <summary>内嵌的预编译安装器 dex 写入 workDir，返回路径。</summary>
    public static string ExtractInstallerDex(string workDir, Action<string>? log = null)
    {
        const string resource = "ZL2PackBundler.Core.Apk.Installer.BundledPackInstaller.dex";
        using var s = typeof(BundledTools).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("内嵌安装器 dex 缺失（EmbeddedResource 未打包）。");
        var path = Path.Combine(workDir, "installer.dex");
        using var fs = File.Create(path);
        s.CopyTo(fs);
        return path;
    }

    /// <summary>注入配置资产内容（installer-config.json）。</summary>
    public static string BuildInstallerConfigJson(string launcherActivity, string importAlias)
    {
        return "{\u0022launcher\u0022:\u0022" + launcherActivity
             + "\u0022,\u0022importAlias\u0022:\u0022" + importAlias + "\u0022}";
    }
}
