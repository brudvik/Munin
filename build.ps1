# Build script for Munin IRC Client
# Usage: .\build.ps1 [-Configuration <Debug|Release>] [-Project <All|Munin|Relay>]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [ValidateSet("All", "Munin", "Relay")]
    [string]$Project = "All"
)

Write-Host "Building Munin IRC Client ($Configuration)..." -ForegroundColor Cyan

switch ($Project) {
    "Munin" {
        Write-Host "  Building Munin.UI..." -ForegroundColor Gray
        dotnet build "$PSScriptRoot\Munin.UI\Munin.UI.csproj" -c $Configuration
    }
    "Relay" {
        Write-Host "  Building Munin.Relay..." -ForegroundColor Gray
        dotnet build "$PSScriptRoot\Munin.Relay\Munin.Relay.csproj" -c $Configuration
    }
    default {
        Write-Host "  Building entire solution (Munin.UI + Munin.Relay)..." -ForegroundColor Gray
        dotnet build "$PSScriptRoot\Munin.sln" -c $Configuration
    }
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
    
    if ($Project -eq "All" -or $Project -eq "Relay") {
        Write-Host "  Munin.Relay: $PSScriptRoot\Munin.Relay\bin\$Configuration\net8.0\MuninRelay.exe" -ForegroundColor Yellow
    }
    if ($Project -eq "All" -or $Project -eq "Munin") {
        Write-Host "  Munin.UI: $PSScriptRoot\Munin.UI\bin\$Configuration\net8.0-windows\Munin.exe" -ForegroundColor Yellow
    }
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
