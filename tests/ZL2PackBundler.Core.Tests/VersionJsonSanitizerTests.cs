using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

public class VersionJsonSanitizerTests
{
    [Fact]
    public void DedupesDuplicateLibraries()
    {
        var json = """
{
  "id": "1.21.1",
  "libraries": [
    { "name": "com.google.code.gson:gson:2.10.1" },
    { "name": "com.google.code.gson:gson:2.10.1" },
    { "name": "com.google.guava:guava:32.1.2-jre" },
    { "name": "com.google.code.gson:gson:2.10.1" }
  ]
}
""";
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.True(VersionJsonSanitizer.TrySanitize(bytes, out var sanitized));
        using var doc = JsonDocument.Parse(sanitized);
        var count = doc.RootElement.GetProperty("libraries").GetArrayLength();
        Assert.Equal(2, count);
    }

    [Fact]
    public void KeepsCleanJsonUnchanged()
    {
        var json = "{\n  \"id\": \"x\",\n  \"libraries\": [ { \"name\": \"a:b:1\" } ]\n}";
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.False(VersionJsonSanitizer.TrySanitize(bytes, out var sanitized));
        Assert.Equal(bytes, sanitized);
    }

    [Fact]
    public void IgnoresMalformedJson()
    {
        var bytes = Encoding.UTF8.GetBytes("{ not json");
        Assert.False(VersionJsonSanitizer.TrySanitize(bytes, out var sanitized));
        Assert.Equal(bytes, sanitized);
    }

    [Fact]
    public void SnapshotPackerDedupesVersionJsonInsideZip()
    {
        var root = Path.Combine(Path.GetTempPath(), "zl2pb-vj-" + Guid.NewGuid().ToString("N"));
        var vdir = Path.Combine(root, "versions", "dupver");
        Directory.CreateDirectory(vdir);
        var dup = """
{
  "id": "dupver",
  "libraries": [
    { "name": "a:b:1" },
    { "name": "a:b:1" }
  ]
}
""";
        File.WriteAllText(Path.Combine(vdir, "dupver.json"), dup);
        var zipPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");

        var fixedCount = SnapshotPacker.Create(root, zipPath);
        Assert.Equal(1, fixedCount);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("versions/dupver/dupver.json")!;
        using var reader = new StreamReader(entry.Open());
        using var doc = JsonDocument.Parse(reader.ReadToEnd());
        Assert.Equal(1, doc.RootElement.GetProperty("libraries").GetArrayLength());
    }
}
