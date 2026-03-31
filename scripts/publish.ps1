<#
.SYNOPSIS
Build, test, pack and publish PicoBench NuGet package.

.DESCRIPTION
This script performs the following steps:
1. Clean build output
2. Restore dependencies
3. Build in Release configuration
4. Run all tests
5. Create NuGet package with symbols
6. Optionally publish to NuGet.org

.PARAMETER Publish
If specified, publishes the package to NuGet.org. Requires NUGET_API_KEY environment variable.

.PARAMETER ApiKey
NuGet API key for publishing. If not provided, uses NUGET_API_KEY environment variable.

.PARAMETER Configuration
Build configuration (default: Release)

.PARAMETER OutputDir
Directory for NuGet packages (default: ./nupkg)

.EXAMPLE
.\publish.ps1
# Builds and tests the package, creates .nupkg in ./nupkg

.EXAMPLE
.\publish.ps1 -Publish
# Builds, tests, creates package and publishes to NuGet.org using NUGET_API_KEY env var

.EXAMPLE
.\publish.ps1 -Publish -ApiKey "your-api-key"
# Builds, tests, creates package and publishes with specified API key
#>

[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$ApiKey,
    [string]$Configuration = "Release",
    [string]$OutputDir = "./nupkg"
)

$ErrorActionPreference = "Stop"

# Colors for output
$Green = [ConsoleColor]::Green
$Yellow = [ConsoleColor]::Yellow
$Red = [ConsoleColor]::Red
$Cyan = [ConsoleColor]::Cyan

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor $Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[SUCCESS] $Message" -ForegroundColor $Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] $Message" -ForegroundColor $Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor $Red
    exit 1
}

# Validate .NET SDK is available
function Test-Dotnet {
    try {
        $dotnetVersion = dotnet --version
        Write-Info "Using .NET SDK $dotnetVersion"
    } catch {
        Write-Error "dotnet command not found. Please install .NET SDK."
    }
}

# Clean previous build outputs
function Invoke-Clean {
    Write-Info "Cleaning build outputs..."
    dotnet clean --configuration $Configuration
    if (Test-Path $OutputDir) {
        Remove-Item -Path $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Restore dependencies
function Invoke-Restore {
    Write-Info "Restoring dependencies..."
    dotnet restore
}

# Build solution
function Invoke-Build {
    Write-Info "Building with configuration: $Configuration..."
    dotnet build --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
    }
}

# Run tests
function Invoke-Test {
    Write-Info "Running tests..."
    dotnet test --configuration $Configuration --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed"
    }
}

# Create NuGet package
function Invoke-Pack {
    Write-Info "Creating NuGet package..."
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    dotnet pack src/PicoBench/PicoBench.csproj `
        --configuration $Configuration `
        --no-build `
        --output $OutputDir `
        --include-symbols
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Pack failed"
    }
    
    $nupkgFiles = Get-ChildItem -Path $OutputDir -Filter "*.nupkg" -Exclude "*.symbols.*", "*.snupkg"
    $snupkgFiles = Get-ChildItem -Path $OutputDir -Filter "*.snupkg"
    
    Write-Success "Created $($nupkgFiles.Count) package(s) and $($snupkgFiles.Count) symbol package(s)"
    foreach ($file in $nupkgFiles) {
        Write-Info "  - $($file.Name) ($([math]::Round($file.Length / 1KB, 2)) KB)"
    }
}

# Publish to NuGet.org
function Invoke-Publish {
    Write-Info "Publishing to NuGet.org..."
    
    # Get API key
    if (-not $ApiKey) {
        $ApiKey = $env:NUGET_API_KEY
    }
    
    if (-not $ApiKey) {
        Write-Error "NuGet API key not found. Set NUGET_API_KEY environment variable or use -ApiKey parameter."
    }
    
    $nupkgFiles = Get-ChildItem -Path $OutputDir -Filter "*.nupkg" -Exclude "*.symbols.*", "*.snupkg"
    
    foreach ($file in $nupkgFiles) {
        Write-Info "Publishing $($file.Name)..."
        dotnet nuget push $file.FullName `
            --api-key $ApiKey `
            --source https://api.nuget.org/v3/index.json `
            --skip-duplicate
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to publish $($file.Name)"
        }
        
        Write-Success "Published $($file.Name)"
    }
}

# Main execution
try {
    Write-Info "Starting PicoBench release process..."
    Write-Info "Configuration: $Configuration"
    Write-Info "Output directory: $OutputDir"
    Write-Info "Publish to NuGet.org: $Publish"
    
    Test-Dotnet
    Invoke-Clean
    Invoke-Restore
    Invoke-Build
    Invoke-Test
    Invoke-Pack
    
    if ($Publish) {
        Invoke-Publish
        Write-Success "Release completed successfully!"
    } else {
        Write-Success "Build, test and pack completed successfully!"
        Write-Warning "Package not published to NuGet.org. Use -Publish flag to publish."
    }
} catch {
    Write-Error "Release process failed: $_"
}