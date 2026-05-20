namespace MauiForge.Services;

public class DeviceService
{
    public List<iOSDevice> GetiOSDevices(string macHost, string macUser)
    {
        var devices = new List<iOSDevice>();
        TryAdd(devices, () => ParseXcrunDevices(RunSsh(macHost, macUser, "xcrun xctrace list devices 2>&1")));
        TryAdd(devices, () => ParseXcdeviceList(RunSsh(macHost, macUser, "xcrun xcdevice list --timeout 5 2>/dev/null")));
        TryAdd(devices, () => ParseInstrumentsDevices(RunSsh(macHost, macUser, "xcrun instruments -s devices 2>/dev/null")));
        return DistinctDevices(devices);
    }

    public List<iOSDevice> GetiOSDevicesLocal()
    {
        var devices = new List<iOSDevice>();
        TryAdd(devices, () => ParseXcrunDevices(RunProcessFull("xcrun", ["xctrace", "list", "devices"])));
        TryAdd(devices, () => ParseXcdeviceList(RunProcessFull("xcrun", ["xcdevice", "list", "--timeout", "5"])));
        TryAdd(devices, () => ParseInstrumentsDevices(RunProcessFull("xcrun", ["instruments", "-s", "devices"])));
        return DistinctDevices(devices);
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

    public static string? FindAdb() => FindAndroidTool("adb", "adb.exe");

    public static string? FindEmulator() => FindAndroidTool("emulator", "emulator.exe");

    private static string? FindAndroidTool(string toolUnix, string toolWin)
    {
        var tool = OperatingSystem.IsWindows() ? toolWin : toolUnix;

        // 1. PATH
        var fromPath = TryFindInPath(toolUnix);
        if (fromPath is not null) return fromPath;

        var candidates = new List<string>();

        // 2. Environment variables
        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_HOME")
                   ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (sdkRoot is not null)
        {
            candidates.Add(Path.Combine(sdkRoot, "platform-tools", tool));
            candidates.Add(Path.Combine(sdkRoot, "emulator", tool));
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var progFiles86  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var progFiles    = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            // 3. Registry — Android SDK path stored by Android Studio / Visual Studio
            var sdkFromRegistry = ReadAndroidSdkFromRegistry();
            if (sdkFromRegistry is not null)
            {
                candidates.Add(Path.Combine(sdkFromRegistry, "platform-tools", tool));
                candidates.Add(Path.Combine(sdkFromRegistry, "emulator", tool));
            }

            // 4. Visual Studio bundled Android SDK (MAUI/Xamarin workload)
            // VS installs to: C:\Program Files (x86)\Android\android-sdk
            candidates.Add(Path.Combine(progFiles86, "Android", "android-sdk", "platform-tools", tool));
            candidates.Add(Path.Combine(progFiles86, "Android", "android-sdk", "emulator", tool));
            // Hardcoded fallback in case SpecialFolder resolves differently in 64-bit process
            candidates.Add(@"C:\Program Files (x86)\Android\android-sdk\platform-tools\" + tool);
            candidates.Add(@"C:\Program Files (x86)\Android\android-sdk\emulator\" + tool);
            // Also check env var ProgramFiles(x86) directly
            var pf86env = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (pf86env is not null)
            {
                candidates.Add(Path.Combine(pf86env, "Android", "android-sdk", "platform-tools", tool));
                candidates.Add(Path.Combine(pf86env, "Android", "android-sdk", "emulator", tool));
            }

            // 5. Visual Studio installation directories (search editions)
            foreach (var vsBase in new[] { progFiles, Path.Combine(progFiles, "Microsoft Visual Studio") })
            {
                if (!Directory.Exists(vsBase)) continue;
                foreach (var year in new[] { "2022", "2019" })
                {
                    foreach (var edition in new[] { "Community", "Professional", "Enterprise", "Preview", "BuildTools" })
                    {
                        // MAUI Android SDK path inside VS
                        var vsAndroid = Path.Combine(vsBase, year, edition, "MSBuild", "Xamarin", "Android");
                        // platform-tools is typically alongside the SDK root
                        // VS also exposes it via: ...\Common7\IDE\Extensions\Xamarin\AndroidPlatformTools
                        var vsXamarin = Path.Combine(vsBase, year, edition, "Common7", "IDE", "Extensions", "Xamarin", "AndroidPlatformTools");
                        candidates.Add(Path.Combine(vsXamarin, tool));
                    }
                }
            }

            // 6. User-level Android SDK (Android Studio default on Windows)
            candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", tool));
            candidates.Add(Path.Combine(localAppData, "Android", "sdk", "platform-tools", tool));

            // 7. Android Studio installed SDK (reads from studio config)
            var studioSdk = ReadAndroidStudioSdkPath();
            if (studioSdk is not null)
            {
                candidates.Add(Path.Combine(studioSdk, "platform-tools", tool));
                candidates.Add(Path.Combine(studioSdk, "emulator", tool));
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, "Library", "Android", "sdk", "platform-tools", toolUnix));
            candidates.Add(Path.Combine(home, "Library", "Android", "sdk", "emulator", toolUnix));
            candidates.Add("/usr/local/share/android-sdk/platform-tools/" + toolUnix);
            candidates.Add("/opt/android-sdk/platform-tools/" + toolUnix);
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? ReadAndroidSdkFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            // Android Studio stores SDK path here
            using var key = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\AndroidStudio")
                ?? Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Google\Android Studio");
            var path = key?.GetValue("SdkPath") as string;
            if (path is { Length: > 0 } && Directory.Exists(path)) return path;

            // Visual Studio / Xamarin stores it here
            using var vsKey = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\VisualStudio\Xamarin");
            var vsPath = vsKey?.GetValue("AndroidSdkDirectory") as string;
            if (vsPath is { Length: > 0 } && Directory.Exists(vsPath)) return vsPath;
        }
        catch { }
        return null;
    }

    private static string? ReadAndroidStudioSdkPath()
    {
        // Android Studio writes the SDK path to its properties file
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var roaming = Path.Combine(appData, "Google");
            if (!Directory.Exists(roaming)) return null;

            foreach (var dir in Directory.GetDirectories(roaming, "AndroidStudio*"))
            {
                var props = Path.Combine(dir, "options", "jdk.table.xml");
                if (!File.Exists(props)) continue;
                // Simple string search — avoid XML dependency
                var content = File.ReadAllText(props);
                var marker = "android.sdk.path";
                var idx = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var valueStart = content.IndexOf('>', idx) + 1;
                var valueEnd   = content.IndexOf('<', valueStart);
                if (valueStart > 0 && valueEnd > valueStart)
                {
                    var path = content[valueStart..valueEnd].Trim();
                    if (Directory.Exists(path)) return path;
                }
            }
        }
        catch { }
        return null;
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
        ProcessEnvironment.UseEnglishCliOutput(psi);
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
        ProcessEnvironment.UseEnglishCliOutput(psi);
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
        ProcessEnvironment.UseEnglishCliOutput(psi);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(10_000);
        return output;
    }

    private static void TryAdd(List<iOSDevice> devices, Func<List<iOSDevice>> readDevices)
    {
        try { devices.AddRange(readDevices()); }
        catch { }
    }

    private static List<iOSDevice> DistinctDevices(List<iOSDevice> devices) =>
        devices
            .Where(d => d.Udid.Length > 0)
            .GroupBy(d => d.Udid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(d => d.Type == "Device" ? 0 : 1).First())
            .OrderBy(d => d.Type == "Device" ? 0 : 1)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

            // Format: Name (OS) (UDID). Physical devices may use 40-hex UDIDs,
            // while simulators use UUID-style identifiers.
            var m = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^(.+?)\s+\(([^)]+)\)\s+\(([0-9A-Fa-f]{8,40}(?:-[0-9A-Fa-f]{4,})*)\)");
            if (m.Success)
                devices.Add(new iOSDevice(m.Groups[1].Value.Trim(), m.Groups[3].Value, currentSection));
        }

        return devices;
    }

    private static List<iOSDevice> ParseXcdeviceList(string output)
    {
        var devices = new List<iOSDevice>();
        if (string.IsNullOrWhiteSpace(output)) return devices;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return devices;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var identifier = GetJsonString(item, "identifier");
                var name = GetJsonString(item, "name") ?? GetJsonString(item, "modelName");
                if (identifier is null || name is null) continue;

                var isSimulator = GetJsonBool(item, "simulator");
                var platform = GetJsonString(item, "platform") ?? "";
                var isIos = platform.Contains("iphoneos", StringComparison.OrdinalIgnoreCase)
                         || platform.Contains("iphonesimulator", StringComparison.OrdinalIgnoreCase)
                         || platform.Contains("iOS", StringComparison.OrdinalIgnoreCase);
                if (!isIos) continue;

                devices.Add(new iOSDevice(name, identifier, isSimulator ? "Simulator" : "Device"));
            }
        }
        catch { }

        return devices;
    }

    private static List<iOSDevice> ParseInstrumentsDevices(string output)
    {
        var devices = new List<iOSDevice>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)
                || trimmed.StartsWith("Known Devices", StringComparison.OrdinalIgnoreCase))
                continue;

            // Legacy format: iPad (15.8) [0000000000000000000000000000000000000000]
            var m = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^(.+?)\s+\(([^)]+)\)\s+\[([0-9A-Fa-f]{8,40}(?:-[0-9A-Fa-f]{4,})*)\](.*)$");
            if (!m.Success) continue;

            var tail = m.Groups[4].Value;
            var type = tail.Contains("Simulator", StringComparison.OrdinalIgnoreCase) ? "Simulator" : "Device";
            devices.Add(new iOSDevice(m.Groups[1].Value.Trim(), m.Groups[3].Value, type));
        }

        return devices;
    }

    private static string? GetJsonString(System.Text.Json.JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetJsonBool(System.Text.Json.JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == System.Text.Json.JsonValueKind.True;

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
