# 整合包内嵌 APK 系统设计规格（ZL2PackBundler）

- Date: `2026-08-30`
- Status: `设计已批准（用户 2026-08-30 会话），待用户审阅本规格后进入实施计划`
- Owner: 本会话（Windows 工具 `PackBundler` + ZL2 应用端配套改动）
- 关联基线: `baseline/2026-08-30-initial-baseline.md`

## 1. 背景与目标

ZalithLauncher2（ZL2）是 Android 端 Minecraft Java 版启动器。当前玩家安装 APK 后，整合包需另走“下载/导入”流程。本系统新增一条分发路径：

**整合包作者/分发者在 Windows 上把整合包直接嵌入 ZL2 APK，玩家安装该 APK 后首次启动自动解包/导入，无需二次下载与手动安装整合包，直接游玩。**

目标拆解：
1. 交付 Windows 端新项目 `PackBundler`（.NET 8）：把整合包嵌入任意已构建好的 ZL2 APK 并重签名。
2. 在 ZL2 应用内新增“内嵌整合包自动导入”能力（复用现有解包/导入机制）。
3. 文件夹快照路径达到**完全离线**（原版 libraries/assets 也来自内嵌包）。

## 2. 术语

| 术语 | 定义 |
| --- | --- |
| 基础 APK | 用户提供的已构建 ZL2 APK（或合规改名后的构建产物） |
| 整合包 | 输入内容：`.minecraft` 游戏文件夹 或 已导出的整合包 zip（MCBBS/Modrinth/CurseForge/MultiMC） |
| snapshot | 整合包类型：完整游戏目录快照（含 `versions/` 等），可完全离线 |
| packzip | 整合包类型：启动器导出的整合包压缩包，App 端复用 ModpackImporter 导入 |
| bundled_pack | APK 内嵌资产目录 `assets/bundled_pack/`（本系统唯一跨端契约） |
| 标记文件 | 游戏目录下 `.bundled_pack_version`，内容 `packId:packVersion`，防重复导入/触发更新 |

## 3. 用户决策记录（需求基线，均为 2026-08-30 会话确认）

1. 输入形态：`.minecraft` 文件夹 + 已导出整合包 zip，两种都支持。
2. 工具形态：.NET 8 WPF 图形界面 + 命令行（共用内核）。
3. 基础 APK：由用户提供已构建好的 APK。
4. 签名：支持用户提供 keystore，也支持工具自动生成（生成时明确提示仅用于测试分发）。
5. 离线范围：完全离线（整合包含原版 libraries/assets）。
6. App 端行为：首次启动检测到内嵌包即**全自动静默导入**（无确认弹窗）。

## 4. 范围与非目标

### 4.1 范围内
- `PackBundler` Windows 项目（Core 内核库 + WPF GUI + CLI）。
- APK 后处理流水线：格式识别 → 打包 → 嵌入 → zipalign → apksigner。
- ZL2 应用端：`BundledModpackTask`（splash 解包任务）+ 契约校验 + 自动导入接线。
- 两端契约 `assets/bundled_pack/` 的完整定义与校验。
- 体积护栏与离线完整性报告。

### 4.2 非目标（明确不做）
- Play Asset Delivery / AAB 分发；App 改名/改图标（GPL 合规由用户在构建期自行满足，工具只提示并指引）。
- 整合包增量更新（差分）；内嵌账号/登录体系；JRE 之外的系统组件打包。
- 不把 `PackBundler` 接入 Gradle 构建，不给 App 新增 Gradle 模块。

## 5. 总体架构

```
[Windows] .minecraft 目录 / 整合包 zip
                │
                ▼
   PackBundler.Core: 识别 type → manifest.json + pack.zip
                │
                ▼
   PackBundler.Core: APK zip 重建（剔除旧签名）→ 追加 assets/bundled_pack/*
                │
                ▼
   Android SDK build-tools: zipalign -p 4 → apksigner(v2+v3)
                │
                ▼
           最终 APK
                │ 安装到设备
                ▼
[Android] SplashActivity: BundledModpackTask
   ├─ snapshot → 解压到默认游戏目录(.minecraft) → 写标记 → VersionsManager.refresh()
   └─ packzip → 复制到缓存 → 复用 ModpackImporter 导入管线 → 写标记
                │
                ▼
         主界面直接可玩
```

依赖方向：工具只产出资产契约，应用只消费；两端无代码依赖，仅共享 `manifest.json` schema。

## 6. 跨端契约：`assets/bundled_pack/`

APK 内固定两个条目：

- `assets/bundled_pack/manifest.json`（UTF-8）
- `assets/bundled_pack/pack.zip`

### 6.1 manifest schema（schema=1）

```json
{
  "schema": 1,
  "packId": "my-pack-2026",
  "packVersion": 1,
  "type": "snapshot",            // "snapshot" | "packzip"
  "name": "示例整合包",
  "mcVersion": "1.20.1",         // snapshot 必须；packzip 可为 null
  "sizeBytes": 123456789,
  "sha256": "<pack.zip 的 SHA-256，小写 hex>"
}
```

字段规则：
- `schema` 必填且等于 1；App 端拒绝未知大版本（记日志并跳过导入，不崩溃）。
- `packId`/`packVersion` 必填；二者组合决定 App 是否重新导入。
- `type` 必填，取值仅 `snapshot`/`packzip`。
- `sha256` 必填；App 端解包/复制前校验，不匹配即报错重试（防传输损坏）。

### 6.2 版本化策略
- 契约变更时 `schema` 大版本 +1，两端校验器同步更新并在本规格记录变更。
- 未知 schema 大版本：读取端拒绝并记日志（fail-safe，不影响旧功能）。

## 7. Windows 工具端设计（PackBundler）

### 7.1 解决方案布局

```
PackBundler/
├── ZL2PackBundler.sln
├── src/ZL2PackBundler.Core/       # net8.0 类库：全部业务逻辑（无 UI 依赖）
│   ├── Analysis/                  # 输入识别（folder/zip、四种导出格式、裸快照）
│   ├── Manifest/                  # manifest 模型 + 序列化 + 校验
│   ├── Apk/                       # APK zip 重建、旧签名剔除、STORED/DEFLATE 策略
│   ├── Signing/                   # keystore 生成(keytool)、zipalign/apksigner 调用、SDK 探测
│   └── Guards/                    # 体积护栏、离线完整性评估
├── src/ZL2PackBundler.App/        # net8.0-windows WPF：向导式界面（选包→分析→签名→进度）
└── src/ZL2PackBundler.Cli/        # net8.0 控制台：pack / analyze / gen-keystore 子命令
```

### 7.2 CLI 接口（GUI 与 CLI 共用 Core，行为一致）

```
zl2packbundler analyze --pack <folder|zip>                          # 输出识别结果+离线完整性报告
zl2packbundler pack --apk <base.apk> --pack <folder|zip> --out <out.apk>
                   [--name <名称>] [--pack-id <id>]
                   [--keystore <path> --ks-pass <pw> --key-alias <a> --key-pass <pw>]
                   [--auto-keystore] [--sdk <android-sdk-dir>] [--force]
zl2packbundler gen-keystore --out <ks.jks> --alias <a> [--pass <pw>]
```

### 7.3 流水线步骤（Core）

1. **加载基础 APK**：校验 zip 完整性；若已含 `assets/bundled_pack/` 且未传 `--force`，报错。
2. **识别输入**（优先级从高到低）：
   - 目录 → 校验为 `.minecraft`（必须含 `versions/` 且其中存在版本目录）→ `snapshot`。
   - zip → 内含 `modrinth.index.json` → Modrinth；内含 `manifest.json` 且含 `minecraft` 键 → CurseForge；含 `mmc-pack.json` 或 `instance.cfg` → MultiMC；MCBBS 元数据 → MCBBS；以上为 `packzip`（原样嵌入）。含 `versions/` +（`libraries/`|`assets/`|`mods/`）→ 裸快照 zip → `snapshot`。
   - 无法识别 → 报错并给出支持格式说明。
3. **生成 pack.zip**：
   - `snapshot`：把整个游戏目录压缩。已压缩扩展名（`.jar` `.png` `.zip` `.ogg` `.litematic` 等）用 STORED，其余 DEFLATE；排除 `.bundled_pack_version`、`logs/`、`crash-reports/` 等运行时产物。
   - `packzip`：原文件原样作为 pack.zip（不重压）。
4. **离线完整性评估**（仅报告，不阻断）：
   - 必需：`versions/<mc>/<mc>.json` 与 `<mc>.jar`。
   - 完全离线需：`libraries/`、`assets/indexes/<mc>.json`、`assets/objects/`。缺失项按“首次启动仍需联网补齐 X”列于报告与 GUI 警告。
   - `packzip` 恒提示“首次导入可能需要联网下载模组/依赖”。
5. **写 manifest**：计算 pack.zip 的 sha256 与 sizeBytes；`--pack-id` 缺省取 name+日期派生，`packVersion` 默认 1（GUI 可填）。
6. **重建 APK**：.NET 打开原 APK zip，复制全部条目但**剔除 `META-INF/*.RSA|*.DSA|*.EC|*.SF|*.MF` 旧签名文件**，追加 `assets/bundled_pack/manifest.json`（DEFLATE）与 `pack.zip`（STORED 存根以提速并避免双重压缩）。
7. **对齐与签名**：`zipalign -p 4 in out` → `apksigner sign --ks ... --v2 --v3`（或自动生成 keystore）。
8. **输出报告**：最终体积、离线完整度、GPL 合规提示、签名证书指纹。

### 7.4 体积护栏
- `pack.zip` > 2GB → 黄色警告（32 位 zip 边界、安装/解包时长风险）。
- 总 APK > 4GB → 拒绝（zip32 上限，apksigner 兼容风险）。
- GUI 在开始前展示预估并二次确认。

### 7.5 SDK 探测
- 依次探测 `ANDROID_HOME`、`ANDROID_SDK_ROOT`、默认路径（`%LOCALAPPDATA%\Android\Sdk`），取最高版本 build-tools；GUI 允许手动指定；找不到则明确报错指引安装。

### 7.6 GUI 流程（单窗口向导）
1. 页1：选择基础 APK + 整合包（文件选择器/拖拽）→ “分析”。
2. 页2：分析结果（type、MC 版本、离线完整度、预估体积、护栏警告）。
3. 页3：签名配置（使用自有 keystore / 自动生成）+ 输出路径。
4. 页4：进度条（压缩→嵌入→对齐→签名）与最终报告；失败可回看日志。
- 页4 始终展示 GPL 合规提示：分发修改版须按 README 指引重命名构建并保留版权声明，本工具不自动改名。

## 8. Android 应用端设计（ZalithLauncher）

### 8.1 新增文件
- `components/BundledModpackTask.kt`：`AbstractUnpackTask` 实现（与 JRE 解包同模式）。
- `game/download/modpack/install/BundledModpackManifest.kt`：契约模型 + 校验（读 `assets/bundled_pack/manifest.json`）。

### 8.2 接线点（改动最小化）
- `SplashActivity.initUnpackItems()`：若 assets 存在 `bundled_pack/manifest.json` 且校验通过，追加 `InstallableItem("内嵌整合包", ..., BundledModpackTask)`；不存在或校验失败 → 不加（老 APK 完全无感）。
- `startAllTask()` 现有并发解包机制直接承载（进度条、失败抛出 `SplashException` 走现有错误界面）。
- `finishSplash()`：若任务判定需要导入且为 `packzip`，构造转发 Intent（`EXTRA_IMPORT_URI=file://...`、`EXTRA_IMPORT_TYPE=modpack`、新增 `EXTRA_IMPORT_BUNDLED=true`、`EXTRA_IMPORT_PACK_NAME=<manifest.name>`）直达 MainActivity，复用其现有导入控制器；控制器在 bundled 模式下跳过“版本名输入”提示（直接使用包名），移动数据确认沿用现有逻辑（packzip 可能联网）。

### 8.3 状态机（checkState）
1. 无 manifest → `NOT_EXISTS`。
2. 读取游戏目录标记文件（`GamePathManager.getCurrentPath()/.bundled_pack_version`）：
   - 内容 == `"<packId>:<packVersion>"` → `FINISHED`（跳过）。
   - 文件存在但内容不同 → `PENDING`（App 更新带新包时自动重装）。
   - 不存在 → `NOT_STARTED`。
3. 标记文件只认当前游戏目录；用户切换游戏目录后视为未安装（允许重装，文档注明）。

### 8.4 执行（run，Dispatchers.IO）
公共前置：
1. 读 manifest 校验（schema/packId/type/sha256 字段齐全）。
2. 解包目标：默认游戏目录（`getExternalFilesDir(null)/.minecraft`，scoped storage，无需权限）。
3. `StatFs` 检查剩余空间 ≥ `sizeBytes * 1.5`，不足则抛异常（splash 错误页显示）。

`snapshot` 分支：
1. 从 assets 流式解压 `pack.zip` 到游戏目录（覆盖合并，幂等；对每个条目做路径穿越防护：拒绝绝对路径与 `..`，拒绝指向标记文件本身）。
2. 校验解压流的累计 sha256 == manifest.sha256（不符 → 抛异常，不写标记）。
3. 写标记文件 → `GamePathManager.reloadPath()`（触发 `VersionsManager.refresh()`）。
4. 失败不写标记 → 下次启动重试（覆盖式解压幂等）。

`packzip` 分支：
1. 复制 `pack.zip` → `DIR_CACHE_MODPACK_DOWNLOADER/bundled/<packId>.zip`。
2. 通知 `finishSplash` 生成转发 Intent（见 8.2），由 MainActivity 侧 ModpackImporter 完成导入；导入成功后写标记。
3. 导入未完成（用户取消/断网）不写标记，下次启动重新尝试。

### 8.5 兼容与安全约束
- 无内嵌包的 APK：启动路径逐字节不变。
- 契约解析失败：记日志并视为无内嵌包（绝不阻塞启动）。
- 解包禁用符号链接/硬链接条目；条目大小上限 = manifest.sizeBytes。
- `packzip` 不承诺离线（见第 4.2 节报告的约束）。

## 9. 离线模型

| 输入 | 内嵌内容 | 首次启动联网需求 |
| --- | --- | --- |
| 完整 `.minecraft`（versions+libraries+assets 齐全） | 全部 | 无（完全离线，登录除外） |
| `.minecraft` 缺 libraries/assets | 现有文件 | 仅补缺的原版依赖（工具报告列出） |
| 已导出整合包 zip | 包本体 | 按包格式可能下载模组/依赖（导入管线原有行为） |

注意：Minecraft 正版登录仍需联网；离线账号不受影响。此约束写进 GUI 提示。

## 10. 错误处理与恢复

| 场景 | 工具端 | App 端 |
| --- | --- | --- |
| 输入无法识别 | 报错+格式说明 | — |
| 基础 APK 已含内嵌包 | 报错（`--force` 覆盖） | — |
| 磁盘空间不足 | 打包前检查临时目录空间 | 解包前 StatFs 检查，错误页提示 |
| pack.zip 损坏 | 重打包 | sha256 校验失败→不写标记→重试 |
| 解包中途失败 | — | 不写标记，下次启动重试（幂等覆盖） |
| apksigner/zipalign 缺失 | 报错+SDK 指引 | — |
| keystore 密码错误 | 报错，不产出半成品 | — |

## 11. 测试策略

- Core 单测（xUnit）：格式识别矩阵（4 种导出格式+裸快照+垃圾输入）、manifest 序列化/校验、zip 重建（旧签名剔除、STORED/DEFLATE 策略）、体积护栏边界（2GB/4GB 用桩）。
- 集成测试（本机）：用样例 APK+样例 `.minecraft` 跑完整 `pack` 命令，`apksigner verify --verbose` 通过，`unzip -l` 确认资产存在。
- App 侧：契约校验单测（合法/缺字段/未知 schema/路径穿越条目）；真机验收见第 12 节。

## 12. 验收标准（可观测）

1. 工具对真实 ZL2 APK + 完整 `.minecraft` 产出 APK：`apksigner verify` 通过；设备安装后首次启动 splash 显示解包进度 → 主界面出现该版本 → 启动游戏全程无整合包下载（断网可玩，登录用离线账号）。
2. 工具对 MCBBS/Modrinth/CurseForge 导出包产出 APK：安装后自动走导入管线并成功生成版本。
3. 无内嵌包的老 APK：启动行为与改动前完全一致。
4. CLI 一条命令完成全部流程；GUI 四步向导产出相同结果（同一 Core）。
5. 打包中断/损坏场景按第 10 节恢复路径成立（至少单测覆盖 sha256 失败与路径穿越拒绝）。
6. 体积护栏：>2GB 出警告、>4GB 拒绝，均有测试。

## 13. 影响面清单（实施时触碰的文件）

- 新增 `PackBundler/` 全部文件。
- 改动 `ZalithLauncher/`：`components/BundledModpackTask.kt`（新增）、`game/download/modpack/install/BundledModpackManifest.kt`（新增）、`ui/activities/SplashActivity.kt`（initUnpackItems/finishSplash 小改）、`MainActivity` 或既有导入控制器（bundled 模式跳过命名）、字符串资源（zh/zh-rCN/en 等，至少 zh-rCN+en）。
- 文档：`README_ZH_CN.md` 增加“整合包内嵌 APK”章节（工具用法+合规指引）。

## 14. ADR 信号

- 新公共契约 `assets/bundled_pack/manifest.json`（schema=1）——跨端、持久、版本化；实施后补 ADR `adr/2026-08-30-bundled-pack-asset-contract.md` 记录决策、备选方案（assets 目录散装 vs 单 zip；manifest 放 zip 内 vs 独立文件）与兼容策略。
- 备选方案已评估并排除：apktool 重打包（慢/破坏资源）、Gradle 构建期内嵌（与“用户提供 APK”决策冲突）、App 内不做改动（无法满足“直接游玩”）。

## 15. 开放问题 / 后续扩展（不阻塞 v1）

- 增量更新：packVersion 变化时只替换差异文件（zip 中央目录 diff）。
- `packzip` 的完全离线化：工具端预下载解析模组文件后转为 snapshot（工作量巨大，明确延期）。
- 多语言 GUI 与 App 字符串翻译。
- 大文件存储优化（split zip / .obb）在 2GB 告警之后的进一步方案。

## 16. 规格自审记录

- 占位符扫描：无 TBD/TODO。
- 一致性：契约字段在 6/8 两节一致；决策记录与基线 4.x 一致。
- 歧义：`packVersion` 默认值、`--force` 语义、标记文件只认当前游戏目录——均已显式定义。
- 边界：非目标（4.2）、兼容边界（8.5）、契约版本化（6.2）已显式标注。
