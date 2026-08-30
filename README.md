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
            ▼  重建 APK zip（剔除旧签名）
   追加 assets/bundled_pack/{manifest.json, pack.zip}
            │
            ▼  zipalign → apksigner（v2+v3 重签名）
         最终 APK
            │ 安装到设备
            ▼  ZL2（已打补丁）SplashActivity
   首次启动自动安装内嵌整合包（快照解包 / 导入管线）
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

- Windows 10/11，.NET 8 SDK
- Android SDK build-tools（需要 `zipalign` 与 `apksigner`；安装 Android Studio 或命令行工具时勾选）
- JDK 17（`keytool`，用于自动生成测试 keystore）
- 出 APK 还需要 JDK 17 + Android SDK + Gradle（构建 ZL2）

## 构建与测试

```bash
dotnet build ZL2PackBundler.sln -c Release
dotnet test ZL2PackBundler.sln -c Release      # 25 个单元测试
bash scripts/integration-test.sh               # 端到端：嵌入→签名→校验（Git Bash）
```

## 完整出包流程

1. **构建带补丁的 ZL2**：把 [android/](android/) 中的改动应用到 ZalithLauncher2 源码（复制文件 或 `git apply`，详见 [android/README.md](android/README.md)），然后：

   ```bash
   cd ZalithLauncher2
   ./gradlew :ZalithLauncher:assembleDebug        # 或 assembleRelease（用你的签名）
   # 产物：ZalithLauncher/build/outputs/apk/debug/*.apk
   ```

2. **准备整合包**：
   - 完整离线：一个完整的 `.minecraft` 目录，含 `versions/`、`libraries/`、`assets/`（模组/配置/材质/存档随意放）；
   - 或 ZL2 支持的整合包 zip（MCBBS/Modrinth/CurseForge/MultiMC，首次导入可能联网补依赖）。

3. **嵌入整合包并重签名**：

   ```bash
   dotnet run --project src/ZL2PackBundler.Cli -- pack \
     --apk <基础.apk> --pack <整合包目录或zip> --out <输出.apk> \
     --auto-keystore        # 测试用；正式分发请用 --keystore/--ks-pass/--key-alias
   ```

   GUI 方式：`dotnet run --project src/ZL2PackBundler.App`，按向导四步完成。

4. 安装输出 APK，首次启动即自动安装整合包（splash 进度条），完成直接可玩。

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

1. 安装输出 APK → 首次启动 splash 出现「Bundled modpack / 内嵌整合包」进度
2. 日志出现 `BundledModpackManifest/INFO Bundled modpack manifest loaded` 与 `Bundled snapshot installed`
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
- **装机后无进度条/无版本**：确认基础 APK 是用打过补丁的源码构建的（原版 APK 不认识内嵌资产）；日志中应有 `Bundled modpack manifest loaded`。
- **包体过大**：参考体积护栏；正式渠道分发请评估 Play 等渠道的包体积限制。

## 参考文档

- 设计规格：[docs/specs/2026-08-30-bundled-modpack-apk-design.md](docs/specs/2026-08-30-bundled-modpack-apk-design.md)
- 实施计划：[docs/plans/2026-08-30-bundled-modpack-apk.md](docs/plans/2026-08-30-bundled-modpack-apk.md)
- 契约 ADR：[docs/adr/2026-08-30-bundled-pack-asset-contract.md](docs/adr/2026-08-30-bundled-pack-asset-contract.md)
