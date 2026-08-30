using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZL2PackBundler.Core.Models;

public enum BundledPackType { Snapshot, PackZip }

/// <summary>契约值固定为小写 "snapshot"/"packzip"（与 Android 端 validate() 一致）。</summary>
public sealed class BundledPackTypeConverter : JsonConverter<BundledPackType>
{
    public override BundledPackType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "snapshot" => BundledPackType.Snapshot,
            "packzip" => BundledPackType.PackZip,
            var other => throw new JsonException($"未知的 bundled pack type：{other}")
        };

    public override void Write(Utf8JsonWriter writer, BundledPackType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value == BundledPackType.Snapshot ? "snapshot" : "packzip");
}

/// <summary>跨端契约 assets/bundled_pack/manifest.json（schema=1）。</summary>
public sealed class BundledPackManifest
{
    public const int CurrentSchema = 1;
    public const string AssetDir = "bundled_pack";
    public const string ManifestAssetPath = "assets/bundled_pack/manifest.json";
    public const string PackZipAssetPath = "assets/bundled_pack/pack.zip";

    public int Schema { get; set; } = CurrentSchema;
    public string PackId { get; set; } = "";
    public long PackVersion { get; set; } = 1;
    [JsonConverter(typeof(BundledPackTypeConverter))]
    public BundledPackType Type { get; set; }
    public string Name { get; set; } = "";
    public string? McVersion { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>返回全部违规项；空列表=通过。与 Kotlin 端 validate() 规则保持一致。</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (Schema != CurrentSchema) errors.Add($"schema must be {CurrentSchema}, got {Schema}");
        if (string.IsNullOrWhiteSpace(PackId)) errors.Add("packId is required");
        if (PackVersion <= 0) errors.Add("packVersion must be > 0");
        if (Type != BundledPackType.Snapshot && Type != BundledPackType.PackZip)
            errors.Add("type must be snapshot|packzip");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("name is required");
        if (SizeBytes <= 0) errors.Add("sizeBytes must be > 0");
        if (Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            errors.Add("sha256 must be 64 lowercase hex chars");
        if (Type == BundledPackType.Snapshot && string.IsNullOrWhiteSpace(McVersion))
            errors.Add("snapshot requires mcVersion");
        return errors;
    }
}
