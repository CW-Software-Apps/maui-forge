using System.Text.Json;

namespace MauiForge.Services;

public class UpdateService
{
    public static readonly UpdateService Instance = new();

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private const string PackageId = "CwSoftware.MauiForge";
    private string? _latestVersion;
    private Task? _checkTask;

    private UpdateService() { }

    public void StartCheck()
    {
        if (_checkTask != null) return;

        _checkTask = Task.Run(async () =>
        {
            try
            {
                var url = $"https://api.nuget.org/v3-flatcontainer/{PackageId.ToLower()}/index.json";
                var response = await HttpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("versions", out var versionsProp) && versionsProp.ValueKind == JsonValueKind.Array)
                    {
                        var versions = versionsProp.EnumerateArray()
                            .Select(v => v.GetString())
                            .Where(v => v != null)
                            .ToList();
                        
                        if (versions.Count > 0)
                        {
                            _latestVersion = versions[^1]; // Last one is the latest version
                        }
                    }
                }
            }
            catch
            {
                // Ignore all connection errors, offline status, or timeouts to remain non-blocking
            }
        });
    }

    public string? GetLatestVersion()
    {
        return _latestVersion;
    }
}
