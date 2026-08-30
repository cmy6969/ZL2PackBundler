using System.IO.Compression;
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
