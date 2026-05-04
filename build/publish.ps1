<#
.SYNOPSIS
    Builds and publishes Stage and Frame to publish/Stage as a self-contained
    win-x64 bundle ready for `vpk pack`.
.DESCRIPTION
    Both apps are published into the same output directory so Stage.exe and
    Frame.exe sit side-by-side. Stage's FrameLauncher discovers Frame.exe in
    that directory at runtime.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repoRoot 'publish/Stage'

if (Test-Path $publishDir)
{
    Remove-Item $publishDir -Recurse -Force
}

$apps = @(
    @{ Name = 'Stage'; Project = 'src/Stage/Stage.csproj' }
    @{ Name = 'Frame'; Project = 'src/Frame/Frame.csproj' }
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
