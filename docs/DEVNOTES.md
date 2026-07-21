# Developer notes — hard-won technical findings

Everything below was established empirically in the initial development
sessions (July 2026). Trust these over intuition; each cost real debugging time.

## Game architecture

- Unity 2020.3.6f1 **Mono** (fully decompilable/patchable), HDRP 10.x
  **custom build with the (experimental in 10.x) render graph enabled**
  (`m_EnableRenderGraph = true` hardcoded). D3D11. Rewired input. Cinemachine
  cameras. FMOD audio. Addressables content.
- Input flows through `HD_InputManager` (singleton) → Rewired `Player`
  objects. Rewired action IDs: Shoot=1 Reload=2 AimX/Y=3/4 NavX/Y=5/6 Accept=9
  Cancel=10 Skip=11 NextWeapon=13 PrevWeapon=14 Pause=16 PageLeft/Right=17/18
  Flashlight=47 WeaponSelection1-5=79-83 Revive=87 BuyToken=88
  CenterCrosshair=89. Players: 0, 1, System=9999999.
- Aiming chain: crosshair UI position (screen px) → `HD_WeaponHolder.FireWeapon`
  → `mainCamera.ScreenPointToRay` → `HD_Weapon.Fire(in Ray)` →
  `handleFiring(ray)`. Crosshair driven by velocity (`MovePositionRaw`) or
  absolute (`SetPosition(normalized)`, gyro path, unclamped).
  Off-screen-reload: `_shotOrReloadWhenOutsideScreen` checks
  `HD_Crosshair.IsInsideScreenSpace()`.
- The game runs SEVERAL Game-type cameras with `targetTexture == null`:
  `Main Camera` (menu), `cam_MainCamera` (chapters), `camera_Loading`,
  `cam_Video`, `cam_Ammo`. During loads it ALTERNATES camera sets per frame
  (`[cam_MainCamera|camera_Loading]` ↔ `[cam_Ammo|cam_Video]`).
- Menu backgrounds/title are fullscreen UI/video on overlay canvases — the 3D
  camera legitimately renders black at the menu. Chapter gameplay is real 3D.
- The game pauses when its window loses focus → mod forces
  `Application.runInBackground = true`.
- Direct exe launch requires `steam_appid.txt` (1694600) next to the exe.

## XR bring-up (what actually works)

- Valve OpenVR XR plugin v1.1.4: native `XRSDKOpenVR.dll` + `openvr_api.dll` in
  `..._Data\Plugins\x86_64\`, manifest in
  `..._Data\UnitySubsystems\XRSDKOpenVR\UnitySubsystemsManifest.json`
  (must exist before launch — engine scans at boot).
- Provider settings come from the plain-text
  `..._Data\StreamingAssets\SteamVR\OpenVRSettings.asset`
  (`StereoRenderingMode: 0` = multipass, `InitializationType: 1`,
  `MirrorView: 2`). **Keys with EMPTY values hang the native parser forever**
  (the game freezes at `Starting Initialize`) — write only valued keys.
- Managed side: `Unity.XR.OpenVR.dll` + `Unity.XR.Management.dll` compiled from
  vendored package sources against the game's own UnityEngine DLLs
  (defines `UNITY_XR_MANAGEMENT;UNITY_STANDALONE_WIN;...`, never UNITY_EDITOR /
  UNITY_INPUT_SYSTEM). Runtime init: CreateInstance of OpenVRSettings /
  XRGeneralSettings / XRManagerSettings / OpenVRLoader → add loader to
  `manager.loaders` → `InitializeLoaderSync()` → `StartSubsystems()`.
  No serialized XR assets needed. Re-register the native tick callback with a
  GC-rooted delegate. Set `display.sRGB = true` before StartSubsystems (HDRP's
  `XRSystemInit` only configures displays that exist at boot).
- `EVRInitError.Init_AnotherAppLaunching` happens after killed sessions —
  retry loop handles it.
- HDRP picks the display up automatically (`RefreshXrSdk` every SetupFrame) and
  runs multipass because the provider exposes per-pass 2D textures.
  `renderPasses=2` in logs = multipass engaged.
- **Do NOT call `HDRenderPipeline.EnableRenderGraph(false)`** — the classic
  path crashes natively in `ScriptableRenderContext.Submit` on chapter load in
  this build.

## Crash class: native Submit crashes (the big one)

All these crash identically in `ScriptableRenderContext.Submit_Internal`:
1. **Two+ cameras taking XR passes in one frame** (double-submit of the eye
   swapchain). Fix: `VRCameraGate` — only `Camera.main` keeps
   `HDAdditionalCameraData.xrRendering = true`, every other camera gets it
   forced false (component added if missing).
2. **Degenerate culling matrices from the provider.** The provider returns NaN
   culling params for the SECOND multipass eye every frame on real hardware
   (steady ~2:1 rendered:skipped before the repair), and for ALL passes during
   load hitches. Fix: `TryCull` prefix validates matrices; invalid ones are
   REPLACED with the same camera's last valid params (≤5 frames old — in
   practice the left eye's, IPD offset only) or the render is skipped.
   Repair = right eye renders; skip = safe black frame.
   **MODE-AWARE since 2026-07-21**: the cache tags entries with whether they
   came from an XR pass (`HDCamera.xr.enabled` via the TryCull `hdCamera`
   param), and an XR pass may only be repaired from an XR entry. Root cause:
   at post-load resume the camera's freshest cache is from its FLAT renders
   during the loading screen; when the provider NaN'd on the exact resume
   frame, the repair substituted mono params into a stereo pass → native
   Submit crash (the "random" chapter-transition crash after the grace gate
   shipped — a race, which is why some transitions survived). No same-mode
   cache → skip (black frame until the provider recovers, typically 1-5
   frames).
3. **Chapter transitions** (camera churn while loading) — crashed at the frame
   the next chapter's `cam_MainCamera` appeared. CRITICAL FINDING (2026-07-21):
   `camera_Loading` is absent from SetupFrame's camera array EVERY OTHER FRAME
   mid-load (the A/B set alternation), so the original presence-only gate
   resumed XR every second frame throughout the load — observed live with
   grace=0. Mitigation now: 3-state gate (Rendering/Suspended/Grace) — the
   suspension only lifts once the loading camera has been gone continuously
   for `Stability.LoadingGraceSeconds` (default 1.5s, realtime; timer restarts
   on every reappearance). **VERIFIED 2026-07-21: user played chapter 1 → 2,
   no crash; both loads show clean suspend → grace → resume onto the new
   chapter camera with renderHealth skipped=0.**
   Prompt phase (CRASHED, now default-off): loading phases contain
   interactive prompts ("Shoot to start") — camera_Loading flaps for the
   whole ~30s wait, so proper suspension shows a black headset there. The
   `PromptResumed` experiment (resume XR mid-load on a 180-frame-stable
   Camera.main) CRASHED on a real chapter load: chapter prompts co-render
   cam_MainCamera WITH camera_Loading every visible frame, and enabling
   xrRendering on such a frame produced a partial XR layout (Execute for
   multipassId=1 with no multipassId=0) and a native Submit crash — the
   diag window caught the exact frame. `Stability.PromptPhaseXR` is now
   default FALSE and hardened (activation additionally needs camera_Loading
   gone 20+ consecutive frames — chapter prompts never satisfy this).
   Instead, suspension fades the SteamVR compositor grid in/out
   (`Stability.LoadingGridFade`, `OpenVR.Compositor.FadeGrid`) AND shows a
   head-locked OpenVR overlay card (`Stability.LoadingOverlay`,
   `VRLoadingOverlay`, `SetOverlayFromFile` with the deployed
   `loading-overlay.png`) telling the player to pull the trigger when the
   load ends — the shoot-to-continue wait is no longer blind. RULE OF
   THUMB: never enable xrRendering on a camera in a frame set that contains
   camera_Loading.
- SteamVR **null driver** (re-tested 2026-07-21): culling params come out
  mostly FINITE (second eye repaired from the first) — XR passes actually
  render and submit headless, so the null driver now exercises the real
  Submit path, better for crash testing than the old "permanently NaN"
  behavior suggested (that behavior did not reproduce). Caveat: cold-starting
  SteamVR via the game can drop the null HMD once ("Device disconnected
  (stopping provider)"), which cleanly quits the game through the OpenVR quit
  event — relaunch; the second boot with vrserver already up is stable.

## Head tracking & controllers

- The subsystem manifest sets `disablesLegacyVr: true` → Unity's implicit
  camera tracking is OFF, and the provider's per-eye view matrices are
  head-relative only → without a driver, the view is head-LOCKED.
  `VRCameraRig` composes the HMD pose onto the game camera's authored pose in
  LateUpdate(order 30000) + onBeforeRender, capturing the game's base pose once
  per frame. Yaw-only recenter (auto on first pose; F10 re-centers).
  `TrackingToWorldPos/Rot` map tracking space → world for other systems.
- **XR InputDevices deliver POSES but NOT buttons** in the provider's legacy
  input mode (no action manifest). Buttons come from raw
  `OpenVR.System.GetControllerState` (legacy state), which works fully under
  Virtual Desktop's rift profile with the STANDARD legacy mapping:
  trigger=bit33+rAxis1.x, grip=bit2+rAxis2.x, A/X=bit7,
  B/Y=bit1(ApplicationMenu), stick=rAxis0 + bit32(Touchpad) as click.
  The real menu/hamburger button is reserved by the SteamVR dashboard → Pause
  lives on off-hand Y.
- `VRControllers` merges XR-features OR raw state, tracks edges, renders the
  laser (LineRenderer, HDRP/Unlit) + reticle sphere at the physics-raycast hit.
  Laser toggle: off-hand stick click held 0.6s with the stick CENTERED
  (deflected click = adjust gestures), or F8; persists via `ShowLaser`.
- **The OFF-hand thumbstick AXIS is dead under VD's rift profile** (found
  2026-07-21): its buttons/click arrive in the legacy state but `rAxis0`
  stays (0,0) — menus were stick-unnavigable. Fix: the nav stick is the
  larger deflection of off-hand and AIM-hand sticks (Nav/Page actions are
  menu-only, so the aim stick doubles safely); the held-adjust gestures use
  the same merged stick. `[VRCtl/RAW off]` now logs ax3/ax4 too in case the
  axis lives elsewhere.
- Held-adjust gestures (off-hand stick click held, dominant axis wins):
  stick Y = aim tilt (PageUp/PageDown), stick X = gun model forward/back
  (Home/End), both auto-saved to config on release
  (`AimPitchOffset` / `GunModelZOffset`).
- `VRGunModel`: procedural primitive gun on the aim pose (the game has no
  player-held weapon meshes in gameplay; armory `HD_PreviewWeapon` models are
  menu-scene-embedded, not addressable). Swaps per-weapon silhouette by
  polling `HD_Player.Player1.WeaponHolder.CurrentWeaponType` (survives scene
  loads). `Controls.ShowGunModel`, forward/back via `GunModelZOffset`.
- Ammo/health pip readout above the gun (`Controls.ShowAmmoReadout`): the
  game's bullet counter is PHYSICAL 3D props (`HD_AmmoController` Rigidbody
  bullets) on the cam_Ammo overlay camera - structurally invisible in VR.
  Pips read `CurrentWeapon.Ammo/MagazineSize/IsReloading` and
  `HD_Player.HealthScript.HealthCurrent` (white=rounds, yellow=reloading,
  red=empty, orange=health). Score singleton if ever needed:
  `HD_ScoreManager.Instance.GetPlayerTotalScore(PlayerType)`.
- In-game 2D HUD = `Generic_IngameUI(Clone)` (`HD_GenericPlayerUI :
  MP_HUDManager`): health torches, score, notifications. Its crosshairs are
  nested children (`prefab_Crosshair_Player1/2`, each an `HD_Crosshair` with
  its own canvas) - hide ONLY those subtrees (CanvasGroup alpha 0, objects
  stay active for the pixel math); disabling the whole canvas kills the HUD
  (the round-3 invisible-HUD bug). Dedicated `canvas_Crosshairs` canvases
  are still hidden wholesale by name.
- Aim ray = controller pose with `AimPitchOffset` (user-tuned, ~45°+) pitched
  down. Live adjust: off-hand stick click held + stick up/down, auto-saved to
  config on release.

## VR gameplay integration

- `HD_Weapon.Fire(in Ray)` prefix (Harmony CAN rewrite `in` byref params) swaps
  in the controller ray — player 1 only, checked via `Owner.ThisPlayer`.
- `HD_Crosshair.IsInsideScreenSpace` postfix → controller-on-screen state
  (keeps the off-screen-reload gesture).
- `HD_InputManager.handleAiming` skipped in VR (kills mouse/gyro crosshair).
- The 2D crosshair canvas is disabled in VR (3D reticle replaces it); its
  pixel math is left intact for flat mode.
- `HD_FlashlightController.getRayToCrosshair` postfix → controller ray.
- `MP_CursorSetterModule.changeCursorVisibility` prefix → cursor never visible
  in VR.
- Input bridge: postfixes on `Rewired.Player.GetButtonDown/GetButton/
  GetButtonUp/GetNegativeButtonDown/GetNegativeButton(int)` + `GetAxis(int)`;
  gameplay actions gated to player id 0, menu actions to id 0 + System.
- MENU NAV MECHANICS (decompiled 2026-07-21): menus run on Rewired's
  `RewiredStandaloneInputModule` (Assembly-CSharp-firstpass). Its move gate
  is a compound AND: `GetAxis(5/6)` non-zero of the right sign AND the HELD
  digital state - `GetButton(int)` for positive, `GetNegativeButton(int)`
  for negative (default `moveOneElementPerAxisPress=false` branch; module
  self-repeats ~10/s). Submit/Cancel are plain `GetButtonDown(9/10)`, which
  is why Accept worked while nav didn't until the held variants were
  bridged. CAVEAT: `MP_MenuNavigationManager` back-navigation registers a
  Rewired `AddInputEventDelegate` (ButtonJustReleased, Cancel) - event
  delegates fire from real device state and CANNOT be reached by Harmony
  postfixes on the polling API; uGUI cancelHandler still works from VR B.
- Comfort: `HD_PlayerCamera` Shake/ApplyHitRecoil/Zoom/StartCameraShake/
  StartCameraZoom no-op'd while VR active.
- UI: `VRUiProjector` converts ScreenSpaceOverlay canvases → ScreenSpaceCamera
  on the VR camera at `UI.HudDistance` every second (crosshair canvases
  excluded+hidden); conversion skipped entirely while XR is suspended.
  NOTE: camera-space canvases break the game's pixel-space canvas math, which
  is why the crosshair is excluded rather than converted.
  Menu-context canvases (vr camera = 'Main Camera') use `UI.MenuDistance`
  (2m) instead of `HudDistance` — fullscreen menu art is less in-your-face.
  `UI.HudMode=WorldFollow` (EXPERIMENTAL, off by default): allowlisted
  chapter HUD canvases (`UI.HudWorldSpaceCanvases`) go WorldSpace on
  `VRHudAnchor` — a yaw-only cockpit anchor on the rig's published base
  (ride) pose with deadzone+hysteresis easing; canvas transforms driven
  directly, never reparented. CAUTION (round-2 lesson): the game RESETS its
  canvases to ScreenSpaceOverlay on UI refreshes — `Register` must re-apply
  the world conversion for known canvases or the HUD sticks in overlay mode
  and is invisible (fixed); remaining known issue: world-space canvases
  z-test against scene geometry and vanish in tight corridors (needs a
  ZTest-Always UI material pass before WorldFollow can be default).
- Perf: `FrameSettings.AggregateFrameSettings` postfix strips SSR/contact
  shadows (default) and optionally volumetrics/SSAO while VR active.
  `RenderScale` sets `display.scaleOfAllRenderTargets` before StartSubsystems.

## Diagnostics built into the mod

- `[VR/stateN]` every 15s: renderPasses, eyeTex dims, renderHealth
  (rendered/repaired/skipped counters — skipped spikes = provider pose trouble)
- `[VR/stateN]` also emits `frametime(cpu)` and `render(cpu-submit)`
  p50/p95/p99/max lines (`FrameStats` ring buffer, 2048 samples; suspended
  loading/grace frames excluded from percentiles but counted as
  `suspendedFrames=`). `render(cpu-submit)` = main-thread time inside
  `ExecuteWithRenderGraph` summed per frame — NOT GPU time. Kill switch:
  `Debug.FrameTimeStats`.
- `[VRGate]` suspend/resume/camera-change lines carry frame number, camera
  identity, camera set and renderHealth. Every gate state change stamps
  `VRCameraGate.LastStateChangeFrame`, opening a 120-frame `InDiagWindow`
  in which the ExecutePre / cull-repair / cull-skip log throttles are
  bypassed and SetupFrame logs a per-frame camera-set timeline (needs
  `VerboseLogging`) — the frames around a transition resume are fully
  attributable from the log tail.
- `[HdrpDiag] camera set changed` on composition changes (A/B flap suppressed)
- `[VRCtl]` button snapshot every 5s + `[VRCtl/RAW]` on every raw mask change
- `EyeCapture` writes `BepInEx\eyecap_{0..3}.png` — readback of the mirror
  source texture = exactly what the compositor receives (right eye)
- `HdrpXrDiag` also carries the culling validation/repair (it is load-bearing,
  not just diagnostics — do not remove wholesale)
