namespace ZL2PackBundler.Core.Apk;

/// <summary>图标替换结果。</summary>
public sealed class IconPatchResult
{
    public List<string> Replaced { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> Skipped { get; } = new();
    public bool Changed => Replaced.Count > 0 || Removed.Count > 0;
}

/// <summary>
/// 应用图标替换（不重建 resources.arsc）：
/// 1. 从二进制清单取 application 的 android:icon / android:roundIcon 资源引用 ID；
/// 2. 用 resources.arsc 把 ID 解析为各 config 下的文件路径（如 res/mipmap-xxxhdpi/ic_launcher.webp）；
/// 3. 位图条目（webp/png）用用户图标按原始尺寸重编码替换；
/// 4. 自适应图标 XML（anydpi）直接移除 —— 设备回退到已替换的位图条目，从而显示新图标。
/// 返回 zip 条目覆盖表：值 = 新字节；null = 删除条目（交给 ApkRebuilder）。
/// </summary>
public static class IconPatcher
{
    public static IconPatchResult Apply(
        byte[] manifestBytes,
        byte[] arscBytes,
        Func<string, byte[]?> readEntry,
        IReadOnlyCollection<string> entryNames,
        string iconFilePath,
        out Dictionary<string, byte[]?> entryOverrides)
    {
        entryOverrides = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var result = new IconPatchResult();

        var userIcon = File.ReadAllBytes(iconFilePath);
        IconImageOps.Validate(userIcon);

        var doc = AxmlPatcher.Parse(manifestBytes);
        var androidNs = (uint)(doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")
            ?? throw new InvalidDataException("AXML 中缺少 android 命名空间。"));
        var application = AxmlPatcher.FindApplicationNode(doc, androidNs);

        var iconIds = new List<uint>();
        foreach (var attr in new[] { "icon", "roundIcon" })
            if (AxmlPatcher.GetAttrReference(application, doc.Pool, androidNs, attr) is { } id)
                iconIds.Add(id);
        if (iconIds.Count == 0)
        {
            result.Skipped.Add("AndroidManifest 中未找到 android:icon / android:roundIcon 资源引用");
            return result;
        }

        foreach (var id in iconIds.Distinct())
        {
            var files = ArscResolver.ResolveFilePaths(arscBytes, id);
            if (files.Count == 0)
            {
                result.Skipped.Add($"资源 0x{id:X8} 在 resources.arsc 中没有可替换的位图条目");
                continue;
            }
            foreach (var file in files)
            {
                var lower = file.ToLowerInvariant();
                if (lower.EndsWith(".xml", StringComparison.Ordinal)
                    && lower.Contains("anydpi", StringComparison.Ordinal))
                {
                    // 自适应图标 XML：移除，让所有版本回退到已替换的位图图标
                    if (entryNames.Contains(file))
                    {
                        entryOverrides[file] = null;
                        result.Removed.Add(file);
                    }
                    continue;
                }
                if (!lower.EndsWith(".webp", StringComparison.Ordinal)
                    && !lower.EndsWith(".png", StringComparison.Ordinal))
                {
                    result.Skipped.Add(file + "（非位图图标，跳过）");
                    continue;
                }
                var original = readEntry(file);
                if (original == null)
                {
                    result.Skipped.Add(file + "（APK 中缺少该条目）");
                    continue;
                }
                var (w, h) = IconImageOps.GetDimensions(original);
                if (w <= 0 || h <= 0)
                {
                    result.Skipped.Add(file + "（无法读取原始尺寸）");
                    continue;
                }
                entryOverrides[file] = IconImageOps.ResizeSquare(userIcon, w, h, lower.EndsWith(".webp", StringComparison.Ordinal));
                result.Replaced.Add(file);
            }
        }
        return result;
    }
}
