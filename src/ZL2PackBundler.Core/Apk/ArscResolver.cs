using System.Text;

namespace ZL2PackBundler.Core.Apk;

/// <summary>
/// 极简 resources.arsc 读取器：把资源 ID（如 android:icon=@0x7f0f0000）解析为文件路径列表
/// （每个 config 一条，如 res/mipmap-xxxhdpi/ic_launcher.webp）。仅覆盖打包工具需要的子集：
/// 表头 + 全局字符串池 + package + type 块 + 简单/引用条目（复杂条目与紧凑条目按 AOSP 布局处理）。
/// </summary>
public static class ArscResolver
{
    private const ushort ResStringPool = 0x0001;
    private const ushort ResTablePackage = 0x0200;
    private const ushort ResTableTypeSpec = 0x0202;
    private const ushort ResTableType = 0x0201;
    private const uint NoEntry = 0xFFFFFFFF;
    private const uint ResStringPoolUtf8 = 0x00000100;
    private const byte TypeString = 0x03;
    private const byte TypeReference = 0x01;
    private const ushort FlagComplex = 0x0001;
    private const ushort FlagCompact = 0x0008;

    /// <summary>解析资源 ID → 文件路径（去重，保持 config 顺序）。别名（REFERENCE）自动跟随。</summary>
    public static List<string> ResolveFilePaths(byte[] arsc, uint resourceId, int maxDepth = 8)
    {
        var globalPool = ReadGlobalStringPool(arsc);
        var acc = new List<string>();
        Resolve(arsc, globalPool, resourceId, acc, new HashSet<uint>(), maxDepth);
        return acc;
    }

    private static void Resolve(byte[] d, List<string> globalPool, uint id,
        List<string> acc, HashSet<uint> seen, int depth)
    {
        if (depth <= 0 || !seen.Add(id)) return;
        var packageId = (byte)(id >> 24);
        var typeId = (byte)(id >> 16);
        var entryId = (ushort)(id & 0xFFFF);

        var off = 12; // 表头 12 字节后是全局字符串池
        off += (int)ReadU32(d, off + 4); // 跳过全局字符串池（按 chunk size）
        while (off + 8 <= d.Length)
        {
            var chunkType = ReadU16(d, off);
            if (chunkType != ResTablePackage) break;
            var pkgSize = (int)ReadU32(d, off + 4);
            var pkgId = ReadU32(d, off + 8);
            if (pkgId == packageId)
            {
                ResolveInPackage(d, globalPool, off, pkgSize, typeId, entryId, acc, seen, depth - 1);
                return;
            }
            off += pkgSize;
        }
    }

    private static void ResolveInPackage(byte[] d, List<string> globalPool, int pkg, int pkgSize,
        byte typeId, ushort entryId, List<string> acc, HashSet<uint> seen, int depth)
    {
        // package 名：固定 uint16_t name[128]（无长度前缀）；nameBytes = headerSize - 8(头) - 4(id) - 20(5×u32)
        var headerSize = ReadU16(d, pkg + 2);
        var nameBytes = headerSize - 8 - 4 - 20;
        // typeStrings / keyStrings 偏移紧随其后（相对 package 起点）
        var typeStringsOff = (int)ReadU32(d, pkg + 12 + nameBytes);
        var keyStringsOff = (int)ReadU32(d, pkg + 12 + nameBytes + 8);
        var typeNames = ReadStringPool(d, pkg + typeStringsOff);
        var keyNames = ReadStringPool(d, pkg + keyStringsOff);
        var off = pkg + headerSize;
        var end = pkg + pkgSize;
        while (off + 20 <= end)
        {
            var chunkType = ReadU16(d, off);
            var chunkSize = (int)ReadU32(d, off + 4);
            if (chunkSize < 8) break;
            if (chunkType == ResStringPool)
            {
                off += chunkSize; // 跳过类型名/键名字符串池
                continue;
            }
            if (chunkType == ResTableType && ReadU8(d, off + 8) == typeId)
            {
                ResolveInType(d, globalPool, keyNames, off, entryId, acc, seen, depth);
            }
            else if (chunkType is not (ResTableType or ResTableTypeSpec))
            {
                break; // package 内出现未知块，停止扫描
            }
            off += chunkSize;
        }
    }

    private static void ResolveInType(byte[] d, List<string> globalPool, List<string> keyNames,
        int chunk, ushort entryId, List<string> acc, HashSet<uint> seen, int depth)
    {
        var entryCount = (int)ReadU32(d, chunk + 12);
        if (entryId >= entryCount) return;
        var entriesStart = (int)ReadU32(d, chunk + 16);
        var configSize = ReadU16(d, chunk + 2) - 20; // headerSize - (头8 + id/flags/reserved/entryCount/entriesStart 12)
        var offsetsBase = chunk + 20 + configSize;
        var entryOff = ReadU32(d, offsetsBase + entryId * 4);
        if (entryOff == NoEntry) return;
        var entry = chunk + entriesStart + (int)entryOff;

        var size = ReadU16(d, entry);
        var flags = ReadU16(d, entry + 2);
        if ((flags & FlagComplex) != 0) return; // 复杂条目（mipmap 一般不会出现）

        byte dataType;
        uint data;
        if ((flags & FlagCompact) != 0)
        {
            // 紧凑条目：无 keyIndex 字段，key = 自身 entryId，value 紧随 size/flags（8 字节 Res_value）
            dataType = ReadU8(d, entry + 4 + 3);
            data = ReadU32(d, entry + 4 + 4);
        }
        else
        {
            if (size < 8) return;
            dataType = ReadU8(d, entry + 8 + 3);
            data = ReadU32(d, entry + 8 + 4);
        }

        switch (dataType)
        {
            case TypeString:
                if (data < globalPool.Count) acc.Add(globalPool[(int)data]);
                break;
            case TypeReference:
                Resolve(d, globalPool, data, acc, seen, depth);
                break;
        }
    }

    private static List<string> ReadGlobalStringPool(byte[] d)
    {
        var pool = ReadStringPool(d, 12);
        return pool ?? throw new InvalidDataException("resources.arsc 缺少全局字符串池。");
    }

    private static List<string>? ReadStringPool(byte[] d, int start)
    {
        if (start + 28 > d.Length || ReadU16(d, start) != ResStringPool) return null;
        var size = (int)ReadU32(d, start + 4);
        var count = (int)ReadU32(d, start + 8);
        var flags = ReadU32(d, start + 16);
        var stringsStart = (int)ReadU32(d, start + 20);
        var utf8 = (flags & ResStringPoolUtf8) != 0;
        var offsets = new List<uint>();
        for (var i = 0; i < count; i++) offsets.Add(ReadU32(d, start + 28 + i * 4));
        var strings = new List<string>();
        foreach (var rel in offsets)
        {
            var p = start + stringsStart + (int)rel;
            strings.Add(utf8 ? ReadUtf8String(d, p) : ReadUtf16String(d, p));
        }
        return strings;
    }

    private static string ReadUtf16String(byte[] d, int p)
    {
        var len16 = ReadU16(d, p);
        int strLen;
        var q = p + 2;
        if ((len16 & 0x8000) != 0)
        {
            strLen = (int)(ReadU32(d, q) & 0x7FFFFFFF);
            q += 4;
        }
        else
        {
            strLen = len16;
        }
        var sb = new StringBuilder(strLen);
        for (var i = 0; i < strLen && q + 1 < d.Length; i++, q += 2)
            sb.Append((char)ReadU16(d, q));
        return sb.ToString();
    }

    /// <summary>
    /// aapt2 的 UTF-8 池字符串编码：[utf16长度(1-2字节)][utf8长度(1-2字节)][数据][NUL]。
    /// 纯 ASCII 时两个长度相等（看起来像重复字节），非 ASCII 时 utf16 &lt; utf8。
    /// </summary>
    private static string ReadUtf8String(byte[] d, int p)
    {
        var (_, q) = ReadPoolLength(d, p);
        var (utf8Len, dataStart) = ReadPoolLength(d, q);
        return Encoding.UTF8.GetString(d, dataStart, Math.Min(utf8Len, d.Length - dataStart));
    }

    private static (int Len, int Next) ReadPoolLength(byte[] d, int p)
    {
        var b0 = d[p];
        if ((b0 & 0x80) != 0) return (((b0 & 0x7F) << 8) | d[p + 1], p + 2);
        return (b0, p + 1);
    }

    private static ushort ReadU16(byte[] d, int off) => (ushort)(d[off] | (d[off + 1] << 8));
    private static byte ReadU8(byte[] d, int off) => d[off];
    private static uint ReadU32(byte[] d, int off) =>
        (uint)(d[off] | (d[off + 1] << 8) | (d[off + 2] << 16) | (d[off + 3] << 24));
}
