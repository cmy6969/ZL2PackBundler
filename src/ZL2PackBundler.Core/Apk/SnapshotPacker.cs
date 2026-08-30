using System.IO.Compression;

namespace ZL2PackBundler.Core.Apk;

public static class SnapshotPacker
{
    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jar", ".zip", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ogg", ".mp3", ".m4a", ".flac",
        ".pack", ".litematic", ".schematic", ".nbs", ".gz", ".xz", ".7z", ".rar"
    };

    private static readonly HashSet<string> ExcludedRootNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bundled_pack_version", "launcher_profiles.json", "usercache.json", "usernamecache.json",
        ".ds_store", "thumbs.db"
    };

    private static readonly HashSet<string> ExcludedRootDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "crash-reports", "crash-reports-modded"
    };

    /// <summary>打包游戏目录；返回修复的版本 json 数量（重复 libraries 去重）。</summary>
    public static int Create(string sourceDir, string outputZip, Action<string>? progress = null)
    {
        var outputFull = Path.GetFullPath(outputZip);
        var files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFullPath(f), outputFull, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Relative: Path.GetRelativePath(sourceDir, f).Replace('\\', '/')))
            .Where(x => ShouldInclude(x.Relative))
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var zip = ZipFile.Open(outputZip, ZipArchiveMode.Create);
        var done = 0;
        var fixedJsons = 0;
        foreach (var (path, relative) in files)
        {
            var level = StoredExtensions.Contains(Path.GetExtension(path))
                     || relative.StartsWith("assets/objects/", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            var entry = zip.CreateEntry(relative, level);

            // 版本 json：修复重复 libraries（PCL2 导出常见），修复后写入清理版本
            if (IsVersionJson(relative))
            {
                var bytes = File.ReadAllBytes(path);
                if (VersionJsonSanitizer.TrySanitize(bytes, out var sanitized))
                {
                    fixedJsons++;
                    progress?.Invoke("修复版本 json 重复库条目：" + relative);
                    using var dst = entry.Open();
                    dst.Write(sanitized);
                    done++;
                    continue;
                }
            }

            using var src = File.OpenRead(path);
            using var dst2 = entry.Open();
            src.CopyTo(dst2);
            done++;
            if (done % 200 == 0) progress?.Invoke($"已打包 {done}/{files.Count} 个文件");
        }
        progress?.Invoke($"打包完成：{files.Count} 个文件");
        return fixedJsons;
    }

    /// <summary>versions/&lt;名称&gt;/&lt;名称&gt;.json 才视为版本清单。</summary>
    private static bool IsVersionJson(string relative)
    {
        var parts = relative.Split('/');
        if (parts.Length != 3 || parts[0] != "versions") return false;
        return string.Equals(parts[2], parts[1] + ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldInclude(string relative)
    {
        var parts = relative.Split('/');
        if (parts.Length >= 2 && ExcludedRootDirs.Contains(parts[0])) return false;
        if (parts.Length == 1)
        {
            var f = parts[0];
            if (ExcludedRootNames.Contains(f)) return false;
            if (f.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase)
                && f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
