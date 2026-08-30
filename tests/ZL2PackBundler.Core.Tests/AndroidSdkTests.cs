using Xunit;
using ZL2PackBundler.Core.Signing;

namespace ZL2PackBundler.Core.Tests;

public class AndroidSdkTests
{
    [Fact]
    public void FakeSdkFoundByExplicitDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "zl2pb-sdk-" + Guid.NewGuid().ToString("N"));
        var bt = Path.Combine(root, "build-tools", "99.0.0");
        Directory.CreateDirectory(Path.Combine(bt, "lib"));
        File.WriteAllText(Path.Combine(bt, "zipalign.exe"), "fake");
        File.WriteAllText(Path.Combine(bt, "lib", "apksigner.jar"), "fake");

        var sdk = AndroidSdk.TryLocate(root);
        Assert.NotNull(sdk);
        Assert.EndsWith("99.0.0", sdk!.BuildToolsDir);
        Assert.Equal(root, sdk.SdkRoot);
    }

    [Fact]
    public void LocateThrowsWithGuidanceWhenNotFound()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "zl2pb-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bogus);

        Assert.Null(AndroidSdk.TryLocate(bogus)); // 显式目录不存在时不允许回退到其它根
        var ex = Assert.Throws<InvalidOperationException>(() => AndroidSdk.Locate(bogus));
        Assert.Contains("--sdk", ex.Message);
    }

    [Fact]
    public void SdkSettingsRoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Environment.SetEnvironmentVariable("ZL2PB_CONFIG_DIR", dir);
            SdkSettings.Save(@"X:\Android\Sdk");
            Assert.Equal(@"X:\Android\Sdk", SdkSettings.Load());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZL2PB_CONFIG_DIR", null);
        }
    }
}
