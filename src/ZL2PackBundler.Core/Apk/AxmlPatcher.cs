using System.Text;

namespace ZL2PackBundler.Core.Apk;

/// <summary>
/// Android 二进制 XML（AXML）解析与重写，用于直接修补 AndroidManifest.xml：
/// 摘除原启动组件的 MAIN/LAUNCHER intent-filter，并追加注入安装器 Activity。
/// 完全绕开 apktool/aapt2 的资源重建（Material 等库属性会让 aapt2 link 失败）。
/// 注意：aapt2 产出的节点 size 不含子节点（扁平 START/END 序列），解析按栈配对。
/// </summary>
public static class AxmlPatcher
{
    public sealed class AxmlInfo
    {
        public required string Package { get; init; }
        public required string LauncherName { get; init; }
        public required string LauncherKind { get; init; }
        public string? ImportAlias { get; init; }
    }

    private const ushort ResStringPool = 0x0001;
    private const ushort ResXml = 0x0003;
    private const ushort ResXmlStartNs = 0x0100;
    private const ushort ResXmlEndNs = 0x0101;
    private const ushort ResXmlStartElement = 0x0102;
    private const ushort ResXmlEndElement = 0x0103;
    private const ushort ResXmlCdata = 0x0104;
    private const ushort ResXmlResourceMap = 0x0180;
    private const uint ResStringPoolUtf8 = 0x00000100;
    private const uint NoIndex = 0xFFFFFFFF;
    private const byte TypeString = 0x03;
    private const byte TypeIntDec = 0x10;      // 枚举/整数属性（如 launchMode）
    private const byte TypeIntBoolean = 0x12;  // 布尔属性（如 exported）

    public const string InstallerActivityName = "com.zl2packbundler.installer.BundledPackInstaller";
    public const string AuthorMetaDataName = "zl2packbundler.author";

    /// <summary>清单是否已注入过安装器（当前 LAUNCHER 就是安装器）。</summary>
    public static bool HasInstallerActivity(byte[] manifestBytes, string packageHint)
        => Analyze(manifestBytes, packageHint).LauncherName == InstallerActivityName;

    /// <summary>分析二进制清单：包名、启动组件、导入别名（不做修改）。</summary>
    public static AxmlInfo Analyze(byte[] manifestBytes, string packageHint)
    {
        var doc = Parse(manifestBytes);
        var androidNs = (uint)(doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")
            ?? throw new InvalidDataException("AXML 中缺少 android 命名空间。"));
        var launcher = FindLauncher(doc, androidNs)
            ?? throw new InvalidDataException("二进制清单中未找到 MAIN/LAUNCHER 启动入口。");
        var importAlias = FindImportAlias(doc, androidNs);
        var package = ReadPackage(doc, androidNs) ?? packageHint;
        return new AxmlInfo
        {
            Package = package,
            LauncherName = ResolveName(package, launcher.GetAttr(doc.Pool, androidNs, "name")),
            LauncherKind = launcher.Name ?? "activity",
            ImportAlias = importAlias == null
                ? null
                : ResolveName(package, importAlias.GetAttr(doc.Pool, androidNs, "name"))
        };
    }

    /// <summary>修改应用包名：manifest package 属性 + 所有 android:authorities 值中的旧包名前缀。</summary>
    public static byte[] ApplyPackageRename(byte[] manifestBytes, string oldPackage, string newPackage)
    {
        var doc = Parse(manifestBytes);
        var androidNs = (uint)(doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")
            ?? throw new InvalidDataException("AXML 中缺少 android 命名空间。"));
        var manifest = FindManifest(doc, androidNs)
            ?? throw new InvalidDataException("二进制清单中未找到 <manifest>。");
        SetAttr(manifest, doc.Pool, androidNs, "package", newPackage);

        // 替换 provider authorities 中的旧包名前缀（避免与旧应用同装冲突）
        foreach (var node in AllNodes(doc.Roots))
        {
            for (var i = 0; i < node.Attrs.Count; i++)
            {
                var (ns, nameIdx, raw, type, data) = node.Attrs[i];
                if (ns != androidNs && ns != NoIndex) continue;
                if (doc.Pool.Strings[(int)nameIdx] != "authorities") continue;
                var current = GetValue(doc.Pool, raw, type, data);
                if (current == null || !current.StartsWith(oldPackage, StringComparison.Ordinal)) continue;
                var renamed = newPackage + current.Substring(oldPackage.Length);
                var valueIdx = doc.Pool.Intern(renamed);
                node.Attrs[i] = (ns, nameIdx, valueIdx, TypeString, valueIdx);
            }
        }
        return Serialize(doc);
    }

    /// <summary>修改应用显示名称：设置/替换 <application> 的 android:label（字符串值）。</summary>
    public static byte[] ApplyAppLabel(byte[] manifestBytes, string newLabel)
    {
        var doc = Parse(manifestBytes);
        var androidNs = (uint)(doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")
            ?? throw new InvalidDataException("AXML 中缺少 android 命名空间。"));
        var application = FindApplication(doc, androidNs)
            ?? throw new InvalidDataException("二进制清单中未找到 <application>。");
        SetAttr(application, doc.Pool, androidNs, "label", newLabel);
        return Serialize(doc);
    }

    /// <summary>
    /// 把作者信息写入 &lt;application&gt; 的 meta-data（zl2packbundler.author，字符串值）。
    /// 已存在同名 meta-data 时替换值，避免重复写入。
    /// </summary>
    public static byte[] ApplyAuthor(byte[] manifestBytes, string author)
    {
        var doc = Parse(manifestBytes);
        var androidNs = (uint)(doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")
            ?? throw new InvalidDataException("AXML 中缺少 android 命名空间。"));
        var application = FindApplication(doc, androidNs)
            ?? throw new InvalidDataException("二进制清单中未找到 <application>。");

        foreach (var child in application.Children)
        {
            if (IsElement(child, "meta-data", androidNs, doc.Pool)
                && child.GetAttr(doc.Pool, androidNs, "name") == AuthorMetaDataName)
            {
                SetAttr(child, doc.Pool, androidNs, "value", author);
                return Serialize(doc);
            }
        }

        var meta = NewElement(doc, androidNs, "meta-data");
        meta.Attrs.Add(Attr(doc.Pool, androidNs, "name", AuthorMetaDataName));
        meta.Attrs.Add(Attr(doc.Pool, androidNs, "value", author));
        application.Children.Add(meta);
        return Serialize(doc);
    }

    /// <summary>摘除原启动组件的 LAUNCHER intent-filter，并追加安装器 Activity；返回修补后的二进制。</summary>
    public static byte[] ApplyPatch(byte[] manifestBytes, string packageHint)
    {
        var doc = Parse(manifestBytes);
        var androidNs = (uint)(doc.Pool.GetIndex("http://schemas.android.com/apk/res/android")
            ?? throw new InvalidDataException("AXML 中缺少 android 命名空间。"));

        var launcher = FindLauncher(doc, androidNs)
            ?? throw new InvalidDataException("二进制清单中未找到 MAIN/LAUNCHER 启动入口。");

        Node? removed = null;
        foreach (var child in launcher.Children)
        {
            if (IsElement(child, "intent-filter", androidNs, doc.Pool)
                && HasActionLauncher(child, androidNs, doc.Pool))
            {
                removed = child;
                break;
            }
        }
        if (removed != null) launcher.Children.Remove(removed);

        var application = FindApplication(doc, androidNs)
            ?? throw new InvalidDataException("二进制清单中未找到 <application>。");
        application.Children.Add(BuildInstallerActivity(doc, androidNs));

        return Serialize(doc);
    }

    // ---------- 解析（扁平序列 + START/END 栈配对） ----------

    internal sealed class Document
    {
        public required StringPool Pool { get; init; }
        public required List<uint> ResMap { get; init; }
        public required List<Node> Roots { get; init; }
    }

    internal sealed class Node
    {
        public ushort Kind;
        public string? Name;          // START_ELEMENT 的元素名（查询便利）
        public uint Ns;
        public uint NameIdx;
        public uint Prefix;           // NS 节点
        public uint Uri;              // NS 节点
        public List<(uint Ns, uint Name, uint RawValue, byte Type, uint Data)> Attrs = new();
        public List<Node> Children = new();
        public byte[]? Raw;           // 未知节点（CDATA 等）原样保留
    }

    internal sealed class StringPool
    {
        public required List<string> Strings { get; init; }
        public int? GetIndex(string value)
        {
            var idx = Strings.FindIndex(s => s == value);
            return idx < 0 ? null : idx;
        }
        public uint Intern(string value)
        {
            var idx = GetIndex(value);
            if (idx != null) return (uint)idx;
            Strings.Add(value);
            return (uint)(Strings.Count - 1);
        }
    }

    internal static Document Parse(byte[] data)
    {
        var reader = new Reader(data);
        if (reader.ReadU16() != ResXml) throw new InvalidDataException("不是 AXML 文件（缺 XML 头）。");
        reader.ReadU16(); // headerSize
        var xmlSize = reader.ReadU32();

        StringPool? pool = null;
        List<uint>? resMap = null;
        var roots = new List<Node>();
        var stack = new Stack<Node>();

        while (reader.Offset + 8 <= data.Length)
        {
            var start = reader.Offset;
            var type = reader.PeekU16();
            var size = (int)reader.PeekU32At(4);
            // size 仅作参考（aapt2 不含子节点、我们的输出含子节点），不能用它做边界判断
            if (size == 0 || size < 8) break;

            if (type == ResStringPool)
            {
                pool = ParseStringPool(reader, start, size);
                reader.Offset = start + size;
            }
            else if (type == ResXmlResourceMap)
            {
                reader.Offset = start + 8;
                var count = (size - 8) / 4;
                resMap = new List<uint>();
                for (var i = 0; i < count; i++) resMap.Add(reader.ReadU32());
                reader.Offset = start + size;
            }
            else
            {
                var node = ParseFlatNode(reader, start, size);
                if (node.Kind is ResXmlStartNs or ResXmlStartElement)
                {
                    if (stack.Count == 0) roots.Add(node);
                    else stack.Peek().Children.Add(node);
                    stack.Push(node);
                }
                else if (node.Kind is ResXmlEndNs or ResXmlEndElement)
                {
                    if (stack.Count > 0) stack.Pop();
                    // END 节点不保留：序列化时按树自动补写
                }
                else
                {
                    // CDATA 等未知节点：挂在当前元素下（或根）
                    if (stack.Count > 0) stack.Peek().Children.Add(node);
                    else roots.Add(node);
                }
                // 节点类：游标已按 payload 前进（ParseFlatNode 末尾即该节点 payload 结束处）
            }
        }

        if (pool == null) throw new InvalidDataException("AXML 缺少字符串池。");
        return new Document { Pool = pool, ResMap = resMap ?? new List<uint>(), Roots = roots };
    }

    private static StringPool ParseStringPool(Reader reader, int start, int size)
    {
        reader.Offset = start + 8;
        var count = reader.ReadU32();
        reader.ReadU32(); // styleCount
        var flags = reader.ReadU32();
        var stringsStart = reader.ReadU32();
        reader.ReadU32(); // stylesStart
        var offsets = new List<uint>();
        for (var i = 0; i < count; i++) offsets.Add(reader.ReadU32());
        var utf8 = (flags & ResStringPoolUtf8) != 0;
        var strings = new List<string>();
        foreach (var off in offsets)
        {
            // AOSP StringPool：偏移相对 stringsStart，而非 chunk 起点
            reader.Offset = start + (int)stringsStart + (int)off;
            strings.Add(ReadPoolString(reader, utf8));
        }
        return new StringPool { Strings = strings };
    }

    private static string ReadPoolString(Reader reader, bool utf8)
    {
        if (utf8)
        {
            var b0 = reader.ReadU8();
            int len = (b0 & 0x80) != 0 ? ((b0 & 0x7F) << 8) | reader.ReadU8() : b0;
            return Encoding.UTF8.GetString(reader.ReadBytes(len));
        }
        var len16 = reader.ReadU16();
        var strLen = (len16 & 0x8000) != 0 ? (int)(reader.ReadU32() & 0x7FFFFFFF) : len16;
        var chars = new char[strLen];
        for (var i = 0; i < strLen; i++) chars[i] = (char)reader.ReadU16();
        return new string(chars);
    }

    private static Node ParseFlatNode(Reader reader, int start, int size)
    {
        var type = reader.ReadU16();
        reader.ReadU16(); // headerSize
        reader.ReadU32(); // chunk size
        reader.ReadU32(); // lineNumber
        reader.ReadU32(); // comment

        var node = new Node { Kind = type };
        switch (type)
        {
            case ResXmlStartNs:
            case ResXmlEndNs:
                node.Prefix = reader.ReadU32();
                node.Uri = reader.ReadU32();
                break;
            case ResXmlEndElement:
                node.Ns = reader.ReadU32();
                node.NameIdx = reader.ReadU32();
                break;
            case ResXmlStartElement:
                node.Ns = reader.ReadU32();
                node.NameIdx = reader.ReadU32();
                reader.ReadU16(); // attributeStart
                reader.ReadU16(); // attributeSize
                var attrCount = reader.ReadU16();
                reader.ReadU16(); // idIndex
                reader.ReadU16(); // classIndex
                reader.ReadU16(); // styleIndex
                for (var i = 0; i < attrCount; i++)
                {
                    var ns = reader.ReadU32();
                    var name = reader.ReadU32();
                    var raw = reader.ReadU32();
                    reader.ReadU16(); // value size
                    reader.ReadU8();  // res0
                    var vType = reader.ReadU8();
                    var vData = reader.ReadU32();
                    node.Attrs.Add((ns, name, raw, vType, vData));
                }
                break;
            default:
                reader.Offset = start;
                node.Raw = reader.ReadBytes(size);
                break;
        }
        // 注意：不按 chunk size 跳转——aapt2 的 size 不含子节点，我们的 size 含子节点，
        // 一律按“节点自身 payload”逐块前进，由顶层循环用 START/END 栈配对。
        return node;
    }

    // ---------- 查询 ----------

    private static Node? FindLauncher(Document doc, uint androidNs)
        => FindElement(doc, androidNs, node => IsComponent(node, androidNs, doc.Pool)
            && node.Children.Any(c => HasActionLauncher(c, androidNs, doc.Pool)));

    private static Node? FindImportAlias(Document doc, uint androidNs)
        => FindElement(doc, androidNs, node => IsComponent(node, androidNs, doc.Pool)
            && node.Children.Any(c => IsElement(c, "meta-data", androidNs, doc.Pool)
                && c.GetAttr(doc.Pool, androidNs, "name") == "import_type"
                && c.GetAttr(doc.Pool, androidNs, "value") == "modpack"));

    private static Node? FindApplication(Document doc, uint androidNs)
        => FindElement(doc, androidNs, node => IsElement(node, "application", androidNs, doc.Pool));

    private static Node? FindManifest(Document doc, uint androidNs)
        => FindElement(doc, androidNs, node => IsElement(node, "manifest", androidNs, doc.Pool));

    private static IEnumerable<Node> AllNodes(List<Node> roots)
    {
        foreach (var r in roots)
        {
            yield return r;
            foreach (var n in AllNodes(r.Children)) yield return n;
        }
    }

    /// <summary>设置/替换属性（字符串类型）；缺失时新增。</summary>
    private static void SetAttr(Node node, StringPool pool, uint androidNs, string name, string value)
    {
        var valueIdx = pool.Intern(value);
        for (var i = 0; i < node.Attrs.Count; i++)
        {
            var (ns, nameIdx, _, _, _) = node.Attrs[i];
            if ((ns == androidNs || ns == NoIndex) && pool.Strings[(int)nameIdx] == name)
            {
                node.Attrs[i] = (ns, nameIdx, valueIdx, TypeString, valueIdx);
                return;
            }
        }
        node.Attrs.Add((androidNs, pool.Intern(name), valueIdx, TypeString, valueIdx));
    }

    private static string? GetValue(StringPool pool, uint raw, byte type, uint data)
    {
        if (raw != NoIndex) return pool.Strings[(int)raw];
        if (type == TypeString) return pool.Strings[(int)data];
        return null;
    }

    private static Node? FindElement(Document doc, uint androidNs, Func<Node, bool> predicate)
    {
        Node? Scan(Node n)
        {
            if (predicate(n)) return n;
            foreach (var c in n.Children)
            {
                var hit = Scan(c);
                if (hit != null) return hit;
            }
            return null;
        }
        foreach (var root in doc.Roots)
        {
            var hit = Scan(root);
            if (hit != null) return hit;
        }
        return null;
    }

    private static bool IsComponent(Node n, uint androidNs, StringPool pool)
        => IsElement(n, "activity", androidNs, pool) || IsElement(n, "activity-alias", androidNs, pool);

    private static bool IsElement(Node n, string name, uint androidNs, StringPool pool)
    {
        if (n.Kind != ResXmlStartElement) return false;
        if (n.Name == name) return true;
        var idx = pool.GetIndex(name);
        return idx != null && n.NameIdx == (uint)idx;
    }

    private static bool HasActionLauncher(Node filter, uint androidNs, StringPool pool)
    {
        if (!IsElement(filter, "intent-filter", androidNs, pool)) return false;
        var hasMain = false;
        var hasLauncher = false;
        foreach (var c in filter.Children)
        {
            if (IsElement(c, "action", androidNs, pool)
                && c.GetAttr(pool, androidNs, "name") == "android.intent.action.MAIN") hasMain = true;
            if (IsElement(c, "category", androidNs, pool)
                && c.GetAttr(pool, androidNs, "name") == "android.intent.category.LAUNCHER") hasLauncher = true;
        }
        return hasMain && hasLauncher;
    }

    private static string? ReadPackage(Document doc, uint androidNs)
    {
        var manifest = doc.Roots.FirstOrDefault(n => IsElement(n, "manifest", androidNs, doc.Pool));
        return manifest?.GetAttr(doc.Pool, androidNs, "package");
    }

    private static string ResolveName(string package, string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0) return name;
        if (name.StartsWith('.')) return package + name;
        if (!name.Contains('.')) return package + "." + name;
        return name;
    }

    // ---------- 构造安装器 Activity ----------

    private static Node BuildInstallerActivity(Document doc, uint androidNs)
    {
        var pool = doc.Pool;
        var activity = NewElement(doc, androidNs, "activity");
        activity.Attrs.Add(Attr(pool, androidNs, "name", InstallerActivityName));
        // exported/launchMode 必须写类型化值（Android PackageParser 按 int/bool 解析）
        activity.Attrs.Add(TypedAttr(pool, androidNs, "exported", TypeIntBoolean, NoIndex));
        activity.Attrs.Add(TypedAttr(pool, androidNs, "launchMode", TypeIntDec, 2)); // 2 = singleTask

        var filter = NewElement(doc, androidNs, "intent-filter");
        var action = NewElement(doc, androidNs, "action");
        action.Attrs.Add(Attr(pool, androidNs, "name", "android.intent.action.MAIN"));
        var category = NewElement(doc, androidNs, "category");
        category.Attrs.Add(Attr(pool, androidNs, "name", "android.intent.category.LAUNCHER"));
        filter.Children.Add(action);
        filter.Children.Add(category);
        activity.Children.Add(filter);
        return activity;
    }

    private static Node NewElement(Document doc, uint androidNs, string name)
    {
        return new Node
        {
            Kind = ResXmlStartElement,
            Ns = androidNs,
            Name = name,
            NameIdx = doc.Pool.Intern(name)
        };
    }

    private static (uint Ns, uint Name, uint RawValue, byte Type, uint Data) Attr(
        StringPool pool, uint androidNs, string name, string value)
    {
        var valueIdx = pool.Intern(value);
        return (androidNs, pool.Intern(name), valueIdx, TypeString, valueIdx);
    }

    /// <summary>类型化属性（无 raw 字符串，与 aapt2 输出一致）。</summary>
    private static (uint Ns, uint Name, uint RawValue, byte Type, uint Data) TypedAttr(
        StringPool pool, uint androidNs, string name, byte type, uint data)
        => (androidNs, pool.Intern(name), NoIndex, type, data);

    // ---------- 序列化 ----------

    private static byte[] Serialize(Document doc)
    {
        var poolBytes = SerializeStringPool(doc.Pool);
        var mapBytes = SerializeResMap(doc.ResMap);
        var nodeBytes = new List<byte[]>();
        foreach (var root in doc.Roots) nodeBytes.Add(SerializeNode(doc.Pool, root));

        var xmlSize = 8 + poolBytes.Length + mapBytes.Length + nodeBytes.Sum(b => b.Length);
        using var ms = new MemoryStream(xmlSize);
        using var w = new BinaryWriter(ms);
        w.Write((ushort)ResXml);
        w.Write((ushort)8);
        w.Write((uint)xmlSize);
        w.Write(poolBytes);
        w.Write(mapBytes);
        foreach (var b in nodeBytes) w.Write(b);
        return ms.ToArray();
    }

    private static byte[] SerializeStringPool(StringPool pool)
    {
        // 与 aapt2 默认一致：UTF-16 池（flags=0），偏移相对 stringsStart，字符串以双 NUL 结尾
        var encoded = pool.Strings.Select(s => Encoding.Unicode.GetBytes(s)).ToList();
        var offsets = new List<uint>();
        var data = new List<byte>();
        foreach (var bytes in encoded)
        {
            offsets.Add((uint)data.Count);
            var chars = bytes.Length / 2;
            if (chars < 0x8000)
            {
                data.Add((byte)(chars & 0xFF));
                data.Add((byte)(chars >> 8));
            }
            else
            {
                data.Add((byte)(0x80 | ((chars >> 16) & 0xFF)));
                data.Add((byte)((chars >> 8) & 0xFF));
                data.Add((byte)(chars & 0xFF));
                data.Add((byte)((chars >> 24) & 0xFF));
            }
            data.AddRange(bytes);
            data.Add(0);
            data.Add(0);
        }
        while (data.Count % 4 != 0) data.Add(0); // 字符串池整体必须 4 字节对齐（AOSP 强制）
        var header = 28 + 4 * pool.Strings.Count; // 8(头)+20(count/style/flags/ss/sts)+4*count(offsets)
        var total = header + data.Count;
        using var ms = new MemoryStream(total);
        using var w = new BinaryWriter(ms);
        w.Write((ushort)ResStringPool);
        w.Write((ushort)28); // headerSize
        w.Write((uint)total);
        w.Write((uint)pool.Strings.Count);
        w.Write(0u); // styleCount
        w.Write(0u); // flags = UTF-16
        w.Write((uint)header);
        w.Write(0u); // stylesStart
        foreach (var off in offsets) w.Write(off);
        w.Write(data.ToArray());
        return ms.ToArray();
    }

    private static byte[] SerializeResMap(List<uint> map)
    {
        var size = 8 + map.Count * 4;
        using var ms = new MemoryStream(size);
        using var w = new BinaryWriter(ms);
        w.Write((ushort)ResXmlResourceMap);
        w.Write((ushort)8);
        w.Write((uint)size);
        foreach (var id in map) w.Write(id);
        return ms.ToArray();
    }

    private static byte[] SerializeNode(StringPool pool, Node node)
    {
        if (node.Raw != null)
        {
            var buf = new byte[node.Raw.Length];
            Array.Copy(node.Raw, buf, buf.Length);
            return buf;
        }

        var children = node.Children.Select(c => SerializeNode(pool, c)).ToList();
        var childrenBytes = children.Sum(b => b.Length);

        // AOSP（aapt2）约定：节点 size 只含节点自身 payload，不含子节点/闭合标签
        int size;
        switch (node.Kind)
        {
            case ResXmlStartNs:
                size = 16 + 8;
                break;
            case ResXmlStartElement:
                size = 16 + 20 + node.Attrs.Count * 20; // 节点头16 + ns/name/ext 20 + 属性
                break;
            default:
                size = 16 + childrenBytes;
                break;
        }

        using var ms = new MemoryStream(size);
        using var w = new BinaryWriter(ms);
        w.Write(node.Kind);
        w.Write((ushort)16);
        w.Write((uint)size);
        w.Write(0u); // lineNumber
        w.Write(NoIndex); // comment

        switch (node.Kind)
        {
            case ResXmlStartNs:
                w.Write(node.Prefix);
                w.Write(node.Uri);
                foreach (var child in children) w.Write(child);
                // 补写 END_NAMESPACE
                w.Write((ushort)ResXmlEndNs);
                w.Write((ushort)16);
                w.Write(24u);
                w.Write(0u);
                w.Write(NoIndex);
                w.Write(node.Prefix);
                w.Write(node.Uri);
                break;
            case ResXmlStartElement:
                w.Write(node.Ns);
                w.Write(node.NameIdx);
                w.Write((ushort)20); // attributeStart
                w.Write((ushort)20); // attributeSize
                w.Write((ushort)node.Attrs.Count);
                w.Write((ushort)0); // idIndex
                w.Write((ushort)0); // classIndex
                w.Write((ushort)0); // styleIndex
                foreach (var (ns, name, raw, type, data) in node.Attrs)
                {
                    w.Write(ns);
                    w.Write(name);
                    w.Write(raw);
                    w.Write((ushort)8); // value size
                    w.Write((byte)0);   // res0
                    w.Write(type);
                    w.Write(data);
                }
                foreach (var child in children) w.Write(child);
                // 补写 END_ELEMENT
                w.Write((ushort)ResXmlEndElement);
                w.Write((ushort)16);
                w.Write(24u);
                w.Write(0u);
                w.Write(NoIndex);
                w.Write(node.Ns);
                w.Write(node.NameIdx);
                break;
        }
        return ms.ToArray();
    }

    // ---------- 小工具 ----------

    private static string? GetAttr(this Node node, StringPool pool, uint androidNs, string name)
    {
        if (node.Kind != ResXmlStartElement) return null;
        foreach (var (ns, nameIdx, raw, type, data) in node.Attrs)
        {
            // aapt2 清单里部分属性（如 package）的 ns 为 0xFFFFFFFF
            if ((ns == androidNs || ns == NoIndex) && pool.Strings[(int)nameIdx] == name)
            {
                if (raw != NoIndex) return pool.Strings[(int)raw];
                if (type == TypeString) return pool.Strings[(int)data];
                return null;
            }
        }
        return null;
    }

    internal sealed class Reader
    {
        private readonly byte[] _data;
        public int Offset;

        public Reader(byte[] data) => _data = data;
        public ushort PeekU16() => (ushort)(_data[Offset] | (_data[Offset + 1] << 8));
        public ushort PeekU16At(int rel) => (ushort)(_data[Offset + rel] | (_data[Offset + rel + 1] << 8));
        public uint PeekU32At(int rel) =>
            (uint)(_data[Offset + rel] | (_data[Offset + rel + 1] << 8)
                 | (_data[Offset + rel + 2] << 16) | (_data[Offset + rel + 3] << 24));
        public ushort ReadU16()
        {
            var v = PeekU16();
            Offset += 2;
            return v;
        }
        public uint ReadU32()
        {
            var v = PeekU32At(0);
            Offset += 4;
            return v;
        }
        public byte ReadU8() => _data[Offset++];
        public byte[] ReadBytes(int count)
        {
            var b = new byte[count];
            Array.Copy(_data, Offset, b, 0, count);
            Offset += count;
            return b;
        }
    }
}
