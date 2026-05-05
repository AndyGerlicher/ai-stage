# unregister-explorer.ps1
# Removes "Open with ai-frame" from the Windows Explorer folder context menu.

$keyPath = "HKCU:\Software\Classes\Directory\shell\ai-frame"
$bgKeyPath = "HKCU:\Software\Classes\Directory\Background\shell\ai-frame"

$removed = $false
foreach ($path in @($keyPath, $bgKeyPath)) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
        $removed = $true
    }
}

if ($removed) {
    Write-Host "Removed 'Open with ai-frame' from Explorer context menu."
} else {
    Write-Host "Nothing to remove — 'Open with ai-frame' was not registered."
}
