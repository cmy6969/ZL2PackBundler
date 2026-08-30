using System.IO.Compression;
using System.Text;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Apk;

public static class ApkRebuilder
{
    private static readonly HashSet<string> SignatureSuffixes = new(StringComparer.OrdinalIgnoreCase)
    { ".SF", ".RSA", ".DSA", ".EC" };

    /// <summary>读取 APK 内的单个 zip 条目字节；不存在返回 null。</summary>
    public static byte[]? ReadEntry(string apkPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        var entry = zip.GetEntry(entryName);
        if (entry == null) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static bool ContainsBundledPack(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        return zip.Entries.Any(e =>
            string.Equals(e.FullName, BundledPackManifest.ManifestAssetPath, StringComparison.OrdinalIgnoreCase)
         || string.Equals(e.FullName, BundledPackManifest.PackZipAssetPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 复制基础 APK 全部条目（剔除旧签名文件与旧内嵌资产），追加注入 dex（可选）、manifest 与 pack.zip。
    /// </summary>
    public static void Rebuild(string baseApk, string outputApk, string manifestJson, string packZipPath,
        IReadOnlyList<(string EntryName, string FilePath)>? extraDexEntries = null,
        IReadOnlyList<(string EntryName, byte[] Content)>? extraAssetEntries = null,
        byte[]? manifestOverride = null,
        Action<string>? progress = null)
    {
        using var source = ZipFile.OpenRead(baseApk);
        using var dest = new ZipArchive(
            new FileStream(outputApk, FileMode.Create, FileAccess.Write, FileShare.None),
            ZipArchiveMode.Create);

        var total = source.Entries.Count;
        long done = 0;
        foreach (var entry in source.Entries)
        {
            if (IsSignatureEntry(entry.FullName)
                || string.Equals(entry.FullName, BundledPackManifest.ManifestAssetPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FullName, BundledPackManifest.PackZipAssetPath, StringComparison.OrdinalIgnoreCase)
                || (manifestOverride != null && string.Equals(entry.FullName, "AndroidManifest.xml", StringComparison.OrdinalIgnoreCase)))
            {
                done++;
                continue;
            }

            var level = entry.CompressedLength == entry.Length
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            var newEntry = dest.CreateEntry(entry.FullName, level);
            using var src = entry.Open();
            using var dst = newEntry.Open();
            src.CopyTo(dst);
            done++;
            if (done % 500 == 0) progress?.Invoke($"复制 APK 条目 {done}/{total}");
        }

        if (manifestOverride != null)
        {
            progress?.Invoke("写入修补后的 AndroidManifest.xml");
            var manifestXmlEntry = dest.CreateEntry("AndroidManifest.xml", CompressionLevel.NoCompression);
            using var dst = manifestXmlEntry.Open();
            dst.Write(manifestOverride);
        }

        if (extraDexEntries != null)
        {
            foreach (var (entryName, filePath) in extraDexEntries)
            {
                progress?.Invoke("注入安装器 dex：" + entryName);
                var dexEntry = dest.CreateEntry(entryName, CompressionLevel.NoCompression);
                using (var fs = File.OpenRead(filePath))
                using (var dst = dexEntry.Open())
                    fs.CopyTo(dst);
            }
        }

        if (extraAssetEntries != null)
        {
            foreach (var (entryName, content) in extraAssetEntries)
            {
                progress?.Invoke("写入注入配置：" + entryName);
                var assetEntry = dest.CreateEntry(entryName, CompressionLevel.Optimal);
                using var dst = assetEntry.Open();
                dst.Write(content);
            }
        }

        progress?.Invoke("写入 assets/bundled_pack/manifest.json");
        var manifestEntry = dest.CreateEntry(BundledPackManifest.ManifestAssetPath, CompressionLevel.Optimal);
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson)))
        using (var dst = manifestEntry.Open())
            ms.CopyTo(dst);

        progress?.Invoke("嵌入 assets/bundled_pack/pack.zip（STORED）");
        var packEntry = dest.CreateEntry(BundledPackManifest.PackZipAssetPath, CompressionLevel.NoCompression);
        using (var fs = File.OpenRead(packZipPath))
        using (var dst = packEntry.Open())
            fs.CopyTo(dst);
    }

    private static bool IsSignatureEntry(string name)
    {
        if (!name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) return false;
        var fileName = name[(name.LastIndexOf('/') + 1)..];
        if (string.Equals(fileName, "MANIFEST.MF", StringComparison.OrdinalIgnoreCase)) return true;
        if (SignatureSuffixes.Contains(Path.GetExtension(fileName))) return true;
        if (fileName.StartsWith("CERT.", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
