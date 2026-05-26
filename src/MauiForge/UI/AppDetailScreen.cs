using MauiForge.Models;
using MauiForge.Services;
using Spectre.Console;

namespace MauiForge.UI;

public class AppDetailScreen(
    VersionService versions,
    GitService git,
    BuildService build,
    DeviceService devices,
    StateService state,
    AiCommitService aiCommit)
{
    private enum Act
    {
        IncrementVersion, IncrementBuild, SetManual, Sync,
        ArchiveiOS, RuniOS,
        RunAndroid, PublishAndroid,
        GitPull, GitCommit, GitPush, Clean, Undo, SetVerbosity, RepeatLast, OpenInEditor, Back
    }

    // ── Show ─────────────────────────────────────────────────────────────────

    public void Show(AppEntry app)
    {
        var st = state.Load();
        _verbosity = st.Verbosity ?? "quiet";
        state.RecordUsage(st, app.Dir);

        GitStatus gitStatus = new(0, 0, false);
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan1"))
            .Start("[dim]Checking git...[/]", _ => gitStatus = git.FetchAndGetStatus(app.Dir));

        if (gitStatus.Behind > 0)
        {
            AnsiConsole.MarkupLine($"  [yellow](!) {gitStatus.Behind} commit(s) behind remote.[/]");
            if (AnsiConsole.Confirm("  Run git pull now?", defaultValue: true))
            {
                var (ok, output) = git.Pull(app.Dir);
                AnsiConsole.MarkupLine(ok ? "  [green]ok  Pull complete.[/]" : $"  [red]x  {Markup.Escape(output)}[/]");
                gitStatus = git.GetStatus(app.Dir);
            }
            AnsiConsole.WriteLine();
        }

        while (true)
        {
            app = RefreshApp(app);
            gitStatus = app.Git;

            AnsiConsole.Clear();
            var cfg = GetOrCreateConfig(st, app);
            RenderDetail(app, gitStatus, cfg, st);

            var action = PromptAction(app, gitStatus, cfg, st);
            if (action == Act.Back) return;
            HandleAction(action, app, ref gitStatus, st, cfg);
        }
    }

    // ── Detail Panel ─────────────────────────────────────────────────────────

    private static void RenderDetail(AppEntry app, GitStatus gitStatus, AppBuildConfig cfg, PersistentState st)
    {
        var v = app.Versions;

        // ── version grid ────────────────────────────────
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(10))
            .AddColumn(new TableColumn("").Width(14))
            .AddColumn(new TableColumn("").Width(8))
            .AddColumn(new TableColumn(""));

        if (v.iOS is { } ios)
            table.AddRow(
                "[skyblue1]iOS[/]",
                $"[bold white]{Markup.Escape(ios.Version)}[/]",
                $"[dim]#{Markup.Escape(ios.Build)}[/]",
                cfg.iOSFramework is { } fw ? $"[dim]{Markup.Escape(fw)}[/]" : "[grey23]—[/]");
        else
            table.AddRow("[grey23]iOS[/]", "[grey23]—[/]", "", "");

        if (v.Android is { } and)
            table.AddRow(
                "[green3]Android[/]",
                $"[bold white]{Markup.Escape(and.Version)}[/]",
                $"[dim]#{Markup.Escape(and.Build)}[/]",
                cfg.AndroidFramework is { } af ? $"[dim]{Markup.Escape(af)}[/]" : "[grey23]—[/]");
        else
            table.AddRow("[grey23]Android[/]", "[grey23]—[/]", "", "");

        // ── sync status ────────────────────────────────
        var syncLine = (v.iOS, v.Android) switch
        {
            (not null, not null) when  v.InSync => "[green]✓ in sync[/]",
            (not null, not null) when !v.InSync => "[yellow bold]!! out of sync[/]",
            _ => ""
        };

        // ── git ────────────────────────────────────────
        var bc        = app.Branch is "main" or "master" ? "green3" : "fuchsia";
        var gitBadges = new List<string>();
        if (gitStatus.Dirty)     gitBadges.Add("[yellow]~ dirty[/]");
        if (gitStatus.Ahead > 0) gitBadges.Add($"[yellow]↑{gitStatus.Ahead} ahead[/]");
        if (gitStatus.Behind > 0) gitBadges.Add($"[red]↓{gitStatus.Behind} behind[/]");
        var gitStr = gitBadges.Count > 0
            ? string.Join("  ", gitBadges)
            : "[dim green]clean[/]";

        // ── build config ───────────────────────────────
        var buildCfg = cfg.BuildConfiguration is { } bc2 ? $"[cyan1]{Markup.Escape(bc2)}[/]" : "[grey46]—[/]";
        var iosDev   = cfg.iOSDeviceName is { } iname ? $"[skyblue1]{Markup.Escape(iname)}[/]"
                     : cfg.iOSDeviceId  is not null    ? "[skyblue1]✓ configured[/]"
                     : "[grey46]none[/]";
        var andDev   = cfg.AndroidDeviceName   is { } aname ? $"[green3]{Markup.Escape(aname)}[/]"
                     : cfg.AndroidDeviceSerial is { } s     ? $"[green3]{Markup.Escape(s)}[/]"
                     : "[grey46]none[/]";
        var macMode  = st.UseLocalMac
            ? "[green3]local Mac[/]"
            : st.MacHost is { } h ? $"[skyblue1]{Markup.Escape(h)}[/]" : "[grey46]not configured[/]";

        // ── assemble panel — two side-by-side grids ────
        var devGrid = new Grid().AddColumn(new GridColumn().Width(11)).AddColumn();
        devGrid.AddRow("[dim]config[/]",    buildCfg);
        devGrid.AddRow("[dim]iOS dev[/]",   iosDev);
        devGrid.AddRow("[dim]droid dev[/]", andDev);
        devGrid.AddRow("[dim]mac[/]",       macMode);

        var gitGrid = new Grid().AddColumn(new GridColumn().Width(11)).AddColumn();
        gitGrid.AddRow("[dim]branch[/]", $"[{bc}]{Markup.Escape(app.Branch)}[/]");
        gitGrid.AddRow("[dim]git[/]",    gitStr);

        var rows = new List<Spectre.Console.Rendering.IRenderable>
        {
            table,
        };
        if (syncLine.Length > 0) rows.Add(new Markup("  " + syncLine));
        rows.Add(new Text(""));
        rows.Add(new Rule("[grey23]devices & config[/]").RuleStyle(new Style(Color.Grey23)));
        rows.Add(devGrid);
        rows.Add(new Rule("[grey23]git[/]").RuleStyle(new Style(Color.Grey23)));
        rows.Add(gitGrid);
        rows.Add(new Text(""));
        rows.Add(new Markup($"[grey23]{Markup.Escape(app.Dir)}[/]"));

        AnsiConsole.Write(
            new Panel(new Rows(rows))
                .Header($"[bold cyan1]  {Markup.Escape(app.Name)}  [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1)
                .Padding(1, 0)
        );
        AnsiConsole.WriteLine();
    }

    // ── Action Menu ──────────────────────────────────────────────────────────

    private Act PromptAction(AppEntry app, GitStatus gitStatus, AppBuildConfig cfg, PersistentState st)
    {
        var master   = app.Versions.Master;
        var nextVer  = master is not null ? VersionService.IncrementVersion(master.Version) : "—";
        var nextBld  = master is not null && int.TryParse(master.Build, out var b) ? (b + 1).ToString() : "—";
        var hasSnap  = st.LastVersion?.AppDir == app.Dir;
        var syncWarn = !app.Versions.InSync && app.Versions.iOS is not null && app.Versions.Android is not null;
        var gitClean = !gitStatus.Dirty && gitStatus.Ahead == 0 && gitStatus.Behind == 0;
        var verbosity = st.Verbosity ?? "quiet";
        var lastAct  = st.LastAction is { Length: > 0 } ? st.LastAction : "none";

        string H(string s)  => $"[grey46]{s}[/]";                       // hint
        string V(string s)  => $"[dim]{s}[/]";                          // version dim
        string OK(string s) => $"[dim green]{s}[/]";
        string WA(string s) => $"[yellow]{s}[/]";

        // Build items: (label, act)
        var items = new List<(string Label, Act Action)>();

        void Group(string header) =>
            items.Add(($"[grey23]── {header} [/]", Act.Back)); // separator placeholder

        void Add(Act act, string label) => items.Add((label, act));

        Add(Act.Back, "[black on grey70]  << Back  [/] [dim]return to app list[/]");

        // ── Version
        Group("Version");
        Add(Act.IncrementVersion,
            $"[cyan1]v+[/]  [white]Increment Version + Build[/]  " +
            V($"{master?.Version ?? "-"} → {nextVer}  #{master?.Build ?? "-"} → #{nextBld}"));
        Add(Act.IncrementBuild,
            $"[cyan1]b+[/]  [white]Increment Build only[/]  " +
            V($"#{master?.Build ?? "-"} → #{nextBld}"));
        Add(Act.SetManual,
            "[cyan1]m~[/]  [white]Set version manually[/]");
        Add(Act.Sync,
            $"[cyan1]<>[/]  [white]Sync iOS ↔ Android[/]  " +
            (syncWarn ? WA("(!!) out of sync") : OK("(ok) in sync")));
        Add(Act.Undo,
            $"[cyan1]un[/]  [white]Undo last version change[/]  " +
            H(hasSnap ? $"{st.LastVersion!.Version} #{st.LastVersion.Build}" : "no snapshot"));

        // ── Run on Device
        Group("Run on Device");
        var iosRunDevice = cfg.iOSDeviceName ?? (cfg.iOSDeviceId is not null ? "device configured" : "no device");
        var iosRunCfg    = cfg.BuildConfiguration ?? "Debug";
        Add(Act.RuniOS,
            $"[skyblue1]ri[/]  [white]Run iOS[/]  " +
            H($"{iosRunDevice} • {iosRunCfg}"));
        var androidRunDevice = cfg.AndroidDeviceName ?? cfg.AndroidDeviceSerial ?? "no device";
        var androidRunCfg    = cfg.BuildConfiguration ?? "Debug";
        Add(Act.RunAndroid,
            $"[green3]ra[/]  [white]Run Android[/]  " +
            H($"{androidRunDevice} • {androidRunCfg}"));

        // ── Release
        Group("Release");
        Add(Act.ArchiveiOS,
            $"[skyblue1]ai[/]  [white]Archive iOS[/] [dim](Release)[/]  " +
            H(cfg.iOSFramework ?? "—"));
        Add(Act.PublishAndroid,
            $"[green3]pa[/]  [white]Publish Android[/] [dim](Release)[/]  " +
            H(cfg.AndroidFramework ?? "—"));

        // ── Git
        Group("Git");
        Add(Act.GitPull,
            $"[yellow]gl[/]  [white]Git Pull[/]  " +
            (gitStatus.Behind > 0 ? WA($"↓{gitStatus.Behind} to pull") : OK("up to date")));
        Add(Act.GitCommit,
            $"[yellow]gc[/]  [white]Git Commit[/]  " +
            (gitStatus.Dirty ? WA("changes pending") : OK("clean")));
        Add(Act.GitPush,
            $"[yellow]gp[/]  [white]Git Push[/]  " +
            (gitStatus.Ahead > 0 ? WA($"↑{gitStatus.Ahead} to push") : OK("up to date")));

        // ── Build & Tools
        Group("Build & Tools");
        Add(Act.Clean,
            "[dim]cl[/]  [white]Clean Project[/]");
        Add(Act.SetVerbosity,
            $"[dim]vb[/]  [white]Build verbosity:[/] [cyan1]{verbosity}[/]");
        Add(Act.RepeatLast,
            $"[dim]..[/]  [white]Repeat last action[/]  " + H(lastAct));
        var editorHint = DetectEditor() is { } ed ? H(ed) : H("no editor found");
        Add(Act.OpenInEditor,
            $"[dim]oe[/]  [white]Open in Editor[/]  {editorHint}");

        // Separate groups from selectables
        var separators = items.Where(x => x.Action == Act.Back && x.Label.StartsWith("[grey23]")).ToList();
        var selectables = items.Where(x => !(x.Action == Act.Back && x.Label.StartsWith("[grey23]"))).ToList();

        // Build SelectionPrompt with choice groups
        var prompt = new SelectionPrompt<string>()
            .Title("\n  [cyan1]What would you like to do?[/]  [dim](↑↓ navigate, Enter select)[/]")
            .PageSize(24)
            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11));

        var currentGroupItems = new List<string>();
        string? currentGroupHeader = null;
        var labelOrder = new List<string>();

        foreach (var item in items)
        {
            bool isSep = item.Action == Act.Back && item.Label.StartsWith("[grey23]");
            if (isSep)
            {
                if (currentGroupHeader is not null && currentGroupItems.Count > 0)
                    prompt.AddChoiceGroup(currentGroupHeader, currentGroupItems);
                currentGroupItems = [];
                currentGroupHeader = item.Label;
            }
            else
            {
                currentGroupItems.Add(item.Label);
                labelOrder.Add(item.Label);
            }
        }
        if (currentGroupHeader is not null && currentGroupItems.Count > 0)
            prompt.AddChoiceGroup(currentGroupHeader, currentGroupItems);

        string? chosen;
        try { chosen = AnsiConsole.Prompt(prompt); }
        catch { return Act.Back; } // ESC

        var found  = selectables.FirstOrDefault(x => x.Label == chosen);
        return found.Label is not null ? found.Action : Act.Back;
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void HandleAction(Act action, AppEntry app, ref GitStatus gitStatus, PersistentState st, AppBuildConfig cfg)
    {
        switch (action)
        {
            case Act.IncrementVersion: IncrementVersionAction(app, incrementVersion: true,  st); break;
            case Act.IncrementBuild:   IncrementVersionAction(app, incrementVersion: false, st); break;
            case Act.SetManual:        SetManualAction(app, st);                                  break;
            case Act.Sync:             SyncAction(app, st);                                       break;
            case Act.ArchiveiOS:       ArchiveIOSAction(app, st, cfg);                            break;
            case Act.RuniOS:           RunIOSAction(app, st, cfg);                                break;
            case Act.RunAndroid:       RunAndroidAction(app, st, cfg);                            break;
            case Act.PublishAndroid:   PublishAndroidAction(app, st, cfg);                        break;
            case Act.GitPull:
                AnsiConsole.WriteLine();
                var (ok, output) = git.Pull(app.Dir);
                AnsiConsole.MarkupLine(ok ? "  [green]ok  Pull complete.[/]" : $"  [red]x  {Markup.Escape(output)}[/]");
                gitStatus = git.GetStatus(app.Dir);
                st.LastAction = "Git Pull";
                state.Save(st);
                Pause();
                break;
            case Act.GitCommit:
                GitCommitAction(app, ref gitStatus, st);
                break;
            case Act.GitPush:
                GitPushAction(app, ref gitStatus, st);
                break;
            case Act.Clean:
                RunBuild(app.Dir, ["clean"]);
                st.LastAction = "Clean Project";
                state.Save(st);
                break;
            case Act.Undo:
                UndoAction(app, st);
                break;
            case Act.RepeatLast:
                RepeatLastAction(app, ref gitStatus, st, cfg);
                break;
            case Act.SetVerbosity:
                SetVerbosityAction(st);
                break;
            case Act.OpenInEditor:
                OpenInEditorAction(app);
                break;
        }
    }

    // ── Version ──────────────────────────────────────────────────────────────

    private void IncrementVersionAction(AppEntry app, bool incrementVersion, PersistentState st)
    {
        var master = app.Versions.Master;
        if (master is null) return;

        var newVer = incrementVersion ? VersionService.IncrementVersion(master.Version) : master.Version;
        var newBld = (int.TryParse(master.Build, out var b) ? b + 1 : 1).ToString();

        AnsiConsole.WriteLine();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Cyan1).HideHeaders();
        table.AddColumn("").AddColumn("").AddColumn("");
        table.AddRow("[dim]Version[/]", $"[grey]{Markup.Escape(master.Version)}[/]", $"[bold cyan1]{Markup.Escape(newVer)}[/]");
        table.AddRow("[dim]Build[/]",   $"[grey]#{Markup.Escape(master.Build)}[/]",  $"[bold cyan1]#{Markup.Escape(newBld)}[/]");
        AnsiConsole.Write(table);

        if (!AnsiConsole.Confirm("\n  Confirm?", defaultValue: true)) return;

        SaveSnapshot(app, master, st);
        ApplyVersion(app, newVer, newBld);
        st.LastAction = incrementVersion ? "Increment Version + Build" : "Increment Build only";
        state.Save(st);
        AnsiConsole.MarkupLine("  [green]ok  Version updated.[/]");

        if (AnsiConsole.Confirm("  Run git push?", defaultValue: false))
        {
            var msg = $"chore: bump version to {newVer} #{newBld} ({app.Name})";
            var (pushOk, _) = git.Push(app.Dir, msg);
            AnsiConsole.MarkupLine(pushOk ? "  [green]ok  Push complete.[/]" : "  [red]x  Push failed.[/]");
        }
        Pause();
    }

    private void SetManualAction(AppEntry app, PersistentState st)
    {
        AnsiConsole.WriteLine();
        var master = app.Versions.Master;
        var newVer = AnsiConsole.Ask<string>("  [cyan1]New version:[/]", master?.Version ?? "1.0.0");
        var newBld = AnsiConsole.Ask<string>("  [cyan1]New build:[/]",   master?.Build   ?? "1");
        if (master is not null) SaveSnapshot(app, master, st);
        ApplyVersion(app, newVer, newBld);
        AnsiConsole.MarkupLine("  [green]ok  Version updated.[/]");
        Pause();
    }

    private void SyncAction(AppEntry app, PersistentState st)
    {
        var v = app.Versions;
        if (v.iOS is null || v.Android is null)
        {
            AnsiConsole.MarkupLine("  [yellow](!) App does not have both platforms.[/]");
            Pause(); return;
        }
        AnsiConsole.WriteLine();
        var items = new List<ForgeMenu.ListItem<string>>
        {
            new($"[skyblue1](iOS)[/]     {Markup.Escape(v.iOS.Version)} [dim]#{v.iOS.Build}[/]",     "ios"),
            new($"[green3](Android)[/]  {Markup.Escape(v.Android.Version)} [dim]#{v.Android.Build}[/]", "android"),
        };
        var source = ForgeMenu.PromptList("Use which version as source?", items);
        if (source is null) return;
        var (ver, bld) = source == "ios" ? (v.iOS.Version, v.iOS.Build) : (v.Android.Version, v.Android.Build);
        SaveSnapshot(app, v.Master!, st);
        ApplyVersion(app, ver, bld);
        AnsiConsole.MarkupLine("  [green]ok  Versions synchronized.[/]");
        Pause();
    }

    private void UndoAction(AppEntry app, PersistentState st)
    {
        var snap = st.LastVersion;
        if (snap is null || snap.AppDir != app.Dir)
        {
            AnsiConsole.MarkupLine("  [yellow](!) No snapshot available for this app.[/]");
            Pause(); return;
        }
        AnsiConsole.WriteLine();
        var master = app.Versions.Master;
        var table  = new Table().Border(TableBorder.Rounded).BorderColor(Color.Yellow).HideHeaders();
        table.AddColumn("").AddColumn("").AddColumn("");
        table.AddRow("[dim]Version[/]", $"[grey]{Markup.Escape(master?.Version ?? "?")}[/]", $"[bold yellow]{Markup.Escape(snap.Version)}[/]");
        table.AddRow("[dim]Build[/]",   $"[grey]#{Markup.Escape(master?.Build  ?? "?")}[/]", $"[bold yellow]#{Markup.Escape(snap.Build)}[/]");
        AnsiConsole.Write(table);

        if (!AnsiConsole.Confirm("\n  Restore?", defaultValue: true)) return;
        ApplyVersion(app, snap.Version, snap.Build);
        st.LastVersion = null;
        state.Save(st);
        AnsiConsole.MarkupLine("  [green]ok  Version restored.[/]");
        Pause();
    }

    // ── Git Commit / Push ────────────────────────────────────────────────────

    private void GitCommitAction(AppEntry app, ref GitStatus gitStatus, PersistentState st)
    {
        AnsiConsole.WriteLine();

        var diffStat = git.GetUnstagedDiffStat(app.Dir);
        if (string.IsNullOrWhiteSpace(diffStat))
            diffStat = git.GetDiffStat(app.Dir);

        if (string.IsNullOrWhiteSpace(diffStat))
        {
            AnsiConsole.MarkupLine("  [yellow](!) Working tree is clean — nothing to commit.[/]");
            Pause(); return;
        }

        AnsiConsole.MarkupLine("  [dim]Changed files:[/]");
        foreach (var line in diffStat.Split('\n').Take(20))
            AnsiConsole.MarkupLine($"  [grey53]{Markup.Escape(line)}[/]");
        AnsiConsole.WriteLine();

        // Let user pick how to generate the message
        var providers = AiCommitService.DetectAvailable(git, app.Dir);
        string message;

        if (providers.Count > 1)
        {
            const string ManualKey = "__manual__";
            var aiItems = providers
                .Select(p => new ForgeMenu.ListItem<string>($"{p.Icon}  {p.Name}", p.Name))
                .ToList();
            aiItems.Add(new ForgeMenu.ListItem<string>("✎  Write manually", ManualKey));

            var pick = ForgeMenu.PromptList("Generate commit message with:", aiItems);
            if (pick is null) { Pause(); return; }

            if (pick == ManualKey)
            {
                message = AnsiConsole.Ask<string>("  [cyan1]Commit message:[/]");
            }
            else
            {
                var provider = providers.First(p => p.Name == pick);
                string? generated = null;
                AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("cyan1"))
                    .Start($"  [dim]Generating with {Markup.Escape(provider.Name)}...[/]", _ =>
                        generated = aiCommit.Generate(provider, diffStat, git, app.Dir));

                message = generated ?? git.SuggestCommitMessage(app.Dir);
                AnsiConsole.MarkupLine($"  [dim]Suggested:[/] [cyan1]{Markup.Escape(message)}[/]");
                var edited = AnsiConsole.Ask<string>("  [cyan1]Message (Enter to accept):[/]", message);
                if (!string.IsNullOrWhiteSpace(edited)) message = edited;
            }
        }
        else
        {
            var suggested = git.SuggestCommitMessage(app.Dir);
            AnsiConsole.MarkupLine($"  [dim]Suggested:[/] [cyan1]{Markup.Escape(suggested)}[/]");
            message = AnsiConsole.Ask<string>("  [cyan1]Commit message:[/]", suggested);
        }

        if (string.IsNullOrWhiteSpace(message)) { Pause(); return; }

        var (commitOk, commitOut) = git.Commit(app.Dir, message);
        AnsiConsole.MarkupLine(commitOk
            ? "  [green]ok  Committed.[/]"
            : $"  [red]x  {Markup.Escape(commitOut)}[/]");

        if (commitOk)
        {
            gitStatus = git.GetStatus(app.Dir);
            st.LastAction = "Git Commit";
            state.Save(st);

            if (AnsiConsole.Confirm("  Push now?", defaultValue: false))
            {
                var (pushOk, pushOut) = git.PushOnly(app.Dir);
                AnsiConsole.MarkupLine(pushOk ? "  [green]ok  Pushed.[/]" : $"  [red]x  {Markup.Escape(pushOut)}[/]");
                gitStatus = git.GetStatus(app.Dir);
            }
        }
        Pause();
    }

    private void GitPushAction(AppEntry app, ref GitStatus gitStatus, PersistentState st)
    {
        AnsiConsole.WriteLine();

        // If there are uncommitted changes, offer to commit first
        if (gitStatus.Dirty)
        {
            AnsiConsole.MarkupLine("  [yellow](!) You have uncommitted changes.[/]");
            if (AnsiConsole.Confirm("  Commit first?", defaultValue: true))
            {
                GitCommitAction(app, ref gitStatus, st);
                return;
            }
        }

        if (gitStatus.Ahead == 0)
        {
            AnsiConsole.MarkupLine("  [dim]Nothing to push — already up to date.[/]");
            Pause(); return;
        }

        // Show commits that will be pushed
        var commits = git.GetUnpushedCommits(app.Dir);
        AnsiConsole.MarkupLine($"  [cyan1]↑ {gitStatus.Ahead} commit(s) to push:[/]");
        foreach (var c in commits)
        {
            var parts = c.Split(' ', 2);
            var sha   = parts.Length > 0 ? parts[0] : "";
            var msg   = parts.Length > 1 ? parts[1] : c;
            AnsiConsole.MarkupLine($"    [grey46]{Markup.Escape(sha)}[/]  {Markup.Escape(msg)}");
        }
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm($"  Push [cyan1]{gitStatus.Ahead}[/] commit(s) to [white]{Markup.Escape(app.Branch)}[/]?", defaultValue: true))
            return;

        var (pushOk, pushOut) = git.PushOnly(app.Dir);
        AnsiConsole.MarkupLine(pushOk ? "  [green]ok  Pushed.[/]" : $"  [red]x  {Markup.Escape(pushOut)}[/]");
        gitStatus = git.GetStatus(app.Dir);
        st.LastAction = "Git Push";
        state.Save(st);
        Pause();
    }

    // ── iOS ──────────────────────────────────────────────────────────────────

    private void RunIOSAction(AppEntry app, PersistentState st, AppBuildConfig cfg)
    {
        var csproj = FindCsproj(app.Dir);
        if (csproj is null) { NoCsproj(); return; }
        if (!ConfigureMac(st)) return;

        if (cfg.iOSDeviceId is { Length: > 0 })
        {
            var lastDevice = new iOSDevice(
                cfg.iOSDeviceName ?? "Last iOS device",
                cfg.iOSDeviceId,
                cfg.iOSDeviceType ?? InferIosDeviceType(cfg.iOSDeviceId));

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]Last iOS device:[/] [skyblue1]{Markup.Escape(lastDevice.Name)}[/] [dim]{Markup.Escape(ShortDeviceId(lastDevice.Udid))}[/]");
            if (AnsiConsole.Confirm("  Use this device?", defaultValue: true))
            {
                RunSelectedIOSDevice(app, st, cfg, csproj, lastDevice);
                return;
            }
        }

        List<iOSDevice> deviceList = [];
        if (st.UseLocalMac)
        {
            AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("skyblue1"))
                .Start("  [dim]Listing iOS devices (local)...[/]", _ =>
                    deviceList = devices.GetiOSDevicesLocal());
        }
        else
        {
            AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("skyblue1"))
                .Start("  [dim]Listing iOS devices via SSH...[/]", _ =>
                    deviceList = devices.GetiOSDevices(st.MacHost!, st.MacUser!));
        }

        if (deviceList.Count == 0)
        {
            AnsiConsole.MarkupLine("  [red]x  No iOS devices or simulators found.[/]");
            AnsiConsole.MarkupLine("  [dim]Make sure Xcode is installed and accessible.[/]");
            Pause(); return;
        }

        var physical   = deviceList.Where(d => d.Type == "Device").ToList();
        var simulators = deviceList.Where(d => d.Type == "Simulator").ToList();

        var listItems = new List<ForgeMenu.ListItem<iOSDevice>>();
        if (physical.Count > 0)
        {
            listItems.Add(new("Physical devices", null!, IsSeparator: true));
            listItems.AddRange(physical.Select(d =>
                new ForgeMenu.ListItem<iOSDevice>($"[skyblue1](iOS)[/] {Markup.Escape(d.Name)}  [dim]{Markup.Escape(ShortDeviceId(d.Udid))}[/]", d)));
        }
        if (simulators.Count > 0)
        {
            listItems.Add(new("Simulators", null!, IsSeparator: true));
            listItems.AddRange(simulators.Select(d =>
                new ForgeMenu.ListItem<iOSDevice>($"[grey53](sim)[/] {Markup.Escape(d.Name)}  [dim]{Markup.Escape(ShortDeviceId(d.Udid))}[/]", d)));
        }

        var found = ForgeMenu.PromptList("Select iOS device:", listItems);
        if (found is null) return;

        RunSelectedIOSDevice(app, st, cfg, csproj, found);
    }

    private void RunSelectedIOSDevice(AppEntry app, PersistentState st, AppBuildConfig cfg, string csproj, iOSDevice device)
    {
        cfg.iOSDeviceId        = device.Udid;
        cfg.iOSDeviceName      = device.Name;
        cfg.iOSDeviceType      = device.Type;
        cfg.BuildConfiguration = PickBuildConfig(csproj, "", cfg.BuildConfiguration ?? "Debug");
        if (cfg.BuildConfiguration is null) return;
        cfg.iOSFramework       = PickFramework(csproj, "ios", cfg.iOSFramework ?? "net9.0-ios");
        if (cfg.iOSFramework is null) return;
        state.Save(st);

        var buildArgs = new List<string>
        {
            "build", csproj,
            "-f", cfg.iOSFramework,
            "-c", cfg.BuildConfiguration,
            "--no-incremental",
        };
        AddIosRunDeviceArgs(buildArgs, device);
        AddMacBuildArgs(buildArgs, st);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]Building iOS app before launch...[/]");
        if (!RunBuild(app.Dir, [.. buildArgs], pauseWhenDone: false))
        {
            Pause();
            return;
        }

        var runArgs = new List<string>
        {
            "build", csproj, "-t:Run",
            "-f", cfg.iOSFramework,
            "-c", cfg.BuildConfiguration,
        };
        AddIosRunDeviceArgs(runArgs, device);
        AddMacBuildArgs(runArgs, st);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [dim]Launching iOS app...[/]");
        st.LastAction = "Run iOS Device";
        state.Save(st);
        RunBuild(app.Dir, [.. runArgs]);
    }

    private static void AddMacBuildArgs(List<string> args, PersistentState st)
    {
        if (!st.UseLocalMac)
        {
            args.Add($"-p:ServerAddress={st.MacHost}");
            args.Add($"-p:ServerUser={st.MacUser}");
        }
    }

    private void ArchiveIOSAction(AppEntry app, PersistentState st, AppBuildConfig cfg)
    {
        var csproj = FindCsproj(app.Dir);
        if (csproj is null) { NoCsproj(); return; }
        if (!ConfigureMac(st)) return;

        cfg.BuildConfiguration = PickBuildConfig(csproj, "Release", "Release");
        if (cfg.BuildConfiguration is null) return;
        cfg.iOSFramework       = PickFramework(csproj, "ios", cfg.iOSFramework ?? "net9.0-ios");
        if (cfg.iOSFramework is null) return;

        var signOptions = new List<ForgeMenu.ListItem<string>>
        {
            new("Apple Development",  "Apple Development"),
            new("Apple Distribution", "Apple Distribution"),
            new("Custom…",            "Custom…"),
        };
        var signPick = ForgeMenu.PromptList("Code Sign key:", signOptions);
        if (signPick is null) return;
        var sign = signPick == "Custom…"
            ? AnsiConsole.Ask<string>("  [cyan1]Code Sign key:[/]", cfg.CodesignKey ?? "Apple Distribution")
            : signPick;
        cfg.CodesignKey = sign;
        state.Save(st);

        var outDir = Path.Combine(app.Dir, "bin", "Release", "archive");
        var args   = new List<string> { "publish", csproj, "-f", cfg.iOSFramework, "-c", cfg.BuildConfiguration,
            "-p:ArchiveOnBuild=true", $"-p:CodesignKey={sign}", "-o", outDir };
        if (!st.UseLocalMac)
        {
            args.Add($"-p:ServerAddress={st.MacHost}");
            args.Add($"-p:ServerUser={st.MacUser}");
        }
        AnsiConsole.MarkupLine($"  [dim]Output: {Markup.Escape(outDir)}[/]");
        AnsiConsole.WriteLine();
        st.LastAction = "Archive iOS";
        state.Save(st);
        
        var buildSuccess = RunBuild(app.Dir, [.. args], pauseWhenDone: false);
        if (buildSuccess)
        {
            var archivePath = _lastBuildOutput
                .Select(ExtractXcodeArchivePath)
                .FirstOrDefault(p => p is not null);

            if (archivePath is not null)
            {
                AnsiConsole.WriteLine();
                if (AnsiConsole.Confirm("  Would you like to open this archive in Xcode?", defaultValue: true))
                {
                    OpenXcodeArchive(st, archivePath);
                }
            }
        }
        
        Pause();
    }

    private static string? ExtractXcodeArchivePath(string line)
    {
        int archiveIdx = line.IndexOf(".xcodearchive", StringComparison.OrdinalIgnoreCase);
        if (archiveIdx == -1) return null;

        int endIdx = archiveIdx + ".xcodearchive".Length;

        int startIdx = line.IndexOf("/", StringComparison.Ordinal);
        if (startIdx == -1 || startIdx > archiveIdx) return null;

        int usersIdx = line.IndexOf("/Users/", StringComparison.Ordinal);
        if (usersIdx != -1 && usersIdx < archiveIdx)
        {
            startIdx = usersIdx;
        }
        else
        {
            int current = startIdx;
            while (current < archiveIdx)
            {
                int nextSlash = line.IndexOf("/", current + 1, StringComparison.Ordinal);
                if (nextSlash == -1 || nextSlash > archiveIdx) break;
                char prevChar = line[nextSlash - 1];
                if (char.IsWhiteSpace(prevChar) || prevChar == ':' || prevChar == '"' || prevChar == '\'')
                {
                    startIdx = nextSlash;
                }
                current = nextSlash;
            }
        }

        var path = line.Substring(startIdx, endIdx - startIdx).Trim('"', '\'', ' ', '\t');
        return path;
    }

    private void OpenXcodeArchive(PersistentState st, string archivePath)
    {
        try
        {
            if (st.UseLocalMac)
            {
                AnsiConsole.MarkupLine("  [dim]Opening archive in Xcode...[/]");
                var psi = new System.Diagnostics.ProcessStartInfo("open")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(archivePath);
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null)
                {
                    AnsiConsole.MarkupLine("  [red]x  Could not start 'open' command.[/]");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(st.MacHost) || string.IsNullOrWhiteSpace(st.MacUser))
                {
                    AnsiConsole.MarkupLine("  [red]x  Remote Mac not configured.[/]");
                    return;
                }

                AnsiConsole.MarkupLine($"  [dim]Opening archive in Xcode on remote Mac ({st.MacHost})...[/]");
                var psi = new System.Diagnostics.ProcessStartInfo("ssh")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("StrictHostKeyChecking=no");
                psi.ArgumentList.Add("-o"); psi.ArgumentList.Add("ConnectTimeout=10");
                psi.ArgumentList.Add($"{st.MacUser}@{st.MacHost}");
                psi.ArgumentList.Add($"open \"{archivePath}\"");

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is not null)
                {
                    proc.WaitForExit(5000);
                    if (proc.ExitCode != 0)
                    {
                        var err = proc.StandardError.ReadToEnd();
                        AnsiConsole.MarkupLine($"  [red]x  SSH open command failed:[/] {Markup.Escape(err.Trim())}");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("  [red]x  Could not start SSH process.[/]");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]x  Failed to open archive in Xcode:[/] {Markup.Escape(ex.Message)}");
        }
    }

    // ── Android ──────────────────────────────────────────────────────────────

    private void RunAndroidAction(AppEntry app, PersistentState st, AppBuildConfig cfg)
    {
        var csproj = FindCsproj(app.Dir);
        if (csproj is null) { NoCsproj(); return; }

        List<AndroidDevice> runningDevices = [];
        List<string> avds = [];
        string? adbPath = null;

        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("green3"))
            .Start("  [dim]Listing Android devices and emulators...[/]", _ =>
                (runningDevices, avds, adbPath) = devices.GetAndroidDevicesAndAvds());

        if (adbPath is null)
        {
            AnsiConsole.MarkupLine("  [red]x  adb not found.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [dim]Checked PATH, registry, Visual Studio, Android Studio, and common SDK locations.[/]");
            AnsiConsole.MarkupLine("  [dim]To fix, set the environment variable:[/]");
            AnsiConsole.MarkupLine("  [cyan1]  ANDROID_HOME=C:\\path\\to\\android-sdk[/]");
            AnsiConsole.MarkupLine("  [dim]Or run from the [bold]Visual Studio Android ADB Command Prompt[/] which sets PATH automatically.[/]");
            Pause(); return;
        }

        var online   = runningDevices.Where(d => d.State == "device").ToList();
        var physical = online.Where(d => !d.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)).ToList();
        var running  = online.Where(d =>  d.Serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)).ToList();

        // Show offline/unauthorized devices as info
        var problem = runningDevices.Where(d => d.State != "device").ToList();
        foreach (var d in problem)
            AnsiConsole.MarkupLine($"  [yellow](!) {Markup.Escape(d.Serial)} — {Markup.Escape(d.State)} (skipped)[/]");

        if (online.Count == 0 && avds.Count == 0)
        {
            AnsiConsole.MarkupLine("  [red]x  No Android devices or emulators available.[/]");
            AnsiConsole.MarkupLine($"  [dim]adb: {Markup.Escape(adbPath)}[/]");
            AnsiConsole.MarkupLine("  [dim]Connect a device (enable USB debugging) or create an AVD in Android Studio.[/]");
            Pause(); return;
        }

        // Filter AVDs that are already running (to avoid showing them twice)
        var runningAvdNames = running.Select(d => d.Model.Replace(' ', '_')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredAvds = avds.Where(a => !runningAvdNames.Contains(a.Replace(' ', '_'))).ToList();

        string? serial = null;

        // Offer last used device as quick option
        if (cfg.AndroidDeviceSerial is not null)
        {
            var lastSerial = cfg.AndroidDeviceSerial;
            var lastName   = cfg.AndroidDeviceName ?? lastSerial;
            var isOnline   = online.Any(d => d.Serial == lastSerial);
            var isAvd      = !isOnline && filteredAvds.Any(a => "avd:" + a == lastSerial || lastSerial.StartsWith("emulator-"));

            var lastLabel = isOnline
                ? $"[green3]▶  Use last: {Markup.Escape(lastName)}[/]  [dim]{Markup.Escape(lastSerial)} (online)[/]"
                : $"[yellow]▶  Use last: {Markup.Escape(lastName)}[/]  [dim]{Markup.Escape(lastSerial)} (offline — will try)[/]";

            var quickItems = new List<ForgeMenu.ListItem<string>>
            {
                new(lastLabel, "last"),
                new("[grey46]Choose a different device…[/]", "pick"),
            };

            var quickPick = ForgeMenu.PromptList($"  Last device: [white]{Markup.Escape(lastName)}[/]", quickItems);
            if (quickPick is null) return;

            if (quickPick == "last")
            {
                if (isOnline)
                {
                    serial = lastSerial;
                }
                else if (lastSerial.StartsWith("avd:", StringComparison.OrdinalIgnoreCase))
                {
                    var avdName = lastSerial[4..];
                    AnsiConsole.MarkupLine($"  [dim]Starting emulator: {Markup.Escape(avdName)}[/]");
                    serial = StartAvdAndWait(avdName, adbPath);
                    if (serial is null) { AnsiConsole.MarkupLine("  [red]x  Emulator did not come online in time.[/]"); Pause(); return; }
                }
                else
                {
                    // serial was a running emulator last time — check if still online
                    serial = online.Any(d => d.Serial == lastSerial) ? lastSerial : null;
                    if (serial is null) { AnsiConsole.MarkupLine("  [red]x  Last device is no longer available. Please choose again.[/]"); Pause(); return; }
                }
            }
        }

        if (serial is null)
        {
            var andListItems = new List<ForgeMenu.ListItem<string>>();
            if (physical.Count > 0)
            {
                andListItems.Add(new("Physical devices", null!, IsSeparator: true));
                andListItems.AddRange(physical.Select(d =>
                    new ForgeMenu.ListItem<string>($"[green3](droid)[/] {Markup.Escape(d.Model)}  [dim]{Markup.Escape(d.Serial)}[/]", d.Serial)));
            }
            if (running.Count > 0)
            {
                andListItems.Add(new("Running emulator", null!, IsSeparator: true));
                andListItems.AddRange(running.Select(d =>
                    new ForgeMenu.ListItem<string>($"[grey53](emu ▶)[/] {Markup.Escape(d.Model)}  [dim]{Markup.Escape(d.Serial)}[/]", d.Serial)));
            }
            if (filteredAvds.Count > 0)
            {
                andListItems.Add(new("Available AVDs (will start)", null!, IsSeparator: true));
                andListItems.AddRange(filteredAvds.Select(a =>
                    new ForgeMenu.ListItem<string>($"[grey53](avd)[/] {Markup.Escape(a)}", "avd:" + a)));
            }

            var picked = ForgeMenu.PromptList("Select Android device or emulator:", andListItems);
            if (picked is null) return;

            if (picked.StartsWith("avd:"))
            {
                var avdName = picked[4..];
                AnsiConsole.MarkupLine($"  [dim]Starting emulator: {Markup.Escape(avdName)}[/]");
                serial = StartAvdAndWait(avdName, adbPath);
                if (serial is null)
                {
                    AnsiConsole.MarkupLine("  [red]x  Emulator did not come online in time.[/]");
                    Pause(); return;
                }
                cfg.AndroidDeviceName   = avdName;
                cfg.AndroidDeviceSerial = "avd:" + avdName;
            }
            else
            {
                serial = picked;
                var pickedDevice = online.FirstOrDefault(d => d.Serial == picked);
                cfg.AndroidDeviceName   = pickedDevice?.Model ?? picked;
                cfg.AndroidDeviceSerial = picked;
            }
        }

        // Quick Launch vs Build & Run
        string? launchMode;
        try
        {
            launchMode = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  [cyan1]How would you like to run?[/]")
                    .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                    .AddChoices(
                        "[bold]Build & Run[/]  [dim]recompile then deploy[/]",
                        "[bold]Quick Launch[/]  [dim]launch already-installed app (skip build)[/]",
                        "[grey46]← Back[/]"));
        }
        catch { return; } // ESC

        if (launchMode is null || launchMode.Contains("Back")) return;

        if (launchMode.Contains("Quick Launch"))
        {
            var packageId = versions.ReadAndroidApplicationId(csproj);
            if (packageId is null)
            {
                AnsiConsole.MarkupLine("  [red]x  Could not determine package ID from csproj or AndroidManifest.xml.[/]");
                Pause(); return;
            }

            var adbExe = DeviceService.FindAdb()!;
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [dim]Launching [/][white]{Markup.Escape(packageId)}[/][dim] on {Markup.Escape(serial!)}...[/]");

            var psi = new System.Diagnostics.ProcessStartInfo(adbExe)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add("-s"); psi.ArgumentList.Add(serial!);
            psi.ArgumentList.Add("shell");
            psi.ArgumentList.Add("monkey");
            psi.ArgumentList.Add("-p"); psi.ArgumentList.Add(packageId);
            psi.ArgumentList.Add("-c"); psi.ArgumentList.Add("android.intent.category.LAUNCHER");
            psi.ArgumentList.Add("1");

            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);

            if (proc.ExitCode == 0 && output.Contains("Events injected: 1"))
                AnsiConsole.MarkupLine("  [green]ok  App launched.[/]");
            else
                AnsiConsole.MarkupLine($"  [yellow](!) adb monkey output: {Markup.Escape(output.Trim())}[/]");

            st.LastAction = "Quick Launch Android";
            state.Save(st);
            Pause(); return;
        }

        cfg.BuildConfiguration = PickBuildConfig(csproj, "", cfg.BuildConfiguration ?? "Debug");
        if (cfg.BuildConfiguration is null) return;
        cfg.AndroidFramework   = PickFramework(csproj, "android", cfg.AndroidFramework ?? "net9.0-android");
        if (cfg.AndroidFramework is null) return;
        state.Save(st);

        var args = new List<string> { "build", csproj, "-t:Run", "-f", cfg.AndroidFramework, "-c", cfg.BuildConfiguration };
        if (serial is not null) args.Add($"-p:AdbTarget=-s {serial}");
        AnsiConsole.WriteLine();
        st.LastAction = "Run Android Device";
        state.Save(st);
        RunBuild(app.Dir, [.. args]);
    }

    private static string? StartAvdAndWait(string avdName, string adbPath)
    {
        var emulatorPath = DeviceService.FindEmulator();
        if (emulatorPath is null)
        {
            AnsiConsole.MarkupLine("  [red]x  emulator binary not found.[/]");
            return null;
        }

        var psi = new System.Diagnostics.ProcessStartInfo(emulatorPath)
        {
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-avd");
        psi.ArgumentList.Add(avdName);
        System.Diagnostics.Process.Start(psi);

        string? found = null;
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(Style.Parse("green3"))
            .Start("  [dim]Waiting for emulator to boot...[/]", ctx =>
            {
                // Phase 1: wait for emulator to appear in adb devices (up to 60s)
                for (var i = 0; i < 30 && found is null; i++)
                {
                    System.Threading.Thread.Sleep(2000);
                    var adbPsi = new System.Diagnostics.ProcessStartInfo(adbPath)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
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

                if (found is null) return;

                // Phase 2: wait for sys.boot_completed=1 so the system is truly ready (up to 90s)
                ctx.Status("  [dim]Waiting for Android to finish booting...[/]");
                for (var i = 0; i < 45; i++)
                {
                    System.Threading.Thread.Sleep(2000);
                    var bootPsi = new System.Diagnostics.ProcessStartInfo(adbPath)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        UseShellExecute        = false,
                    };
                    bootPsi.ArgumentList.Add("-s"); bootPsi.ArgumentList.Add(found);
                    bootPsi.ArgumentList.Add("shell"); bootPsi.ArgumentList.Add("getprop"); bootPsi.ArgumentList.Add("sys.boot_completed");
                    using var bootProc = System.Diagnostics.Process.Start(bootPsi)!;
                    var bootOut = bootProc.StandardOutput.ReadToEnd().Trim();
                    bootProc.WaitForExit(5000);
                    if (bootOut == "1") break;
                }
            });

        return found;
    }

    private void PublishAndroidAction(AppEntry app, PersistentState st, AppBuildConfig cfg)
    {
        var csproj = FindCsproj(app.Dir);
        if (csproj is null) { NoCsproj(); return; }

        cfg.BuildConfiguration = PickBuildConfig(csproj, "Release", "Release");
        if (cfg.BuildConfiguration is null) return;
        cfg.AndroidFramework   = PickFramework(csproj, "android", cfg.AndroidFramework ?? "net9.0-android");
        if (cfg.AndroidFramework is null) return;
        state.Save(st);

        var outDir = Path.Combine(app.Dir, "bin", cfg.BuildConfiguration, cfg.AndroidFramework, "publish");
        var args   = new List<string> { "publish", csproj, "-f", cfg.AndroidFramework, "-c", cfg.BuildConfiguration };
        AnsiConsole.MarkupLine($"  [dim]Output: {Markup.Escape(outDir)}[/]");
        AnsiConsole.WriteLine();
        st.LastAction = "Publish Android";
        state.Save(st);
        if (!RunBuild(app.Dir, [.. args])) return;

        if (Directory.Exists(outDir))
            foreach (var f in Directory.EnumerateFiles(outDir, "*.apk").Concat(Directory.EnumerateFiles(outDir, "*.aab")))
                AnsiConsole.MarkupLine($"  [green][[APK]] {Markup.Escape(f)}[/]");
    }

    // ── Repeat / Verbosity ───────────────────────────────────────────────────

    private void RepeatLastAction(AppEntry app, ref GitStatus gitStatus, PersistentState st, AppBuildConfig cfg)
    {
        if (st.LastAction is null or { Length: 0 })
        {
            AnsiConsole.MarkupLine("  [yellow](!) No action recorded yet.[/]");
            Pause(); return;
        }

        AnsiConsole.MarkupLine($"  [dim]Repeating:[/] [cyan1]{Markup.Escape(st.LastAction)}[/]");
        AnsiConsole.WriteLine();

        var act = st.LastAction switch
        {
            "Increment Version + Build" => Act.IncrementVersion,
            "Increment Build only"      => Act.IncrementBuild,
            "Archive iOS"               => Act.ArchiveiOS,
            "Run iOS Device"            => Act.RuniOS,
            "Run Android Device"        => Act.RunAndroid,
            "Publish Android"           => Act.PublishAndroid,
            "Git Pull"                  => Act.GitPull,
            "Git Push"                  => Act.GitPush,
            "Clean Project"             => Act.Clean,
            _                           => Act.Back,
        };

        if (act == Act.Back)
        {
            AnsiConsole.MarkupLine("  [yellow](!) This action cannot be repeated.[/]");
            Pause(); return;
        }

        HandleAction(act, app, ref gitStatus, st, cfg);
    }

    private void SetVerbosityAction(PersistentState st)
    {
        AnsiConsole.WriteLine();
        var options  = new[] { "quiet", "minimal", "normal", "detailed", "diagnostic" };
        var listItems = options.Select(o => new ForgeMenu.ListItem<string>(o, o)).ToList();
        var chosen = ForgeMenu.PromptList("Build verbosity (dotnet -v):", listItems);
        if (chosen is null) return;
        st.Verbosity = chosen;
        _verbosity   = chosen;
        state.Save(st);
        AnsiConsole.MarkupLine($"  [green]ok  Verbosity set to[/] [cyan1]{chosen}[/].");
        Pause();
    }

    // ── Open in Editor ───────────────────────────────────────────────────────

    private static string? DetectEditor()
    {
        var candidates = new[] { ("code", "VS Code"), ("rider", "Rider"), ("idea", "IntelliJ"), ("zed", "Zed") };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";
        foreach (var (cmd, label) in candidates)
        {
            foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator))
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir.Trim(), cmd + exeSuffix)))
                    return label;
            }
        }
        return null;
    }

    private static void OpenInEditorAction(AppEntry app)
    {
        var editors = new[] { "code", "rider", "idea", "zed" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var exeSuffix = OperatingSystem.IsWindows() ? ".exe" : "";

        string? found = null;
        foreach (var cmd in editors)
        {
            foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator))
            {
                var full = System.IO.Path.Combine(dir.Trim(), cmd + exeSuffix);
                if (System.IO.File.Exists(full)) { found = cmd; break; }
            }
            if (found is not null) break;
        }

        if (found is null)
        {
            AnsiConsole.MarkupLine("  [yellow](!) No supported editor found in PATH (code, rider, idea, zed).[/]");
            Pause(); return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(found)
            {
                UseShellExecute = true,
            };
            psi.ArgumentList.Add(app.Dir);
            System.Diagnostics.Process.Start(psi);
            AnsiConsole.MarkupLine($"  [green]ok  Opening in {Markup.Escape(found)}...[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]x  Failed to open editor: {Markup.Escape(ex.Message)}[/]");
            Pause();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool ConfigureMac(PersistentState st)
    {
        if (st.UseLocalMac) return true;
        if (st.MacHost is { Length: > 0 } && st.MacUser is { Length: > 0 }) return true;
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  [yellow](!) Mac not configured. Go to Mac / SSH config from the main menu.[/]");
        Pause(); return false;
    }

    private string? PickBuildConfig(string csproj, string typeFilter, string fallback)
    {
        var all = versions.GetBuildConfigurations(csproj);

        List<string> list;
        if (string.IsNullOrWhiteSpace(typeFilter))
        {
            // For run flows, always expose the common build modes even if the csproj only
            // explicitly lists one configuration.
            list = all
                .Concat(["Debug", "Release"])
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            var filtered = all.Where(c => c.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            list = filtered.Count > 0 ? filtered : [fallback];
        }

        if (list.Count == 1) return list[0];

        return ForgeMenu.PromptList("Build Configuration:",
            list.Select(c => new ForgeMenu.ListItem<string>(c, c)).ToList());
    }

    private string? PickFramework(string csproj, string filter, string fallback)
    {
        var list = versions.GetTargetFrameworks(csproj, filter);
        if (list.Count == 0) return fallback;
        if (list.Count == 1) return list[0];

        return ForgeMenu.PromptList("Target Framework:",
            list.Select(f => new ForgeMenu.ListItem<string>(f, f)).ToList());
    }

    private static void AddIosRunDeviceArgs(List<string> args, iOSDevice device)
    {
        if (device.Type == "Device")
        {
            args.Add("-p:RuntimeIdentifier=ios-arm64");
            args.Add($"-p:_DeviceName={device.Udid}");
            return;
        }

        args.Add($"-p:_DeviceName=:v2:udid={device.Udid}");
    }

    private static string InferIosDeviceType(string udid) =>
        udid.Contains('-', StringComparison.Ordinal) ? "Simulator" : "Device";

    private static string ShortDeviceId(string udid) =>
        udid.Length <= 8 ? udid : udid[..8] + "...";

    private void ApplyVersion(AppEntry app, string version, string bld)
    {
        var csproj = FindCsproj(app.Dir);
        if (app.Versions.iOS     is not null) versions.WriteiOS(app.Dir, version, bld);
        if (app.Versions.Android is not null) versions.WriteAndroid(app.Dir, version, bld);
        if (csproj               is not null) versions.WriteCsproj(csproj, version, bld);
    }

    private string _verbosity = "quiet";
    private readonly List<string> _lastBuildOutput = new();

    private bool RunBuild(string dir, string[] args, bool pauseWhenDone = true)
    {
        _lastBuildOutput.Clear();
        var allArgs = args.Concat(["-v", _verbosity]).ToArray();

        var sw      = System.Diagnostics.Stopwatch.StartNew();
        int exit    = 0;
        var errorLines = new List<string>();
        var allLines   = new List<string>();

        void CaptureLine(string line)
        {
            allLines.Add(line);
            _lastBuildOutput.Add(line);
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                errorLines.Add(line);
        }

        if (ShowsLiveBuildOutput(_verbosity))
        {
            AnsiConsole.MarkupLine($"  [dim]dotnet {Markup.Escape(string.Join(' ', allArgs))}[/]");
            AnsiConsole.WriteLine();

            var consoleLock = new object();
            exit = build.Run(dir, allArgs, line =>
            {
                CaptureLine(line);
                lock (consoleLock)
                {
                    var style = line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        ? "red"
                        : line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                            ? "yellow"
                            : "grey70";
                    AnsiConsole.MarkupLine($"  [{style}]{Markup.Escape(line)}[/]");
                }
            });
        }
        else
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan1"))
                .Start($"  [dim]dotnet {string.Join(' ', args.Take(2))}...[/]", _ =>
                {
                    exit = build.Run(dir, allArgs, CaptureLine);
                });
        }
        sw.Stop();

        var t = sw.Elapsed.TotalMinutes >= 1
            ? $"{(int)sw.Elapsed.TotalMinutes}m {sw.Elapsed.Seconds}s"
            : $"{sw.Elapsed.TotalSeconds:F1}s";

        AnsiConsole.WriteLine();

        if (exit != 0)
        {
            // Show last lines that contain errors, plus context
            var relevantLines = allLines
                .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)
                         || l.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (relevantLines.Count == 0)
                relevantLines = allLines.TakeLast(20).ToList();

            foreach (var line in relevantLines)
                AnsiConsole.MarkupLine($"  [red]{Markup.Escape(line)}[/]");

            AnsiConsole.WriteLine();
        }

        AnsiConsole.Write(new Rule(exit == 0
            ? $"[green]ok  Completed in {t}[/]"
            : $"[red]x  Build failed in {t} (exit {exit})[/]")
            .RuleStyle(exit == 0 ? Style.Parse("green dim") : Style.Parse("red dim")));

        if (pauseWhenDone) Pause();
        return exit == 0;
    }

    private static bool ShowsLiveBuildOutput(string verbosity) =>
        verbosity is "normal" or "detailed" or "diagnostic";

    private static AppBuildConfig GetOrCreateConfig(PersistentState st, AppEntry app)
    {
        if (!st.AppBuildConfigs.TryGetValue(app.Dir, out var cfg))
        {
            cfg = new AppBuildConfig();
            st.AppBuildConfigs[app.Dir] = cfg;
        }
        return cfg;
    }

    private AppEntry RefreshApp(AppEntry app)
    {
        var csproj = FindCsproj(app.Dir);
        var ios = versions.ReadiOS(app.Dir);
        var android = versions.ReadAndroid(app.Dir);
        var csprojVersion = csproj is not null ? versions.ReadCsproj(csproj) : null;

        return app with
        {
            Branch = git.GetBranch(app.Dir),
            Versions = new AppVersions(ios, android, csprojVersion),
            Git = git.GetStatus(app.Dir),
        };
    }

    private static string? FindCsproj(string dir) =>
        Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();

    private static void NoCsproj() =>
        AnsiConsole.MarkupLine("  [red]x  No .csproj found.[/]");

    private static void SaveSnapshot(AppEntry app, PlatformVersion current, PersistentState st) =>
        st.LastVersion = new() { AppDir = app.Dir, Version = current.Version, Build = current.Build };

    private static void Pause() =>
        AnsiConsole.Prompt(new TextPrompt<string>("\n  [dim]Press Enter to continue...[/]").AllowEmpty());
}
