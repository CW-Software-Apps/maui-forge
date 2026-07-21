using System.Diagnostics;
using Microsoft.Win32;

namespace MauiForge.Services;

public class AutoStartService
{
    private const string AppName = "MauiForge";
    private readonly LaunchAgentService _macLaunchAgent = new();

    public record AutoStartStatus(bool Supported, bool Enabled, bool IsRunning, string Platform, string Details);

    public AutoStartStatus GetStatus()
    {
        if (OperatingSystem.IsMacOS())
        {
            var macStatus = _macLaunchAgent.GetStatus();
            var isRunning = MacTrayHelper.IsTrayProcessRunning();
            return new AutoStartStatus(
                Supported: true,
                Enabled: macStatus.Installed,
                IsRunning: isRunning,
                Platform: "macOS",
                Details: macStatus.Loaded ? "LaunchAgent active and running" : (macStatus.Installed ? "LaunchAgent installed" : "Not installed")
            );
        }

        if (OperatingSystem.IsWindows())
        {
            var enabled = IsWindowsAutoStartEnabled();
            return new AutoStartStatus(
                Supported: true,
                Enabled: enabled,
                IsRunning: true,
                Platform: "Windows",
                Details: enabled ? "Windows Registry (HKCU Run) startup enabled" : "Login auto-start not configured"
            );
        }

        return new AutoStartStatus(false, false, true, Environment.OSVersion.Platform.ToString(), "Auto-start not supported on this OS");
    }

    public bool ToggleAutoStart(out string message)
    {
        var current = GetStatus();
        if (!current.Supported)
        {
            message = "Auto-start is not supported on this platform.";
            return false;
        }

        var newState = !current.Enabled;
        return SetAutoStart(newState, out message);
    }

    public bool SetAutoStart(bool enable, out string message)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (enable)
            {
                var ok = _macLaunchAgent.Install();
                message = ok ? "macOS Auto-start (LaunchAgent) installed successfully." : "Failed to install macOS LaunchAgent.";
                return ok;
            }
            else
            {
                var ok = _macLaunchAgent.Uninstall();
                message = ok ? "macOS Auto-start (LaunchAgent) removed." : "Failed to remove macOS LaunchAgent.";
                return ok;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null)
                {
                    message = "Could not open Windows Registry key.";
                    return false;
                }

                if (enable)
                {
                    var dotnetToolPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".dotnet", "tools", "maui-forge.exe"
                    );

                    var execPath = File.Exists(dotnetToolPath) ? $"\"{dotnetToolPath}\" --no-open" : $"\"{Environment.ProcessPath}\" --no-open";
                    key.SetValue(AppName, execPath);
                    message = "Auto-start added to Windows startup (HKCU Run).";
                    return true;
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                    message = "Auto-start removed from Windows startup.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = $"Error configuring Windows Registry: {ex.Message}";
                return false;
            }
        }

        message = "Platform not supported.";
        return false;
    }

    private static bool IsWindowsAutoStartEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }
}
