using System.Text;
using System.Text.RegularExpressions;

namespace ZL2PackBundler.Core.Apk;

public sealed class ManifestInfo
{
    public required string Package { get; init; }
    public required string LauncherKind { get; init; }   // "activity" | "activity-alias"
    public required string LauncherName { get; init; }   // 全限定类名
    public string? ImportAlias { get; init; }            // import_type=modpack 的组件全名
    public string LabelAttr { get; init; } = "";
    public string IconAttr { get; init; } = "";
}

/// <summary>对 apktool 解码后的 AndroidManifest.xml 做纯文本级修补（不重建资源）。</summary>
public static class ManifestPatcher
{
    public const string InstallerActivityName = "com.zl2packbundler.installer.BundledPackInstaller";

    public static ManifestInfo Parse(string xml, string? packageHint = null)
    {
        var package = Regex.Match(xml, "package=\u0022([^\u0022]+)\u0022").Groups[1].Value;
        if (string.IsNullOrEmpty(package)) package = packageHint ?? "";
        if (string.IsNullOrEmpty(package))
            throw new InvalidDataException("AndroidManifest.xml 缺少 package 属性（且无法从 badging 获取）。");

        var launcher = FindLauncherComponent(xml)
            ?? throw new InvalidDataException("AndroidManifest.xml 中未找到 MAIN/LAUNCHER 启动入口。");

        var importAlias = FindImportAlias(xml);

        return new ManifestInfo
        {
            Package = package,
            LauncherKind = launcher.Kind,
            LauncherName = ResolveName(package, launcher.Name),
            ImportAlias = importAlias == null ? null : ResolveName(package, importAlias),
            LabelAttr = launcher.Label,
            IconAttr = launcher.Icon
        };
    }

    /// <summary>移除原启动组件的 MAIN/LAUNCHER intent-filter（返回新 XML）。</summary>
    public static string RemoveLauncherFilter(string xml)
    {
        var block = FindIntentFilterBlock(xml);
        if (block == null) throw new InvalidDataException("未找到可移除的 LAUNCHER intent-filter。");
        return xml.Remove(block.Value.Start, block.Value.Length);
    }

    /// <summary>在 &lt;/application&gt; 前插入安装器 Activity（承接 LAUNCHER 入口）。</summary>
    public static string AddInstallerActivity(string xml, ManifestInfo info)
    {
        var label = info.LabelAttr.Length > 0 ? $" android:label=\u0022{info.LabelAttr}\u0022" : "";
        var icon = info.IconAttr.Length > 0 ? $" android:icon=\u0022{info.IconAttr}\u0022" : "";
        var block =
            "        <activity android:name=\u0022" + InstallerActivityName + "\u0022" +
            " android:exported=\u0022true\u0022 android:launchMode=\u0022singleTask\u0022" + label + icon + ">" + "\n" +
            "            <intent-filter>" + "\n" +
            "                <action android:name=\u0022android.intent.action.MAIN\u0022 />" + "\n" +
            "                <category android:name=\u0022android.intent.category.LAUNCHER\u0022 />" + "\n" +
            "            </intent-filter>" + "\n" +
            "        </activity>" + "\n";
        var idx = xml.LastIndexOf("</application>", StringComparison.Ordinal);
        if (idx < 0) throw new InvalidDataException("AndroidManifest.xml 缺少 </application>。");
        return xml.Insert(idx, block);
    }

    public static bool HasInstallerActivity(string xml) =>
        xml.Contains(InstallerActivityName, StringComparison.Ordinal);

    // ---------- 内部工具 ----------

    private sealed record Component(string Kind, string Name, string Label, string Icon);

    private static Component? FindLauncherComponent(string xml)
    {
        foreach (Match filter in Regex.Matches(xml, "<intent-filter[\\s>][\\s\\S]*?</intent-filter>"))
        {
            if (!filter.Value.Contains("android.intent.action.MAIN")
                || !filter.Value.Contains("android.intent.category.LAUNCHER")) continue;
            return ComponentAt(xml, filter.Index);
        }
        return null;
    }

    /// <summary>找 meta-data import_type=modpack 所属的 activity/activity-alias 的组件名。</summary>
    private static string? FindImportAlias(string xml)
    {
        foreach (Match meta in Regex.Matches(xml, "<meta-data[\\s>][^>]*/?>"))
        {
            if (!meta.Value.Contains("import_type") || !meta.Value.Contains("modpack")) continue;
            var component = ComponentAt(xml, meta.Index);
            return component?.Name;
        }
        return null;
    }

    private static Component? ComponentAt(string xml, int at)
    {
        var elementStart = LastIndexOfAny(xml, new[] { "<activity", "<activity-alias" }, at);
        if (elementStart < 0) return null;
        var openTag = xml.Substring(elementStart, xml.IndexOf('>', elementStart) - elementStart + 1);
        var kind = openTag.StartsWith("<activity-alias", StringComparison.Ordinal) ? "activity-alias" : "activity";
        var name = Attr(openTag, "android:name");
        if (string.IsNullOrEmpty(name)) return null;
        return new Component(kind, name, Attr(openTag, "android:label"), Attr(openTag, "android:icon"));
    }

    private static (int Start, int Length)? FindIntentFilterBlock(string xml)
    {
        foreach (Match filter in Regex.Matches(xml, "<intent-filter[\\s>][\\s\\S]*?</intent-filter>"))
        {
            if (filter.Value.Contains("android.intent.action.MAIN")
                && filter.Value.Contains("android.intent.category.LAUNCHER"))
                return (filter.Index, filter.Length);
        }
        return null;
    }

    private static int LastIndexOfAny(string xml, string[] needles, int before)
    {
        var best = -1;
        foreach (var n in needles)
        {
            var idx = xml.LastIndexOf(n, before, StringComparison.Ordinal);
            if (idx > best) best = idx;
        }
        return best;
    }

    private static string Attr(string openTag, string attrName)
    {
        var m = Regex.Match(openTag, attrName + "=\u0022([^\u0022]+)\u0022");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string ResolveName(string package, string name)
    {
        name = name.Trim();
        if (name.StartsWith('.')) return package + name;
        if (!name.Contains('.')) return package + "." + name;
        return name;
    }
}
