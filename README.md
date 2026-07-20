# HOTD Remake VR

A native VR mod for **THE HOUSE OF THE DEAD Remake** (Steam) — true stereo rendering, 6DOF head tracking, and motion-controller lightgun aiming. Built for Quest 3 over Virtual Desktop / SteamVR, works with any SteamVR-compatible headset.

> **Status: work in progress.** This README will be expanded with full installation and troubleshooting instructions as the mod reaches its first release.

## How it works (short version)

The game runs on Unity 2020.3 (Mono) with HDRP. The mod:

- loads via [BepInEx 5](https://github.com/BepInEx/BepInEx),
- injects Valve's [OpenVR XR plugin](https://github.com/ValveSoftware/unity-xr-plugin) into the built game and initializes Unity's XR stack at runtime (multipass stereo),
- patches the game's aiming pipeline so your motion controller *is* the lightgun (the game's own crosshair/scoring/aim-assist logic stays intact),
- re-projects the HUD and menus into VR-friendly world space.

## Building

```
dotnet build src/HotdVR/HotdVR.csproj -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\THE HOUSE OF THE DEAD Remake"
```

`GamePath` must point at your game install (defaults to the path above). The build auto-deploys the plugin into `BepInEx/plugins/HotdVR/` if BepInEx is present in the game folder.

## License

MIT for this repository's code. Third-party components retain their own licenses — see `THIRD-PARTY-NOTICES.md`.
