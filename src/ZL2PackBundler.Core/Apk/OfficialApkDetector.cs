using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ZL2PackBundler.Core.Apk;

public static class OfficialApkDetector
{
    private const string Marker = "bundled_pack/manifest.json";

    /// <summary>
    /// 已打过源码补丁（或已被注入过安装器）的 APK，其 dex 中必然存在内嵌契约常量。
    /// 官方原版 APK 没有该常量。
    /// </summary>
    public static bool IsPatchedBuild(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        foreach (var entry in zip.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (!Regex.IsMatch(name, "^classes\\d*\\.dex$")) continue;
            using var s = entry.Open();
            var buf = new byte[1 << 20];
            var tail = "";
            int read;
            while ((read = s.Read(buf, 0, buf.Length)) > 0)
            {
                var text = tail + Encoding.ASCII.GetString(buf, 0, read);
                if (text.Contains(Marker, StringComparison.Ordinal)) return true;
                tail = text.Length > Marker.Length ? text[^Marker.Length..] : text;
            }
        }
        return false;
    }

    /// <summary>classes.dex 计为 1，classesN.dex 计为 N；返回最大 N（无 dex 时为 0）。</summary>
    public static int MaxDexIndex(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        var max = 0;
        foreach (var entry in zip.Entries)
        {
            var n = Path.GetFileName(entry.FullName);
            if (n == "classes.dex") { max = Math.Max(max, 1); continue; }
            var m = Regex.Match(n, "^classes(\\d+)\\.dex$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var i)) max = Math.Max(max, i);
        }
        return max;
    }

    public static string NextDexEntryName(int maxIndex) =>
        maxIndex == 0 ? "classes.dex" : $"classes{maxIndex + 1}.dex";
}
