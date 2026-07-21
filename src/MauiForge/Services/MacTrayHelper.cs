using System.Diagnostics;
using System.Reflection;

namespace MauiForge.Services;

public static class MacTrayHelper
{
    private static readonly string AgentStateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge");

    public static readonly string TrayBinaryPath =
        Path.Combine(AgentStateDir, "mac-tray", "maui-forge-tray");

    private static readonly string TrayVersionFile =
        Path.Combine(AgentStateDir, "mac-tray", ".version");

    private static readonly string CliVersion =
        typeof(MacTrayHelper).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
        ?? typeof(MacTrayHelper).Assembly.GetName().Version?.ToString(3)
        ?? "1.6.31";

    public static bool IsTrayInstalled() => File.Exists(TrayBinaryPath);

    public static bool IsTrayStale() => File.Exists(TrayBinaryPath) && IsTrayBinaryStale();

    private static bool IsTrayBinaryStale()
    {
        try
        {
            if (!File.Exists(TrayVersionFile)) return true;
            var saved = File.ReadAllText(TrayVersionFile).Trim();
            return saved != CliVersion;
        }
        catch { return true; }
    }

    public static bool IsTrayProcessRunning()
    {
        try { return Process.GetProcessesByName("maui-forge-tray").Length > 0; }
        catch { return false; }
    }

    public static (bool Success, string Message) LaunchOrActivate()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return (false, "Menu bar (Tray) is available on macOS only.");
        }

        // Terminate previous instance of maui-forge-tray before update/relaunch
        try
        {
            foreach (var p in Process.GetProcessesByName("maui-forge-tray"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        var (binary, installError) = GetOrInstallBinary();
        if (binary == null)
        {
            var reason = string.IsNullOrWhiteSpace(installError)
                ? "binary not found and failed to compile via swiftc"
                : installError;
            return (false, $"⚠ Menu bar helper could not be started: {reason}");
        }

        try
        {
            var existing = Process.GetProcessesByName("maui-forge-tray");
            if (existing.Length > 0)
            {
                var psi = new ProcessStartInfo("open", $"\"{binary}\"")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                return (true, "Menu bar (maui-forge-tray) is already running. Bringing to front...");
            }

            var startPsi = new ProcessStartInfo(binary)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(binary)
            };
            Process.Start(startPsi);
            return (true, "✅ MAUI Forge Status Bar icon started successfully in macOS menu bar.");
        }
        catch (Exception ex)
        {
            return (false, $"Error starting menu bar: {ex.Message}");
        }
    }

    private static (string? Binary, string? Error) GetOrInstallBinary()
    {
        // 1) Try extracting pre-compiled binary from embedded resources
        try
        {
            var assembly = typeof(MacTrayHelper).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("maui-forge-tray", StringComparison.OrdinalIgnoreCase) || n.EndsWith("mac-tray", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(TrayBinaryPath)!);
                    using var fileStream = new FileStream(TrayBinaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    stream.CopyTo(fileStream);
                    fileStream.Flush();
                    Process.Start("chmod", $"+x \"{TrayBinaryPath}\"")?.WaitForExit();
                    WriteVersionFile();
                    return (TrayBinaryPath, null);
                }
            }
        }
        catch { }

        // 2) Binary already exists on disk and is up to date
        if (File.Exists(TrayBinaryPath) && !IsTrayBinaryStale())
            return (TrayBinaryPath, null);

        // 3) Recompile from Swift source on macOS
        if (OperatingSystem.IsMacOS())
        {
            var result = CompileFromSource();
            if (result.Binary != null)
                return result;
        }

        // 4) Fallback: use existing binary even if stale
        if (File.Exists(TrayBinaryPath))
            return (TrayBinaryPath, "using previous binary (recompilation recommended)");

        return (null, "Could not prepare maui-forge-tray binary");
    }

    private static (string? Binary, string? Error) CompileFromSource()
    {
        try
        {
            var targetDir = Path.GetDirectoryName(TrayBinaryPath)!;
            Directory.CreateDirectory(targetDir);

            string? swiftContent = null;
            var assembly = typeof(MacTrayHelper).Assembly;
            var swiftResource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("main.swift", StringComparison.OrdinalIgnoreCase));

            if (swiftResource != null)
            {
                using var stream = assembly.GetManifestResourceStream(swiftResource);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    swiftContent = reader.ReadToEnd();
                }
            }

            if (string.IsNullOrEmpty(swiftContent))
            {
                return (null, "Could not load embedded main.swift resource.");
            }

            var tempSwift = Path.Combine(targetDir, "main.swift");
            File.WriteAllText(tempSwift, swiftContent);

            var psi = new ProcessStartInfo("swiftc", $"-O \"{tempSwift}\" -o \"{TrayBinaryPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var proc = Process.Start(psi);
            if (proc == null) return (null, "Failed to start swiftc compiler");

            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            try { File.Delete(tempSwift); } catch { }

            if (proc.ExitCode != 0)
            {
                return (null, $"Error compiling main.swift: {stderr}");
            }

            Process.Start("chmod", $"+x \"{TrayBinaryPath}\"")?.WaitForExit();
            WriteVersionFile();
            return (TrayBinaryPath, null);
        }
        catch (Exception ex)
        {
            return (null, $"Exception compiling main.swift: {ex.Message}");
        }
    }

    private static void WriteVersionFile()
    {
        try
        {
            File.WriteAllText(TrayVersionFile, CliVersion);
        }
        catch { }
    }
}
