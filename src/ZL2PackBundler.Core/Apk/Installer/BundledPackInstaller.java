package com.zl2packbundler.installer;

import android.app.Activity;
import android.content.ComponentName;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.StatFs;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.security.MessageDigest;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * ZL2PackBundler 注入安装器（官方原版 APK 的入口替代）。
 * 仅使用 Android 框架 API：首次启动安装 APK 内嵌整合包，然后把入口转交给原版启动器。
 * 组件名从 assets/zl2packbundler/installer-config.json 读取（由打包工具注入时写入），
 * 因此本类可预编译成静态 dex，无需在用户机器上编译。
 */
public class BundledPackInstaller extends Activity {
    private static final String CONFIG_ASSET = "zl2packbundler/installer-config.json";
    private static final String MANIFEST_ASSET = "bundled_pack/manifest.json";
    private static final String PACK_ASSET = "bundled_pack/pack.zip";
    private static final String MARKER_FILE = ".bundled_pack_version";
    private static final String IMPORT_FLAG_FILE = ".bundled_pack_import";
    private static final String TYPE_SNAPSHOT = "snapshot";

    private TextView statusView;
    private ProgressBar progressBar;
    private Button retryButton;
    private Button continueButton;
    private final Handler ui = new Handler(Looper.getMainLooper());

    private File gameDir;
    private Manifest manifest;
    private String launcherActivity = "";
    private String importAlias = "";
    private String manifestError = "";

    private static final class Manifest {
        int schema;
        String packId;
        long packVersion;
        String type;
        String name;
        String author;
        String sha256;
        long sizeBytes;

        boolean valid() {
            return schema == 1 && packId != null && !packId.isEmpty()
                    && packVersion >= 0 && sha256 != null && sha256.length() == 64
                    && sizeBytes > 0 && (TYPE_SNAPSHOT.equals(type) || "packzip".equals(type));
        }

        String markerContent() {
            return packId + ":" + packVersion;
        }
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        File external = getExternalFilesDir(null);
        gameDir = new File(external != null ? external : getFilesDir(), ".minecraft");

        readConfig();
        manifest = readManifest();

        if (launcherActivity.isEmpty() || manifest == null || !manifest.valid()) {
            String reason = "配置缺失";
            if (!launcherActivity.isEmpty() && manifest != null) {
                reason = "manifest 校验失败 (schema=" + manifest.schema + ", type=" + manifest.type
                        + ", packId=" + manifest.packId + ", sha256=" + manifest.sha256
                        + ", sizeBytes=" + manifest.sizeBytes + ")";
            } else if (!launcherActivity.isEmpty()) {
                reason = "整合包 manifest 缺失或无法解析（" + manifestError + "）";
            }
            showConfigError(reason);
            return;
        }

        File marker = new File(gameDir, MARKER_FILE);
        if (marker.isFile() && manifest.markerContent().equals(readText(marker))) {
            forwardToLauncher();
            return;
        }

        buildUi();
        startInstall();
    }

    private void showConfigError(String reason) {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        int pad = dp(24);
        root.setPadding(pad, pad, pad, pad);
        TextView title = new TextView(this);
        title.setTextSize(18f);
        title.setText("内嵌整合包配置缺失或损坏");
        root.addView(title);
        TextView detail = new TextView(this);
        detail.setTextSize(13f);
        detail.setText("原因：" + reason);
        root.addView(detail);
        TextView hint = new TextView(this);
        hint.setTextSize(13f);
        hint.setText("请使用最新版 ZL2PackBundler 重新打包此 APK。");
        root.addView(hint);
        setContentView(root);
    }

    private void buildUi() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER);
        int pad = dp(24);
        root.setPadding(pad, pad, pad, pad);

        TextView title = new TextView(this);
        title.setTextSize(20f);
        title.setText("正在安装内嵌整合包");
        root.addView(title);

        TextView name = new TextView(this);
        name.setTextSize(14f);
        name.setText(manifest.name != null ? manifest.name : manifest.packId);
        root.addView(name);

        if (manifest.author != null && !manifest.author.isEmpty()) {
            TextView author = new TextView(this);
            author.setTextSize(13f);
            author.setTextColor(0xFF6B7280);
            author.setText("作者：" + manifest.author);
            root.addView(author);
        }

        progressBar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        progressBar.setMax(1000);
        root.addView(progressBar, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, dp(18)));

        statusView = new TextView(this);
        statusView.setTextSize(13f);
        statusView.setText("准备中…");
        root.addView(statusView);

        LinearLayout buttons = new LinearLayout(this);
        buttons.setOrientation(LinearLayout.HORIZONTAL);
        retryButton = new Button(this);
        retryButton.setText("重试");
        retryButton.setEnabled(false);
        retryButton.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                retryButton.setEnabled(false);
                startInstall();
            }
        });
        continueButton = new Button(this);
        continueButton.setText("直接进入启动器");
        continueButton.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { forwardToLauncher(); }
        });
        buttons.addView(retryButton);
        buttons.addView(continueButton);
        root.addView(buttons);

        setContentView(root);
    }

    private void startInstall() {
        new Thread(new Runnable() {
            @Override public void run() {
                try {
                    setStatus("校验整合包…");
                    File staged = stageAndVerify();
                    if (TYPE_SNAPSHOT.equals(manifest.type)) {
                        setStatus("解压整合包…");
                        extract(staged, gameDir);
                        writeText(new File(gameDir, MARKER_FILE), manifest.markerContent());
                        forwardToLauncher();
                    } else {
                        handlePackZip(staged);
                    }
                } catch (final Throwable t) {
                    ui.post(new Runnable() {
                        @Override public void run() {
                            statusView.setText("安装失败：" + t.getMessage());
                            retryButton.setEnabled(true);
                        }
                    });
                }
            }
        }, "bundled-pack-installer").start();
    }

    private File stageAndVerify() throws Exception {
        File dir = new File(getCacheDir(), "bundled");
        dir.mkdirs();
        File target = new File(dir, manifest.packId + ".zip");
        MessageDigest digest = MessageDigest.getInstance("SHA-256");
        InputStream in = getAssets().open(PACK_ASSET);
        OutputStream out = new FileOutputStream(target);
        byte[] buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = in.read(buffer)) >= 0) {
            out.write(buffer, 0, read);
            digest.update(buffer, 0, read);
            total += read;
        }
        in.close();
        out.close();
        String actual = toHex(digest.digest());
        if (!actual.equals(manifest.sha256)) {
            target.delete();
            throw new IllegalStateException("整合包校验失败（SHA-256 不一致）");
        }
        checkFreeSpace(gameDir, manifest.sizeBytes);
        return target;
    }

    private void extract(File zipFile, File targetDir) throws Exception {
        ZipInputStream zip = new ZipInputStream(new FileInputStream(zipFile));
        byte[] buffer = new byte[1 << 16];
        ZipEntry entry;
        long done = 0;
        while ((entry = zip.getNextEntry()) != null) {
            String name = safeName(entry.getName());
            if (name == null) {
                continue; // 拒绝危险条目
            }
            if (MARKER_FILE.equals(name)) {
                continue;
            }
            File target = new File(targetDir, name);
            if (entry.isDirectory()) {
                target.mkdirs();
                continue;
            }
            target.getParentFile().mkdirs();
            FileOutputStream out = new FileOutputStream(target);
            int read;
            while ((read = zip.read(buffer)) >= 0) {
                out.write(buffer, 0, read);
                done += read;
                if (done % (32L << 20) == 0) {
                    final long mb = done >> 20;
                    ui.post(new Runnable() {
                        @Override public void run() {
                            statusView.setText("解压整合包… " + mb + " MB");
                        }
                    });
                }
            }
            out.close();
        }
        zip.close();
    }

    private void handlePackZip(final File staged) {
        ui.post(new Runnable() {
            @Override public void run() {
                try {
                    File flag = new File(gameDir, IMPORT_FLAG_FILE);
                    int versions = countVersions();
                    if (flag.isFile() && versions > parseIntSafe(readText(flag), -1)) {
                        // 上次转交的导入已经完成
                        writeText(new File(gameDir, MARKER_FILE), manifest.markerContent());
                        flag.delete();
                        forwardToLauncher();
                        return;
                    }
                    writeText(flag, String.valueOf(versions));
                    if (importAlias.isEmpty()) {
                        forwardToLauncher(); // 该 APK 没有整合包导入入口，只能让用户手动导入
                        return;
                    }
                    Intent intent = new Intent(Intent.ACTION_VIEW);
                    intent.setDataAndType(Uri.fromFile(staged), "application/zip");
                    intent.setComponent(new ComponentName(getPackageName(), importAlias));
                    intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    startActivity(intent);
                    finish();
                } catch (Throwable t) {
                    statusView.setText("导入启动失败：" + t.getMessage());
                    retryButton.setEnabled(true);
                }
            }
        });
    }

    private int countVersions() {
        File versionsDir = new File(gameDir, "versions");
        int count = 0;
        File[] subs = versionsDir.listFiles();
        if (subs == null) return 0;
        for (File sub : subs) {
            if (!sub.isDirectory()) continue;
            String name = sub.getName();
            if (new File(sub, name + ".json").isFile()) count++;
        }
        return count;
    }

    private void checkFreeSpace(File dir, long packBytes) {
        // 部分 ROM 对“尚不存在”的目录做 StatFs 会抛 Invalid path——先创建目录再检查
        dir.mkdirs();
        long free = -1;
        try {
            free = new StatFs(dir.getAbsolutePath()).getAvailableBytes();
        } catch (Throwable t) {
            try {
                File parent = dir.getParentFile();
                free = new StatFs(parent != null ? parent.getAbsolutePath() : "/").getAvailableBytes();
            } catch (Throwable ignored) {
                free = -1; // 仍失败则跳过空间检查（解压阶段 IO 异常会自然暴露）
            }
        }
        if (free >= 0 && free < packBytes * 3 / 2) {
            throw new IllegalStateException("磁盘空间不足");
        }
    }

    private void forwardToLauncher() {
        Intent intent = new Intent(Intent.ACTION_MAIN);
        intent.addCategory(Intent.CATEGORY_LAUNCHER);
        intent.setComponent(new ComponentName(getPackageName(), launcherActivity));
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        startActivity(intent);
        finish();
    }

    private void setStatus(final String text) {
        ui.post(new Runnable() {
            @Override public void run() { statusView.setText(text); }
        });
    }

    private void readConfig() {
        try {
            String json = readAsset(CONFIG_ASSET);
            if (json == null) return;
            launcherActivity = readString(json, "launcher");
            importAlias = readString(json, "importAlias");
            if (launcherActivity == null) launcherActivity = "";
            if (importAlias == null) importAlias = "";
        } catch (Throwable ignored) {
        }
    }

    private Manifest readManifest() {
        try {
            String json = readAsset(MANIFEST_ASSET);
            if (json == null) {
                manifestError = "assets 打开失败（文件不存在或不可读）";
                return null;
            }
            Manifest m = new Manifest();
            m.schema = parseIntSafe(readString(json, "schema"), 0);
            m.packId = readString(json, "packId");
            m.packVersion = parseLongSafe(readString(json, "packVersion"), -1);
            m.type = readString(json, "type");
            m.name = readString(json, "name");
            m.author = readString(json, "author");
            m.sha256 = readString(json, "sha256");
            m.sizeBytes = parseLongSafe(readString(json, "sizeBytes"), -1);
            return m;
        } catch (Throwable t) {
            manifestError = "解析异常：" + t;
            return null;
        }
    }

    private String readAsset(String path) {
        try {
            InputStream in = getAssets().open(path);
            StringBuilder sb = new StringBuilder();
            byte[] buffer = new byte[1 << 13];
            int read;
            while ((read = in.read(buffer)) >= 0) {
                sb.append(new String(buffer, 0, read, "UTF-8"));
            }
            in.close();
            return sb.toString();
        } catch (Throwable t) {
            return null;
        }
    }

    /** 极简 JSON 字段读取（字符串与数字都支持；不做完整转义，避免正则/依赖）。 */
    static String readString(String json, String key) {
        String marker = "\"" + key + "\"";
        int keyIdx = json.indexOf(marker);
        if (keyIdx < 0) return null;
        int colon = json.indexOf(':', keyIdx + marker.length());
        if (colon < 0) return null;
        int p = colon + 1;
        while (p < json.length() && (json.charAt(p) == ' ' || json.charAt(p) == '\r'
                || json.charAt(p) == '\n' || json.charAt(p) == '\t')) p++;
        if (p >= json.length()) return null;
        if (json.charAt(p) == '"') {
            StringBuilder sb = new StringBuilder();
            for (int i = p + 1; i < json.length(); i++) {
                char c = json.charAt(i);
                if (c == '\\' && i + 1 < json.length()) { // 转义
                    char n = json.charAt(i + 1);
                    if (n == '"' || n == '\\') { sb.append(n); i++; continue; }
                }
                if (c == '"') break;
                sb.append(c);
            }
            return sb.toString();
        }
        // 数字/布尔等裸值：取到逗号或花括号为止
        int end = p;
        while (end < json.length() && json.charAt(end) != ',' && json.charAt(end) != '}'
                && json.charAt(end) != '\r' && json.charAt(end) != '\n') end++;
        String token = json.substring(p, end).trim();
        return token.isEmpty() ? null : token;
    }

    private static int parseIntSafe(String s, int def) {
        try { return s == null ? def : Integer.parseInt(s.trim()); } catch (Throwable t) { return def; }
    }

    private static long parseLongSafe(String s, long def) {
        try { return s == null ? def : Long.parseLong(s.trim()); } catch (Throwable t) { return def; }
    }

    private static String readText(File f) {
        try {
            InputStream in = new FileInputStream(f);
            byte[] data = new byte[(int) f.length()];
            int off = 0;
            while (off < data.length) {
                int r = in.read(data, off, data.length - off);
                if (r < 0) break;
                off += r;
            }
            in.close();
            return new String(data, 0, off, "UTF-8");
        } catch (Throwable t) {
            return null;
        }
    }

    private static void writeText(File f, String text) {
        try {
            f.getParentFile().mkdirs();
            FileOutputStream out = new FileOutputStream(f);
            out.write(text.getBytes("UTF-8"));
            out.close();
        } catch (Throwable ignored) {
        }
    }

    private static String toHex(byte[] bytes) {
        StringBuilder sb = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) {
            sb.append(Character.forDigit((b >> 4) & 0xF, 16));
            sb.append(Character.forDigit(b & 0xF, 16));
        }
        return sb.toString();
    }

    /** 路径穿越防护：返回合法相对路径，非法返回 null。 */
    static String safeName(String name) {
        if (name == null || name.isEmpty()) return null;
        if (name.startsWith("/") || name.startsWith("\\")) return null;
        if (name.contains("\\")) return null;
        String[] parts = name.split("/");
        for (int i = 0; i < parts.length; i++) {
            boolean last = i == parts.length - 1;
            if ("..".equals(parts[i])) return null;
            if (parts[i].isEmpty() && !last) return null;
            if (parts[i].contains(":")) return null;
        }
        return name;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }
}
