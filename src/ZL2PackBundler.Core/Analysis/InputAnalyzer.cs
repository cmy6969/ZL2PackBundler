using System.IO.Compression;
using System.Text.Json;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Analysis;

public enum PackFormat { RawSnapshot, Mcbbs, Modrinth, CurseForge, MultiMC }

public sealed record OfflineItem(string Path, bool Present, string Label);

public sealed class AnalysisResult
{
    public required BundledPackType Type { get; init; }
    public required PackFormat Format { get; init; }
    public string? NameHint { get; init; }
    public string? McVersion { get; init; }
    public required List<OfflineItem> OfflineReport { get; init; }
}

public static class InputAnalyzer
{
    public static AnalysisResult Analyze(string inputPath)
    {
        if (Directory.Exists(inputPath)) return AnalyzeDirectory(inputPath);
        if (File.Exists(inputPath)) return AnalyzeArchive(inputPath);
        throw new FileNotFoundException("整合包输入不存在", inputPath);
    }

    private static AnalysisResult AnalyzeDirectory(string dir)
    {
        var versionsDir = Path.Combine(dir, "versions");
        if (!Directory.Exists(versionsDir))
            throw new InvalidDataException("不是 .minecraft 目录：缺少 versions/。");

        var found = FindVersion(versionsDir);
        if (found.Version == null || !found.HasJson)
            throw new InvalidDataException("versions/ 下没有有效版本（需要 <名称>/<名称>.json）。");

        return new AnalysisResult
        {
            Type = BundledPackType.Snapshot,
            Format = PackFormat.RawSnapshot,
            NameHint = Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            McVersion = found.Version,
            OfflineReport = BuildOfflineReport(dir, found.Version, found.HasJar)
        };
    }

    private static (string? Version, bool HasJson, bool HasJar) FindVersion(string versionsDir)
    {
        foreach (var sub in Directory.EnumerateDirectories(versionsDir))
        {
            var name = Path.GetFileName(sub);
            var json = Path.Combine(sub, name + ".json");
            if (!File.Exists(json)) continue;
            var id = ReadVersionId(json);
            if (id == null) continue;
            return (id, true, File.Exists(Path.Combine(sub, name + ".jar")));
        }
        return (null, false, false);
    }

    private static string? ReadVersionId(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch { return null; }
    }

    private static List<OfflineItem> BuildOfflineReport(string dir, string mcVersion, bool hasJar)
    {
        bool FileOk(string rel) => File.Exists(Path.Combine(dir, rel));
        bool DirOk(string rel) => Directory.Exists(Path.Combine(dir, rel));
        return new List<OfflineItem>
        {
            new($"versions/{mcVersion}/{mcVersion}.json", FileOk($"versions/{mcVersion}/{mcVersion}.json"), "版本清单"),
            new($"versions/{mcVersion}/{mcVersion}.jar", hasJar, "版本主 Jar"),
            new("libraries/", DirOk("libraries/"), "原版库文件"),
            new($"assets/indexes/{mcVersion}.json", FileOk($"assets/indexes/{mcVersion}.json"), "资源索引"),
            new("assets/objects/", DirOk("assets/objects/"), "资源对象")
        };
    }

    private static AnalysisResult AnalyzeArchive(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        bool Has(string n) => names.Any(x =>
            string.Equals(x.TrimEnd('/'), n, StringComparison.OrdinalIgnoreCase));

        if (Has("modrinth.index.json")) return PackZip(PackFormat.Modrinth, zip, zipPath);
        if (Has("mmc-pack.json") || Has("instance.cfg")) return PackZip(PackFormat.MultiMC, zip, zipPath);
        if (Has("mcbbs.packmeta")) return PackZip(PackFormat.Mcbbs, zip, zipPath);
        if (Has("manifest.json") && CurseForgeLike(zip)) return PackZip(PackFormat.CurseForge, zip, zipPath);

        var versionJson = names
            .Where(n => n.StartsWith("versions/", StringComparison.OrdinalIgnoreCase)
                     && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.TrimEnd('/').Split('/').Skip(1).FirstOrDefault())
            .FirstOrDefault(x => !string.IsNullOrEmpty(x));
        var looksLikeMinecraft = versionJson != null &&
            (names.Any(n => n.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase))
          || names.Any(n => n.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
          || names.Any(n => n.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)));
        if (looksLikeMinecraft)
        {
            return new AnalysisResult
            {
                Type = BundledPackType.Snapshot,
                Format = PackFormat.RawSnapshot,
                NameHint = Path.GetFileNameWithoutExtension(zipPath),
                McVersion = versionJson,
                OfflineReport = new List<OfflineItem>
                {
                    new("libraries/", names.Any(n => n.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase)), "原版库文件"),
                    new("assets/", names.Any(n => n.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)), "资源目录")
                }
            };
        }

        throw new InvalidDataException(
            "无法识别整合包格式。支持：MCBBS(mcbbs.packmeta)、Modrinth(.mrpack)、CurseForge、MultiMC、或裸 .minecraft 快照 zip。");
    }

    private static bool CurseForgeLike(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json");
        if (entry == null) return false;
        try
        {
            using var reader = new StreamReader(entry.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            return doc.RootElement.TryGetProperty("minecraft", out var mc) && mc.ValueKind == JsonValueKind.Object;
        }
        catch { return false; }
    }

    private static AnalysisResult PackZip(PackFormat format, ZipArchive zip, string zipPath)
    {
        var name = format switch
        {
            PackFormat.Modrinth => ReadName(zip, "modrinth.index.json"),
            PackFormat.CurseForge => ReadName(zip, "manifest.json"),
            _ => null
        } ?? Path.GetFileNameWithoutExtension(zipPath);
        return new AnalysisResult
        {
            Type = BundledPackType.PackZip,
            Format = format,
            NameHint = name,
            McVersion = null,
            OfflineReport = new List<OfflineItem>
            {
                new("mods/ 及依赖", false, "整合包依赖模组（首次导入时可能联网下载）")
            }
        };
    }

    private static string? ReadName(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry == null) return null;
        try
        {
            using var reader = new StreamReader(entry.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            return doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch { return null; }
    }
}
