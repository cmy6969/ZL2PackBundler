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
import java.io.FileInputStream
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

        // 先把 pack.zip 原样复制到缓存并校验原始字节 sha256（与工具端 ComputeSha256 口径一致），
        // 校验通过前不写任何游戏文件，损坏的包不会留下半成品。
        val staged = stageAndVerify(manifest)
        when {
            manifest.isSnapshot -> {
                extractSnapshot(staged, targetDir)
                runCatching { staged.delete() }
                markerFile.parentFile?.mkdirs()
                markerFile.writeText(manifest.markerContent)
                VersionsManager.refresh(TAG)
                Logger.info(TAG, "Bundled snapshot installed: ${manifest.markerContent}")
            }
            else -> {
                pendingImportFile = staged // 不写标记：导入成功后由 MainActivity 侧写入
                Logger.info(TAG, "Bundled packzip staged for import: ${staged.absolutePath}")
            }
        }
    }

    private fun checkFreeSpace(dir: File, packBytes: Long) {
        val free = StatFs(dir.absolutePath).availableBytes
        val required = packBytes * 3 / 2
        if (free < required) {
            throw IllegalStateException("Not enough free space for bundled modpack: need $required bytes, have $free")
        }
    }

    /** 把 assets 中的 pack.zip 复制到缓存文件，并校验其原始字节的 SHA-256。 */
    private fun stageAndVerify(manifest: BundledModpackManifest): File {
        val dir = File(PathManager.DIR_CACHE_MODPACK_DOWNLOADER, "bundled").apply { mkdirs() }
        val target = File(dir, "${manifest.packId}.zip")
        val digest = MessageDigest.getInstance("SHA-256")
        var totalBytes = 0L
        context.assets.open(BUNDLED_PACK_ZIP_ASSET).use { input ->
            target.outputStream().use { out ->
                val buffer = ByteArray(1 shl 16)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    out.write(buffer, 0, read)
                    digest.update(buffer, 0, read)
                    totalBytes += read
                    if (totalBytes % (64L shl 20) == 0L) {
                        updateMessage("Verifying bundled modpack... ${totalBytes shr 20} MB")
                    }
                }
            }
        }
        val actual = digest.digest().joinToString("") { "%02x".format(it) }
        if (actual != manifest.sha256) {
            runCatching { target.delete() }
            throw IllegalStateException(
                "Bundled pack checksum mismatch: expected ${manifest.sha256}, got $actual"
            )
        }
        return target
    }

    /** 从已校验的缓存 zip 解包到游戏目录（覆盖合并、幂等；逐条目路径穿越防护）。 */
    private fun extractSnapshot(zipFile: File, targetDir: File) {
        ZipInputStream(FileInputStream(zipFile)).use { zip ->
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
                    var entryBytes = 0L
                    while (true) {
                        val read = zip.read(buffer)
                        if (read < 0) break
                        out.write(buffer, 0, read)
                        entryBytes += read
                        if (entryBytes % (64L shl 20) == 0L) {
                            updateMessage("Extracting ${safeName}... ${entryBytes shr 20} MB")
                        }
                    }
                }
            }
        }
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
