# ZL2PackBundler — 整合包内嵌 APK 工具

把 Minecraft 整合包直接嵌入 Zalith Launcher 2（ZL2）的 APK：玩家安装后首次启动自动解包/导入，**无需二次下载整合包，直接游玩**（配合完整游戏目录可完全离线）。

> 本项目是独立工具，与 [ZalithLauncher/ZalithLauncher2](https://github.com/ZalithLauncher/ZalithLauncher2) 官方项目**无隶属关系**。本仓库不包含 ZL2 应用源码，只提供接入补丁（见 [android/](android/)）。

## 工作原理

```
[Windows] 整合包（.minecraft 目录 或 整合包 zip）
            │
            ▼  ZL2PackBundler（.NET 8，GUI / CLI 共用内核）
   识别格式 → 生成 manifest.json + pack.zip
            │
            ▼  基础 APK 处理（自动）
   ├─ 官方原版 APK → 注入内置安装器（manifest 修补 + 注入 dex）
   └─ 已打补丁构建 → 直接使用
            │
            ▼  重建 APK zip（剔除旧签名）
   追加 assets/bundled_pack/{manifest.json, pack.zip}
            │
            ▼  zipalign → apksigner（v2+v3 重签名）
         最终 APK
            │ 安装到设备
            ▼  首次启动：安装器（官方 APK）或 SplashActivity（打补丁构建）
   自动安装内嵌整合包（快照解包 / 导入管线）
            │
            ▼  主界面直接出现该 MC 版本，零下载
```

## 目录结构

```
ZL2PackBundler.sln            .NET 8 解决方案
src/ZL2PackBundler.Core       内核：分析/打包/APK 重建/签名/护栏（GUI 与 CLI 共用）
src/ZL2PackBundler.App        WPF 四步向导（选包→分析→签名→进度）
src/ZL2PackBundler.Cli        命令行 zl2packbundler（analyze/pack/gen-keystore）
tests/ZL2PackBundler.Core.Tests   xUnit 单元测试
scripts/integration-test.sh   端到端集成验证（Git Bash）
android/                      ZL2 应用端接入补丁（文件 + git patch）
docs/                         设计规格 / 实施计划 / ADR
```

## 环境要求

**便携版（Releases 的 exe）**：仅需 Windows 10/11 x64，其它全部内置。

**源码构建/开发**：
- Windows 10/11，.NET 8 SDK
- JDK 17 与 Android SDK build-tools（未内嵌便携运行时 `runtime.zip` 时使用；`zipalign`/`aapt2`/`apksigner` 需要 build-tools，apktool 首次联网下载缓存于 `%APPDATA%\ZL2PackBundler\tools`）
- 自行构建带补丁的 ZL2 另需 Android SDK + Gradle（可选，见下）

## 下载使用（Releases 便携版）

从 [Releases](../../releases) 下载单个 exe 双击即用，**无需安装 .NET/JDK/Android SDK**（内置精简 JRE、apktool、zipalign/aapt2/apksigner，首次运行自动解包）：

- `ZL2PackBundler.App.exe` — 图形界面（四步向导）
- `zl2packbundler.exe` — 命令行

仅要求 Windows 10/11 x64。

## 构建与测试（开发者）

```bash
dotnet build ZL2PackBundler.sln -c Release
dotnet test ZL2PackBundler.sln -c Release      # 29 个单元测试
bash scripts/integration-test.sh               # 端到端：嵌入→签名→校验（Git Bash）

# 便携运行时内嵌（否则开发构建回退使用本机 JDK/Android SDK）
powershell -ExecutionPolicy Bypass -File scripts/build-bundled-runtime.ps1 `
  -ApktoolJar <apktool.jar> -BuildTools <sdk/build-tools/36.1.0> -JdkHome <jdk17>

# 单文件发布
dotnet publish src/ZL2PackBundler.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

推送 `v*` 标签（如 `v1.0.0`）后，GitHub Actions 会自动构建测试并把两个 exe 附加到 Release。

## 完整出包流程

1. **准备基础 APK（二选一）**：

   - **官方原版 APK（推荐，零代码工作）**：从 [ZalithLauncher2 Releases](https://github.com/ZalithLauncher/ZalithLauncher2/releases) 下载官方 APK。工具检测到官方原版后会自动注入一个内置安装器（修改清单入口 + 注入 dex），首次启动先由安装器装好整合包再进入原版启动器。
   - **自行构建带补丁的 ZL2**：把 [android/](android/) 中的改动应用到 ZalithLauncher2 源码（复制文件 或 `git apply`，详见 [android/README.md](android/README.md)），然后 `./gradlew :ZalithLauncher:assembleDebug`（或 `assembleRelease`）。

2. **准备整合包**：
   - 完整离线：一个完整的 `.minecraft` 目录，含 `versions/`、`libraries/`、`assets/`（模组/配置/材质/存档随意放）；
   - 或 ZL2 支持的整合包 zip（MCBBS/Modrinth/CurseForge/MultiMC，首次导入可能联网补依赖）。

3. **嵌入整合包并重签名**：

   ```bash
   dotnet run --project src/ZL2PackBundler.Cli -- pack \
     --apk <基础.apk> --pack <整合包目录或zip> --out <输出.apk> \
     --auto-keystore        # 测试用；正式分发请用 --keystore/--ks-pass/--key-alias
   ```

   GUI 方式：`dotnet run --project src/ZL2PackBundler.App`，按向导四步完成（选择 APK 后会自动显示“官方原版/已打补丁”检测结果）。

4. 安装输出 APK，首次启动即自动安装整合包（进度条），完成直接可玩。

## 命令行

```
zl2packbundler analyze --pack <文件夹|zip>     # 识别格式 + 离线完整性报告
zl2packbundler pack --apk <a.apk> --pack <包> --out <o.apk>
     [--name 名称] [--pack-id id] [--sdk SDK目录] [--force]
     [--keystore ks --ks-pass pw --key-alias a --key-pass pw] [--auto-keystore]
zl2packbundler gen-keystore --out ks.jks [--alias a] [--pass pw]
```

Android SDK 自动探测（环境变量 → 上次记住的目录 → 常见路径 → PATH 祖先 → 各盘符）；未找到时用 `--sdk` 指定一次即被记住。

## 跨端契约：`assets/bundled_pack/`

- `manifest.json`（schema=1）：`packId`/`packVersion`/`type`(`snapshot`|`packzip`)/`name`/`mcVersion`/`sizeBytes`/`sha256`
- `pack.zip`：整合包本体；App 端安装前校验其原始字节 SHA-256，通过后才写入游戏目录
- 标记文件 `.bundled_pack_version`（内容 `packId:packVersion`）防重复安装，换新包自动重装

## 体积护栏

- `pack.zip` > 2GB：警告（zip 32 位边界风险、安装/解包慢）
- 最终 APK > 4GB：拒绝（超出 zip 32 位格式承载能力）

## 真机验收清单

1. 安装输出 APK → 首次启动出现「正在安装内嵌整合包」进度界面（官方原版路径）或 splash 的「Bundled modpack」进度项（打补丁构建路径）
2. 打补丁构建路径：日志出现 `BundledModpackManifest/INFO Bundled modpack manifest loaded` 与 `Bundled snapshot installed`
3. 主界面版本列表出现该 MC 版本
4. 飞行模式下用离线账号启动游戏，能进标题/世界（完全离线）
5. 再次冷启动不重复解包
6. 无内嵌包的原始 APK 行为不变

## GPL-3.0 合规说明

- 本工具与 `android/` 下的接入代码均为 GPL-3.0（[LICENSE](LICENSE)）；Android 补丁文件源自 ZalithLauncher2（Copyright (C) 2025 MovTery 及贡献者），版权声明已保留。
- 分发内嵌整合包的修改版 ZL2 时，须遵守 ZL2 的 GPLv3 附加条款：构建期在 `ZalithLauncher/gradle.properties` 重命名（不得含 “ZalithLauncher”/“ZL”）、启动页标注“非官方修改版”、保留版权声明。
- 本工具不自动改名、不改图标。

## 常见问题

- **报“未找到 Android SDK build-tools”**：`--sdk <SDK目录>` 指定一次（GUI 里选择一次）即被记住；或设置 `ANDROID_HOME`。
- **官方原版 APK 首次打包很慢**：需要联网下载 apktool（仅首次，约 22MB）并对 APK 做一次解码/重建（视 APK 大小需数分钟），后续复用缓存；也支持直接选用打补丁构建的 APK 跳过注入。
- **装机后无进度条/无版本**：官方原版路径看是否出现「正在安装内嵌整合包」界面；打补丁构建路径确认日志中有 `Bundled modpack manifest loaded`（旧版工具产出的大小写 `type` 已修复，请用最新版重新打包）。
- **包体过大**：参考体积护栏；正式渠道分发请评估 Play 等渠道的包体积限制。

## 参考文档

- 设计规格：[docs/specs/2026-08-30-bundled-modpack-apk-design.md](docs/specs/2026-08-30-bundled-modpack-apk-design.md)
- 实施计划：[docs/plans/2026-08-30-bundled-modpack-apk.md](docs/plans/2026-08-30-bundled-modpack-apk.md)
- 契约 ADR：[docs/adr/2026-08-30-bundled-pack-asset-contract.md](docs/adr/2026-08-30-bundled-pack-asset-contract.md)
