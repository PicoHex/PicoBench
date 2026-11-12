#!/usr/bin/env pwsh
<#
Verifies the exit-code contract of scripts/release.ps1 (review items B5-H1, B5-H3;
Batch 3.1 / 3.3 / 3.7).

Contract:

    0  success - either a dry run that reported the decision, or baseline
       refresh + commit + annotated tag completed
    2  precondition / usage error (no baselines, bad switch combination,
       api-surface tooling missing)
    3  working tree is dirty (a release must be cut from a clean tree)
    4  the public API lost members and -AllowBreaking was not passed
    5  build failure, or the api-surface tool failed to run

Every case runs against a throwaway git repository in the temp directory, so
the real repository is never written to. The script exits 0 only when every
case matches the contract above; a mismatch prints the observed exit code and
keeps going so a single run reports all broken cases.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$releaseScript = (Resolve-Path (Join-Path $PSScriptRoot ".." "release.ps1")).Path
$script:Failed = 0
$script:Cases = 0

function Write-Case([string]$name) {
    $script:Cases++
    Write-Host "== $name" -ForegroundColor Cyan
}

function Assert-Exit([string]$expectation, [int]$actual, [int]$expected) {
    if ($actual -eq $expected) {
        Write-Host "   ok: exit $actual ($expectation)" -ForegroundColor Green
        return
    }
    Write-Host "   FAIL: expected exit $expected, got $actual ($expectation)" -ForegroundColor Red
    $script:Failed++
}

function Assert-True([string]$what, [bool]$condition) {
    if ($condition) {
        Write-Host "   ok: $what" -ForegroundColor Green
        return
    }
    Write-Host "   FAIL: $what" -ForegroundColor Red
    $script:Failed++
}

function Invoke-Release([string]$root, [string[]]$extra) {
    $arguments = @("-NoProfile", "-NonInteractive", "-File", $releaseScript, "-RepoRoot", $root) + $extra
    $output = & pwsh @arguments 2>&1
    $code = $LASTEXITCODE
    return [pscustomobject]@{ ExitCode = $code; Output = ($output -join [Environment]::NewLine) }
}

function Get-Tags([string]$root) {
    return @(
        (Invoke-GitQuiet $root @("tag", "--list", "v*")) -split "`n" |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ }
    )
}

function Invoke-GitQuiet([string]$root, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & git -C $root @arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) { throw "git $($arguments -join ' ') failed: $($output -join ' ')" }
    return ($output -join "`n")
}

$root = Join-Path ([System.IO.Path]::GetTempPath()) ("pb-release-contract-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$null = New-Item -ItemType Directory -Path $root -Force

try {
    Write-Case "fixture: a packable project with a committed API baseline"
    $null = New-Item -ItemType Directory -Path (Join-Path $root "src/Fixture") -Force
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>Fixture</AssemblyName>
    <PackageId>Fixture</PackageId>
    <IsPackable>true</IsPackable>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $root "src/Fixture/Fixture.csproj")
    @'
namespace Fixture
{
    public sealed class Widget
    {
        public int Size { get; set; }

        public void Run() { }
    }
}
'@ | Set-Content -LiteralPath (Join-Path $root "src/Fixture/Widget.cs")
    # Build output must not make the tree dirty, or every second release would be
    # refused - the real repository ignores bin/obj the same way.
    "bin/`nobj/`napi.stash/`n" | Set-Content -LiteralPath (Join-Path $root ".gitignore")
    $null = Invoke-GitQuiet $root @("init", "-q", "-b", "main")
    $null = Invoke-GitQuiet $root @("config", "user.email", "release-contract@example.invalid")
    $null = Invoke-GitQuiet $root @("config", "user.name", "Release Contract")
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "fixture")

    $bootstrap = Invoke-Release $root @("-Bootstrap", "-AssumeCurrent")
    Assert-Exit "bootstrap writes the baseline" $bootstrap.ExitCode 0
    $baseline = Join-Path $root "api/Fixture.public.txt"
    Assert-True "api/Fixture.public.txt exists after bootstrap" (Test-Path $baseline)
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "api baseline")

    # ------------------------------------------------------------------ 3.1
    Write-Case "3.1 - an unchanged API still reaches a tag (empty release commit is allowed)"
    $first = Invoke-Release $root @()
    Assert-Exit "clean tree, unchanged API" $first.ExitCode 0
    $year = (Get-Date).Year
    $expectedTag = "v$year.0.0"
    Assert-True "tag $expectedTag created" ((Get-Tags $root) -contains $expectedTag)
    Assert-True "release commit exists" ((Invoke-GitQuiet $root @("log", "-1", "--format=%s")).Trim() -eq "chore(release): v$year.0.0")
    Assert-True "working tree clean after the release" ((Invoke-GitQuiet $root @("status", "--porcelain")).Trim() -eq "")

    Write-Case "3.1 - a second consecutive release also succeeds (y-counter bump)"
    $second = Invoke-Release $root @()
    Assert-Exit "second release on the same tree" $second.ExitCode 0
    $expectedSecond = "v$year.0.1"
    Assert-True "tag $expectedSecond created" ((Get-Tags $root) -contains $expectedSecond)

    Write-Case "3.1 - -DryRun reports the decision and writes nothing"
    $tagsBefore = (Get-Tags $root) -join ','
    $headBefore = (Invoke-GitQuiet $root @("rev-parse", "HEAD")).Trim()
    $dry = Invoke-Release $root @("-DryRun")
    Assert-Exit "dry run" $dry.ExitCode 0
    Assert-True "dry run created no tag" (((Get-Tags $root) -join ',') -eq $tagsBefore)
    Assert-True "dry run did not move HEAD" ((Invoke-GitQuiet $root @("rev-parse", "HEAD")).Trim() -eq $headBefore)

    # ------------------------------------------------------------------ 3.3
    Write-Case "3.3 - removing a public member fails without -AllowBreaking"
    $baselineOriginal = Get-Content -LiteralPath $baseline -Raw
    # A baseline line that no longer exists in the assembly: the diff reports
    # removed=1, which must be a hard failure.
    Set-Content -LiteralPath $baseline -Value ($baselineOriginal + "M:Fixture.Widget.Removed() : void`n")
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "simulate a removed public member")
    $breaking = Invoke-Release $root @()
    Assert-Exit "removed>0 without -AllowBreaking" $breaking.ExitCode 4
    Assert-True "no tag was created for the rejected release" (-not ((Get-Tags $root) -contains "v$year.1.0"))

    Write-Case "3.3 - the same diff is accepted with -AllowBreaking and re-baselines the API"
    $allowed = Invoke-Release $root @("-AllowBreaking")
    Assert-Exit "removed>0 with -AllowBreaking" $allowed.ExitCode 0
    Assert-True "tag v$year.1.0 created (x counter moves on an API change)" ((Get-Tags $root) -contains "v$year.1.0")
    Assert-True "the baseline was re-written without the phantom member" (-not ((Get-Content -LiteralPath $baseline -Raw).Contains("Fixture.Widget.Removed")))

    # ------------------------------------------------------------------ 3.7
    Write-Case "3.7 - a dirty working tree is refused with exit 3"
    Set-Content -LiteralPath (Join-Path $root "src/Fixture/Widget.cs") -Value ((Get-Content -LiteralPath (Join-Path $root "src/Fixture/Widget.cs") -Raw) + "`n// dirty`n")
    $dirty = Invoke-Release $root @()
    Assert-Exit "dirty tree" $dirty.ExitCode 3
    $null = Invoke-GitQuiet $root @("checkout", "--", "src/Fixture/Widget.cs")

    Write-Case "3.7 - missing baselines is a precondition error (exit 2)"
    $apiDir = Join-Path $root "api"
    $stash = Join-Path $root "api.stash"
    Move-Item -LiteralPath $apiDir -Destination $stash
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "drop the baselines")
    $missing = Invoke-Release $root @()
    Assert-Exit "no api/ baselines" $missing.ExitCode 2
    
    Move-Item -LiteralPath $stash -Destination $apiDir
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "restore the baselines")

    Write-Case "3.7 - a build failure is reported with exit 5"
    $sourceFile = Join-Path $root "src/Fixture/Widget.cs"
    $sourceOriginal = Get-Content -LiteralPath $sourceFile -Raw
    Set-Content -LiteralPath $sourceFile -Value "$sourceOriginal`nthis is not C#`n"
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "break the build")
    $broken = Invoke-Release $root @()
    Assert-Exit "build failure" $broken.ExitCode 5
    Set-Content -LiteralPath $sourceFile -Value $sourceOriginal
    $null = Invoke-GitQuiet $root @("add", "-A")
    $null = Invoke-GitQuiet $root @("commit", "-q", "-m", "fix the build")

    Write-Host ""
    if ($script:Failed -eq 0) {
        Write-Host "release.ps1 exit-code contract: $($script:Cases) cases, all ok" -ForegroundColor Green
        exit 0
    }
    Write-Host "release.ps1 exit-code contract: $script:Failed failure(s) across $($script:Cases) cases" -ForegroundColor Red
    exit 1
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
