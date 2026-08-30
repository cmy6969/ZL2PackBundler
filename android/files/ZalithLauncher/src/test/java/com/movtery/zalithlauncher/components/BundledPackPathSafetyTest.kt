package com.movtery.zalithlauncher.components

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class BundledPackPathSafetyTest {

    @Test
    fun normalPathsPass() {
        assertEquals("versions/1.20.1/1.20.1.jar", BundledPackPathSafety.sanitize("versions/1.20.1/1.20.1.jar"))
        assertEquals("mods/a.mod.jar", BundledPackPathSafety.sanitize("mods/a.mod.jar"))
        assertEquals("dir/", BundledPackPathSafety.sanitize("dir/"))
    }

    @Test
    fun parentTraversalRejected() {
        assertNull(BundledPackPathSafety.sanitize("../evil.txt"))
        assertNull(BundledPackPathSafety.sanitize("a/../../b.txt"))
        assertNull(BundledPackPathSafety.sanitize(".."))
    }

    @Test
    fun absoluteAndBackslashRejected() {
        assertNull(BundledPackPathSafety.sanitize("/etc/passwd"))
        assertNull(BundledPackPathSafety.sanitize("\\windows\\system32\\x"))
        assertNull(BundledPackPathSafety.sanitize("a\\b.txt"))
    }

    @Test
    fun emptySegmentsAndDriveRejected() {
        assertNull(BundledPackPathSafety.sanitize("a//b.txt"))
        assertNull(BundledPackPathSafety.sanitize(""))
        assertNull(BundledPackPathSafety.sanitize("C:/windows/x"))
    }
}
