using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MauiForge.Models;
using MauiForge.Services;
using MauiForge.UI;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding  = Encoding.UTF8;

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

var depth   = 2;
var cliPath = (string?)null;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--depth" && i + 1 < args.Length && int.TryParse(args[i + 1], out var d)) depth = d;
    if (args[i] == "--path"  && i + 1 < args.Length) cliPath = args[i + 1];
}

var stateService = services.GetRequiredService<StateService>();
var discovery    = services.GetRequiredService<AppDiscoveryService>();
var detail       = services.GetRequiredService<AppDetailScreen>();
var deviceSvc    = services.GetRequiredService<DeviceService>();

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
                    AnsiConsole.MarkupLine("  [dim]Closing and updating in background...[/]");
                    System.Threading.Thread.Sleep(500);
                    UpdateService.LaunchDeferredUpdate(latestStr, System.Environment.GetCommandLineArgs().Skip(1).ToArray());
                }
            }
            else
            {
                AnsiConsole.MarkupLine("  [green]✓ Already up to date.[/]");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[dim]Press Enter to go back...[/]").AllowEmpty());
        continue;
    }

    if (result is MacConfigRequested)
    {
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cyan1]  Mac / SSH Config  [/]").RuleStyle(Style.Parse("cyan1 dim")));
        AnsiConsole.WriteLine();

        st.UseLocalMac = AnsiConsole.Confirm("  Use local Mac (no SSH)?", defaultValue: st.UseLocalMac);

        if (!st.UseLocalMac)
        {
            st.MacHost = AnsiConsole.Ask<string>("  [cyan1]Mac Host (IP or hostname):[/]", st.MacHost ?? "");
            st.MacUser = AnsiConsole.Ask<string>("  [cyan1]Mac User:[/]", st.MacUser ?? "");

            if (AnsiConsole.Confirm("  Scan network for Macs?", defaultValue: false))
            {
                List<string> hosts = [];
                AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan1"))
                    .Start("  [dim]Scanning network (arp -a)...[/]", _ => hosts = deviceSvc.FindMacsOnNetwork());

                if (hosts.Count > 0)
                {
                    hosts.Insert(0, "[Keep current]");
                    var picked = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("  [cyan1]Select Mac host:[/]")
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
        AnsiConsole.Prompt(new TextPrompt<string>("\n  [dim]Press Enter to continue...[/]").AllowEmpty());
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
