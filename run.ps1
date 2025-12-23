# Run script for Munin IRC Client
# Usage: .\run.ps1 [-Configuration <Debug|Release>] [-Build]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [switch]$Build
)

$exePath = "$PSScriptRoot\Munin.UI\bin\$Configuration\net8.0-windows\Munin.exe"

if ($Build) {
    Write-Host "Building before running..." -ForegroundColor Cyan
    & "$PSScriptRoot\build.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path $exePath)) {
    Write-Host "Executable not found. Building first..." -ForegroundColor Yellow
    & "$PSScriptRoot\build.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Write-Host "Starting Munin IRC Client..." -ForegroundColor Cyan
Start-Process $exePath
