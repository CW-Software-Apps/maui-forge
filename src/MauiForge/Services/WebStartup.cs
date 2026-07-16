using System.Text.Json;
using MauiForge.Models;
using MauiForge.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MauiForge.Services;

public class LogHub : Hub
{
}

public static class WebStartup
{
    private static IHubContext<LogHub>? _hubContext;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Diagnostics.Process> _runningBuilds = new();

    public static void Start(string[] args, StateService stateService, AppDiscoveryService discoveryService, VersionService versionService, GitService gitService, BuildService buildService, DeviceService deviceService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        // Configure ports
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(5123);
        });

        // Add services
        builder.Services.AddSingleton(stateService);
        builder.Services.AddSingleton(discoveryService);
        builder.Services.AddSingleton(versionService);
        builder.Services.AddSingleton(gitService);
        builder.Services.AddSingleton(buildService);
        builder.Services.AddSingleton(deviceService);

        builder.Services.AddSignalR();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            });
        });

        var app = builder.Build();

        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        _hubContext = app.Services.GetRequiredService<IHubContext<LogHub>>();

        // Paths Endpoints
        app.MapGet("/api/paths", (StateService state) =>
        {
            var st = state.Load();
            return Results.Ok(st.MonitoredPaths);
        });

        app.MapPost("/api/paths", (StateService state, PathRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Path)) return Results.BadRequest("Path cannot be empty.");
            var st = state.Load();
            if (!st.MonitoredPaths.Contains(req.Path))
            {
                st.MonitoredPaths.Add(req.Path);
                state.Save(st);
            }
            return Results.Ok(st.MonitoredPaths);
        });

        app.MapPost("/api/paths/delete", (StateService state, PathRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Path)) return Results.BadRequest("Path cannot be empty.");
            var st = state.Load();
            if (st.MonitoredPaths.Remove(req.Path))
            {
                state.Save(st);
            }
            return Results.Ok(st.MonitoredPaths);
        });

        // Apps Endpoint
        app.MapGet("/api/apps", (AppDiscoveryService discovery, StateService state) =>
        {
            var st = state.Load();
            var paths = st.MonitoredPaths;
            if (paths.Count == 0 && !string.IsNullOrEmpty(st.ScanRootPath))
            {
                paths = [st.ScanRootPath];
            }
            if (paths.Count == 0)
            {
                paths = [Directory.GetCurrentDirectory()];
            }

            var cached = st.CachedApps ?? [];

            // Background task to perform scanning
            _ = Task.Run(() =>
            {
                try
                {
                    var freshApps = discovery.FindApps(paths, depth: 2);
                    var curState = state.Load();
                    curState.CachedApps = freshApps;
                    state.Save(curState);

                    if (_hubContext != null)
                    {
                        _ = _hubContext.Clients.All.SendAsync("ScanCompleted", freshApps);
                    }
                }
                catch (Exception ex)
                {
                    _ = SendLog($"Background scan failed: {ex.Message}");
                }
            });

            return Results.Ok(cached);
        });

        // Version Update Endpoint
        app.MapPost("/api/apps/version", (VersionService versions, StateService state, VersionUpdateRequest req) =>
        {
            var st = state.Load();
            string newVersion = req.Version;
            string newBuild = req.Build;

            // 1. Unity
            var unitySettings = Path.Combine(req.Dir, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(unitySettings))
            {
                var currentUnity = versions.ReadUnity(req.Dir);
                st.LastVersion = new VersionSnapshot
                {
                    AppDir = req.Dir,
                    Version = currentUnity?.Version ?? "1.0.0",
                    Build = currentUnity?.Build ?? "1"
                };
                state.Save(st);
                versions.WriteUnity(req.Dir, newVersion, newBuild);
                return Results.Ok(new { Success = true, Version = newVersion, Build = newBuild });
            }

            // 2. Csproj
            var csproj = Directory.EnumerateFiles(req.Dir, "*.csproj").FirstOrDefault();
            if (csproj == null) return Results.BadRequest("No .csproj found.");

            var currentIos = versions.ReadiOS(req.Dir);
            var currentAndroid = versions.ReadAndroid(req.Dir);
            var currentCsproj = versions.ReadCsproj(csproj) ?? versions.ReadAssemblyInfo(req.Dir);

            // Increment log / snapshot
            st.LastVersion = new VersionSnapshot
            {
                AppDir = req.Dir,
                Version = currentCsproj?.Version ?? currentIos?.Version ?? currentAndroid?.Version ?? "1.0.0",
                Build = currentCsproj?.Build ?? currentIos?.Build ?? currentAndroid?.Build ?? "1"
            };
            state.Save(st);

            if (currentIos is not null) versions.WriteiOS(req.Dir, newVersion, newBuild);
            if (currentAndroid is not null) versions.WriteAndroid(req.Dir, newVersion, newBuild);
            if (csproj is not null) versions.WriteCsproj(csproj, newVersion, newBuild);
            versions.WriteAssemblyInfo(req.Dir, newVersion, newBuild);

            return Results.Ok(new { Success = true, Version = newVersion, Build = newBuild });
        });

        // Git Endpoints
        app.MapPost("/api/apps/git/pull", (GitService git, GitRequest req) =>
        {
            var res = git.Pull(req.Dir);
            return Results.Ok(new { Success = res.Success, Output = res.Output });
        });

        app.MapPost("/api/apps/git/push", (GitService git, GitPushRequest req) =>
        {
            var res = git.Push(req.Dir, req.Message);
            return Results.Ok(new { Success = res.Success, Output = res.Output });
        });

        app.MapPost("/api/apps/refresh", (AppDiscoveryService discovery, RefreshRequest req) =>
        {
            var appEntry = discovery.RefreshApp(req.Dir);
            if (appEntry is null) return Results.NotFound("App not found.");
            return Results.Ok(appEntry);
        });

        app.MapPost("/api/apps/bump-push", (VersionService versions, GitService git, StateService state, BumpPushRequest req) =>
        {
            var st = state.Load();
            string newVersion = req.Version;
            string newBuild = req.Build;

            // 1. Unity
            var unitySettings = Path.Combine(req.Dir, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(unitySettings))
            {
                var currentUnity = versions.ReadUnity(req.Dir);
                st.LastVersion = new VersionSnapshot
                {
                    AppDir = req.Dir,
                    Version = currentUnity?.Version ?? "1.0.0",
                    Build = currentUnity?.Build ?? "1"
                };
                state.Save(st);
                versions.WriteUnity(req.Dir, newVersion, newBuild);

                var commitMsgU = $"chore: bump version to {newVersion} #{newBuild}";
                var (gitSuccessU, gitOutputU) = git.Push(req.Dir, commitMsgU);
                return Results.Ok(new { Success = gitSuccessU, Output = gitOutputU, Version = newVersion, Build = newBuild });
            }

            // 2. Csproj
            var csproj = Directory.EnumerateFiles(req.Dir, "*.csproj").FirstOrDefault();
            if (csproj == null) return Results.BadRequest("No .csproj found.");

            var currentIos = versions.ReadiOS(req.Dir);
            var currentAndroid = versions.ReadAndroid(req.Dir);
            var currentCsproj = versions.ReadCsproj(csproj) ?? versions.ReadAssemblyInfo(req.Dir);

            st.LastVersion = new VersionSnapshot
            {
                AppDir = req.Dir,
                Version = currentCsproj?.Version ?? currentIos?.Version ?? currentAndroid?.Version ?? "1.0.0",
                Build = currentCsproj?.Build ?? currentIos?.Build ?? currentAndroid?.Build ?? "1"
            };
            state.Save(st);

            if (currentIos is not null) versions.WriteiOS(req.Dir, newVersion, newBuild);
            if (currentAndroid is not null) versions.WriteAndroid(req.Dir, newVersion, newBuild);
            if (csproj is not null) versions.WriteCsproj(csproj, newVersion, newBuild);
            versions.WriteAssemblyInfo(req.Dir, newVersion, newBuild);

            var commitMsg = $"chore: bump version to {newVersion} #{newBuild}";
            var (gitSuccess, gitOutput) = git.Push(req.Dir, commitMsg);

            return Results.Ok(new { Success = gitSuccess, Output = gitOutput, Version = newVersion, Build = newBuild });
        });

        // Build Endpoint
        app.MapPost("/api/apps/build", (BuildService builder, DeviceService devices, StateService state, BuildRequest req) =>
        {
            // Run build asynchronously in a task to not block API response, but notify client via SignalR
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendLog("=========================================");
                    await SendLog($"Starting Build for platform {req.Platform}...");
                    await SendLog("=========================================");

                    var buildArgs = new List<string> { "build" };
                    if (req.Platform.Equals("Android", StringComparison.OrdinalIgnoreCase))
                    {
                        buildArgs.AddRange(new[] { "-f", "net10.0-android" });
                    }
                    else
                    {
                        buildArgs.AddRange(new[] { "-f", "net10.0-ios" });
                    }

                    if (req.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        buildArgs.Add("-c");
                        buildArgs.Add("Release");
                    }

                    int exitCode = builder.Run(req.Dir, buildArgs.ToArray(), line =>
                    {
                        _ = SendLog(line);
                    }, onStart: proc => _runningBuilds[req.Dir] = proc);

                    // If the entry is already gone, /api/apps/build/cancel removed it and logged the cancellation.
                    bool completedNaturally = _runningBuilds.TryRemove(req.Dir, out _);
                    if (completedNaturally)
                    {
                        await SendLog("=========================================");
                        await SendLog($"Build process completed with exit code: {exitCode}");
                        await SendLog("=========================================");
                    }
                }
                catch (Exception ex)
                {
                    _runningBuilds.TryRemove(req.Dir, out _);
                    await SendLog($"[Error] Build failed to start: {ex.Message}");
                }
            });

            return Results.Accepted();
        });

        // Cancel a running build
        app.MapPost("/api/apps/build/cancel", (BuildCancelRequest req) =>
        {
            if (_runningBuilds.TryRemove(req.Dir, out var proc))
            {
                try
                {
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                    _ = SendLog("=========================================");
                    _ = SendLog("Build cancelled by user.");
                    _ = SendLog("=========================================");
                    return Results.Ok(new { Success = true });
                }
                catch (Exception ex)
                {
                    return Results.Ok(new { Success = false, Error = ex.Message });
                }
            }
            return Results.Ok(new { Success = false, Error = "No running build found for this app." });
        });

        app.MapPost("/api/apps/open-folder", (OpenFolderRequest req) =>
        {
            if (Directory.Exists(req.Dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"\"{req.Dir}\"",
                    UseShellExecute = true
                });
                return Results.Ok();
            }
            return Results.BadRequest("Directory does not exist.");
        });

        app.MapPost("/api/apps/open-ide", (OpenIdeRequest req) =>
        {
            if (Directory.Exists(req.Dir))
            {
                var sln = Directory.EnumerateFiles(req.Dir, "*.sln").FirstOrDefault();
                var csproj = Directory.EnumerateFiles(req.Dir, "*.csproj").FirstOrDefault();
                var target = sln ?? csproj ?? req.Dir;

                if (req.Ide.Equals("vscode", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("code")
                    {
                        Arguments = $"\"{req.Dir}\"",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });
                }
                else if (req.Ide.Equals("vs", StringComparison.OrdinalIgnoreCase))
                {
                    if (csproj != null || sln != null)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
                        {
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        return Results.BadRequest("No solution or csproj found for VS.");
                    }
                }
                else if (req.Ide.Equals("rider", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rider")
                    {
                        Arguments = $"\"{target}\"",
                        UseShellExecute = true
                    });
                }
                return Results.Ok();
            }
            return Results.BadRequest("Directory does not exist.");
        });

        // Update Check Endpoint
        app.MapGet("/api/update/check", async () =>
        {
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var currentStr = current is not null ? $"{current.Major}.{current.Minor}.{current.Build}" : "0.0.0";

            UpdateService.Instance.ForceCheck();
            for (var i = 0; i < 50; i++)
            {
                var latest = UpdateService.Instance.GetLatestVersion();
                if (latest is not null)
                {
                    var updateAvailable = !string.Equals(latest, currentStr, StringComparison.OrdinalIgnoreCase);
                    var updateCommand = UpdateService.GetManualUpdateCommand(latest);
                    return Results.Ok(new
                    {
                        CurrentVersion = currentStr,
                        LatestVersion = latest,
                        UpdateAvailable = updateAvailable,
                        UpdateCommand = updateCommand
                    });
                }
                await Task.Delay(100);
            }

            return Results.Ok(new
            {
                CurrentVersion = currentStr,
                LatestVersion = (string?)null,
                UpdateAvailable = false,
                UpdateCommand = (string?)null
            });
        });

        // Update Install Endpoint — runs the same deferred updater the CLI uses, minus the console prompts.
        app.MapPost("/api/update/install", async (UpdateInstallRequest req) =>
        {
            var latest = req.Version;
            if (string.IsNullOrWhiteSpace(latest))
            {
                UpdateService.Instance.ForceCheck();
                for (var i = 0; i < 50 && UpdateService.Instance.GetLatestVersion() is null; i++)
                    await Task.Delay(100);
                latest = UpdateService.Instance.GetLatestVersion();
            }

            if (string.IsNullOrWhiteSpace(latest))
                return Results.Ok(new { Success = false, Error = "Could not reach NuGet to resolve the latest version." });

            // Give the HTTP response time to reach the browser before this process exits.
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);
                UpdateService.LaunchDeferredUpdate(latest, args, interactive: false);
            });

            return Results.Ok(new { Success = true, Version = latest });
        });

        // Diagnostics Endpoint
        app.MapGet("/api/diagnostics", (DeviceService devices, GitService git) =>
        {
            // Simple diagnostic report
            return Results.Ok(new
            {
                DotnetVersion = System.Environment.Version.ToString(),
                OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription
            });
        });

        // Start Kestrel and launch browser
        var url = "http://localhost:5123";
        Console.WriteLine($"Maui-Forge Web Server started at {url}");
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore browser launch failure
        }

        app.MapHub<LogHub>("/hubs/logs");
        app.Run();
    }

    private static async Task SendLog(string message)
    {
        if (_hubContext != null)
        {
            await _hubContext.Clients.All.SendAsync("LogReceived", message);
        }
    }
}

public record PathRequest(string Path);
public record GitRequest(string Dir);
public record GitPushRequest(string Dir, string Message);
public record VersionUpdateRequest(string Dir, string Version, string Build);
public record BuildRequest(string Dir, string Platform, string Configuration);
public record BuildCancelRequest(string Dir);
public record UpdateInstallRequest(string? Version);
public record RefreshRequest(string Dir);
public record BumpPushRequest(string Dir, string Version, string Build);
public record OpenFolderRequest(string Dir);
public record OpenIdeRequest(string Dir, string Ide);
