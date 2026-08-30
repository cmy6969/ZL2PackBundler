using System.Text;
using Xunit;
using ZL2PackBundler.Core.Apk;

namespace ZL2PackBundler.Core.Tests;

/// <summary>用程序构造的最小 resources.arsc 验证解析器（结构按 AOSP ResourceTypes.h）。</summary>
public class ArscResolverTests
{
    [Fact]
    public void ResolvesIconIdsToFilePathsAndFollowsAliases()
    {
        var arsc = BuildArsc();

        var icon = ArscResolver.ResolveFilePaths(arsc, 0x7f0f0000);
        Assert.Equal(new[]
        {
            "res/mipmap-anydpi/ic_launcher.xml",
            "res/mipmap-xhdpi/ic_launcher.webp"
        }, icon);

        var round = ArscResolver.ResolveFilePaths(arsc, 0x7f0f0001);
        Assert.Equal(new[]
        {
            "res/mipmap-anydpi/ic_launcher_round.xml",
            "res/mipmap-mdpi/ic_launcher_round.webp"
        }, round);

        // drawable/ic_alias 是 REFERENCE 别名 → 跟随到 ic_launcher 的路径
        var alias = ArscResolver.ResolveFilePaths(arsc, 0x7f100000);
        Assert.Equal(icon, alias);

        // 不存在的条目 / 类型
        Assert.Empty(ArscResolver.ResolveFilePaths(arsc, 0x7f0f0002));
        Assert.Empty(ArscResolver.ResolveFilePaths(arsc, 0x7f200000));
    }

    [Fact]
    public void MissingEntryReturnsEmpty()
    {
        var arsc = BuildArsc();
        // entryId 超出 entryCount
        Assert.Empty(ArscResolver.ResolveFilePaths(arsc, 0x7f0f00ff));
    }

    /// <summary>
    /// 构造：全局池 5 条路径；包 0x7f；type 0x0f(mipmap) 两个 config 块 + type 0x10(drawable) 别名；
    /// 包名区 256 字节 UTF-16、headerSize 288。
    /// </summary>
    internal static byte[] BuildArsc()
    {
        var globalStrings = new[]
        {
            "res/mipmap-anydpi/ic_launcher.xml",        // 0
            "res/mipmap-xhdpi/ic_launcher.webp",        // 1
            "res/mipmap-anydpi/ic_launcher_round.xml",  // 2
            "res/mipmap-mdpi/ic_launcher_round.webp",   // 3
        };

        var typeStrings = new[] { "mipmap", "drawable" };
        var keyStrings = new[] { "ic_launcher", "ic_launcher_round", "ic_alias" };

        var globalPool = BuildUtf8StringPool(globalStrings);
        var typePool = BuildUtf8StringPool(typeStrings);
        var keyPool = BuildUtf8StringPool(keyStrings);

        // type chunk 0x0f，config A：entry0→global[0]（anydpi xml），entry1→global[2]
        var typeA = BuildTypeChunk(0x0f, 2, new[] { (0, (byte)0x03, (uint)0), (1, (byte)0x03, (uint)2) });
        // type chunk 0x0f，config B：entry0→global[1]（xhdpi webp），entry1→global[3]
        var typeB = BuildTypeChunk(0x0f, 2, new[] { (0, (byte)0x03, (uint)1), (1, (byte)0x03, (uint)3) });
        // type chunk 0x10（drawable）：entry0（ic_alias）→ REFERENCE 0x7f0f0000
        var typeAlias = BuildTypeChunk(0x10, 1, new[] { (0, (byte)0x01, 0x7f0f0000u) });

        const int packageHeaderSize = 8 + 4 + 256 + 20; // 288
        var packageName = new byte[256];
        var packageBody = new List<byte>();
        packageBody.AddRange(BitConverter.GetBytes((ushort)0x0200));
        packageBody.AddRange(BitConverter.GetBytes((ushort)packageHeaderSize));
        packageBody.AddRange(new byte[4]); // size 占位
        packageBody.AddRange(BitConverter.GetBytes(0x7fu)); // id
        packageBody.AddRange(packageName);
        // typeStrings / lastPublicType / keyStrings / lastPublicKey / typeIdOffset
        var typeStringsOff = (uint)(packageHeaderSize + typeA.Length + typeB.Length + typeAlias.Length);
        var keyStringsOff = typeStringsOff + (uint)typePool.Length;
        packageBody.AddRange(BitConverter.GetBytes(typeStringsOff));
        packageBody.AddRange(BitConverter.GetBytes(0u));
        packageBody.AddRange(BitConverter.GetBytes(keyStringsOff));
        packageBody.AddRange(BitConverter.GetBytes(0u));
        packageBody.AddRange(BitConverter.GetBytes(0u));
        packageBody.AddRange(typeA);
        packageBody.AddRange(typeB);
        packageBody.AddRange(typeAlias);
        packageBody.AddRange(typePool);
        packageBody.AddRange(keyPool);
        OverwriteU32(packageBody, 4, (uint)packageBody.Count);

        var table = new List<byte>();
        table.AddRange(BitConverter.GetBytes((ushort)0x0002)); // RES_TABLE_TYPE
        table.AddRange(BitConverter.GetBytes((ushort)12));     // headerSize
        table.AddRange(new byte[4]);                           // size 占位
        table.AddRange(BitConverter.GetBytes(1u));             // packageCount
        table.AddRange(globalPool);
        table.AddRange(packageBody);
        OverwriteU32(table, 4, (uint)table.Count);
        return table.ToArray();
    }

    private static byte[] BuildUtf8StringPool(IReadOnlyList<string> strings)
    {
        var offsets = new List<uint>();
        var data = new List<byte>();
        foreach (var s in strings)
        {
            offsets.Add((uint)data.Count);
            var bytes = Encoding.UTF8.GetBytes(s);
            var utf16Len = s.Length; // 测试字符串均为 ASCII：utf16 长度 == 字符数 == utf8 字节数
            WritePoolLength(data, utf16Len);
            WritePoolLength(data, bytes.Length);
            data.AddRange(bytes);
            data.Add(0);
        }
        while (data.Count % 4 != 0) data.Add(0);

        var header = 28 + 4 * strings.Count;
        var ms = new List<byte>();
        ms.AddRange(BitConverter.GetBytes((ushort)0x0001));
        ms.AddRange(BitConverter.GetBytes((ushort)28));
        ms.AddRange(BitConverter.GetBytes((uint)(header + data.Count)));
        ms.AddRange(BitConverter.GetBytes((uint)strings.Count));
        ms.AddRange(BitConverter.GetBytes(0u)); // styleCount
        ms.AddRange(BitConverter.GetBytes(0x00000100u)); // UTF-8
        ms.AddRange(BitConverter.GetBytes((uint)header));
        ms.AddRange(BitConverter.GetBytes(0u)); // stylesStart
        foreach (var off in offsets) ms.AddRange(BitConverter.GetBytes(off));
        ms.AddRange(data);
        return ms.ToArray();
    }

    /// <summary>typeId 的 TYPE chunk（单个 config，config 64 字节；entry 值为字符串/引用）。</summary>
    private static byte[] BuildTypeChunk(byte typeId, int entryCount, (int EntryId, byte DataType, uint Data)[] entries)
    {
        const int configSize = 64;
        const int headerSize = 20 + configSize;
        var entryData = new List<byte>();
        var offsets = new List<uint>();
        for (var i = 0; i < entryCount; i++) offsets.Add(0xFFFFFFFFu); // 未定义条目 = NO_ENTRY
        foreach (var (entryId, dataType, data) in entries)
        {
            offsets[entryId] = (uint)entryData.Count;
            // ResTable_entry：size=16, flags=0, keyIndex=entryId（测试里 key 与 id 一致即可）
            entryData.AddRange(BitConverter.GetBytes((ushort)16));
            entryData.AddRange(BitConverter.GetBytes((ushort)0));
            entryData.AddRange(BitConverter.GetBytes((uint)entryId));
            entryData.AddRange(BitConverter.GetBytes((ushort)8)); // Res_value size
            entryData.Add(0);
            entryData.Add(dataType);
            entryData.AddRange(BitConverter.GetBytes(data));
        }

        var ms = new List<byte>();
        ms.AddRange(BitConverter.GetBytes((ushort)0x0201));
        ms.AddRange(BitConverter.GetBytes((ushort)headerSize));
        ms.AddRange(new byte[4]); // size 占位
        ms.Add(typeId);
        ms.Add(0); // flags
        ms.AddRange(BitConverter.GetBytes((ushort)0));
        ms.AddRange(BitConverter.GetBytes((uint)entryCount));
        ms.AddRange(BitConverter.GetBytes((uint)(headerSize + entryCount * 4))); // entriesStart
        var config = new byte[configSize];
        BitConverter.GetBytes((uint)configSize).CopyTo(config, 0);
        BitConverter.GetBytes((ushort)320).CopyTo(config, 14); // density=xhdpi
        ms.AddRange(config);
        foreach (var off in offsets) ms.AddRange(BitConverter.GetBytes(off));
        ms.AddRange(entryData);
        OverwriteU32(ms, 4, (uint)ms.Count);
        return ms.ToArray();
    }

    private static void WritePoolLength(List<byte> data, int len)
    {
        if (len < 0x80)
        {
            data.Add((byte)len);
        }
        else
        {
            data.Add((byte)(0x80 | ((len >> 8) & 0x7F)));
            data.Add((byte)(len & 0xFF));
        }
    }

    private static void OverwriteU32(List<byte> buf, int off, uint value)
    {
        var b = BitConverter.GetBytes(value);
        for (var i = 0; i < 4; i++) buf[off + i] = b[i];
    }
}
