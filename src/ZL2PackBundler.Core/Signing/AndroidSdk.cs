namespace ZL2PackBundler.Core.Signing;

public sealed class AndroidSdk
{
    public required string BuildToolsDir { get; init; }
    /// <summary>SDK 根目录（探测到/指定时填充）。</summary>
    public string? SdkRoot { get; init; }
    public string Zipalign => Path.Combine(BuildToolsDir, "zipalign.exe");
    public string ApkSignerJar => Path.Combine(BuildToolsDir, "lib", "apksigner.jar");

    /// <summary>探测 Android SDK；找不到返回 null（不抛异常）。</summary>
    public static AndroidSdk? TryLocate(string? explicitDir = null)
    {
        var candidates = BuildCandidates(explicitDir);
        foreach (var root in candidates)
        {
            var found = FindIn(root);
            if (found != null)
            {
                if (explicitDir == null && !string.Equals(root, SdkSettings.Load(), StringComparison.OrdinalIgnoreCase))
                    SdkSettings.Save(root); // 记住本次探测结果
                return found;
            }
        }
        return null;
    }

    /// <summary>探测 Android SDK；找不到抛出带排查指引的异常。</summary>
    public static AndroidSdk Locate(string? explicitDir = null)
    {
        var sdk = TryLocate(explicitDir);
        if (sdk != null) return sdk;

        var searched = BuildCandidates(explicitDir)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Take(6)
            .ToList();
        throw new InvalidOperationException(
            "未找到 Android SDK build-tools（zipalign/apksigner）。\n" +
            "已查找：" + string.Join("、", searched) + "（以及 PATH 中的目录祖先与各盘符下的 Android\\Sdk）。\n" +
            "解决方式（任选其一）：\n" +
            "  1) 命令行加 --sdk 指定 SDK 目录，例如：--sdk E:\\Develop\\AndroidSDK（首次指定后自动记住，之后无需再传）；\n" +
            "  2) 设置环境变量 ANDROID_HOME 指向 SDK 目录；\n" +
            "  3) 用 Android Studio SDK Manager 安装 build-tools。");
    }

    private static List<string> BuildCandidates(string? explicitDir)
    {
        var list = new List<string>();

        // 1) 显式指定：只信任这一处，保证行为可预期
        if (!string.IsNullOrEmpty(explicitDir))
        {
            list.Add(explicitDir);
            return list;
        }

        // 2) 环境变量
        var home = Environment.GetEnvironmentVariable("ANDROID_HOME")
                ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(home)) list.Add(home);

        // 3) 上次记住的目录
        var saved = SdkSettings.Load();
        if (!string.IsNullOrEmpty(saved)) list.Add(saved);

        // 4) 常见安装路径
        list.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"));
        list.Add(@"C:\Android\Sdk");
        list.Add(@"D:\Android\Sdk");

        // 5) PATH 条目祖先扫描（SDK 的 build-tools/ndk 常被加进 PATH）
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in pathEntries)
        {
            var dir = NormalizePathEntry(entry);
            if (string.IsNullOrEmpty(dir)) continue;
            try
            {
                for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
                {
                    if (Directory.Exists(Path.Combine(d.FullName, "build-tools")))
                    {
                        list.Add(d.FullName);
                        break;
                    }
                }
            }
            catch { /* 无效条目忽略 */ }
        }

        // 6) 各盘符根下的 Android\Sdk
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady))
                list.Add(Path.Combine(drive.RootDirectory.FullName, "Android", "Sdk"));
        }
        catch { /* 无权限枚举盘符时忽略 */ }

        return list.Where(d => !string.IsNullOrWhiteSpace(d))
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    private static string NormalizePathEntry(string entry)
    {
        var dir = entry.Trim().Trim('"').Trim();
        if (dir.Length == 0) return dir;
        // MSYS/Git Bash 风格路径 /c/... -> C:\...
        if (dir.Length >= 3 && dir[0] == '/' && char.IsLetter(dir[1]) && dir[2] == '/')
            return dir[1] + ":\\" + dir[3..];
        return dir;
    }

    private static AndroidSdk? FindIn(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        var btRoot = Path.Combine(root, "build-tools");
        if (!Directory.Exists(btRoot)) return null;
        var best = Directory.EnumerateDirectories(btRoot)
            .Where(d => File.Exists(Path.Combine(d, "zipalign.exe"))
                     && File.Exists(Path.Combine(d, "lib", "apksigner.jar")))
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return best == null ? null : new AndroidSdk { BuildToolsDir = best, SdkRoot = root };
    }
}
