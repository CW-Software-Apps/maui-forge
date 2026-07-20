namespace MauiForge.Services;

public class BuildService
{
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

            Task.WaitAll(readOut, readErr, proc.WaitForExitAsync());
            return proc.ExitCode;
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
        onLine(line);
        log?.WriteLine(line);
    }
}
