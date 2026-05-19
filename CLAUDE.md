# MAUI Forge — CLAUDE.md

## What this project is

A cross-platform CLI tool written in **C# .NET 10** that replaces the PowerShell script `maui-version.ps1`. It manages versions, builds, and deployments for .NET MAUI apps via an interactive terminal UI.

Repository: `https://github.com/CW-Software-Apps/maui-forge`  
Distribution: `dotnet tool install -g CwSoftware.MauiForge` (NuGet) or self-contained exe via install scripts.

## Stack

- **C# .NET 10**, console app, no web framework
- **Spectre.Console** — all UI (tables, prompts, panels, progress, colors)
- **Spectre.Console.Cli** — CLI arg parsing (`--depth`, `--path`)
- **Microsoft.Extensions.DependencyInjection** — DI between services
- **Distribution**: NuGet (`dotnet tool`) as primary; `install.ps1` / `install.sh` as self-contained fallback

## Project structure

```
maui-forge/
├── assets/
│   ├── banner.png                 ← GitHub repo banner
│   └── icon.png                   ← NuGet package icon (256×256)
├── .github/workflows/
│   └── publish.yml                ← Auto-publish to NuGet on v* tag push
├── install.ps1                    ← Windows: self-contained exe → dist/ → PATH
├── install.sh                     ← macOS/Linux: self-contained binary → ~/.local/bin
└── src/MauiForge/
    ├── Program.cs                 ← Entry point, DI setup, main loop, result routing
    ├── Models/
    │   ├── AppEntry.cs            ← record: Name, Dir, Branch, Versions, Git
    │   ├── AppVersions.cs         ← record: iOS?, Android?, Csproj? + InSync + Master
    │   ├── GitStatus.cs           ← record: Ahead, Behind, Dirty
    │   └── PersistentState.cs     ← All persisted state + AppBuildConfig per app
    ├── Services/
    │   ├── AppDiscoveryService.cs ← Scans dirs for *.csproj, builds AppEntry[]
    │   ├── VersionService.cs      ← Read/write Info.plist, AndroidManifest.xml, .csproj
    │   ├── GitService.cs          ← status, fetch, pull, push via Process
    │   ├── BuildService.cs        ← dotnet with async line streaming
    │   ├── DeviceService.cs       ← iOS devices (SSH or local xcrun), Android (adb), ARP scan
    │   └── StateService.cs        ← ~/.maui-forge.state.json
    └── UI/
        ├── AppListScreen.cs       ← App table, platform filter, search, folder browser
        ├── AppDetailScreen.cs     ← Detail panel, all action handlers
        └── DiagnosticsScreen.cs   ← dotnet/adb/ssh/xcrun/git status table
```

## All features implemented

**Version management**
- [x] App discovery by `.csproj` in subfolders (configurable depth)
- [x] Read version: `Info.plist` (iOS), `AndroidManifest.xml` (Android), `.csproj`
- [x] Write version to all 3 files atomically
- [x] Increment version + build, increment build only, set manually
- [x] Sync iOS ↔ Android versions
- [x] Snapshot/undo of last version change

**iOS**
- [x] Archive iOS Release (`dotnet publish -p:ArchiveOnBuild=true`)
- [x] Run on device/simulator via SSH to remote Mac
- [x] Run on device/simulator local Mac (no SSH)
- [x] List devices via `xcrun xctrace list devices` (SSH or local)
- [x] Select physical device or simulator before run
- [x] Codesign key selection (Apple Development / Apple Distribution / custom)

**Android**
- [x] Run on device/emulator via `adb`
- [x] List devices via `adb devices -l` (physical + emulators)
- [x] Publish Android Release (outputs `.apk` / `.aab`)

**Git**
- [x] Git status (ahead/behind/dirty) per app
- [x] Auto-fetch on app open
- [x] Pull warning if behind remote
- [x] Pull and push with formatted commit message

**Build**
- [x] `dotnet clean`
- [x] Build verbosity: quiet / minimal / normal / detailed / diagnostic
- [x] Repeat last action (persisted in state)

**UI**
- [x] App list with iOS/Android/branch/git columns, sorted by most recently used
- [x] Platform filter (All / iOS / Android)
- [x] Search/filter by app name
- [x] Interactive folder browser
- [x] Mac/SSH config with ARP network scan
- [x] Diagnostics screen

**Distribution**
- [x] NuGet `dotnet tool` package (primary)
- [x] Self-contained exe via `install.ps1` (Windows)
- [x] Self-contained binary via `install.sh` (macOS arm64/x64)
- [x] Auto-publish to NuGet via GitHub Actions on `v*` tag

**Pending**
- [ ] Auto-update check against NuGet (compare installed vs latest version)

## Design decisions

- **`SelectionPrompt`** for all menus — no raw keyboard handling needed
- **`Process` directly** for git, dotnet, adb, ssh — no shell abstraction layer
- **Immutable records** for models — AppEntry is never mutated, UI re-reads from disk when needed
- **State in `~/.maui-forge.state.json`** — never inside the project folder
- **NuGet `dotnet tool`** as primary distribution — install scripts kept as fallback for dev/offline use
- **AppBuildConfig per app dir** in state — remembers last device, framework, codesign key per project

## How to run in development

```powershell
dotnet run --project src/MauiForge -- --path K:\your\projects --depth 2
```

## How to install locally (dev build)

```powershell
# Windows
.\install.ps1

# macOS/Linux
./install.sh
```

## How to publish a new release

```bash
git tag v1.4.0
git push origin v1.4.0
# GitHub Actions runs dotnet pack + nuget push automatically
# Requires NUGET_API_KEY secret in repo settings
```

## Origin

Migrated from Gist `wagenheimer/6e27938c32dd667e2d8bf2e135dad2f1` (`maui-version.ps1`, ~2700 lines of PowerShell).
