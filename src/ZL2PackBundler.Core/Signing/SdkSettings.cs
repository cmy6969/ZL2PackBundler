using System.Text.Json;

namespace ZL2PackBundler.Core.Signing;

/// <summary>
/// 本地工具配置（%APPDATA%/ZL2PackBundler/config.json）。
/// 用户指定过 SDK 目录后自动记住，下次运行无需再传 --sdk。
/// </summary>
public static class SdkSettings
{
    private const string ConfigDirEnv = "ZL2PB_CONFIG_DIR";
    private const string ConfigFileName = "config.json";

    private static string ConfigDir =>
        Environment.GetEnvironmentVariable(ConfigDirEnv) is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZL2PackBundler");

    private static string ConfigPath => Path.Combine(ConfigDir, ConfigFileName);

    public static string? Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            if (doc.RootElement.TryGetProperty("androidSdkDir", out var el)
                && el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }
        catch { /* 配置损坏时静默忽略 */ }
        return null;
    }

    public static void Save(string sdkDir)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(new { androidSdkDir = sdkDir });
            File.WriteAllText(ConfigPath, json);
        }
        catch { /* 配置写入失败不影响打包 */ }
    }
}
