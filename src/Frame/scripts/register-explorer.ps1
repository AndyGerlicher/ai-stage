# register-explorer.ps1
# Adds "Open with Frame" to the Windows Explorer folder context menu.
# Run as current user (HKCU) — no admin required.

param(
    [string]$FrameExePath
)

if (-not $FrameExePath) {
    # Auto-detect: look for Frame.exe next to this script or in typical build output
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $candidates = @(
        (Join-Path $scriptDir "..\src\Frame\bin\Release\net10.0-windows\Frame.exe"),
        (Join-Path $scriptDir "..\src\Frame\bin\Debug\net10.0-windows\Frame.exe")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $FrameExePath = (Resolve-Path $c).Path
            break
        }
    }
    if (-not $FrameExePath) {
        Write-Error "Could not find Frame.exe. Pass -FrameExePath explicitly."
        exit 1
    }
}

$keyPath = "HKCU:\Software\Classes\Directory\shell\Frame"
$commandPath = "$keyPath\command"

New-Item -Path $keyPath -Force | Out-Null
Set-ItemProperty -Path $keyPath -Name "(Default)" -Value "Open with AI Frame"
Set-ItemProperty -Path $keyPath -Name "Icon" -Value "`"$FrameExePath`""

New-Item -Path $commandPath -Force | Out-Null
Set-ItemProperty -Path $commandPath -Name "(Default)" -Value "`"$FrameExePath`" `"%V`""

# Also register for folder background (right-click empty space inside a folder)
$bgKeyPath = "HKCU:\Software\Classes\Directory\Background\shell\Frame"
$bgCommandPath = "$bgKeyPath\command"

New-Item -Path $bgKeyPath -Force | Out-Null
Set-ItemProperty -Path $bgKeyPath -Name "(Default)" -Value "Open with AI Frame"
Set-ItemProperty -Path $bgKeyPath -Name "Icon" -Value "`"$FrameExePath`""

New-Item -Path $bgCommandPath -Force | Out-Null
Set-ItemProperty -Path $bgCommandPath -Name "(Default)" -Value "`"$FrameExePath`" `"%V`""

Write-Host "Registered 'Open with AI Frame' in Explorer context menu."
Write-Host "  Path: $FrameExePath"
