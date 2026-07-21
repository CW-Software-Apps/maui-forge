using System.Diagnostics;
using System.Reflection;

namespace MauiForge.Services;

public static class MacTrayHelper
{
    private static readonly string AgentStateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".maui-forge");

    public static readonly string TrayBinaryPath =
        Path.Combine(AgentStateDir, "mac-tray", "mac-tray");

    private static readonly string TrayVersionFile =
        Path.Combine(AgentStateDir, "mac-tray", ".version");

    private static readonly string CliVersion =
        typeof(MacTrayHelper).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
        ?? typeof(MacTrayHelper).Assembly.GetName().Version?.ToString(3)
        ?? "1.6.30";

    public static bool IsTrayInstalled() => File.Exists(TrayBinaryPath);

    public static bool IsTrayStale() => File.Exists(TrayBinaryPath) && IsTrayBinaryStale();

    private static bool IsTrayBinaryStale()
    {
        try
        {
            if (!File.Exists(TrayVersionFile)) return true;
            var saved = File.ReadAllText(TrayVersionFile).Trim();
            return saved != CliVersion;
        }
        catch { return true; }
    }

    public static bool IsTrayProcessRunning()
    {
        try { return Process.GetProcessesByName("mac-tray").Length > 0; }
        catch { return false; }
    }

    public static (bool Success, string Message) LaunchOrActivate()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return (false, "Menu bar (Tray) está disponível apenas no macOS.");
        }

        // Encerra instância prévia do mac-tray para atualizar/relançar
        try
        {
            foreach (var p in Process.GetProcessesByName("mac-tray"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }
        }
        catch { }

        var (binary, installError) = GetOrInstallBinary();
        if (binary == null)
        {
            var reason = string.IsNullOrWhiteSpace(installError)
                ? "binário não encontrado e não foi possível compilar via swiftc"
                : installError;
            return (false, $"⚠ Helper da menu bar não pôde ser iniciado: {reason}");
        }

        try
        {
            var existing = Process.GetProcessesByName("mac-tray");
            if (existing.Length > 0)
            {
                var psi = new ProcessStartInfo("open", $"\"{binary}\"")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                return (true, "Menu bar (mac-tray) já estava em execução. Trazendo para a frente...");
            }

            var startPsi = new ProcessStartInfo(binary)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(binary)
            };
            Process.Start(startPsi);
            return (true, "✅ Menu bar do MAUI Forge iniciada com sucesso na barra de status do macOS.");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao iniciar menu bar: {ex.Message}");
        }
    }

    private static (string? Binary, string? Error) GetOrInstallBinary()
    {
        // 1) Tenta extrair binário pré-compilado do recurso embarcado
        try
        {
            var assembly = typeof(MacTrayHelper).Assembly;
            var resourceName = "MauiForge.Resources.mac-tray.mac-tray";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TrayBinaryPath)!);
                using var fileStream = new FileStream(TrayBinaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(fileStream);
                fileStream.Flush();
                Process.Start("chmod", $"+x \"{TrayBinaryPath}\"")?.WaitForExit();
                WriteVersionFile();
                return (TrayBinaryPath, null);
            }
        }
        catch { }

        // 2) Binário já existe no disco e está atualizado
        if (File.Exists(TrayBinaryPath) && !IsTrayBinaryStale())
            return (TrayBinaryPath, null);

        // 3) Recompila do fonte Swift se estiver no macOS
        if (OperatingSystem.IsMacOS())
        {
            var result = CompileFromSource();
            if (result.Binary != null)
                return result;
        }

        // 4) Recurso de último caso
        if (File.Exists(TrayBinaryPath))
            return (TrayBinaryPath, "usando binário prévio (recompilação recomendada)");

        return (null, "Não foi possível preparar o binário do mac-tray");
    }

    private static (string? Binary, string? Error) CompileFromSource()
    {
        try
        {
            var targetDir = Path.GetDirectoryName(TrayBinaryPath)!;
            Directory.CreateDirectory(targetDir);

            string? swiftContent = null;
            var assembly = typeof(MacTrayHelper).Assembly;
            var swiftResource = "MauiForge.Resources.mac-tray.main.swift";
            using (var stream = assembly.GetManifestResourceStream(swiftResource))
            {
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    swiftContent = reader.ReadToEnd();
                }
            }

            if (string.IsNullOrEmpty(swiftContent))
            {
                return (null, "Não foi possível carregar o arquivo main.swift dos recursos embarcados.");
            }

            var tempSwift = Path.Combine(targetDir, "main.swift");
            File.WriteAllText(tempSwift, swiftContent);

            var psi = new ProcessStartInfo("swiftc", $"-O \"{tempSwift}\" -o \"{TrayBinaryPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var proc = Process.Start(psi);
            if (proc == null) return (null, "Falha ao iniciar o compilador swiftc");

            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            try { File.Delete(tempSwift); } catch { }

            if (proc.ExitCode != 0)
            {
                return (null, $"Erro na compilação do main.swift: {stderr}");
            }

            Process.Start("chmod", $"+x \"{TrayBinaryPath}\"")?.WaitForExit();
            WriteVersionFile();
            return (TrayBinaryPath, null);
        }
        catch (Exception ex)
        {
            return (null, $"Exceção ao compilar main.swift: {ex.Message}");
        }
    }

    private static void WriteVersionFile()
    {
        try
        {
            File.WriteAllText(TrayVersionFile, CliVersion);
        }
        catch { }
    }
}
