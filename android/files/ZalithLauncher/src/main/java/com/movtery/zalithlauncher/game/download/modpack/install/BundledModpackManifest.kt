/*
 * Zalith Launcher 2
 * Copyright (C) 2025 MovTery <movtery228@qq.com> and contributors
 * (GPL-3.0，见仓库 LICENSE)
 */
package com.movtery.zalithlauncher.game.download.modpack.install

import android.content.Context
import com.movtery.zalithlauncher.utils.GSON
import com.movtery.zalithlauncher.utils.logging.Logger
import java.io.File

private const val TAG = "BundledModpackManifest"

/** 内嵌整合包资产目录（跨端契约，schema=1） */
const val BUNDLED_PACK_ASSET_DIR = "bundled_pack"
const val BUNDLED_PACK_MANIFEST_ASSET = "$BUNDLED_PACK_ASSET_DIR/manifest.json"
const val BUNDLED_PACK_ZIP_ASSET = "$BUNDLED_PACK_ASSET_DIR/pack.zip"
const val BUNDLED_PACK_MARKER_FILE = ".bundled_pack_version"

data class BundledModpackManifest(
    val schema: Int = 0,
    val packId: String? = null,
    val packVersion: Long = -1L,
    val type: String? = null,
    val name: String? = null,
    val mcVersion: String? = null,
    val sizeBytes: Long = -1L,
    val sha256: String? = null,
) {
    companion object {
        const val SCHEMA = 1
        const val TYPE_SNAPSHOT = "snapshot"
        const val TYPE_PACKZIP = "packzip"

        /** 从 assets 读取并校验；不存在或非法返回 null（调用方视为无内嵌包）。 */
        fun load(context: Context): BundledModpackManifest? {
            val json = runCatching {
                context.assets.open(BUNDLED_PACK_MANIFEST_ASSET).use { it.readBytes().decodeToString() }
            }.getOrElse {
                Logger.debug(TAG, "No bundled modpack manifest in this APK", it)
                return null
            }
            val manifest = runCatching {
                GSON.fromJson(json, BundledModpackManifest::class.java)
            }.getOrElse {
                Logger.error(TAG, "Failed to parse bundled modpack manifest", it)
                return null
            }
            if (!manifest.validate()) {
                Logger.error(TAG, "Bundled modpack manifest validation failed: $manifest")
                return null
            }
            Logger.info(
                TAG,
                "Bundled modpack manifest loaded: packId=${manifest.packId}, type=${manifest.type}, size=${manifest.sizeBytes}"
            )
            return manifest
        }
    }

    /** 与 Windows 端 PackBundler 的 Validate() 规则一致。 */
    fun validate(): Boolean {
        if (schema != SCHEMA) return false
        if (packId.isNullOrBlank()) return false
        if (packVersion < 0) return false
        if (type != TYPE_SNAPSHOT && type != TYPE_PACKZIP) return false
        if (sizeBytes <= 0) return false
        val sha = sha256 ?: return false
        if (!sha.matches(Regex("^[0-9a-f]{64}$"))) return false
        if (type == TYPE_SNAPSHOT && mcVersion.isNullOrBlank()) return false
        return true
    }

    val isSnapshot: Boolean get() = type == TYPE_SNAPSHOT
    val markerContent: String get() = "$packId:$packVersion"

    fun markerMatches(file: File): Boolean =
        file.exists() && runCatching { file.readText() }.getOrNull() == markerContent
}
