package com.movtery.zalithlauncher.game.download.modpack.install

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class BundledModpackManifestTest {

    private fun valid() = BundledModpackManifest(
        schema = 1,
        packId = "test-pack",
        packVersion = 1,
        type = "snapshot",
        name = "Test",
        mcVersion = "1.20.1",
        sizeBytes = 1024,
        sha256 = "a".repeat(64)
    )

    @Test
    fun validManifestPasses() {
        assertTrue(valid().validate())
    }

    @Test
    fun wrongSchemaFails() {
        assertFalse(valid().copy(schema = 2).validate())
    }

    @Test
    fun missingPackIdFails() {
        assertFalse(valid().copy(packId = " ").validate())
    }

    @Test
    fun snapshotRequiresMcVersion() {
        assertFalse(valid().copy(mcVersion = null).validate())
    }

    @Test
    fun packZipWithoutMcVersionPasses() {
        assertTrue(valid().copy(type = "packzip", mcVersion = null).validate())
    }

    @Test
    fun badShaFails() {
        assertFalse(valid().copy(sha256 = "zzz").validate())
    }

    @Test
    fun markerContentIsPackIdColonVersion() {
        assertEquals("test-pack:1", valid().markerContent)
    }

    @Test
    fun gsonParsesCamelCaseJson() {
        val json = """
            {
              "schema": 1,
              "packId": "p1",
              "packVersion": 3,
              "type": "snapshot",
              "name": "n",
              "mcVersion": "1.20.1",
              "sizeBytes": 42,
              "sha256": "${"a".repeat(64)}"
            }
        """.trimIndent()
        val parsed = com.movtery.zalithlauncher.utils.GSON.fromJson(json, BundledModpackManifest::class.java)
        assertTrue(parsed.validate())
        assertEquals(3L, parsed.packVersion)
    }
}
