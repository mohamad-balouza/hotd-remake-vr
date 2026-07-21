# Roadmap

Status after the initial sessions (2026-07-21): Chapter 1 fully playable in VR —
stereo, 6DOF, motion-controller aiming, all buttons, menus navigable, comfort
patches in. Repo builds + auto-deploys; docs in place.

## 1. Stability — chapter transitions (NEXT, highest priority)

- The game crashed (native `ScriptableRenderContext.Submit`) when loading
  chapter 2 after finishing chapter 1. Root-cause insight (2026-07-21): the
  original presence-only gate resumed XR every other frame during loads
  (`camera_Loading` A/B set alternation) — see DEVNOTES. Shipped fix: 3-state
  gate with a `Stability.LoadingGraceSeconds` (1.5s realtime) suspension tail,
  plus resume diagnostics (enriched `[VRGate]` lines + 120-frame un-throttled
  window) so any remaining crash is attributable from the log tail.
- Headless-verified across the boot load (suspend → grace → resume with a
  reloaded camera identity). **Verify**: user plays chapter 1 → 2. If it
  still crashes: bump the grace duration, suspend during `HD_Cutscene` starts
  (needs decompile — no reference in src yet), or validate the XR pass
  renderTarget pre-submit (promote `ExecutePre` to a skip-capable bool prefix
  modeled on `TryCullValidatePre`).
- Also worth testing: game over → continue, returning to main menu from a
  chapter, photo mode, bestiary/gallery.

## 2. Performance (instrumented; read numbers from the next playthrough)

- Measurement shipped 2026-07-21: `[VR/stateN]` emits `frametime(cpu)` and
  `render(cpu-submit)` p50/p95/p99/max every 15s (`Debug.FrameTimeStats`).
  Budget at VD 72Hz = 13.9ms. Read the user's chapter-run log before touching
  any lever; if p95 fits the budget, skip the levers entirely.
- Current levers: `RenderScale` (biggest), DisableSSR/ContactShadows (default
  on), DisableVolumetrics/SSAO (opt-in), VD resolution preset, motion blur off
  in game settings.
- Candidates: shadow resolution/distance caps via volume overrides, LOD bias,
  reducing HDRP color buffer format, `XRSettings.renderViewportScale` dynamic
  scaling, capping refresh (72Hz in VD), reflection probe update throttling.

## 3. UI polish (M6)

- HUD (score/ammo/health) currently sits on a 1m camera-locked plane via
  camera-space conversion — functional but basic. Improve: world-space panel
  with smoothing (lazy-follow), configurable distance/size/curvature, maybe
  split HUD elements (score top, ammo near the gun hand).
- **Gun model in hand + laser toggle belong here**: a simple gun mesh (or the
  game's own weapon models if extractable from addressables) attached to the
  aim hand; laser on/off togglable in-game (e.g. long-press stick click) and in
  config. Reticle-only mode as a middle ground.
- Menu interaction: laser-pointer UI clicking would beat stick navigation
  (uGUI raycaster on the controller ray).
- Subtitle/dialog placement during cutscenes.

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
