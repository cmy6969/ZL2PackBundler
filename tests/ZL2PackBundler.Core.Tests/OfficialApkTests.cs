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
    public void ManifestParseAndPatchRoundTrip()
    {
        var xml = """
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.example.app">
    <application>
        <activity android:name=".SplashActivity" android:label="@string/app_name" android:icon="@mipmap/ic_launcher">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
        <activity-alias android:name=".ImportAlias" android:exported="true">
            <meta-data android:name="import_type" android:value="modpack" />
        </activity-alias>
    </application>
</manifest>
""";
        var info = ManifestPatcher.Parse(xml);
        Assert.Equal("com.example.app", info.Package);
        Assert.Equal("com.example.app.SplashActivity", info.LauncherName);
        Assert.Equal("activity", info.LauncherKind);
        Assert.Equal("com.example.app.ImportAlias", info.ImportAlias);

        var removed = ManifestPatcher.RemoveLauncherFilter(xml);
        Assert.DoesNotContain("android.intent.category.LAUNCHER", removed);

        var patched = ManifestPatcher.AddInstallerActivity(removed, info);
        Assert.Contains(ManifestPatcher.InstallerActivityName, patched);
        Assert.Contains("android:name=\u0022.SplashActivity\u0022", patched);
        Assert.Contains("android.intent.action.MAIN", patched); // 安装器承接 LAUNCHER
        Assert.True(ManifestPatcher.HasInstallerActivity(patched));
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
