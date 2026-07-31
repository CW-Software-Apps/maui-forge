namespace MauiForge.Services;

public class LinuxAutoStartService
{
    private const string FileName = "com.cwsoftware.mauiforge.desktop";

    private string AutostartDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");

    private string DesktopFilePath => Path.Combine(AutostartDir, FileName);

    public record StatusResult(bool Installed, string DesktopFilePath);

    public StatusResult GetStatus()
    {
        if (!OperatingSystem.IsLinux())
            return new StatusResult(false, DesktopFilePath);

        return new StatusResult(File.Exists(DesktopFilePath), DesktopFilePath);
    }

    public bool Install()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            var shimPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "maui-forge");
            var execPath = File.Exists(shimPath) ? shimPath : "maui-forge";

            Directory.CreateDirectory(AutostartDir);

            var content =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=MAUI Forge\n" +
                "Comment=Version and build manager for .NET MAUI apps\n" +
                $"Exec={execPath} --no-open\n" +
                "Terminal=false\n" +
                "X-GNOME-Autostart-enabled=true\n" +
                "Categories=Development;\n";

            File.WriteAllText(DesktopFilePath, content);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Uninstall()
    {
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            if (File.Exists(DesktopFilePath)) File.Delete(DesktopFilePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
