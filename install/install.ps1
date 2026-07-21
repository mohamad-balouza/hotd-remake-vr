<#
.SYNOPSIS
  Installs the HOTD Remake VR mod files into the game directory.

.DESCRIPTION
  Places the Valve OpenVR XR plugin files (native provider + subsystem manifest
  + settings file) into the game's Data folder so Unity discovers the XR
  subsystems at boot, and deploys the mod plugin DLLs into BepInEx/plugins.

  BepInEx 5 (x64) must already be installed in the game folder (the release
  bundle ships it; see README).

.PARAMETER GamePath
  Path to the game install folder (the one containing
  "The House of the Dead Remake.exe").
#>
param(
    [string]$GamePath = "D:\SteamLibrary\steamapps\common\THE HOUSE OF THE DEAD Remake"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $GamePath "The House of the Dead Remake_Data"

if (-not (Test-Path (Join-Path $GamePath "The House of the Dead Remake.exe"))) {
    throw "Game exe not found under '$GamePath'. Pass -GamePath pointing at the game folder."
}
if (-not (Test-Path (Join-Path $GamePath "BepInEx\core\BepInEx.dll"))) {
    throw "BepInEx not found in '$GamePath'. Install BepInEx 5.4.23.5 (win x64) first - see README."
}

# --- 1. Native OpenVR XR provider + subsystem manifest ---------------------
$pluginsDir = Join-Path $dataDir "Plugins\x86_64"
$subsystemsDir = Join-Path $dataDir "UnitySubsystems\XRSDKOpenVR"
$thirdparty = Join-Path $repoRoot "thirdparty\openvr-xr-plugin"

New-Item -ItemType Directory -Force $subsystemsDir | Out-Null
Copy-Item (Join-Path $thirdparty "XRSDKOpenVR.dll") $pluginsDir -Force
Copy-Item (Join-Path $thirdparty "openvr_api.dll") $pluginsDir -Force
Copy-Item (Join-Path $thirdparty "UnitySubsystemsManifest.json") $subsystemsDir -Force
Write-Host "[ok] Native XR provider + subsystem manifest placed."

# --- 2. OpenVR settings file (read by the native provider in player builds) -
# StereoRenderingMode 0 = MultiPass (safe: no stereo shader variants needed)
# InitializationType 1  = Scene (full VR app, not overlay)
# MirrorView 2          = Right eye on the desktop window (mode 3 has a known
#                         black-window bug in the Valve plugin)
# IMPORTANT: keys with EMPTY values (e.g. "ActionManifestFileRelativeFilePath:")
# hang the native parser in XRSDKOpenVR.dll - only write keys that have values.
$steamVrDir = Join-Path $dataDir "StreamingAssets\SteamVR"
New-Item -ItemType Directory -Force $steamVrDir | Out-Null
@"
StereoRenderingMode: 0
InitializationType: 1
MirrorView: 2
"@ | Set-Content -Path (Join-Path $steamVrDir "OpenVRSettings.asset") -Encoding ascii
Write-Host "[ok] OpenVRSettings.asset written (multipass, scene, mirror=right)."

# --- 3. Mod plugin DLLs ----------------------------------------------------
$pluginDir = Join-Path $GamePath "BepInEx\plugins\HotdVR"
New-Item -ItemType Directory -Force $pluginDir | Out-Null
$binaries = @(
    (Join-Path $repoRoot "src\HotdVR\bin\Release\HotdVR.dll"),
    (Join-Path $repoRoot "src\Unity.XR.OpenVR\bin\Release\Unity.XR.OpenVR.dll"),
    (Join-Path $repoRoot "src\Unity.XR.Management\bin\Release\Unity.XR.Management.dll")
)
foreach ($bin in $binaries) {
    if (Test-Path $bin) {
        Copy-Item $bin $pluginDir -Force
        Write-Host "[ok] Deployed $(Split-Path -Leaf $bin)"
    } else {
        Write-Warning "Missing build output: $bin (run 'dotnet build src/HotdVR/HotdVR.csproj -c Release' first)"
    }
}

Write-Host ""
Write-Host "Install complete. Launch the game via Steam (or the exe directly) with SteamVR available."
