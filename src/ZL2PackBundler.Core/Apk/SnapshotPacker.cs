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

    public static void Create(string sourceDir, string outputZip, Action<string>? progress = null)
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
        foreach (var (path, relative) in files)
        {
            var level = StoredExtensions.Contains(Path.GetExtension(path))
                     || relative.StartsWith("assets/objects/", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            var entry = zip.CreateEntry(relative, level);
            using var src = File.OpenRead(path);
            using var dst = entry.Open();
            src.CopyTo(dst);
            done++;
            if (done % 200 == 0) progress?.Invoke($"已打包 {done}/{files.Count} 个文件");
        }
        progress?.Invoke($"打包完成：{files.Count} 个文件");
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
