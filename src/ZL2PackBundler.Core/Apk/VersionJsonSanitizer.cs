using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZL2PackBundler.Core.Apk;

/// <summary>
/// 修复版本 json 中重复的 libraries 条目（PCL2 等启动器导出的整合包常见：
/// 同一个库出现两次会导致 HMCL 系启动器构建 classpath 时报
/// "Duplicate key ... attempted merging values"，游戏无法启动）。
/// </summary>
public static class VersionJsonSanitizer
{
    /// <summary>按 name 去重 libraries；有改动返回 true 并输出修复后的字节。</summary>
    public static bool TrySanitize(byte[] input, out byte[] sanitized)
    {
        sanitized = input;
        try
        {
            var root = JsonNode.Parse(input);
            if (root is not JsonObject obj || obj["libraries"] is not JsonArray libs) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new JsonArray();
            var changed = false;
            foreach (var lib in libs)
            {
                var name = (lib as JsonObject)?["name"]?.GetValue<string>();
                if (name == null)
                {
                    result.Add(lib?.DeepClone());
                    continue;
                }
                if (!seen.Add(name))
                {
                    changed = true; // 重复条目：丢弃
                    continue;
                }
                result.Add(lib.DeepClone());
            }
            if (!changed) return false;

            obj["libraries"] = result;
            sanitized = JsonSerializer.SerializeToUtf8Bytes(obj, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch
        {
            return false; // 解析失败：原样保留，不破坏用户数据
        }
    }
}
