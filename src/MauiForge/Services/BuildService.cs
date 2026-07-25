using System.Collections.Concurrent;

namespace MauiForge.Services;

public class BuildService
{
    private static readonly ConcurrentDictionary<string, System.Diagnostics.Process> _hotReloadProcesses = new();

    public bool IsHotReloadActive(string dir) =>
        _hotReloadProcesses.TryGetValue(dir, out var proc) && proc is not null && !proc.HasExited;

    public List<string> GetActiveHotReloadDirs() =>
        _hotReloadProcesses
            .Where(kvp => kvp.Value is not null && !kvp.Value.HasExited)
            .Select(kvp => kvp.Key)
            .ToList();

    public System.Diagnostics.Process? StartHotReload(string dir, string[] args, Action<string> onLine, Dictionary<string, string>? envVars = null)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.EnvironmentVariables["DOTNET_WATCH_SUPPRESS_PROMPTS"] = "1";
            psi.EnvironmentVariables["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
            psi.EnvironmentVariables["DOTNET_WATCH_RESTART_ON_BUILD_ERROR"] = "1";
            ProcessEnvironment.UseEnglishCliOutput(psi);
            if (envVars is not null)
                foreach (var kv in envVars)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;
            foreach (var a in args) psi.ArgumentList.Add(a);

            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return null;

            _hotReloadProcesses[dir] = proc;

            _ = Task.Run(async () =>
            {
                try
                {
                    var readOut = Task.Run(async () =>
                    {
                        while (await proc.StandardOutput.ReadLineAsync() is { } line)
                            HandleLine(line, onLine, null);
                    });
                    var readErr = Task.Run(async () =>
                    {
                        while (await proc.StandardError.ReadLineAsync() is { } line)
                            HandleLine(line, onLine, null);
                    });
                    await Task.WhenAll(readOut, readErr, proc.WaitForExitAsync());
                }
                catch { }
                finally
                {
                    _hotReloadProcesses.TryRemove(KeyValuePair.Create(dir, proc));
                }
            });

            return proc;
        }
        catch (Exception ex)
        {
            onLine($"[HotReload Error] {ex.Message}");
            return null;
        }
    }

    public bool StopHotReload(string dir)
    {
        if (_hotReloadProcesses.TryRemove(dir, out var proc))
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                return true;
            }
            catch { return false; }
        }
        return false;
    }
    public int Run(string dir, string[] args, Action<string> onLine, string? logFile = null, Action<System.Diagnostics.Process>? onStart = null)
    {
        StreamWriter? log = null;
        if (logFile is not null)
        {
            log = new StreamWriter(logFile, append: false, System.Text.Encoding.UTF8);
            log.WriteLine($"=== dotnet {string.Join(' ', args)} ===");
            log.WriteLine($"=== Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            ProcessEnvironment.UseEnglishCliOutput(psi);
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = System.Diagnostics.Process.Start(psi)!;
            onStart?.Invoke(proc);

            var readOut = Task.Run(async () =>
            {
                while (await proc.StandardOutput.ReadLineAsync() is { } line)
                {
                    HandleLine(line, onLine, log);
                }
            });

            var readErr = Task.Run(async () =>
            {
                while (await proc.StandardError.ReadLineAsync() is { } line)
                {
                    HandleLine(line, onLine, log);
                }
            });

            try
            {
                Task.WaitAll([readOut, readErr, proc.WaitForExitAsync()]);
            }
            catch { }
            return proc.HasExited ? proc.ExitCode : -1;
        }
        catch (Exception ex)
        {
            onLine?.Invoke($"[Build Error] {ex.Message}");
            return -1;
        }
        finally
        {
            log?.Flush();
            log?.Dispose();
        }
    }

    private static void HandleLine(string? line, Action<string> onLine, StreamWriter? log)
    {
        if (line is null) return;
        try
        {
            onLine(line);
            log?.WriteLine(line);
        }
        catch { }
    }
}

