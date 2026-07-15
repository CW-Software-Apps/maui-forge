using System.Text.Json;
using MauiForge.Models;

namespace MauiForge.Services;

public class StateService
{
    private static readonly string StatePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge.state.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PersistentState Load()
    {
        try
        {
            if (!File.Exists(StatePath)) return new();
            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<PersistentState>(json) ?? new();
            if (state.MonitoredPaths.Count == 0 && !string.IsNullOrEmpty(state.ScanRootPath))
            {
                state.MonitoredPaths.Add(state.ScanRootPath);
            }
            return state;
        }
        catch { return new(); }
    }

    public void Save(PersistentState state)
    {
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOpts)); }
        catch { /* non-critical */ }
    }

    public void RecordUsage(PersistentState state, string appDir)
    {
        state.AppUsage[appDir] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Save(state);
    }
}
