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
        var snapHint = hasSnap ? $"{st.LastVersion!.Version} #{st.LastVersion.Build}" : "no snapshot";
        var syncWarn = !app.Versions.InSync && app.Versions.iOS is not null && app.Versions.Android is not null;
        var iosDev   = cfg.iOSDeviceId is not null ? "device ok" : "no device";
        var andDev   = cfg.AndroidDeviceSerial ?? "no device";
        var iosFw    = cfg.iOSFramework  ?? "—";
        var andFw    = cfg.AndroidFramework ?? "—";
        var sign     = cfg.CodesignKey ?? "—";
        var verbosity = st.Verbosity ?? "quiet";
        var lastAct  = st.LastAction ?? "none";
        var gitClean = !gitStatus.Dirty && gitStatus.Ahead == 0 && gitStatus.Behind == 0;

        var groups = new List<ForgeMenu.KeyGroup>
        {
            new("Version", [
                new('v', "[white]Increment Version + Build[/]",
                    $"{master?.Version ?? "-"} → {nextVer}  #{master?.Build ?? "-"} → #{nextBld}"),
                new('b', "[white]Increment Build only[/]",
                    $"#{master?.Build ?? "-"} → #{nextBld}"),
                new('m', "[white]Set version manually[/]"),
                new('s', "[white]Sync iOS ↔ Android[/]",
                    syncWarn ? "(!) out of sync" : "(ok) in sync"),
            ]),
            new("iOS", [
                new('a', "[skyblue1]Archive iOS[/] [dim](Release)[/]", $"{iosFw}  {sign}"),
                new('i', "[skyblue1]Run iOS Device[/]", iosDev),
            ]),
            new("Android", [
                new('r', "[green3]Run Android Device[/]", andDev),
                new('p', "[green3]Publish Android[/] [dim](Release)[/]", andFw),
            ]),
            new("Git & Build", [
                new('u', "[yellow]Git Pull[/]", gitClean ? "clean" : "pending"),
                new('c', "[yellow]Git Commit[/]", gitStatus.Dirty ? "changes pending" : "clean"),
                new('h', "[yellow]Git Push[/]", gitStatus.Ahead > 0 ? $"^{gitStatus.Ahead} to push" : "up to date"),
                new('n', "[yellow]Clean Project[/]"),
            ]),
            new("Misc", [
                new('z', "[dim]Undo last version change[/]", snapHint),
                new('.', "[dim]Repeat last action[/]", lastAct),
                new('x', $"[dim]Build verbosity:[/] [cyan1]{verbosity}[/]"),
            ]),
        };

        var key = ForgeMenu.PromptKey("What would you like to do?  [dim](ESC = back)[/]", groups);

        return key switch
        {
            'v'  => Act.IncrementVersion,
            'b'  => Act.IncrementBuild,
            'm'  => Act.SetManual,
            's'  => Act.Sync,
            'a'  => Act.ArchiveiOS,
            'i'  => Act.RuniOS,
            'r'  => Act.RunAndroid,
            'p'  => Act.PublishAndroid,
            'u'  => Act.GitPull,
            'c'  => Act.GitCommit,
            'h'  => Act.GitPush,
            'n'  => Act.Clean,
            'z'  => Act.Undo,
            '.'  => Act.RepeatLast,
            'x'  => Act.SetVerbosity,
            _    => Act.Back,  // ESC or unknown
        };
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

        var listItems = new List<ForgeMenu.ListItem<iOSDevice>>();
        if (physical.Count > 0)
        {
            listItems.Add(new("Physical devices", null!, IsSeparator: true));
            listItems.AddRange(physical.Select(d =>
                new ForgeMenu.ListItem<iOSDevice>($"[skyblue1](iOS)[/] {Markup.Escape(d.Name)}  [dim]{d.Udid[..8]}...[/]", d)));
        }
        if (simulators.Count > 0)
        {
            listItems.Add(new("Simulators", null!, IsSeparator: true));
            listItems.AddRange(simulators.Select(d =>
                new ForgeMenu.ListItem<iOSDevice>($"[grey53](sim)[/] {Markup.Escape(d.Name)}  [dim]{d.Udid[..8]}...[/]", d)));
        }

        var found = ForgeMenu.PromptList("Select iOS device:", listItems);
        if (found is null) return;

        cfg.iOSDeviceId        = found!.Udid;
        cfg.BuildConfiguration = PickBuildConfig(csproj, "Debug", "Debug");
        if (cfg.BuildConfiguration is null) return;
        cfg.iOSFramework       = PickFramework(csproj, "ios", cfg.iOSFramework ?? "net9.0-ios");
        if (cfg.iOSFramework is null) return;
        state.Save(st);

        var args = new List<string>
        {
            "build", csproj, "-t:Run",
            "-f", cfg.iOSFramework,
            "-c", cfg.BuildConfiguration,
            $"-p:_DeviceId={found!.Udid}",
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
        if (avds.Count > 0)
        {
            andListItems.Add(new("Available AVDs (will start)", null!, IsSeparator: true));
            andListItems.AddRange(avds.Select(a =>
                new ForgeMenu.ListItem<string>($"[grey53](avd)[/] {Markup.Escape(a)}", "avd:" + a)));
        }

        var picked = ForgeMenu.PromptList("Select Android device or emulator:", andListItems);
        if (picked is null) return;

        string? serial = null;

        if (picked.StartsWith("avd:"))
        {
            var avdName = picked[4..];
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
        else
        {
            serial = picked;
            cfg.AndroidDeviceSerial = serial;
        }

        cfg.BuildConfiguration = PickBuildConfig(csproj, "Debug", "Debug");
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
        var all      = versions.GetBuildConfigurations(csproj);
        var filtered = all.Where(c => c.Contains(typeFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        var list     = filtered.Count > 0 ? filtered : [fallback];

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
