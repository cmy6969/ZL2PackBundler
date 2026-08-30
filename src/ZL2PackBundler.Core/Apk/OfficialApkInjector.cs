using System.IO.Compression;

namespace ZL2PackBundler.Core.Apk;

public sealed class InjectionResult
{
    /// <summary>追加的安装器 dex 条目名；已注入过时为 null。</summary>
    public string? DexEntryName { get; init; }
    public string? DexFilePath { get; init; }
    /// <summary>写入 assets/zl2packbundler/installer-config.json 的内容；已注入过时为 null。</summary>
    public string? InstallerConfigJson { get; init; }
    /// <summary>修补后的二进制 AndroidManifest.xml（由 ApkRebuilder 直接替换 zip 条目）；已注入过时为 null。</summary>
    public byte[]? ManifestOverride { get; init; }
    public AxmlPatcher.AxmlInfo? ManifestInfo { get; init; }
}

/// <summary>
/// 官方原版 APK 注入：把 com.zl2packbundler.installer.BundledPackInstaller 加为新 LAUNCHER 入口，
/// 首次启动时由它安装内嵌整合包，再把入口转交给原版启动器。
/// 不再经过 apktool（其 -r 回包会丢失资源文件导致原版启动器崩溃）：
/// 直接读取 APK 内的二进制 AndroidManifest.xml → AXML 修补 → 替换回 zip 条目，其余条目逐字节保留。
/// </summary>
public static class OfficialApkInjector
{
    public static InjectionResult Inject(
        string baseApk, string workDir, string sdkBuildToolsDir, Action<string>? log = null)
    {
        var manifestBytes = ApkRebuilder.ReadEntry(baseApk, "AndroidManifest.xml")
            ?? throw new InvalidDataException("基础 APK 中缺少 AndroidManifest.xml。");
        var packageName = AndroidBuildTools.GetPackageName(baseApk, sdkBuildToolsDir);

        if (AxmlPatcher.HasInstallerActivity(manifestBytes, packageName))
        {
            // 已注入过：直接复用（幂等）
            return new InjectionResult();
        }

        var info = AxmlPatcher.Analyze(manifestBytes, packageName);
        log?.Invoke($"检测到官方原版 APK：启动入口 {info.LauncherName}（{info.LauncherKind}），包名 {info.Package}");

        var patchedManifest = AxmlPatcher.ApplyPatch(manifestBytes, packageName);
        var next = OfficialApkDetector.NextDexEntryName(OfficialApkDetector.MaxDexIndex(baseApk));
        var dex = BundledTools.ExtractInstallerDex(workDir, log);
        var config = BundledTools.BuildInstallerConfigJson(info.LauncherName, info.ImportAlias ?? "");

        return new InjectionResult
        {
            DexEntryName = next,
            DexFilePath = dex,
            InstallerConfigJson = config,
            ManifestOverride = patchedManifest,
            ManifestInfo = info
        };
    }
}
