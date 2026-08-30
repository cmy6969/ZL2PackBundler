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
            if (analysis.Type == BundledPackType.Snapshot)
            {
                progress?.Invoke("打包游戏目录…");
                SnapshotPacker.Create(options.PackInput, packZip, progress);
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
                baseForEmbedding = injection.BaseApkPath;
                baseApkKind = BaseApkKind.OfficialInjected;
                if (injection.DexEntryName != null && injection.DexFilePath != null)
                    extraDex = new List<(string, string)> { (injection.DexEntryName, injection.DexFilePath) };
            }

            var rebuilt = Path.Combine(tempDir, "rebuilt.apk");
            progress?.Invoke("重建 APK（嵌入内嵌资产）…");
            ApkRebuilder.Rebuild(baseForEmbedding, rebuilt, manifest.ToJson(), packZip, extraDex, progress);

            var warnings = Guards.Check(packBytes, new FileInfo(rebuilt).Length);

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
