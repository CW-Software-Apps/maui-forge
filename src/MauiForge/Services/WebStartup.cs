using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using MauiForge.Models;
using MauiForge.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MauiForge.Services;

public class LogHub : Hub
{
}

public static class WebStartup
{
    private static IHubContext<LogHub>? _hubContext;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Diagnostics.Process> _runningBuilds = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BuildRecord> _buildRecords = new();
    private static readonly List<BuildRecord> _recentBuilds = new();
    private static readonly object _buildsLock = new();
    public static string[]? OriginalArgs { get; set; }
    private static string? _serveToken;

    private static string GetBuildLogPath(string appName, string buildId)
    {
        var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge", "build_logs");
        if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
        var safeName = string.Concat(appName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(logsDir, $"{safeName}_{buildId}.log");
    }

    private static BuildRecord RecordBuildStart(string dir, string platform, string configuration = "Debug", string? deviceId = null, string? deviceName = null)
    {
        var appName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(appName)) appName = "App";

        var buildId = Guid.NewGuid().ToString("N")[..10];
        var record = new BuildRecord
        {
            Id = buildId,
            AppDir = dir,
            AppName = appName,
            Platform = platform,
            Configuration = configuration,
            DeviceId = deviceId,
            DeviceName = deviceName,
            StartTime = DateTime.UtcNow,
            Status = "Running",
            LogFilePath = GetBuildLogPath(appName, buildId)
        };

        lock (_buildsLock)
        {
            _recentBuilds.Insert(0, record);
            if (_recentBuilds.Count > 100) _recentBuilds.RemoveAt(_recentBuilds.Count - 1);
        }
        _buildRecords[record.Id] = record;
        if (_hubContext != null)
        {
            _ = _hubContext.Clients.All.SendAsync("BuildStatusUpdated", record);
        }
        return record;
    }

    private static void RecordBuildEnd(BuildRecord record, string status, int? exitCode = null, string? errorSummary = null)
    {
        record.EndTime = DateTime.UtcNow;
        record.Status = status;
        record.ExitCode = exitCode;
        record.ErrorSummary = errorSummary;

        if (_hubContext != null)
        {
            _ = _hubContext.Clients.All.SendAsync("BuildStatusUpdated", record);
        }
    }


    public static void Start(string[] args, StateService stateService, AppDiscoveryService discoveryService, VersionService versionService, GitService gitService, BuildService buildService, DeviceService deviceService,
        bool serveMode = false, string? token = null, int port = 5123)
    {
        OriginalArgs = args;
        _serveToken = serveMode ? token : null;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        builder.Logging.ClearProviders();

        // Configure ports
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (serveMode)
                options.Listen(System.Net.IPAddress.Any, port);
            else
                options.ListenLocalhost(port);
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

        // Token auth middleware (for serve/remote mode)
        app.Use(async (context, next) =>
        {
            if (_serveToken != null && context.Request.Path.StartsWithSegments("/api"))
            {
                if (context.Request.Path == "/api/remote/info")
                {
                    await next(); return;
                }
                if (!context.Request.Headers.TryGetValue("X-MauiForge-Token", out var token) || token != _serveToken)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized. Provide X-MauiForge-Token header." });
                    return;
                }
            }
            await next();
        });

        _hubContext = app.Services.GetRequiredService<IHubContext<LogHub>>();

        // Remote info endpoint (no auth required)
        app.MapGet("/api/remote/info", () =>
        {
            return Results.Ok(new
            {
                version = typeof(WebStartup).Assembly.GetName().Version?.ToString() ?? "unknown",
                hostname = Environment.MachineName,
                requiresToken = _serveToken != null,
                port,
                addresses = GetLanAddresses()
            });
        });

        // Enable server mode (generate/save token, show restart command). Accepts an
        // optional { "token": "..." } body so the user can pick their own instead of a
        // random one. If this instance is already serving, the new token applies live —
        // no restart needed to rotate it.
        app.MapPost("/api/remote/enable-server", async (StateService state, HttpContext ctx) =>
        {
            string? customToken = null;
            if (ctx.Request.ContentLength is > 0)
            {
                try
                {
                    var body = await ctx.Request.ReadFromJsonAsync<SetTokenRequest>();
                    customToken = body?.Token?.Trim();
                }
                catch (JsonException) { /* malformed/empty body — fall back to auto-generated */ }
            }

            var st = state.Load();
            var token = string.IsNullOrEmpty(customToken) ? Guid.NewGuid().ToString("N")[..12] : customToken;
            st.ServeToken = token;
            st.ServerModeEnabled = true;
            state.Save(st);

            var appliedLive = _serveToken != null;
            if (appliedLive) _serveToken = token;

            return Results.Ok(new
            {
                token,
                appliedLive,
                message = appliedLive
                    ? "Token updated — takes effect immediately, no restart needed."
                    : "Server mode configured. Restart with --serve to bind to 0.0.0.0."
            });
        });

        // Start server mode (auto-restart). Also flips ServerModeEnabled so a later plain
        // relaunch (desktop shortcut, "maui-forge" with no args) comes back up serving too.
        app.MapPost("/api/remote/start-server", (StateService state) =>
        {
            var st = state.Load();
            var token = st.ServeToken ?? Guid.NewGuid().ToString("N")[..12];
            st.ServeToken = token;
            st.ServerModeEnabled = true;
            state.Save(st);

            RelaunchSelf($"--serve --token {token} {string.Join(" ", StripServeArgs(OriginalArgs ?? []))}".TrimEnd());

            return Results.Ok(new { token, message = "Server restarting...", willRestart = true });
        });

        // Disable server mode (auto-restart back to localhost-only). Clears
        // ServerModeEnabled so future plain launches no longer come back up serving.
        app.MapPost("/api/remote/disable-server", (StateService state) =>
        {
            var st = state.Load();
            st.ServerModeEnabled = false;
            state.Save(st);

            RelaunchSelf(string.Join(" ", StripServeArgs(OriginalArgs ?? [])));

            return Results.Ok(new { message = "Server restarting in local-only mode...", willRestart = true });
        });

        // Shutdown server (for restart or quit)
        app.MapPost("/api/shutdown", () =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                Environment.Exit(0);
            });
            return Results.Ok(new { message = "Server shutting down..." });
        });

        // Scan network for remote servers
        app.MapGet("/api/remote/scan", () =>
        {
            var servers = RemoteDiscoveryService.Discover(timeoutMs: 4000);
            return Results.Ok(servers.Select(s => new
            {
                s.Host, s.Port, s.Hostname, s.TokenRequired
            }));
        });

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
        app.MapPost("/api/apps/version", (VersionService versions, AppDiscoveryService discovery, StateService state, VersionUpdateRequest req) =>
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
                RefreshCacheAndNotify(discovery, state, req.Dir);
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

            RefreshCacheAndNotify(discovery, state, req.Dir);
            return Results.Ok(new { Success = true, Version = newVersion, Build = newBuild });
        });

        // Git Endpoints
        app.MapPost("/api/apps/git/pull", (GitService git, AppDiscoveryService discovery, StateService state, GitRequest req) =>
        {
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            var res = git.Pull(dir);
            RefreshCacheAndNotify(discovery, state, dir);
            return Results.Ok(new { Success = res.Success, Output = res.Output });
        });

        app.MapPost("/api/apps/git/push", (GitService git, AppDiscoveryService discovery, StateService state, GitPushRequest req) =>
        {
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            var res = git.Push(dir, req.Message);
            RefreshCacheAndNotify(discovery, state, dir);
            return Results.Ok(new { Success = res.Success, Output = res.Output });
        });

        app.MapPost("/api/apps/refresh", (AppDiscoveryService discovery, RefreshRequest req) =>
        {
            var appEntry = discovery.RefreshApp(req.Dir);
            if (appEntry is null) return Results.NotFound("App not found.");
            return Results.Ok(appEntry);
        });

        app.MapPost("/api/apps/bump-push", (VersionService versions, GitService git, AppDiscoveryService discovery, StateService state, BumpPushRequest req) =>
        {
            var st = state.Load();
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            string newVersion = req.Version;
            string newBuild = req.Build;

            // 1. Unity
            var unitySettings = Path.Combine(dir, "ProjectSettings", "ProjectSettings.asset");
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
                RefreshCacheAndNotify(discovery, state, req.Dir);
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

            RefreshCacheAndNotify(discovery, state, req.Dir);
            return Results.Ok(new { Success = gitSuccess, Output = gitOutput, Version = newVersion, Build = newBuild });
        });

        // Build History Endpoints
        app.MapGet("/api/builds", () =>
        {
            lock (_buildsLock)
            {
                return Results.Ok(_recentBuilds);
            }
        });

        app.MapGet("/api/builds/log", (string? id, string? file) =>
        {
            string? path = null;
            if (!string.IsNullOrEmpty(id) && _buildRecords.TryGetValue(id, out var rec))
            {
                path = rec.LogFilePath;
            }
            else if (!string.IsNullOrEmpty(file))
            {
                path = file;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return Results.NotFound(new { error = "Log file not found." });
            }

            try
            {
                var content = File.ReadAllText(path);
                return Results.Ok(new { content });
            }
            catch (Exception ex)
            {
                return Results.Problem("Error reading log file: " + ex.Message);
            }
        });

        // Build Endpoint
        app.MapPost("/api/apps/build", (BuildService builder, DeviceService devices, StateService state, BuildRequest req) =>
        {
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            var record = RecordBuildStart(dir, req.Platform, req.Configuration);

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
                    else if (req.Platform.Equals("iOS", StringComparison.OrdinalIgnoreCase))
                    {
                        buildArgs.AddRange(new[] { "-f", "net10.0-ios" });
                    }

                    if (req.Configuration.Equals("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        buildArgs.Add("-c");
                        buildArgs.Add("Release");
                    }

                    int exitCode = builder.Run(dir, buildArgs.ToArray(), line =>
                    {
                        _ = SendLog(line);
                    }, logFile: record.LogFilePath, onStart: proc => _runningBuilds[dir] = proc);

                    bool completedNaturally = _runningBuilds.TryRemove(dir, out _);
                    if (completedNaturally)
                    {
                        await SendLog("=========================================");
                        await SendLog($"Build process completed with exit code: {exitCode}");
                        await SendLog("=========================================");
                        RecordBuildEnd(record, exitCode == 0 ? "Success" : "Failed", exitCode);
                    }
                    else
                    {
                        RecordBuildEnd(record, "Cancelled", -1, "Cancelled by user");
                    }
                }
                catch (Exception ex)
                {
                    _runningBuilds.TryRemove(req.Dir, out _);
                    await SendLog($"[Error] Build failed to start: {ex.Message}");
                    RecordBuildEnd(record, "Failed", -1, ex.Message);
                }
            });

            return Results.Accepted(null, new { buildId = record.Id });
        });

        // Devices Endpoint — lista devices iOS ou Android (compatível com os 3 parsers do CLI)
        app.MapPost("/api/apps/devices", (DeviceService devices, StateService state, DevicesRequest req) =>
        {
            try
            {
                var st = state.Load();

                if (req.Platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
                {
                    if (!st.UseLocalMac && (string.IsNullOrEmpty(st.MacHost) || string.IsNullOrEmpty(st.MacUser)))
                        return Results.Ok(new DevicesResponse([]));

                    var deviceList = st.UseLocalMac
                        ? devices.GetiOSDevicesLocal()
                        : devices.GetiOSDevices(st.MacHost!, st.MacUser!);

                    return Results.Ok(new DevicesResponse(
                        deviceList.Select(d => new DeviceItem(d.Udid, d.Name, d.Type)).ToList()));
                }

                if (req.Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
                {
                    var (running, avds, _) = devices.GetAndroidDevicesAndAvds();
                    var items = new List<DeviceItem>();

                    foreach (var d in running.Where(x => !x.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)))
                        items.Add(new DeviceItem(d.Serial, d.Model, "Device"));

                    var runningEmuSerials = running
                        .Where(x => x.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
                        .Select(d => d.Serial)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var d in running.Where(x => runningEmuSerials.Contains(x.Serial)))
                        items.Add(new DeviceItem(d.Serial, d.Model, "Emulator"));

                    var runningAvdNames = running
                        .Where(x => x.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
                        .Select(d => d.Model.Replace(' ', '_'))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var a in avds.Where(a => !runningAvdNames.Contains(a.Replace(' ', '_'))))
                        items.Add(new DeviceItem("avd:" + a, a, "AVD"));

                    return Results.Ok(new DevicesResponse(items));
                }

                return Results.Ok(new DevicesResponse([]));
            }
            catch
            {
                return Results.Ok(new DevicesResponse([]));
            }
        });

        // Config Endpoint — frameworks e build configurations disponíveis
        app.MapPost("/api/apps/config", (VersionService versions, ConfigRequest req) =>
        {
            try
            {
                var csproj = Directory.EnumerateFiles(req.Dir, "*.csproj").FirstOrDefault();
                List<string> configs = ["Debug", "Release"];
                List<string> frameworks = [];

                if (csproj is not null)
                {
                    var fromProject = versions.GetBuildConfigurations(csproj);
                    if (fromProject.Count > 0) configs = fromProject;
                    frameworks = versions.GetTargetFrameworks(csproj, req.Platform);
                }

                return Results.Ok(new ConfigResponse(configs, frameworks));
            }
            catch
            {
                return Results.Ok(new ConfigResponse(["Debug", "Release"], []));
            }
        });

        // Run Endpoint — build + deploy num device específico
        app.MapPost("/api/apps/run", (BuildService builder, StateService state, RunRequest req) =>
        {
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            var record = RecordBuildStart(dir, req.Platform, req.Configuration, req.DeviceId, req.DeviceName);

            _ = Task.Run(async () =>
            {
                try
                {
                    var st = state.Load();
                    var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
                    if (csproj is null)
                    {
                        await SendLog("[Error] No .csproj found in directory: " + dir);
                        await SendLog("===STEP:FAILED===");
                        RecordBuildEnd(record, "Failed", -1, "No .csproj found");
                        return;
                    }

                    await SendLog("=========================================");
                    await SendLog($"Starting {req.Platform} build & run...");
                    await SendLog($"Device: {req.DeviceName} ({req.DeviceId})");
                    await SendLog($"Config: {req.Configuration} | Framework: {req.Framework}");
                    await SendLog("=========================================");

                    var outputLines = new List<string>();
                    void OnLine(string line) { _ = SendLog(line); lock (outputLines) { outputLines.Add(line); } }

                    int exit = 0;
                    if (req.Platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
                    {
                        // Step 1: build
                        var buildArgs = new List<string>
                        {
                            "build", csproj, "-f", req.Framework, "-c", req.Configuration, "--no-incremental"
                        };
                        if (req.DeviceType == "Device")
                        {
                            buildArgs.Add("-p:RuntimeIdentifier=ios-arm64");
                            buildArgs.Add($"-p:_DeviceName={req.DeviceId}");
                        }
                        else
                        {
                            buildArgs.Add($"-p:_DeviceName=:v2:udid={req.DeviceId}");
                        }
                        if (!st.UseLocalMac)
                        {
                            buildArgs.Add($"-p:ServerAddress={st.MacHost}");
                            buildArgs.Add($"-p:ServerUser={st.MacUser}");
                        }

                        await SendLog("===STEP:BUILD===");
                        exit = builder.Run(dir, buildArgs.ToArray(), OnLine,
                            logFile: record.LogFilePath,
                            onStart: proc => _runningBuilds[dir] = proc);
                        _runningBuilds.TryRemove(dir, out _);

                        if (exit != 0)
                        {
                            await SendLog("===STEP:FAILED===");
                            RecordBuildEnd(record, "Failed", exit, "iOS build step failed");
                            return;
                        }

                        // Step 2: run
                        var runArgs = new List<string>
                        {
                            "build", csproj, "-t:Run", "-f", req.Framework, "-c", req.Configuration
                        };
                        if (req.DeviceType == "Device")
                        {
                            runArgs.Add("-p:RuntimeIdentifier=ios-arm64");
                            runArgs.Add($"-p:_DeviceName={req.DeviceId}");
                        }
                        else
                        {
                            runArgs.Add($"-p:_DeviceName=:v2:udid={req.DeviceId}");
                        }
                        if (!st.UseLocalMac)
                        {
                            runArgs.Add($"-p:ServerAddress={st.MacHost}");
                            runArgs.Add($"-p:ServerUser={st.MacUser}");
                        }

                        await SendLog("===STEP:DEPLOY===");
                        exit = builder.Run(dir, runArgs.ToArray(), OnLine,
                            logFile: record.LogFilePath,
                            onStart: proc => _runningBuilds[dir] = proc);
                        _runningBuilds.TryRemove(dir, out _);

                        // mlaunch often exits 134 after successfully launching the app
                        // (it tries to read stdin for --wait-for-exit in non-interactive context)
                        if (exit != 0 && outputLines.Any(l => l.Contains("Launched application", StringComparison.OrdinalIgnoreCase)))
                        {
                            await SendLog("── APP LAUNCHED SUCCESSFULLY ──────────────────────────────");
                            await SendLog("The iOS deploy tool (mlaunch) reported an exit code " + exit + ",");
                            await SendLog("but this is a KNOWN false positive — mlaunch crashes after");
                            await SendLog("launch because it cannot read stdin in non-interactive mode.");
                            await SendLog("Your app IS running on " + req.DeviceName + ".");
                            await SendLog("───────────────────────────────────────────────────────────");
                            exit = 0;
                        }

                        SaveRunConfig(st, req, csproj);
                        state.Save(st);

                        if (exit == 0) await SendLog("===STEP:DONE===");
                        else await SendLog("===STEP:FAILED===");
                        await SendLog(exit == 0 ? "iOS app launched successfully." : $"iOS launch failed (exit {exit}).");
                        RecordBuildEnd(record, exit == 0 ? "Success" : "Failed", exit);
                    }
                    else if (req.Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
                    {
                        string? serial = req.DeviceId;

                        if (serial.StartsWith("avd:", StringComparison.OrdinalIgnoreCase))
                        {
                            var avdName = serial[4..];
                            var adbPath = DeviceService.FindAdb();
                            if (adbPath is null)
                            {
                                await SendLog("[Error] adb not found.");
                                await SendLog("[Hint] Set ANDROID_HOME or install Android SDK platform-tools.");
                                await SendLog("===STEP:FAILED===");
                                RecordBuildEnd(record, "Failed", -1, "adb not found");
                                return;
                            }

                            await SendLog($"Starting emulator: {avdName}...");
                            serial = await StartAvdAndWaitForWeb(avdName, adbPath);
                            if (serial is null)
                            {
                                await SendLog("[Error] Emulator did not start in time.");
                                await SendLog("===STEP:FAILED===");
                                RecordBuildEnd(record, "Failed", -1, "Emulator start timeout");
                                return;
                            }
                        }

                        await SendLog("===STEP:BUILD===");
                        var runArgs = new List<string>
                        {
                            "build", csproj, "-t:Run", "-f", req.Framework, "-c", req.Configuration,
                            $"-p:AdbTarget=-s {serial}"
                        };

                        exit = builder.Run(dir, runArgs.ToArray(), OnLine,
                            logFile: record.LogFilePath,
                            onStart: proc => _runningBuilds[dir] = proc);
                        _runningBuilds.TryRemove(dir, out _);

                        SaveRunConfig(st, req, csproj);
                        state.Save(st);

                        if (exit == 0) await SendLog("===STEP:DONE===");
                        else await SendLog("===STEP:FAILED===");
                        await SendLog(exit == 0 ? "Android app launched successfully." : $"Android launch failed (exit {exit}).");
                        RecordBuildEnd(record, exit == 0 ? "Success" : "Failed", exit);
                    }
                }
                catch (Exception ex)
                {
                    _runningBuilds.TryRemove(dir, out _);
                    await SendLog($"[Error] Build & Run failed: {ex.Message}");
                    await SendLog("===STEP:FAILED===");
                    RecordBuildEnd(record, "Failed", -1, ex.Message);
                }
            });

            return Results.Accepted(null, new { buildId = record.Id });
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

        app.MapPost("/api/apps/open-folder", (StateService state, OpenFolderRequest req) =>
        {
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            if (Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"\"{dir}\"",
                    UseShellExecute = true
                });
                return Results.Ok();
            }
            return Results.BadRequest("Directory does not exist.");
        });

        app.MapPost("/api/apps/open-ide", (StateService state, OpenIdeRequest req) =>
        {
            var dir = PathUtils.NormalizeOrRepairPath(req.Dir, state);
            if (Directory.Exists(dir))
            {
                var sln = Directory.EnumerateFiles(dir, "*.sln").FirstOrDefault();
                var csproj = Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
                var target = sln ?? csproj ?? dir;

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

    private static void RefreshCacheAndNotify(AppDiscoveryService discovery, StateService state, string dir)
    {
        try
        {
            var updated = discovery.RefreshApp(dir);
            if (updated != null)
            {
                var st = state.Load();
                var cached = st.CachedApps ?? new List<AppEntry>();
                var index = cached.FindIndex(a => a.Dir.Equals(dir, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    cached[index] = updated;
                }
                else
                {
                    cached.Add(updated);
                }
                st.CachedApps = cached;
                state.Save(st);

                if (_hubContext != null)
                {
                    _ = _hubContext.Clients.All.SendAsync("AppUpdated", updated);
                }
            }
        }
        catch { }
    }

    private static void SaveRunConfig(PersistentState st, RunRequest req, string csproj)
    {
        if (!st.AppBuildConfigs.TryGetValue(req.Dir, out var cfg))
        {
            cfg = new AppBuildConfig();
            st.AppBuildConfigs[req.Dir] = cfg;
        }

        cfg.BuildConfiguration = req.Configuration;

        if (req.Platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
        {
            cfg.iOSDeviceId = req.DeviceId;
            cfg.iOSDeviceName = req.DeviceName;
            cfg.iOSDeviceType = req.DeviceType;
            cfg.iOSFramework = req.Framework;
        }
        else
        {
            cfg.AndroidDeviceSerial = req.DeviceId;
            cfg.AndroidDeviceName = req.DeviceName;
            cfg.AndroidFramework = req.Framework;
        }
    }

    private static async Task<string?> StartAvdAndWaitForWeb(string avdName, string adbPath)
    {
        var emulatorPath = DeviceService.FindEmulator();
        if (emulatorPath is null) { await SendLog("[Error] emulator binary not found."); return null; }

        await SendLog($"Starting emulator: {emulatorPath} -avd {avdName}...");
        var psi = new System.Diagnostics.ProcessStartInfo(emulatorPath)
        {
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-avd");
        psi.ArgumentList.Add(avdName);
        System.Diagnostics.Process.Start(psi);

        string? found = null;

        // Phase 1: wait for emulator in adb devices (up to 60s)
        for (var i = 0; i < 30 && found is null; i++)
        {
            await Task.Delay(2000);
            var adbPsi = new System.Diagnostics.ProcessStartInfo(adbPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            adbPsi.ArgumentList.Add("devices");
            using var proc = System.Diagnostics.Process.Start(adbPsi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var emLine = output.Split('\n').Skip(1)
                .FirstOrDefault(l =>
                {
                    var parts = l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length >= 2 && parts[0].StartsWith("emulator-") && parts[1] == "device";
                });

            if (emLine is not null)
                found = emLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        }

        if (found is null) { await SendLog("[Error] Emulator did not appear in adb devices after 60s."); return null; }
        await SendLog($"Emulator online: {found}");

        // Phase 2: wait for sys.boot_completed (up to 90s)
        await SendLog("Waiting for Android to finish booting...");
        for (var i = 0; i < 45; i++)
        {
            await Task.Delay(2000);
            var bootPsi = new System.Diagnostics.ProcessStartInfo(adbPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            bootPsi.ArgumentList.Add("-s"); bootPsi.ArgumentList.Add(found);
            bootPsi.ArgumentList.Add("shell"); bootPsi.ArgumentList.Add("getprop"); bootPsi.ArgumentList.Add("sys.boot_completed");
            using var bootProc = System.Diagnostics.Process.Start(bootPsi)!;
            var bootOut = bootProc.StandardOutput.ReadToEnd().Trim();
            bootProc.WaitForExit(5000);
            if (bootOut == "1") { await SendLog("Android boot completed."); return found; }
        }

        await SendLog("[Warning] Emulator boot check timeout — proceeding anyway.");
        return found;
    }

    private static async Task SendLog(string message)
    {
        if (_hubContext != null)
        {
            await _hubContext.Clients.All.SendAsync("LogReceived", message);
        }
    }

    // Drops --serve and --token <value> from a previous launch's args so a relaunch
    // (either into or out of serve mode) doesn't inherit a stale flag or leave the
    // token's value behind as a stray positional argument.
    private static List<string> StripServeArgs(string[] args)
    {
        var result = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--serve") continue;
            if (args[i] == "--token") { i++; continue; }
            result.Add(args[i]);
        }
        return result;
    }

    // Writes a small wait-then-relaunch script (batch on Windows, shell elsewhere) that
    // waits for this process to exit, then starts a new one with the given arguments —
    // used to hop in and out of server mode from the UI without a manual restart.
    private static void RelaunchSelf(string args)
    {
        var pid = Environment.ProcessId;
        var selfExe = Environment.ProcessPath ?? "maui-forge";

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-relaunch.bat");
            var batch = "@echo off\r\n" +
                "chcp 65001 > nul\r\n" +
                "title MAUI Forge\r\n" +
                $"set \"PID={pid}\"\r\n" +
                ":wait\r\n" +
                "tasklist /FI \"PID eq %PID%\" 2>NUL | find \"%PID%\" >NUL\r\n" +
                "if %ERRORLEVEL% == 0 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                $"\"{selfExe}\" {args}\r\n";
            File.WriteAllText(script, batch);
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c start \"\" \"{script}\"")
            {
                UseShellExecute = true,
                CreateNoWindow = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        else
        {
            var script = Path.Combine(Path.GetTempPath(), "maui-forge-relaunch.sh");
            var sh = "#!/bin/bash\n" +
                $"PID={pid}\n" +
                "while kill -0 $PID 2>/dev/null; do sleep 1; done\n" +
                $"exec {selfExe} {args}\n";
            File.WriteAllText(script, sh);
            System.Diagnostics.Process.Start("chmod", $"+x {script}");
            System.Diagnostics.Process.Start("nohup", $"{script} &");
        }
    }

    private static List<string> GetLanAddresses()
    {
        var addresses = new List<string>();
        try
        {
            foreach (var nic in NetworkUtils.GetActiveLanInterfaces())
            {
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    addresses.Add(addr.Address.ToString());
                }
            }
        }
        catch { /* best effort — an empty list just means the UI falls back to "your machine's LAN IP" */ }
        return addresses;
    }
}

public record SetTokenRequest(string? Token);
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
public record DevicesRequest(string Dir, string Platform);
public record DeviceItem(string Id, string Name, string Type);
public record DevicesResponse(List<DeviceItem> Devices);
public record ConfigRequest(string Dir, string Platform);
public record ConfigResponse(List<string> Configurations, List<string> Frameworks);
public record RunRequest(string Dir, string Platform, string DeviceId, string DeviceName, string DeviceType, string Configuration, string Framework);
