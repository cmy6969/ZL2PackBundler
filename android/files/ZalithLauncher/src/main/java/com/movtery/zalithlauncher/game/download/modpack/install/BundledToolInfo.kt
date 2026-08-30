/*
 * Zalith Launcher 2
 * Copyright (C) 2025 MovTery <movtery228@qq.com> and contributors
 * (GPL-3.0，见仓库 LICENSE)
 */
package com.movtery.zalithlauncher.game.download.modpack.install

import android.content.Context
import com.movtery.zalithlauncher.utils.GSON
import com.movtery.zalithlauncher.utils.logging.Logger

private const val TAG = "BundledToolInfo"

/** 打包工具信息（跨端契约，由 ZL2PackBundler 写入 assets/zl2packbundler/tool-info.json） */
data class BundledToolInfo(
    val tool: String? = null,
    val version: String? = null,
    val packedAt: String? = null,
    val author: String? = null,
    val repo: String? = null,
) {
    companion object {
        const val ASSET_PATH = "zl2packbundler/tool-info.json"

        /** 从 assets 读取打包工具信息；不存在返回 null（未内嵌整合包的普通构建）。 */
        fun load(context: Context): BundledToolInfo? {
            val json = runCatching {
                context.assets.open(ASSET_PATH).use { it.readBytes().decodeToString() }
            }.getOrElse {
                return null
            }
            return runCatching { GSON.fromJson(json, BundledToolInfo::class.java) }
                .onFailure { Logger.error(TAG, "Failed to parse bundled tool info", it) }
                .getOrNull()
        }
    }
}
