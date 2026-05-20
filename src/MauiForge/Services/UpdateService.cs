using System.Text.Json;

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

    /// <summary>
    /// Launches a deferred update: writes a temp script that runs AFTER the
    /// current process exits (avoids the file-lock problem on Windows), then
    /// exits the current process. The updater does not restart MAUI Forge
    /// because a background child process is not attached to an interactive TTY.
    /// </summary>
    public static void LaunchDeferredUpdate(string latestVer, string[] originalArgs)
    {
        var currentPid = Environment.ProcessId;
        var dotnetPath = FindDotnet() ?? "dotnet";
        var manualCommand = GetManualUpdateCommand(latestVer);

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-update.ps1");
            var log = Path.Combine(Path.GetTempPath(), "maui-forge-update.log");
            File.WriteAllText(script,
                "$ErrorActionPreference = 'Continue'\r\n" +
                "$env:DOTNET_CLI_UI_LANGUAGE = 'en'\r\n" +
                "$env:LANG = 'en_US.UTF-8'\r\n" +
                "$env:LC_ALL = 'en_US.UTF-8'\r\n" +
                "$env:LC_MESSAGES = 'en_US.UTF-8'\r\n" +
                "$env:LANGUAGE = 'en'\r\n" +
                $"$log = '{EscapePowerShellSingleQuoted(log)}'\r\n" +
                $"$pidToWait = {currentPid}\r\n" +
                $"$dotnet = '{EscapePowerShellSingleQuoted(dotnetPath)}'\r\n" +
                $"$version = '{EscapePowerShellSingleQuoted(latestVer)}'\r\n" +
                "\"=== MAUI Forge updater started $(Get-Date -Format o) ===\" | Out-File -FilePath $log -Encoding utf8\r\n" +
                $"\"Manual fallback: {EscapePowerShellSingleQuoted(manualCommand)}\" | Tee-Object -FilePath $log -Append\r\n" +
                "try { Wait-Process -Id $pidToWait -Timeout 60 -ErrorAction SilentlyContinue } catch { }\r\n" +
                "Start-Sleep -Seconds 1\r\n" +
                "for ($i = 1; $i -le 8; $i++) {\r\n" +
                "  \"Attempt $i: $dotnet tool update CwSoftware.MauiForge -g --version $version\" | Tee-Object -FilePath $log -Append\r\n" +
                "  & $dotnet tool update CwSoftware.MauiForge -g --version $version *>&1 | Tee-Object -FilePath $log -Append\r\n" +
                "  if ($LASTEXITCODE -eq 0) { break }\r\n" +
                "  Start-Sleep -Seconds 2\r\n" +
                "}\r\n" +
                "\"Updater finished with exit code $LASTEXITCODE\" | Tee-Object -FilePath $log -Append\r\n" +
                "Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue\r\n");

            var shell = FindWindowsPowerShell();
            if (shell is null)
                throw new InvalidOperationException($"PowerShell not found. Run manually: {manualCommand}");

            var updater = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(shell)
            {
                Arguments      = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            if (updater is null)
                throw new InvalidOperationException($"Could not start updater. Run manually: {manualCommand}");
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-update.sh");
            var log = Path.Combine(Path.GetTempPath(), "maui-forge-update.log");
            File.WriteAllText(script,
                "#!/bin/sh\n" +
                "export DOTNET_CLI_UI_LANGUAGE=en\n" +
                "export LANG=en_US.UTF-8\n" +
                "export LC_ALL=en_US.UTF-8\n" +
                "export LC_MESSAGES=en_US.UTF-8\n" +
                "export LANGUAGE=en\n" +
                $"log='{EscapeShellSingleQuoted(log)}'\n" +
                $"pid_to_wait='{currentPid}'\n" +
                $"dotnet_cmd='{EscapeShellSingleQuoted(dotnetPath)}'\n" +
                $"version='{EscapeShellSingleQuoted(latestVer)}'\n" +
                "printf '=== MAUI Forge updater started %s ===\\n' \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\" > \"$log\"\n" +
                $"printf 'Manual fallback: {EscapeShellSingleQuoted(manualCommand)}\\n' >> \"$log\"\n" +
                "while kill -0 \"$pid_to_wait\" 2>/dev/null; do sleep 0.2; done\n" +
                "sleep 1\n" +
                "i=1\n" +
                "while [ \"$i\" -le 8 ]; do\n" +
                "  printf 'Attempt %s: %s tool update CwSoftware.MauiForge -g --version %s\\n' \"$i\" \"$dotnet_cmd\" \"$version\" >> \"$log\"\n" +
                "  \"$dotnet_cmd\" tool update CwSoftware.MauiForge -g --version \"$version\" >> \"$log\" 2>&1 && break\n" +
                "  i=$((i + 1))\n" +
                "  sleep 2\n" +
                "done\n" +
                "printf 'Updater finished with exit code %s\\n' \"$?\" >> \"$log\"\n" +
                "rm -- \"$0\"\n");
            System.IO.File.SetUnixFileMode(script,
                UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var updater = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sh")
            {
                Arguments      = script,
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            if (updater is null)
                throw new InvalidOperationException($"Could not start updater. Run manually: {manualCommand}");
        }

        Environment.Exit(0);
    }

    private static string? FindDotnet()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is not null
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && File.Exists(processPath))
            return processPath;

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

    private static string? FindWindowsPowerShell()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? "powershell.exe";
    }

    private static string EscapePowerShellSingleQuoted(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeShellSingleQuoted(string value) =>
        value.Replace("'", "'\"'\"'", StringComparison.Ordinal);
}
