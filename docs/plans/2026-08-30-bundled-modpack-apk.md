# 实施计划：整合包内嵌 APK 系统（ZL2PackBundler）

- Date: `2026-08-30`
- Spec: `docs/aegis/specs/2026-08-30-bundled-modpack-apk-design.md`
- Status: `待执行（spec 已由用户审阅通过）`
- Owner: 本会话（Windows 工具 + ZL2 应用端配套）

## Plan Header

- **Goal**：交付 `PackBundler`（.NET 8 Windows 工具，GUI+CLI）与 ZL2 应用端“内嵌整合包自动导入”，达到规格第 12 节验收标准：装机后首次启动自动解包/导入整合包，无二次下载，直接游玩。
- **Architecture**：工具端 Core 内核（分析→打包→APK 重建→签名）被 WPF/CLI 共用；跨端契约 `assets/bundled_pack/manifest.json + pack.zip`；应用端复用 `AbstractUnpackTask` splash 解包机制与 `ModpackImporter` 导入管线。
- **Tech Stack**：.NET 8（classlib net8.0 / console net8.0 / WPF net8.0-windows / xUnit）；Android SDK build-tools（zipalign/apksigner）、JDK keytool；Kotlin（应用侧，复用 Gson/协程/现有组件模式）。
- **Baseline/Authority Refs**：设计规格（上）、初始基线 `docs/aegis/baseline/2026-08-30-initial-baseline.md`、`README_ZH_CN.md`（GPL 附加条款）、`ZalithLauncher/src/main/java/com/movtery/zalithlauncher/components/`（UnpackSingleTask 先例）、`viewmodel/ModpackImportViewModel.kt`（导入管线）、`ui/activities/SplashActivity.kt`、`ui/activities/MainActivity.kt`。
- **Compatibility Boundary**（不可破坏）：
  1. 无内嵌包资产的 APK：启动路径逐字节不变（SplashActivity 只在 manifest 存在时追加任务）。
  2. 现有外部导入流程（ACTION_SEND/VIEW、EXTRA_IMPORT_URI/TYPE 语义）不变，仅新增可选 extras。
  3. 现有组件解包（JRE/LWJGL/JNA）逻辑不变。
  4. 契约 schema=1 冻结；读取端拒绝未知大版本并记日志。
- **TDD Route**：
  - Mode: off
  - Decision: skipped
  - Strict authority: not applicable
  - Strict signals: 新公共契约（manifest）属于合同类信号，但项目无 TDD 路线要求且无用户严格请求。
  - Light eligibility: 不适用（跨端契约，风险中高）。
  - TDD-fit exception: 无。
  - Test posture: post-change regression（每个任务完成后立即跑对应单测/集成验证）。
  - Reason: 无显式 TDD 请求；以“实现最小改动 + 完成后验证”为主。
  - Verification: `dotnet test`、`./gradlew :ZalithLauncher:testDebugUnitTest`、集成命令（见各任务）。
- **Verification**（总览）：
  - `cd PackBundler && dotnet build ZL2PackBundler.sln -c Release`
  - `cd PackBundler && dotnet test ZL2PackBundler.sln -c Release`
  - `cd ZalithLauncher2 && ./gradlew :ZalithLauncher:compileDebugKotlin :ZalithLauncher:testDebugUnitTest`

## Scope Check

**Plan Basis**：已批准设计规格（唯一权威）；用户 6 项决策已固化于规格第 3 节。

**Requirement Ready Check**：
- Requirement source refs: spec 第 3、12 节 + 用户会话确认记录。
- Goals/scope refs: spec 第 1、4 节。
- User/scenario refs: spec 第 4.1 节。
- Requirement item refs: spec 第 6–9 节（契约、工具、应用、离线模型）。
- Acceptance refs: spec 第 12 节（6 条可观测标准）。
- Open blocker questions: 无。
- Decision: **ready**。

**Files**（见各任务 Files 小节；汇总）：
- 新增 `PackBundler/`（sln + 3 工程 + 1 测试工程，约 20 个文件）。
- 新增/修改 `ZalithLauncher/`：`components/BundledModpackTask.kt`、`game/download/modpack/install/BundledModpackManifest.kt`、`ui/activities/SplashActivity.kt`（改）、`viewmodel/ModpackImportViewModel.kt`（改）、`ui/activities/MainActivity.kt`（改）、三个 strings.xml（改）、2 个测试文件（新增）。
- 文档：`README_ZH_CN.md`（改）、`docs/aegis/adr/…`（新增）、`docs/aegis/INDEX.md`（改）、根 `.gitignore`（改）。

**Compatibility**：见 Plan Header Compatibility Boundary；每条都有对应验证（Task A9 集成测试 + Task B3/B4 代码审查点）。

**Change Necessity**：
- User-visible need: 玩家免二次下载整合包；分发者需要 Windows 出包工具。
- No-change / non-code option: 不可行——无内嵌资产消费机制则 APK 内文件永远不被读取；无 Windows 工具则无法产出合规 APK。
- Why code change is necessary: 需要新工具工程 + 应用端消费代码。
- Minimum change boundary: `PackBundler/`（新工程）+ `ZalithLauncher` 应用模块内 5 个 Kotlin/XML 触点。
- Decision: **code-change**。

**Existence Check**：
- Proposed new surface: `PackBundler/`（.NET 工程，独立 sln）、`assets/bundled_pack` 契约、`BundledModpackTask`（App 内）。
- Existing owner / reuse candidate: 应用侧全部复用 `AbstractUnpackTask`/`ModpackImporter`/`VersionsManager`；工具侧仓库内无任何 Windows/桌面工程可复用。
- Why existing surface is insufficient: 仓库只有 Android/Gradle 工程，无法承载 WPF/控制台。
- Creation proof: 用户明确要求“Windows 端新项目”且已选择 .NET 8；契约是两端通信的唯一载体（规格第 6 节已批准）。
- Entropy / retirement impact: 工具为独立 sln，不进 Gradle；契约 schema 版本化，退役触发= schema 大版本升级并双端同步。
- Decision: **add-with-proof**（PackBundler/契约）| **reuse-existing**（应用侧）。

**Architecture Integrity Lens**：
- Invariant: 工具只写契约资产，App 只读契约资产；两端无代码依赖。
- Canonical owner / contract: `assets/bundled_pack/manifest.json`（schema=1）唯一契约，写入方 PackBundler.Core，消费方 BundledModpackManifest。
- Responsibility overlap: 无——packzip 导入唯一走 ModpackImporter，snapshot 解包唯一走 BundledModpackTask；不新增第二条导入路径。
- Higher-level simplification: 无更上层 owner（HMCL 管线不感知 APK 资产）。
- Retirement / falsifier: 契约失效信号=双端校验器不一致的测试失败。
- Verdict: **通过**。

**Plan Pressure Test**：
- Owner / contract / retirement: 契约单 owner + 版本化 + 双端校验测试 → 已覆盖。
- Architecture integrity / higher-level path: 复用现有 owner，无新框架 → 已覆盖。
- Verification scope: 每任务带精确命令；大 APK 真机验收为手动清单（无模拟器保证）。
- Task executability: 每任务 2–5 分钟步骤 + 完整代码。
- Pressure result: **proceed**。

**Plan-Time Complexity Check**：
- Artifact class: 中型新工具工程 + 应用内单文件小改。
- Target files / artifacts: PackBundler 约 20 文件；App 端 5 触点 + 2 测试。
- Current pressure: 低（新目录，不与现有 Gradle 结构耦合）。
- Projected post-change pressure: 中低；Core 类均 ≤250 行，App 新文件 ≤200 行。
- Budget result: **within-budget**。
- Recommendation: 按现有边界直接新建文件；SplashActivity 仅追加两处小改，不做重构。

## 任务总览

| ID | 任务 | 产出 | 验证 |
| --- | --- | --- | --- |
| A1 | 解决方案与工程骨架 | sln + 4 csproj + gitignore | `dotnet build` |
| A2 | 契约模型与序列化（Core.Manifest） | BundledPackManifest.cs + 测试 | `dotnet test --filter Manifest` |
| A3 | 输入识别与离线报告（Core.Analysis） | InputAnalyzer.cs + 测试 | `dotnet test --filter Analyzer` |
| A4 | 快照打包与 APK 重建（Core.Apk） | SnapshotPacker.cs / ApkRebuilder.cs + 测试 | `dotnet test --filter Packer/Rebuilder` |
| A5 | SDK 探测与签名（Core.Signing） | AndroidSdk/ApkSigner/KeyStoreGenerator | `dotnet test --filter Signing`（探测）+ 集成 |
| A6 | 流水线编排与护栏（Core.PackPipeline/Guards） | PackPipeline.cs + 测试 | `dotnet test --filter Pipeline/Guards` |
| A7 | CLI | Program.cs | `dotnet run --project … analyze/pack` |
| A8 | WPF GUI | MainWindow + MainViewModel | `dotnet build` + 手动冒烟 |
| A9 | 端到端集成验证 | 脚本 + 报告 | apksigner verify + unzip 检查 |
| B1 | App 契约解析 | BundledModpackManifest.kt | `gradlew testDebugUnitTest --tests *Manifest*` |
| B2 | App 解包任务 | BundledModpackTask.kt（含路径安全） | 单测 PathSafety |
| B3 | Splash 接线 + 字符串 | SplashActivity.kt + strings.xml | `compileDebugKotlin` |
| B4 | 导入管线 bundled 模式 | ModpackImportViewModel.kt / MainActivity.kt | `compileDebugKotlin` + 代码走查 |
| B5 | App 单元测试 | 2 个测试文件 | `gradlew testDebugUnitTest` |
| B6 | 真机验收清单 | 手动步骤 | 人工执行 |
| C1 | README 文档章节 | README_ZH_CN.md | 渲染检查 |
| C2 | ADR 补录 + 索引 | adr 文档 + INDEX | 存在性检查 |

每个 Task 完成后做一次 scoped commit（任务内 2–5 分钟步骤不单独提交）。

---

## Task A1：解决方案与工程骨架

**Files**：创建 `PackBundler/ZL2PackBundler.sln`、`PackBundler/src/ZL2PackBundler.Core/`、`PackBundler/src/ZL2PackBundler.Cli/`、`PackBundler/src/ZL2PackBundler.App/`、`PackBundler/tests/ZL2PackBundler.Core.Tests/`；修改根 `.gitignore`。
**Why**：工具的两个 UI 形态共用内核，测试先行建立骨架。
**Change Necessity**：code-change（新工程）。
**Impact/Compatibility**：不影响 Gradle 构建；`settings.gradle.kts` 不改。

**Steps**：

1. 脚手架（在仓库根执行）：

```bash
cd ZalithLauncher2
dotnet new sln -n ZL2PackBundler -o PackBundler
dotnet new classlib -n ZL2PackBundler.Core -o PackBundler/src/ZL2PackBundler.Core -f net8.0
dotnet new console  -n ZL2PackBundler.Cli  -o PackBundler/src/ZL2PackBundler.Cli  -f net8.0
dotnet new wpf      -n ZL2PackBundler.App  -o PackBundler/src/ZL2PackBundler.App  -f net8.0
dotnet new xunit    -n ZL2PackBundler.Core.Tests -o PackBundler/tests/ZL2PackBundler.Core.Tests -f net8.0
cd PackBundler
dotnet sln ZL2PackBundler.sln add src/ZL2PackBundler.Core src/ZL2PackBundler.Cli src/ZL2PackBundler.App tests/ZL2PackBundler.Core.Tests
dotnet add src/ZL2PackBundler.Cli  reference src/ZL2PackBundler.Core
dotnet add src/ZL2PackBundler.App  reference src/ZL2PackBundler.Core
dotnet add tests/ZL2PackBundler.Core.Tests reference src/ZL2PackBundler.Core
```

2. 将 `PackBundler/src/ZL2PackBundler.Cli/ZL2PackBundler.Cli.csproj` 改为（加 InvariantGlobalization，避免中文输出编码问题）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>zl2packbundler</AssemblyName>
    <RootNamespace>ZL2PackBundler.Cli</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\ZL2PackBundler.Core\ZL2PackBundler.Core.csproj" />
  </ItemGroup>
</Project>
```

3. 根 `.gitignore` 末尾追加：

```gitignore
# PackBundler (.NET)
PackBundler/**/bin/
PackBundler/**/obj/
```

4. **Verification**：`cd PackBundler && dotnet build ZL2PackBundler.sln -c Debug`，期望 `Build succeeded`（4 个工程）。
5. Commit：`git add PackBundler .gitignore && git commit -m "build(packbundler): .NET 8 解决方案骨架（Core/App/Cli/Tests）"`

---

## Task A2：契约模型与序列化（Core.Manifest）

**Files**：创建 `PackBundler/src/ZL2PackBundler.Core/Models/BundledPackManifest.cs`、`PackBundler/tests/ZL2PackBundler.Core.Tests/ManifestTests.cs`。
**Why**：跨端契约的写入端实现，与 spec 6.1 逐字段对齐。
**Change Necessity**：code-change（契约模型是工具核心产物）。
**Impact/Compatibility**：schema=1 冻结；字段名 camelCase 与 Kotlin 端 Gson 一致。

**BundledPackManifest.cs（完整）**：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZL2PackBundler.Core.Models;

public enum BundledPackType { Snapshot, PackZip }

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
    [JsonConverter(typeof(JsonStringEnumConverter))]
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
```

**ManifestTests.cs（完整）**：

```csharp
using System.Text.Json;
using Xunit;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Tests;

public class ManifestTests
{
    private static BundledPackManifest Valid() => new()
    {
        PackId = "test-pack",
        Type = BundledPackType.Snapshot,
        Name = "Test",
        McVersion = "1.20.1",
        SizeBytes = 1024,
        Sha256 = new string('a', 64)
    };

    [Fact]
    public void ValidManifestPasses()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void JsonUsesCamelCaseAndRoundTrips()
    {
        var json = Valid().ToJson();
        Assert.Contains("\"packId\"", json);
        Assert.Contains("\"packVersion\"", json);
        Assert.Contains("\"mcVersion\"", json);
        Assert.Contains("\"sha256\"", json);
        var back = JsonSerializer.Deserialize<BundledPackManifest>(json, BundledPackManifest.JsonOptions)!;
        Assert.Equal("test-pack", back.PackId);
        Assert.Equal(BundledPackType.Snapshot, back.Type);
    }

    [Fact]
    public void MissingFieldsFail()
    {
        var m = new BundledPackManifest();
        var errors = m.Validate();
        Assert.Contains(errors, e => e.Contains("packId"));
        Assert.Contains(errors, e => e.Contains("sha256"));
    }

    [Fact]
    public void SnapshotRequiresMcVersion()
    {
        var m = Valid();
        m.McVersion = null;
        Assert.Contains(m.Validate(), e => e.Contains("mcVersion"));
    }

    [Fact]
    public void PackZipDoesNotRequireMcVersion()
    {
        var m = Valid();
        m.Type = BundledPackType.PackZip;
        m.McVersion = null;
        Assert.Empty(m.Validate());
    }

    [Fact]
    public void BadShaFails()
    {
        var m = Valid();
        m.Sha256 = "zzz";
        Assert.Contains(m.Validate(), e => e.Contains("sha256"));
    }
}
```

**Verification**：`cd PackBundler && dotnet test ZL2PackBundler.sln --filter FullyQualifiedName~ManifestTests`（6 通过）。
**Commit**：`git commit -m "feat(packbundler): bundled_pack 契约模型与校验（schema=1）"`

---

## Task A3：输入识别与离线报告（Core.Analysis）

**Files**：创建 `PackBundler/src/ZL2PackBundler.Core/Analysis/InputAnalyzer.cs`、`PackBundler/tests/ZL2PackBundler.Core.Tests/AnalyzerTests.cs`。
**Why**：spec 7.3 步骤 2/4：目录→snapshot；四种导出格式→packzip；裸快照 zip→snapshot；离线完整性报告。
**Change Necessity**：code-change。
**Impact/Compatibility**：识别规则按 spec 7.3 优先级；MCBBS 标记文件为 `mcbbs.packmeta`（与 ZL2 端 MCBBSPackMetaParser 一致）。

**InputAnalyzer.cs（完整）**：

```csharp
using System.IO.Compression;
using System.Text.Json;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Analysis;

public enum PackFormat { RawSnapshot, Mcbbs, Modrinth, CurseForge, MultiMC }

public sealed record OfflineItem(string Path, bool Present, string Label);

public sealed class AnalysisResult
{
    public required BundledPackType Type { get; init; }
    public required PackFormat Format { get; init; }
    public string? NameHint { get; init; }
    public string? McVersion { get; init; }
    public required List<OfflineItem> OfflineReport { get; init; }
}

public static class InputAnalyzer
{
    public static AnalysisResult Analyze(string inputPath)
    {
        if (Directory.Exists(inputPath)) return AnalyzeDirectory(inputPath);
        if (File.Exists(inputPath)) return AnalyzeArchive(inputPath);
        throw new FileNotFoundException("整合包输入不存在", inputPath);
    }

    private static AnalysisResult AnalyzeDirectory(string dir)
    {
        var versionsDir = Path.Combine(dir, "versions");
        if (!Directory.Exists(versionsDir))
            throw new InvalidDataException("不是 .minecraft 目录：缺少 versions/。");

        var found = FindVersion(versionsDir);
        if (found.Version == null || !found.HasJson)
            throw new InvalidDataException("versions/ 下没有有效版本（需要 <名称>/<名称>.json）。");

        return new AnalysisResult
        {
            Type = BundledPackType.Snapshot,
            Format = PackFormat.RawSnapshot,
            NameHint = Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            McVersion = found.Version,
            OfflineReport = BuildOfflineReport(dir, found.Version, found.HasJar)
        };
    }

    private static (string? Version, bool HasJson, bool HasJar) FindVersion(string versionsDir)
    {
        foreach (var sub in Directory.EnumerateDirectories(versionsDir))
        {
            var name = Path.GetFileName(sub);
            var json = Path.Combine(sub, name + ".json");
            if (!File.Exists(json)) continue;
            var id = ReadVersionId(json);
            if (id == null) continue;
            return (id, true, File.Exists(Path.Combine(sub, name + ".jar")));
        }
        return (null, false, false);
    }

    private static string? ReadVersionId(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch { return null; }
    }

    private static List<OfflineItem> BuildOfflineReport(string dir, string mcVersion, bool hasJar)
    {
        bool FileOk(string rel) => File.Exists(Path.Combine(dir, rel));
        bool DirOk(string rel) => Directory.Exists(Path.Combine(dir, rel));
        return new List<OfflineItem>
        {
            new($"versions/{mcVersion}/{mcVersion}.json", FileOk($"versions/{mcVersion}/{mcVersion}.json"), "版本清单"),
            new($"versions/{mcVersion}/{mcVersion}.jar", hasJar, "版本主 Jar"),
            new("libraries/", DirOk("libraries/"), "原版库文件"),
            new($"assets/indexes/{mcVersion}.json", FileOk($"assets/indexes/{mcVersion}.json"), "资源索引"),
            new("assets/objects/", DirOk("assets/objects/"), "资源对象")
        };
    }

    private static AnalysisResult AnalyzeArchive(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        bool Has(string n) => names.Any(x =>
            string.Equals(x.TrimEnd('/'), n, StringComparison.OrdinalIgnoreCase));

        if (Has("modrinth.index.json")) return PackZip(PackFormat.Modrinth, zip, zipPath);
        if (Has("mmc-pack.json") || Has("instance.cfg")) return PackZip(PackFormat.MultiMC, zip, zipPath);
        if (Has("mcbbs.packmeta")) return PackZip(PackFormat.Mcbbs, zip, zipPath);
        if (Has("manifest.json") && CurseForgeLike(zip)) return PackZip(PackFormat.CurseForge, zip, zipPath);

        var versionJson = names
            .Where(n => n.StartsWith("versions/", StringComparison.OrdinalIgnoreCase)
                     && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(n => n.TrimEnd('/').Split('/').Skip(1).FirstOrDefault())
            .FirstOrDefault(x => !string.IsNullOrEmpty(x));
        var looksLikeMinecraft = versionJson != null &&
            (names.Any(n => n.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase))
          || names.Any(n => n.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
          || names.Any(n => n.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)));
        if (looksLikeMinecraft)
        {
            return new AnalysisResult
            {
                Type = BundledPackType.Snapshot,
                Format = PackFormat.RawSnapshot,
                NameHint = Path.GetFileNameWithoutExtension(zipPath),
                McVersion = versionJson,
                OfflineReport = new List<OfflineItem>
                {
                    new("libraries/", names.Any(n => n.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase)), "原版库文件"),
                    new("assets/", names.Any(n => n.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)), "资源目录")
                }
            };
        }

        throw new InvalidDataException(
            "无法识别整合包格式。支持：MCBBS(mcbbs.packmeta)、Modrinth(.mrpack)、CurseForge、MultiMC、或裸 .minecraft 快照 zip。");
    }

    private static bool CurseForgeLike(ZipArchive zip)
    {
        var entry = zip.GetEntry("manifest.json");
        if (entry == null) return false;
        try
        {
            using var reader = new StreamReader(entry.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            return doc.RootElement.TryGetProperty("minecraft", out var mc) && mc.ValueKind == JsonValueKind.Object;
        }
        catch { return false; }
    }

    private static AnalysisResult PackZip(PackFormat format, ZipArchive zip, string zipPath)
    {
        var name = format switch
        {
            PackFormat.Modrinth => ReadName(zip, "modrinth.index.json"),
            PackFormat.CurseForge => ReadName(zip, "manifest.json"),
            _ => null
        } ?? Path.GetFileNameWithoutExtension(zipPath);
        return new AnalysisResult
        {
            Type = BundledPackType.PackZip,
            Format = format,
            NameHint = name,
            McVersion = null,
            OfflineReport = new List<OfflineItem>
            {
                new("mods/ 及依赖", false, "整合包依赖模组（首次导入时可能联网下载）")
            }
        };
    }

    private static string? ReadName(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry == null) return null;
        try
        {
            using var reader = new StreamReader(entry.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            return doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch { return null; }
    }
}
```

**AnalyzerTests.cs（完整）**：

```csharp
using System.IO.Compression;
using Xunit;
using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Tests;

public class AnalyzerTests
{
    [Fact]
    public void DirectoryWithVersionIsSnapshot()
    {
        var root = MakeDir("mc");
        Directory.CreateDirectory(Path.Combine(root, "versions", "1.20.1"));
        File.WriteAllText(Path.Combine(root, "versions", "1.20.1", "1.20.1.json"), "{\"id\":\"1.20.1\"}");
        File.WriteAllText(Path.Combine(root, "versions", "1.20.1", "1.20.1.jar"), "jar");
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        var result = InputAnalyzer.Analyze(root);
        Assert.Equal(BundledPackType.Snapshot, result.Type);
        Assert.Equal(PackFormat.RawSnapshot, result.Format);
        Assert.Equal("1.20.1", result.McVersion);
        Assert.Contains(result.OfflineReport, o => o.Path == "libraries/" && !o.Present);
    }

    [Fact]
    public void DirectoryWithoutVersionsThrows()
    {
        var root = MakeDir("bad");
        Assert.Throws<InvalidDataException>(() => InputAnalyzer.Analyze(root));
    }

    [Fact]
    public void ModrinthZipIsPackZip()
    {
        var zipPath = MakeZip("mr.zip", ("modrinth.index.json", "{\"name\":\"MyPack\",\"formatVersion\":1}"));
        var result = InputAnalyzer.Analyze(zipPath);
        Assert.Equal(BundledPackType.PackZip, result.Type);
        Assert.Equal(PackFormat.Modrinth, result.Format);
        Assert.Equal("MyPack", result.NameHint);
    }

    [Fact]
    public void CurseForgeZipIsPackZip()
    {
        var zipPath = MakeZip("cf.zip", ("manifest.json", "{\"minecraft\":{\"version\":\"1.20.1\"},\"name\":\"CFPack\"}"));
        var result = InputAnalyzer.Analyze(zipPath);
        Assert.Equal(PackFormat.CurseForge, result.Format);
        Assert.Equal("CFPack", result.NameHint);
    }

    [Fact]
    public void McbbsZipIsPackZip()
    {
        var zipPath = MakeZip("mc.zip", ("mcbbs.packmeta", "{}"));
        Assert.Equal(PackFormat.Mcbbs, InputAnalyzer.Analyze(zipPath).Format);
    }

    [Fact]
    public void MultiMcZipIsPackZip()
    {
        var zipPath = MakeZip("mmc.zip", ("mmc-pack.json", "{}"));
        Assert.Equal(PackFormat.MultiMC, InputAnalyzer.Analyze(zipPath).Format);
    }

    [Fact]
    public void RawSnapshotZipIsSnapshot()
    {
        var zipPath = MakeZip("raw.zip",
            ("versions/1.20.1/1.20.1.json", "{\"id\":\"1.20.1\"}"),
            ("versions/1.20.1/1.20.1.jar", "jar"),
            ("mods/mod.jar", "mod"));
        var result = InputAnalyzer.Analyze(zipPath);
        Assert.Equal(BundledPackType.Snapshot, result.Type);
        Assert.Equal("1.20.1", result.McVersion);
    }

    [Fact]
    public void GarbageZipThrows()
    {
        var zipPath = MakeZip("garbage.zip", ("readme.txt", "hello"));
        Assert.Throws<InvalidDataException>(() => InputAnalyzer.Analyze(zipPath));
    }

    private static string MakeDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeZip(string name, params (string, string)[] entries)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), "zl2pb-" + name + "-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = zip.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return zipPath;
    }
}
```

**Verification**：`cd PackBundler && dotnet test ZL2PackBundler.sln --filter FullyQualifiedName~AnalyzerTests`（8 通过）。
**Commit**：`git commit -m "feat(packbundler): 整合包输入识别与离线完整性报告"`

---

## Task A4：快照打包与 APK 重建（Core.Apk）

**Files**：创建 `PackBundler/src/ZL2PackBundler.Core/Apk/SnapshotPacker.cs`、`PackBundler/src/ZL2PackBundler.Core/Apk/ApkRebuilder.cs`、`PackBundler/tests/ZL2PackBundler.Core.Tests/PackerTests.cs`。
**Why**：spec 7.3 步骤 3/6：排除运行时产物、STORED/DEFLATE 策略、剔除旧签名、嵌入资产。
**Change Necessity**：code-change。
**Impact/Compatibility**：不修改基础 APK 任何现有条目内容（除签名文件剔除）；v2 签名块在 zip 之外，读取时天然被忽略。

**SnapshotPacker.cs（完整）**：

```csharp
using System.IO.Compression;

namespace ZL2PackBundler.Core.Apk;

public static class SnapshotPacker
{
    private static readonly HashSet<string> StoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jar", ".zip", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ogg", ".mp3", ".m4a", ".flac",
        ".pack", ".litematic", ".schematic", ".nbs", ".gz", ".xz", ".7z", ".rar"
    };

    private static readonly HashSet<string> ExcludedRootNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bundled_pack_version", "launcher_profiles.json", "usercache.json", "usernamecache.json",
        ".ds_store", "thumbs.db"
    };

    private static readonly HashSet<string> ExcludedRootDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "logs", "crash-reports", "crash-reports-modded"
    };

    public static void Create(string sourceDir, string outputZip, Action<string>? progress = null)
    {
        var outputFull = Path.GetFullPath(outputZip);
        var files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFullPath(f), outputFull, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path: f, Relative: Path.GetRelativePath(sourceDir, f).Replace('\', '/')))
            .Where(x => ShouldInclude(x.Relative))
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var zip = ZipFile.Open(outputZip, ZipArchiveMode.Create);
        var done = 0;
        foreach (var (path, relative) in files)
        {
            var level = StoredExtensions.Contains(Path.GetExtension(path))
                     || relative.StartsWith("assets/objects/", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            var entry = zip.CreateEntry(relative, level);
            using var src = File.OpenRead(path);
            using var dst = entry.Open();
            src.CopyTo(dst);
            done++;
            if (done % 200 == 0) progress?.Invoke($"已打包 {done}/{files.Count} 个文件");
        }
        progress?.Invoke($"打包完成：{files.Count} 个文件");
    }

    private static bool ShouldInclude(string relative)
    {
        var parts = relative.Split('/');
        if (parts.Length >= 2 && ExcludedRootDirs.Contains(parts[0])) return false;
        if (parts.Length == 1)
        {
            var f = parts[0];
            if (ExcludedRootNames.Contains(f)) return false;
            if (f.StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase)
                && f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
```

**ApkRebuilder.cs（完整）**：

```csharp
using System.IO.Compression;
using System.Text;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core.Apk;

public static class ApkRebuilder
{
    private static readonly HashSet<string> SignatureSuffixes = new(StringComparer.OrdinalIgnoreCase)
    { ".SF", ".RSA", ".DSA", ".EC" };

    public static bool ContainsBundledPack(string apkPath)
    {
        using var zip = ZipFile.OpenRead(apkPath);
        return zip.Entries.Any(e =>
            string.Equals(e.FullName, BundledPackManifest.ManifestAssetPath, StringComparison.OrdinalIgnoreCase)
         || string.Equals(e.FullName, BundledPackManifest.PackZipAssetPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>复制基础 APK 全部条目（剔除旧签名文件与旧内嵌资产），追加 manifest 与 pack.zip。</summary>
    public static void Rebuild(string baseApk, string outputApk, string manifestJson, string packZipPath,
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
                || string.Equals(entry.FullName, BundledPackManifest.PackZipAssetPath, StringComparison.OrdinalIgnoreCase))
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
```

**PackerTests.cs（完整）**：

```csharp
using System.IO.Compression;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

public class PackerTests
{
    [Fact]
    public void SnapshotPackerExcludesRuntimeArtifacts()
    {
        var root = MakeDir();
        File.WriteAllText(Path.Combine(root, "versions", "x.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        File.WriteAllText(Path.Combine(root, "logs", "latest.log"), "log");
        File.WriteAllText(Path.Combine(root, "usercache.json"), "private");
        File.WriteAllText(Path.Combine(root, "options.txt"), "keep");

        var zipPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        SnapshotPacker.Create(root, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("versions/x.json", names);
        Assert.Contains("options.txt", names);
        Assert.DoesNotContain(names, n => n.StartsWith("logs/"));
        Assert.DoesNotContain("usercache.json", names);
    }

    [Fact]
    public void JarEntriesAreStored()
    {
        var root = MakeDir();
        var bytes = new byte[4096];
        new Random(42).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(root, "mods", "mod.jar"), bytes);

        var zipPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        SnapshotPacker.Create(root, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("mods/mod.jar")!;
        Assert.Equal(entry.Length, entry.CompressedLength); // STORED
    }

    [Fact]
    public void RebuildStripsSignaturesAndEmbedsAssets()
    {
        var apkPath = Path.Combine(Path.GetTempPath(), "base-" + Guid.NewGuid().ToString("N") + ".apk");
        using (var zip = ZipFile.Open(apkPath, ZipArchiveMode.Create))
        {
            using (var s1 = zip.CreateEntry("META-INF/MANIFEST.MF").Open()) s1.Write(new byte[] { 1 });
            using (var s2 = zip.CreateEntry("META-INF/CERT.RSA").Open()) s2.Write(new byte[] { 2 });
            using (var s3 = zip.CreateEntry("META-INF/services/org.example.Service").Open()) s3.Write(new byte[] { 3 });
            using (var s4 = zip.CreateEntry("classes.dex").Open()) s4.Write(new byte[] { 4, 5, 6 });
            using (var s5 = zip.CreateEntry("res/x.txt").Open()) s5.Write(new byte[] { 7 });
        }
        var packPath = Path.Combine(Path.GetTempPath(), "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(packPath, new byte[] { 9, 9, 9 });
        var outPath = Path.Combine(Path.GetTempPath(), "out-" + Guid.NewGuid().ToString("N") + ".apk");

        ApkRebuilder.Rebuild(apkPath, outPath, "{\"schema\":1}", packPath);

        using var zip = ZipFile.OpenRead(outPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.DoesNotContain("META-INF/MANIFEST.MF", names);
        Assert.DoesNotContain("META-INF/CERT.RSA", names);
        Assert.Contains("META-INF/services/org.example.Service", names);
        Assert.Contains("classes.dex", names);
        Assert.Contains("res/x.txt", names);
        Assert.Contains("assets/bundled_pack/manifest.json", names);
        Assert.Contains("assets/bundled_pack/pack.zip", names);
    }

    private static string MakeDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zl2pb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
```

**Verification**：`cd PackBundler && dotnet test ZL2PackBundler.sln --filter "FullyQualifiedName~PackerTests"`（3 通过）。
**Commit**：`git commit -m "feat(packbundler): 快照打包与 APK 重建（剔除签名/嵌入资产）"`

---

## Task A5：SDK 探测与签名（Core.Signing）

**Files**：创建 `PackBundler/src/ZL2PackBundler.Core/Signing/AndroidSdk.cs`、`ApkSigner.cs`、`KeyStoreGenerator.cs`。
**Why**：spec 7.3 步骤 7、7.5：zipalign -p 4 → apksigner v2+v3；SDK 自动探测；keystore 可生成。
**Change Necessity**：code-change。
**Impact/Compatibility**：签名后原 APK 证书被替换（预期行为）；失败时不产出半成品（先写临时文件，成功才落到输出路径——zipalign 输出为临时名，apksigner `--out` 直接写最终路径）。

**AndroidSdk.cs（完整）**：

```csharp
namespace ZL2PackBundler.Core.Signing;

public sealed class AndroidSdk
{
    public required string BuildToolsDir { get; init; }
    public string Zipalign => Path.Combine(BuildToolsDir, "zipalign.exe");
    public string ApkSignerJar => Path.Combine(BuildToolsDir, "lib", "apksigner.jar");

    public static AndroidSdk Locate(string? explicitDir = null)
    {
        var roots = new List<string>();
        if (!string.IsNullOrEmpty(explicitDir)) roots.Add(explicitDir);
        var home = Environment.GetEnvironmentVariable("ANDROID_HOME")
                ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(home)) roots.Add(home);
        roots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"));

        foreach (var root in roots)
        {
            var btRoot = Path.Combine(root, "build-tools");
            if (!Directory.Exists(btRoot)) continue;
            var best = Directory.EnumerateDirectories(btRoot)
                .Where(d => File.Exists(Path.Combine(d, "zipalign.exe"))
                         && File.Exists(Path.Combine(d, "lib", "apksigner.jar")))
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (best != null) return new AndroidSdk { BuildToolsDir = best };
        }
        throw new InvalidOperationException(
            "未找到 Android SDK build-tools（zipalign/apksigner）。请用 Android Studio SDK Manager 安装，或使用 --sdk 指定。");
    }
}
```

**ApkSigner.cs（完整）**：

```csharp
using System.Diagnostics;

namespace ZL2PackBundler.Core.Signing;

public sealed class SigningOptions
{
    public string? KeyStorePath { get; set; }
    public string? KeyStorePassword { get; set; }
    public string? KeyAlias { get; set; }
    public string? KeyPassword { get; set; }
    public bool AutoKeyStore { get; set; }
    public string? AutoKeyStoreDir { get; set; }
}

public static class ApkSigner
{
    public const string AutoKeyStorePassword = "zl2packbundler"; // 默认密钥：仅用于测试分发

    public static string Run(string sdkBuildToolsDir, string apkPath, string outputPath,
        SigningOptions options, Action<string>? log = null)
    {
        var aligned = outputPath + ".aligned.tmp";
        RunTool(Path.Combine(sdkBuildToolsDir, "zipalign.exe"),
            new[] { "-p", "-f", "4", apkPath, aligned }, log);

        var ks = options.KeyStorePath;
        if (ks == null && options.AutoKeyStore)
            ks = KeyStoreGenerator.Create(
                Path.Combine(options.AutoKeyStoreDir ?? Directory.GetCurrentDirectory(), "zl2packbundler.keystore"), log);
        if (ks == null)
            throw new InvalidOperationException("未配置 keystore（使用 --keystore 或 --auto-keystore）。");

        var java = LocateJava();
        var args = new List<string>
        {
            "-jar", Path.Combine(sdkBuildToolsDir, "lib", "apksigner.jar"), "sign",
            "--ks", ks,
            "--ks-pass", "pass:" + (options.KeyStorePassword ?? AutoKeyStorePassword),
            "--v1-signing-enabled", "false",
            "--v2-signing-enabled", "true",
            "--v3-signing-enabled", "true",
            "--min-sdk-version", "26", // ZL2 最低 API 26；伪 APK/异常清单时作为签名方案判定的回退值
            "--out", outputPath
        };
        if (!string.IsNullOrEmpty(options.KeyAlias)) { args.Add("--ks-key-alias"); args.Add(options.KeyAlias); }
        if (!string.IsNullOrEmpty(options.KeyPassword)) { args.Add("--key-pass"); args.Add("pass:" + options.KeyPassword); }
        args.Add(aligned);

        RunTool(java, args, log);
        try { File.Delete(aligned); } catch { /* 临时文件清理尽力而为 */ }
        return ks;
    }

    public static string Verify(string sdkBuildToolsDir, string apkPath, Action<string>? log = null)
    {
        return RunToolCaptured(LocateJava(),
            new[] { "-jar", Path.Combine(sdkBuildToolsDir, "lib", "apksigner.jar"),
                    "verify", "--verbose", "--print-certs", "--min-sdk-version", "26", apkPath }, log);
    }

    public static string LocateJava()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(p)) return p;
        }
        return "java";
    }

    private static void RunTool(string fileName, IEnumerable<string> args, Action<string>? log)
        => RunToolCaptured(fileName, args, log);

    private static string RunToolCaptured(string fileName, IEnumerable<string> args, Action<string>? log)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动 {fileName}");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        log?.Invoke(stdout);
        if (!string.IsNullOrWhiteSpace(stderr)) log?.Invoke(stderr);
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} 退出码 {p.ExitCode}：\n{stdout}\n{stderr}");
        return stdout + stderr;
    }
}
```

**KeyStoreGenerator.cs（完整）**：

```csharp
using System.Diagnostics;

namespace ZL2PackBundler.Core.Signing;

public static class KeyStoreGenerator
{
    public static string Create(string outputPath, Action<string>? log = null)
    {
        if (File.Exists(outputPath)) return outputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var psi = new ProcessStartInfo(LocateKeytool())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[]
        {
            "-genkeypair", "-keystore", outputPath, "-alias", "zl2packbundler",
            "-keyalg", "RSA", "-keysize", "2048", "-validity", "10950",
            "-storepass", ApkSigner.AutoKeyStorePassword,
            "-keypass", ApkSigner.AutoKeyStorePassword,
            "-dname", "CN=ZL2PackBundler, OU=PackBundler, O=ZL2PackBundler, L=Unknown, ST=Unknown, C=CN"
        }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        log?.Invoke(stdout + stderr);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"keytool 失败：{stderr}");
        return outputPath;
    }

    private static string LocateKeytool()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Path.Combine(javaHome, "bin", "keytool.exe");
            if (File.Exists(p)) return p;
        }
        return "keytool";
    }
}
```

**Verification**：
- `cd PackBundler && dotnet build ZL2PackBundler.sln -c Debug`
- 本机探测：`dotnet run --project src/ZL2PackBundler.Cli -- analyze --pack <任意目录>`（SDK 探测会在 analyze 不触发，用 pack 命令触发；此处仅确认编译）。
- 手动冒烟（可选）：生成 keystore 后 `keytool -list` 能列出 `zl2packbundler` 别名。
**Commit**：`git commit -m "feat(packbundler): Android SDK 探测与 zipalign/apksigner 签名"`

---

## Task A6：流水线编排与护栏（Core）

**Files**：创建 `PackBundler/src/ZL2PackBundler.Core/PackPipeline.cs`、`Guards.cs`、`PackReport.cs`（并入 PackPipeline.cs 亦可，本计划拆三个文件：`PackPipeline.cs`、`Guards.cs`、`PackReport.cs`）；测试 `PackBundler/tests/ZL2PackBundler.Core.Tests/PipelineTests.cs`。
**Why**：spec 7.3 全流程编排 + 7.4 体积护栏（>2GB 警告、>4GB 拒绝）。
**Change Necessity**：code-change。
**Impact/Compatibility**：输出路径与基础 APK 相同、基础 APK 已含内嵌包且未 `--force` 时均报错。

**Guards.cs（完整）**：

```csharp
namespace ZL2PackBundler.Core;

public sealed record GuardWarning(string Level, string Message);

public static class Guards
{
    public const long WarnPackBytes = 2L * 1024 * 1024 * 1024; // 2GB
    public const long MaxApkBytes = 4L * 1024 * 1024 * 1024;  // 4GB（zip32 上限）

    public static List<GuardWarning> Check(long packBytes, long finalApkBytes)
    {
        var warnings = new List<GuardWarning>();
        if (packBytes > WarnPackBytes)
            warnings.Add(new GuardWarning("warning",
                $"pack.zip 超过 2GB（约 {packBytes / (1024L * 1024 * 1024)}GB），部分设备安装/解包会非常慢，zip 32 位边界风险上升。"));
        if (finalApkBytes > MaxApkBytes)
            throw new InvalidOperationException(
                $"最终 APK 超过 4GB 上限（{finalApkBytes} 字节），zip 32 位格式无法承载，请精简整合包。");
        return warnings;
    }
}
```

**PackReport.cs（完整）**：

```csharp
using ZL2PackBundler.Core.Analysis;
using ZL2PackBundler.Core.Models;

namespace ZL2PackBundler.Core;

public sealed record PackReport(
    BundledPackType Type,
    PackFormat Format,
    string Name,
    string? McVersion,
    long PackZipBytes,
    long FinalApkBytes,
    string OutputPath,
    IReadOnlyList<OfflineItem> OfflineReport,
    IReadOnlyList<GuardWarning> Warnings,
    string? CertificateInfo);
```

**PackPipeline.cs（完整）**：

```csharp
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

            var rebuilt = Path.Combine(tempDir, "rebuilt.apk");
            progress?.Invoke("重建 APK（嵌入内嵌资产）…");
            ApkRebuilder.Rebuild(options.BaseApk, rebuilt, manifest.ToJson(), packZip, progress);

            var warnings = Guards.Check(packBytes, new FileInfo(rebuilt).Length);

            progress?.Invoke("zipalign + apksigner 签名…");
            ApkSigner.Run(sdk.BuildToolsDir, rebuilt, options.OutputApk, options.Signing, progress);
            var cert = ApkSigner.Verify(sdk.BuildToolsDir, options.OutputApk, progress);

            return new PackReport(
                analysis.Type, analysis.Format, name, analysis.McVersion,
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
```

**PipelineTests.cs（完整）**：

```csharp
using Xunit;
using ZL2PackBundler.Core;

namespace ZL2PackBundler.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void GuardsWarnAbove2Gb()
    {
        var warnings = Guards.Check(3L * 1024 * 1024 * 1024, 3L * 1024 * 1024 * 1024);
        Assert.Contains(warnings, w => w.Level == "warning" && w.Message.Contains("2GB"));
    }

    [Fact]
    public void GuardsRejectAbove4Gb()
    {
        Assert.Throws<InvalidOperationException>(() => Guards.Check(5L * 1024 * 1024 * 1024, 5L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void GuardsPassUnderLimits()
    {
        Assert.Empty(Guards.Check(1024, 1024));
    }

    [Fact]
    public void SanitizeIdKeepsSafeChars()
    {
        Assert.Equal("my-pack-2026", PackPipeline.SanitizeId("My Pack 2026！"));
    }
}
```

**Verification**：`cd PackBundler && dotnet test ZL2PackBundler.sln --filter FullyQualifiedName~PipelineTests`（4 通过）。
**Commit**：`git commit -m "feat(packbundler): 打包流水线编排与体积护栏"`

---

## Task A7：CLI（Cli）

**Files**：替换 `PackBundler/src/ZL2PackBundler.Cli/Program.cs`；微调 `KeyStoreGenerator.cs`（支持自定义 alias/密码，默认值不变）。
**Why**：spec 7.2 三个子命令；GUI 与 CLI 共用 Core。
**Change Necessity**：code-change。
**Impact/Compatibility**：默认参数路径与 A6 一致；命令解析只支持 `--key value` 形式（布尔开关 `--force`/`--auto-keystore` 无值）。

**Steps**：

1. 将 `PackBundler/src/ZL2PackBundler.Core/Signing/KeyStoreGenerator.cs` 的 `Create` 签名改为（其余不变）：

```csharp
public static string Create(string outputPath, string? alias = null, string? password = null, Action<string>? log = null)
{
    if (File.Exists(outputPath)) return outputPath;
    var aliasName = alias ?? "zl2packbundler";
    var pass = password ?? ApkSigner.AutoKeyStorePassword;
    // ... 其余代码中把 "zl2packbundler" 替换为 aliasName、把 AutoKeyStorePassword 替换为 pass ...
}
```

2. 替换 `PackBundler/src/ZL2PackBundler.Cli/Program.cs`（完整）：

```csharp
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
        SdkDir = o.GetValueOrDefault("sdk"),
        Force = o.ContainsKey("force"),
        Signing = BuildSigning(o)
    };
    Console.WriteLine($"开始打包：{options.PackInput} -> {options.OutputApk}");
    var report = PackPipeline.Run(options, msg => Console.WriteLine("  " + msg));
    Console.WriteLine();
    Console.WriteLine("=== 打包完成 ===");
    Console.WriteLine($"类型: {report.Type} / 格式: {report.Format}");
    Console.WriteLine($"名称: {report.Name} / MC: {report.McVersion ?? "-"}");
    Console.WriteLine($"pack.zip: {report.PackZipBytes / (1024.0 * 1024.0):F1} MB");
    Console.WriteLine($"最终 APK: {report.FinalApkBytes / (1024.0 * 1024.0):F1} MB");
    foreach (var w in report.Warnings)
        Console.WriteLine($"[{w.Level}] {w.Message}");
    foreach (var item in report.OfflineReport)
        if (!item.Present)
            Console.WriteLine($"提示：{item.Path} 缺失，首次启动将联网补齐（{item.Label}）。");
    Console.WriteLine($"输出: {report.OutputPath}");
    Console.WriteLine();
    Console.WriteLine("合规提示：本工具不修改应用名称/图标。分发修改版 ZalithLauncher 须遵守 GPLv3 附加条款：");
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
      [--name <名称>] [--pack-id <id>] [--sdk <android-sdk目录>] [--force]
      [--keystore <path> --ks-pass <密码> --key-alias <别名> --key-pass <密码>]
      [--auto-keystore]
      嵌入整合包 -> zipalign -> apksigner(v2+v3) 重签名。

  zl2packbundler gen-keystore --out <ks.jks> [--alias <别名>] [--pass <密码>]
      生成一个测试用 keystore（正式分发请使用自有密钥）。

示例:
  zl2packbundler pack --apk zalith.apk --pack D:\mc\.minecraft --out out.apk --auto-keystore
""");
}
```

3. **Verification**：
- `cd PackBundler && dotnet build ZL2PackBundler.sln -c Debug`
- `dotnet run --project src/ZL2PackBundler.Cli -- help`（输出用法）
- `dotnet run --project src/ZL2PackBundler.Cli -- gen-keystore --out %TEMP%/zl2pb-test.keystore`（成功生成）

4. **Commit**：`git commit -m "feat(packbundler): CLI 命令（analyze/pack/gen-keystore）"`

---

## Task A8：WPF 图形界面（App）

**Files**：替换 `PackBundler/src/ZL2PackBundler.App/MainWindow.xaml`、`MainWindow.xaml.cs`；创建 `PackBundler/src/ZL2PackBundler.App/MainViewModel.cs`。
**Why**：spec 7.6 四页向导（选包→分析→签名→进度/报告）。
**Change Necessity**：code-change。
**Impact/Compatibility**：纯 UI 层，零 Core 改动；与 CLI 共用同一 `PackPipeline`。

**MainViewModel.cs（完整）**：

```csharp
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
```

**MainWindow.xaml（完整）**：

```xml
<Window x:Class="ZL2PackBundler.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ZL2PackBundler — 整合包内嵌 APK 工具"
        Width="900" Height="680" WindowStartupLocation="CenterScreen">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="{Binding PageTitle}" FontSize="22" FontWeight="Bold" Margin="0,0,0,12"/>

        <Grid Grid.Row="1">
            <!-- 页1：选择基础 APK 与整合包 -->
            <StackPanel x:Name="Page1">
                <TextBlock Text="基础 APK（ZL2 或其改名分支的构建产物）" FontWeight="SemiBold"/>
                <DockPanel Margin="0,4,0,16">
                    <Button DockPanel.Dock="Right" Content="浏览…" Width="80" Click="OnBrowseApk"/>
                    <TextBox Text="{Binding ApkPath, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,8,0"/>
                </DockPanel>
                <TextBlock Text="整合包（.minecraft 文件夹 或 整合包 zip）" FontWeight="SemiBold"/>
                <DockPanel Margin="0,4,0,16">
                    <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                        <Button Content="选文件夹" Width="80" Margin="4,0,0,0" Click="OnBrowsePackFolder"/>
                        <Button Content="选压缩包" Width="80" Margin="4,0,0,0" Click="OnBrowsePackFile"/>
                    </StackPanel>
                    <TextBox Text="{Binding PackPath, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,8,0"/>
                </DockPanel>
                <Button Content="分析" Width="120" HorizontalAlignment="Left" Click="OnAnalyze"/>
            </StackPanel>

            <!-- 页2：分析结果 -->
            <StackPanel x:Name="Page2" Visibility="Collapsed">
                <TextBox Text="{Binding AnalysisText}" IsReadOnly="True" TextWrapping="Wrap"
                         Height="320" VerticalScrollBarVisibility="Auto"/>
                <TextBlock Text="{Binding WarningsText}" Foreground="#B45309" TextWrapping="Wrap" Margin="0,8,0,0"/>
            </StackPanel>

            <!-- 页3：签名与输出 -->
            <StackPanel x:Name="Page3" Visibility="Collapsed">
                <RadioButton Content="自动生成测试 keystore（仅测试分发）" IsChecked="{Binding UseAutoKeyStore}" GroupName="sign" Margin="0,4,0,4"/>
                <RadioButton Content="使用自有 keystore" IsChecked="{Binding UseOwnKeyStore}" GroupName="sign" Margin="0,4,0,4"/>
                <Grid Margin="24,4,0,12" IsEnabled="{Binding UseOwnKeyStore}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition/><RowDefinition/><RowDefinition/><RowDefinition/>
                    </Grid.RowDefinitions>
                    <TextBlock Grid.Row="0" Grid.Column="0" Text="keystore 路径" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBox Grid.Row="0" Grid.Column="1" Text="{Binding KeyStorePath, UpdateSourceTrigger=PropertyChanged}" Margin="0,2"/>
                    <TextBlock Grid.Row="1" Grid.Column="0" Text="store 密码" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <PasswordBox Grid.Row="1" Grid.Column="1" x:Name="KeyStorePassBox" Margin="0,2"/>
                    <TextBlock Grid.Row="2" Grid.Column="0" Text="别名" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBox Grid.Row="2" Grid.Column="1" Text="{Binding KeyAlias, UpdateSourceTrigger=PropertyChanged}" Margin="0,2"/>
                    <TextBlock Grid.Row="3" Grid.Column="0" Text="key 密码" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <PasswordBox Grid.Row="3" Grid.Column="1" x:Name="KeyPassBox" Margin="0,2"/>
                </Grid>
                <TextBlock Text="输出 APK 路径" FontWeight="SemiBold"/>
                <DockPanel Margin="0,4,0,0">
                    <Button DockPanel.Dock="Right" Content="浏览…" Width="80" Click="OnBrowseOutput"/>
                    <TextBox Text="{Binding OutputPath, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,8,0"/>
                </DockPanel>
                <Button Content="开始打包" Width="140" Margin="0,16,0,0" Click="OnStartPack"/>
            </StackPanel>

            <!-- 页4：进度与报告 -->
            <Grid x:Name="Page4" Visibility="Collapsed">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/><RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                <StackPanel Grid.Row="0">
                    <TextBlock Text="{Binding ProgressText}" Margin="0,0,0,4"/>
                    <ProgressBar Height="18" Minimum="0" Maximum="100" Value="{Binding ProgressValue}"/>
                </StackPanel>
                <TextBox Grid.Row="1" Text="{Binding LogText}" IsReadOnly="True" TextWrapping="Wrap"
                         Margin="0,12,0,0" VerticalScrollBarVisibility="Auto"/>
            </Grid>
        </Grid>

        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="BackButton" Content="上一步" Width="90" Click="OnBack" Margin="0,0,8,0"/>
            <Button x:Name="NextButton" Content="下一步" Width="90" Click="OnNext"/>
        </StackPanel>
    </Grid>
</Window>
```

**MainWindow.xaml.cs（完整）**：

```csharp
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
        if (dlg.ShowDialog(this) == true) vm.ApkPath = dlg.FileName;
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
```

3. **Verification**：
- `cd PackBundler && dotnet build ZL2PackBundler.sln -c Debug`（WPF 工程编译通过）。
- 冒烟：`dotnet run --project src/ZL2PackBundler.App`，窗口打开 → 选整合包 → 分析 → 页 2 显示报告（人工）。
4. **Commit**：`git commit -m "feat(packbundler): WPF 四步向导界面"`

---

## Task A9：端到端集成验证（本机）

**Files**：创建 `PackBundler/scripts/integration-test.sh`（Git Bash 可用）。
**Why**：spec 12 验收第 1/4/6 的工具侧部分：完整命令跑通 + apksigner verify + 资产存在检查。
**Change Necessity**：docs/scripts-only（不改产品代码）。
**Impact/Compatibility**：仅本机验证，不进入产物。

**integration-test.sh（完整）**：

```bash
#!/usr/bin/env bash
# ZL2PackBundler 端到端集成验证（Git Bash）
set -euo pipefail
cd "$(dirname "$0")/.."

TMP=.tmp/itest
rm -rf "$TMP"
mkdir -p "$TMP/mc/versions/1.20.1" "$TMP/mc/mods" "$TMP/mc/libraries" "$TMP/mc/assets/indexes" "$TMP/mc/assets/objects"
printf '{"id":"1.20.1"}' > "$TMP/mc/versions/1.20.1/1.20.1.json"
echo "fake-jar" > "$TMP/mc/versions/1.20.1/1.20.1.jar"
echo "options" > "$TMP/mc/options.txt"

# 伪造基础 APK（带旧签名文件与 dex）
python - <<'PY'
import zipfile, os
os.makedirs('.tmp/itest', exist_ok=True)
with zipfile.ZipFile('.tmp/itest/base.apk', 'w') as z:
    z.writestr('classes.dex', b'dex')
    z.writestr('META-INF/MANIFEST.MF', b'Manifest-Version: 1.0')
    z.writestr('META-INF/CERT.RSA', b'fake-signature')
    z.writestr('AndroidManifest.xml', b'<manifest/>')
PY

echo "== analyze =="
dotnet run --project src/ZL2PackBundler.Cli -- analyze --pack "$TMP/mc"

echo "== pack =="
dotnet run --project src/ZL2PackBundler.Cli -- pack \
  --apk "$TMP/base.apk" --pack "$TMP/mc" --out "$TMP/out.apk" --auto-keystore --name "集成测试包"

echo "== 检查输出 =="
python - <<'PY'
import zipfile
z = zipfile.ZipFile('.tmp/itest/out.apk')
names = z.namelist()
assert 'assets/bundled_pack/manifest.json' in names, names
assert 'assets/bundled_pack/pack.zip' in names, names
assert 'META-INF/MANIFEST.MF' not in names, names
assert 'classes.dex' in names, names
print('资产存在且旧签名已剔除: OK')
manifest = z.read('assets/bundled_pack/manifest.json').decode('utf-8')
assert '"schema": 1' in manifest, manifest
assert '1.20.1' in manifest, manifest
print('manifest 内容: OK')
PY

SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$LOCALAPPDATA/Android/Sdk}}"
BT=$(ls -d "$SDK"/build-tools/*/ | sort -V | tail -1)
java -jar "$BT"lib/apksigner.jar verify --verbose --print-certs --min-sdk-version 26 "$TMP/out.apk" | head -8
echo "INTEGRATION OK"
```

**Verification**：`cd PackBundler && bash scripts/integration-test.sh`，期望输出 `INTEGRATION OK` 且 apksigner 输出 `Verifies`。
**Commit**：`git commit -m "test(packbundler): 端到端集成验证脚本"`

---

## Task B1：App 端契约解析（ZalithLauncher）

**Files**：创建 `ZalithLauncher/src/main/java/com/movtery/zalithlauncher/game/download/modpack/install/BundledModpackManifest.kt`。
**Why**：契约消费端（与 A2 的 C# 写入端对称），spec 6.1/8.5。
**Change Necessity**：code-change（App 必须能读内嵌资产）。
**Impact/Compatibility**：失败即视为无内嵌包（绝不阻塞启动）；未知 schema 拒绝并记日志。

**BundledModpackManifest.kt（完整）**：

```kotlin
/*
 * Zalith Launcher 2
 * Copyright (C) 2025 MovTery <movtery228@qq.com> and contributors
 * (GPL-3.0，见仓库 LICENSE)
 */
package com.movtery.zalithlauncher.game.download.modpack.install

import android.content.Context
import com.movtery.zalithlauncher.utils.GSON
import com.movtery.zalithlauncher.utils.logging.Logger
import java.io.File

private const val TAG = "BundledModpackManifest"

/** 内嵌整合包资产目录（跨端契约，schema=1） */
const val BUNDLED_PACK_ASSET_DIR = "bundled_pack"
const val BUNDLED_PACK_MANIFEST_ASSET = "$BUNDLED_PACK_ASSET_DIR/manifest.json"
const val BUNDLED_PACK_ZIP_ASSET = "$BUNDLED_PACK_ASSET_DIR/pack.zip"
const val BUNDLED_PACK_MARKER_FILE = ".bundled_pack_version"

data class BundledModpackManifest(
    val schema: Int = 0,
    val packId: String? = null,
    val packVersion: Long = -1L,
    val type: String? = null,
    val name: String? = null,
    val mcVersion: String? = null,
    val sizeBytes: Long = -1L,
    val sha256: String? = null,
) {
    companion object {
        const val SCHEMA = 1
        const val TYPE_SNAPSHOT = "snapshot"
        const val TYPE_PACKZIP = "packzip"

        /** 从 assets 读取并校验；不存在或非法返回 null（调用方视为无内嵌包）。 */
        fun load(context: Context): BundledModpackManifest? {
            val json = runCatching {
                context.assets.open(BUNDLED_PACK_MANIFEST_ASSET).use { it.readBytes().decodeToString() }
            }.getOrElse {
                Logger.debug(TAG, "No bundled modpack manifest in this APK", it)
                return null
            }
            val manifest = runCatching {
                GSON.fromJson(json, BundledModpackManifest::class.java)
            }.getOrElse {
                Logger.error(TAG, "Failed to parse bundled modpack manifest", it)
                return null
            }
            if (!manifest.validate()) {
                Logger.error(TAG, "Bundled modpack manifest validation failed: $manifest")
                return null
            }
            return manifest
        }
    }

    /** 与 Windows 端 PackBundler 的 Validate() 规则一致。 */
    fun validate(): Boolean {
        if (schema != SCHEMA) return false
        if (packId.isNullOrBlank()) return false
        if (packVersion < 0) return false
        if (type != TYPE_SNAPSHOT && type != TYPE_PACKZIP) return false
        if (sizeBytes <= 0) return false
        val sha = sha256 ?: return false
        if (!sha.matches(Regex("^[0-9a-f]{64}$"))) return false
        if (type == TYPE_SNAPSHOT && mcVersion.isNullOrBlank()) return false
        return true
    }

    val isSnapshot: Boolean get() = type == TYPE_SNAPSHOT
    val markerContent: String get() = "$packId:$packVersion"

    fun markerMatches(file: File): Boolean =
        file.exists() && runCatching { file.readText() }.getOrNull() == markerContent
}
```

**Verification**：`./gradlew :ZalithLauncher:compileDebugKotlin`（本任务只编译；单测在 B5）。
**Commit**：`git commit -m "feat(app): 内嵌整合包契约解析与校验（schema=1）"`

---

## Task B2：App 端解包任务（ZalithLauncher）

**Files**：创建 `ZalithLauncher/src/main/java/com/movtery/zalithlauncher/components/BundledModpackTask.kt`。
**Why**：spec 8.3/8.4：状态机 + snapshot 解包（sha256 校验、路径穿越防护、幂等）+ packzip 缓存复制；复用 splash 进度机制。
**Change Necessity**：code-change。
**Impact/Compatibility**：不触碰现有组件任务；snapshot 覆盖式解包幂等，失败不写标记。

**BundledModpackTask.kt（完整）**：

```kotlin
/*
 * Zalith Launcher 2
 * Copyright (C) 2025 MovTery <movtery228@qq.com> and contributors
 * (GPL-3.0，见仓库 LICENSE)
 */
package com.movtery.zalithlauncher.components

import android.content.Context
import android.os.StatFs
import com.movtery.zalithlauncher.game.download.modpack.install.BUNDLED_PACK_MARKER_FILE
import com.movtery.zalithlauncher.game.download.modpack.install.BUNDLED_PACK_ZIP_ASSET
import com.movtery.zalithlauncher.game.download.modpack.install.BundledModpackManifest
import com.movtery.zalithlauncher.game.path.GamePathManager
import com.movtery.zalithlauncher.game.version.installed.VersionsManager
import com.movtery.zalithlauncher.path.PathManager
import com.movtery.zalithlauncher.utils.logging.Logger
import java.io.File
import java.security.MessageDigest
import java.util.zip.ZipInputStream

private const val TAG = "BundledModpackTask"

/**
 * 内嵌整合包安装任务（splash 解包组件之一）。
 * snapshot：解包到默认游戏目录并写标记；packzip：复制到缓存供 ModpackImporter 导入。
 */
class BundledModpackTask(private val context: Context) : AbstractUnpackTask() {

    val manifest: BundledModpackManifest? = BundledModpackManifest.load(context)

    /** packzip 模式导入所需的缓存文件（SplashActivity 在任务完成后读取并转发给 MainActivity）。 */
    var pendingImportFile: File? = null
        private set

    private val markerFile: File
        get() = File(GamePathManager.getCurrentPath(), BUNDLED_PACK_MARKER_FILE)

    /** 与 UnpackSingleTask.isCheckFailed 同语义：资产缺失/非法时跳过本任务。 */
    fun isCheckFailed(): Boolean = manifest == null

    override fun checkState(): InstallableItem.State {
        val manifest = manifest ?: return InstallableItem.State.NOT_EXISTS
        return when {
            manifest.markerMatches(markerFile) -> InstallableItem.State.FINISHED
            markerFile.exists() -> InstallableItem.State.PENDING // 换新包时自动重装
            else -> InstallableItem.State.NOT_STARTED
        }
    }

    override suspend fun run() {
        val manifest = manifest ?: return
        updateMessage("Preparing bundled modpack...")
        val targetDir = File(GamePathManager.getCurrentPath()).apply { mkdirs() }
        checkFreeSpace(targetDir, manifest.sizeBytes)

        when {
            manifest.isSnapshot -> runSnapshot(manifest, targetDir)
            else -> runPackZip(manifest)
        }
    }

    private fun checkFreeSpace(dir: File, packBytes: Long) {
        val free = StatFs(dir.absolutePath).availableBytes
        val required = packBytes * 3 / 2
        if (free < required) {
            throw IllegalStateException("Not enough free space for bundled modpack: need $required bytes, have $free")
        }
    }

    private fun runSnapshot(manifest: BundledModpackManifest, targetDir: File) {
        val digest = MessageDigest.getInstance("SHA-256")
        var totalBytes = 0L
        context.assets.open(BUNDLED_PACK_ZIP_ASSET).use { input ->
            ZipInputStream(input).use { zip ->
                while (true) {
                    val entry = zip.nextEntry ?: break
                    val safeName = BundledPackPathSafety.sanitize(entry.name) ?: run {
                        Logger.warning(TAG, "Rejecting unsafe zip entry: ${entry.name}")
                        continue
                    }
                    if (safeName == BUNDLED_PACK_MARKER_FILE) {
                        Logger.warning(TAG, "Rejecting marker overwrite entry: ${entry.name}")
                        continue
                    }
                    val target = File(targetDir, safeName)
                    if (entry.isDirectory) {
                        target.mkdirs()
                        continue
                    }
                    target.parentFile?.mkdirs()
                    target.outputStream().use { out ->
                        val buffer = ByteArray(1 shl 16)
                        while (true) {
                            val read = zip.read(buffer)
                            if (read < 0) break
                            out.write(buffer, 0, read)
                            digest.update(buffer, 0, read)
                            totalBytes += read
                            if (totalBytes % (64L shl 20) == 0L) {
                                updateMessage("Extracting bundled modpack... ${totalBytes shr 20} MB")
                            }
                        }
                    }
                }
            }
        }
        val actual = digest.digest().joinToString("") { "%02x".format(it) }
        if (actual != manifest.sha256) {
            throw IllegalStateException(
                "Bundled pack checksum mismatch: expected ${manifest.sha256}, got $actual"
            )
        }
        markerFile.parentFile?.mkdirs()
        markerFile.writeText(manifest.markerContent)
        VersionsManager.refresh(TAG)
        Logger.info(TAG, "Bundled snapshot installed: ${manifest.markerContent}")
    }

    private fun runPackZip(manifest: BundledModpackManifest) {
        val dir = File(PathManager.DIR_CACHE_MODPACK_DOWNLOADER, "bundled").apply { mkdirs() }
        val target = File(dir, "${manifest.packId}.zip")
        var totalBytes = 0L
        context.assets.open(BUNDLED_PACK_ZIP_ASSET).use { input ->
            target.outputStream().use { out ->
                val buffer = ByteArray(1 shl 16)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    out.write(buffer, 0, read)
                    totalBytes += read
                    if (totalBytes % (64L shl 20) == 0L) {
                        updateMessage("Preparing bundled modpack... ${totalBytes shr 20} MB")
                    }
                }
            }
        }
        pendingImportFile = target // 不写标记：导入成功后由 MainActivity 侧写入
        Logger.info(TAG, "Bundled packzip staged for import: ${target.absolutePath}")
    }
}

/** zip 条目路径安全校验（纯函数，便于单测）。 */
object BundledPackPathSafety {
    /** 通过返回规范化条目名；拒绝绝对路径、'..'、反斜杠、盘符与空段。 */
    fun sanitize(name: String): String? {
        if (name.isEmpty()) return null
        if (name.startsWith("/") || name.startsWith("\\")) return null
        if (name.contains("\\")) return null
        val parts = name.split('/')
        for ((index, part) in parts.withIndex()) {
            val isLast = index == parts.lastIndex
            if (part == "..") return null
            if (part.isEmpty() && !isLast) return null
            if (part.contains(":")) return null
        }
        return name
    }
}
```

**Verification**：`./gradlew :ZalithLauncher:compileDebugKotlin`；`./gradlew :ZalithLauncher:testDebugUnitTest --tests "*PathSafety*"`（B5 补测试后）。本任务编译通过即可。
**Commit**：`git commit -m "feat(app): 内嵌整合包解包任务（snapshot/packzip + 路径安全）"`

---

## Task B3：Splash 接线与字符串资源

**Files**：修改 `ZalithLauncher/src/main/java/com/movtery/zalithlauncher/ui/activities/SplashActivity.kt`、`ZalithLauncher/src/main/res/values/strings.xml`、`values-zh-rCN/strings.xml`、`values-zh-rTW/strings.xml`。
**Why**：spec 8.2：任务挂载 + packzip 自动转发；老 APK 无感。
**Change Necessity**：code-change。
**Impact/Compatibility**：老 APK（无 manifest 资产）`isCheckFailed()==true` → 不追加任务，路径不变。

**Steps**：

1. `SplashActivity.kt` 在 `const val IMPORT_TYPE_UNKNOWN = "unknown"` 之后追加：

```kotlin
const val EXTRA_IMPORT_BUNDLED = "EXTRA_IMPORT_BUNDLED"
const val EXTRA_IMPORT_PACK_NAME = "EXTRA_IMPORT_PACK_NAME"
const val EXTRA_IMPORT_BUNDLED_MARKER = "EXTRA_IMPORT_BUNDLED_MARKER"
```

2. 在类字段区（`private var pendingImportIntent: Intent? = null` 之后）追加：

```kotlin
private var bundledModpackTask: BundledModpackTask? = null
```

3. 在 `initUnpackItems()` 的 `val jnaTask = ...` 块之后、`unpackItems.sort()` 之前追加：

```kotlin
val bundledTask = BundledModpackTask(this@SplashActivity)
if (!bundledTask.isCheckFailed()) {
    bundledModpackTask = bundledTask
    unpackItems.add(
        InstallableItem(
            getString(R.string.unpack_screen_bundled_pack),
            getString(R.string.unpack_screen_bundled_pack_summary),
            bundledTask
        )
    )
}
```

4. 在 `finishSplash()` 的 `swapToMain()` 调用前插入：

```kotlin
val bundledImport = bundledModpackTask?.pendingImportFile
val bundledManifest = bundledModpackTask?.manifest
if (bundledImport != null && bundledManifest != null) {
    val forward = Intent(this, MainActivity::class.java).apply {
        flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
        putExtra(EXTRA_IMPORT_ACTION, Intent.ACTION_VIEW)
        putExtra(EXTRA_IMPORT_URI, Uri.fromFile(bundledImport))
        putExtra(EXTRA_IMPORT_TYPE, IMPORT_TYPE_MODPACK)
        putExtra(EXTRA_IMPORT_BUNDLED, true)
        putExtra(EXTRA_IMPORT_PACK_NAME, bundledManifest.name)
        putExtra(EXTRA_IMPORT_BUNDLED_MARKER, bundledManifest.markerContent)
    }
    startActivity(forward)
    finish()
    return
}

swapToMain()
```

5. 顶部 import 区追加：`import com.movtery.zalithlauncher.components.BundledModpackTask`。

6. `values/strings.xml` 在 `unpack_screen_jna` 之后追加：

```xml
<string name="unpack_screen_bundled_pack">Bundled modpack</string>
<string name="unpack_screen_bundled_pack_summary">Imports the modpack embedded in this APK</string>
```

7. `values-zh-rCN/strings.xml` 追加：

```xml
<string name="unpack_screen_bundled_pack">内嵌整合包</string>
<string name="unpack_screen_bundled_pack_summary">安装此 APK 内嵌的整合包</string>
```

8. `values-zh-rTW/strings.xml` 追加：

```xml
<string name="unpack_screen_bundled_pack">內嵌整合包</string>
<string name="unpack_screen_bundled_pack_summary">安裝此 APK 內嵌的整合包</string>
```

**Verification**：`./gradlew :ZalithLauncher:compileDebugKotlin`；`./gradlew :ZalithLauncher:lintDebug`（可选）。
**Commit**：`git commit -m "feat(app): splash 挂载内嵌整合包任务并自动转发导入"`

---

## Task B4：导入管线 bundled 模式

**Files**：修改 `ZalithLauncher/src/main/java/com/movtery/zalithlauncher/viewmodel/ModpackImportViewModel.kt`、`ZalithLauncher/src/main/java/com/movtery/zalithlauncher/ui/activities/MainActivity.kt`。
**Why**：spec 8.2：bundled 模式跳过版本名输入，导入成功后写标记（防重复导入）。
**Change Necessity**：code-change。
**Impact/Compatibility**：新参数默认 null，既有调用（DownloadModPackScreen 等）零变化。

**Steps**：

1. `ModpackImportViewModel.kt`：
   - 顶部 import 追加：`import com.movtery.zalithlauncher.game.download.modpack.install.BUNDLED_PACK_MARKER_FILE`、`import com.movtery.zalithlauncher.game.path.GamePathManager`、`import java.io.File`。
   - `import(...)` 签名改为：

```kotlin
fun import(
    context: Context,
    uri: Uri,
    bundledName: String? = null,
    bundledMarker: String? = null,
    onStart: () -> Unit = {},
    onStop: () -> Unit = {}
) {
```

   - 构造函数参数改为：

```kotlin
waitForVersionName = if (bundledName != null) { { bundledName } } else ::waitForVersionName,
```

   - `onFinished` 回调开头（`importer = null` 之前）插入：

```kotlin
if (bundledMarker != null) {
    runCatching {
        File(GamePathManager.getCurrentPath(), BUNDLED_PACK_MARKER_FILE).apply {
            parentFile?.mkdirs()
            writeText(bundledMarker)
        }
    }.onFailure { e ->
        Logger.error(TAG, "Failed to write bundled pack marker", e)
    }
}
```

2. `MainActivity.kt` 的 `handleModpackImport(intent)` 中，将 `modpackImportViewModel.import(...)` 调用改为：

```kotlin
modpackImportViewModel.import(
    context = this@MainActivity,
    uri = uri,
    bundledName = intent.getStringExtra(EXTRA_IMPORT_PACK_NAME)?.takeIf {
        intent.getBooleanExtra(EXTRA_IMPORT_BUNDLED, false)
    },
    bundledMarker = intent.getStringExtra(EXTRA_IMPORT_BUNDLED_MARKER)?.takeIf {
        intent.getBooleanExtra(EXTRA_IMPORT_BUNDLED, false)
    },
    onStart = {
        lifecycleScope.launch {
            keepScreen(true)
        }
    },
    onStop = {
        lifecycleScope.launch {
            keepScreen(false)
        }
    }
)
```

（`EXTRA_IMPORT_*` 常量与 MainActivity 同包，无需 import。）

**Verification**：`./gradlew :ZalithLauncher:compileDebugKotlin`；代码走查点：bundledName 非空时 `waitForVersionName` 不弹窗、onFinished 写标记、默认参数不影响旧调用。
**Commit**：`git commit -m "feat(app): 导入管线支持 bundled 自动模式（跳过命名、完成后写标记）"`

---

## Task B5：App 单元测试

**Files**：创建 `ZalithLauncher/src/test/java/com/movtery/zalithlauncher/game/download/modpack/install/BundledModpackManifestTest.kt`、`ZalithLauncher/src/test/java/com/movtery/zalithlauncher/components/BundledPackPathSafetyTest.kt`。
**Why**：spec 11：契约校验 + 路径穿越拒绝（纯 JVM 单测，无 Robolectric 依赖）。
**Change Necessity**：test-only。
**Impact/Compatibility**：仅新增测试，不动主代码。

**BundledModpackManifestTest.kt（完整）**：

```kotlin
package com.movtery.zalithlauncher.game.download.modpack.install

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BundledModpackManifestTest {

    private fun valid() = BundledModpackManifest(
        schema = 1,
        packId = "test-pack",
        packVersion = 1,
        type = "snapshot",
        name = "Test",
        mcVersion = "1.20.1",
        sizeBytes = 1024,
        sha256 = "a".repeat(64)
    )

    @Test
    fun validManifestPasses() {
        assertTrue(valid().validate())
    }

    @Test
    fun wrongSchemaFails() {
        assertFalse(valid().copy(schema = 2).validate())
    }

    @Test
    fun missingPackIdFails() {
        assertFalse(valid().copy(packId = " ").validate())
    }

    @Test
    fun snapshotRequiresMcVersion() {
        assertFalse(valid().copy(mcVersion = null).validate())
    }

    @Test
    fun packZipWithoutMcVersionPasses() {
        assertTrue(valid().copy(type = "packzip", mcVersion = null).validate())
    }

    @Test
    fun badShaFails() {
        assertFalse(valid().copy(sha256 = "zzz").validate())
    }

    @Test
    fun markerContentIsPackIdColonVersion() {
        assertEquals("test-pack:1", valid().markerContent)
    }

    @Test
    fun gsonParsesCamelCaseJson() {
        val json = """
            {
              "schema": 1,
              "packId": "p1",
              "packVersion": 3,
              "type": "snapshot",
              "name": "n",
              "mcVersion": "1.20.1",
              "sizeBytes": 42,
              "sha256": "${"a".repeat(64)}"
            }
        """.trimIndent()
        val parsed = com.movtery.zalithlauncher.utils.GSON.fromJson(json, BundledModpackManifest::class.java)
        assertTrue(parsed.validate())
        assertEquals(3L, parsed.packVersion)
    }
}
```

**BundledPackPathSafetyTest.kt（完整）**：

```kotlin
package com.movtery.zalithlauncher.components

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class BundledPackPathSafetyTest {

    @Test
    fun normalPathsPass() {
        assertEquals("versions/1.20.1/1.20.1.jar", BundledPackPathSafety.sanitize("versions/1.20.1/1.20.1.jar"))
        assertEquals("mods/a.mod.jar", BundledPackPathSafety.sanitize("mods/a.mod.jar"))
        assertEquals("dir/", BundledPackPathSafety.sanitize("dir/"))
    }

    @Test
    fun parentTraversalRejected() {
        assertNull(BundledPackPathSafety.sanitize("../evil.txt"))
        assertNull(BundledPackPathSafety.sanitize("a/../../b.txt"))
        assertNull(BundledPackPathSafety.sanitize(".."))
    }

    @Test
    fun absoluteAndBackslashRejected() {
        assertNull(BundledPackPathSafety.sanitize("/etc/passwd"))
        assertNull(BundledPackPathSafety.sanitize("\\windows\\system32\\x"))
        assertNull(BundledPackPathSafety.sanitize("a\\b.txt"))
    }

    @Test
    fun emptySegmentsAndDriveRejected() {
        assertNull(BundledPackPathSafety.sanitize("a//b.txt"))
        assertNull(BundledPackPathSafety.sanitize(""))
        assertNull(BundledPackPathSafety.sanitize("C:/windows/x"))
    }
}
```

**Verification**：`./gradlew :ZalithLauncher:testDebugUnitTest --tests "com.movtery.zalithlauncher.game.download.modpack.install.BundledModpackManifestTest" --tests "com.movtery.zalithlauncher.components.BundledPackPathSafetyTest"`（12 通过）。
**Commit**：`git commit -m "test(app): 契约校验与 zip 路径安全单元测试"`

---

## Task B6：真机验收清单（手动）

**Files**：无代码改动。
**Why**：spec 12 验收 1–3 需要设备；本机无模拟器保证，交付人工清单。
**Change Necessity**：无（manual-verification）。

**步骤（需要：一台 Android 8+ 真机、由 Task A9 产出的测试 APK，或真实 ZL2 APK + 完整 .minecraft）**：

1. 用真实 ZL2 APK + 一个含 `versions/`、`libraries/`、`assets/` 的完整 `.minecraft` 运行 `pack` 命令产出 `out.apk`。
2. `adb install out.apk`（或拷贝到手机安装）。卸载旧同包名应用避免签名冲突。
3. 首次启动：观察 splash 出现 `Bundled modpack / 内嵌整合包` 任务与解包进度；完成后进入主界面。
4. 断网（开飞行模式）→ 主界面版本列表应出现该 MC 版本 → 选择离线账号启动游戏 → 游戏能进入标题/世界（完全离线验收）。
5. 再次冷启动：splash 不重复解包（标记文件生效）。
6. 用导出整合包 zip（MCBBS/Modrinth/CurseForge 任一）再产出一个 APK：安装后自动进入导入进度界面，完成后版本出现（允许联网）。
7. 用未内嵌包的原始 APK：启动行为与改动前一致（老 APK 无感）。
8. 预期偏差记录到 issue；全部通过即验收 1–3 达成。

**Verification**：人工勾选 1–7；无自动化命令（设备依赖）。

---

## Task C1：README 文档章节

**Files**：修改 `README_ZH_CN.md`（`## 📦 构建方式（开发者）` 之后新增章节）。
**Why**：工具用法 + GPL 合规指引（spec 13）。
**Change Necessity**：docs-only。
**Impact/Compatibility**：纯文档。

**追加内容（完整）**：

```markdown
## 📦 整合包内嵌 APK（ZL2PackBundler）

Windows 端工具 `PackBundler` 可把整合包直接嵌入 APK：玩家安装后首次启动自动解包/导入，无需二次下载整合包，直接游玩。

### 构建与运行

```bash
cd PackBundler
dotnet build ZL2PackBundler.sln -c Release
# 图形界面
dotnet run --project src/ZL2PackBundler.App
# 或命令行
dotnet run --project src/ZL2PackBundler.Cli -- pack --apk <基础.apk> --pack <整合包> --out <输出.apk> --auto-keystore
```

输入支持：`.minecraft` 游戏文件夹（含 `versions/`，推荐同时包含 `libraries/` 与 `assets/` 以实现完全离线）或 ZL2 支持的整合包压缩包（MCBBS/Modrinth/CurseForge/MultiMC）。

### 合规提醒（GPLv3 附加条款）

本工具不改名、不改图标。分发内嵌整合包的修改版时：
1. 构建期在 `ZalithLauncher/gradle.properties` 修改 `launcher_name` 等字段重命名；
2. 在启动页/主界面明显标注“非官方修改版”；
3. 保留版权声明。

详细设计见 `docs/aegis/specs/2026-08-30-bundled-modpack-apk-design.md`。
```

**Verification**：`git diff --check` 无空白错误；GitHub 渲染检查（人工）。
**Commit**：`git commit -m "docs: README 增加整合包内嵌 APK 章节"`

---

## Task C2：ADR 补录与索引更新

**Files**：创建 `docs/aegis/adr/2026-08-30-bundled-pack-asset-contract.md`；修改 `docs/aegis/INDEX.md`。
**Why**：spec 14 的 ADR 信号落地（契约决策、备选方案、兼容策略）。
**Change Necessity**：docs-only。
**Impact/Compatibility**：无。

**ADR（完整）**：

```markdown
# ADR：内嵌整合包资产契约 assets/bundled_pack/

- Date: 2026-08-30
- Status: accepted
- Deciders: 用户（2026-08-30 会话决策）+ 本会话

## 背景
ZL2PackBundler 工具把整合包嵌入 APK，ZL2 应用端首次启动自动导入。

## 决策
- 契约：`assets/bundled_pack/manifest.json`（schema=1）+ `assets/bundled_pack/pack.zip`。
- manifest 独立文件而非打进 pack.zip 内：应用端无需解压即可读元数据（大小/校验/类型），splash 状态机更快、更安全。
- 单 pack.zip 而非散装 assets：原子解包 + 单次 sha256 校验 + 大文件流式处理。
- 标记文件 `.bundled_pack_version`（内容 `packId:packVersion`）防重复安装、支持换包重装。

## 备选方案（已排除）
- apktool 解码重打包：慢、易破坏资源，大包不可行。
- Gradle 构建期内嵌：与“用户提供已构建 APK”的产品决策冲突。
- 散装 assets：无原子性、校验复杂、条目数爆炸。

## 兼容策略
- schema 大版本 +1 时两端校验器同步更新；读取端拒绝未知大版本并记日志（fail-safe）。
- packzip 不承诺离线；snapshot 为完全离线路径。

## 关联
- Spec: `docs/aegis/specs/2026-08-30-bundled-modpack-apk-design.md`
- Plan: `docs/aegis/plans/2026-08-30-bundled-modpack-apk.md`
```

**INDEX.md** 在 Specs 与 Baseline 之间追加：

```markdown
## Plans

- [2026-08-30 整合包内嵌 APK 实施计划](plans/2026-08-30-bundled-modpack-apk.md) — kind: plan

## ADR

- [2026-08-30 内嵌整合包资产契约](adr/2026-08-30-bundled-pack-asset-contract.md) — kind: adr
```

**Verification**：`git diff --check`；`ls docs/aegis/adr docs/aegis/plans`。
**Commit**：`git commit -m "docs(aegis): 内嵌整合包资产契约 ADR 与索引更新"`

---

## Execution Readiness View

- **Intent Lock**：按规格第 12 节验收标准交付；不扩展非目标（4.2 节）。
- **Scope Fence**：`PackBundler/` + `ZalithLauncher` 应用模块 5 触点 + docs；不改 Gradle 结构、不改现有导入/解包语义。
- **Baseline Lock**：契约 schema=1 冻结；应用侧复用 `AbstractUnpackTask`/`ModpackImporter` owner。
- **Approved Behavior**：双输入形态、GUI+CLI、用户提供 APK、双签名方式、完全离线（snapshot）、全自动导入——均为用户确认决策。
- **Owner / Contract Constraints**：契约唯一写入方 Core、唯一消费方 BundledModpackManifest；标记文件由 App 维护。
- **Compatibility Boundary**：老 APK 启动路径不变；外部导入流程不变；组件解包不变。
- **Retirement Boundary**：本计划无退役对象（纯新增）；`--force` 覆盖语义只作用于内嵌资产条目。
- **Task Batches**：A 批（Windows 工具 A1–A9）→ B 批（App 端 B1–B5）→ C 批（文档 C1–C2）；B6 为交付后人工清单。
- **Test Obligations**：每任务精确命令；A9 集成脚本 + B5 单测为硬性门槛。
- **Review Gates**：A 批完成后跑 A9 全绿再进 B 批；B 批完成后 compileDebugKotlin + 单测全绿再进 C 批。
- **Drift / Rewind Rules**：发现规格矛盾→停下回报，不回写规格；单任务失败→修到绿再提交，不跨任务提交。
- **Evidence Required Before Completion**：A9 `INTEGRATION OK`、`dotnet test` 全绿、`gradlew testDebugUnitTest` 全绿、git log 含 11 个任务提交、真机清单交付。
- **Advisory Boundary**: 本视图为执行指引，不构成完成授权。

## 风险与回滚

| 风险 | 缓解 |
| --- | --- |
| 真机无设备/模拟器 | B6 人工清单交付；A9 本机集成覆盖管道 |
| 2GB+ 大包解包慢 | 体积护栏 + splash 进度 UI + 后台 IO |
| packzip 首次导入联网下载 | 工具报告明确提示（离线只对 snapshot 成立） |
| Gradle 首次构建下载依赖慢 | B 批使用 `--offline` 前先普通跑一次；失败重试 |
| 中文控制台输出乱码 | CLI 设置 `Console.OutputEncoding = UTF8`；文档示例用 bash |
| 回滚 | 每个 Task 独立提交，`git revert <commit>` 即可整体回滚；无 schema 迁移负担（纯新增） |

## 完成定义

A9 集成全绿、Windows `dotnet test` 全绿、App `testDebugUnitTest` 全绿、11 个任务提交齐全、README/ADR/索引齐备、B6 清单交付用户。

