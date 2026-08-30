using System.Reflection;
using System.Text;
using Xunit;

namespace ZL2PackBundler.Core.Tests;

/// <summary>
/// 防回归：内嵌的安装器 dex 必须同时定义外层类与内部类 BundledPackInstaller$Manifest。
/// （历史事故：d8 只收到单个 .class，内部类缺失导致设备 NoClassDefFoundError。）
/// </summary>
public class BundledInstallerDexTests
{
    [Fact]
    public void EmbeddedDexDefinesOuterAndManifestInnerClass()
    {
        var dex = LoadEmbeddedDex();
        var defined = ParseDefinedClasses(dex);
        Assert.Contains("Lcom/zl2packbundler/installer/BundledPackInstaller;", defined);
        Assert.Contains("Lcom/zl2packbundler/installer/BundledPackInstaller$Manifest;", defined);
    }

    private static byte[] LoadEmbeddedDex()
    {
        using var s = typeof(Apk.BundledTools).Assembly.GetManifestResourceStream(
            "ZL2PackBundler.Core.Apk.Installer.BundledPackInstaller.dex")
            ?? throw new InvalidOperationException("内嵌 dex 资源缺失");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static List<string> ParseDefinedClasses(byte[] dex)
    {
        uint U32(int off) => (uint)(dex[off] | (dex[off + 1] << 8) | (dex[off + 2] << 16) | (dex[off + 3] << 24));
        var stringIdsSize = U32(0x38);
        var stringIdsOff = U32(0x3C);
        var typeIdsSize = U32(0x40);
        var typeIdsOff = U32(0x44);
        var classDefsSize = U32(0x60);
        var classDefsOff = U32(0x64);

        string ReadMutf8(int off)
        {
            // 长度 = uleb128（类名为 ASCII，只处理 1-2 字节长度）
            var len = dex[off] & 0x7F;
            var shift = 1;
            if ((dex[off] & 0x80) != 0)
            {
                len |= (dex[off + 1] & 0x7F) << 7;
                shift = 2;
            }
            return Encoding.ASCII.GetString(dex, off + shift, len);
        }
        string StringAt(uint idx)
        {
            var dataOff = (int)U32((int)(stringIdsOff + idx * 4));
            return ReadMutf8(dataOff);
        }
        string TypeAt(uint idx) => StringAt(U32((int)(typeIdsOff + idx * 4)));

        var result = new List<string>();
        for (var i = 0; i < classDefsSize; i++)
        {
            var classIdx = U32((int)(classDefsOff + i * 32));
            result.Add(TypeAt(classIdx));
        }
        return result;
    }
}
