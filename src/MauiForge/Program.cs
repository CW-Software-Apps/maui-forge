using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MauiForge.Models;
using MauiForge.Services;
using MauiForge.UI;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding  = Encoding.UTF8;

AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    try
    {
        var log = Path.Combine(Path.GetTempPath(), "maui-forge-error.log");
        File.AppendAllText(log, $"[{DateTime.Now}] UnhandledException: {e.ExceptionObject}\n");
    }
    catch { }
};

TaskScheduler.UnobservedTaskException += (s, e) =>
{
    try
    {
        var log = Path.Combine(Path.GetTempPath(), "maui-forge-error.log");
        File.AppendAllText(log, $"[{DateTime.Now}] UnobservedTaskException: {e.Exception}\n");
    }
    catch { }
    e.SetObserved();
};

// On macOS/Linux, dotnet tool install prints a broken pt-BR PATH warning.
// Show the correct instructions once if the tools dir is not in PATH yet.
if (!OperatingSystem.IsWindows())
{
    var toolsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
    var pathEnv  = Environment.GetEnvironmentVariable("PATH") ?? "";
    if (!pathEnv.Split(':').Any(p => p.TrimEnd('/') == toolsDir.TrimEnd('/')))
    {
        var profile = Environment.GetEnvironmentVariable("SHELL")?.EndsWith("zsh") == true
            ? "~/.zprofile" : "~/.bash_profile";
        AnsiConsole.MarkupLine($"[yellow]maui-forge is installed but [bold]~/.dotnet/tools[/] is not in your PATH.[/]");
        AnsiConsole.MarkupLine($"[grey]Run this once to fix it permanently:[/]");
        AnsiConsole.MarkupLine($"[cyan]echo 'export PATH=\"$PATH:{toolsDir}\"' >> {profile} && source {profile}[/]");
        AnsiConsole.MarkupLine($"[grey]Then reopen your terminal.[/]");
        Console.WriteLine();
    }
}

UpdateService.Instance.StartCheck();

var services = new ServiceCollection()
    .AddSingleton<GitService>()
    .AddSingleton<VersionService>()
    .AddSingleton<BuildService>()
    .AddSingleton<DeviceService>()
    .AddSingleton<StateService>()
    .AddSingleton<AppDiscoveryService>()
    .AddSingleton<AiCommitService>()
    .AddSingleton<AppDetailScreen>()
    .BuildServiceProvider();

var depth       = 2;
var cliPath     = (string?)null;
var runTerminal = false;
var runUpdate   = false;
var runHelp     = false;
var serveMode   = false;
var serveToken  = (string?)null;
var servePort   = 5123;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--depth" && i + 1 < args.Length && int.TryParse(args[i + 1], out var d)) depth = d;
    if (args[i] == "--path"  && i + 1 < args.Length) cliPath = args[i + 1];
    if (args[i] == "--cli" || args[i] == "--terminal") runTerminal = true;
    if (args[i] == "--update") runUpdate = true;
    if (args[i] == "--help" || args[i] == "-h" || args[i] == "/?") runHelp = true;
    if (args[i] == "--serve") serveMode = true;
    if (args[i] == "--token" && i + 1 < args.Length) serveToken = args[i + 1];
    if (args[i] == "--port"  && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) servePort = p;
}

// Server Mode persists across restarts once enabled from the UI — a plain launch
// (double-click, desktop shortcut, "maui-forge" with no args) picks it back up
// automatically until explicitly disabled, instead of silently reverting to
// localhost-only and requiring the --serve flag to be retyped every time.
if (!serveMode)
{
    var persisted = services.GetRequiredService<StateService>().Load();
    if (persisted.ServerModeEnabled && !string.IsNullOrEmpty(persisted.ServeToken))
    {
        serveMode = true;
        serveToken = persisted.ServeToken;
    }
}

if (args.Length > 0 && (args[0] is "tray" or "--tray"))
{
    var (ok, msg) = MacTrayHelper.LaunchOrActivate();
    Console.WriteLine(msg);
    return;
}

if (args.Length > 0 && (args[0] is "autostart" or "service"))
{
    if (!OperatingSystem.IsMacOS())
    {
        Console.WriteLine("O comando autostart/service está disponível apenas no macOS.");
        return;
    }
    var launchAgent = new LaunchAgentService();
    var action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
    switch (action)
    {
        case "install":
            var installed = launchAgent.Install();
            Console.WriteLine(installed ? "✅ Auto-start (LaunchAgent) instalado e iniciado com sucesso." : "❌ Falha ao instalar auto-start.");
            return;
        case "uninstall":
        case "remove":
            var uninstalled = launchAgent.Uninstall();
            Console.WriteLine(uninstalled ? "✅ Auto-start (LaunchAgent) removido." : "❌ Falha ao remover auto-start.");
            return;
        case "logs":
            Console.WriteLine(launchAgent.GetLogs());
            return;
        case "status":
        default:
            var agentStatus = launchAgent.GetStatus();
            Console.WriteLine($"Instalado: {(agentStatus.Installed ? "Sim" : "Não")}");
            Console.WriteLine($"Ativo:     {(agentStatus.Loaded ? "Sim" : "Não")}");
            Console.WriteLine($"Label:     {agentStatus.Label}");
            Console.WriteLine($"Plist:     {agentStatus.PlistPath}");
            if (!string.IsNullOrWhiteSpace(agentStatus.Details)) Console.WriteLine($"Detalhes:  {agentStatus.Details}");
            return;
    }
}

if (runHelp)
{
    AnsiConsole.MarkupLine("[bold cyan1]MAUI Forge Command Line Help[/]");
    AnsiConsole.MarkupLine("Usage:");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge[/]                         Starts the local Web Dashboard (default)");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge tray[/]                    Opens the macOS Status Bar tray icon");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge autostart [action][/]      Manages macOS LaunchAgent (install/uninstall/status/logs)");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge --cli[/]                  Starts the traditional terminal interface");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge --update[/]               Forces check and installs updates");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge --serve --token X[/]      Starts in remote server mode (binds 0.0.0.0)");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge --serve --port 9000[/]    Server mode on custom port");
    AnsiConsole.MarkupLine("  [cyan1]maui-forge --help[/]                 Shows this help message");
    return;
}

if (runUpdate)
{
    AnsiConsole.MarkupLine("[cyan1]Checking for updates...[/]");
    UpdateService.Instance.ForceCheck();
    for (var i = 0; i < 60 && UpdateService.Instance.GetLatestVersion() is null; i++)
        System.Threading.Thread.Sleep(100);
    
    var latestStr = UpdateService.Instance.GetLatestVersion();
    var currentVer = typeof(AppListScreen).Assembly.GetName().Version;
    
    if (latestStr != null && currentVer != null)
    {
        var cleanLatest = latestStr.Split('-')[0];
        if (Version.TryParse(cleanLatest, out var latestVer) && latestVer > currentVer)
        {
            AnsiConsole.MarkupLine($"[cyan1 bold]↑ New version available: {latestStr} (installed: {currentVer.ToString(3)})[/]");
            AnsiConsole.MarkupLine("[dim]Closing and updating. If it does not update, check maui-forge-update.log in your temp folder.[/]");
            try
            {
                System.Threading.Thread.Sleep(500);
                UpdateService.LaunchDeferredUpdate(latestStr, args);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]x  Could not start updater:[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine($"[dim]Run manually:[/] [cyan1]{Markup.Escape(UpdateService.GetManualUpdateCommand(latestStr))}[/]");
            }
            return;
        }
    }
    
    AnsiConsole.MarkupLine("[green]✓ Already up to date.[/]");
    return;
}

var stateService = services.GetRequiredService<StateService>();
var discovery    = services.GetRequiredService<AppDiscoveryService>();
var detail       = services.GetRequiredService<AppDetailScreen>();
var deviceSvc    = services.GetRequiredService<DeviceService>();

if (!runTerminal)
{
    var versionSvc = services.GetRequiredService<VersionService>();
    var gitSvc     = services.GetRequiredService<GitService>();
    var buildSvc   = services.GetRequiredService<BuildService>();

    if (serveMode)
    {
        if (string.IsNullOrEmpty(serveToken))
        {
            serveToken = Guid.NewGuid().ToString("N")[..12];
            AnsiConsole.MarkupLine($"[yellow]Remote access token: [bold]{serveToken}[/][/]");
        }
        AnsiConsole.MarkupLine($"[dim]Server mode on port [white]{servePort}[/]. Clients connect via [cyan1]http://<this-ip>:{servePort}[/][/]");
        AnsiConsole.MarkupLine($"[dim]Token required: [white]{serveToken}[/][/]");
        System.Threading.Thread.Sleep(1500);

        // Start UDP discovery responder
        var discoverySvc = new RemoteDiscoveryService();
        discoverySvc.StartResponder(webPort: servePort, token: serveToken);
    }

    WebStartup.Start(args, stateService, discovery, versionSvc, gitSvc, buildSvc, deviceSvc,
        serveMode: serveMode, token: serveToken, port: servePort);
    return;
}

var st       = stateService.Load();
var scanPath = cliPath ?? st.ScanRootPath ?? Directory.GetCurrentDirectory();

if (OperatingSystem.IsMacOS() && !st.UseLocalMac)
{
    st.UseLocalMac = true;
    stateService.Save(st);
}

var nameFilter     = (string?)null;
var platformFilter = "All";
var needsScan      = true;
List<AppEntry> allApps = [];

while (true)
{
    if (needsScan)
    {
        AnsiConsole.Clear();
        AppListScreen.RenderHeader();
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan1"))
            .Start("[dim]Scanning for apps...[/]", _ =>
            {
                allApps = discovery.FindApps(scanPath, depth);
            });
        needsScan = false;
    }

    var apps   = Filter(allApps, nameFilter, platformFilter);
    var result = AppListScreen.Show(apps, scanPath, st, nameFilter, platformFilter);

    if (result is ListQuit) break;

    if (result is WebInterfaceRequested)
    {
        var versionSvc = services.GetRequiredService<VersionService>();
        var gitSvc     = services.GetRequiredService<GitService>();
        var buildSvc   = services.GetRequiredService<BuildService>();
        WebStartup.Start(args, stateService, discovery, versionSvc, gitSvc, buildSvc, deviceSvc);
        return;
    }

    if (result is ScanRefresh refresh)
    {
        scanPath        = refresh.NewPath;
        st.ScanRootPath = scanPath;
        stateService.Save(st);
        needsScan = true;
        continue;
    }

    if (result is FilterChanged fc)
    {
        nameFilter     = fc.NameFilter;
        platformFilter = fc.PlatformFilter;
        // no re-scan needed — just re-filter in memory
        continue;
    }

    if (result is DiagnosticsRequested)
    {
        DiagnosticsScreen.Show();
        continue;
    }

    if (result is CheckUpdatesRequested)
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan1]  Check for Updates  [/]").RuleStyle(Style.Parse("cyan1 dim")));
        AnsiConsole.WriteLine();

        var currentVer = typeof(AppListScreen).Assembly.GetName().Version;
        AnsiConsole.MarkupLine($"  [dim]Installed:[/] [white]{currentVer?.ToString(3)}[/]");

        string? latestStr = null;
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan1"))
            .Start("  [dim]Checking NuGet...[/]", _ =>
            {
                UpdateService.Instance.ForceCheck();
                for (var i = 0; i < 60 && UpdateService.Instance.GetLatestVersion() is null; i++)
                    System.Threading.Thread.Sleep(100);
                latestStr = UpdateService.Instance.GetLatestVersion();
            });

        if (latestStr is null)
        {
            AnsiConsole.MarkupLine("  [yellow](!) Could not reach NuGet. Check your internet connection.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [dim]Latest:[/]    [white]{latestStr}[/]");
            AnsiConsole.WriteLine();

            var cleanLatest = latestStr.Split('-')[0];
            if (Version.TryParse(cleanLatest, out var latestVer) && currentVer is not null && latestVer > currentVer)
            {
                AnsiConsole.MarkupLine($"  [cyan1 bold]↑ Update available![/]");
                AnsiConsole.WriteLine();

                if (AnsiConsole.Confirm("  Install update now?", defaultValue: true))
                {
                    AnsiConsole.MarkupLine("  [dim]Closing and updating. If it does not update, check maui-forge-update.log in your temp folder.[/]");
                    try
                    {
                        System.Threading.Thread.Sleep(500);
                        UpdateService.LaunchDeferredUpdate(latestStr, System.Environment.GetCommandLineArgs().Skip(1).ToArray());
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"  [red]x  Could not start updater:[/] {Markup.Escape(ex.Message)}");
                        AnsiConsole.MarkupLine($"  [dim]Run manually:[/] [cyan1]{Markup.Escape(UpdateService.GetManualUpdateCommand(latestStr))}[/]");
                    }
                }
            }
            else
            {
                AnsiConsole.MarkupLine("  [green]✓ Already up to date.[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to return to dashboard...[/]").AllowEmpty());
        continue;
    }

    if (result is MacConfigRequested)
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan1]  Environment Settings (Mac / SSH)  [/]").RuleStyle(Style.Parse("cyan1 dim")));
        AnsiConsole.WriteLine();

        st.UseLocalMac = AnsiConsole.Confirm("  Use local Mac mode (no SSH)?", defaultValue: st.UseLocalMac);

        if (!st.UseLocalMac)
        {
            st.MacHost = AnsiConsole.Ask<string>("  [cyan1]Mac host (IP or hostname):[/]", st.MacHost ?? "");
            st.MacUser = AnsiConsole.Ask<string>("  [cyan1]Mac username:[/]", st.MacUser ?? "");

            if (AnsiConsole.Confirm("  Scan local network for Macs?", defaultValue: false))
            {
                List<string> hosts = [];
                AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan1"))
                    .Start("  [dim]Scanning network (arp -a)...[/]", _ => hosts = deviceSvc.FindMacsOnNetwork());

                if (hosts.Count > 0)
                {
                    hosts.Insert(0, "[Keep current]");
                    var picked = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("  [cyan1]Select a Mac host:[/]")
                            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                            .AddChoices(hosts));
                    if (!picked.Contains("Keep")) st.MacHost = picked;
                }
                else
                {
                    AnsiConsole.MarkupLine("  [yellow](!) No hosts found in ARP table.[/]");
                }
            }
        }

        stateService.Save(st);
        AnsiConsole.MarkupLine("  [green]ok  Settings saved.[/]");
        AnsiConsole.Prompt(new TextPrompt<string>("\n  [dim]Press Enter to return to dashboard...[/]").AllowEmpty());
        continue;
    }

    if (result is RemoteConnectRequested)
    {
        var remoteClient = RemoteConnectScreen.Show(stateService);
        if (remoteClient?.IsConnected == true)
        {
            AnsiConsole.MarkupLine($"[green]✓ Connected. Remote server is ready.[/]");
            AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to return to dashboard...[/]").AllowEmpty());
        }
        continue;
    }

    if (result is AppSelected selected)
    {
        detail.Show(selected.App);
        needsScan = true; // re-scan after returning — versions or git may have changed
    }
}

AnsiConsole.MarkupLine("[dim]Bye! ⚡[/]");

static List<AppEntry> Filter(List<AppEntry> apps, string? nameFilter, string platformFilter) =>
    apps
        .Where(a => platformFilter switch
        {
            "iOS"     => a.Versions.iOS     is not null,
            "Android" => a.Versions.Android is not null,
            _         => true,
        })
        .Where(a => nameFilter is null || a.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
        .ToList();
