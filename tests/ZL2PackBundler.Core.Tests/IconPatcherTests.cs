using System.IO.Compression;
using System.Text;
using SkiaSharp;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

public class IconPatcherTests
{
    [Fact]
    public void ReplacesMipmapBitmapsAndRemovesAnyDpiXml()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 1) 合成 APK：真实二进制清单（含 icon/roundIcon 引用）+ 两个 webp 图标 + 两个 anydpi XML
            var manifest = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
            var arsc = ArscResolverTests.BuildArsc();
            var apkPath = Path.Combine(dir, "base.apk");
            using (var zip = ZipFile.Open(apkPath, ZipArchiveMode.Create))
            {
                Write(zip, "AndroidManifest.xml", manifest);
                Write(zip, "resources.arsc", arsc);
                Write(zip, "res/mipmap-xhdpi/ic_launcher.webp", Encode(SKColors.Blue, 96, 96, SKEncodedImageFormat.Webp));
                Write(zip, "res/mipmap-mdpi/ic_launcher_round.webp", Encode(SKColors.Green, 48, 48, SKEncodedImageFormat.Webp));
                Write(zip, "res/mipmap-anydpi/ic_launcher.xml", new byte[] { 1, 2, 3 });
                Write(zip, "res/mipmap-anydpi/ic_launcher_round.xml", new byte[] { 4, 5, 6 });
            }

            // 2) 用户图标：300x200 红色 PNG
            var userIcon = Path.Combine(dir, "icon.png");
            File.WriteAllBytes(userIcon, Encode(SKColors.Red, 300, 200, SKEncodedImageFormat.Png));

            // 3) 应用替换
            var entries = new List<string>();
            using (var zip = ZipFile.OpenRead(apkPath))
                foreach (var e in zip.Entries) entries.Add(e.FullName);
            var report = IconPatcher.Apply(manifest, arsc,
                name => ApkRebuilder.ReadEntry(apkPath, name), entries, userIcon, out var overrides);

            Assert.Equal(2, report.Replaced.Count);
            Assert.Contains("res/mipmap-xhdpi/ic_launcher.webp", report.Replaced);
            Assert.Contains("res/mipmap-mdpi/ic_launcher_round.webp", report.Replaced);
            Assert.Equal(2, report.Removed.Count);
            Assert.Contains("res/mipmap-anydpi/ic_launcher.xml", report.Removed);
            Assert.Contains("res/mipmap-anydpi/ic_launcher_round.xml", report.Removed);

            // 替换后：尺寸与原始一致、内容为红色、格式仍为 webp；anydpi XML 被删除（null）
            var webp = overrides["res/mipmap-xhdpi/ic_launcher.webp"]!;
            var (w, h) = IconImageOps.GetDimensions(webp);
            Assert.Equal((96, 96), (w, h));
            using (var bmp = SKBitmap.Decode(webp))
            {
                Assert.NotNull(bmp);
                var px = bmp!.GetPixel(0, 0);
                Assert.True(px.Red > 200 && px.Green < 60 && px.Blue < 60, $"应为红色，实际 {px}");
            }
            var round = overrides["res/mipmap-mdpi/ic_launcher_round.webp"]!;
            Assert.Equal((48, 48), IconImageOps.GetDimensions(round));
            Assert.Null(overrides["res/mipmap-anydpi/ic_launcher.xml"]);
            Assert.Null(overrides["res/mipmap-anydpi/ic_launcher_round.xml"]);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 尽力清理 */ }
        }
    }

    [Fact]
    public void RemovesAdaptiveIconXmlEvenWithShrunkPath()
    {
        // shrinkResources 会把自适应图标 XML 路径缩短为 res/BW.xml（不含 anydpi），
        // 必须按内容识别并移除，否则设备继续显示旧自适应图标（回归：图标修改未生效）。
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-icon3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifest = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
            var arsc = ArscResolverTests.BuildArsc(new[] { "res/BW.xml", "res/d2.webp" });
            var apkPath = Path.Combine(dir, "base.apk");
            using (var zip = ZipFile.Open(apkPath, ZipArchiveMode.Create))
            {
                Write(zip, "AndroidManifest.xml", manifest);
                Write(zip, "resources.arsc", arsc);
                Write(zip, "res/d2.webp", Encode(SKColors.Blue, 96, 96, SKEncodedImageFormat.Webp));
                Write(zip, "res/BW.xml", BuildAdaptiveIconXml());
            }
            var userIcon = Path.Combine(dir, "icon.png");
            File.WriteAllBytes(userIcon, Encode(SKColors.Red, 300, 200, SKEncodedImageFormat.Png));

            var entries = new List<string>();
            using (var zip = ZipFile.OpenRead(apkPath))
                foreach (var e in zip.Entries) entries.Add(e.FullName);
            var report = IconPatcher.Apply(manifest, arsc,
                name => ApkRebuilder.ReadEntry(apkPath, name), entries, userIcon, out var overrides);

            Assert.Contains("res/BW.xml", report.Removed);
            Assert.Contains("res/d2.webp", report.Replaced);
            Assert.Null(overrides["res/BW.xml"]);
            Assert.Equal((96, 96), IconImageOps.GetDimensions(overrides["res/d2.webp"]!));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 尽力清理 */ }
        }
    }

    /// <summary>最小二进制 AXML：根元素 adaptive-icon（无属性），供内容识别测试使用。</summary>
    private static byte[] BuildAdaptiveIconXml()
    {
        var poolData = new List<byte>();
        const string name = "adaptive-icon";
        poolData.AddRange(BitConverter.GetBytes((ushort)name.Length));
        poolData.AddRange(Encoding.Unicode.GetBytes(name));
        poolData.AddRange(BitConverter.GetBytes((ushort)0));
        while (poolData.Count % 4 != 0) poolData.Add(0);
        const int poolHeader = 28 + 4; // header + 1 offset
        var pool = new List<byte>();
        pool.AddRange(BitConverter.GetBytes((ushort)0x0001));
        pool.AddRange(BitConverter.GetBytes((ushort)28));
        pool.AddRange(BitConverter.GetBytes((uint)(poolHeader + poolData.Count)));
        pool.AddRange(BitConverter.GetBytes(1u));    // stringCount
        pool.AddRange(BitConverter.GetBytes(0u));    // styleCount
        pool.AddRange(BitConverter.GetBytes(0u));    // flags = UTF-16
        pool.AddRange(BitConverter.GetBytes((uint)poolHeader));
        pool.AddRange(BitConverter.GetBytes(0u));    // stylesStart
        pool.AddRange(BitConverter.GetBytes(0u));    // offset[0]
        pool.AddRange(poolData);

        // START_NAMESPACE（aapt2 编译的 res XML 会把元素包在命名空间节点里，测试递归识别）
        var startNs = new List<byte>();
        startNs.AddRange(BitConverter.GetBytes((ushort)0x0100));
        startNs.AddRange(BitConverter.GetBytes((ushort)16));
        startNs.AddRange(BitConverter.GetBytes((uint)24));
        startNs.AddRange(BitConverter.GetBytes(0u));
        startNs.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        startNs.AddRange(BitConverter.GetBytes(0xFFFFFFFFu)); // prefix
        startNs.AddRange(BitConverter.GetBytes(0xFFFFFFFFu)); // uri
        var endNs = new List<byte>(startNs);
        endNs[0] = 0x03; endNs[1] = 0x01; // END_NAMESPACE (0x0103 → 0x0101)

        var start = new List<byte>();
        start.AddRange(BitConverter.GetBytes((ushort)0x0102)); // START_ELEMENT
        start.AddRange(BitConverter.GetBytes((ushort)16));
        start.AddRange(BitConverter.GetBytes((uint)36));       // 16 + 20 + 0 属性
        start.AddRange(BitConverter.GetBytes(0u));
        start.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));    // comment
        start.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));    // ns
        start.AddRange(BitConverter.GetBytes(0u));             // nameIdx
        start.AddRange(BitConverter.GetBytes((ushort)20));
        start.AddRange(BitConverter.GetBytes((ushort)20));
        start.AddRange(BitConverter.GetBytes((ushort)0));      // attrCount
        start.AddRange(BitConverter.GetBytes((ushort)0));
        start.AddRange(BitConverter.GetBytes((ushort)0));
        start.AddRange(BitConverter.GetBytes((ushort)0));

        var end = new List<byte>();
        end.AddRange(BitConverter.GetBytes((ushort)0x0103));   // END_ELEMENT
        end.AddRange(BitConverter.GetBytes((ushort)16));
        end.AddRange(BitConverter.GetBytes((uint)24));
        end.AddRange(BitConverter.GetBytes(0u));
        end.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
        end.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));      // ns
        end.AddRange(BitConverter.GetBytes(0u));               // nameIdx

        var xml = new List<byte>();
        xml.AddRange(BitConverter.GetBytes((ushort)0x0003));   // RES_XML
        xml.AddRange(BitConverter.GetBytes((ushort)8));
        xml.AddRange(BitConverter.GetBytes((uint)(8 + pool.Count + startNs.Count + start.Count + end.Count + endNs.Count)));
        xml.AddRange(pool);
        xml.AddRange(startNs);
        xml.AddRange(start);
        xml.AddRange(end);
        xml.AddRange(endNs);
        return xml.ToArray();
    }

    [Fact]
    public void InvalidIconFileThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-icon2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bad = Path.Combine(dir, "bad.png");
            File.WriteAllBytes(bad, new byte[] { 0, 1, 2, 3 });
            Assert.ThrowsAny<Exception>(() => IconImageOps.Validate(File.ReadAllBytes(bad)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 尽力清理 */ }
        }
    }

    private static void Write(ZipArchive zip, string name, byte[] content)
    {
        using var s = zip.CreateEntry(name).Open();
        s.Write(content);
    }

    private static byte[] Encode(SKColor color, int w, int h, SKEncodedImageFormat format)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(color);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(format, 100);
        return data.ToArray();
    }
}
