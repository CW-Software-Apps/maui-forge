namespace MauiForge.Models;

public class BuildRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppDir { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string Configuration { get; set; } = "Debug";
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? Version { get; set; }
    public string? BuildNumber { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }
    public string Status { get; set; } = "Running"; // Running, Success, Failed, Cancelled
    public int? ExitCode { get; set; }
    public string? LogFilePath { get; set; }
    public string? ErrorSummary { get; set; }
}
