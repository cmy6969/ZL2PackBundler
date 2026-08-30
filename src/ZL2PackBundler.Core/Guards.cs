namespace ZL2PackBundler.Core;

public sealed record GuardWarning(string Level, string Message);

public static class Guards
{
    public const long WarnPackBytes = 2L * 1024 * 1024 * 1024; // 2GB
    public const long MaxApkBytes = 4L * 1024 * 1024 * 1024;  // 4GB（zip32 上限）

    public static List<GuardWarning> Check(long packBytes, long finalApkBytes)
    {
        var warnings = new List<GuardWarning>();
        if (packBytes > WarnPackBytes)
            warnings.Add(new GuardWarning("warning",
                $"pack.zip 超过 2GB（约 {packBytes / (1024L * 1024 * 1024)}GB），部分设备安装/解包会非常慢，zip 32 位边界风险上升。"));
        if (finalApkBytes > MaxApkBytes)
            throw new InvalidOperationException(
                $"最终 APK 超过 4GB 上限（{finalApkBytes} 字节），zip 32 位格式无法承载，请精简整合包。");
        return warnings;
    }
}
