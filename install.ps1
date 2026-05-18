#Requires -Version 7
<#
.SYNOPSIS
    Build maui-forge as a self-contained Windows exe and add it to PATH.
#>

$ProjectPath = "$PSScriptRoot\src\MauiForge\MauiForge.csproj"
$OutDir      = "$PSScriptRoot\dist"

Write-Host "Building maui-forge..." -ForegroundColor Cyan
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

Write-Host "Run: maui-forge" -ForegroundColor Cyan
