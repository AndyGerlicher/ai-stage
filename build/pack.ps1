<#
.SYNOPSIS
    Packs the published ai-stage + ai-frame bundle into a Velopack release.
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
$publishDir  = Join-Path $repoRoot 'publish/ai-stage'
$releasesDir = Join-Path $repoRoot 'Releases'
$icon        = Join-Path $repoRoot 'src/ai-stage/app.ico'

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

if (-not (Test-Path (Join-Path $publishDir 'ai-stage.exe')))
{
    Write-Host "ai-stage.exe not found in $publishDir. Run publish.ps1 first." -ForegroundColor Red
    exit 1
}

Write-Host "Packing ai-stage $Version..." -ForegroundColor Cyan
vpk pack `
    --packId ai-stage `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe ai-stage.exe `
    --packTitle 'ai-stage' `
    --icon $icon `
    --channel $Channel `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Release artifacts in $releasesDir" -ForegroundColor Green
