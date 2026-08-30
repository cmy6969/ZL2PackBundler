using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZL2PackBundler.App;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class MainViewModel : ObservableObject
{
    private string apkPath = "";
    public string ApkPath { get => apkPath; set => Set(ref apkPath, value); }

    private string baseApkStatus = "";
    public string BaseApkStatus { get => baseApkStatus; set => Set(ref baseApkStatus, value); }

    private string packPath = "";
    public string PackPath { get => packPath; set => Set(ref packPath, value); }

    private string outputPath = "";
    public string OutputPath { get => outputPath; set => Set(ref outputPath, value); }

    private string analysisText = "";
    public string AnalysisText { get => analysisText; set => Set(ref analysisText, value); }

    private string warningsText = "";
    public string WarningsText { get => warningsText; set => Set(ref warningsText, value); }

    private string logText = "";
    public string LogText { get => logText; set => Set(ref logText, value); }

    private string progressText = "";
    public string ProgressText { get => progressText; set => Set(ref progressText, value); }

    private double progressValue;
    public double ProgressValue { get => progressValue; set => Set(ref progressValue, value); }

    private bool useAutoKeyStore = true;
    public bool UseAutoKeyStore { get => useAutoKeyStore; set => Set(ref useAutoKeyStore, value); }

    private bool useOwnKeyStore;
    public bool UseOwnKeyStore { get => useOwnKeyStore; set => Set(ref useOwnKeyStore, value); }

    private string sdkPath = "";
    public string SdkPath { get => sdkPath; set => Set(ref sdkPath, value); }

    private string packageName = "";
    public string PackageName { get => packageName; set => Set(ref packageName, value); }

    private string appName = "";
    public string AppName { get => appName; set => Set(ref appName, value); }

    private string keyStorePath = "";
    public string KeyStorePath { get => keyStorePath; set => Set(ref keyStorePath, value); }

    private string keyStorePass = "";
    public string KeyStorePass { get => keyStorePass; set => Set(ref keyStorePass, value); }

    private string keyAlias = "";
    public string KeyAlias { get => keyAlias; set => Set(ref keyAlias, value); }

    private string keyPass = "";
    public string KeyPass { get => keyPass; set => Set(ref keyPass, value); }

    private string pageTitle = "";
    public string PageTitle { get => pageTitle; set => Set(ref pageTitle, value); }
}
