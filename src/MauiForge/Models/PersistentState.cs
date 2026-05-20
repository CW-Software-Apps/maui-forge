namespace MauiForge.Models;

public class PersistentState
{
    public Dictionary<string, long> AppUsage { get; set; } = [];
    public string? LastCommand { get; set; }
    public string? MacHost { get; set; }
    public string? MacUser { get; set; }
    public string? Verbosity { get; set; }
    public string? LastAction { get; set; }
    public VersionSnapshot? LastVersion { get; set; }
    public string? ScanRootPath { get; set; }
    public bool UseLocalMac { get; set; } = false;
    public Dictionary<string, AppBuildConfig> AppBuildConfigs { get; set; } = [];
}

public class VersionSnapshot
{
    public string AppDir { get; set; } = "";
    public string Version { get; set; } = "";
    public string Build { get; set; } = "";
}

public class AppBuildConfig
{
    public string? BuildConfiguration { get; set; }
    public string? iOSFramework { get; set; }
    public string? AndroidFramework { get; set; }
    public string? iOSDeviceId { get; set; }
    public string? iOSDeviceName { get; set; }
    public string? iOSDeviceType { get; set; }
    public string? AndroidDeviceSerial { get; set; }
    public string? AndroidDeviceName { get; set; }
    public string? CodesignKey { get; set; }
}
