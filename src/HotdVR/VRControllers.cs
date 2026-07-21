using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;

namespace HotdVR
{
    /// <summary>
    /// Reads Quest/Touch controller state each frame (poses + buttons with edge
    /// detection), computes the world-space aim ray of the gun hand and renders
    /// a laser pointer with a 3D reticle at the aim hit point.
    /// </summary>
    [DefaultExecutionOrder(30001)] // after VRCameraRig published the base pose
    public class VRControllers : MonoBehaviour
    {
        // --- shared state consumed by patches ---
        internal static bool AimValid;
        internal static Ray AimRay;                 // world space
        internal static Quaternion AimWorldRot;     // full aim rotation incl. pitch tilt (for the gun model)
        internal static Vector3 AimHitPoint;        // world space
        internal static Vector2 AimViewport;        // 0..1 when on screen
        internal static bool AimOnScreen;

        internal static bool TriggerHeld, TriggerDown, TriggerUp;
        internal static bool GripHeld, GripDown;
        internal static bool AHeld, ADown;
        internal static bool BHeld, BDown;
        internal static bool XHeld, XDown;
        internal static bool YHeld, YDown;
        internal static bool StickClickDown;
        internal static bool MenuDown;
        internal static Vector2 LeftStick;
        internal static bool StickUpDown, StickDownDown, StickLeftDown, StickRightDown;

        private bool prevTrigger, prevGrip, prevA, prevB, prevX, prevY, prevStickClick, prevMenu;
        private Vector2 prevStick;

        private LineRenderer laser;
        private Transform reticle;
        private Material laserMaterial;

        private float nextDiagLog;
        private bool loggedSource;

        private void Update()
        {
            if (!VRRuntimeBootstrap.Active)
                return;

            bool leftHanded = Plugin.Cfg.LeftHanded.Value;
            var aimHand = InputDevices.GetDeviceAtXRNode(leftHanded ? XRNode.LeftHand : XRNode.RightHand);
            var offHand = InputDevices.GetDeviceAtXRNode(leftHanded ? XRNode.RightHand : XRNode.LeftHand);

            // Source 1: Unity XR input features (may be unavailable in the
            // provider's legacy input mode - poses work but buttons may not).
            aimHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            aimHand.TryGetFeatureValue(CommonUsages.gripButton, out bool grip);
            aimHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
            aimHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
            offHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool x);
            offHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool y);
            offHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
            offHand.TryGetFeatureValue(CommonUsages.menuButton, out bool menu);
            aimHand.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickClick);
            aimHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 aimStickXr);
            bool xrAny = trigger || grip || a || b || x || y || menu || stickClick || stick.sqrMagnitude > 0.04f;

            // Source 2: raw OpenVR legacy controller state - works whenever the
            // runtime is up (same pipe the poses come from).
            rawAimStick = Vector2.zero;
            rawOffStick = Vector2.zero;
            rawOffStickClick = false;
            if (ReadRawOpenVR(leftHanded, out var raw))
            {
                rawAimStick = raw.aimStick;
                rawOffStick = raw.stick;
                rawOffStickClick = raw.offStickClick;
                trigger |= raw.trigger; grip |= raw.grip; a |= raw.a; b |= raw.b;
                x |= raw.x; y |= raw.y; stickClick |= raw.stickClick; menu |= raw.menu;
                if (stick.sqrMagnitude < 0.04f && raw.stick.sqrMagnitude >= 0.04f)
                    stick = raw.stick;
                if (!loggedSource && (raw.trigger || raw.grip || raw.a || raw.b))
                {
                    loggedSource = true;
                    Plugin.Log.LogInfo($"[VRCtl] raw OpenVR input active (XR features {(xrAny ? "also" : "NOT")} delivering)");
                }
            }
            if (!loggedSource && xrAny)
            {
                loggedSource = true;
                Plugin.Log.LogInfo("[VRCtl] XR InputDevices features delivering input");
            }
            if (Plugin.Cfg.VerboseLogging.Value && Time.realtimeSinceStartup > nextDiagLog)
            {
                nextDiagLog = Time.realtimeSinceStartup + 5f;
                Plugin.Log.LogInfo($"[VRCtl] aimValid={aimHand.isValid} trig={trigger} grip={grip} a={a} b={b} x={x} y={y} stick={stick} click={stickClick} menu={menu}");
            }

            TriggerHeld = trigger; TriggerDown = trigger && !prevTrigger; TriggerUp = !trigger && prevTrigger;
            GripHeld = grip; GripDown = grip && !prevGrip;
            AHeld = a; ADown = a && !prevA;
            BHeld = b; BDown = b && !prevB;
            XHeld = x; XDown = x && !prevX;
            YHeld = y; YDown = y && !prevY;
            StickClickDown = stickClick && !prevStickClick;
            MenuDown = menu && !prevMenu;

            // The off-hand thumbstick AXIS is dead in some runtimes (VD's
            // legacy state delivers its buttons but rAxis0 stays zero, seen
            // 2026-07-21) - menus were unnavigable. Use whichever stick shows
            // the larger deflection: the Nav/Page actions are only read by
            // menu screens, so the aim stick doubles as a nav stick safely.
            Vector2 navStick = stick;
            if (rawAimStick.sqrMagnitude > navStick.sqrMagnitude) navStick = rawAimStick;
            if (aimStickXr.sqrMagnitude > navStick.sqrMagnitude) navStick = aimStickXr;
            LeftStick = navStick;
            const float on = 0.6f;
            StickUpDown = navStick.y > on && prevStick.y <= on;
            StickDownDown = navStick.y < -on && prevStick.y >= -on;
            StickLeftDown = navStick.x < -on && prevStick.x >= -on;
            StickRightDown = navStick.x > on && prevStick.x <= on;

            prevTrigger = trigger; prevGrip = grip; prevA = a; prevB = b; prevX = x; prevY = y;
            prevStickClick = stickClick; prevMenu = menu; prevStick = navStick;

            // Held-adjust gestures and the laser toggle also use the merged
            // stick, so they keep working when the off-hand axis is dead.
            UpdateHeldAdjust(navStick, rawOffStickClick);
            UpdateLaserToggle(navStick, rawOffStickClick);
        }

        // Laser on/off: hold the off-hand stick click 0.6s with the stick
        // CENTERED (deflecting cancels - that combination belongs to the aim
        // tilt adjust). F8 as keyboard fallback. Persists via the config entry.
        private HoldGesture laserToggleHold;

        private void UpdateLaserToggle(Vector2 offStick, bool offStickClicked)
        {
            bool cancel = offStick.magnitude > 0.3f;
            bool fired = laserToggleHold.Update(offStickClicked, cancel, 0.6f, Time.realtimeSinceStartup);
            if (fired || Input.GetKeyDown(KeyCode.F8))
            {
                bool show = !Plugin.Cfg.ShowLaser.Value;
                Plugin.Cfg.ShowLaser.Value = show; // persists to cfg
                Plugin.Log.LogInfo($"[VRCtl] laser {(show ? "enabled" : "disabled")} (saved to config)");
            }
        }

        /// <summary>Fires exactly once when 'held' has been continuously true
        /// for 'seconds' with 'cancel' never true during the hold; releasing
        /// re-arms. Reusable for future long-press bindings.</summary>
        private struct HoldGesture
        {
            private float downSince;
            private bool fired, canceled;

            public bool Update(bool held, bool cancel, float seconds, float now)
            {
                if (!held)
                {
                    downSince = 0f;
                    fired = false;
                    canceled = false;
                    return false;
                }
                if (downSince == 0f)
                    downSince = now;
                if (cancel)
                    canceled = true;
                if (fired || canceled || now - downSince < seconds)
                    return false;
                fired = true;
                return true;
            }
        }

        private Vector2 rawAimStick;
        private Vector2 rawOffStick;
        private bool rawOffStickClick;

        private struct RawButtons
        {
            public bool trigger, grip, a, b, x, y, stickClick, menu;
            public bool offStickClick;
            public Vector2 stick;
            public Vector2 aimStick;
        }

        // Live-adjust gestures, both gated on holding the off-hand stick
        // click (or keyboard): dominant stick axis picks the parameter -
        //   Y (up/down)    -> aim tilt (also PageUp/PageDown)
        //   X (left/right) -> gun model forward/back (also Home/End)
        // Committed to the config file on release. The click-gate keeps them
        // from colliding with menu navigation on the same stick.
        internal static float LivePitch = float.NaN;
        internal static float LiveGunZ = float.NaN;
        private bool pitchDirty, gunZDirty;

        private void UpdateHeldAdjust(Vector2 adjStick, bool offStickClicked)
        {
            if (float.IsNaN(LivePitch))
                LivePitch = Plugin.Cfg.AimPitchOffset.Value;
            if (float.IsNaN(LiveGunZ))
                LiveGunZ = Mathf.Clamp(Plugin.Cfg.GunModelZOffset.Value, -0.2f, 0.2f);

            bool yDominant = Mathf.Abs(adjStick.y) >= Mathf.Abs(adjStick.x);
            float pitchDelta = 0f, gunDelta = 0f;
            if (offStickClicked && yDominant && Mathf.Abs(adjStick.y) > 0.5f)
                pitchDelta = -adjStick.y * 20f * Time.unscaledDeltaTime; // stick up = aim higher = less tilt
            if (offStickClicked && !yDominant && Mathf.Abs(adjStick.x) > 0.5f)
                gunDelta = -adjStick.x * 0.08f * Time.unscaledDeltaTime; // stick left = pull gun back
            if (Input.GetKey(KeyCode.PageDown)) pitchDelta += 20f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.PageUp)) pitchDelta -= 20f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.Home)) gunDelta += 0.08f * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.End)) gunDelta -= 0.08f * Time.unscaledDeltaTime;

            if (pitchDelta != 0f)
            {
                LivePitch = Mathf.Clamp(LivePitch + pitchDelta, -10f, 80f);
                pitchDirty = true;
            }
            else if (pitchDirty)
            {
                pitchDirty = false;
                LivePitch = Mathf.Round(LivePitch * 2f) / 2f;
                Plugin.Cfg.AimPitchOffset.Value = LivePitch; // persists to cfg
                Plugin.Log.LogInfo($"[VRCtl] AimPitchOffset saved: {LivePitch:F1}");
            }

            if (gunDelta != 0f)
            {
                LiveGunZ = Mathf.Clamp(LiveGunZ + gunDelta, -0.2f, 0.2f);
                gunZDirty = true;
            }
            else if (gunZDirty)
            {
                gunZDirty = false;
                LiveGunZ = Mathf.Round(LiveGunZ * 200f) / 200f; // 5mm steps
                Plugin.Cfg.GunModelZOffset.Value = LiveGunZ; // persists to cfg
                Plugin.Log.LogInfo($"[VRCtl] GunModelZOffset saved: {LiveGunZ:F3}");
            }
        }

        private static ulong lastAimMask, lastOffMask;
        private static float lastAxisLog;

        // Legacy Oculus Touch mapping via SteamVR: trigger=SteamVR_Trigger(33),
        // grip=Grip(2), A/X=A(7), B/Y=ApplicationMenu(1), stick=axis0 with
        // Touchpad(32) as the click.
        private static bool ReadRawOpenVR(bool leftHanded, out RawButtons result)
        {
            result = default;
            var system = OpenVR.System;
            if (system == null)
                return false;

            uint aimIndex = system.GetTrackedDeviceIndexForControllerRole(
                leftHanded ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand);
            uint offIndex = system.GetTrackedDeviceIndexForControllerRole(
                leftHanded ? ETrackedControllerRole.RightHand : ETrackedControllerRole.LeftHand);

            uint size = (uint)Marshal.SizeOf(typeof(VRControllerState_t));
            VRControllerState_t state = default;
            bool any = false;

            if (aimIndex != OpenVR.k_unTrackedDeviceIndexInvalid && system.GetControllerState(aimIndex, ref state, size))
            {
                ulong pressed = state.ulButtonPressed;
                result.trigger = (pressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger)) != 0 || state.rAxis1.x > 0.75f;
                result.grip = (pressed & (1UL << (int)EVRButtonId.k_EButton_Grip)) != 0 || state.rAxis2.x > 0.75f;
                result.a = (pressed & (1UL << (int)EVRButtonId.k_EButton_A)) != 0;
                result.b = (pressed & (1UL << (int)EVRButtonId.k_EButton_ApplicationMenu)) != 0;
                result.stickClick = (pressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Touchpad)) != 0;
                result.aimStick = new Vector2(state.rAxis0.x, state.rAxis0.y);
                any = true;

                // Discovery logging: every press/release logs the exact raw mask
                // + axes so the active runtime's legacy layout can be mapped.
                if (pressed != lastAimMask || (Time.realtimeSinceStartup - lastAxisLog > 3f && (state.rAxis1.x > 0.05f || state.rAxis2.x > 0.05f || Mathf.Abs(state.rAxis0.x) + Mathf.Abs(state.rAxis0.y) > 0.1f)))
                {
                    lastAimMask = pressed;
                    lastAxisLog = Time.realtimeSinceStartup;
                    Plugin.Log.LogInfo($"[VRCtl/RAW aim] pressed=0x{pressed:X} touched=0x{state.ulButtonTouched:X} ax0=({state.rAxis0.x:F2},{state.rAxis0.y:F2}) ax1=({state.rAxis1.x:F2}) ax2=({state.rAxis2.x:F2}) ax3=({state.rAxis3.x:F2}) ax4=({state.rAxis4.x:F2})");
                }
            }
            state = default;
            if (offIndex != OpenVR.k_unTrackedDeviceIndexInvalid && system.GetControllerState(offIndex, ref state, size))
            {
                ulong pressed = state.ulButtonPressed;
                result.x = (pressed & (1UL << (int)EVRButtonId.k_EButton_A)) != 0;
                result.y = (pressed & (1UL << (int)EVRButtonId.k_EButton_ApplicationMenu)) != 0;
                result.offStickClick = (pressed & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Touchpad)) != 0;
                result.stick = new Vector2(state.rAxis0.x, state.rAxis0.y);
                any = true;

                if (pressed != lastOffMask || (Time.realtimeSinceStartup - lastAxisLog > 2f && result.stick.sqrMagnitude > 0.09f))
                {
                    lastOffMask = pressed;
                    lastAxisLog = Time.realtimeSinceStartup;
                    // ax3/ax4 included: VD delivered a permanently-zero off-hand
                    // rAxis0 (2026-07-21) - if the stick lives on another axis,
                    // these lines will reveal it.
                    Plugin.Log.LogInfo($"[VRCtl/RAW off] pressed=0x{pressed:X} touched=0x{state.ulButtonTouched:X} ax0=({state.rAxis0.x:F2},{state.rAxis0.y:F2}) ax1=({state.rAxis1.x:F2}) ax2=({state.rAxis2.x:F2}) ax3=({state.rAxis3.x:F2},{state.rAxis3.y:F2}) ax4=({state.rAxis4.x:F2},{state.rAxis4.y:F2})");
                }
            }
            return any;
        }

        private void LateUpdate()
        {
            AimValid = false;
            if (!VRRuntimeBootstrap.Active || !VRCameraRig.HasBase)
            {
                SetLaserVisible(false);
                return;
            }

            var aimHand = InputDevices.GetDeviceAtXRNode(
                Plugin.Cfg.LeftHanded.Value ? XRNode.LeftHand : XRNode.RightHand);
            if (!aimHand.isValid ||
                !aimHand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) ||
                !aimHand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            {
                SetLaserVisible(false);
                return;
            }

            Vector3 worldPos = VRCameraRig.TrackingToWorldPos(pos);
            Quaternion worldRot = VRCameraRig.TrackingToWorldRot(rot);
            // Tilt the aim axis down a little from the controller's forward so
            // it points like a pistol barrel rather than along the flat top.
            float pitch = float.IsNaN(LivePitch) ? Plugin.Cfg.AimPitchOffset.Value : LivePitch;
            AimWorldRot = worldRot * Quaternion.Euler(pitch, 0f, 0f);
            Vector3 dir = AimWorldRot * Vector3.forward;
            AimRay = new Ray(worldPos, dir);
            AimValid = true;

            AimHitPoint = Physics.Raycast(AimRay, out var hit, 500f)
                ? hit.point
                : AimRay.origin + AimRay.direction * 500f;

            var cam = VRCameraGate.CurrentVRCamera != null ? VRCameraGate.CurrentVRCamera : Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(AimHitPoint);
                AimOnScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
                AimViewport = vp;
            }

            UpdateLaser();
        }

        private void UpdateLaser()
        {
            if (!Plugin.Cfg.ShowLaser.Value)
            {
                SetLaserVisible(false);
                return;
            }
            if (laser == null)
                CreateLaser();

            laser.enabled = true;
            laser.SetPosition(0, AimRay.origin);
            laser.SetPosition(1, AimHitPoint);
            reticle.gameObject.SetActive(true);
            reticle.position = AimHitPoint;
            float dist = Vector3.Distance(AimRay.origin, AimHitPoint);
            reticle.localScale = Vector3.one * Mathf.Clamp(dist * 0.012f, 0.01f, 0.6f);
        }

        private void SetLaserVisible(bool visible)
        {
            if (laser != null) laser.enabled = visible;
            if (reticle != null) reticle.gameObject.SetActive(visible);
        }

        private void CreateLaser()
        {
            var go = new GameObject("HotdVR_Laser");
            DontDestroyOnLoad(go);
            laser = go.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.startWidth = 0.004f;
            laser.endWidth = 0.002f;
            laser.useWorldSpace = true;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;

            var shader = Shader.Find("HDRP/Unlit");
            laserMaterial = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            var color = new Color(1f, 0.15f, 0.1f, 0.9f);
            laserMaterial.color = color;
            if (laserMaterial.HasProperty("_UnlitColor"))
                laserMaterial.SetColor("_UnlitColor", color);
            laser.material = laserMaterial;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "HotdVR_Reticle";
            Destroy(sphere.GetComponent<Collider>());
            DontDestroyOnLoad(sphere);
            sphere.GetComponent<MeshRenderer>().material = laserMaterial;
            reticle = sphere.transform;
            Plugin.Log.LogInfo("[VRCtl] laser + reticle created");
        }
    }
}
