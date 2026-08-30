using System.Windows;
using Microsoft.Win32;
using ZL2PackBundler.Core;
using ZL2PackBundler.Core.Signing;

namespace ZL2PackBundler.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm = new();
    private int page;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = vm;
        SetPage(0);
        // 预填自动探测到的 SDK（找不到时留空，由用户手动选择）
        var detected = AndroidSdk.TryLocate();
        if (detected?.SdkRoot != null) vm.SdkPath = detected.SdkRoot;
    }

    private void SetPage(int index)
    {
        page = index;
        Page1.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page4.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        vm.PageTitle = index switch
        {
            0 => "① 选择基础 APK 与整合包",
            1 => "② 分析结果与离线完整性",
            2 => "③ 签名配置与输出路径",
            _ => "④ 打包进度与报告"
        };
        BackButton.IsEnabled = index > 0 && index < 3;
        NextButton.IsEnabled = index == 1;
        NextButton.Visibility = index <= 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnBrowseApk(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "APK 文件 (*.apk)|*.apk" };
        if (dlg.ShowDialog(this) != true) return;
        vm.ApkPath = dlg.FileName;
        vm.BaseApkStatus = "正在检测 APK 类型…";
        _ = Task.Run(() =>
        {
            try
            {
                var patched = Core.Apk.OfficialApkDetector.IsPatchedBuild(vm.ApkPath);
                vm.BaseApkStatus = patched
                    ? "检测结果：已含内嵌支持代码的构建 → 直接嵌入。"
                    : "检测结果：官方原版 APK → 将自动注入内置安装器（首次运行需联网下载 apktool）。";
            }
            catch (Exception ex)
            {
                vm.BaseApkStatus = "检测失败：" + ex.Message;
            }
        });
    }

    private void OnBrowsePackFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择 .minecraft 文件夹" };
        if (dlg.ShowDialog(this) == true) vm.PackPath = dlg.FolderName;
    }

    private void OnBrowsePackFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "整合包 (*.zip;*.mrpack)|*.zip;*.mrpack|所有文件 (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true) vm.PackPath = dlg.FileName;
    }

    private void OnBrowseSdk(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择 Android SDK 根目录（含 build-tools 的目录）" };
        if (dlg.ShowDialog(this) == true) vm.SdkPath = dlg.FolderName;
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "APK 文件 (*.apk)|*.apk", FileName = "out.apk" };
        if (dlg.ShowDialog(this) == true) vm.OutputPath = dlg.FileName;
    }

    private void OnAnalyze(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(vm.PackPath))
        {
            MessageBox.Show(this, "请先选择整合包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var result = PackPipeline.AnalyzeOnly(vm.PackPath);
            vm.AnalysisText =
                $"类型: {(result.Type == Core.Models.BundledPackType.Snapshot ? "snapshot（游戏目录快照，可完全离线）" : "packzip（整合包压缩包，首次导入可能联网）")}\n" +
                $"格式: {result.Format}\n" +
                $"MC 版本: {result.McVersion ?? "（导入后由 App 解析）"}\n" +
                $"名称建议: {result.NameHint}\n\n离线完整性：\n" +
                string.Join("\n", result.OfflineReport.Select(i => $"  [{(i.Present ? "OK" : "缺失")}] {i.Path} — {i.Label}"));
            vm.WarningsText = result.OfflineReport.Any(i => !i.Present)
                ? "注意：存在缺失项，首次启动仍需联网补齐上述内容。若要完全离线，请补全后再打包。"
                : "";
            SetPage(1);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "分析失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnStartPack(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(vm.ApkPath) || string.IsNullOrWhiteSpace(vm.PackPath)
            || string.IsNullOrWhiteSpace(vm.OutputPath))
        {
            MessageBox.Show(this, "请完整填写基础 APK、整合包与输出路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetPage(3);
        vm.LogText = "";
        vm.ProgressValue = 0;
        IProgress<string> progress = new Progress<string>(m =>
        {
            vm.ProgressText = m;
            vm.LogText = m + "\n" + vm.LogText;
            vm.ProgressValue = vm.ProgressValue < 90 ? vm.ProgressValue + 10 : 90;
        });
        try
        {
            var options = new PackOptions
            {
                BaseApk = vm.ApkPath,
                PackInput = vm.PackPath,
                OutputApk = vm.OutputPath,
                SdkDir = string.IsNullOrWhiteSpace(vm.SdkPath) ? null : vm.SdkPath,
                PackageName = string.IsNullOrWhiteSpace(vm.PackageName) ? null : vm.PackageName.Trim(),
                AppName = string.IsNullOrWhiteSpace(vm.AppName) ? null : vm.AppName.Trim(),
                Signing = new SigningOptions
                {
                    AutoKeyStore = vm.UseAutoKeyStore,
                    KeyStorePath = vm.UseOwnKeyStore ? vm.KeyStorePath : null,
                    KeyStorePassword = vm.UseOwnKeyStore ? KeyStorePassBox.Password : null,
                    KeyAlias = vm.UseOwnKeyStore ? vm.KeyAlias : null,
                    KeyPassword = vm.UseOwnKeyStore ? KeyPassBox.Password : null
                }
            };
            var report = await Task.Run(() => PackPipeline.Run(options, progress.Report));
            vm.ProgressValue = 100;
            vm.ProgressText = "完成";
            vm.LogText =
                $"=== 打包完成 ===\n类型: {report.Type} / 格式: {report.Format}\n" +
                $"名称: {report.Name} / MC: {report.McVersion ?? "-"}\n" +
                $"pack.zip: {report.PackZipBytes / (1024.0 * 1024.0):F1} MB\n" +
                $"最终 APK: {report.FinalApkBytes / (1024.0 * 1024.0):F1} MB\n" +
                string.Join("\n", report.Warnings.Select(w => $"[{w.Level}] {w.Message}")) + "\n" +
                "输出: " + report.OutputPath + "\n\n" +
                "合规提示：本工具不修改应用名称/图标。分发修改版 ZalithLauncher 须遵守 GPLv3 附加条款：\n" +
                "1) 构建期用 gradle.properties 的 launcher_name 重命名并在启动页标注非官方修改版；\n" +
                "2) 不得移除版权声明。详见仓库 README_ZH_CN.md。\n" + vm.LogText;
        }
        catch (Exception ex)
        {
            vm.ProgressText = "失败";
            vm.LogText = "错误：" + ex.Message + "\n\n" + vm.LogText;
            MessageBox.Show(this, ex.Message, "打包失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e) => SetPage(Math.Max(0, page - 1));
    private void OnNext(object sender, RoutedEventArgs e) => SetPage(Math.Min(2, page + 1));
}
