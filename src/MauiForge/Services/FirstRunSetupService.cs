using System.Diagnostics;

namespace MauiForge.Services;

public static class FirstRunSetupService
{
    public static void EnsureSetup()
    {
        try
        {
            if (OperatingSystem.IsWindows()) EnsureWindowsShortcut();
            else if (OperatingSystem.IsMacOS()) EnsureMacLauncher();
            else if (OperatingSystem.IsLinux()) EnsureLinuxDesktopEntry();
        }
        catch { /* best-effort — setup failure should never block the app */ }
    }

    private static string DotnetToolShimPath(string exeName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", exeName);

    private static void EnsureWindowsShortcut()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, "MAUI Forge.lnk");
        if (File.Exists(shortcutPath)) return;

        var shimPath = DotnetToolShimPath("maui-forge.exe");
        var target = File.Exists(shimPath) ? shimPath : (Environment.ProcessPath ?? "maui-forge.exe");
        var workingDir = Path.GetDirectoryName(target) ?? ".";
        var iconPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MauiForge", "icon.ico");

        var iconLine = File.Exists(iconPath) ? $"$S.IconLocation = '{iconPath},0'; " : "";
        var psScript =
            "$W = New-Object -ComObject WScript.Shell; " +
            $"$S = $W.CreateShortcut('{shortcutPath}'); " +
            $"$S.TargetPath = '{target}'; " +
            $"$S.WorkingDirectory = '{workingDir}'; " +
            iconLine +
            "$S.Save()";

        var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{psScript}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);
    }

    private static void EnsureMacLauncher()
    {
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
        var launcherPath = Path.Combine(desktop, "MAUI Forge.command");
        if (File.Exists(launcherPath)) return;

        var content =
            "#!/bin/bash\n" +
            "# MAUI Forge Launcher — starts the web dashboard and opens your browser.\n" +
            "export PATH=\"$HOME/.dotnet/tools:$PATH\"\n" +
            "pkill -f \"maui-forge\" 2>/dev/null || true\n" +
            "nohup maui-forge > /dev/null 2>&1 &\n" +
            "sleep 2\n" +
            $"open \"http://localhost:{WebStartup.DefaultPort}\" 2>/dev/null || true\n" +
            $"echo \"MAUI Forge is running at http://localhost:{WebStartup.DefaultPort}\"\n";

        Directory.CreateDirectory(desktop);
        File.WriteAllText(launcherPath, content);
        try { Process.Start("chmod", $"+x \"{launcherPath}\"")?.WaitForExit(2000); } catch { }
    }

    private static void EnsureLinuxDesktopEntry()
    {
        var appsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
        var desktopFile = Path.Combine(appsDir, "com.cwsoftware.mauiforge.desktop");
        if (File.Exists(desktopFile)) return;

        var shimPath = DotnetToolShimPath("maui-forge");
        var iconPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "maui-forge", "icon.png");

        var content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=MAUI Forge\n" +
            "Comment=Version and build manager for .NET MAUI apps\n" +
            $"Exec={shimPath}\n" +
            $"Icon={iconPath}\n" +
            "Terminal=false\n" +
            "Categories=Development;\n";

        Directory.CreateDirectory(appsDir);
        File.WriteAllText(desktopFile, content);

        try
        {
            var psi = new ProcessStartInfo("update-desktop-database")
            {
                ArgumentList = { appsDir },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { /* update-desktop-database not present — the .desktop file still works */ }
    }
}
