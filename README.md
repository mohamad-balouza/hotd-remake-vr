# HOTD Remake VR

A native VR mod for **THE HOUSE OF THE DEAD Remake** (Steam) — true stereo rendering, 6DOF head tracking, and motion-controller lightgun aiming. Built and tested with a Quest 3 over Virtual Desktop / SteamVR; should work with any SteamVR-compatible headset.

> **Status: playable work-in-progress.** Chapter 1 is playable start to finish in VR with motion-controller aiming. Known issues: a crash can occur at chapter transitions (mitigation shipped, under verification), and performance still wants tuning on mid-range GPUs. See [Known issues](#known-issues).

## Features

- **True stereo 3D** — both eyes rendered by HDRP in multipass, not a flat screen
- **6DOF head tracking** — rotate and lean freely; the game's rail/cutscene camera motion composes underneath your head movement
- **Motion-controller lightgun** — your controller is the gun: red laser + 3D reticle, shots land where you point
- **Classic off-screen reload** — point away from the scene and pull the trigger, arcade style (grip also reloads)
- **Full controller mapping** — gameplay and menus playable entirely from Touch controllers
- **VR comfort** — camera shake, hit recoil, and FOV zoom punches disabled in VR
- **UI in the headset** — menus and HUD projected into view
- **Performance controls** — render scale + HDRP feature reducers (SSR, contact shadows, volumetrics, SSAO)

## Controls (Quest 3 Touch, right-handed default)

| Control | Action |
|---|---|
| Trigger (aim hand) | Shoot |
| Grip (aim hand) | Reload |
| Point off-screen + Trigger | Reload (arcade gesture) |
| A | Accept / Skip cutscene (hold) / Revive |
| B | Cancel / Buy continue token |
| Stick click (aim hand) | Next weapon |
| X (off hand) | Flashlight |
| Y (off hand) | Pause |
| Stick (off hand) | Menu navigation |
| Click + hold off-hand stick, push up/down | Adjust aim tilt live (auto-saved) |
| F10 (keyboard) | Recenter view |
| F9 (keyboard) | Start VR manually (when `AutoStartVR = false`) |

Set `LeftHanded = true` in the config to swap hands.

## Installation

### Requirements

- THE HOUSE OF THE DEAD Remake (Steam, Windows)
- SteamVR installed (Virtual Desktop, Quest Link, or any SteamVR-compatible headset connection)
- [.NET SDK 8+](https://dotnet.microsoft.com/download) if building from source

### Steps

1. **Install BepInEx 5.4.23.5 (win x64)** into the game folder:
   - Download `BepInEx_win_x64_5.4.23.5.zip` from the [official BepInEx releases](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
   - Extract it directly into `...\steamapps\common\THE HOUSE OF THE DEAD Remake\` (so `winhttp.dll` sits next to the game exe)
2. **Build the mod** (from this repository root):
   ```powershell
   dotnet build src/HotdVR/HotdVR.csproj -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\THE HOUSE OF THE DEAD Remake"
   ```
   `GamePath` must point at your install; the build compiles the mod plus the managed XR assemblies.
3. **Run the installer script** — places the OpenVR XR runtime files and the plugin into the game:
   ```powershell
   powershell -ExecutionPolicy Bypass -File install/install.ps1 -GamePath "D:\SteamLibrary\steamapps\common\THE HOUSE OF THE DEAD Remake"
   ```
4. **Play**: connect your headset (Virtual Desktop → SteamVR), launch the game from Steam. VR engages automatically a moment after boot.

To uninstall: `install/uninstall.ps1` (add `-IncludeBepInEx` to remove BepInEx too). The game is untouched on disk otherwise — with the mod removed (or `VREnabled = false`), it runs exactly as vanilla.

## Configuration

`BepInEx/config/com.hotdremake.vrmod.cfg` (created on first run):

| Setting | Default | Notes |
|---|---|---|
| `VREnabled` | true | Master switch; false = fully flat/vanilla |
| `AutoStartVR` | true | false = start VR with F9 instead |
| `ProjectUiToVR` | true | Menus/HUD visible in headset |
| `LeftHanded` | false | Aim with left controller |
| `ShowLaser` | true | Laser + reticle visuals |
| `AimPitchOffset` | 45 | Downward aim tilt in degrees; adjust live in-game (see controls) |
| `RenderScale` | 1.0 | Eye resolution scale; **0.75 recommended on mid-range GPUs** |
| `DisableSSR` | true | Screen-space reflections off in VR |
| `DisableContactShadows` | true | Contact shadows off in VR |
| `DisableVolumetrics` | false | true = big GPU win, loses fog atmosphere |
| `DisableSSAO` | false | true = moderate GPU win |

**Performance tips**: lower `RenderScale` first, then Virtual Desktop's resolution preset, then try `DisableVolumetrics = true`. Turn **motion blur off** in the game's own Quality settings (strongly recommended in VR).

## Known issues

- **Chapter-transition crash**: after finishing a chapter, the game may crash while loading the next one. A mitigation (suspending VR rendering during load screens) is in place — if it still occurs, restart and use Continue; progress is kept per chapter.
- **Performance**: HDRP renders each eye separately (multipass), which is heavy. See the performance tips above.
- **Cutscene cameras**: cutscenes play with the game's authored camera; brief hard cuts are inherent to them.
- **2-player co-op** is untouched/flat — VR drives player 1 only.

## Troubleshooting

- **Logs** (attach these when reporting problems):
  - `<game>\BepInEx\LogOutput.log` — mod + VR diagnostics (subsystem status, render health counters, controller input)
  - `%USERPROFILE%\AppData\LocalLow\MegaPixel Studio S.A_\The House of the Dead Remake\Player.log` — engine log, crash stack traces
- **Game runs flat, no VR**: check `LogOutput.log` for `[VR]` lines. `EVRInitError` there means SteamVR wasn't reachable — make sure the headset session is up (VD connected) before launching, or start SteamVR first.
- **Black screen in headset but game visible on desktop**: check `renderHealth` lines in the log — a high `skipped` count means the runtime is delivering broken poses; restart SteamVR.
- **Buttons do nothing**: the log prints `[VRCtl]` button state lines every few seconds and `[VRCtl/RAW ...]` on every press — if presses show there but the game ignores them, report it; if nothing shows, the controllers aren't reaching SteamVR (check VD).
- **Crash on level load**: see Known issues. The log's last `[VRGate]`/`[HdrpDiag]` lines before the stack trace tell the story.
- **VR everything looks tilted/wrong height**: press F10 while facing forward comfortably.

## How it works (short)

The game is Unity 2020.3 (Mono) + HDRP. The mod loads via BepInEx, injects Valve's OpenVR XR plugin into the built game at runtime (subsystem manifest + native provider + a settings file the provider reads), and brings up Unity's XR stack manually — no serialized XR assets needed. HDRP then renders multipass stereo on its own. Harmony patches: keep exactly one camera feeding the headset, repair the NaN culling matrices the provider produces for the right eye, apply the HMD pose on top of the game's authored camera, swap the fire ray for the controller's, and bridge controller buttons in at the Rewired input seam. Full details in [docs/DEVNOTES.md](docs/DEVNOTES.md).

## License

MIT for this repository's code — see [LICENSE](LICENSE). Third-party components (BepInEx LGPL-2.1, Valve OpenVR XR Plugin BSD-style, Unity XR Management under the Unity Companion License) retain their licenses — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The game and its content belong to SEGA / Forever Entertainment / MegaPixel Studio; this mod redistributes no game files.
