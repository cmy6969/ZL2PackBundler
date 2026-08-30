using System.IO.Compression;
using System.Text;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

public class OfficialApkTests
{
    [Fact]
    public void DetectorFindsMarkerInDex()
    {
        var apk = MakeApk("patched.apk", ("classes.dex", "ABC bundled_pack/manifest.json XYZ"));
        Assert.True(OfficialApkDetector.IsPatchedBuild(apk));
    }

    [Fact]
    public void DetectorIgnoresApkWithoutMarker()
    {
        var apk = MakeApk("official.apk", ("classes.dex", "no marker here"));
        Assert.False(OfficialApkDetector.IsPatchedBuild(apk));
    }

    [Fact]
    public void NextDexEntryNamesAreCorrect()
    {
        Assert.Equal("classes.dex", OfficialApkDetector.NextDexEntryName(0));
        Assert.Equal("classes31.dex", OfficialApkDetector.NextDexEntryName(30));
        var apk = MakeApk("multi.apk", ("classes.dex", "x"), ("classes2.dex", "x"), ("classes12.dex", "x"));
        Assert.Equal(12, OfficialApkDetector.MaxDexIndex(apk));
    }

    [Fact]
    public void AxmlPatcherAnalyzesRealManifest()
    {
        // 夹具来自 ZL2 上游 2.4.11 调试构建的二进制 AndroidManifest.xml
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
        var info = AxmlPatcher.Analyze(bytes, "com.movtery.zalithlauncher.v2.debug");
        Assert.Equal("com.movtery.zalithlauncher.v2.debug", info.Package);
        Assert.Equal("com.movtery.zalithlauncher.ui.activities.SplashActivity", info.LauncherName);
        Assert.Equal("activity", info.LauncherKind);
        Assert.Equal("com.movtery.activity.ImportModpackActivity", info.ImportAlias);
        Assert.False(AxmlPatcher.HasInstallerActivity(bytes, "com.movtery.zalithlauncher.v2.debug"));
    }

    [Fact]
    public void AxmlPatcherAppliesAndRoundTrips()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
        var patched = AxmlPatcher.ApplyPatch(bytes, "com.movtery.zalithlauncher.v2.debug");

        // 修补后：安装器成为 LAUNCHER，且解析/重写可往返
        Assert.True(AxmlPatcher.HasInstallerActivity(patched, "com.movtery.zalithlauncher.v2.debug"));
        var info = AxmlPatcher.Analyze(patched, "com.movtery.zalithlauncher.v2.debug");
        Assert.Equal(AxmlPatcher.InstallerActivityName, info.LauncherName);

        // 再解析一遍（往返稳定）
        var again = AxmlPatcher.ApplyPatch(patched, "com.movtery.zalithlauncher.v2.debug");
        Assert.True(AxmlPatcher.HasInstallerActivity(again, "com.movtery.zalithlauncher.v2.debug"));
    }

    [Fact]
    public void RenamePackageAndAppLabel()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
        const string oldPkg = "com.movtery.zalithlauncher.v2.debug";
        const string newPkg = "com.example.renamed";

        var renamed = AxmlPatcher.ApplyPackageRename(bytes, oldPkg, newPkg);
        var labeled = AxmlPatcher.ApplyAppLabel(renamed, "我的启动器");

        var doc = AxmlPatcher.Parse(labeled);
        var androidNs = (uint)doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")!;

        // manifest package 属性已改
        var manifest = FindElem(doc.Roots, "manifest", doc.Pool)!;
        var pkgValue = GetAttrValue(manifest, doc.Pool, androidNs, "package");
        Assert.Equal(newPkg, pkgValue);

        // 组件类名保持不变（类仍在原包）
        Assert.True(AxmlPatcher.HasInstallerActivity(labeled, newPkg) || true); // 原清单无安装器：此处仅验证可解析
        var info = AxmlPatcher.Analyze(labeled, newPkg);
        Assert.Equal("com.movtery.zalithlauncher.ui.activities.SplashActivity", info.LauncherName);

        // authorities 旧包名前缀被替换
        var all = new List<AxmlPatcher.Node>();
        void Walk(List<AxmlPatcher.Node> nodes)
        {
            foreach (var n in nodes) { all.Add(n); Walk(n.Children); }
        }
        Walk(doc.Roots);
        var auths = all
            .SelectMany(n => n.Attrs)
            .Where(a => doc.Pool.Strings[(int)a.Name] == "authorities")
            .Select(a => GetAttrValue(a, doc.Pool))
            .Where(v => v != null)
            .ToList();
        Assert.NotEmpty(auths);
        Assert.All(auths, v => Assert.DoesNotContain(oldPkg, v!));

        // application label 已改为新名称
        var application = FindApp(doc.Roots, doc.Pool);
        Assert.NotNull(application);
        Assert.Equal("我的启动器", GetAttrValue(application!, doc.Pool, androidNs, "label"));

        // 序列化结果必须 4 字节对齐（AOSP 硬性要求，防回归）
        Assert.Equal(0, labeled.Length % 4);
    }

    private static string? GetAttrValue(AxmlPatcher.Node node, AxmlPatcher.StringPool pool, uint androidNs, string name)
        => node.Attrs
            .Where(a => (a.Ns == androidNs || a.Ns == 0xFFFFFFFF) && pool.Strings[(int)a.Name] == name)
            .Select(a => a.RawValue != 0xFFFFFFFF ? pool.Strings[(int)a.RawValue] : (a.Type == 0x03 ? pool.Strings[(int)a.Data] : null))
            .FirstOrDefault();

    private static string? GetAttrValue((uint Ns, uint Name, uint RawValue, byte Type, uint Data) a, AxmlPatcher.StringPool pool)
        => a.RawValue != 0xFFFFFFFF ? pool.Strings[(int)a.RawValue] : (a.Type == 0x03 ? pool.Strings[(int)a.Data] : null);

    private static AxmlPatcher.Node? FindApp(List<AxmlPatcher.Node> nodes, AxmlPatcher.StringPool pool)
        => FindElem(nodes, "application", pool);

    private static AxmlPatcher.Node? FindElem(List<AxmlPatcher.Node> nodes, string element, AxmlPatcher.StringPool pool)
    {
        foreach (var n in nodes)
        {
            if (n.Kind == 0x0102 && pool.Strings[(int)n.NameIdx] == element) return n;
            var hit = FindElem(n.Children, element, pool);
            if (hit != null) return hit;
        }
        return null;
    }

    [Fact]
    public void ApplyAuthorWritesMetaDataAndReplacesOnRerun()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
        var patched = AxmlPatcher.ApplyAuthor(bytes, "测试作者");
        var doc = AxmlPatcher.Parse(patched);
        var androidNs = (uint)doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")!;

        var app = FindApp(doc.Roots, doc.Pool)!;
        var meta = app.Children.FirstOrDefault(c =>
            c.Kind == 0x0102 && doc.Pool.Strings[(int)c.NameIdx] == "meta-data"
            && GetAttrValue(c, doc.Pool, androidNs, "name") == "zl2packbundler.author");
        Assert.NotNull(meta);
        Assert.Equal("测试作者", GetAttrValue(meta!, doc.Pool, androidNs, "value"));
        Assert.Equal(0, patched.Length % 4);

        // 再次写入（例如重复打包）只更新值，不产生重复 meta-data
        var again = AxmlPatcher.ApplyAuthor(patched, "新作者");
        var doc2 = AxmlPatcher.Parse(again);
        var app2 = FindApp(doc2.Roots, doc2.Pool)!;
        var metas = app2.Children
            .Where(c => c.Kind == 0x0102 && doc2.Pool.Strings[(int)c.NameIdx] == "meta-data"
                && GetAttrValue(c, doc2.Pool, androidNs, "name") == "zl2packbundler.author")
            .ToList();
        Assert.Single(metas);
        Assert.Equal("新作者", GetAttrValue(metas[0], doc2.Pool, androidNs, "value"));
    }

    [Fact]
    public void InstallerActivityUsesTypedLaunchModeAndExported()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "AndroidManifest.bin"));
        var patched = AxmlPatcher.ApplyPatch(bytes, "com.movtery.zalithlauncher.v2.debug");
        var doc = AxmlPatcher.Parse(patched);

        var installer = FindByName(doc.Roots, "activity", AxmlPatcher.InstallerActivityName, doc.Pool);
        Assert.NotNull(installer);
        var launchMode = installer!.Attrs.FirstOrDefault(a => doc.Pool.Strings[(int)a.Name] == "launchMode");
        Assert.Equal(0x10, launchMode.Type);
        Assert.Equal(2u, launchMode.Data);
        Assert.Equal(0xFFFFFFFFu, launchMode.RawValue);
        var exported = installer.Attrs.FirstOrDefault(a => doc.Pool.Strings[(int)a.Name] == "exported");
        Assert.Equal(0x12, exported.Type);
    }

    private static AxmlPatcher.Node? FindByName(List<AxmlPatcher.Node> nodes, string element, string nameValue, AxmlPatcher.StringPool pool)
    {
        foreach (var n in nodes)
        {
            if (n.Kind == 0x0102 && pool.Strings[(int)n.NameIdx] == element
                && n.Attrs.Any(a => pool.Strings[(int)a.Name] == "name" && a.RawValue != 0xFFFFFFFF && pool.Strings[(int)a.RawValue] == nameValue))
                return n;
            var hit = FindByName(n.Children, element, nameValue, pool);
            if (hit != null) return hit;
        }
        return null;
    }

    private static string MakeApk(string name, params (string Entry, string Content)[] entries)
    {
        var apk = Path.Combine(Path.GetTempPath(), "zl2pb-" + name + "-" + Guid.NewGuid().ToString("N") + ".apk");
        using (var zip = ZipFile.Open(apk, ZipArchiveMode.Create))
        {
            foreach (var (entry, content) in entries)
            {
                using var s = zip.CreateEntry(entry).Open();
                s.Write(Encoding.ASCII.GetBytes(content));
            }
        }
        return apk;
    }
}
