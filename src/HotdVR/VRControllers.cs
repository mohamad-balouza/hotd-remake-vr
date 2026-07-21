using UnityEngine;
using UnityEngine.XR;

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

        private void Update()
        {
            if (!VRRuntimeBootstrap.Active)
                return;

            var aimHand = InputDevices.GetDeviceAtXRNode(
                Plugin.Cfg.LeftHanded.Value ? XRNode.LeftHand : XRNode.RightHand);
            var offHand = InputDevices.GetDeviceAtXRNode(
                Plugin.Cfg.LeftHanded.Value ? XRNode.RightHand : XRNode.LeftHand);

            // Buttons: trigger/grip/primary(A|X)/secondary(B|Y) from the aim
            // hand, stick + menu from the off hand, primary2DAxisClick either.
            aimHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            aimHand.TryGetFeatureValue(CommonUsages.gripButton, out bool grip);
            aimHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool a);
            aimHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
            offHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool x);
            offHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool y);
            offHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
            offHand.TryGetFeatureValue(CommonUsages.menuButton, out bool menu);
            aimHand.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool stickClick);

            TriggerHeld = trigger; TriggerDown = trigger && !prevTrigger; TriggerUp = !trigger && prevTrigger;
            GripHeld = grip; GripDown = grip && !prevGrip;
            AHeld = a; ADown = a && !prevA;
            BHeld = b; BDown = b && !prevB;
            XHeld = x; XDown = x && !prevX;
            YHeld = y; YDown = y && !prevY;
            StickClickDown = stickClick && !prevStickClick;
            MenuDown = menu && !prevMenu;
            LeftStick = stick;
            const float on = 0.6f;
            StickUpDown = stick.y > on && prevStick.y <= on;
            StickDownDown = stick.y < -on && prevStick.y >= -on;
            StickLeftDown = stick.x < -on && prevStick.x >= -on;
            StickRightDown = stick.x > on && prevStick.x <= on;

            prevTrigger = trigger; prevGrip = grip; prevA = a; prevB = b; prevX = x; prevY = y;
            prevStickClick = stickClick; prevMenu = menu; prevStick = stick;
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
            Vector3 dir = worldRot * Quaternion.Euler(Plugin.Cfg.AimPitchOffset.Value, 0f, 0f) * Vector3.forward;
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
