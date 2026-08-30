using System.IO.Compression;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

public class PackerTests
{
    [Fact]
    public void SnapshotPackerExcludesRuntimeArtifacts()
    {
        var root = MakeDir();
        Directory.CreateDirectory(Path.Combine(root, "versions"));
        File.WriteAllText(Path.Combine(root, "versions", "x.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        File.WriteAllText(Path.Combine(root, "logs", "latest.log"), "log");
        File.WriteAllText(Path.Combine(root, "usercache.json"), "private");
        File.WriteAllText(Path.Combine(root, "options.txt"), "keep");

        var zipPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        SnapshotPacker.Create(root, zipPath);

        using var z1 = ZipFile.OpenRead(zipPath);
        var names = z1.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("versions/x.json", names);
        Assert.Contains("options.txt", names);
        Assert.DoesNotContain(names, n => n.StartsWith("logs/"));
        Assert.DoesNotContain("usercache.json", names);
    }

    [Fact]
    public void JarEntriesAreStored()
    {
        var root = MakeDir();
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        var bytes = new byte[4096];
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(root, "mods", "mod.jar"), bytes);

        var zipPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        SnapshotPacker.Create(root, zipPath);

        using var z2 = ZipFile.OpenRead(zipPath);
        var entry = z2.GetEntry("mods/mod.jar")!;
        Assert.Equal(entry.Length, entry.CompressedLength); // STORED
    }

    [Fact]
    public void RebuildStripsSignaturesAndEmbedsAssets()
    {
        var apkPath = Path.Combine(Path.GetTempPath(), "base-" + Guid.NewGuid().ToString("N") + ".apk");
        using (var w = ZipFile.Open(apkPath, ZipArchiveMode.Create))
        {
            using (var s1 = w.CreateEntry("META-INF/MANIFEST.MF").Open()) s1.Write(new byte[] { 1 });
            using (var s2 = w.CreateEntry("META-INF/CERT.RSA").Open()) s2.Write(new byte[] { 2 });
            using (var s3 = w.CreateEntry("META-INF/services/org.example.Service").Open()) s3.Write(new byte[] { 3 });
            using (var s4 = w.CreateEntry("classes.dex").Open()) s4.Write(new byte[] { 4, 5, 6 });
            using (var s5 = w.CreateEntry("res/x.txt").Open()) s5.Write(new byte[] { 7 });
        }
        var packPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(packPath, new byte[] { 9, 9, 9 });
        var outPath = Path.Combine(Path.GetTempPath(), "out-" + Guid.NewGuid().ToString("N") + ".apk");

        ApkRebuilder.Rebuild(apkPath, outPath, "{\"schema\":1}", packPath);

        using var z3 = ZipFile.OpenRead(outPath);
        var names = z3.Entries.Select(e => e.FullName).ToList();
        Assert.DoesNotContain("META-INF/MANIFEST.MF", names);
        Assert.DoesNotContain("META-INF/CERT.RSA", names);
        Assert.Contains("META-INF/services/org.example.Service", names);
        Assert.Contains("classes.dex", names);
        Assert.Contains("res/x.txt", names);
        Assert.Contains("assets/bundled_pack/manifest.json", names);
        Assert.Contains("assets/bundled_pack/pack.zip", names);
    }

    private static string MakeDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
