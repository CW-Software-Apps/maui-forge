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
        var iconPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MauiForge", "icon.ico");

        // Marker so we only pay the (re)creation cost once per icon-fix generation, not on
        // every launch — but it lets us retroactively fix shortcuts created by older versions
        // before icon.ico existed, or before the Start Menu entry was added, instead of leaving
        // them stuck with the generic icon / desktop-only forever.
        var markerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MauiForge", "shortcuts-v2.flag");
        if (File.Exists(markerPath)) return;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) is { Length: > 0 } sm
            ? Path.Combine(sm, "Programs")
            : null;

        var shimPath = DotnetToolShimPath("maui-forge.exe");
        var target = File.Exists(shimPath) ? shimPath : (Environment.ProcessPath ?? "maui-forge.exe");
        var workingDir = Path.GetDirectoryName(target) ?? ".";

        var shortcutPaths = new List<string> { Path.Combine(desktop, "MAUI Forge.lnk") };
        if (startMenuDir is not null) shortcutPaths.Add(Path.Combine(startMenuDir, "MAUI Forge.lnk"));

        var createLines = string.Join(" ", shortcutPaths.Select((path, i) =>
            $"$S{i} = $W.CreateShortcut('{path}'); $S{i}.TargetPath = '{target}'; $S{i}.WorkingDirectory = '{workingDir}'; " +
            (File.Exists(iconPath) ? $"$S{i}.IconLocation = '{iconPath},0'; " : "") +
            $"$S{i}.Save();"));

        var psScript = "$W = New-Object -ComObject WScript.Shell; " + createLines;

        var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{psScript}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(5000);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O"));
        }
        catch { }
    }

    private static void EnsureMacLauncher()
    {
        var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
        var appPath = Path.Combine(desktop, "MAUI Forge.app");
        if (Directory.Exists(appPath)) return;

        // Replace the plain .command launcher used by earlier versions — it carried
        // no custom icon, so Finder just showed the generic shell-script icon.
        try
        {
            var oldCommandPath = Path.Combine(desktop, "MAUI Forge.command");
            if (File.Exists(oldCommandPath)) File.Delete(oldCommandPath);
        }
        catch { }

        var contentsDir = Path.Combine(appPath, "Contents");
        var macOsDir = Path.Combine(contentsDir, "MacOS");
        var resourcesDir = Path.Combine(contentsDir, "Resources");
        Directory.CreateDirectory(macOsDir);
        Directory.CreateDirectory(resourcesDir);

        var infoPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "    <key>CFBundleExecutable</key><string>launcher</string>\n" +
            "    <key>CFBundleIconFile</key><string>icon.icns</string>\n" +
            "    <key>CFBundleName</key><string>MAUI Forge</string>\n" +
            "    <key>CFBundleIdentifier</key><string>com.cwsoftware.mauiforge</string>\n" +
            "    <key>CFBundlePackageType</key><string>APPL</string>\n" +
            "    <key>LSUIElement</key><false/>\n" +
            "</dict>\n" +
            "</plist>\n";
        File.WriteAllText(Path.Combine(contentsDir, "Info.plist"), infoPlist);

        var launcherPath = Path.Combine(macOsDir, "launcher");
        var launcherContent =
            "#!/bin/bash\n" +
            "# MAUI Forge Launcher — starts the web dashboard and opens your browser.\n" +
            "export PATH=\"$HOME/.dotnet/tools:$PATH\"\n" +
            "pkill -f \"maui-forge\" 2>/dev/null || true\n" +
            "nohup maui-forge > /dev/null 2>&1 &\n" +
            "sleep 2\n" +
            $"open \"http://localhost:{WebStartup.DefaultPort}\" 2>/dev/null || true\n";
        File.WriteAllText(launcherPath, launcherContent);
        try { Process.Start("chmod", $"+x \"{launcherPath}\"")?.WaitForExit(2000); } catch { }

        BuildMacIcon(resourcesDir);
    }

    // Builds Contents/Resources/icon.icns from the bundled icon.png using macOS's
    // built-in sips + iconutil — no Xcode Command Line Tools required, both ship
    // with every Mac. Best-effort: if either tool is missing, the .app still works,
    // it just falls back to the generic app icon.
    private static void BuildMacIcon(string resourcesDir)
    {
        try
        {
            var srcIcon = Path.Combine(AppContext.BaseDirectory, "icon.png");
            if (!File.Exists(srcIcon)) return;

            var iconset = Path.Combine(Path.GetTempPath(), $"mauiforge-icon-{Guid.NewGuid():N}.iconset");
            Directory.CreateDirectory(iconset);

            (string File, int Size)[] specs =
            [
                ("icon_16x16.png", 16),
                ("icon_16x16@2x.png", 32),
                ("icon_32x32.png", 32),
                ("icon_32x32@2x.png", 64),
                ("icon_128x128.png", 128),
                ("icon_128x128@2x.png", 256),
                ("icon_256x256.png", 256),
                ("icon_256x256@2x.png", 512),
                ("icon_512x512.png", 512),
                ("icon_512x512@2x.png", 1024),
            ];

            foreach (var (file, size) in specs)
                RunTool("sips", $"-z {size} {size} \"{srcIcon}\" --out \"{Path.Combine(iconset, file)}\"", timeoutMs: 5000);

            var icnsOut = Path.Combine(resourcesDir, "icon.icns");
            RunTool("iconutil", $"-c icns \"{iconset}\" -o \"{icnsOut}\"", timeoutMs: 10000);

            try { Directory.Delete(iconset, recursive: true); } catch { }
        }
        catch { }
    }

    private static void RunTool(string exe, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(timeoutMs);
        }
        catch { }
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
