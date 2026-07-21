using System.Diagnostics;

namespace MauiForge.Services;

public class LaunchAgentService
{
    public const string Label = "com.cwsoftware.mauiforge";

    private string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", $"{Label}.plist"
    );

    public record StatusResult(bool Installed, bool Loaded, string Label, string PlistPath, string? Details);

    public StatusResult GetStatus()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new StatusResult(false, false, Label, PlistPath, "Indisponível fora do macOS");
        }

        var installed = File.Exists(PlistPath);
        var loaded = false;
        string? details = null;

        try
        {
            var uid = GetCurrentUserId();
            var psi = new ProcessStartInfo("launchctl", $"print gui/{uid}/{Label}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                loaded = proc.ExitCode == 0 && stdout.Contains("state = running");
                details = stdout;
            }
        }
        catch (Exception ex)
        {
            details = ex.Message;
        }

        return new StatusResult(installed, loaded, Label, PlistPath, details);
    }

    public bool Install()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            var dotnetToolPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet", "tools", "maui-forge"
            );

            var execPath = File.Exists(dotnetToolPath) ? dotnetToolPath : "maui-forge";

            var dir = Path.GetDirectoryName(PlistPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var plistContent = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>{Label}</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>{execPath}</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>KeepAlive</key>
                    <true/>
                    <key>StandardOutPath</key>
                    <string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge", "maui-forge-stdout.log")}</string>
                    <key>StandardErrorPath</key>
                    <string>{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge", "maui-forge-stderr.log")}</string>
                </dict>
                </plist>
                """;

            File.WriteAllText(PlistPath, plistContent);

            var uid = GetCurrentUserId();
            Process.Start("launchctl", $"bootstrap gui/{uid} \"{PlistPath}\"")?.WaitForExit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Uninstall()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        try
        {
            var uid = GetCurrentUserId();
            Process.Start("launchctl", $"bootout gui/{uid}/{Label}")?.WaitForExit();
            if (File.Exists(PlistPath)) File.Delete(PlistPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string GetLogs()
    {
        var stdoutLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge", "maui-forge-stdout.log");
        var stderrLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge", "maui-forge-stderr.log");

        var sb = new System.Text.StringBuilder();
        if (File.Exists(stdoutLog))
        {
            sb.AppendLine("=== STDOUT ===");
            sb.AppendLine(File.ReadAllText(stdoutLog));
        }
        if (File.Exists(stderrLog))
        {
            sb.AppendLine("=== STDERR ===");
            sb.AppendLine(File.ReadAllText(stderrLog));
        }

        return sb.Length > 0 ? sb.ToString() : "Nenhum log encontrado.";
    }

    private static string GetCurrentUserId()
    {
        try
        {
            var psi = new ProcessStartInfo("id", "-u")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var outStr = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrEmpty(outStr)) return outStr;
            }
        }
        catch { }
        return "501";
    }
}
