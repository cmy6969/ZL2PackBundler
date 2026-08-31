# ZL2PackBundler — Modpack-in-APK Bundler

> 中文: [README.md](README.md)

Embeds a Minecraft modpack directly into a Zalith Launcher 2 (ZL2) APK: after installation, the modpack is unpacked/imported automatically on first launch — **no second download, play immediately** (fully offline when a complete game directory is bundled).

> This project is a standalone tool with **no affiliation** with the official [ZalithLauncher/ZalithLauncher2](https://github.com/ZalithLauncher/ZalithLauncher2) project. This repository does not contain ZL2 application source code; it only provides the integration patch (see [android/](android/)).

> **Development note**: this tool was developed by the DeepSeek AI assistant — architecture, code, tests and documentation were all produced by the AI through iterative rounds driven by the repository owner's (cmy6969) requirements; the owner is responsible for requirements, distribution and compliance. 本工具由 DeepSeek AI 助手完成开发。

## How it works

```
[Windows] Modpack (.minecraft directory or modpack zip)
            │
            ▼  ZL2PackBundler (.NET 8, GUI / CLI share the same core)
    Detect format → generate manifest.json + pack.zip
            │
            ▼  Base APK handling (automatic)
    ├─ Official unmodified APK → inject built-in installer (manifest patch + injected dex)
    └─ Already-patched build → used directly
            │
            ▼  Rebuild APK zip (old signatures stripped)
    Append assets/bundled_pack/{manifest.json, pack.zip}
            │
            ▼  zipalign → apksigner (v2+v3 re-signing)
           Final APK
            │  Install on device
            ▼  First launch: installer (official APK) or SplashActivity (patched build)
    Install the bundled modpack automatically (snapshot extraction / import pipeline)
            │
            ▼  The MC version appears in the main UI, zero download
```

## Directory layout

```
ZL2PackBundler.sln            .NET 8 solution
src/ZL2PackBundler.Core       Core: analysis / packing / APK rebuild / signing / guardrails (shared by GUI & CLI)
src/ZL2PackBundler.App        WPF 4-step wizard (select → analyze → sign → progress)
src/ZL2PackBundler.Cli        Command line zl2packbundler (analyze/pack/gen-keystore)
tests/ZL2PackBundler.Core.Tests   xUnit unit tests
scripts/integration-test.sh   End-to-end integration verification (Git Bash)
android/                      ZL2 app-side integration patch (files + git patch)
docs/                         Design spec / implementation plan / ADRs
```

## Requirements

**Portable release exe**: Windows 10/11 x64 only; everything else is bundled.

**Building from source / development**:
- Windows 10/11, .NET 8 SDK
- JDK 17 and Android SDK build-tools (used when the portable `runtime.zip` is not embedded; `zipalign`/`aapt2`/`apksigner` all come from build-tools)
- Building a patched ZL2 yourself additionally needs Android SDK + Gradle (optional, see below)

## Download (portable releases)

Portable exes are published to [Releases](../../releases) by the repository owner as needed — download a single exe and double-click it, **no .NET/JDK/Android SDK installation required** (a trimmed JRE, zipalign/aapt2/apksigner are bundled and extracted on first run). You can also build them yourself with the "single-file publish" command below:

- `ZL2PackBundler.App.exe` — GUI (4-step wizard)
- `zl2packbundler.exe` — command line

Releases also include a **ZL2 APK with the bundled-modpack patch applied** (renamed at build time per the GPL additional terms; it can be used directly as the base APK, see [android/](android/)).

Windows 10/11 x64 only.

## Build & test (developers)

```bash
dotnet build ZL2PackBundler.sln -c Release
dotnet test ZL2PackBundler.sln -c Release      # 47 unit tests
bash scripts/integration-test.sh               # end-to-end: embed → sign → verify (Git Bash)

# Embed the portable runtime (otherwise dev builds fall back to the system JDK/Android SDK)
powershell -ExecutionPolicy Bypass -File scripts/build-bundled-runtime.ps1 `
  -BuildTools <sdk/build-tools/36.1.0> -JdkHome <jdk17>

# Single-file publish
dotnet publish src/ZL2PackBundler.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

The GitHub Actions workflow is manual-only (Actions → Run workflow); build outputs are uploaded as workflow artifacts and are **never published to Releases automatically**. Publishing portable exes to Releases is a manual step.

## Full packaging workflow

1. **Prepare the base APK (choose one)**:

   - **Official unmodified APK (recommended, zero code work)**: download the official APK from [ZalithLauncher2 Releases](https://github.com/ZalithLauncher/ZalithLauncher2/releases). When an official APK is detected, the tool automatically injects a built-in installer (manifest entry patch + injected dex); on first launch the installer sets up the modpack before handing over to the original launcher.
   - **Build a patched ZL2 yourself**: apply the changes in [android/](android/) to the ZalithLauncher2 source (copy files or `git apply`; see [android/README.md](android/README.md)), then run `./gradlew :ZalithLauncher:assembleDebug` (or `assembleRelease`).

2. **Prepare the modpack**:
   - Fully offline: a complete `.minecraft` directory containing `versions/`, `libraries/`, `assets/` (mods/configs/resource packs/saves can be added freely);
   - Or a modpack zip supported by ZL2 (MCBBS/Modrinth/CurseForge/MultiMC; first import may need network access to fetch dependencies).

3. **Embed the modpack and re-sign**:

   ```bash
   dotnet run --project src/ZL2PackBundler.Cli -- pack \
     --apk <base.apk> --pack <modpack dir or zip> --out <output.apk> \
     --auto-keystore        # for testing; use --keystore/--ks-pass/--key-alias for real distribution
   ```

   GUI: `dotnet run --project src/ZL2PackBundler.App`, follow the 4-step wizard (after selecting an APK it shows whether it is "official / already patched").

4. Install the output APK; the modpack installs automatically on first launch (progress UI) and is playable immediately.

## Command line

```
zl2packbundler analyze --pack <dir|zip>     # detect format + offline-completeness report
zl2packbundler pack --apk <a.apk> --pack <pack> --out <o.apk>
     [--name name] [--pack-id id] [--package new.package] [--app-name new.name] [--author author] [--icon icon.png]
     [--sdk SDK_DIR] [--force]
     [--keystore ks --ks-pass pw --key-alias a --key-pass pw] [--auto-keystore]
zl2packbundler gen-keystore --out ks.jks [--alias a] [--pass pw]
```

The Android SDK is auto-detected (env vars → last remembered dir → common paths → PATH ancestors → drive roots); if not found, pass `--sdk` once and it is remembered.

## Cross-platform contract: `assets/bundled_pack/`

- `manifest.json` (schema=1): `packId`/`packVersion`/`type`(`snapshot`|`packzip`)/`name`/`author`(optional)/`mcVersion`/`sizeBytes`/`sha256`
- `--author` is also written to `AndroidManifest.xml` as `meta-data zl2packbundler.author` and shown on the first-install progress screen (author info)
- `tool-info.json`: bundler tool info (tool name/version/pack time/repo/optional author), written on every pack; also written to `AndroidManifest.xml` as `meta-data zl2packbundler.tool/version`
- Where it is shown: patched builds display a "Bundled modpack tool" section in the launcher's Settings → About (reads tool-info.json); official-APK injection shows "由 ZL2PackBundler 打包 · 版本 X" (bundled by ZL2PackBundler · version X) in the first-install progress footer
- `pack.zip`: the modpack itself; the app verifies the raw-byte SHA-256 before writing into the game directory
- Marker file `.bundled_pack_version` (content `packId:packVersion`) prevents re-installation; a new pack version triggers re-install automatically

## Size guardrails

- `pack.zip` > 2 GB: warning (zip 32-bit boundary risk, slow install/extraction)
- Final APK > 4 GB: rejected (exceeds zip32 format capacity)

## Device acceptance checklist

1. Install the output APK → first launch shows the "Installing bundled modpack" progress screen (official path) or the splash "Bundled modpack" progress item (patched build path)
2. Patched build path: logs contain `BundledModpackManifest/INFO Bundled modpack manifest loaded` and `Bundled snapshot installed`
3. The MC version appears in the version list on the main screen
4. With airplane mode on, launch the game with an offline account and reach the title/world (fully offline)
5. A second cold start does not re-extract the pack
6. An original APK without a bundled pack behaves unchanged

## GPL-3.0 compliance

- This tool and the integration code under `android/` are GPL-3.0 ([LICENSE](LICENSE)); the Android patch files are derived from ZalithLauncher2 (Copyright (C) 2025 MovTery and contributors) and copyright notices are retained.
- When distributing a modified ZL2 with a bundled modpack, follow ZL2's GPLv3 additional terms: rename the launcher at build time in `ZalithLauncher/gradle.properties` (must not contain "ZalithLauncher"/"ZL"), mark it as an unofficial build on the splash screen, and keep copyright notices.
- By default the tool does not rename the app or change the icon; you may explicitly pass `--package` (package name), `--app-name` (app name) and `--icon` (launcher icon). Note: after renaming the package, file share/import features (FileProvider authority) may not match in-code constants — for production distribution, renaming at source-build time (`gradle.properties`) is still recommended.
- Icon replacement: `--icon <png|jpg|webp>` resizes the image to each density bucket's original dimensions and replaces the mipmap bitmaps (webp/png), rewrites the adaptive-icon XMLs to point their foreground at the replaced host bitmap `img_launcher` (the launcher logo at the top of Settings → About changes accordingly) and drops the monochrome child (Android 13+ themed icons no longer show the old icon). APKs with vector-only icons (no bitmap buckets and no host bitmap) are reported as skipped.

## FAQ

- **"Android SDK build-tools not found"**: pass `--sdk <SDK dir>` once (or pick it once in the GUI) and it is remembered; or set `ANDROID_HOME`.
- **Official-APK injection is fast and lossless**: it binary-patches AndroidManifest.xml and replaces zip entries directly — no apktool, no resource rebuild; the original APK's resources.arsc and all resource files are preserved byte-for-byte. You can also use an already-patched build to skip injection.
- **No progress bar / no version after install**: official path — check for the "Installing bundled modpack" screen; patched build path — confirm the log contains `Bundled modpack manifest loaded` (old tool versions wrote `type` with wrong casing; repack with the latest version).
- **Package too large**: see size guardrails; evaluate channel size limits (e.g. Play) for official distribution.

## Reference docs

- Design spec: [docs/specs/2026-08-30-bundled-modpack-apk-design.md](docs/specs/2026-08-30-bundled-modpack-apk-design.md)
- Implementation plan: [docs/plans/2026-08-30-bundled-modpack-apk.md](docs/plans/2026-08-30-bundled-modpack-apk.md)
- Contract ADR: [docs/adr/2026-08-30-bundled-pack-asset-contract.md](docs/adr/2026-08-30-bundled-pack-asset-contract.md)
