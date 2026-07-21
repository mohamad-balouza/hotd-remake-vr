# CLAUDE.md — working notes for agent sessions

VR mod for THE HOUSE OF THE DEAD Remake (Steam). Read `docs/DEVNOTES.md` for the
technical findings and `docs/ROADMAP.md` for what's next before doing anything.

## Environment facts

- Game: Unity **2020.3.6f1 Mono**, HDRP 10.x (custom, render graph ON), D3D11,
  at `D:\SteamLibrary\steamapps\common\THE HOUSE OF THE DEAD Remake`, AppID 1694600
- `steam_appid.txt` is in the game dir → the exe can be launched directly
  (without it, Steam DRM quits the game a few frames in)
- User hardware: Quest 3 via **Virtual Desktop** (SteamVR), RTX 4060
- No Unity Editor on this machine; .NET SDK 9; `ilspycmd` installed globally

## Build & deploy

```powershell
dotnet build src/HotdVR/HotdVR.csproj -c Release
```
- Auto-deploys `HotdVR.dll` + `Unity.XR.OpenVR.dll` + `Unity.XR.Management.dll`
  to `<game>\BepInEx\plugins\HotdVR\` (DeployToGame target; GamePath default in
  `Directory.Build.props`)
- XR runtime files (native provider, subsystem manifest, OpenVRSettings.asset)
  are placed by `install/install.ps1` — only needed once or after `uninstall.ps1`
- **Kill any running game instance before building** or the copy silently fails
  and the old DLL keeps running (`Get-Process "The House of the Dead Remake" | Stop-Process -Force`)

## Test loop (headless, no headset)

1. Launch exe directly, wait for boot:
   `Start-Process "<game>\The House of the Dead Remake.exe" -WorkingDirectory "<game>"`
2. Read `<game>\BepInEx\LogOutput.log` — all mod diagnostics are there
   (`[VR]`, `[VRGate]`, `[VRCtl]`, `[HdrpDiag]`, `[VRUi]`, `renderHealth`)
3. Crash stacks: `%USERPROFILE%\AppData\LocalLow\MegaPixel Studio S.A_\The House of the Dead Remake\Player.log`
4. Drive menus with `scratchpad` helper scripts (SendKeys; the in-game MOUSE
   CURSOR ignores SetCursorPos — game uses raw deltas; keyboard: Enter=Accept,
   Esc=Back, arrows=nav, but menu highlight is hard to read from screenshots)
5. Window screenshots of the occluded game window: PrintWindow with flag 2
6. SteamVR **null driver** gives a real XR session without a headset. As of
   2026-07-21 its culling params come out mostly FINITE (second eye repaired
   from the first) → XR passes actually render AND submit headless — good for
   crash testing the real Submit path; still useless for visual verification.
   A cold SteamVR start via the game may drop the null HMD once ("Device
   disconnected (stopping provider)") which cleanly quits the game — relaunch;
   the second boot is stable. Enable/disable it by editing
   `C:\Program Files (x86)\Steam\config\steamvr.vrsettings` (ALWAYS back up and
   restore — `forcedDriver: null` left behind breaks Virtual Desktop!)
7. Real rendering/input verification requires the user with the headset —
   batch changes to minimize their test rounds, and instrument logs so their
   session answers questions even when something fails

## Conventions

- Commit + push to `main` at every working increment (user mandate)
- Plain commit messages — NO `Co-Authored-By: Claude` / AI-attribution
  trailers (user mandate, 2026-07-21)
- Never commit decompiled game sources (copyright) — they live in the session
  scratchpad `decomp/` dir; regenerate with
  `ilspycmd <game>\...\Managed\Assembly-CSharp.dll -t <TypeName>`
- All Harmony patches must be inert when `VRRuntimeBootstrap.Active` is false —
  flat mode must stay vanilla
- The user tests with the headset at checkpoints; agent verifies everything
  else from logs
