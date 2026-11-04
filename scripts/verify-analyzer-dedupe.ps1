<#
.SYNOPSIS
Proves that a PicoBench consumer which references the source generator twice
still builds, and still generates exactly one implementation per class.

.DESCRIPTION
B3-M2: the package ships PicoBench.Generators.dll as an analyzer
(buildTransitive/PicoBench.props). A consumer that also brings the same
generator in — a ProjectReference to PicoBench.Generators.csproj, a vendored
copy of the DLL, a second package — used to make Roslyn run two generator
instances and fail the build with

    error CS0111: Type 'X' already defines a member called 'RunBenchmarksAsync'

src/buildTransitive/PicoBench.targets closes that hole at the MSBuild level.
This script is the guard: it packs the current tree, then builds three
consumers out of that package and requires every one of them to compile and to
produce exactly one generated file per benchmark class.

  package                      PackageReference only (control)
  project-reference            the generator as ProjectReference, next to the package
  vendored-renamed-copy        the same DLL again, under a different file name
                               (this is the case the SDK cannot collapse)

Run it with the repository as it is; a tree without the targets file fails on
vendored-renamed-copy with CS0111, which is the point of the check.

.PARAMETER RepoRoot
Repository root. Defaults to the parent of this script.

.PARAMETER WorkingRoot
Where the throw-away feed and consumer projects are written. Defaults to a
fresh directory under the system temp path.

.PARAMETER Version
Version packed for the check. Default 0.0.0-dedupe-verify.

.EXAMPLE
./scripts/verify-analyzer-dedupe.ps1

.EXAMPLE
./scripts/verify-analyzer-dedupe.ps1 -KeepArtifacts -Verbose
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string] $WorkingRoot,
    [string] $Version = '0.0.0-dedupe-verify',
    [switch] $KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string] $What, [scriptblock] $Action)

    Write-Verbose "> $What"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
if (-not $WorkingRoot) {
    $WorkingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "picobench-dedupe-$([System.IO.Path]::GetRandomFileName())"
}
$WorkingRoot = [System.IO.Path]::GetFullPath($WorkingRoot)

$feed = Join-Path $WorkingRoot 'feed'
$unpacked = Join-Path $WorkingRoot 'unpacked'
New-Item -ItemType Directory -Force -Path $feed, $unpacked | Out-Null

# The repository NuGet.config lists artifacts/nupkg/ as a local source; restore
# warns when it is missing.
New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot 'artifacts/nupkg') | Out-Null

Write-Host "Packing PicoBench $Version from $RepoRoot"
Invoke-Checked 'dotnet pack' {
    dotnet pack (Join-Path $RepoRoot 'src/PicoBench/PicoBench.csproj') `
        --configuration Release `
        --output $feed `
        -p:Version=$Version `
        -p:ContinuousIntegrationBuild=false
}

$nupkg = Join-Path $feed "PicoBench.$Version.nupkg"
if (-not (Test-Path -LiteralPath $nupkg)) {
    throw "Expected package was not produced: $nupkg"
}

Copy-Item -LiteralPath $nupkg -Destination (Join-Path $WorkingRoot 'package.zip')
Expand-Archive -LiteralPath (Join-Path $WorkingRoot 'package.zip') -DestinationPath $unpacked -Force
$packagedGenerator = Join-Path $unpacked 'buildTransitive/PicoBench.Generators/analyzers/dotnet/cs/PicoBench.Generators.dll'
if (-not (Test-Path -LiteralPath $packagedGenerator)) {
    throw "The package does not ship the source generator: $packagedGenerator"
}
if (-not (Test-Path -LiteralPath (Join-Path $unpacked 'buildTransitive/PicoBench.targets'))) {
    throw 'The package does not ship buildTransitive/PicoBench.targets — the duplicate-analyzer guard is missing from the package.'
}

$program = @'
using PicoBench;

[BenchmarkClass]
public partial class Dbl
{
    [Benchmark]
    public void Run() { }
}

public static class Program
{
    public static async Task Main()
    {
        var suite = await new Dbl().RunBenchmarksAsync(BenchmarkConfig.Quick);
        Console.WriteLine($"results={suite.Results.Count}");
    }
}
'@

$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="check-feed" value="$feed" />
  </packageSources>
</configuration>
"@

function New-Consumer {
    param([string] $Path, [string] $ExtraItems)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Set-Content -LiteralPath (Join-Path $Path 'nuget.config') -Value $nugetConfig
    Set-Content -LiteralPath (Join-Path $Path 'Program.cs') -Value $program
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'global.json') -Destination (Join-Path $Path 'global.json')

    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="PicoBench" Version="$Version" />
$ExtraItems
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $Path 'app.csproj') -Value $csproj
}

$cases = [ordered] @{
    'package'                = ''
    'project-reference'      = "    <ProjectReference Include=`"$(Join-Path $RepoRoot 'src/PicoBench.Generators/PicoBench.Generators.csproj')`" OutputItemType=`"Analyzer`" ReferenceOutputAssembly=`"false`" />"
    'vendored-renamed-copy'  = '    <Analyzer Include="analyzers/PicoBench.Generators.Vendored.dll" />'
}

$failures = @()
foreach ($case in $cases.Keys) {
    $path = Join-Path $WorkingRoot $case
    New-Consumer -Path $path -ExtraItems $cases[$case]

    if ($case -eq 'vendored-renamed-copy') {
        $analyzers = Join-Path $path 'analyzers'
        New-Item -ItemType Directory -Force -Path $analyzers | Out-Null
        Copy-Item -LiteralPath $packagedGenerator -Destination (Join-Path $analyzers 'PicoBench.Generators.Vendored.dll')
    }

    Write-Host "Building consumer '$case'"
    Push-Location $path
    try {
        $output = dotnet build --configuration Release --verbosity minimal 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $generated = @(Get-ChildItem -Path (Join-Path $path 'obj') -Recurse -Filter '*.g.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*generated*' })

    if ($exit -ne 0) {
        $failures += $case
        Write-Host "  FAIL: build exited $exit" -ForegroundColor Red
        $output | Where-Object { $_ -match 'error ' } | Select-Object -First 5 | ForEach-Object { Write-Host "    $_" }
    }
    elseif ($generated.Count -ne 1) {
        $failures += $case
        Write-Host "  FAIL: expected exactly one generated file, found $($generated.Count)" -ForegroundColor Red
        $generated | ForEach-Object { Write-Host "    $($_.FullName)" }
    }
    else {
        Write-Host "  ok: 1 generated file ($($generated[0].Name))" -ForegroundColor Green
    }
}

if (-not $KeepArtifacts) {
    Remove-Item -LiteralPath $WorkingRoot -Recurse -Force -ErrorAction SilentlyContinue
}
else {
    Write-Host "Artifacts kept in $WorkingRoot"
}

if ($failures.Count -gt 0) {
    throw "Duplicate-generator check failed for: $($failures -join ', ')"
}

Write-Host 'Duplicate-generator check passed: every double reference still generates one implementation.' -ForegroundColor Green
