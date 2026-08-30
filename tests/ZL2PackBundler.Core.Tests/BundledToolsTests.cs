using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

public class BundledToolsTests
{
    [Fact]
    public void ExtractZipHandlesBackslashDirectoryEntries()
    {
        // 模拟 PowerShell Compress-Archive：目录条目以反斜杠结尾
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-ext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                zip.CreateEntry("jre\\legal\\");
                var f = zip.CreateEntry("jre\\bin\\java.exe");
                using var s = f.Open();
                s.Write(Encoding.ASCII.GetBytes("fake-java"));
            }
            ms.Position = 0;
            BundledTools.ExtractZip(ms, dir);

            Assert.True(Directory.Exists(Path.Combine(dir, "jre", "legal")));
            Assert.True(File.Exists(Path.Combine(dir, "jre", "bin", "java.exe")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 尽力清理 */ }
        }
    }

    [Fact]
    public void ToolInfoJsonCarriesContractFields()
    {
        var json = BundledTools.BuildToolInfoJson(null);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ZL2PackBundler", root.GetProperty("tool").GetString());
        Assert.Equal(BundledTools.ToolVersion, root.GetProperty("version").GetString());
        Assert.NotNull(root.GetProperty("packedAt").GetString());
        Assert.Equal(BundledTools.ToolRepoUrl, root.GetProperty("repo").GetString());
        Assert.False(root.TryGetProperty("author", out _)); // 未提供作者时不写入

        var withAuthor = JsonDocument.Parse(BundledTools.BuildToolInfoJson("测试作者")).RootElement;
        Assert.Equal("测试作者", withAuthor.GetProperty("author").GetString());
    }

    [Fact]
    public void ToolVersionIsSemverThreeParts()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", BundledTools.ToolVersion);
    }
}
