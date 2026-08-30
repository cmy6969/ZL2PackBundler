# 生成便携运行时 zip（jre + zipalign + aapt2 + apksigner）
# 用法（PowerShell）：
#   powershell -ExecutionPolicy Bypass -File scripts/build-bundled-runtime.ps1 `
#     -BuildTools <sdk/build-tools/36.1.0> -JdkHome <jdk17>
# 输出：src/ZL2PackBundler.Core/Bundled/runtime.zip（gitignored）
param(
    [Parameter(Mandatory = $true)][string]$BuildTools,
    [Parameter(Mandatory = $true)][string]$JdkHome
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $env:TEMP "zl2pb-runtime-stage"
$outDir = Join-Path $root "src/ZL2PackBundler.Core/Bundled"
$outZip = Join-Path $outDir "runtime.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

# 1) 精简 JRE（apktool 依赖的模块）
& (Join-Path $JdkHome "bin/jlink.exe") `
    --add-modules java.base,java.desktop,java.logging,java.naming,jdk.zipfs,jdk.crypto.ec `
    --output (Join-Path $stage "jre") `
    --strip-debug --no-man-pages --no-header-files --compress=2
if ($LASTEXITCODE -ne 0) { throw "jlink failed" }

# 2) 工具二进制
Copy-Item (Join-Path $BuildTools "zipalign.exe") $stage
Copy-Item (Join-Path $BuildTools "aapt2.exe") $stage
New-Item -ItemType Directory -Path (Join-Path $stage "lib") | Out-Null
Copy-Item (Join-Path $BuildTools "lib/apksigner.jar") (Join-Path $stage "lib/apksigner.jar")

# 3) 打包
if (Test-Path $outZip) { Remove-Item $outZip -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $outZip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
Write-Host "runtime.zip -> $outZip"
