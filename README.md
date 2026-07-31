<div align="center">

![MAUI Forge Banner](https://raw.githubusercontent.com/CW-Software-Apps/maui-forge/master/assets/banner.png)

# ⚡ MAUI Forge

**Your MAUI release copilot.**

*One dashboard. Every app. Every device. Every release.*

[![NuGet](https://img.shields.io/nuget/v/CwSoftware.MauiForge?style=for-the-badge&logo=nuget&color=004880&label=nuget)](https://www.nuget.org/packages/CwSoftware.MauiForge)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CwSoftware.MauiForge?style=for-the-badge&logo=nuget&color=004880)](https://www.nuget.org/packages/CwSoftware.MauiForge)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/github/license/CW-Software-Apps/maui-forge?style=for-the-badge)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey?style=for-the-badge)](https://github.com/CW-Software-Apps/maui-forge)

</div>

---

## Install

One command, identical on Windows, macOS, and Linux — requires the [.NET SDK](https://dotnet.microsoft.com/download), which every MAUI developer already has:

```bash
dotnet tool install -g CwSoftware.MauiForge
maui-forge
```

That's it. On first run, MAUI Forge sets itself up — a Desktop shortcut on Windows, a Desktop launcher on macOS, an app-menu entry on Linux — and auto-updates on every start. No install scripts, no manual PATH edits, no platform-specific steps.

```bash
dotnet tool update -g CwSoftware.MauiForge    # update manually anytime
dotnet tool uninstall -g CwSoftware.MauiForge # remove
```

Prefer a copy-paste page with a live "is it running?" check? → **[cw-software-apps.github.io/maui-forge](https://cw-software-apps.github.io/maui-forge/)**

---

## The problem

You have multiple .NET MAUI apps. To release even one of them, you have to:

- Edit `Info.plist` (iOS version + build)
- Edit `AndroidManifest.xml` (Android version + build)
- Edit `.csproj` (ApplicationVersion / ApplicationDisplayVersion)
- Make sure all three match
- Open Xcode or Android Studio to pick a device
- Remember the right `dotnet publish` flags for archive, codesign, and framework
- Repeat for every app, every time

**MAUI Forge replaces all of that** — open the dashboard, pick an app, pick an action, done.

---

## 🌐 The Dashboard

`maui-forge` opens a **web dashboard** — the primary, default way to use MAUI Forge, on every platform. It runs locally at `http://localhost:6284`, no cloud, no account, and it's the single control panel everywhere: no separate tray/menu-bar app to install or keep in sync.

### Card view

![Card view](https://raw.githubusercontent.com/CW-Software-Apps/maui-forge/master/assets/screenshots/dashboard-cards.webp)

Every app in your monitored folders shows up as a card:

- **Project type badge** — MAUI, WPF, Blazor, Unity, ClassLibrary
- **iOS 📱 and Android 🤖 version + build**, side by side, flagged when they're out of sync
- **Git branch** and status (clean / dirty / ahead-behind), color-coded
- **`+1` quick bump**, **🚀 Build & Run**, and a **⋮ overflow menu** (update version, pull, open folder, open in IDE) right on the card
- Toggle to a **compact table (List view)** instead, with the same actions per row

![List view](https://raw.githubusercontent.com/CW-Software-Apps/maui-forge/master/assets/screenshots/dashboard-list.webp)

### Features

| Feature | Description |
|---------|-------------|
| Card / List layout | Toggle between visual cards and compact table |
| Search + tech filter | Filter by app name or platform (MAUI / iOS / Android / All) |
| Quick bump | `+1` bumps version and build, shows before/after confirmation |
| Build menu | Build iOS, Build Android, Build & Run iOS, Build & Run Android |
| Row overflow menu | Update version, pull git, open folder, open in IDE |
| Build & Run modal | Device picker (grouped: Physical / Emulator / Simulator), config selector, framework picker — all with last-used defaults |
| Progress modal | Animated progress bar (Build → Deploy → Launch) with live logs and timer |
| Real-time logs | SignalR streaming build output to terminal panel |
| Sidebar | Monitored paths, theme toggle, diagnostics, update checker |
| Auto-Start toggle | Enable/disable launch on login — works the same on Windows, macOS, and Linux |
| Mobile responsive | Sidebar collapses to drawer on small screens |

![Build menu](https://raw.githubusercontent.com/CW-Software-Apps/maui-forge/master/assets/screenshots/build-menu.webp)

![Build & Run modal](https://raw.githubusercontent.com/CW-Software-Apps/maui-forge/master/assets/screenshots/build-run-modal.webp)

Start it: `maui-forge` (default).

---

## 🍎 The Mac + VS Code story

If you develop MAUI apps on a Mac using **VS Code**, you know the pain: there's no built-in Run/Archive launcher for iOS. Every time you want to:

- Run on a physical device → manually pick the UDID, type the `dotnet build -t:Run` flags
- Switch simulator → edit the launch config or find the right UDID from `xcrun`
- Archive for App Store → remember the right `-p:ArchiveOnBuild=true -p:CodesignKey=...` flags and configuration

VS Code doesn't have individual per-project launch configurations for each device/simulator out of the box, so you end up with a terminal full of copy-pasted commands.

**MAUI Forge solves this entirely:** the device picker in the **Build & Run** modal calls `xcrun xctrace list devices` for you and lists every connected physical device and every simulator — iPhone 16 Pro (simulator), Cezar's iPhone (physical, connected), iPad Air 3rd gen (old device, iOS 16), all in one dropdown. Pick one and it fires the right `dotnet build -t:Run -p:_DeviceId=<udid>` — no config files, no remembered UDIDs, no launch.json setup. The selected device is remembered per project for next time.

Same for **Archive**: pick your codesign key from a list (Apple Development / Apple Distribution / custom), choose the framework, and MAUI Forge assembles the full `dotnet publish` command — correctly, every time.

**You never need to create individual VS Code launch configurations per device or per app again.**

### 📱 Old iPads and legacy devices VS Code can't see

VS Code's MAUI extension only lists devices running the **latest iOS version**. If you have an iPad Air 2, iPad mini 4, or any device stuck on iOS 15/16 because Apple dropped support — VS Code simply doesn't show them.

MAUI Forge reads the full `xcrun xctrace list devices` output, which includes **every connected device regardless of iOS version**. If it's plugged in and trusted, it shows up. You can run and debug your app on an old iPad that VS Code has completely forgotten about — which is invaluable for testing on lower-end hardware or older OS versions your users still run.

---

## ✨ What it does

<table>
<tr>
<td width="50%">

### 📋 App discovery
- Scans folders for all your MAUI, WPF, Blazor, Unity, and ClassLibrary projects
- Shows iOS version, Android version, git branch, and git status in one table
- Sorted by most recently used — active projects always at top
- Filter by platform (iOS / Android / All) or search by name
- **Full project type detection** — MAUI, WPF, Blazor, Unity, ClassLibrary

</td>
<td width="50%">

### 🔢 Version management
- Reads and writes all three files atomically (`Info.plist`, `AndroidManifest.xml`, `.csproj`)
- Also supports Unity (`ProjectSettings.asset`) and legacy WPF (`AssemblyInfo.cs`)
- Increment version + build, build only, or set manually
- Sync iOS ↔ Android when they drift apart
- Snapshot + undo before every change — instant rollback

</td>
</tr>
<tr>
<td>

### 🍎 iOS
- List all physical devices and simulators (SSH to remote Mac or local `xcrun`)
- **Three parsers**: `xctrace`, `xcdevice`, `instruments` — maximum device coverage
- Pick a device from a menu — no UDID hunting
- Run on device or simulator with one keypress
- Archive for App Store with codesign key selection
- **Upload to App Store Connect** + **Open archive in Xcode**
- Remembered per project: device, framework, codesign key

</td>
<td>

### 🤖 Android
- List all connected devices, emulators, and AVDs via `adb`
- **Auto-start emulators** with boot-progress wait (no more waiting blindly)
- Pick from a menu and run — no serial number copy-paste
- **Quick Launch** via `adb shell monkey` for fast re-deploys
- **Build & Run** — combined build + deploy pipeline
- Publish Release `.apk` / `.aab` with one action
- Works from Windows, macOS, or Linux

</td>
</tr>
<tr>
<td>

### 🌿 Git
- Live git status (ahead / behind / dirty / last commit time) per app
- Auto-fetch when you open an app
- Warning + pull prompt if you're behind the remote
- **Create commits** with AI-generated messages (Claude, Gemini, or local Ollama)
- **Push with formatted commit message** after version bumps
- **Push only** — commit separately, push when ready
- View unpushed commits before pushing

</td>
<td>

### 🤖 AI commit messages
MAUI Forge can **write your commit messages** using:
- **Claude CLI** (`claude` in PATH)
- **Gemini CLI** (`gemini` in PATH)
- **Ollama** (local — serves `llama3.2` via HTTP)
- **Smart suggestion** — heuristic fallback (detects version bumps, C# changes, XAML edits)

No API keys, no cloud service. Just diff → message.

</td>
</tr>
<tr>
<td>

### 🧹 Clean modes
Four levels of clean:
- **Quick** — `dotnet clean`
- **Android / iOS** — clean + delete platform bin/obj
- **Deep** — delete `bin/`, `obj/`, `.vs/`, `artifacts/`, `TestResults/`
- **Nuclear** — deep + `dotnet clean --verbosity diag`

</td>
<td>

### 🔁 Productivity
- **Repeat last action** — re-runs the last build/deploy without navigating
- **Build verbosity** — quiet / minimal / normal / detailed / diagnostic
- **Build & Run** — single action to build and deploy on device
- **Open in IDE** — opens project in VS Code, Visual Studio, or Rider (auto-detected)
- **Diagnostics screen** — checks every dependency before a build fails
- **Auto-update** — checks on every start, plus a periodic re-check every 4h while running in the background

</td>
</tr>
<tr>
<td colspan="2">

### 🌐 Remote access

![Remote access](https://raw.githubusercontent.com/CW-Software-Apps/maui-forge/master/assets/screenshots/remote-access.webp)

Run MAUI Forge on one machine and drive it from another — no VPN, no port-forwarding setup:
- **Enable Server Mode** — binds to `0.0.0.0` with a generated (or custom) access token; other devices on the network connect over `http://<host-ip>:6284`
- **Auto-discovery** — a UDP responder lets other MAUI Forge instances on the LAN find and connect to it automatically
- **Connect to Remote** — pick a discovered server from a list, or type a host:port + token manually
- Great for driving iOS builds on a **remote Mac** from a Windows PC dashboard, or checking on a build from your phone

</td>
</tr>
</table>

---

## Quick start

```bash
maui-forge                              # starts web dashboard on localhost:6284
maui-forge --cli                        # starts terminal TUI (traditional)
maui-forge --path ~/projects            # point to your projects folder
maui-forge --path ~/projects --depth 3  # deeper scan (default depth: 2)
maui-forge --update                     # force update check + install
maui-forge autostart [status|install|uninstall]  # manage login auto-start (Windows/macOS/Linux)
maui-forge --help                       # show help
```

The path and all settings are remembered in `~/.maui-forge.state.json` — after the first run, just type `maui-forge`.

---

## Version management

| Action | What it does |
|--------|-------------|
| **Increment Version + Build** | Bumps the patch number and build counter in all three files. Shows before/after preview. Optionally commits and pushes with a formatted message like `chore: bump version to 2.0.1 #104 (ShippingApp)`. |
| **Increment Build only** | Keeps the version string, bumps only the build number. Useful for TestFlight or Play Store re-submissions. |
| **Set manually** | Prompts for version and build number. For major bumps or fixing incorrect values. |
| **Sync iOS ↔ Android** | When platforms drift apart, pick which one to use as the source and copy to the other. |
| **Undo** | Restores the version snapshot taken before the last change — no `git revert` needed. |

---

## iOS builds

Works with a **remote Mac via SSH** or a **local Mac** running maui-forge directly.

**Create iOS Archive** — Prompts for codesign key, then runs:
```
dotnet publish -c Release -f net10.0-ios -p:ArchiveOnBuild=true -p:CodesignKey="Apple Distribution: ..."
```

After archiving: shows `.xcarchive` / `.ipa` path, **offers to upload to App Store Connect** and **open the archive in Xcode**.

**Run on iOS** — Lists connected devices and simulators (using all three parsers: `xctrace`, `xcdevice`, `instruments`), lets you pick, and runs:
```
dotnet build -t:Run -f net10.0-ios -p:_DeviceId=<udid>
```

The selected device, framework, and codesign key are saved per project.

---

## Android builds

Requires `adb` in PATH (comes with Android SDK / Android Studio).

**Run on Android** — Lists devices, emulators, and AVDs via `adb devices -l` + `emulator -list-avds`:

**Build & Run** (full pipeline):
```
dotnet build -t:Run -f net10.0-android -p:AdbArguments="-s <serial>"
```

**Quick Launch** (skips build, uses `adb shell monkey` for fast re-deploy):
```
adb -s <serial> shell monkey -p <package> 1
```

If the selected device is an AVD that isn't running, MAUI Forge **auto-starts the emulator** and waits for it to boot before deploying.

**Create Android Release** — Runs `dotnet publish -c Release` and prints `.apk` / `.aab` paths.

---

## AI commit messages

Open an app and select **Create Commit** in the dashboard. You'll see a diff summary and a source picker — **Smart Suggestion** (heuristic, always available), **Claude CLI**, **Gemini CLI**, **Ollama** (local), or **Write manually** — then a generated message like:

```
feat: add user profile screen
- added ProfilePage.xaml + ProfilePage.xaml.cs
- updated navigation service registration
- added profile data models
```

The providers auto-detect — only show up if the CLI tool is in PATH or Ollama is running.

---

## Clean modes

| Mode | What it deletes |
|------|----------------|
| **Quick** | `dotnet clean` |
| **Android** | Quick + `bin/` + `obj/` under Android-specific folders |
| **iOS** | Quick + `bin/` + `obj/` under iOS-specific folders |
| **Deep** | `bin/`, `obj/`, `.vs/`, `artifacts/`, `TestResults/` — full project reset |
| **Nuclear** | Deep + `dotnet clean --verbosity diag` — maximum verbosity for debugging |

---

## Mac / iOS setup

### Remote Mac (SSH)

Configure once via `maui-forge --cli` → **Mac / SSH config** (not yet in the web dashboard):
1. Enter the Mac **Host** (IP or hostname) and **User** — or use **Scan network** to discover from ARP
2. SSH key auth is recommended (no password prompts during builds)

The Mac must have Xcode and the MAUI workload:
```bash
sudo xcode-select --install
dotnet workload install maui-ios
```

### Local Mac

Running `maui-forge` directly on a Mac uses local `xcrun` automatically — no SSH or `ServerAddress`/`ServerUser` MSBuild properties needed.

---

## Diagnostics

Click **Diagnostics** in the dashboard sidebar to check every tool MAUI Forge depends on:

| Component | How it's checked |
|-----------|-----------------|
| `dotnet` | `dotnet --version` |
| Workloads | `dotnet workload list` |
| `adb` | `adb version` (auto-discovers PATH, ANDROID_HOME, VS, Android Studio) |
| `emulator` | `emulator -version` (auto-discovers same paths) |
| `ssh` | `ssh -V` |
| `xcrun` | `xcrun --version` |
| `git` | `git --version` |

Run this first when something isn't working — it immediately shows if `adb` is missing or `maui-ios` workload isn't installed.

---

## CLI arguments

| Argument | Effect |
|----------|--------|
| *(none)* | Starts web dashboard on `http://localhost:6284` |
| `--cli` or `--terminal` | Starts traditional terminal TUI |
| `--port <n>` | Overrides the default port (6284) |
| `--update` | Forces NuGet update check; installs if newer available |
| `--path <dir>` | Sets scan root directory |
| `--depth <n>` | Sets scan depth (default: 2) |
| `autostart [status\|install\|uninstall]` | Manages login auto-start (Windows/macOS/Linux) |
| `--help` / `-h` / `/?` | Show help |

---

## Persistent state

All settings live in `~/.maui-forge.state.json` — nothing inside your project folders.

| Field | Description |
|-------|-------------|
| `ScanRootPath` | Last root folder scanned |
| `MonitoredPaths` | Multiple monitored directories (web dashboard) |
| `MacHost` / `MacUser` | Remote Mac SSH credentials |
| `UseLocalMac` | Skip SSH and use local `xcrun` |
| `Verbosity` | Build log level for all `dotnet` commands |
| `LastAction` | Last action per app (used by Repeat) |
| `LastVersion` | Version snapshot per app (used by Undo) |
| `AppUsage` | Last-used timestamp per app — drives sort order |
| `AppBuildConfigs` | Per-app: framework, device IDs, codesign key, build config |
| `CachedApps` | Cached app list for instant web dashboard load |

---

## Publishing a new release

Tags trigger automatic publishing to NuGet.org via GitHub Actions:

```bash
git tag v1.7.0
git push origin v1.7.0
```

The workflow runs `dotnet pack` and pushes to NuGet. Requires a `NUGET_API_KEY` secret in repo settings.

---

## Stack

- **C# .NET 10**, console app + ASP.NET Core Minimal API
- **[Spectre.Console](https://spectreconsole.net/)** — TUI (tables, prompts, panels, progress bars, colors)
- **Spectre.Console.Cli** — CLI arg parsing
- **ASP.NET Core + SignalR** — web dashboard with real-time logs
- **Tailwind CSS** — web dashboard styling
- **Microsoft.Extensions.DependencyInjection** — service composition
- Distributed as a single `dotnet tool` — no self-contained builds, no install scripts, no OS-specific tray app to keep in sync. Same install command and the same web dashboard everywhere.

---

<div align="center">

[MIT](LICENSE) © CW Software &nbsp;·&nbsp; [NuGet](https://www.nuget.org/packages/CwSoftware.MauiForge) &nbsp;·&nbsp; [Issues](https://github.com/CW-Software-Apps/maui-forge/issues)

</div>
