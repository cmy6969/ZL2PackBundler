namespace ZL2PackBundler.Core.Apk;

public sealed class InjectionResult
{
    /// <summary>注入完成（并保留原签名）的 APK 路径；未注入时为原路径。</summary>
    public required string BaseApkPath { get; init; }
    /// <summary>追加的安装器 dex 条目名；未注入时为 null。</summary>
    public string? DexEntryName { get; init; }
    public string? DexFilePath { get; init; }
    /// <summary>写入 assets/zl2packbundler/installer-config.json 的内容；未注入时为 null。</summary>
    public string? InstallerConfigJson { get; init; }
    public required ManifestInfo ManifestInfo { get; init; }
}

/// <summary>
/// 官方原版 APK 注入：把 com.zl2packbundler.installer.BundledPackInstaller 加为新 LAUNCHER 入口，
/// 首次启动时由它安装内嵌整合包，再把入口转交给原版启动器。
/// </summary>
public static class OfficialApkInjector
{
    public static InjectionResult Inject(
        string baseApk, string workDir, string sdkBuildToolsDir, Action<string>? log = null)
    {
        var decodedDir = Path.Combine(workDir, "decoded");
        log?.Invoke("apktool 解码清单（官方原版 APK 注入模式）…");
        ApktoolRunner.Decode(baseApk, decodedDir, sdkBuildToolsDir, log);

        var manifestPath = Path.Combine(decodedDir, "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("apktool 未产出 AndroidManifest.xml。");

        var xml = File.ReadAllText(manifestPath);
        var packageName = AndroidBuildTools.GetPackageName(baseApk, sdkBuildToolsDir);
        if (ManifestPatcher.HasInstallerActivity(xml))
        {
            // 已注入过：直接复用（幂等）
            return new InjectionResult
            {
                BaseApkPath = baseApk,
                ManifestInfo = ManifestPatcher.Parse(xml, packageName)
            };
        }

        var info = ManifestPatcher.Parse(xml, packageName);
        log?.Invoke($"检测到官方原版 APK：启动入口 {info.LauncherName}（{info.LauncherKind}），包名 {info.Package}");

        xml = ManifestPatcher.RemoveLauncherFilter(xml);
        xml = ManifestPatcher.AddInstallerActivity(xml, info);
        File.WriteAllText(manifestPath, xml, new System.Text.UTF8Encoding(false));

        log?.Invoke("apktool 重新打包…");
        var patchedApk = Path.Combine(workDir, "patched.apk");
        ApktoolRunner.Build(decodedDir, patchedApk, sdkBuildToolsDir, log);

        var next = OfficialApkDetector.NextDexEntryName(OfficialApkDetector.MaxDexIndex(patchedApk));
        var dex = BundledTools.ExtractInstallerDex(workDir, log);
        var config = BundledTools.BuildInstallerConfigJson(info.LauncherName, info.ImportAlias ?? "");

        return new InjectionResult
        {
            BaseApkPath = patchedApk,
            DexEntryName = next,
            DexFilePath = dex,
            InstallerConfigJson = config,
            ManifestInfo = info
        };
    }
}
