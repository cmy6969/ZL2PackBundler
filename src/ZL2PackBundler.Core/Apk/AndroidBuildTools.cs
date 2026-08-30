using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ZL2PackBundler.Core.Apk;

/// <summary>javac + d8：把注入安装器编译成 dex。</summary>
public static class AndroidBuildTools
{
    public static string LocateJavac()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Path.Combine(javaHome, "bin", "javac.exe");
            if (File.Exists(p)) return p;
        }
        return "javac";
    }

    /// <summary>用 aapt2 dump badging 读取 APK 的包名（AGP 8 的清单里可能没有 package 属性）。</summary>
    public static string GetPackageName(string apkPath, string sdkBuildToolsDir)
    {
        var output = RunCaptured(Path.Combine(sdkBuildToolsDir, "aapt2.exe"),
            new[] { "dump", "badging", apkPath }, null);
        var m = Regex.Match(output, "package: name='([^']+)'");
        if (!m.Success)
            throw new InvalidDataException("aapt2 无法读取 APK 包名。");
        return m.Groups[1].Value;
    }

    public static string LocateAndroidJar(string sdkBuildToolsDir)
    {
        // build-tools/<ver> -> <sdk>/platforms
        var sdkRoot = Directory.GetParent(Directory.GetParent(sdkBuildToolsDir)!.FullName)!.FullName;
        var platforms = Path.Combine(sdkRoot, "platforms");
        var jar = Directory.EnumerateDirectories(platforms)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => Path.Combine(d, "android.jar"))
            .FirstOrDefault(File.Exists);
        if (jar == null)
            throw new InvalidOperationException("未找到 android.jar（请用 SDK Manager 安装任一 platforms 组件）。");
        return jar;
    }

    /// <summary>
    /// 用嵌入式源码模板编译注入安装器，替换 @LAUNCHER@/@IMPORT_ALIAS@ 常量。
    /// 返回编译出的 classes.dex 文件路径。
    /// </summary>
    public static string CompileInstaller(
        string sdkBuildToolsDir,
        string workDir,
        string launcherActivity,
        string importAlias,
        Action<string>? log = null)
    {
        var androidJar = LocateAndroidJar(sdkBuildToolsDir);
        var javaPath = Path.Combine(workDir, "BundledPackInstaller.java");
        var classesDir = Path.Combine(workDir, "classes");
        var dexDir = Path.Combine(workDir, "dex");
        Directory.CreateDirectory(classesDir);
        Directory.CreateDirectory(dexDir);

        var source = LoadInstallerSource();
        source = source.Replace("@LAUNCHER@", launcherActivity);
        source = source.Replace("@IMPORT_ALIAS@", importAlias ?? "");
        File.WriteAllText(javaPath, source, new System.Text.UTF8Encoding(false));

        log?.Invoke("javac 编译注入安装器…");
        Run(LocateJavac(), new[]
        {
            "-encoding", "UTF-8", "-source", "8", "-target", "8", "-nowarn",
            "-classpath", androidJar, "-d", classesDir, javaPath
        }, log);

        log?.Invoke("d8 生成 dex…");
        Run(Signing.ApkSigner.LocateJava(), new[]
        {
            "-cp", Path.Combine(sdkBuildToolsDir, "lib", "d8.jar"),
            "com.android.tools.r8.D8", "--release",
            "--lib", androidJar,
            "--output", dexDir,
            Path.Combine(classesDir, "com", "zl2packbundler", "installer", "BundledPackInstaller.class")
        }, log);

        var dex = Path.Combine(dexDir, "classes.dex");
        if (!File.Exists(dex))
            throw new InvalidOperationException("d8 未产出 classes.dex。");
        return dex;
    }

    private static string LoadInstallerSource()
    {
        var asm = typeof(AndroidBuildTools).Assembly;
        using var stream = asm.GetManifestResourceStream(
            "ZL2PackBundler.Core.Apk.Installer.BundledPackInstaller.java")
            ?? throw new InvalidOperationException("内嵌安装器源码缺失（EmbeddedResource 未打包）。");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string RunCaptured(string fileName, IEnumerable<string> args, Action<string>? log)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 " + fileName);
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (stdout.Length > 0) log?.Invoke(stdout.TrimEnd());
        if (stderr.Length > 0) log?.Invoke(stderr.TrimEnd());
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                Path.GetFileName(fileName) + " 退出码 " + p.ExitCode + "：\n" + stdout + "\n" + stderr);
        return stdout + stderr;
    }

    private static void Run(string fileName, IEnumerable<string> args, Action<string>? log)
    {
        RunCaptured(fileName, args, log);
    }
}
