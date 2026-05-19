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
        GitPull, GitCommit, GitPush, Clean, Undo, SetVerbosity, RepeatLast, Back
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

        var iosLine = v.iOS is { } ios
            ? $"[skyblue1](iOS)[/]     [bold white]{Markup.Escape(ios.Version)}[/]  [dim]build #{ios.Build}[/]"
            : "[grey46](iOS)[/]     [dim]not detected[/]";

        var andLine = v.Android is { } and
            ? $"[green3](Android)[/]  [bold white]{Markup.Escape(and.Version)}[/]  [dim]build #{and.Build}[/]"
            : "[grey46](Android)[/]  [dim]not detected[/]";

        var syncLine = (v.iOS, v.Android) switch
        {
            (not null, not null) when  v.InSync => "  [green](ok) iOS and Android in sync[/]",
            (not null, not null) when !v.InSync => "  [yellow](!!) iOS and Android out of sync[/]",
            _ => ""
        };

        var bc       = app.Branch is "main" or "master" ? "green" : "fuchsia";
        var gitIcon  = gitStatus.Dirty ? "[yellow]~[/]" : "[green]✓[/]";
        var gitExtra = new List<string>();
        if (gitStatus.Ahead  > 0) gitExtra.Add($"[yellow]^{gitStatus.Ahead} to push[/]");
        if (gitStatus.Behind > 0) gitExtra.Add($"[red]v{gitStatus.Behind} to pull[/]");
        var gitDetail = gitExtra.Count > 0 ? "  " + string.Join("  ", gitExtra) : "";

        var buildCfg = cfg.BuildConfiguration is { } bc2 ? $"[cyan1]{Markup.Escape(bc2)}[/]" : "[grey46]not set[/]";
        var iosFw    = cfg.iOSFramework        is { } fw  ? $"[skyblue1]{Markup.Escape(fw)}[/]"  : "[grey46]—[/]";
        var andFw    = cfg.AndroidFramework    is { } af  ? $"[green3]{Markup.Escape(af)}[/]"    : "[grey46]—[/]";
        var iosDev   = cfg.iOSDeviceId         is not null ? "[skyblue1]device configured[/]"    : "[grey46]none[/]";
        var andDev   = cfg.AndroidDeviceSerial is { } s   ? $"[green3]{Markup.Escape(s)}[/]"    : "[grey46]none[/]";
        var macMode  = st.UseLocalMac
            ? "[green]local Mac[/]"
            : (st.MacHost is { } h ? $"[cyan1]{Markup.Escape(h)}[/]" : "[grey46]not configured[/]");

        var content = string.Join("\n",
            iosLine,
            andLine,
            syncLine.Length > 0 ? syncLine : null,
            "",
            $"  [dim]branch[/]    [{bc}]{Markup.Escape(app.Branch)}[/]   {gitIcon}{gitDetail}",
            "",
            $"  [dim]config[/]    {buildCfg}   [dim]ios fw[/] {iosFw}   [dim]droid fw[/] {andFw}",
            $"  [dim]ios dev[/]   {iosDev}   [dim]android dev[/]  {andDev}",
            $"  [dim]mac[/]       {macMode}",
            "",
            $"  [dim]{Markup.Escape(app.Dir)}[/]"
        );

        AnsiConsole.Write(
            new Panel(content)
                .Header($"[bold cyan1]  >>> {Markup.Escape(app.Name)}  [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Cyan1)
                .Padding(1, 0)
        );
        AnsiConsole.WriteLine();
    }

    // ── Action Menu ──────────────────────────────────────────────────────────

    private Act PromptAction(AppEntry app, GitStatus gitStatus, AppBuildConfig cfg, PersistentState st)
    {
        var master  = app.Versions.Master;
        var nextVer = master is not null ? VersionService.IncrementVersion(master.Version) : "—";
        var nextBld = master is not null && int.TryParse(master.Build, out var b) ? (b + 1).ToString() : "—";

        var hasSnap  = st.LastVersion?.AppDir == app.Dir;
        var snapHint = hasSnap ? $"[dim]→ {st.LastVersion!.Version} #{st.LastVersion.Build}[/]" : "[grey46]no snapshot[/]";
        var syncWarn = !app.Versions.InSync && app.Versions.iOS is not null && app.Versions.Android is not null;
        var iosDev   = cfg.iOSDeviceId is not null ? "[dim]device ok[/]" : "[grey46]no device[/]";
        var andDev   = cfg.AndroidDeviceSerial is not null ? $"[dim]{Markup.Escape(cfg.AndroidDeviceSerial)}[/]" : "[grey46]no device[/]";
        var iosFw    = cfg.iOSFramework     is { } fw ? $"[dim]{Markup.Escape(fw)}[/]" : "[grey46]—[/]";
        var andFw    = cfg.AndroidFramework is { } af ? $"[dim]{Markup.Escape(af)}[/]" : "[grey46]—[/]";
        var sign     = cfg.CodesignKey is { } cs ? $"[dim]{Markup.Escape(cs)}[/]" : "[grey46]—[/]";
        var gitClean = !gitStatus.Dirty && gitStatus.Ahead == 0 && gitStatus.Behind == 0;

        var items = new List<(string Label, Act Action)>();
        void Add(string label, Act act) => items.Add((label, act));

        // ── Version
        Add($"  [bold cyan1]v+[/]   [white]Increment Version + Build[/]" +
            $"    [dim]{Markup.Escape(master?.Version ?? "-")} -> {Markup.Escape(nextVer)}  " +
            $"#{Markup.Escape(master?.Build ?? "-")} -> #{Markup.Escape(nextBld)}[/]",
            Act.IncrementVersion);

        Add($"  [bold cyan1]b+[/]   [white]Increment Build only[/]" +
            $"          [dim]#{Markup.Escape(master?.Build ?? "-")} -> #{Markup.Escape(nextBld)}[/]",
            Act.IncrementBuild);

        Add("  [bold cyan1]~~[/]   [white]Set version manually[/]", Act.SetManual);

        Add($"  [bold cyan1]<>[/]   [white]Sync iOS -- Android[/]" +
            (syncWarn ? "              [yellow](!!) out of sync[/]" : "              [dim](ok) in sync[/]"),
            Act.Sync);

        // ── iOS
        Add($"  [bold skyblue1][[#]][/]  [white]Archive iOS[/] [dim](Release)[/]" +
            $"         {iosFw}  {sign}",
            Act.ArchiveiOS);

        Add($"  [bold skyblue1][[>]][/]  [white]Run iOS Device[/]" +
            $"               {iosDev}",
            Act.RuniOS);

        // ── Android
        Add($"  [bold green3][[>]][/]  [white]Run Android Device[/]" +
            $"            {andDev}",
            Act.RunAndroid);

        Add($"  [bold green3][[#]][/]  [white]Publish Android[/] [dim](Release)[/]" +
            $"     {andFw}",
            Act.PublishAndroid);

        // ── Git & Build
        var gitStatus2 = gitClean ? "[green]clean[/]" : "[yellow]pending[/]";
        Add($"  [bold yellow]git[/]  [white]Git Pull[/]" +
            $"                     {gitStatus2}",
            Act.GitPull);

        var commitHint = gitStatus.Dirty ? "[yellow]changes staged[/]" : "[dim]working tree clean[/]";
        Add($"  [bold yellow]cmt[/]  [white]Git Commit[/]" +
            $"                   {commitHint}",
            Act.GitCommit);

        var pushHint = gitStatus.Ahead > 0 ? $"[yellow]^{gitStatus.Ahead} to push[/]" : "[dim]up to date[/]";
        Add($"  [bold yellow]psh[/]  [white]Git Push[/]" +
            $"                     {pushHint}",
            Act.GitPush);

        Add("  [bold yellow]clr[/]  [white]Clean Project[/]", Act.Clean);

        // ── Misc
        var verbosity  = st.Verbosity ?? "quiet";
        var lastAction = st.LastAction is { Length: > 0 } ? $"[dim]{Markup.Escape(st.LastAction)}[/]" : "[grey46]none[/]";
        Add($"  [bold grey53]<<[/]   [white]Undo last version change[/]      {snapHint}", Act.Undo);
        Add($"  [bold grey53]>>|[/]  [white]Repeat last action[/]            {lastAction}", Act.RepeatLast);
        Add($"  [bold grey53]~~~[/]  [white]Build verbosity:[/] [cyan1]{Markup.Escape(verbosity)}[/]", Act.SetVerbosity);
        Add("  [bold grey53] x[/]   [white]Back[/]", Act.Back);

        var verItems  = items.Where(x => x.Action is Act.IncrementVersion or Act.IncrementBuild or Act.SetManual or Act.Sync).ToList();
        var iosItems  = items.Where(x => x.Action is Act.ArchiveiOS or Act.RuniOS).ToList();
        var andItems  = items.Where(x => x.Action is Act.RunAndroid or Act.PublishAndroid).ToList();
        var gitItems  = items.Where(x => x.Action is Act.GitPull or Act.GitCommit or Act.GitPush or Act.Clean).ToList();
        var miscItems = items.Where(x => x.Action is Act.Undo or Act.RepeatLast or Act.SetVerbosity or Act.Back).ToList();

        var prompt = new SelectionPrompt<string>()
            .Title("[cyan1]What would you like to do?[/]")
            .PageSize(20)
            .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
            .AddChoiceGroup(Markup.Escape("-- Version " + new string('-', 57)), verItems.Select(x => x.Label).ToList())
            .AddChoiceGroup(Markup.Escape("-- iOS " + new string('-', 61)), iosItems.Select(x => x.Label).ToList())
            .AddChoiceGroup(Markup.Escape("-- Android " + new string('-', 57)), andItems.Select(x => x.Label).ToList())
            .AddChoiceGroup(Markup.Escape("-- Git & Build " + new string('-', 53)), gitItems.Select(x => x.Label).ToList())
            .AddChoiceGroup(Markup.Escape(new string('-', 68)), miscItems.Select(x => x.Label).ToList());

        var chosen = AnsiConsole.Prompt(prompt);
        return items.First(x => x.Label == chosen).Action;
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
        var source = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  [cyan1]Use which version as source?[/]")
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .AddChoices(
                    $"(iOS)     [skyblue1]{Markup.Escape(v.iOS.Version)}[/] [dim]#{v.iOS.Build}[/]",
                    $"(Android) [green3]{Markup.Escape(v.Android.Version)}[/] [dim]#{v.Android.Build}[/]"
                )
        );
        var (ver, bld) = source.StartsWith("(iOS)") ? (v.iOS.Version, v.iOS.Build) : (v.Android.Version, v.Android.Build);
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
            var choices = providers.Select(p => $"  {p.Icon}  {p.Name}").ToList();
            choices.Add("  ✎  Write manually");

            var pick = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("  [cyan1]Generate commit message with:[/]")
                    .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                    .AddChoices(choices));

            if (pick.Contains("Write manually"))
            {
                message = AnsiConsole.Ask<string>("  [cyan1]Commit message:[/]");
            }
            else
            {
                var provider = providers[choices.IndexOf(pick)];
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

        var choices = new SelectionPrompt<string>()
            .Title("  [cyan1]Select iOS device:[/]")
            .HighlightStyle(new Style(foreground: Color.SkyBlue1, background: Color.Grey11))
            .PageSize(18);

        if (physical.Count   > 0) choices.AddChoiceGroup(
            Markup.Escape("-- Physical device " + new string('-', 39)),
            physical.Select(d => $"  [skyblue1](iOS)[/] {Markup.Escape(d.Name)}  [dim]{d.Udid[..8]}...[/]").ToList());

        if (simulators.Count > 0) choices.AddChoiceGroup(
            Markup.Escape("-- Simulator " + new string('-', 45)),
            simulators.Select(d => $"  [grey53](sim)[/] {Markup.Escape(d.Name)}  [dim]{d.Udid[..8]}...[/]").ToList());

        var picked = AnsiConsole.Prompt(choices);
        var found  = deviceList.FirstOrDefault(d => picked.Contains(d.Udid[..8]));
        if (found is null) { AnsiConsole.MarkupLine("  [red]x  Device not identified.[/]"); Pause(); return; }

        cfg.iOSDeviceId        = found.Udid;
        cfg.BuildConfiguration = PickBuildConfig(csproj, "Debug", "Debug");
        cfg.iOSFramework       = PickFramework(csproj, "ios", cfg.iOSFramework ?? "net9.0-ios");
        state.Save(st);

        var args = new List<string>
        {
            "build", csproj, "-t:Run",
            "-f", cfg.iOSFramework,
            "-c", cfg.BuildConfiguration,
            $"-p:_DeviceId={found.Udid}",
        };
        if (!st.UseLocalMac)
        {
            args.Add($"-p:ServerAddress={st.MacHost}");
            args.Add($"-p:ServerUser={st.MacUser}");
        }
        AnsiConsole.WriteLine();
        st.LastAction = "Run iOS Device";
        state.Save(st);
        RunBuild(app.Dir, [.. args]);
    }

    private void ArchiveIOSAction(AppEntry app, PersistentState st, AppBuildConfig cfg)
    {
        var csproj = FindCsproj(app.Dir);
        if (csproj is null) { NoCsproj(); return; }
        if (!ConfigureMac(st)) return;

        cfg.BuildConfiguration = PickBuildConfig(csproj, "Release", "Release");
        cfg.iOSFramework       = PickFramework(csproj, "ios", cfg.iOSFramework ?? "net9.0-ios");

        var sign = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  [cyan1]Code Sign key:[/]")
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .AddChoices("Apple Development", "Apple Distribution", "Custom…")
        );
        if (sign == "Custom…") sign = AnsiConsole.Ask<string>("  [cyan1]Code Sign key:[/]", cfg.CodesignKey ?? "Apple Distribution");
        cfg.CodesignKey = sign;
        state.Save(st);

        var outDir = Path.Combine(app.Dir, "bin", "Release", "archive");
        var args   = new List<string> { "publish", csproj, "-f", cfg.iOSFramework, "-c", cfg.BuildConfiguration,
            $"-p:ServerAddress={st.MacHost}", $"-p:ServerUser={st.MacUser}",
            "-p:ArchiveOnBuild=true", $"-p:CodesignKey={sign}", "-o", outDir };
        AnsiConsole.MarkupLine($"  [dim]Output: {Markup.Escape(outDir)}[/]");
        AnsiConsole.WriteLine();
        st.LastAction = "Archive iOS";
        state.Save(st);
        RunBuild(app.Dir, [.. args]);
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
            AnsiConsole.MarkupLine("  [dim]Install Android SDK Platform-Tools or set ANDROID_HOME.[/]");
            AnsiConsole.MarkupLine($"  [dim]Checked PATH and common SDK locations.[/]");
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

        var choices = new SelectionPrompt<string>()
            .Title("  [cyan1]Select Android device or emulator:[/]")
            .HighlightStyle(new Style(foreground: Color.Green3, background: Color.Grey11))
            .PageSize(20);

        if (physical.Count > 0) choices.AddChoiceGroup(
            Markup.Escape("-- Physical device " + new string('-', 39)),
            physical.Select(d => $"  [green3](droid)[/] {Markup.Escape(d.Model)}  [dim]{Markup.Escape(d.Serial)}[/]").ToList());

        if (running.Count > 0) choices.AddChoiceGroup(
            Markup.Escape("-- Running emulator " + new string('-', 39)),
            running.Select(d => $"  [grey53](emu ▶)[/] {Markup.Escape(d.Model)}  [dim]{Markup.Escape(d.Serial)}[/]").ToList());

        if (avds.Count > 0) choices.AddChoiceGroup(
            Markup.Escape("-- Available AVDs (will start) " + new string('-', 28)),
            avds.Select(a => $"  [grey53](avd)[/] {Markup.Escape(a)}").ToList());

        var picked   = AnsiConsole.Prompt(choices);
        string? serial = null;

        // Check if picked is a running device
        var foundDevice = online.FirstOrDefault(d => picked.Contains(d.Serial));
        if (foundDevice is not null)
        {
            serial = foundDevice.Serial;
            cfg.AndroidDeviceSerial = serial;
        }
        else
        {
            // It's an AVD — start it
            var avdName = avds.FirstOrDefault(a => picked.Contains(a));
            if (avdName is null) { AnsiConsole.MarkupLine("  [red]x  Could not identify selection.[/]"); Pause(); return; }

            AnsiConsole.MarkupLine($"  [dim]Starting emulator: {Markup.Escape(avdName)}[/]");
            serial = StartAvdAndWait(avdName, adbPath);
            if (serial is null)
            {
                AnsiConsole.MarkupLine("  [red]x  Emulator did not come online in time.[/]");
                Pause(); return;
            }
            cfg.AndroidDeviceSerial = serial;
        }

        cfg.BuildConfiguration = PickBuildConfig(csproj, "Debug", "Debug");
        cfg.AndroidFramework   = PickFramework(csproj, "android", cfg.AndroidFramework ?? "net9.0-android");
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
            .Start("  [dim]Waiting for emulator to boot...[/]", _ =>
            {
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
            });

        return found;
    }

    private void PublishAndroidAction(AppEntry app, PersistentState st, AppBuildConfig cfg)
    {
        var csproj = FindCsproj(app.Dir);
        if (csproj is null) { NoCsproj(); return; }

        cfg.BuildConfiguration = PickBuildConfig(csproj, "Release", "Release");
        cfg.AndroidFramework   = PickFramework(csproj, "android", cfg.AndroidFramework ?? "net9.0-android");
        state.Save(st);

        var outDir = Path.Combine(app.Dir, "bin", cfg.BuildConfiguration, cfg.AndroidFramework, "publish");
        var args   = new List<string> { "publish", csproj, "-f", cfg.AndroidFramework, "-c", cfg.BuildConfiguration };
        AnsiConsole.MarkupLine($"  [dim]Output: {Markup.Escape(outDir)}[/]");
        AnsiConsole.WriteLine();
        st.LastAction = "Publish Android";
        state.Save(st);
        RunBuild(app.Dir, [.. args]);

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
        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  [cyan1]Build verbosity (dotnet -v):[/]")
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .AddChoices("quiet", "minimal", "normal", "detailed", "diagnostic"));
        st.Verbosity = chosen;
        _verbosity   = chosen;
        state.Save(st);
        AnsiConsole.MarkupLine($"  [green]ok  Verbosity set to[/] [cyan1]{chosen}[/].");
        Pause();
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

    private string PickBuildConfig(string csproj, string typeFilter, string fallback)
    {
        var all      = versions.GetBuildConfigurations(csproj);
        var filtered = all.Where(c => c.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        var list     = filtered.Count > 0 ? filtered : [fallback];

        if (list.Count == 1) return list[0];

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"  [cyan1]Build Configuration[/] [dim]({Markup.Escape(typeFilter)}):[/]")
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .AddChoices(list)
        );
    }

    private string PickFramework(string csproj, string filter, string fallback)
    {
        var list = versions.GetTargetFrameworks(csproj, filter);
        if (list.Count == 0) return fallback;
        if (list.Count == 1) return list[0];
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("  [cyan1]Target Framework:[/]")
                .HighlightStyle(new Style(foreground: Color.Cyan1, background: Color.Grey11))
                .AddChoices(list)
        );
    }

    private void ApplyVersion(AppEntry app, string version, string bld)
    {
        var csproj = FindCsproj(app.Dir);
        if (app.Versions.iOS     is not null) versions.WriteiOS(app.Dir, version, bld);
        if (app.Versions.Android is not null) versions.WriteAndroid(app.Dir, version, bld);
        if (csproj               is not null) versions.WriteCsproj(csproj, version, bld);
    }

    private string _verbosity = "quiet";

    private void RunBuild(string dir, string[] args)
    {
        var allArgs = args.Concat(["-v", _verbosity]).ToArray();

        var sw   = System.Diagnostics.Stopwatch.StartNew();
        int exit = 0;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan1"))
            .Start($"  [dim]dotnet {string.Join(' ', args.Take(2))}...[/]", _ =>
            {
                exit = build.Run(dir, allArgs, line =>
                {
                    var color = line.Contains("error",   StringComparison.OrdinalIgnoreCase) ? "red"
                              : line.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "yellow"
                              : "dim";
                    AnsiConsole.MarkupLine($"  [{color}]{Markup.Escape(line)}[/]");
                });
            });
        sw.Stop();
        var t = sw.Elapsed.TotalMinutes >= 1
            ? $"{(int)sw.Elapsed.TotalMinutes}m {sw.Elapsed.Seconds}s"
            : $"{sw.Elapsed.TotalSeconds:F1}s";
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(exit == 0
            ? $"[green]ok  Completed in {t}[/]"
            : $"[red]x  Failed in {t} (exit {exit})[/]")
            .RuleStyle(exit == 0 ? Style.Parse("green dim") : Style.Parse("red dim")));
        Pause();
    }

    private static AppBuildConfig GetOrCreateConfig(PersistentState st, AppEntry app)
    {
        if (!st.AppBuildConfigs.TryGetValue(app.Dir, out var cfg))
        {
            cfg = new AppBuildConfig();
            st.AppBuildConfigs[app.Dir] = cfg;
        }
        return cfg;
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
