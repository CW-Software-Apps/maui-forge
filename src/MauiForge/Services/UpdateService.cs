using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;

namespace MauiForge.Services;

public class UpdateService
{
    public static readonly UpdateService Instance = new();

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const string PackageId = "CwSoftware.MauiForge";
    private string? _latestVersion;
    private Task? _checkTask;

    private UpdateService() { }

    public void StartCheck()
    {
        _checkTask ??= Task.Run(FetchLatestVersion);
    }

    public void ForceCheck()
    {
        _latestVersion = null;
        _checkTask = Task.Run(FetchLatestVersion);
    }

    private async Task FetchLatestVersion()
    {
        try
        {
            var url      = $"https://api.nuget.org/v3-flatcontainer/{PackageId.ToLower()}/index.json";
            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("versions", out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                var versions = prop.EnumerateArray()
                    .Select(v => v.GetString())
                    .Where(v => v is not null)
                    .ToList();
                if (versions.Count > 0)
                    _latestVersion = versions[^1];
            }
        }
        catch { }
    }

    public string? GetLatestVersion() => _latestVersion;

    public static string GetManualUpdateCommand(string latestVer) =>
        $"dotnet tool update {PackageId} -g --version {latestVer}";

    public static void LaunchDeferredUpdate(string latestVer, string[] originalArgs, bool interactive = true, int port = 5123)
    {
        var currentPid = Environment.ProcessId;
        var dotnetPath = FindDotnet() ?? "dotnet";
        var manualCommand = GetManualUpdateCommand(latestVer);
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-update.bat");
            var log = Path.Combine(Path.GetTempPath(), "maui-forge-update.log");

            try { if (File.Exists(script)) File.Delete(script); } catch { }
            try { if (File.Exists(log)) File.Delete(log); } catch { }
            var batchContent =
                "@echo off\r\n" +
                "chcp 65001 > nul\r\n" +
                "title MAUI Forge Auto-Updater\r\n" +
                "color 0B\r\n" +
                "echo.\r\n" +
                "echo  ---------------------------------------------------------\r\n" +
                "echo               MAUI Forge Auto-Updater\r\n" +
                "echo  ---------------------------------------------------------\r\n" +
                "echo.\r\n" +
                $"echo  [1/3] Waiting for MAUI Forge (PID: {currentPid}) to close...\r\n" +
                "setlocal enabledelayedexpansion\r\n" +
                $"set \"PID_TO_WAIT={currentPid}\"\r\n" +
                $"set \"DOTNET_CMD={dotnetPath}\"\r\n" +
                $"set \"VERSION={latestVer}\"\r\n" +
                $"set \"LOG_FILE={log}\"\r\n" +
                $"set \"PORT={port}\"\r\n" +
                "\r\n" +
                "echo === MAUI Forge updater started %DATE% %TIME% === > \"!LOG_FILE!\"\r\n" +
                "\r\n" +
                ":wait_loop\r\n" +
                "tasklist /FI \"PID eq %PID_TO_WAIT%\" 2>NUL | find \"%PID_TO_WAIT%\" >NUL\r\n" +
                "if %ERRORLEVEL% == 0 (\r\n" +
                "    timeout /t 1 /nobreak >nul\r\n" +
                "    goto wait_loop\r\n" +
                ")\r\n" +
                "\r\n" +
                "echo.\r\n" +
                "echo  [2/3] Downloading and installing version !VERSION!...\r\n" +
                "echo  Running: \"!DOTNET_CMD!\" tool update CwSoftware.MauiForge -g --version !VERSION!\r\n" +
                "echo.\r\n" +
                "\r\n" +
                "for /L %%i in (1,1,3) do (\r\n" +
                "    \"!DOTNET_CMD!\" tool update CwSoftware.MauiForge -g --version !VERSION!\r\n" +
                "    if !ERRORLEVEL! == 0 (\r\n" +
                "        echo Update successful. >> \"!LOG_FILE!\"\r\n" +
                "        goto success\r\n" +
                "    )\r\n" +
                "    echo Attempt %%i failed. Retrying in 3 seconds...\r\n" +
                "    timeout /t 3 /nobreak >nul\r\n" +
                ")\r\n" +
                "\r\n" +
                "echo.\r\n" +
                "color 0C\r\n" +
                "echo  ---------------------------------------------------------\r\n" +
                "echo  [ERROR] Update failed! Check log: !LOG_FILE!\r\n" +
                "echo  ---------------------------------------------------------\r\n" +
                "echo.\r\n" +
                "echo  Press any key to exit...\r\n" +
                "pause > nul\r\n" +
                "exit /b 1\r\n" +
                "\r\n" +
                ":success\r\n" +
                "echo.\r\n" +
                "echo  ---------------------------------------------------------\r\n" +
                "echo  [3/3] Update completed successfully!\r\n" +
                "echo  ---------------------------------------------------------\r\n" +
                "echo.\r\n" +
                "echo  Relaunching MAUI Forge automatically...\r\n" +
                "timeout /t 1 /nobreak >nul\r\n" +
                $"start maui-forge --port !PORT!\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"start http://localhost:!PORT!\r\n" +
                "del \"%~f0\"\r\n" +
                "exit /b 0\r\n";
            File.WriteAllText(script, batchContent);

            var updater = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments      = $"/c start \"\" \"{script}\"",
                UseShellExecute = true,
                CreateNoWindow  = true
            });

            if (updater is null)
                throw new InvalidOperationException($"Could not start updater. Run manually: {manualCommand}");

            Environment.Exit(0);
        }
        else
        {
            var shPath = Path.Combine(Path.GetTempPath(), "maui-forge-update.sh");
            var shLog = Path.Combine(Path.GetTempPath(), "maui-forge-update.log");

            var shContent =
                "#!/bin/bash\n" +
                $"exec >> \"{shLog}\" 2>&1\n" +
                "echo \"[$(date)] MAUI Forge self-update started\"\n" +
                $"export PATH=\"$HOME/.dotnet/tools:/opt/homebrew/bin:/opt/homebrew/sbin:/usr/local/bin:/usr/local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin:{currentPath}\"\n" +
                "sleep 3\n" +
                $"dotnet tool update CwSoftware.MauiForge -g --version {latestVer} || dotnet tool update CwSoftware.MauiForge -g || true\n" +
                "echo \"[$(date)] Relaunching maui-forge...\"\n" +
                $"nohup maui-forge --port {port} > /dev/null 2>&1 &\n" +
                "sleep 2\n" +
                $"open \"http://localhost:{port}\" 2>/dev/null || xdg-open \"http://localhost:{port}\" 2>/dev/null || true\n";

            File.WriteAllText(shPath, shContent);
            try { Process.Start("chmod", $"+x \"{shPath}\"")?.WaitForExit(); } catch { }

            var psiMac = new ProcessStartInfo("bash")
            {
                ArgumentList = { shPath },
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psiMac);
            Environment.Exit(0);
        }
    }

    private static string? FindDotnet()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is not null
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && File.Exists(processPath))
            return processPath;

        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            var exeName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            var paths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                try
                {
                    var fullPath = Path.Combine(path.Trim(), exeName);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe"),
            }
            : new[]
            {
                "/usr/local/share/dotnet/dotnet",
                "/opt/homebrew/bin/dotnet",
                "/usr/local/bin/dotnet",
                "/usr/bin/dotnet",
            };

        return candidates.FirstOrDefault(File.Exists);
    }
}
