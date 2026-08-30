using System.Diagnostics;
using System.Text;

namespace ZL2PackBundler.Core;

/// <summary>
/// 统一的子进程执行器：stdout/stderr 异步并发读取（避免管道缓冲死锁）、实时日志、
/// 超时强杀。所有外部工具（apktool/apksigner/zipalign/aapt2）都走这里。
/// </summary>
public static class ProcessRunner
{
    public static string Run(
        string fileName,
        IEnumerable<string> args,
        Action<string>? log = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (extraEnv != null)
            foreach (var kv in extraEnv) psi.Environment[kv.Key] = kv.Value;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 " + fileName);
        var output = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); log?.Invoke(e.Data); } };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); log?.Invoke(e.Data); } };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var limit = timeout ?? TimeSpan.FromMinutes(20);
        if (!p.WaitForExit((int)limit.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            throw new InvalidOperationException(
                Path.GetFileName(fileName) + " 执行超时（" + limit.TotalMinutes + " 分钟），已终止。");
        }
        p.WaitForExit(); // 排空异步输出
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                Path.GetFileName(fileName) + " 退出码 " + p.ExitCode + "：\n" + output);
        return output.ToString();
    }
}
