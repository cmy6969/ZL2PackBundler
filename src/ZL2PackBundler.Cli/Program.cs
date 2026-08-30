using System.Text;
using ZL2PackBundler.Core;
using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Models;
using ZL2PackBundler.Core.Signing;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant();
try
{
    switch (command)
    {
        case "analyze": Analyze(ParseOptions(args.Skip(1))); break;
        case "pack": Pack(ParseOptions(args.Skip(1))); break;
        case "gen-keystore": GenKeyStore(ParseOptions(args.Skip(1))); break;
        case "help": case "--help": case "-h": PrintHelp(); break;
        default:
            Console.Error.WriteLine($"未知命令：{args[0]}");
            PrintHelp();
            return 2;
    }
}
catch (Exception e)
{
    Console.Error.WriteLine("错误：" + e.Message);
    return 1;
}
return 0;

static Dictionary<string, string?> ParseOptions(IEnumerable<string> args)
{
    var map = new Dictionary<string, string?>();
    var list = args.ToList();
    for (var i = 0; i < list.Count; i++)
    {
        var a = list[i];
        if (a.StartsWith("--", StringComparison.Ordinal))
        {
            var key = a[2..];
            string? value = null;
            if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                value = list[++i];
            map[key] = value;
        }
    }
    return map;
}

static string Require(Dictionary<string, string?> o, string key)
{
    if (o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
    throw new InvalidOperationException($"缺少参数 --{key}");
}

static void Analyze(Dictionary<string, string?> o)
{
    var result = PackPipeline.AnalyzeOnly(Require(o, "pack"));
    Console.WriteLine($"类型: {(result.Type == BundledPackType.Snapshot ? "snapshot（游戏目录快照，可完全离线）" : "packzip（整合包压缩包，首次导入可能联网）")}");
    Console.WriteLine($"格式: {result.Format}");
    Console.WriteLine($"MC 版本: {result.McVersion ?? "（导入后由 App 解析）"}");
    Console.WriteLine($"名称建议: {result.NameHint}");
    Console.WriteLine("离线完整性：");
    foreach (var item in result.OfflineReport)
        Console.WriteLine($"  [{(item.Present ? "OK" : "缺失")}] {item.Path} — {item.Label}");
}

static void Pack(Dictionary<string, string?> o)
{
    var options = new PackOptions
    {
        BaseApk = Require(o, "apk"),
        PackInput = Require(o, "pack"),
        OutputApk = Require(o, "out"),
        Name = o.GetValueOrDefault("name"),
        PackId = o.GetValueOrDefault("pack-id"),
        PackageName = o.GetValueOrDefault("package"),
        AppName = o.GetValueOrDefault("app-name"),
        Author = o.GetValueOrDefault("author"),
        IconPng = o.GetValueOrDefault("icon"),
        SdkDir = o.GetValueOrDefault("sdk"),
        Force = o.ContainsKey("force"),
        Signing = BuildSigning(o)
    };
    Console.WriteLine($"开始打包：{options.PackInput} -> {options.OutputApk}");
    var report = PackPipeline.Run(options, msg => Console.WriteLine("  " + msg));
    Console.WriteLine();
    Console.WriteLine("=== 打包完成 ===");
    Console.WriteLine($"基础 APK: {(report.BaseApkKind == BaseApkKind.OfficialInjected ? "官方原版（已自动注入安装器）" : "已含内嵌支持代码（直接嵌入）")}");
    Console.WriteLine($"类型: {report.Type} / 格式: {report.Format}");
    Console.WriteLine($"名称: {report.Name} / MC: {report.McVersion ?? "-"}");
    if (report.Author != null) Console.WriteLine($"作者: {report.Author}");
    if (report.IconSummary != null) Console.WriteLine($"图标: {report.IconSummary}");
    Console.WriteLine($"pack.zip: {report.PackZipBytes / (1024.0 * 1024.0):F1} MB");
    Console.WriteLine($"最终 APK: {report.FinalApkBytes / (1024.0 * 1024.0):F1} MB");
    foreach (var w in report.Warnings)
        Console.WriteLine($"[{w.Level}] {w.Message}");
    foreach (var item in report.OfflineReport)
        if (!item.Present)
            Console.WriteLine($"提示：{item.Path} 缺失，首次启动将联网补齐（{item.Label}）。");
    Console.WriteLine($"输出: {report.OutputPath}");
    Console.WriteLine();
    Console.WriteLine("合规提示：本工具默认不修改应用名称/图标（可选 --icon 替换桌面图标）。分发修改版 ZalithLauncher 须遵守 GPLv3 附加条款：");
    Console.WriteLine("  1) 在构建期用 gradle.properties 的 launcher_name 重命名，并在启动页/主界面标注非官方修改版；");
    Console.WriteLine("  2) 不得移除版权声明。详见仓库 README_ZH_CN.md。");
}

static SigningOptions BuildSigning(Dictionary<string, string?> o)
{
    var s = new SigningOptions
    {
        KeyStorePath = o.GetValueOrDefault("keystore"),
        KeyStorePassword = o.GetValueOrDefault("ks-pass"),
        KeyAlias = o.GetValueOrDefault("key-alias"),
        KeyPassword = o.GetValueOrDefault("key-pass"),
        AutoKeyStore = o.ContainsKey("auto-keystore")
    };
    if (s.KeyStorePath == null && !s.AutoKeyStore)
        throw new InvalidOperationException("需要 --keystore <path> 或 --auto-keystore。");
    if (s.KeyStorePath != null && (string.IsNullOrEmpty(s.KeyStorePassword) || string.IsNullOrEmpty(s.KeyAlias)))
        throw new InvalidOperationException("提供 --keystore 时必须同时提供 --ks-pass 与 --key-alias。");
    return s;
}

static void GenKeyStore(Dictionary<string, string?> o)
{
    var outPath = Require(o, "out");
    var alias = o.GetValueOrDefault("alias");
    var pass = o.GetValueOrDefault("pass");
    var path = KeyStoreGenerator.Create(outPath, alias, pass, Console.WriteLine);
    Console.WriteLine($"已生成 keystore: {path}（别名 {(alias ?? "zl2packbundler")}，密码 {(pass ?? ApkSigner.AutoKeyStorePassword)}，仅用于测试分发）");
}

static void PrintHelp()
{
    Console.WriteLine(
"""
ZL2PackBundler — 把整合包嵌入 ZL2 APK 的 Windows 工具

用法:
  zl2packbundler analyze --pack <文件夹|zip>
      识别整合包类型并输出离线完整性报告。

  zl2packbundler pack --apk <基础.apk> --pack <文件夹|zip> --out <输出.apk>
      [--name <名称>] [--pack-id <id>] [--package <新包名>] [--app-name <新应用名>]
      [--author <作者信息>]
      [--icon <图标.png|jpg|webp>]
      [--sdk <android-sdk目录>] [--force]
      [--keystore <path> --ks-pass <密码> --key-alias <别名> --key-pass <密码>]
      [--auto-keystore]
      嵌入整合包 -> zipalign -> apksigner(v2+v3) 重签名。
      可选修改包名/应用名（如 --package com.example.renamed --app-name "我的启动器"）。
      --author 会把作者信息写入 manifest.json 的 author 字段与 AndroidManifest 的
      meta-data zl2packbundler.author（安装页也会显示）。
      --icon 替换桌面图标：按各密度桶原始尺寸缩放替换 mipmap 位图（webp/png），
      并移除 anydpi 自适应图标 XML（桌面回退到替换后的位图图标）。

  zl2packbundler gen-keystore --out <ks.jks> [--alias <别名>] [--pass <密码>]
      生成一个测试用 keystore（正式分发请使用自有密钥）。

示例:
  zl2packbundler pack --apk zalith.apk --pack D:\mc\.minecraft --out out.apk --auto-keystore --author "cmy6969"

作者: cmy6969 · https://github.com/cmy6969/ZL2PackBundler · GPL-3.0
""");
}
