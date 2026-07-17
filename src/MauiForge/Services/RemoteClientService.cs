using System.Net.Http.Json;
using System.Text.Json;
using MauiForge.Models;

namespace MauiForge.Services;

public class RemoteClientService
{
    private HttpClient _http = new();
    private string _token = "";
    public RemoteServerInfo? CurrentServer { get; private set; }
    public bool IsConnected => CurrentServer != null;

    public void Connect(RemoteServerInfo server, string? token)
    {
        var baseUrl = $"http://{server.Host}:{server.Port}";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _token = token ?? "";
        _http.DefaultRequestHeaders.Remove("X-MauiForge-Token");
        if (!string.IsNullOrEmpty(_token))
            _http.DefaultRequestHeaders.Add("X-MauiForge-Token", _token);
        CurrentServer = server;
    }

    public void Disconnect()
    {
        _http = new HttpClient();
        _token = "";
        CurrentServer = null;
    }

    // ── Info ────────────────────────────────────────────────────

    public async Task<JsonElement> GetInfoAsync()
    {
        var res = await _http.GetAsync("/api/remote/info");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    // ── Apps ────────────────────────────────────────────────────

    public async Task<List<AppEntry>?> GetAppsAsync()
    {
        var res = await _http.GetAsync("/api/apps");
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<AppEntry>>();
    }

    public async Task<AppEntry?> RefreshAppAsync(string dir)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/refresh", new { dir });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<AppEntry>();
    }

    // ── Version ─────────────────────────────────────────────────

    public async Task<string> SetVersionAsync(string dir, string version, string build)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/version", new { dir, version, build });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> BumpPushAsync(string dir, string version, string build)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/bump-push", new { dir, version, build });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    // ── Git ─────────────────────────────────────────────────────

    public async Task<string> GitPullAsync(string dir)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/git/pull", new { dir });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    public async Task<string> GitPushAsync(string dir, string message)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/git/push", new { dir, message });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    // ── Build ───────────────────────────────────────────────────

    public async Task StartBuildAsync(string dir, string platform, string configuration)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/build", new { dir, platform, configuration });
        res.EnsureSuccessStatusCode();
    }

    public async Task CancelBuildAsync(string dir)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/build/cancel", new { dir });
        res.EnsureSuccessStatusCode();
    }

    // ── Build & Run ─────────────────────────────────────────────

    public async Task StartRunAsync(string dir, string platform, string deviceId, string deviceName, string deviceType, string configuration, string framework)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/run", new
        {
            dir, platform, deviceId, deviceName, deviceType, configuration, framework
        });
        res.EnsureSuccessStatusCode();
    }

    public async Task<DevicesResponse?> GetDevicesAsync(string dir, string platform)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/devices", new { dir, platform });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<DevicesResponse>();
    }

    public async Task<ConfigResponse?> GetConfigAsync(string dir, string platform)
    {
        var res = await _http.PostAsJsonAsync("/api/apps/config", new { dir, platform });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<ConfigResponse>();
    }
}

// DeviceItem, DevicesResponse, ConfigResponse are defined in WebStartup.cs
