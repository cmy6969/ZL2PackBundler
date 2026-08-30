using System.IO.Compression;
using Xunit;
using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Tests;

public class AnalyzerTests
{
    [Fact]
    public void DirectoryWithVersionIsSnapshot()
    {
        var root = MakeDir("mc");
        Directory.CreateDirectory(Path.Combine(root, "versions", "1.20.1"));
        File.WriteAllText(Path.Combine(root, "versions", "1.20.1", "1.20.1.json"), "{\"id\":\"1.20.1\"}");
        File.WriteAllText(Path.Combine(root, "versions", "1.20.1", "1.20.1.jar"), "jar");
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        var result = InputAnalyzer.Analyze(root);
        Assert.Equal(BundledPackType.Snapshot, result.Type);
        Assert.Equal(PackFormat.RawSnapshot, result.Format);
        Assert.Equal("1.20.1", result.McVersion);
        Assert.Contains(result.OfflineReport, o => o.Path == "libraries/" && !o.Present);
    }

    [Fact]
    public void DirectoryWithoutVersionsThrows()
    {
        var root = MakeDir("bad");
        Assert.Throws<InvalidDataException>(() => InputAnalyzer.Analyze(root));
    }

    [Fact]
    public void ModrinthZipIsPackZip()
    {
        var zipPath = MakeZip("mr.zip", ("modrinth.index.json", "{\"name\":\"MyPack\",\"formatVersion\":1}"));
        var result = InputAnalyzer.Analyze(zipPath);
        Assert.Equal(BundledPackType.PackZip, result.Type);
        Assert.Equal(PackFormat.Modrinth, result.Format);
        Assert.Equal("MyPack", result.NameHint);
    }

    [Fact]
    public void CurseForgeZipIsPackZip()
    {
        var zipPath = MakeZip("cf.zip", ("manifest.json", "{\"minecraft\":{\"version\":\"1.20.1\"},\"name\":\"CFPack\"}"));
        var result = InputAnalyzer.Analyze(zipPath);
        Assert.Equal(PackFormat.CurseForge, result.Format);
        Assert.Equal("CFPack", result.NameHint);
    }

    [Fact]
    public void McbbsZipIsPackZip()
    {
        var zipPath = MakeZip("mc.zip", ("mcbbs.packmeta", "{}"));
        Assert.Equal(PackFormat.Mcbbs, InputAnalyzer.Analyze(zipPath).Format);
    }

    [Fact]
    public void MultiMcZipIsPackZip()
    {
        var zipPath = MakeZip("mmc.zip", ("mmc-pack.json", "{}"));
        Assert.Equal(PackFormat.MultiMC, InputAnalyzer.Analyze(zipPath).Format);
    }

    [Fact]
    public void RawSnapshotZipIsSnapshot()
    {
        var zipPath = MakeZip("raw.zip",
            ("versions/1.20.1/1.20.1.json", "{\"id\":\"1.20.1\"}"),
            ("versions/1.20.1/1.20.1.jar", "jar"),
            ("mods/mod.jar", "mod"));
        var result = InputAnalyzer.Analyze(zipPath);
        Assert.Equal(BundledPackType.Snapshot, result.Type);
        Assert.Equal("1.20.1", result.McVersion);
    }

    [Fact]
    public void GarbageZipThrows()
    {
        var zipPath = MakeZip("garbage.zip", ("readme.txt", "hello"));
        Assert.Throws<InvalidDataException>(() => InputAnalyzer.Analyze(zipPath));
    }

    private static string MakeDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeZip(string name, params (string, string)[] entries)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "zl2pb-" + name + "-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = zip.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return zipPath;
    }
}
