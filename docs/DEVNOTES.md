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
3. **Chapter transitions** (camera churn while loading) — crashed at the frame
   the next chapter's `cam_MainCamera` appeared. Mitigation: XR passes fully
   suspended while a `camera_Loading` camera is present
   (`VRCameraGate.LoadingScreenActive`). Shipped, NOT yet verified by a user
   playthrough across a transition.
- SteamVR **null driver** produces permanently-NaN poses → all XR renders are
  guard-skipped → black eyes headless. Artifact of the null driver only.

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
  GetButtonUp/GetNegativeButtonDown(int)` + `GetAxis(int)`; gameplay actions
  gated to player id 0, menu actions to id 0 + System. All confirmed working
  in-game.
- Comfort: `HD_PlayerCamera` Shake/ApplyHitRecoil/Zoom/StartCameraShake/
  StartCameraZoom no-op'd while VR active.
- UI: `VRUiProjector` converts ScreenSpaceOverlay canvases → ScreenSpaceCamera
  on the VR camera at 1m every second (crosshair canvases excluded+hidden).
  NOTE: camera-space canvases break the game's pixel-space canvas math, which
  is why the crosshair is excluded rather than converted.
- Perf: `FrameSettings.AggregateFrameSettings` postfix strips SSR/contact
  shadows (default) and optionally volumetrics/SSAO while VR active.
  `RenderScale` sets `display.scaleOfAllRenderTargets` before StartSubsystems.

## Diagnostics built into the mod

- `[VR/stateN]` every 15s: renderPasses, eyeTex dims, renderHealth
  (rendered/repaired/skipped counters — skipped spikes = provider pose trouble)
- `[HdrpDiag] camera set changed` on composition changes (A/B flap suppressed)
- `[VRCtl]` button snapshot every 5s + `[VRCtl/RAW]` on every raw mask change
- `EyeCapture` writes `BepInEx\eyecap_{0..3}.png` — readback of the mirror
  source texture = exactly what the compositor receives (right eye)
- `HdrpXrDiag` also carries the culling validation/repair (it is load-bearing,
  not just diagnostics — do not remove wholesale)
