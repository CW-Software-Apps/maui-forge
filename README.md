# MAUI Forge ⚡

![MAUI Forge Banner](assets/banner.png)

**The missing CLI for .NET MAUI developers.**

Managing version numbers across iOS, Android, and the `.csproj` is tedious — you have to edit `Info.plist`, `AndroidManifest.xml`, and the project file separately, make sure they're in sync, bump the build number, commit, and push. Every. Single. Release.

MAUI Forge automates all of that from a single interactive terminal UI. Point it at your projects folder, pick an app, and manage versions, run builds, deploy to devices, and handle git — without leaving the terminal or touching a single file manually.

![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![NuGet](https://img.shields.io/badge/nuget-v1.1.0-004880?logo=nuget)
![License](https://img.shields.io/github/license/CW-Software-Apps/maui-forge)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)

---

## What it does

- **Scans a folder** and lists all your .NET MAUI apps at a glance — with their iOS version, Android version, git branch, and git status in a single table
- **Reads and writes versions** across all three files (`Info.plist`, `AndroidManifest.xml`, `.csproj`) in one action — no manual editing, no out-of-sync risk
- **Increments version or build** with one keypress, or sets them manually
- **Syncs iOS ↔ Android** when they drift apart
- **Runs on iOS devices and simulators** — lists connected devices via SSH (remote Mac) or `xcrun` (local Mac), lets you pick one and fires `dotnet build -t:Run`
- **Archives iOS for release** — configures Release build, codesign key, and runs `dotnet publish` with `ArchiveOnBuild=true`
- **Runs on Android devices and emulators** — lists connected devices via `adb`, picks one and runs `dotnet build -t:Run`
- **Publishes Android** — builds an `.apk` / `.aab` in Release mode
- **Git pull/push** — pulls before showing actions if you're behind, pushes with a formatted commit message after version bumps
- **Undo** — snapshots the version before any change so you can roll back instantly
- **Repeat last action** — re-runs the same build or deploy without navigating the menu again
- **Diagnostics** — shows `dotnet`, workloads, `adb`, `ssh`, `xcrun`, and `git` status so you know what's missing before a build fails

If you manage more than one MAUI app or release often, this replaces a lot of manual steps.

---

## Install

Requires the [.NET SDK](https://dotnet.microsoft.com/download) — which every MAUI developer already has.

```bash
dotnet tool install -g maui-forge
```

```bash
# Update
dotnet tool update -g maui-forge

# Uninstall
dotnet tool uninstall -g maui-forge
```

---

## Quick start

```bash
# Run in the current directory
maui-forge

# Point to your projects folder
maui-forge --path ~/projects

# Increase search depth (default is 2)
maui-forge --path ~/projects --depth 3
```

The chosen path is remembered between runs (`~/.maui-forge.state.json`), so after the first time you just run `maui-forge`.

---

## The app list

```
  MAUIForge
  >>> by CW Software  |  .NET MAUI Version & Build Manager

  scan: /Users/me/projects  |  mac: 192.168.1.50

  12 apps  ·  8 iOS  ·  10 Android  ·  2 dirty  ·  1 out of sync

                        iOS               Android           Branch        Git
  ── Apps (12) ──────────────────────────────────────────────────────────────
  + MyApp               1.4.2 #18         1.4.2 #18         main          clean
  * ShippingApp         2.0.0 #103        2.0.0 #103        release/2.0   +2
  * OtherApp            1.0.0 #5        ! 1.0.1 #6          feature/x     ~
  ! LegacyApp           2.1.0 #40         2.1.0 #40         main          -3
  ── Options ─────────────────────────────────────────────────────────────────
  >  Change folder
  >  Platform: All  → iOS
  >  Search: all
  >  Mac / SSH config
  >  Diagnostics
  >  Quit
```

| Indicator | Meaning |
|-----------|---------|
| `+` green dot | Git clean, up to date |
| `*` yellow dot | Working tree dirty or commits ahead of remote |
| `!` red dot | Behind remote — pull recommended |
| `!` yellow between version columns | iOS and Android versions are out of sync |
| **Bold name** | Used in the last 24 hours |

Apps are sorted by most recently used, so your active projects are always at the top.

---

## App detail

Selecting an app opens the detail panel with all actions available:

```
╭─ >>> ShippingApp ─────────────────────────────────────────────────╮
│ (iOS)     2.0.0  build #103                                       │
│ (Android) 2.0.0  build #103                                       │
│   (ok) iOS and Android in sync                                    │
│                                                                   │
│   branch    release/2.0   ✓  ^2 to push                          │
│                                                                   │
│   config    Release   ios fw  net9.0-ios   droid fw  net9.0-and.. │
│   ios dev   device configured   android dev  R5CX208XXXX          │
│   mac       192.168.1.50                                          │
╰───────────────────────────────────────────────────────────────────╯

What would you like to do?
-- Version ──────────────────────────────────────────────────────────
  v+   Increment Version + Build    2.0.0 -> 2.0.1  #103 -> #104
  b+   Increment Build only         #103 -> #104
  ~~   Set version manually
  <>   Sync iOS -- Android          (ok) in sync
-- iOS ──────────────────────────────────────────────────────────────
  [#]  Archive iOS (Release)        net9.0-ios  Apple Distribution
  [>]  Run iOS Device               device ok
-- Android ──────────────────────────────────────────────────────────
  [>]  Run Android Device           R5CX208XXXX
  [#]  Publish Android (Release)    net9.0-android
-- Git & Build ──────────────────────────────────────────────────────
  git  Git Pull                     pending
  clr  Clean Project
────────────────────────────────────────────────────────────────────
  <<   Undo last version change     → 1.9.9 #102
  >>|  Repeat last action           Run iOS Device
  ~~~  Build verbosity:             quiet
   x   Back
```

### Version management

**`v+` — Increment Version + Build**
Bumps the patch number and the build counter across all three files at once (`Info.plist`, `AndroidManifest.xml`, `.csproj`). Shows a before/after preview before confirming. Optionally commits and pushes with a formatted message like `chore: bump version to 2.0.1 #104 (ShippingApp)`.

**`b+` — Increment Build only**
Same as above but keeps the version string unchanged. Useful when the public version doesn't change but you need a new build for TestFlight or Play Store.

**`~~` — Set manually**
Prompts for version and build number. Useful for major bumps or fixing an incorrect value.

**`<>` — Sync iOS ↔ Android**
When the two platforms have drifted apart (e.g. Android is `1.0.1 #6` and iOS is still `1.0.0 #5`), this lets you pick which one to use as the source and copies it to the other.

**`<<` — Undo**
Before applying any version change, MAUI Forge saves a snapshot. Undo restores it instantly — no git revert needed.

### iOS builds

Requires a Mac with Xcode and the `.NET MAUI` workload installed. Works both with a **remote Mac via SSH** and a **local Mac** if you run maui-forge directly on macOS.

**`[#]` — Archive iOS (Release)**
Prompts for build configuration and codesign key, then runs:
```
dotnet publish -c Release -f net9.0-ios -p:ArchiveOnBuild=true -p:CodesignKey=...
```
Output goes to `bin/Release/archive/` in the project folder.

**`[>]` — Run iOS Device**
Lists all connected physical devices and simulators (via `xcrun xctrace list devices`), lets you pick one, and runs:
```
dotnet build -t:Run -f net9.0-ios -p:_DeviceId=<udid>
```
The selected device is remembered for the next run.

### Android builds

Requires `adb` in PATH (comes with Android SDK / Android Studio).

**`[>]` — Run Android Device**
Lists connected devices and emulators (via `adb devices -l`), lets you pick one, and runs:
```
dotnet build -t:Run -f net9.0-android -p:AdbArguments=-s <serial>
```

**`[#]` — Publish Android (Release)**
Runs `dotnet publish -c Release` and prints the paths of any `.apk` and `.aab` files generated.

### Git

**`git` — Git Pull**
Runs `git pull` and refreshes the git status shown in the panel.

If you are behind the remote when you open an app, MAUI Forge warns you and offers to pull before showing the action menu — so you don't build on stale code.

### Repeat and verbosity

**`>>|` — Repeat last action**
Re-runs the last build or deploy without going through the menus. Useful when iterating on a fix and deploying repeatedly.

**`~~~` — Build verbosity**
Controls the `-v` flag passed to every `dotnet` command. Default is `quiet` (shows only errors and warnings). Switch to `normal` or `detailed` when debugging a build failure.

---

## Mac / iOS setup

### Remote Mac (SSH)

From the main menu → **Mac / SSH config**:
1. Enter the Mac **Host** (IP address or hostname) and **User**
2. Optionally use **Scan network** to discover hosts from the local ARP table instead of typing the IP

SSH key-based authentication is recommended (no password prompt during builds). The Mac must have **Xcode** and the **.NET MAUI workload** installed:
```bash
sudo xcode-select --install
dotnet workload install maui-ios
```

### Local Mac

If you are running `maui-forge` directly on a Mac:

1. Main menu → **Mac / SSH config**
2. Select **yes** to "Use local Mac (no SSH)?"

Device listing uses `xcrun` locally and builds skip the `ServerAddress`/`ServerUser` MSBuild properties.

---

## Diagnostics

Main menu → **Diagnostics** shows a status table for every tool MAUI Forge depends on:

| Component | Checked via |
|-----------|-------------|
| `dotnet` | `dotnet --version` |
| Workloads | `dotnet workload list` |
| `adb` | `adb version` |
| `ssh` | `ssh -V` |
| `xcrun` | `xcrun --version` |
| `git` | `git --version` |

Use this first when something isn't working — it tells you immediately if `adb` is missing from PATH or the `maui-ios` workload isn't installed.

---

## Persistent state

Everything is saved at `~/.maui-forge.state.json` — no config files in your project folders.

| Field | Description |
|-------|-------------|
| `ScanRootPath` | Last root folder scanned |
| `MacHost` / `MacUser` | Remote Mac SSH credentials |
| `UseLocalMac` | Skip SSH and use local `xcrun` |
| `Verbosity` | Build log level for all `dotnet` commands |
| `LastAction` | Last action (used by Repeat) |
| `LastVersion` | Version snapshot (used by Undo) |
| `AppUsage` | Last-used timestamp per app — drives sort order |
| `AppBuildConfigs` | Per-app settings: framework, device ID, codesign key |

---

## Manual install (without NuGet)

<details>
<summary>Windows — self-contained exe (no .NET runtime required)</summary>

```powershell
git clone https://github.com/CW-Software-Apps/maui-forge.git
cd maui-forge
.\install.ps1
```

Publishes `dist\maui-forge.exe` as a self-contained `win-x64` binary and adds `dist\` to the user PATH.

To update:
```powershell
git pull && .\install.ps1
```

</details>

<details>
<summary>macOS — self-contained binary (no .NET runtime required)</summary>

```bash
git clone https://github.com/CW-Software-Apps/maui-forge.git
cd maui-forge
./install.sh
```

Publishes as `osx-arm64` (Apple Silicon) or `osx-x64` (Intel) and symlinks to `~/.local/bin/maui-forge`.

To update:
```bash
git pull && ./install.sh
```

</details>

---

## Publishing a new release

Tags trigger automatic publishing to NuGet.org via GitHub Actions:

```bash
git tag v1.2.0
git push origin v1.2.0
```

The workflow runs `dotnet pack -p:Version=1.2.0` and pushes to NuGet. Requires a `NUGET_API_KEY` secret in the repository settings (`Settings → Secrets → Actions`).

---

## Stack

- **C# .NET 10**, console app
- **[Spectre.Console](https://spectreconsole.net/)** — all UI (tables, prompts, panels, progress bars, colors)
- **Microsoft.Extensions.DependencyInjection** — service composition
- Distributed as a `dotnet tool` — single command install, works on Windows, macOS, and Linux

---

## License

[MIT](LICENSE) © CW Software
