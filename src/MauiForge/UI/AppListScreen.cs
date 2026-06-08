using MauiForge.Models;
using MauiForge.Services;
using Spectre.Console;

namespace MauiForge.UI;

public abstract record ListResult;
public record AppSelected(AppEntry App) : ListResult;
public record ScanRefresh(string NewPath) : ListResult;
public record MacConfigRequested : ListResult;
public record DiagnosticsRequested : ListResult;
public record CheckUpdatesRequested : ListResult;
public record FilterChanged(string? NameFilter, string PlatformFilter) : ListResult;
public record ListQuit : ListResult;

public static class AppListScreen
{
    private const int ColIOS      = 18;
    private const int ColAndroid  = 18;
    private const int ColBranch   = 13;
    private const int ColLastUsed = 8;

    // ── Folder Browser ───────────────────────────────────────────────────────

    public static string? BrowseFolder(string startPath)
    {
        var current = Directory.Exists(startPath) ? startPath : Directory.GetCurrentDirectory();

        while (true)
        {
            AnsiConsole.Clear();
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[bold cyan1] Choose Scan Folder [/]").RuleStyle(new Style(Color.Cyan1, decoration: Decoration.Dim)));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]Current:[/] [bold white]{Markup.Escape(current)}[/]");
            AnsiConsole.WriteLine();

            const string kBack   = "[black on grey70]  << Back  [/]";
            const string kSelect = "[[OK]] Use this folder";
            const string kUp     = " ^  .. (go up)";

            // map label -> real dir name to handle special chars in folder names
            var subdirMap = new Dictionary<string, string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(current))
                {
                    var name = Path.GetFileName(dir);
                    if (name is null || name.StartsWith('.')) continue;
                    var label = $"  {Markup.Escape(name)}";
                    subdirMap[label] = name;
                }
            }
            catch { }

            var subdirs = subdirMap.Keys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var actions = new List<string> { kBack, kSelect };
            if (Directory.GetParent(current) is not null) actions.Add(kUp);

            var prompt = new SelectionPrompt<string>()
                .Title("[dim]  Use arrows to navigate  ·  Enter to open  ·  Choose to confirm[/]")
                .PageSize(22)
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .AddChoiceGroup("[bold grey53]── Actions ──────────────────────[/]", actions)
                .AddChoiceGroup("[bold grey53]── Subfolders ────────────────────[/]", subdirs.Count > 0 ? subdirs : ["[dim](no subfolders found)[/]"]);

            string? choice;
            try { choice = AnsiConsole.Prompt(prompt); }
            catch { return null; } // ESC

            if (choice == kSelect) return current;
            if (choice == null || choice == kBack) return null;
            if (choice == kUp)
            {
                current = Directory.GetParent(current)?.FullName ?? current;
                continue;
            }

            if (subdirMap.TryGetValue(choice, out var realName))
            {
                var next = Path.Combine(current, realName);
                if (Directory.Exists(next)) current = next;
            }
        }
    }

    public static ListResult Show(
        List<AppEntry> apps,
        string scanPath,
        PersistentState st,
        string? nameFilter = null,
        string platformFilter = "All")
    {
        AnsiConsole.Clear();
        RenderHeader();
        RenderScanBar(scanPath, st, nameFilter, platformFilter, apps);
        return ShowMenu(apps, scanPath, st, nameFilter, platformFilter);
    }

    // ── Header ───────────────────────────────────────────────────────────────

    public static void RenderHeader()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(Color.Cyan1, decoration: Decoration.Dim)));
        AnsiConsole.Write(new FigletText("MAUIForge").Color(Color.Cyan1));
        
        var version = typeof(AppListScreen).Assembly.GetName().Version?.ToString(3) ?? "1.4.1";
        AnsiConsole.MarkupLine($"  [bold cyan1]>>>[/] [bold white][link=https://cwsoftware.com.br]by CW Software[/][/] [cyan1]v{version}[/]  [grey46]|[/]  [dim]Unified MAUI Build, Release, and Version Console[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule().RuleStyle(new Style(Color.Cyan1, decoration: Decoration.Dim)));
        AnsiConsole.WriteLine();

        var latestStr = UpdateService.Instance.GetLatestVersion();
        if (latestStr != null)
        {
            var cleanLatestStr = latestStr.Split('-')[0];
            if (Version.TryParse(cleanLatestStr, out var latestVer))
            {
                var currentVer = typeof(AppListScreen).Assembly.GetName().Version;
                if (currentVer != null && latestVer > currentVer)
                {
                    AnsiConsole.MarkupLine($"  [cyan1 bold]↑ New version available:[/] [white]{latestStr}[/]  [dim](installed: {currentVer.ToString(3)})[/]");
                    AnsiConsole.MarkupLine("  [dim]Closing and updating. If it does not update, check maui-forge-update.log in your temp folder.[/]");
                    AnsiConsole.WriteLine();
                    try
                    {
                        UpdateService.LaunchDeferredUpdate(latestStr, System.Environment.GetCommandLineArgs().Skip(1).ToArray());
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"  [red]x  Could not start updater:[/] {Markup.Escape(ex.Message)}");
                        AnsiConsole.MarkupLine($"  [dim]Run manually:[/] [cyan1]{Markup.Escape(UpdateService.GetManualUpdateCommand(latestStr))}[/]");
                    }
                    return; // unreachable — LaunchDeferredUpdate calls Environment.Exit
                }
            }
        }
    }

    // ── Scan bar + Stats (merged) ─────────────────────────────────────────────

    private static void RenderScanBar(string scanPath, PersistentState st, string? nameFilter, string platformFilter, List<AppEntry>? apps = null)
    {
        // Line 1: path + mac + filters
        var macInfo = st.MacHost is { Length: > 0 }
            ? $"  [grey46]|[/]  [dim]mac:[/] [skyblue1]{Markup.Escape(st.MacHost)}[/]"
            : "";

        var filterInfo = new List<string>();
        if (platformFilter != "All") filterInfo.Add($"[dim]platform:[/] [cyan1]{Markup.Escape(platformFilter)}[/]");
        if (nameFilter is { Length: > 0 }) filterInfo.Add($"[dim]search:[/] [cyan1]{Markup.Escape(nameFilter)}[/]");
        var filterStr = filterInfo.Count > 0 ? "  [grey46]|[/]  " + string.Join("  ", filterInfo) : "";

        AnsiConsole.MarkupLine($"  [dim]scan:[/] [white]{Markup.Escape(scanPath)}[/]{macInfo}{filterStr}");

        // Line 2: stats inline
        if (apps is null || apps.Count == 0)
        {
            AnsiConsole.MarkupLine(apps is null ? "" : "  [yellow](!) No projects found in this scan path.[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var iosC    = apps.Count(a => a.Versions.iOS     is not null);
        var andC    = apps.Count(a => a.Versions.Android is not null);
        var dirtyC  = apps.Count(a => a.Git.Dirty);
        var syncOff = apps.Count(a => !a.Versions.InSync && a.Versions.iOS is not null && a.Versions.Android is not null);
        var aheadC  = apps.Count(a => a.Git.Ahead > 0);

        var stats = new List<string>
        {
            $"[bold white]{apps.Count}[/] [dim]projects[/]",
            $"[skyblue1]{iosC}[/] [dim]iOS[/]",
            $"[green3]{andC}[/] [dim]Android[/]",
        };
        if (dirtyC  > 0) stats.Add($"[yellow]{dirtyC} dirty[/]");
        if (aheadC  > 0) stats.Add($"[yellow]{aheadC} ahead[/]");
        if (syncOff > 0) stats.Add($"[red]{syncOff} out of sync[/]");

        AnsiConsole.MarkupLine("  " + string.Join("  [grey46]·[/]  ", stats));
        AnsiConsole.WriteLine();
    }

    // ── Menu ─────────────────────────────────────────────────────────────────

    private static ListResult ShowMenu(List<AppEntry> apps, string scanPath, PersistentState st, string? nameFilter, string platformFilter)
    {
        var sorted = apps
            .OrderByDescending(a => st.AppUsage.GetValueOrDefault(a.Dir, 0))
            .ToList();

        int nameW = sorted.Count > 0
            ? Math.Min(28, Math.Max(16, sorted.Max(a => a.Name.Length) + 2))
            : 16;

        var appLabels = sorted.Select(a => AppLine(a, nameW, st)).ToList();

        var nextPlat   = platformFilter switch { "All" => "iOS", "iOS" => "Android", _ => "All" };
        var platLabel  = platformFilter == "All" ? "[dim]All[/]" : platformFilter == "iOS" ? "[skyblue1]iOS[/]" : "[green3]Android[/]";
        var searchLabel = nameFilter is { Length: > 0 } ? $"[cyan1]{Markup.Escape(nameFilter)}[/]" : "[dim]all[/]";

        var latestVer   = UpdateService.Instance.GetLatestVersion();
        var currentVer  = typeof(AppListScreen).Assembly.GetName().Version;
        var updateLabel = latestVer is not null && Version.TryParse(latestVer.Split('-')[0], out var lv) && currentVer is not null && lv > currentVer
            ? $"[cyan1] >[/]  [white]Check for Updates[/]  [yellow]↑ {Markup.Escape(latestVer)} available[/]"
            : "[cyan1] >[/]  [white]Check for Updates[/]  [dim]latest installed[/]";

        var globalLabels = new List<string>();
        if (sorted.Count > 0)
            globalLabels.Add("[cyan1] >[/]  [white]Open Most Recent Project[/] [dim]quick start[/]");
        globalLabels.AddRange(
        [
            "[cyan1] >[/]  [white]Change Scan Folder[/]",
            "[cyan1] >[/]  [white]Refresh Project Scan[/] [dim]re-scan[/]",
            $"[cyan1] >[/]  [white]Platform Filter:[/] {platLabel}  [dim]→ {Markup.Escape(nextPlat)}[/]",
            $"[cyan1] >[/]  [white]Search Projects:[/] {searchLabel}",
            "[cyan1] >[/]  [white]Environment Settings[/] [dim](Mac / SSH)[/]",
            "[cyan1] >[/]  [white]Diagnostics & Health[/]",
            updateLabel,
            "[cyan1] >[/]  [white]Exit[/]",
        ]);

        var hNamePad = "".PadRight(nameW + 4);
        var hIosPad  = "iOS".PadRight(ColIOS);
        var hAndPad  = "Android".PadRight(ColAndroid);
        var hBrPad   = "Branch".PadRight(ColBranch);
        var hLastPad = "Last".PadRight(ColLastUsed);

        var header =
            $"[bold dim]{Markup.Escape(hNamePad)}[/]" +
            $"[bold skyblue1]{Markup.Escape(hIosPad)}[/]" +
            $"[bold green3]{Markup.Escape(hAndPad)}[/]" +
            $"[bold dim]{Markup.Escape(hBrPad)}[/]" +
            $"[bold dim]{Markup.Escape(hLastPad)}[/]" +
            $"[bold dim]Git[/]";

        var appGroupHeader    = $"[bold grey53]── Projects ({sorted.Count}) ─────────────────────────────────────────────────────────────[/]";
        var globalGroupHeader = $"[bold grey53]── Home Dashboard ─────────────────────────────────────────────────────────────[/]";

        var prompt = new SelectionPrompt<string>()
            .Title("[bold cyan1]Project Dashboard[/]\n" + header)
            .PageSize(26)
            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
            .AddChoiceGroup(appGroupHeader,    appLabels.Count > 0 ? appLabels : ["[dim]  (no projects found)[/]"])
            .AddChoiceGroup(globalGroupHeader, globalLabels);

        string? choice;
        try { choice = AnsiConsole.Prompt(prompt); }
        catch { return new ListQuit(); } // ESC

        if (choice is null) return new ListQuit();

        if (globalLabels.Contains(choice))
        {
            if (choice.Contains("Open Most Recent Project"))
            {
                if (sorted.Count > 0)
                    return new AppSelected(sorted[0]);
                return new ListQuit();
            }
            if (choice.Contains("Change Scan Folder"))
            {
                var newPath = BrowseFolder(scanPath);
                return new ScanRefresh(newPath ?? scanPath);
            }
            if (choice.Contains("Refresh Project Scan")) return new ScanRefresh(scanPath);
            if (choice.Contains("Platform Filter"))      return new FilterChanged(nameFilter, nextPlat);
            if (choice.Contains("Search Projects"))
            {
                AnsiConsole.Clear();
                RenderHeader();
                RenderScanBar(scanPath, st, nameFilter, platformFilter);
                var newFilter = AnsiConsole.Ask<string>(
                    "  [cyan1]Filter projects by name[/] [dim](empty = show all):[/]", nameFilter ?? "");
                return new FilterChanged(newFilter.Length == 0 ? null : newFilter, platformFilter);
            }
            if (choice.Contains("Environment Settings")) return new MacConfigRequested();
            if (choice.Contains("Diagnostics & Health")) return new DiagnosticsRequested();
            if (choice.Contains("Check for Updates")) return new CheckUpdatesRequested();
            return new ListQuit();
        }

        var idx = appLabels.IndexOf(choice);
        return idx >= 0 ? new AppSelected(sorted[idx]) : new ListQuit();
    }

    // ── App line ─────────────────────────────────────────────────────────────

    private static string AppLine(AppEntry app, int nameW, PersistentState st)
    {
        var (dotColor, dotChar) = app.Git switch
        {
            { Behind: > 0 }                   => ("red",    "!"),
            { Dirty: true } or { Ahead: > 0 } => ("yellow", "*"),
            _                                 => ("green",  "+"),
        };
        var dotMarkup = $"[bold {dotColor}]{dotChar}[/]";

        var recent     = IsRecent(app, st);
        var nameRaw    = app.Name.Length > nameW - 1 ? app.Name[..(nameW - 2)] + "~" : app.Name;
        var nameColor  = recent ? "bold white" : "white";
        var nameMarkup = $"[{nameColor}]{Markup.Escape(nameRaw)}[/]";
        var namePad    = new string(' ', nameW - nameRaw.Length);

        string iosMarkup;
        int    iosRawLen;
        if (app.Versions.iOS is { } ios)
        {
            iosRawLen = ios.Version.Length + 2 + ios.Build.Length;
            iosMarkup = $"[skyblue1]{Markup.Escape(ios.Version)}[/] [dim]#{Markup.Escape(ios.Build)}[/]";
        }
        else { iosRawLen = 1; iosMarkup = "[grey23]-[/]"; }
        var iosPad = new string(' ', Math.Max(1, ColIOS - iosRawLen));

        var (syncMark, syncW) = (app.Versions.iOS, app.Versions.Android) switch
        {
            (not null, not null) when !app.Versions.InSync => ("[yellow bold]![/] ", 2),
            _ => ("  ", 2)
        };

        string andMarkup;
        int    andRawLen;
        if (app.Versions.Android is { } and)
        {
            andRawLen = and.Version.Length + 2 + and.Build.Length;
            andMarkup = $"[green3]{Markup.Escape(and.Version)}[/] [dim]#{Markup.Escape(and.Build)}[/]";
        }
        else { andRawLen = 1; andMarkup = "[grey23]-[/]"; }
        var andPad = new string(' ', Math.Max(1, ColAndroid - andRawLen - syncW));

        var bc       = app.Branch is "main" or "master" ? "green" : "fuchsia";
        var brRaw    = app.Branch.Length > ColBranch - 1 ? app.Branch[..(ColBranch - 2)] + "~" : app.Branch;
        var brPad    = new string(' ', Math.Max(0, ColBranch - brRaw.Length));
        var brMarkup = $"[{bc}]{Markup.Escape(brRaw)}[/]{brPad}";

        // Last used relative time
        var lastUsedMarkup = "[grey23]—[/]";
        if (st.AppUsage.TryGetValue(app.Dir, out var usedTs) && usedTs > 0)
        {
            var ago = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - usedTs;
            var agoStr = ago < 60          ? "now"
                       : ago < 3600        ? $"{ago / 60}m"
                       : ago < 86400       ? $"{ago / 3600}h"
                       : ago < 86400 * 7   ? $"{ago / 86400}d"
                       : ago < 86400 * 30  ? $"{ago / 86400 / 7}w"
                       :                     $"{ago / 86400 / 30}mo";
            var agoColor = ago < 3600 ? "cyan1" : ago < 86400 ? "white" : "grey46";
            lastUsedMarkup = $"[{agoColor}]{agoStr}[/]";
        }
        var lastUsedPad = new string(' ', Math.Max(0, ColLastUsed - (lastUsedMarkup.Length > 10 ? 2 : 4)));

        // Git badge — textual, compact
        var gitMarkup = app.Git switch
        {
            { Dirty: false, Ahead: 0, Behind: 0 } => "[dim green]ok[/]",
            var g => string.Join(" ", new[]
            {
                g.Dirty      ? "[yellow]dirty[/]"            : null,
                g.Ahead  > 0 ? $"[yellow]↑{g.Ahead}[/]"    : null,
                g.Behind > 0 ? $"[red bold]↓{g.Behind}[/]" : null,
            }.Where(x => x is not null))
        };

        return $"  {dotMarkup} {nameMarkup}{namePad}  {iosMarkup}{iosPad}{syncMark}{andMarkup}{andPad}  {brMarkup}  {lastUsedMarkup}{lastUsedPad}{gitMarkup}";
    }

    private static bool IsRecent(AppEntry app, PersistentState st) =>
        st.AppUsage.TryGetValue(app.Dir, out var ts) &&
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts < 86400;
}
