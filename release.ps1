# Munin IRC Client - Release Build Script
# Creates a self-contained Windows x64 release build and packages it as a zip

param(
    [string]$Version = "",
    [ValidateSet("major", "minor", "patch", "none")]
    [string]$Bump = "patch"
)

$ErrorActionPreference = "Stop"

function Get-ProjectVersion {
    $csproj = Get-Content "Munin.UI\Munin.UI.csproj" -Raw
    if ($csproj -match '<Version>([^<]+)</Version>') {
        return $matches[1]
    }
    return "1.0.0"
}

function Set-ProjectVersion {
    param([string]$NewVersion)
    
    # Update all project files
    $projects = @("Munin.UI\Munin.UI.csproj", "Munin.Core\Munin.Core.csproj", "MuninRelay\MuninRelay.csproj")
    
    foreach ($proj in $projects) {
        $content = Get-Content $proj -Raw
        $content = $content -replace '<Version>[^<]+</Version>', "<Version>$NewVersion</Version>"
        Set-Content $proj $content -NoNewline
        Write-Host "  Updated $proj to v$NewVersion" -ForegroundColor Gray
    }
}

function Increment-Version {
    param(
        [string]$CurrentVersion,
        [string]$BumpType
    )
    
    $parts = $CurrentVersion.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]
    
    switch ($BumpType) {
        "major" { $major++; $minor = 0; $patch = 0 }
        "minor" { $minor++; $patch = 0 }
        "patch" { $patch++ }
    }
    
    return "$major.$minor.$patch"
}

# Get current version
$CurrentVersion = Get-ProjectVersion

# Determine new version
if ([string]::IsNullOrEmpty($Version)) {
    if ($Bump -eq "none") {
        $Version = $CurrentVersion
        Write-Host "Using current version: v$Version" -ForegroundColor Cyan
    } else {
        $Version = Increment-Version -CurrentVersion $CurrentVersion -BumpType $Bump
        Write-Host "Incrementing version: v$CurrentVersion -> v$Version ($Bump)" -ForegroundColor Cyan
        Set-ProjectVersion -NewVersion $Version
    }
} else {
    Write-Host "Setting version: v$Version" -ForegroundColor Cyan
    Set-ProjectVersion -NewVersion $Version
}

$Date = Get-Date -Format "yyyy-MM-dd"
$ZipName = "munin-windows-$Date-v$Version.zip"
$PublishDir = "Munin.UI\bin\Release\net8.0-windows\win-x64\publish"
$RelayPublishDir = "MuninRelay\bin\Release\net8.0\win-x64\publish"
$OutputDir = "releases"

Write-Host "`nBuilding Munin IRC Client v$Version (Release)..." -ForegroundColor Cyan

# Clean previous publish
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
if (Test-Path $RelayPublishDir) {
    Remove-Item $RelayPublishDir -Recurse -Force
}

# Build Munin.UI
Write-Host "  Publishing Munin.UI..." -ForegroundColor Gray
dotnet publish Munin.UI -c Release -r win-x64 --self-contained

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nMunin.UI build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

# Build MuninRelay
Write-Host "  Publishing MuninRelay..." -ForegroundColor Gray
dotnet publish MuninRelay -c Release -r win-x64 --self-contained

if ($LASTEXITCODE -ne 0) {
    Write-Host "`nMuninRelay build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nBuild successful!" -ForegroundColor Green

# Create scripts folder in publish directory
$ScriptsDir = Join-Path $PublishDir "scripts"
$ExamplesDir = Join-Path $ScriptsDir "examples"
New-Item -ItemType Directory -Path $ExamplesDir -Force | Out-Null

# Copy example scripts
Write-Host "Copying example scripts..." -ForegroundColor Cyan
Copy-Item "scripts\examples\*" -Destination $ExamplesDir -Recurse

# Copy MuninRelay to its own folder in publish directory
Write-Host "Copying MuninRelay..." -ForegroundColor Cyan
$RelayDestDir = Join-Path $PublishDir "MuninRelay"
New-Item -ItemType Directory -Path $RelayDestDir -Force | Out-Null
Copy-Item "$RelayPublishDir\*" -Destination $RelayDestDir -Recurse

# Copy documentation
Write-Host "Copying documentation..." -ForegroundColor Cyan
Copy-Item "README.md" -Destination $PublishDir
Copy-Item "CHANGELOG.md" -Destination $PublishDir
Copy-Item "LICENSE" -Destination $PublishDir

# Copy docs folder if it exists
if (Test-Path "docs") {
    $DocsDir = Join-Path $PublishDir "docs"
    New-Item -ItemType Directory -Path $DocsDir -Force | Out-Null
    Copy-Item "docs\*" -Destination $DocsDir -Recurse
}

# Create releases folder
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Create zip
$ZipPath = Join-Path $OutputDir $ZipName
Write-Host "Creating $ZipName..." -ForegroundColor Cyan

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host "`nRelease package created successfully!" -ForegroundColor Green
Write-Host "Output: $ZipPath" -ForegroundColor Yellow
Write-Host "Size: $([math]::Round((Get-Item $ZipPath).Length / 1MB, 2)) MB" -ForegroundColor Yellow
