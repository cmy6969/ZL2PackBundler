using System.Security.Cryptography;
using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Apk;
using ZL2PackBundler.Core.Models;
using ZL2PackBundler.Core.Signing;

namespace ZL2PackBundler.Core;

public sealed class PackOptions
{
    public required string BaseApk { get; init; }
    public required string PackInput { get; init; }
    public required string OutputApk { get; init; }
    public string? Name { get; init; }
    public string? PackId { get; init; }
    /// <summary>可选：修改应用包名（如 com.example.renamed）。</summary>
    public string? PackageName { get; init; }
    /// <summary>可选：修改应用显示名称。</summary>
    public string? AppName { get; init; }
    public SigningOptions Signing { get; init; } = new();
    public string? SdkDir { get; init; }
    public bool Force { get; init; }
}

public static class PackPipeline
{
    /// <summary>只做输入识别（analyze 子命令 / GUI 分析页）。</summary>
    public static AnalysisResult AnalyzeOnly(string inputPath) => InputAnalyzer.Analyze(inputPath);

    public static PackReport Run(PackOptions options, Action<string>? progress = null)
    {
        if (string.Equals(Path.GetFullPath(options.BaseApk), Path.GetFullPath(options.OutputApk),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("输出路径不能与基础 APK 相同。");

        progress?.Invoke("分析整合包输入…");
        var analysis = InputAnalyzer.Analyze(options.PackInput);

        if (ApkRebuilder.ContainsBundledPack(options.BaseApk) && !options.Force)
            throw new InvalidOperationException("基础 APK 已包含内嵌整合包资产；如需覆盖请使用 --force。");

        var sdk = AndroidSdk.Locate(options.SdkDir);
        var name = options.Name ?? analysis.NameHint ?? "bundled-pack";
        var packId = options.PackId ?? SanitizeId(name) + "-" + DateTime.Now.ToString("yyyyMMdd");

        var tempDir = Path.Combine(Path.GetTempPath(), "zl2packbundler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var packZip = Path.Combine(tempDir, "pack.zip");
            var fixedJsons = 0;
            if (analysis.Type == BundledPackType.Snapshot)
            {
                progress?.Invoke("打包游戏目录…");
                fixedJsons = SnapshotPacker.Create(options.PackInput, packZip, progress);
            }
            else
            {
                progress?.Invoke("复制整合包压缩包…");
                File.Copy(options.PackInput, packZip);
            }

            var packBytes = new FileInfo(packZip).Length;
            var manifest = new BundledPackManifest
            {
                PackId = packId,
                PackVersion = 1,
                Type = analysis.Type,
                Name = name,
                McVersion = analysis.McVersion,
                SizeBytes = packBytes,
                Sha256 = ComputeSha256(packZip)
            };
            var errors = manifest.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("manifest 校验失败：" + string.Join("; ", errors));

            // 基础 APK 处理：官方原版 → 注入安装器；已打补丁 → 直接使用
            if (OfficialApkDetector.MaxDexIndex(options.BaseApk) == 0)
                throw new InvalidDataException("基础 APK 中没有 classes.dex，不是有效的 ZL2 APK。");

            string baseForEmbedding;
            BaseApkKind baseApkKind;
            List<(string EntryName, string FilePath)>? extraDex = null;
            List<(string EntryName, byte[] Content)>? extraAssets = null;
            byte[]? manifestOverride = null;
            if (OfficialApkDetector.IsPatchedBuild(options.BaseApk))
            {
                baseForEmbedding = options.BaseApk;
                baseApkKind = BaseApkKind.PatchedBuild;
                progress?.Invoke("基础 APK 已含内嵌支持代码，直接嵌入…");
            }
            else
            {
                progress?.Invoke("基础 APK 为官方原版，开始注入安装器…");
                var injection = OfficialApkInjector.Inject(
                    options.BaseApk, tempDir, sdk.BuildToolsDir, progress);
                baseForEmbedding = options.BaseApk;
                baseApkKind = BaseApkKind.OfficialInjected;
                manifestOverride = injection.ManifestOverride;
                if (injection.DexEntryName != null && injection.DexFilePath != null)
                    extraDex = new List<(string, string)> { (injection.DexEntryName, injection.DexFilePath) };
                if (injection.InstallerConfigJson != null)
                    extraAssets = new List<(string, byte[])>
                    {
                        (BundledTools.InstallerConfigZipEntry,
                         System.Text.Encoding.UTF8.GetBytes(injection.InstallerConfigJson))
                    };
            }

            // 可选：修改包名 / 应用显示名称（两条路径都生效）
            if (options.PackageName != null || options.AppName != null)
            {
                var pkgRegex = new System.Text.RegularExpressions.Regex(
                    "^[a-zA-Z][a-zA-Z0-9_]*(\\.[a-zA-Z][a-zA-Z0-9_]*)+$");
                var renameManifest = manifestOverride
                    ?? ApkRebuilder.ReadEntry(options.BaseApk, "AndroidManifest.xml")
                    ?? throw new InvalidDataException("基础 APK 中缺少 AndroidManifest.xml。");

                var originalPackage = AndroidBuildTools.GetPackageName(options.BaseApk, sdk.BuildToolsDir);

                if (options.PackageName != null)
                {
                    if (!pkgRegex.IsMatch(options.PackageName))
                        throw new InvalidOperationException($"非法包名：{options.PackageName}（示例：com.example.renamed）");
                    progress?.Invoke($"修改包名：{originalPackage} → {options.PackageName}");
                    renameManifest = AxmlPatcher.ApplyPackageRename(renameManifest, originalPackage, options.PackageName);
                }
                if (options.AppName != null)
                {
                    progress?.Invoke($"修改应用名称：{options.AppName}");
                    renameManifest = AxmlPatcher.ApplyAppLabel(renameManifest, options.AppName);
                }
                manifestOverride = renameManifest;
            }

            var rebuilt = Path.Combine(tempDir, "rebuilt.apk");
            progress?.Invoke("重建 APK（嵌入内嵌资产）…");
            ApkRebuilder.Rebuild(baseForEmbedding, rebuilt, manifest.ToJson(), packZip, extraDex, extraAssets, manifestOverride, progress);

            var warnings = Guards.Check(packBytes, new FileInfo(rebuilt).Length);
            if (fixedJsons > 0)
                warnings.Add(new GuardWarning("info",
                    $"已自动修复 {fixedJsons} 个版本 json 中重复的 libraries 条目（PCL2 等导出的整合包常见，重复库会导致启动游戏时报 Duplicate key）。"));
            if (options.PackageName != null)
                warnings.Add(new GuardWarning("info",
                    "已修改应用包名。注意：文件分享/导入导出类功能（FileProvider authority）可能与代码内常量不一致，如遇相关功能异常属正常；正式分发建议改源码构建期重命名。"));
            if (options.AppName != null)
                warnings.Add(new GuardWarning("info", "已修改应用显示名称。"));

            progress?.Invoke("zipalign + apksigner 签名…");
            ApkSigner.Run(sdk.BuildToolsDir, rebuilt, options.OutputApk, options.Signing, progress);
            var cert = ApkSigner.Verify(sdk.BuildToolsDir, options.OutputApk, progress);

            return new PackReport(
                analysis.Type, analysis.Format, baseApkKind, name, analysis.McVersion,
                packBytes, new FileInfo(options.OutputApk).Length, options.OutputApk,
                analysis.OfflineReport, warnings, cert);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 尽力清理 */ }
        }
    }

    public static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static string SanitizeId(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? char.ToLowerInvariant(c) : '-');
        return sb.ToString().Trim('-');
    }
}
