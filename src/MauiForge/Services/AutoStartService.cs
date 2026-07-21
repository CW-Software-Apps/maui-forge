using System.Diagnostics;
using Microsoft.Win32;

namespace MauiForge.Services;

public class AutoStartService
{
    private const string AppName = "MauiForge";
    private readonly LaunchAgentService _macLaunchAgent = new();

    public record AutoStartStatus(bool Supported, bool Enabled, bool IsRunning, string Platform, string Details);

    public AutoStartStatus GetStatus()
    {
        if (OperatingSystem.IsMacOS())
        {
            var macStatus = _macLaunchAgent.GetStatus();
            var isRunning = MacTrayHelper.IsTrayProcessRunning();
            return new AutoStartStatus(
                Supported: true,
                Enabled: macStatus.Installed,
                IsRunning: isRunning,
                Platform: "macOS",
                Details: macStatus.Loaded ? "LaunchAgent ativo e rodando" : (macStatus.Installed ? "LaunchAgent instalado" : "Não instalado")
            );
        }

        if (OperatingSystem.IsWindows())
        {
            var enabled = IsWindowsAutoStartEnabled();
            return new AutoStartStatus(
                Supported: true,
                Enabled: enabled,
                IsRunning: true, // Server is running current process
                Platform: "Windows",
                Details: enabled ? "Inicialização no Registro do Windows (HKCU) ativa" : "Inicialização no login não configurada"
            );
        }

        return new AutoStartStatus(false, false, true, Environment.OSVersion.Platform.ToString(), "Auto-start não suportado neste SO");
    }

    public bool ToggleAutoStart(out string message)
    {
        var current = GetStatus();
        if (!current.Supported)
        {
            message = "Auto-start não é suportado nesta plataforma.";
            return false;
        }

        var newState = !current.Enabled;
        return SetAutoStart(newState, out message);
    }

    public bool SetAutoStart(bool enable, out string message)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (enable)
            {
                var ok = _macLaunchAgent.Install();
                message = ok ? "Auto-start do macOS (LaunchAgent) instalado com sucesso." : "Falha ao instalar LaunchAgent do macOS.";
                return ok;
            }
            else
            {
                var ok = _macLaunchAgent.Uninstall();
                message = ok ? "Auto-start do macOS (LaunchAgent) removido." : "Falha ao remover LaunchAgent do macOS.";
                return ok;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null)
                {
                    message = "Não foi possível abrir a chave do Registro do Windows.";
                    return false;
                }

                if (enable)
                {
                    var dotnetToolPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".dotnet", "tools", "maui-forge.exe"
                    );

                    var execPath = File.Exists(dotnetToolPath) ? $"\"{dotnetToolPath}\"" : $"\"{Environment.ProcessPath}\"";
                    key.SetValue(AppName, execPath);
                    message = "Auto-start adicionado à inicialização do Windows (HKCU Run).";
                    return true;
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                    message = "Auto-start removido da inicialização do Windows.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = $"Erro ao configurar Registro do Windows: {ex.Message}";
                return false;
            }
        }

        message = "Plataforma não suportada.";
        return false;
    }

    private static bool IsWindowsAutoStartEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }
}
