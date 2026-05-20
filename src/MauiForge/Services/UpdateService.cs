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

    /// <summary>
    /// Launches a deferred update: writes a temp script that runs AFTER the
    /// current process exits (avoids the file-lock problem on Windows),
    /// then exits the current process.
    /// </summary>
    public static void LaunchDeferredUpdate(string latestVer, string[] originalArgs)
    {
        var restartArgs = string.Join(" ", originalArgs.Select(a => $"\"{a}\""));

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "set DOTNET_CLI_UI_LANGUAGE=en\r\n" +
                "set LANG=en_US.UTF-8\r\n" +
                "set LC_ALL=en_US.UTF-8\r\n" +
                "set LC_MESSAGES=en_US.UTF-8\r\n" +
                "set LANGUAGE=en\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "dotnet tool update CwSoftware.MauiForge -g\r\n" +
                $"if %errorlevel% == 0 start maui-forge {restartArgs}\r\n" +
                $"del \"%~f0\"\r\n");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments      = $"/c \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-update.sh");
            File.WriteAllText(script,
                "#!/bin/sh\n" +
                "export DOTNET_CLI_UI_LANGUAGE=en\n" +
                "export LANG=en_US.UTF-8\n" +
                "export LC_ALL=en_US.UTF-8\n" +
                "export LC_MESSAGES=en_US.UTF-8\n" +
                "export LANGUAGE=en\n" +
                "sleep 2\n" +
                "dotnet tool update CwSoftware.MauiForge -g\n" +
                $"if [ $? -eq 0 ]; then maui-forge {restartArgs} & fi\n" +
                $"rm -- \"$0\"\n");
            System.IO.File.SetUnixFileMode(script,
                UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sh")
            {
                Arguments      = script,
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
        }

        Environment.Exit(0);
    }
}
