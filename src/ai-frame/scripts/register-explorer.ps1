# register-explorer.ps1
# Adds "Open with ai-frame" to the Windows Explorer folder context menu.
# Run as current user (HKCU) — no admin required.

param(
    [string]$FrameExePath
)

if (-not $FrameExePath) {
    # Auto-detect: look for ai-frame.exe next to this script or in typical build output
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $candidates = @(
        (Join-Path $scriptDir "..\src\ai-frame\bin\Release\net10.0-windows\ai-frame.exe"),
        (Join-Path $scriptDir "..\src\ai-frame\bin\Debug\net10.0-windows\ai-frame.exe")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $FrameExePath = (Resolve-Path $c).Path
            break
        }
    }
    if (-not $FrameExePath) {
        Write-Error "Could not find ai-frame.exe. Pass -FrameExePath explicitly."
        exit 1
    }
}

$keyPath = "HKCU:\Software\Classes\Directory\shell\ai-frame"
$commandPath = "$keyPath\command"

New-Item -Path $keyPath -Force | Out-Null
Set-ItemProperty -Path $keyPath -Name "(Default)" -Value "Open with ai-frame"
Set-ItemProperty -Path $keyPath -Name "Icon" -Value "`"$FrameExePath`""

New-Item -Path $commandPath -Force | Out-Null
Set-ItemProperty -Path $commandPath -Name "(Default)" -Value "`"$FrameExePath`" `"%V`""

# Also register for folder background (right-click empty space inside a folder)
$bgKeyPath = "HKCU:\Software\Classes\Directory\Background\shell\ai-frame"
$bgCommandPath = "$bgKeyPath\command"

New-Item -Path $bgKeyPath -Force | Out-Null
Set-ItemProperty -Path $bgKeyPath -Name "(Default)" -Value "Open with ai-frame"
Set-ItemProperty -Path $bgKeyPath -Name "Icon" -Value "`"$FrameExePath`""

New-Item -Path $bgCommandPath -Force | Out-Null
Set-ItemProperty -Path $bgCommandPath -Name "(Default)" -Value "`"$FrameExePath`" `"%V`""

Write-Host "Registered 'Open with ai-frame' in Explorer context menu."
Write-Host "  Path: $FrameExePath"
