<#
.SYNOPSIS
    Packs the published Stage+Frame bundle into a Velopack release.
.DESCRIPTION
    Runs build/publish.ps1 first (unless -SkipPublish), then invokes the
    `vpk` global tool to produce a Setup.exe + nupkg + RELEASES under
    Releases/. Install the vpk tool once with:
        dotnet tool install -g vpk
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Channel = 'win',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot    = Split-Path $PSScriptRoot -Parent
$publishDir  = Join-Path $repoRoot 'publish/Stage'
$releasesDir = Join-Path $repoRoot 'Releases'
$icon        = Join-Path $repoRoot 'src/Stage/app.ico'

if (-not (Get-Command vpk -ErrorAction SilentlyContinue))
{
    Write-Host "vpk not found on PATH. Install with: dotnet tool install -g vpk" -ForegroundColor Red
    exit 1
}

if (-not $SkipPublish)
{
    & (Join-Path $PSScriptRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path (Join-Path $publishDir 'Stage.exe')))
{
    Write-Host "Stage.exe not found in $publishDir. Run publish.ps1 first." -ForegroundColor Red
    exit 1
}

Write-Host "Packing StageFrame $Version..." -ForegroundColor Cyan
vpk pack `
    --packId StageFrame `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe Stage.exe `
    --packTitle 'Stage' `
    --icon $icon `
    --channel $Channel `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Release artifacts in $releasesDir" -ForegroundColor Green
