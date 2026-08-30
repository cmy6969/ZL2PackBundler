# ZL2 应用端接入补丁

本目录提供 ZL2 应用端的改动，使启动器能识别并安装 APK 内嵌的整合包（`assets/bundled_pack/`）。

> 基准版本：基于 [ZalithLauncher2](https://github.com/ZalithLauncher/ZalithLauncher2) 上游提交 `87481daf`（v2.4.11）生成。上游有更新时如遇冲突，请按文件手工合并。

## 改动内容

**新增文件**（`files/` 目录，按 ZL2 源码路径存放，可直接复制）：

| 文件 | 作用 |
| --- | --- |
| `ZalithLauncher/src/main/java/.../components/BundledModpackTask.kt` | splash 解包任务：先校验 pack.zip 原始字节 SHA-256，再解包到游戏目录（路径穿越防护、标记文件、磁盘空间检查）；packzip 类型转发给导入管线 |
| `ZalithLauncher/src/main/java/.../game/download/modpack/install/BundledModpackManifest.kt` | 跨端契约 manifest 解析与校验（schema=1） |
| `ZalithLauncher/src/test/java/.../components/BundledPackPathSafetyTest.kt` | zip 路径安全单测 |
| `ZalithLauncher/src/test/java/.../game/download/modpack/install/BundledModpackManifestTest.kt` | 契约校验单测 |

**修改文件**（在 `zalithlauncher-bundled-modpack.patch` 中）：

| 文件 | 改动 |
| --- | --- |
| `ui/activities/SplashActivity.kt` | 挂载 BundledModpackTask；packzip 自动转发导入 |
| `ui/activities/MainActivity.kt` | 透传 bundled 导入参数 |
| `viewmodel/ModpackImportViewModel.kt` | bundled 模式跳过版本名输入、导入成功后写标记 |
| `res/values{,-zh-rCN,-zh-rTW}/strings.xml` | 新增 splash 文案（三语） |

## 应用方式（二选一）

**方式 A：复制文件 + 手工改 4 个文件**（适合上游已更新、补丁冲突时）：
1. 把 `files/ZalithLauncher/` 下的文件复制进 ZL2 源码对应位置；
2. 参照 `zalithlauncher-bundled-modpack.patch` 对 4 个修改文件做同样的增改。

**方式 B：git apply**（适合上游仍基于 `87481daf`）：

```bash
cd ZalithLauncher2
git apply ../ZL2PackBundler/android/zalithlauncher-bundled-modpack.patch
```

（用 `git apply --check ...` 可先验证能否干净应用。）

## 构建与验证

```bash
cd ZalithLauncher2
./gradlew :ZalithLauncher:compileDebugKotlin
./gradlew :ZalithLauncher:testDebugUnitTest \
  --tests 'com.movtery.zalithlauncher.components.BundledPackPathSafetyTest' \
  --tests 'com.movtery.zalithlauncher.game.download.modpack.install.BundledModpackManifestTest'
./gradlew :ZalithLauncher:assembleDebug
```

构建产物再用 PackBundler 嵌入整合包即可（见仓库根 [README](../README.md)）。

## GPL 提示

这些文件是 ZalithLauncher2（GPL-3.0）的一部分；将其合入构建得到的修改版应用，分发时须遵守 ZL2 的 GPLv3 附加条款（重命名、标注非官方修改版、保留版权声明）。
