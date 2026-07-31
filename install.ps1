#Requires -Version 5.1
<#
.SYNOPSIS
    Install maui-forge as a dotnet tool and create a Desktop shortcut.
    The shortcut always runs the latest version because it targets the
    dotnet tool shim (updated via dotnet tool update / auto-update).
#>

$ProjectPath = "$PSScriptRoot\src\MauiForge\MauiForge.csproj"
$OutDir      = "$PSScriptRoot\dist"
$IconDir     = "$env:LOCALAPPDATA\MauiForge"
$DotNetTools = "$env:USERPROFILE\.dotnet\tools"
$ShimExe     = "$DotNetTools\maui-forge.exe"

# ── 1. Install / update the dotnet tool globally ──────────────
Write-Host "Installing maui-forge as a global dotnet tool..." -ForegroundColor Cyan
$installed = & dotnet tool list -g 2>$null | Select-String "CwSoftware.MauiForge"
if ($installed) {
    Write-Host "  Already installed — updating to latest..." -ForegroundColor Gray
    & dotnet tool update CwSoftware.MauiForge -g 2>&1 | Out-Null
} else {
    & dotnet tool install CwSoftware.MauiForge -g 2>&1 | Out-Null
}

if (-not (Test-Path $ShimExe)) {
    Write-Host "  WARNING: dotnet tool shim not found at $ShimExe" -ForegroundColor Yellow
    Write-Host "  Falling back to self-contained build..." -ForegroundColor Yellow
}

# ── 2. Build self-contained exe (offline fallback) ────────────
Write-Host "Building self-contained executable (offline fallback)..." -ForegroundColor Cyan
dotnet publish $ProjectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $OutDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit 1
}

$exePath = "$OutDir\maui-forge.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "Executable not found at $exePath" -ForegroundColor Red
    exit 1
}

# Add dist/ to PATH (user scope, persistent)
$currentPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
if ($currentPath -notlike "*$OutDir*") {
    [Environment]::SetEnvironmentVariable('PATH', "$currentPath;$OutDir", 'User')
    Write-Host "Added $OutDir to PATH." -ForegroundColor Green
}

if ($env:PATH -notlike "*$OutDir*") {
    $env:PATH += ";$OutDir"
    Write-Host "Updated PATH in current session." -ForegroundColor Green
}

# ── 3. Copy icon to permanent location ────────────────────────
Write-Host "Setting up icon..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $IconDir | Out-Null
$iconSource = "$PSScriptRoot\assets\icon.ico"
$iconDest   = "$IconDir\icon.ico"
Copy-Item -Path $iconSource -Destination $iconDest -Force
Write-Host "  Icon copied to $iconDest" -ForegroundColor Green

# ── 4. Create Desktop Shortcut ────────────────────────────────
Write-Host "Creating Desktop Shortcut..." -ForegroundColor Cyan
try {
    $WshShell = New-Object -ComObject WScript.Shell
    $ShortcutPath = [System.IO.Path]::Combine([System.Environment]::GetFolderPath('Desktop'), "MAUI Forge.lnk")
    $Shortcut = $WshShell.CreateShortcut($ShortcutPath)

    # Prefer the dotnet tool shim — when dotnet tool update runs (manually
    # or via auto-update), the shim is replaced with the new version, so
    # the desktop shortcut always launches the latest build.
    if (Test-Path $ShimExe) {
        $Shortcut.TargetPath = $ShimExe
        Write-Host "  Shortcut targets dotnet tool shim (auto-updating)" -ForegroundColor Green
    } else {
        $Shortcut.TargetPath = $exePath
        Write-Host "  Shortcut targets self-contained exe (manual update)" -ForegroundColor Yellow
    }

    $Shortcut.Arguments = "--web"
    $Shortcut.WorkingDirectory = $DotNetTools
    $Shortcut.IconLocation = "$iconDest,0"
    $Shortcut.Save()
    Write-Host "  Shortcut created on Desktop: MAUI Forge" -ForegroundColor Green
} catch {
    Write-Host "  Failed to create Desktop shortcut: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "✓ MAUI Forge installed successfully!" -ForegroundColor Green
Write-Host "  Desktop shortcut ready — double-click to launch." -ForegroundColor Cyan
Write-Host "  The app auto-updates on every start." -ForegroundColor Cyan
Write-Host "  Run: maui-forge" -ForegroundColor Cyan
