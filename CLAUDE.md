# MAUI Forge — CLAUDE.md

## What this project is

A cross-platform CLI + web tool written in **C# .NET 10** that replaces the PowerShell script `maui-version.ps1`. It manages versions, builds, and deployments for .NET MAUI apps via an interactive terminal TUI and a web dashboard.

Repository: `https://github.com/CW-Software-Apps/maui-forge`  
Distribution: `dotnet tool install -g CwSoftware.MauiForge` (NuGet) or self-contained exe via install scripts.

## Stack

- **C# .NET 10**, console app + ASP.NET Core Minimal API
- **Spectre.Console** — TUI (tables, prompts, panels, progress, colors)
- **Spectre.Console.Cli** — CLI arg parsing (`--depth`, `--path`, `--cli`, `--web`)
- **ASP.NET Core + SignalR** — web dashboard (`localhost:5123`), real-time build log streaming
- **Tailwind CSS** — web dashboard (CDN, no build step)
- **Microsoft.Extensions.DependencyInjection** — DI between services

## Distribution

- **NuGet `dotnet tool`** (primary) — `dotnet tool install -g CwSoftware.MauiForge`
- **Self-contained exe** via `install.ps1` (Windows) / `install.sh` (macOS/Linux) as fallback
- **Auto-publish** via GitHub Actions on `v*` tag push

## Mode

- **Default** (`maui-forge`): starts web dashboard on `http://localhost:5123`
- **`--cli` / `--terminal`**: starts traditional terminal TUI
- **`--web`**: explicit web dashboard

## Project structure

```
maui-forge/
├── assets/
│   ├── banner.png                 ← GitHub repo banner
│   ├── icon.png                   ← NuGet package icon (256×256)
│   └── icon.ico                   ← Application icon
├── .github/workflows/
│   └── publish.yml                ← Auto-publish to NuGet on v* tag push
├── install.ps1                    ← Windows: self-contained exe → dist/ → PATH + desktop shortcut
├── install.sh                     ← macOS/Linux: dotnet tool install -g, ensures ~/.dotnet/tools in profile
├── PRODUCT.md                     ← Product strategy ("Your MAUI release copilot")
├── DESIGN.md                      ← Design system (OKLCH tokens, typography, components)
├── README.md                      ← Full documentation
└── src/MauiForge/
    ├── MauiForge.csproj           ← .NET 10, packed as dotnet tool, refs Spectre + ASP.NET Core
    ├── Program.cs                 ← Entry: arg parsing, DI setup, TUI main loop or web routing
    ├── Models/
    │   ├── AppEntry.cs            ← record: Name, Dir, Branch, Versions, Git, ProjectType, IconBase64
    │   ├── AppVersions.cs         ← record: iOS?, Android?, Csproj? + PlatformVersion, Master, InSync
    │   ├── GitStatus.cs           ← record: Ahead, Behind, Dirty, LastCommit
    │   └── PersistentState.cs     ← All persisted state + VersionSnapshot + AppBuildConfig per app
    ├── Services/
    │   ├── AppDiscoveryService.cs ← Scans dirs for *.csproj + Unity, builds AppEntry[], reads icons
    │   ├── VersionService.cs      ← Read/write Info.plist, AndroidManifest.xml, .csproj, AssemblyInfo.cs, Unity
    │   ├── GitService.cs          ← status (porcelain v2), fetch, FetchAndGetStatus, pull, commit, push, push-only, diff stat, unpushed commits, suggest commit msg
    │   ├── BuildService.cs        ← dotnet with async line streaming, log file, process capture for cancel
    │   ├── DeviceService.cs       ← iOS (SSH/local xcrun/xctrace/xcdevice/instruments), Android (adb + emulator + AVD), ARP scan, FindAdb/FindEmulator
    │   ├── StateService.cs        ← ~/.maui-forge.state.json load/save, RecordUsage
    │   ├── UpdateService.cs       ← NuGet version check (background), deferred update (batch/Unix), ForceCheck
    │   ├── AiCommitService.cs     ← Claude CLI / Gemini CLI / Ollama (HTTP) / Smart suggestion fallback
    │   └── ProcessEnvironment.cs  ← Forces English CLI output on spawned processes
    ├── UI/
    │   ├── AppListScreen.cs       ← Main dashboard: app table, filters, folder browser, menu, scan
    │   ├── AppDetailScreen.cs     ← Detail panel: version/git/run profile, all actions (bump, run, archive, publish, git, commit, clean, IDE)
    │   ├── DiagnosticsScreen.cs   ← dotnet/git/ssh/xcrun/adb/emulator/workloads status table
    │   └── ForgeMenu.cs           ← Selection prompt helpers: PromptList<T>, PromptListValue<T> with groups + Back
    └── wwwroot/
        └── index.html             ← SPA web dashboard (Tailwind CSS + SignalR + fetch API)
```

## Service details

### AppDiscoveryService
- `FindApps(string rootDir, int depth)` → `List<AppEntry>` — scans single root
- `FindApps(IEnumerable<string> rootDirs, int depth)` → `List<AppEntry>` — scans multiple roots
- `RefreshApp(string dir)` → `AppEntry?` — re-reads single app (versions + git fetch + status)

### VersionService
- `ReadiOS(dir)` / `WriteiOS(dir, ver, build)` — Info.plist
- `ReadAndroid(dir)` / `WriteAndroid(dir, ver, build)` — AndroidManifest.xml
- `ReadCsproj(path)` / `WriteCsproj(path, ver, build)` — .csproj XML
- `ReadAssemblyInfo(dir)` / `WriteAssemblyInfo(dir, ver, build)` — AssemblyInfo.cs
- `ReadUnity(dir)` / `WriteUnity(dir, ver, build)` — Unity ProjectSettings.asset
- `GetTargetFrameworks(csprojPath, platform?)` → `List<string>` — parses TargetFramework(s)
- `GetBuildConfigurations(csprojPath)` → `List<string>` — parses PropertyGroup conditions
- `IncrementVersion(version)` — static: bumps last semver segment

### GitService
- `GetStatus(dir)` → `GitStatus` — porcelain v2 + branch
- `FetchAndGetStatus(dir)` → `GitStatus` — git fetch then GetStatus
- `GetBranch(dir)` → `string`
- `Pull(dir)` → `(bool, string)`
- `Commit(dir, message)` → `(bool, string)` — git add -A + commit
- `Push(dir, message)` → `(bool, string)` — add + commit + push
- `PushOnly(dir)` → `(bool, string)` — git push only
- `GetUnpushedCommits(dir, max)` → `List<string>`
- `GetDiffStat(dir)` / `GetUnstagedDiffStat(dir)` → `string`
- `GetChangedFilesSummary(dir)` → `string` — staged + unstaged + untracked
- `SuggestCommitMessage(dir)` → `string` — heuristic: version bumps, file type patterns

### BuildService
- `Run(dir, args[], onLine, logFile?, onStart?)` → `int` exitCode

### DeviceService
- `GetiOSDevices(macHost, macUser)` → `List<iOSDevice>` — SSH remote, 3 parsers
- `GetiOSDevicesLocal()` → `List<iOSDevice>` — local xcrun
- `FindMacsOnNetwork()` → `List<string>` — arp -a
- `GetAndroidDevicesAndAvds()` → `(List<AndroidDevice>, List<string> avdNames, string? adbPath)`

### StateService
- `Load()` → `PersistentState`
- `Save(state)`
- `RecordUsage(state, appDir)`

### AiCommitService
- `DetectAvailable(git, dir)` → `List<Provider>` — checks PATH for claude/gemini, Ollama HTTP, Smart
- `Generate(provider, diffContext, git, dir)` → `string`
- Providers: `Claude`, `Gemini`, `Ollama`, `Smart`

### UpdateService (singleton)
- `Instance` — static singleton
- `StartCheck()` / `ForceCheck()` — background NuGet version fetch
- `GetLatestVersion()` → `string?`
- `GetManualUpdateCommand(latestVer)` → `string`
- `LaunchDeferredUpdate(latestVer, originalArgs, interactive)` — Windows: batch with PID wait + 3 retries; Unix: sync update + relaunch

### ProcessEnvironment
- `UseEnglishCliOutput(ProcessStartInfo)` — sets DOTNET_CLI_UI_LANGUAGE, LANG, LC_ALL, etc.

## Models

```csharp
AppEntry               — Name, Dir, Branch, Versions(AppVersions), Git(GitStatus), ProjectType, IconBase64
PlatformVersion        — Version(string), Build(string)
AppVersions            — iOS?, Android?, Csproj?, → Master, InSync(bool)
GitStatus              — Ahead(int), Behind(int), Dirty(bool), LastCommit(DateTimeOffset?)
PersistentState        — AppUsage, MacHost, MacUser, Verbosity, LastAction, LastVersion, ScanRootPath,
                         MonitoredPaths, UseLocalMac, AppBuildConfigs, CachedApps
VersionSnapshot        — AppDir, Version, Build
AppBuildConfig         — BuildConfiguration, iOSFramework, AndroidFramework, iOSDeviceId/Name/Type,
                         AndroidDeviceSerial/Name, CodesignKey
iOSDevice              — Name, Udid, Type
AndroidDevice          — Serial, Model, State
```

## Web Dashboard API Endpoints

All defined in `WebStartup.cs`. Server runs on `http://localhost:5123`.

### REST Endpoints

| Method | Path | Request | Response |
|--------|------|---------|----------|
| GET | `/api/paths` | — | `string[]` (monitored paths) |
| POST | `/api/paths` | `{ path }` | — |
| POST | `/api/paths/delete` | `{ path }` | — |
| GET | `/api/apps` | — | `AppEntry[]` (cached, triggers background scan) |
| POST | `/api/apps/version` | `{ dir, version, build }` | — (writes version to files) |
| POST | `/api/apps/git/pull` | `{ dir }` | `{ success, output }` |
| POST | `/api/apps/git/push` | `{ dir, message }` | `{ success, output }` |
| POST | `/api/apps/refresh` | `{ dir }` | `AppEntry` (refreshes single app) |
| POST | `/api/apps/bump-push` | `{ dir, version, build }` | `{ success, output, version, build }` |
| POST | `/api/apps/build` | `{ dir, platform, configuration }` | 202 Accepted (streams via SignalR) |
| POST | `/api/apps/build/cancel` | `{ dir }` | — |
| POST | `/api/apps/devices` | `{ dir, platform }` | `{ devices: DeviceItem[] }` |
| POST | `/api/apps/config` | `{ dir, platform }` | `{ configurations, frameworks }` |
| POST | `/api/apps/run` | `{ dir, platform, deviceId, deviceName, deviceType, configuration, framework }` | 202 Accepted (build + deploy pipeline, streams via SignalR) |
| POST | `/api/apps/open-folder` | `{ dir }` | — |
| POST | `/api/apps/open-ide` | `{ dir, ide }` | — |
| GET | `/api/update/check` | — | `{ currentVersion, latestVersion, updateAvailable, updateCommand }` |
| POST | `/api/update/install` | `{ version? }` | — |
| GET | `/api/diagnostics` | — | `{ dotnetVersion, os }` |

### SignalR Hub (`/hubs/logs`)

| Event | Payload | Description |
|-------|---------|-------------|
| `LogReceived` | `string` | Build output line (also step markers: `===STEP:BUILD===`, `===STEP:DEPLOY===`, `===STEP:DONE===`, `===STEP:FAILED===`) |
| `ScanCompleted` | `AppEntry[]` | Background scan finished |
| `AppUpdated` | `AppEntry` | Single app refreshed via version/git endpoint |

## All features implemented

**App Discovery**
- [x] Recursive `.csproj` scan (configurable depth)
- [x] Unity project support (ProjectSettings.asset)
- [x] Project type detection: MAUI, WPF, Blazor, ClassLibrary, Unity
- [x] App icon discovery (base64 data URI)
- [x] Usage-based sorting (most recently used first)

**Version Management**
- [x] Read: Info.plist (iOS), AndroidManifest.xml (Android), .csproj, AssemblyInfo.cs (WPF), ProjectSettings.asset (Unity)
- [x] Write to all 5 file types atomically
- [x] Increment version + build, increment build only, set manually
- [x] Sync iOS ↔ Android versions
- [x] Snapshot + undo of last version change

**iOS**
- [x] List devices via SSH to remote Mac (xctrace + xcdevice + instruments)
- [x] List devices via local xcrun (same 3 parsers)
- [x] Run on device/simulator
- [x] Archive with codesign key selection (Apple Dev / Distribution / custom)
- [x] Upload archive to App Store Connect
- [x] Open archive in Xcode
- [x] Per-app persisted: device, framework, codesign key

**Android**
- [x] List devices via `adb devices -l`
- [x] List AVDs via `emulator -list-avds`
- [x] Auto-start emulator with boot-progress wait
- [x] Build & Run (full deploy pipeline)
- [x] Quick Launch (adb shell monkey — skip build)
- [x] Publish Release (.apk / .aab)

**Git**
- [x] Git status (ahead/behind/dirty/last commit) via porcelain v2
- [x] Auto-fetch on app open
- [x] Pull warning if behind remote
- [x] Pull and push with formatted commit message
- [x] Push only (separate from commit)
- [x] AI commit messages (Claude / Gemini / Ollama / Smart)
- [x] View unpushed commits + diff stats before committing

**Build**
- [x] `dotnet build` with verbosity control (quiet/minimal/normal/detailed/diagnostic)
- [x] Build & Run combined pipeline (build → deploy on device)
- [x] Step markers for frontend progress tracking
- [x] Live output streaming via SignalR
- [x] Cancel running build
- [x] Clean: Quick / Android / iOS / Deep / Nuclear

**TUI**
- [x] App table with iOS/Android/branch/git columns, sorted by most recently used
- [x] Platform filter (All / iOS / Android)
- [x] Search/filter by app name
- [x] Interactive folder browser
- [x] Mac/SSH config with ARP network scan
- [x] Diagnostics screen (dotnet/git/ssh/xcrun/adb/emulator/workloads)
- [x] Auto-update check (NuGet version comparison)
- [x] Open in IDE (VS Code / Visual Studio / Rider — auto-detected)
- [x] Repeat last action

**Web Dashboard**
- [x] SPA with Tailwind CSS + SignalR
- [x] Card layout + list/table layout toggle
- [x] Search + tech/platform filter
- [x] Quick bump (+1 version/build)
- [x] Build menu (Build Only / Build & Run per platform)
- [x] Build & Run modal: device picker (Physical/Emulator/AVD/Simulator), config, framework
- [x] Progress modal: animated bar (Build → Deploy → Launch), live logs, timer
- [x] Version update modal with before/after confirmation
- [x] Bump & Push confirmation modal
- [x] Git pull, open folder, open IDE
- [x] Row overflow menu
- [x] Collapsible sidebar + terminal panel
- [x] Theme toggle (dark/light)
- [x] Toast notifications
- [x] Background scan with live indicator
- [x] Mobile responsive (sidebar → drawer)

**Distribution**
- [x] NuGet `dotnet tool` package
- [x] Self-contained exe via `install.ps1` (win-x64, single-file, desktop shortcut)
- [x] Self-contained via `install.sh` (macOS/Linux: dotnet tool install -g + profile setup)
- [x] Auto-publish to NuGet via GitHub Actions on `v*` tag
- [x] Auto-update check + deferred install

**Pending**
- (none — all planned features implemented)

## Design decisions

- **`SelectionPrompt`** for all TUI menus — no raw keyboard handling needed
- **`Process` directly** for git, dotnet, adb, ssh — no shell abstraction layer
- **Immutable records** for models — AppEntry is never mutated, UI re-reads from disk when needed
- **State in `~/.maui-forge.state.json`** — never inside the project folder
- **NuGet `dotnet tool`** as primary distribution — install scripts kept as fallback for dev/offline use
- **AppBuildConfig per app dir** in state — remembers last device, framework, codesign key per project
- **Web dashboard default** (`maui-forge` starts web, `--cli` for TUI) — richer visual surface
- **ASP.NET Core Minimal API** — no MVC/controllers, inline route handlers for simplicity
- **SignalR for build logs** — real-time streaming without polling
- **Single Kestrel process** — embeds the web server in the dotnet tool, no separate hosting
- **Step markers** (`===STEP:BUILD===`) sent as log lines — frontend parses them for progress bar without a separate event channel

## DI registration

### Program.cs (TUI path)
```csharp
services
  .AddSingleton<GitService>()
  .AddSingleton<VersionService>()
  .AddSingleton<BuildService>()
  .AddSingleton<DeviceService>()
  .AddSingleton<StateService>()
  .AddSingleton<AppDiscoveryService>()
  .AddSingleton<AiCommitService>()
  .AddSingleton<AppDetailScreen>()
```

### WebStartup.cs (Web path)
Pre-created singleton instances are passed to `WebStartup.Start()`, plus:
```csharp
builder.Services.AddSignalR().AddCors();
```

## How to run in development

```powershell
dotnet run --project src/MauiForge -- --path K:\your\projects --depth 2
```

Default mode starts the web dashboard at `http://localhost:5123`.

```powershell
dotnet run --project src/MauiForge -- --cli --path K:\your\projects --depth 2
```

Force TUI mode.

## How to install locally (dev build)

```powershell
# Windows
.\install.ps1

# macOS/Linux
./install.sh
```

## How to publish a new release

```bash
git tag v1.5.49
git push origin v1.5.49
# GitHub Actions runs dotnet pack + nuget push automatically
# Requires NUGET_API_KEY secret in repo settings
```

## Origin

Migrated from Gist `wagenheimer/6e27938c32dd667e2d8bf2e135dad2f1` (`maui-version.ps1`, ~2700 lines of PowerShell).

## Design Context

See `PRODUCT.md` (strategy) and `DESIGN.md` (visual system) at project root.

- **Register**: product / **Platform**: web (web dashboard is primary design surface)
- **Design**: Dark & Bold — near-black bg, royal blue primary, amber accents, OKLCH palette
- **Two surfaces share one design language**: Spectre.Console TUI (terminal, keyboard-driven) + web dashboard (Tailwind/HTML, richer visual)
- **Commands**: `/impeccable craft <feature>` to build, `/impeccable polish <target>` to refine, `/impeccable live` for in-browser iteration
