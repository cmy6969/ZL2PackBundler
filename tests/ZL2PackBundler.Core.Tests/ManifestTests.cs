using System.Text.Json;
using Xunit;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Tests;

public class ManifestTests
{
    private static BundledPackManifest Valid() => new()
    {
        PackId = "test-pack",
        Type = BundledPackType.Snapshot,
        Name = "Test",
        McVersion = "1.20.1",
        SizeBytes = 1024,
        Sha256 = new string('a', 64)
    };

    [Fact]
    public void ValidManifestPasses()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void JsonUsesCamelCaseAndRoundTrips()
    {
        var json = Valid().ToJson();
        Assert.Contains("\"packId\"", json);
        Assert.Contains("\"packVersion\"", json);
        Assert.Contains("\"mcVersion\"", json);
        Assert.Contains("\"sha256\"", json);
        var back = JsonSerializer.Deserialize<BundledPackManifest>(json, BundledPackManifest.JsonOptions)!;
        Assert.Equal("test-pack", back.PackId);
        Assert.Equal(BundledPackType.Snapshot, back.Type);
    }

    [Fact]
    public void MissingFieldsFail()
    {
        var m = new BundledPackManifest();
        var errors = m.Validate();
        Assert.Contains(errors, e => e.Contains("packId"));
        Assert.Contains(errors, e => e.Contains("sha256"));
    }

    [Fact]
    public void SnapshotRequiresMcVersion()
    {
        var m = Valid();
        m.McVersion = null;
        Assert.Contains(m.Validate(), e => e.Contains("mcVersion"));
    }

    [Fact]
    public void PackZipDoesNotRequireMcVersion()
    {
        var m = Valid();
        m.Type = BundledPackType.PackZip;
        m.McVersion = null;
        Assert.Empty(m.Validate());
    }

    [Fact]
    public void BadShaFails()
    {
        var m = Valid();
        m.Sha256 = "zzz";
        Assert.Contains(m.Validate(), e => e.Contains("sha256"));
    }

    [Fact]
    public void AuthorOptionalAndRoundTrips()
    {
        // 未设置时不写入（旧版设备端解析器可忽略未知字段，但保持产物精简）
        Assert.DoesNotContain("author", Valid().ToJson());

        var m = Valid();
        m.Author = "cmy6969";
        Assert.Empty(m.Validate());
        var json = m.ToJson();
        Assert.Contains("\"author\": \"cmy6969\"", json);
        var back = JsonSerializer.Deserialize<BundledPackManifest>(json, BundledPackManifest.JsonOptions)!;
        Assert.Equal("cmy6969", back.Author);
    }

    [Fact]
    public void TypeSerializesAsLowercaseContractValue()
    {
        Assert.Contains("\"type\": \"snapshot\"", Valid().ToJson());
        var packZip = Valid();
        packZip.Type = BundledPackType.PackZip;
        Assert.Contains("\"type\": \"packzip\"", packZip.ToJson());
    }
}
