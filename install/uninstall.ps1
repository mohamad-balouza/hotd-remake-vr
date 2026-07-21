<#
.SYNOPSIS
  Removes all HOTD Remake VR mod files from the game directory (leaves BepInEx in place).
#>
param(
    [string]$GamePath = "D:\SteamLibrary\steamapps\common\THE HOUSE OF THE DEAD Remake",
    [switch]$IncludeBepInEx
)

$ErrorActionPreference = "Stop"
$dataDir = Join-Path $GamePath "The House of the Dead Remake_Data"

$targets = @(
    (Join-Path $dataDir "UnitySubsystems"),
    (Join-Path $dataDir "Plugins\x86_64\XRSDKOpenVR.dll"),
    (Join-Path $dataDir "Plugins\x86_64\openvr_api.dll"),
    (Join-Path $dataDir "StreamingAssets\SteamVR"),
    (Join-Path $GamePath "BepInEx\plugins\HotdVR")
)
if ($IncludeBepInEx) {
    $targets += @(
        (Join-Path $GamePath "BepInEx"),
        (Join-Path $GamePath "winhttp.dll"),
        (Join-Path $GamePath "doorstop_config.ini"),
        (Join-Path $GamePath ".doorstop_version"),
        (Join-Path $GamePath "changelog.txt")
    )
}

foreach ($t in $targets) {
    if (Test-Path $t) {
        Remove-Item $t -Recurse -Force -Confirm:$false
        Write-Host "[removed] $t"
    }
}
Write-Host "Uninstall complete."
