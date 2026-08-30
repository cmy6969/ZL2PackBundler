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
