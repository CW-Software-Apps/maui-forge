namespace MauiForge.Services;

public class DeviceService
{
    public List<iOSDevice> GetiOSDevices(string macHost, string macUser)
    {
        try
        {
            var output = RunSsh(macHost, macUser, "xcrun xctrace list devices 2>&1");
            return ParseXcrunDevices(output);
        }
        catch { return []; }
    }

    public List<iOSDevice> GetiOSDevicesLocal()
    {
        try
        {
            var output = RunProcess("xcrun", "xctrace list devices");
            return ParseXcrunDevices(output);
        }
        catch { return []; }
    }

    public List<string> FindMacsOnNetwork()
    {
        try
        {
            var output = RunProcess("arp", "-a");
            return ParseArpHosts(output);
        }
        catch { return []; }
    }

    public (List<AndroidDevice> Running, List<string> Avds, string? AdbPath) GetAndroidDevicesAndAvds()
    {
        var adbPath = FindAdb();
        if (adbPath is null) return ([], [], null);

        List<AndroidDevice> running = [];
        List<string> avds = [];

        try
        {
            var output = RunProcessFull(adbPath, ["devices", "-l"]);
            running = ParseAdbDevices(output);
        }
        catch { }

        try
        {
            var emulatorPath = FindEmulator();
            if (emulatorPath is not null)
            {
                var avdOutput = RunProcessFull(emulatorPath, ["-list-avds"]);
                avds = avdOutput.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith("INFO") && !l.StartsWith("WARNING"))
                    .ToList();
            }
        }
        catch { }

        return (running, avds, adbPath);
    }

    public static string? FindAdb()
    {
        // Check PATH first
        var fromPath = TryFindInPath("adb");
        if (fromPath is not null) return fromPath;

        // Common Android SDK locations
        var candidates = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                       ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (androidHome is not null)
            candidates.Add(Path.Combine(androidHome, "platform-tools", "adb"));

        // Windows paths
        candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(home, "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"));

        // macOS/Linux paths
        candidates.Add(Path.Combine(home, "Library", "Android", "sdk", "platform-tools", "adb"));
        candidates.Add("/usr/local/share/android-sdk/platform-tools/adb");
        candidates.Add("/opt/android-sdk/platform-tools/adb");

        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? FindEmulator()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                       ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        var candidates = new List<string>();
        if (androidHome is not null)
            candidates.Add(Path.Combine(androidHome, "emulator", "emulator"));

        candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "emulator", "emulator.exe"));
        candidates.Add(Path.Combine(home, "AppData", "Local", "Android", "Sdk", "emulator", "emulator.exe"));
        candidates.Add(Path.Combine(home, "Library", "Android", "sdk", "emulator", "emulator"));

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? TryFindInPath(string exe)
    {
        var exeName = OperatingSystem.IsWindows() ? exe + ".exe" : exe;
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    private static string RunSsh(string host, string user, string command)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("ssh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("StrictHostKeyChecking=no");
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("ConnectTimeout=10");
        psi.ArgumentList.Add($"{user}@{host}");
        psi.ArgumentList.Add(command);

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(15_000);
        return output;
    }

    private static string RunProcess(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(10_000);
        return output;
    }

    private static string RunProcessFull(string exe, string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(10_000);
        return output;
    }

    // xcrun xctrace list devices output format:
    // == Devices ==
    // iPhone 15 Pro (17.4) (UDID)
    // == Simulators ==
    // iPhone 15 Pro Simulator (17.4) (UDID)
    private static List<iOSDevice> ParseXcrunDevices(string output)
    {
        var devices = new List<iOSDevice>();
        var currentSection = "Device";

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("== Simulator")) { currentSection = "Simulator"; continue; }
            if (trimmed.StartsWith("==")) { currentSection = "Device"; continue; }
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Format: Name (OS) (UDID)
            var m = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^(.+?)\s+\(([^)]+)\)\s+\(([0-9A-Fa-f-]{36})\)");
            if (m.Success)
                devices.Add(new iOSDevice(m.Groups[1].Value.Trim(), m.Groups[3].Value, currentSection));
        }

        return devices;
    }

    // adb devices -l output:
    // List of devices attached
    // emulator-5554          device product:sdk_gphone_x86_64 model:sdk_gphone_x86_64
    // R5CX208XXXX            device product:a52sxq model:SM_A526B
    private static List<AndroidDevice> ParseAdbDevices(string output)
    {
        var devices = new List<AndroidDevice>();

        foreach (var line in output.Split('\n').Skip(1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            // Split on first whitespace to get serial and rest
            var tabIdx = trimmed.IndexOfAny([' ', '\t']);
            if (tabIdx < 0) continue;

            var serial = trimmed[..tabIdx].Trim();
            var rest   = trimmed[tabIdx..].Trim();
            var parts  = rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var state = parts[0];
            var model = parts.FirstOrDefault(p => p.StartsWith("model:"))?.Substring(6)
                     ?? parts.FirstOrDefault(p => p.StartsWith("product:"))?.Substring(8)
                     ?? serial;

            devices.Add(new AndroidDevice(serial, model.Replace('_', ' '), state));
        }

        return devices;
    }

    // arp -a output (Windows): "  192.168.1.1           00-11-22-33-44-55     dynamic"
    private static List<string> ParseArpHosts(string output)
    {
        var hosts = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line.Trim(),
                @"(\d+\.\d+\.\d+\.\d+)\s+([\da-fA-F:\-]+)\s+(\w+)");
            if (!m.Success) continue;
            var ip  = m.Groups[1].Value;
            var mac = m.Groups[2].Value;
            if (mac.Contains("incomplete", StringComparison.OrdinalIgnoreCase)) continue;
            hosts.Add(ip);
        }
        return hosts;
    }
}

public record iOSDevice(string Name, string Udid, string Type);
public record AndroidDevice(string Serial, string Model, string State);
