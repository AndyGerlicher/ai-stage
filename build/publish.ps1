<#
.SYNOPSIS
    Builds and publishes ai-stage and ai-frame to publish/ai-stage as a self-contained
    win-x64 bundle ready for `vpk pack`.
.DESCRIPTION
    Both apps are published into the same output directory so ai-stage.exe and
    ai-frame.exe sit side-by-side. ai-stage's FrameLauncher discovers ai-frame.exe in
    that directory at runtime.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repoRoot 'publish/ai-stage'

if (Test-Path $publishDir)
{
    Remove-Item $publishDir -Recurse -Force
}

$apps = @(
    @{ Name = 'ai-stage'; Project = 'src/ai-stage/ai-stage.csproj' }
    @{ Name = 'ai-frame'; Project = 'src/ai-frame/ai-frame.csproj' }
)

foreach ($app in $apps)
{
    $project = Join-Path $repoRoot $app.Project
    Write-Host "Publishing $($app.Name)..." -ForegroundColor Cyan
    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $publishDir `
        --nologo
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host "  FAILED ($($app.Name) exit $LASTEXITCODE)" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Published to $publishDir" -ForegroundColor Green
