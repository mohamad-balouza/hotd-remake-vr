# Roadmap

Status after the initial sessions (2026-07-21): Chapter 1 fully playable in VR —
stereo, 6DOF, motion-controller aiming, all buttons, menus navigable, comfort
patches in. Repo builds + auto-deploys; docs in place.

## 1. Stability — chapter transitions

- Root cause #1 of the original native Submit crash: the presence-only gate
  resumed XR every other frame during loads (`camera_Loading` A/B set
  alternation) — see DEVNOTES. Fix: grace-gate
  (`Stability.LoadingGraceSeconds`, 1.5s realtime tail) + resume diagnostics
  (enriched `[VRGate]` lines + 120-frame un-throttled window).
- Root cause #2, found+fixed 2026-07-21 after a round-5 transition crash:
  the cull repair substituted FLAT loading-screen culling params into the
  first stereo pass at resume whenever the provider NaN'd on that exact
  frame — a race, which is why some transitions survived and one didn't.
  Repair is now mode-aware (XR passes repair only from XR cache entries,
  else safe skip) — see DEVNOTES crash class 2. **Verify**: user replays a
  chapter 1 → 2 transition (ideally a couple of times).
- Prompt phase: the PromptPhaseXR experiment CRASHED on a real chapter load
  (co-render frames — see DEVNOTES) and is now default-off + hardened.
  Loads instead fade the SteamVR compositor grid in
  (`Stability.LoadingGridFade`) so the headset shows the void, not black.
  Headless-verified end-to-end 2026-07-21: new game → chapter 1 load →
  Shoot-to-start prompt → shot fired → grace resume → gameplay, no crash.
  Future idea for visible loading screens: OpenVR overlay quad showing the
  loading texture (compositor-side, no game rendering).
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

## 3. UI polish (M6 — round-2 feedback folded in 2026-07-21)

- Round-2 user verdict: gun model good, pause menu good, laser toggle works,
  transition holds, perf improved (volumetrics+SSAO off).
- FIXED after round 2: menu stick-nav (off-hand axis dead under VD → nav
  merges both sticks); invisible chapter HUD (game resets canvases to
  overlay → world conversion now re-applied; user back on CameraSpace
  default anyway); main menu pushed to `UI.MenuDistance` 2m; gun model
  forward/back live-adjust (`Controls.GunModelZOffset`); loading overlay
  card for shoot-to-continue waits (`Stability.LoadingOverlay`).
- FIXED after round 3 (2026-07-21, decompile-driven): menu stick-nav for
  real this time — the Rewired uGUI input module needs the HELD
  GetButton/GetNegativeButton(int) states alongside GetAxis (see DEVNOTES);
  in-game HUD restored — the crosshair exclusion was hiding the whole
  Generic_IngameUI canvas (health/score), now only the nested crosshair
  subtrees fade; ammo/health pips above the gun (the game's bullet counter
  is an overlay-camera prop rig that can't reach the headset); loading
  overlay swaps to a pull-the-trigger card when the chapter has loaded
  behind the loading screen (camera-stability signal). Round-4 headset
  verification pending.
- WorldFollow HUD stays EXPERIMENTAL: needs a ZTest-Always UI material pass
  (world canvases vanish into corridor geometry) before it can be default.
- Remaining: menu interaction via laser-pointer UI clicking (uGUI raycaster
  on the controller ray); subtitle/dialog placement during cutscenes;
  nicer gun mesh (user request, low priority — runtime OBJ loader for a
  CC0 model, or clone armory `HD_PreviewWeapon` renderers via additive
  scene load; both deferred); possible HUD element splitting.

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
