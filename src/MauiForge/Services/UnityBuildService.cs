using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MauiForge.Models;

namespace MauiForge.Services;

public record UnityBuildOptions(
    string ProjectDir,
    string Platform, // "Windows64", "Android", "macOS", "iOS", "WebGL"
    string? Publisher = null,
    string? ProfileId = null,
    bool DevelopmentBuild = false,
    bool CheatMode = false,
    bool IsMatrix = false,
    string? CustomOutputPath = null,
    string? Version = null,
    string? BuildNumber = null,
    string? KeystorePath = null,
    string? KeystoreAlias = null,
    bool RunAfterBuild = false,
    string? DeviceId = null,
    string? CustomEditorPath = null
);

public class UnityBuildService(
    UnityLocatorService locator,
    DeviceService devices)
{
    private static readonly Regex ErrorRegex = new(@"(?:error CS\d+|BuildFailedException|Compilation failed|Fatal Error|Assertion failed)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WarningRegex = new(@"warning CS\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public int ExecuteBuild(
        UnityBuildOptions options,
        Action<string> onLine,
        string? logFile = null,
        Action<Process>? onStart = null)
    {
        StreamWriter? log = null;
        if (logFile is not null)
        {
            try
            {
                log = new StreamWriter(logFile, append: false, Encoding.UTF8);
                log.WriteLine($"=== Unity Build: {options.Platform} ===");
                log.WriteLine($"=== Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            catch { }
        }

        void WriteLine(string line)
        {
            try
            {
                onLine(line);
                log?.WriteLine(line);
            }
            catch { }
        }

        var editor = !string.IsNullOrEmpty(options.CustomEditorPath) && File.Exists(options.CustomEditorPath)
            ? new UnityEditorInstallation("Custom", options.CustomEditorPath)
            : locator.ResolveEditorForProject(options.ProjectDir);

        if (editor == null || !File.Exists(editor.ExecutablePath))
        {
            WriteLine("[Unity Error] No compatible Unity Editor found installed.");
            WriteLine("Please install Unity via Unity Hub or specify the editor path.");
            log?.Dispose();
            return -1;
        }

        var projectName = Path.GetFileName(options.ProjectDir.TrimEnd('/', '\\'));
        var hasBuildPipeline = ProjectHasBuildPipeline(options.ProjectDir);

        string? helperScriptPath = null;
        string? generatedExecutablePath = null;

        try
        {
            var psi = new ProcessStartInfo(editor.ExecutablePath)
            {
                WorkingDirectory = options.ProjectDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ProcessEnvironment.UseEnglishCliOutput(psi);

            psi.ArgumentList.Add("-batchmode");
            psi.ArgumentList.Add("-quit");
            psi.ArgumentList.Add("-projectPath");
            psi.ArgumentList.Add(options.ProjectDir);
            psi.ArgumentList.Add("-logFile");
            psi.ArgumentList.Add("-"); // Stream logs to stdout

            if (hasBuildPipeline)
            {
                WriteLine($"[Unity] Using Wagenheimer.BuildPipeline for {projectName}...");
                var method = options.IsMatrix
                    ? "Wagenheimer.BuildPipeline.Editor.BuildCLI.BuildMatrix"
                    : "Wagenheimer.BuildPipeline.Editor.BuildCLI.Build";

                psi.ArgumentList.Add("-executeMethod");
                psi.ArgumentList.Add(method);

                // Platform
                var targetPlatform = MapPlatformName(options.Platform);
                psi.ArgumentList.Add("-platform");
                psi.ArgumentList.Add(targetPlatform);

                // Profile or Publisher
                if (!string.IsNullOrEmpty(options.ProfileId))
                {
                    psi.ArgumentList.Add("-buildProfile");
                    psi.ArgumentList.Add(options.ProfileId);
                }
                else if (!string.IsNullOrEmpty(options.Publisher))
                {
                    psi.ArgumentList.Add("-publisher");
                    psi.ArgumentList.Add(options.Publisher);
                }

                if (options.DevelopmentBuild) psi.ArgumentList.Add("-development");
                if (options.CheatMode) psi.ArgumentList.Add("-cheat");

                if (!string.IsNullOrEmpty(options.Version))
                {
                    psi.ArgumentList.Add("-appVersion");
                    psi.ArgumentList.Add(options.Version);
                }
                if (!string.IsNullOrEmpty(options.BuildNumber))
                {
                    psi.ArgumentList.Add("-appBuildNumber");
                    psi.ArgumentList.Add(options.BuildNumber);
                }

                if (!string.IsNullOrEmpty(options.CustomOutputPath))
                {
                    psi.ArgumentList.Add("-outputPath");
                    psi.ArgumentList.Add(options.CustomOutputPath);
                }

                if (!string.IsNullOrEmpty(options.KeystorePath))
                {
                    psi.ArgumentList.Add("-keystorePath");
                    psi.ArgumentList.Add(options.KeystorePath);
                    if (!string.IsNullOrEmpty(options.KeystoreAlias))
                    {
                        psi.ArgumentList.Add("-keystoreAlias");
                        psi.ArgumentList.Add(options.KeystoreAlias);
                    }
                }
            }
            else
            {
                // Standard Unity Project
                WriteLine($"[Unity] Standard headless build for {projectName} ({options.Platform})...");

                var outputBase = !string.IsNullOrEmpty(options.CustomOutputPath)
                    ? options.CustomOutputPath
                    : Path.Combine(options.ProjectDir, "Builds", options.Platform);

                Directory.CreateDirectory(outputBase);

                var normPlat = options.Platform.ToLowerInvariant();
                if (normPlat.Contains("win"))
                {
                    generatedExecutablePath = Path.Combine(outputBase, $"{projectName}.exe");
                    psi.ArgumentList.Add("-buildWindows64Player");
                    psi.ArgumentList.Add(generatedExecutablePath);
                }
                else if (normPlat.Contains("mac") || normPlat.Contains("osx"))
                {
                    generatedExecutablePath = Path.Combine(outputBase, $"{projectName}.app");
                    psi.ArgumentList.Add("-buildOSXUniversalPlayer");
                    psi.ArgumentList.Add(generatedExecutablePath);
                }
                else if (normPlat.Contains("linux"))
                {
                    generatedExecutablePath = Path.Combine(outputBase, projectName);
                    psi.ArgumentList.Add("-buildLinux64Player");
                    psi.ArgumentList.Add(generatedExecutablePath);
                }
                else
                {
                    // Android / iOS / WebGL: inject temporary Editor runner
                    helperScriptPath = InjectStandardBuildHelper(options.ProjectDir, options.Platform, outputBase, projectName, options.DevelopmentBuild);
                    psi.ArgumentList.Add("-executeMethod");
                    psi.ArgumentList.Add("MauiForge.Editor.BuildRunner.Build");
                }
            }

            WriteLine($"=== Unity Executable: {editor.ExecutablePath} (v{editor.Version}) ===");
            WriteLine($"=== Command: {psi.FileName} {string.Join(" ", psi.ArgumentList)} ===");

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                WriteLine("[Unity Error] Failed to launch Unity process.");
                return -1;
            }

            onStart?.Invoke(proc);

            var readOut = Task.Run(async () =>
            {
                while (await proc.StandardOutput.ReadLineAsync() is { } line)
                {
                    WriteLine(line);
                }
            });

            var readErr = Task.Run(async () =>
            {
                while (await proc.StandardError.ReadLineAsync() is { } line)
                {
                    WriteLine(line);
                }
            });

            try
            {
                Task.WaitAll([readOut, readErr, proc.WaitForExitAsync()]);
            }
            catch { }

            var exitCode = proc.HasExited ? proc.ExitCode : -1;
            WriteLine($"[Unity] Process exited with code: {exitCode}");

            // Post-build execution if requested and successful
            if (exitCode == 0 && options.RunAfterBuild)
            {
                WriteLine("=========================================");
                WriteLine("Starting Post-Build Execution (Run)...");
                WriteLine("=========================================");
                RunAppPostBuild(options, generatedExecutablePath, projectName, WriteLine);
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            WriteLine($"[Unity Error] Execution exception: {ex.Message}");
            return -1;
        }
        finally
        {
            // Clean up transient helper script if created
            if (helperScriptPath != null && File.Exists(helperScriptPath))
            {
                try
                {
                    File.Delete(helperScriptPath);
                    var meta = helperScriptPath + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
                catch { }
            }

            log?.Flush();
            log?.Dispose();
        }
    }

    private void RunAppPostBuild(
        UnityBuildOptions options,
        string? expectedExePath,
        string projectName,
        Action<string> log)
    {
        try
        {
            var normPlat = options.Platform.ToLowerInvariant();
            if (normPlat.Contains("win"))
            {
                // 1. Try expected exe path
                var exe = expectedExePath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    // Scan output dir or default Builds/
                    var searchDirs = new[]
                    {
                        options.CustomOutputPath,
                        Path.Combine(options.ProjectDir, "Builds"),
                        Path.Combine(options.ProjectDir, "Build")
                    }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).ToList();

                    foreach (var dir in searchDirs)
                    {
                        var candidates = Directory.EnumerateFiles(dir!, "*.exe", SearchOption.AllDirectories)
                            .Where(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                            .ToList();
                        if (candidates.Count > 0)
                        {
                            exe = candidates[0];
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    log($"[Run] Launching Windows game executable: {exe}");
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                }
                else
                {
                    log("[Run Warning] Could not locate generated .exe to launch.");
                }
            }
            else if (normPlat.Contains("android"))
            {
                var adb = devices.GetAndroidDevicesAndAvds().AdbPath;
                if (string.IsNullOrEmpty(adb))
                {
                    log("[Run Warning] ADB not found. Cannot deploy to Android device.");
                    return;
                }

                // Locate generated .apk
                var searchDirs = new[]
                {
                    options.CustomOutputPath,
                    Path.Combine(options.ProjectDir, "Builds"),
                    Path.Combine(options.ProjectDir, "Build")
                }.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).ToList();

                string? apk = null;
                foreach (var dir in searchDirs)
                {
                    var candidates = Directory.EnumerateFiles(dir!, "*.apk", SearchOption.AllDirectories)
                        .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                        .ToList();
                    if (candidates.Count > 0)
                    {
                        apk = candidates[0];
                        break;
                    }
                }

                if (string.IsNullOrEmpty(apk) || !File.Exists(apk))
                {
                    log("[Run Warning] Could not find generated .apk to install.");
                    return;
                }

                var deviceArg = !string.IsNullOrEmpty(options.DeviceId) ? $"-s {options.DeviceId} " : "";
                log($"[Run] Installing APK on device: {Path.GetFileName(apk)}...");

                var installProc = Process.Start(new ProcessStartInfo(adb, $"{deviceArg}install -r \"{apk}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                installProc?.WaitForExit(60000);
                log($"[Run] Install completed with code: {installProc?.ExitCode}");

                // Resolve package identifier to launch
                var appId = ReadApplicationId(options.ProjectDir);
                if (!string.IsNullOrEmpty(appId))
                {
                    log($"[Run] Launching package: {appId}...");
                    var launchProc = Process.Start(new ProcessStartInfo(adb, $"{deviceArg}shell monkey -p {appId} -c android.intent.category.LAUNCHER 1")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    });
                    launchProc?.WaitForExit(5000);
                }
            }
        }
        catch (Exception ex)
        {
            log($"[Run Error] Failed to launch game: {ex.Message}");
        }
    }

    private static string InjectStandardBuildHelper(
        string projectDir,
        string platform,
        string outputPath,
        string projectName,
        bool development)
    {
        var editorDir = Path.Combine(projectDir, "Assets", "Editor");
        Directory.CreateDirectory(editorDir);
        var scriptPath = Path.Combine(editorDir, "__MauiForgeBuildHelper.cs");

        var targetEnum = platform.ToLowerInvariant() switch
        {
            var p when p.Contains("android") => "BuildTarget.Android",
            var p when p.Contains("ios") => "BuildTarget.iOS",
            var p when p.Contains("webgl") => "BuildTarget.WebGL",
            var p when p.Contains("mac") => "BuildTarget.StandaloneOSX",
            var p when p.Contains("linux") => "BuildTarget.StandaloneLinux64",
            _ => "BuildTarget.StandaloneWindows64"
        };

        var outputExt = platform.ToLowerInvariant() switch
        {
            var p when p.Contains("android") => ".apk",
            var p when p.Contains("mac") => ".app",
            var p when p.Contains("win") => ".exe",
            _ => ""
        };

        var finalOutput = Path.Combine(outputPath, projectName + outputExt).Replace("\\", "/");

        var code = $$"""
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MauiForge.Editor
{
    public static class BuildRunner
    {
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            var options = BuildOptions.None;
            if ({{(development ? "true" : "false")}})
            {
                options |= BuildOptions.Development;
                options |= BuildOptions.AllowDebugging;
            }

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "{{finalOutput}}",
                target = {{targetEnum}},
                options = options
            };

            Debug.Log("[MauiForge] Starting BuildPlayer: {{targetEnum}} -> {{finalOutput}}");
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            var summary = report.summary;

            Debug.Log($"[MauiForge] Result: {summary.result}, Total Errors: {summary.totalErrors}, Size: {summary.totalSize} bytes");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }
    }
}
""";
        File.WriteAllText(scriptPath, code, Encoding.UTF8);
        return scriptPath;
    }

    private static string? ReadApplicationId(string projectDir)
    {
        try
        {
            var assetPath = Path.Combine(projectDir, "ProjectSettings", "ProjectSettings.asset");
            if (!File.Exists(assetPath)) return null;
            var text = File.ReadAllText(assetPath);
            var m = Regex.Match(text, @"applicationIdentifier:\s*([^\r\n]+)");
            if (m.Success) return m.Groups[1].Value.Trim();

            var m2 = Regex.Match(text, @"Android:\s*([^\r\n]+)");
            if (m2.Success) return m2.Groups[1].Value.Trim();
        }
        catch { }
        return null;
    }

    private static bool ProjectHasBuildPipeline(string projectDir)
    {
        var manifest = Path.Combine(projectDir, "Packages", "manifest.json");
        if (File.Exists(manifest))
        {
            try
            {
                var text = File.ReadAllText(manifest);
                if (text.Contains("com.wagenheimer.buildpipeline")) return true;
            }
            catch { }
        }
        return false;
    }

    private static string MapPlatformName(string platform)
    {
        var p = platform.ToLowerInvariant();
        if (p.Contains("win")) return "Windows64";
        if (p.Contains("android")) return "Android";
        if (p.Contains("ios")) return "iOS";
        if (p.Contains("mac") || p.Contains("osx")) return "macOS";
        if (p.Contains("webgl")) return "WebGL";
        if (p.Contains("linux")) return "Linux64";
        return platform;
    }
}
