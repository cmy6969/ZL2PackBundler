using Xunit;
using ZL2PackBundler.Core;

namespace ZL2PackBundler.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void GuardsWarnAbove2Gb()
    {
        var warnings = Guards.Check(3L * 1024 * 1024 * 1024, 3L * 1024 * 1024 * 1024);
        Assert.Contains(warnings, w => w.Level == "warning" && w.Message.Contains("2GB"));
    }

    [Fact]
    public void GuardsRejectAbove4Gb()
    {
        Assert.Throws<InvalidOperationException>(() => Guards.Check(5L * 1024 * 1024 * 1024, 5L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void GuardsPassUnderLimits()
    {
        Assert.Empty(Guards.Check(1024, 1024));
    }

    [Fact]
    public void SanitizeIdKeepsSafeChars()
    {
        Assert.Equal("my-pack-2026", PackPipeline.SanitizeId("My Pack 2026！"));
    }
}
