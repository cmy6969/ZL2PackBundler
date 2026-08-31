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
    public void RewritesAdaptiveIconXmlWithShrunkPathAndHostBitmap()
    {
        // 回归：shrinkResources 缩短路径（res/BW.xml）+ 按内容识别 + 重写（而非删除）自适应图标 XML。
        // 删除会导致按默认密度请求图标的启动器解析到缺失文件 → 安卓默认机器人图标。
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-icon3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manifest = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
            var arsc = ArscResolverTests.BuildArscWithHostDrawable(new[] { "res/BW.xml", "res/d2.webp" });
            var apkPath = Path.Combine(dir, "base.apk");
            using (var zip = ZipFile.Open(apkPath, ZipArchiveMode.Create))
            {
                Write(zip, "AndroidManifest.xml", manifest);
                Write(zip, "resources.arsc", arsc);
                Write(zip, "res/d2.webp", Encode(SKColors.Blue, 96, 96, SKEncodedImageFormat.Webp));
                Write(zip, "res/BW.xml", BuildAdaptiveIconXml());
                Write(zip, "res/img_launcher.png", Encode(SKColors.Green, 512, 512, SKEncodedImageFormat.Png));
            }
            var userIcon = Path.Combine(dir, "icon.png");
            File.WriteAllBytes(userIcon, Encode(SKColors.Red, 300, 200, SKEncodedImageFormat.Png));

            var entries = new List<string>();
            using (var zip = ZipFile.OpenRead(apkPath))
                foreach (var e in zip.Entries) entries.Add(e.FullName);
            var report = IconPatcher.Apply(manifest, arsc,
                name => ApkRebuilder.ReadEntry(apkPath, name), entries, userIcon, out var overrides);

            Assert.Empty(report.Removed);
            Assert.Contains("res/BW.xml", report.Rewritten);
            Assert.Contains("res/d2.webp", report.Replaced);
            Assert.Contains("res/img_launcher.png", report.Replaced);

            // 重写后的 XML：foreground → 宿主位图 0x7f020000；monochrome 移除；background 保持原引用
            var rewritten = overrides["res/BW.xml"]!;
            Assert.NotNull(rewritten);
            var doc = AxmlPatcher.Parse(rewritten);
            var androidNs = (uint)doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")!;
            AxmlPatcher.Node? adaptive = null;
            foreach (var root in doc.Roots)
            {
                adaptive = FindElem(root, "adaptive-icon", doc.Pool);
                if (adaptive != null) break;
            }
            Assert.NotNull(adaptive);
            var foreground = adaptive!.Children.FirstOrDefault(
                c => c.Kind == 0x0102 && doc.Pool.Strings[(int)c.NameIdx] == "foreground");
            Assert.NotNull(foreground);
            Assert.Equal(0x7f020000u, AxmlPatcher.GetAttrReference(foreground!, doc.Pool, androidNs, "drawable"));
            var background = adaptive.Children.FirstOrDefault(
                c => c.Kind == 0x0102 && doc.Pool.Strings[(int)c.NameIdx] == "background");
            Assert.NotNull(background);
            Assert.Equal(0x7f060001u, AxmlPatcher.GetAttrReference(background!, doc.Pool, androidNs, "drawable"));
            Assert.DoesNotContain(adaptive.Children,
                c => c.Kind == 0x0102 && doc.Pool.Strings[(int)c.NameIdx] == "monochrome");

            Assert.Equal((96, 96), IconImageOps.GetDimensions(overrides["res/d2.webp"]!));
            Assert.Equal((512, 512), IconImageOps.GetDimensions(overrides["res/img_launcher.png"]!));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 尽力清理 */ }
        }
    }

    private static AxmlPatcher.Node? FindElem(AxmlPatcher.Node node, string name, AxmlPatcher.StringPool pool)
    {
        if (node.Kind == 0x0102 && pool.Strings[(int)node.NameIdx] == name) return node;
        foreach (var child in node.Children)
        {
            var hit = FindElem(child, name, pool);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// 最小二进制 AXML：命名空间 + adaptive-icon（background/foreground/monochrome 子元素，
    /// 各带一个 drawable 引用属性），供内容识别与重写测试使用。
    /// </summary>
    private static byte[] BuildAdaptiveIconXml()
    {
        // 池：0 adaptive-icon, 1 background, 2 foreground, 3 monochrome, 4 android, 5 android URI, 6 drawable
        var strings = new[]
        {
            "adaptive-icon", "background", "foreground", "monochrome",
            "android", "http://schemas.android.com/apk/res/android", "drawable"
        };
        var poolData = new List<byte>();
        var offsets = new List<uint>();
        foreach (var s in strings)
        {
            offsets.Add((uint)poolData.Count);
            poolData.AddRange(BitConverter.GetBytes((ushort)s.Length));
            poolData.AddRange(Encoding.Unicode.GetBytes(s));
            poolData.AddRange(BitConverter.GetBytes((ushort)0));
        }
        while (poolData.Count % 4 != 0) poolData.Add(0);
        const int poolHeader = 28 + 4 * 7;
        var pool = new List<byte>();
        pool.AddRange(BitConverter.GetBytes((ushort)0x0001));
        pool.AddRange(BitConverter.GetBytes((ushort)28));
        pool.AddRange(BitConverter.GetBytes((uint)(poolHeader + poolData.Count)));
        pool.AddRange(BitConverter.GetBytes((uint)strings.Length));
        pool.AddRange(BitConverter.GetBytes(0u));
        pool.AddRange(BitConverter.GetBytes(0u)); // UTF-16
        pool.AddRange(BitConverter.GetBytes((uint)poolHeader));
        pool.AddRange(BitConverter.GetBytes(0u));
        foreach (var off in offsets) pool.AddRange(BitConverter.GetBytes(off));
        pool.AddRange(poolData);

        byte[] Ns(bool start)
        {
            var n = new List<byte>();
            n.AddRange(BitConverter.GetBytes((ushort)(start ? 0x0100 : 0x0101)));
            n.AddRange(BitConverter.GetBytes((ushort)16));
            n.AddRange(BitConverter.GetBytes((uint)24));
            n.AddRange(BitConverter.GetBytes(0u));
            n.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
            n.AddRange(BitConverter.GetBytes((uint)4)); // prefix = android
            n.AddRange(BitConverter.GetBytes((uint)5)); // uri
            return n.ToArray();
        }

        byte[] StartElement(uint nameIdx, uint? refId)
        {
            var hasAttr = refId != null;
            var el = new List<byte>();
            el.AddRange(BitConverter.GetBytes((ushort)0x0102));
            el.AddRange(BitConverter.GetBytes((ushort)16));
            el.AddRange(BitConverter.GetBytes((uint)(16 + 20 + (hasAttr ? 20 : 0))));
            el.AddRange(BitConverter.GetBytes(0u));
            el.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
            el.AddRange(BitConverter.GetBytes((uint)5));  // ns = android URI
            el.AddRange(BitConverter.GetBytes(nameIdx));
            el.AddRange(BitConverter.GetBytes((ushort)20));
            el.AddRange(BitConverter.GetBytes((ushort)20));
            el.AddRange(BitConverter.GetBytes((ushort)(hasAttr ? 1 : 0)));
            el.AddRange(BitConverter.GetBytes((ushort)0));
            el.AddRange(BitConverter.GetBytes((ushort)0));
            el.AddRange(BitConverter.GetBytes((ushort)0));
            if (hasAttr)
            {
                // attr: ns=5, name=6(drawable), raw=NoIndex, type=0x01, data=refId
                el.AddRange(BitConverter.GetBytes((uint)5));
                el.AddRange(BitConverter.GetBytes((uint)6));
                el.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
                el.AddRange(BitConverter.GetBytes((ushort)8));
                el.Add(0);
                el.Add(0x01);
                el.AddRange(BitConverter.GetBytes(refId!.Value));
            }
            return el.ToArray();
        }

        byte[] EndElement(uint nameIdx)
        {
            var el = new List<byte>();
            el.AddRange(BitConverter.GetBytes((ushort)0x0103));
            el.AddRange(BitConverter.GetBytes((ushort)16));
            el.AddRange(BitConverter.GetBytes((uint)24));
            el.AddRange(BitConverter.GetBytes(0u));
            el.AddRange(BitConverter.GetBytes(0xFFFFFFFFu));
            el.AddRange(BitConverter.GetBytes((uint)5));
            el.AddRange(BitConverter.GetBytes(nameIdx));
            return el.ToArray();
        }

        // NS + adaptive-icon + background + foreground + monochrome + END(adaptive-icon) + END_NS
        var body = new List<byte>();
        body.AddRange(Ns(true));
        body.AddRange(StartElement(0, null));
        body.AddRange(StartElement(1, 0x7f060001)); // background
        body.AddRange(EndElement(1));
        body.AddRange(StartElement(2, 0x7f080002)); // foreground
        body.AddRange(EndElement(2));
        body.AddRange(StartElement(3, 0x7f080003)); // monochrome
        body.AddRange(EndElement(3));
        body.AddRange(EndElement(0));
        body.AddRange(Ns(false));

        var xml = new List<byte>();
        xml.AddRange(BitConverter.GetBytes((ushort)0x0003));
        xml.AddRange(BitConverter.GetBytes((ushort)8));
        xml.AddRange(BitConverter.GetBytes((uint)(8 + pool.Count + body.Count)));
        xml.AddRange(pool);
        xml.AddRange(body);
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
