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

# 伪造基础 APK（dex 含内嵌契约常量 → 模拟“已打补丁构建”，走直接嵌入路径）
python - <<'PY'
import zipfile, os
os.makedirs('.tmp/itest', exist_ok=True)
with zipfile.ZipFile('.tmp/itest/base.apk', 'w') as z:
    z.writestr('classes.dex', b'dex-bundled_pack/manifest.json-marker')
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
import json, hashlib
m = json.loads(manifest)
asset_sha = hashlib.sha256(z.read('assets/bundled_pack/pack.zip')).hexdigest()
assert asset_sha == m['sha256'], (asset_sha, m['sha256'])
print('内嵌资产原始字节哈希 == manifest.sha256: OK')
PY

SDK="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [ -z "$SDK" ]; then
  SDK=$(python - <<'PY'
import json, os
p = os.path.join(os.environ.get('APPDATA', ''), 'ZL2PackBundler', 'config.json')
try:
    with open(p, encoding='utf-8') as f:
        print(json.load(f).get('androidSdkDir', ''))
except Exception:
    pass
PY
)
fi
if [ -z "$SDK" ]; then SDK="$LOCALAPPDATA/Android/Sdk"; fi
BT=$(ls -d "$SDK"/build-tools/*/ | sort -V | tail -1)
java -jar "$BT"lib/apksigner.jar verify --verbose --print-certs --min-sdk-version 26 "$TMP/out.apk" | head -8
echo "INTEGRATION OK"
