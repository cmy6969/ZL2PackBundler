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
