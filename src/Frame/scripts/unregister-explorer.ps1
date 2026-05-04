# unregister-explorer.ps1
# Removes "Open with Frame" from the Windows Explorer folder context menu.

$keyPath = "HKCU:\Software\Classes\Directory\shell\Frame"
$bgKeyPath = "HKCU:\Software\Classes\Directory\Background\shell\Frame"

$removed = $false
foreach ($path in @($keyPath, $bgKeyPath)) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
        $removed = $true
    }
}

if ($removed) {
    Write-Host "Removed 'Open with AI Frame' from Explorer context menu."
} else {
    Write-Host "Nothing to remove — 'Open with AI Frame' was not registered."
}
