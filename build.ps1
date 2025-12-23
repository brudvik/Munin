# Build script for Munin IRC Client
# Usage: .\build.ps1 [-Configuration <Debug|Release>]

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

Write-Host "Building Munin IRC Client ($Configuration)..." -ForegroundColor Cyan

dotnet build "$PSScriptRoot\Munin.sln" -c $Configuration

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBuild successful!" -ForegroundColor Green
} else {
    Write-Host "`nBuild failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
