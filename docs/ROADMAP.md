# Roadmap

Status after the initial sessions (2026-07-21): Chapter 1 fully playable in VR —
stereo, 6DOF, motion-controller aiming, all buttons, menus navigable, comfort
patches in. Repo builds + auto-deploys; docs in place.

## 1. Stability — chapter transitions (VERIFIED 2026-07-21)

- **User playthrough chapter 1 → 2: no crash.** Root cause of the original
  native Submit crash: the presence-only gate resumed XR every other frame
  during loads (`camera_Loading` A/B set alternation) — see DEVNOTES. Fix:
  grace-gate (`Stability.LoadingGraceSeconds`, 1.5s realtime tail) + resume
  diagnostics (enriched `[VRGate]` lines + 120-frame un-throttled window).
- Prompt phase (`Stability.PromptPhaseXR`): "Shoot to start" lives inside
  the loading state; XR resumes mid-load on a 180-frame-stable main camera.
  Shipped, verified headless; user round 2 confirms visually.
- If a transition crash ever resurfaces: bump grace, suspend during
  `HD_Cutscene` starts (needs decompile — no reference in src yet), or
  validate the XR pass renderTarget pre-submit (promote `ExecutePre` to a
  skip-capable bool prefix modeled on `TryCullValidatePre`).
- Still untested: game over → continue, returning to main menu from a
  chapter, photo mode, bestiary/gallery.

## 2. Performance (checkpoint measured 2026-07-21)

- Chapter-1 playthrough numbers: gameplay `frametime(cpu)` p50 ≈ 23–31ms
  (~33–43fps → VD reprojects at 36fps half-rate), `render(cpu-submit)` only
  2–3ms → GPU-bound, not main-thread-bound. Native 72Hz (13.9ms) would need
  ~2×: out of reach of single levers.
- Applied (user cfg, 2026-07-21): DisableVolumetrics=true, DisableSSAO=true.
  Re-read percentiles from the round-2 log to quantify the win.
- Next candidates if still wanted: RenderScale 0.85, shadow resolution/
  distance caps via volume overrides, LOD bias, HDRP color buffer format,
  `XRSettings.renderViewportScale` dynamic scaling, reflection probe update
  throttling.

## 3. UI polish (M6 — mostly shipped 2026-07-21, round-2 verification pending)

- DONE: lazy-follow world-space HUD (`UI.HudMode=WorldFollow`, allowlisted
  chapter canvases on a yaw-deadzone cockpit anchor; distance/scale/speed
  configs). Camera-space remains the default + menu behavior.
- DONE: laser toggle (off-hand stick-click long-press 0.6s centered, or F8;
  persists via ShowLaser).
- DONE: procedural gun model per weapon type (`Controls.ShowGunModel`) —
  the game has no gameplay gun meshes (armory models are menu-scene-only,
  not addressable), so primitives it is. Upgrade path: additively load the
  armory scene and clone `HD_PreviewWeapon` renderers (fragile, deferred).
- Remaining: menu interaction via laser-pointer UI clicking (uGUI raycaster
  on the controller ray); subtitle/dialog placement during cutscenes;
  possible HUD element splitting (score top, ammo near gun hand).

## 4. Release engineering (M7)

- GitHub release v0.1.0 with a prebuilt zip (plugin + XR files + installer that
  downloads BepInEx or bundles it per LGPL) so users don't need the .NET SDK.
- Installer hardening: locate GamePath from Steam library folders automatically.
- README: add screenshots/GIF, tested-hardware matrix.
- Optional: haptics on shoot/hit (OpenVR TriggerHapticPulse), height offset
  config, seated/standing toggle, smooth-turn... none blocking release.

## Parked / known-good decisions

- Multipass stays (SPI needs stereo shader variants the build lacks).
- Render graph stays ON (classic path crashes on load in this custom build).
- The 2D crosshair stays hidden in VR (3D reticle instead) — converting its
  canvas breaks the game's pixel math.
- Astien's closed-source mod exists for this game (Discord-gated) — reference
  point only, user chose an open build.
- 2-player co-op: out of scope; patches must stay inert for player 2 / flat.
