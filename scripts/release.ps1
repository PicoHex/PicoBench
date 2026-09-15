# Release script — packs to the local folder feed, tags, and pushes.
#
# Why: after pushing a release tag, nuget.org needs minutes to index the new
# package (plus NuGet's 30-minute HTTP index cache on consumer machines).
# Sibling PicoHex repos consume PicoBench via PackageReference, so local
# development would stall until indexing completes. This script packs the
# package into the local folder feed (NuGet.config: local -> artifacts/nupkg)
# before tagging, so local restores resolve the new version instantly. CI
# (release.yml) still publishes to nuget.org as usual; once indexed, both
# sources serve the same bits.
#
# Usage (from repo root):
#   pwsh ./scripts/release.ps1 -Version 2026.2.6
#   pwsh ./scripts/release.ps1 -Version 2026.2.6 -SkipTests
#   pwsh ./scripts/release.ps1 -Version 2026.2.6 -NoPush   # pack + tag only
#
# The pack phase mirrors release.yml exactly so local nupkgs match CI output.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$SkipTests,

    [switch]$NoPush
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Fail([string]$message) {
    Write-Error $message
    exit 1
}

# --- Preconditions -----------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repoRoot
try {
    if (-not ($Version -match '^\d+\.\d+\.\d+$')) {
        Fail "Version must be numeric (e.g. 2026.2.6), got '$Version'"
    }

    $tag = "v$Version"
    if (git tag -l $tag) {
        Fail "Tag '$tag' already exists"
    }

    $dirty = git status --porcelain
    if ($dirty) {
        Fail "Working tree is not clean:`n$dirty`nCommit or stash changes before releasing."
    }

    # --- Tests ------------------------------------------------------------------

    if (-not $SkipTests) {
        Write-Host "=== Running tests ===" -ForegroundColor Cyan
        dotnet build PicoBench.slnx --configuration Release
        if ($LASTEXITCODE -ne 0) { Fail "Build failed" }

        # TUnit on Microsoft.Testing.Platform: build first, then test --no-build
        # (see the tunit-runner skill; do not pass --nologo).
        dotnet test --project tests/PicoBench.Tests/PicoBench.Tests.csproj `
            --configuration Release `
            --no-build `
            --no-progress
        if ($LASTEXITCODE -ne 0) { Fail "Tests failed" }
    }

    # --- Pack (mirrors release.yml phase ordering) --------------------------------

    $nupkgDir = Join-Path $repoRoot "artifacts/nupkg"
    Remove-Item -Path $nupkgDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $nupkgDir | Out-Null

    $packCommon = @(
        "--configuration", "Release",
        "--output", $nupkgDir,
        "-p:Version=$Version",
        "-p:UseProjectReferences=false"
    )

    function Pack([string]$project) {
        Write-Host "  -> pack $project" -ForegroundColor DarkGray
        dotnet pack $project @packCommon
        if ($LASTEXITCODE -ne 0) {
            # Occasional MSBuild node races exit non-zero after a successful
            # compile (no error output) - retry once before failing.
            Write-Host "  !! pack failed (exit $LASTEXITCODE), retrying once..." -ForegroundColor Yellow
            dotnet pack $project @packCommon
            if ($LASTEXITCODE -ne 0) { Fail "Pack failed: $project" }
        }
    }

    Write-Host "=== Packing (generator is embedded in the PicoBench package) ===" -ForegroundColor Cyan
    Pack "src/PicoBench/PicoBench.csproj"

    $packed = @(Get-ChildItem $nupkgDir -Filter "*.nupkg")
    if ($packed.Count -eq 0) { Fail "No packages were produced" }
    Write-Host "=== Local feed ready: $nupkgDir ($($packed.Count) packages, version $Version) ===" -ForegroundColor Green

    # --- Tag + push ----------------------------------------------------------------

    git tag -a $tag -m "PicoBench $Version - packed locally + published via release.yml"
    if ($LASTEXITCODE -ne 0) { Fail "git tag failed" }

    if (-not $NoPush) {
        git push origin main
        if ($LASTEXITCODE -ne 0) { Fail "git push main failed" }
        git push origin $tag
        if ($LASTEXITCODE -ne 0) { Fail "git push tag failed" }
    }
    else {
        Write-Host "Tag '$tag' created locally. Push when ready: git push origin main $tag" -ForegroundColor Yellow
    }

    Write-Host "=== Release $Version complete ===" -ForegroundColor Green
}
finally {
    Pop-Location
}
