using UnityEngine;
using UnityEngine.XR;

namespace HotdVR
{
    /// <summary>
    /// 6DOF head tracking. The subsystem manifest disables Unity's legacy
    /// implicit camera tracking (disablesLegacyVr), and the provider's per-eye
    /// render matrices are head-relative only - so the HMD pose must be applied
    /// to the camera transform manually, on top of whatever pose the game
    /// (Cinemachine, cutscenes, rail movement) wrote this frame.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public class VRCameraRig : MonoBehaviour
    {
        private int baseFrame = -1;
        private Vector3 basePos;
        private Quaternion baseRot = Quaternion.identity;
        private Camera baseCam;

        private bool recentered;
        private Vector3 recenterPos;
        private Quaternion recenterRotInv = Quaternion.identity;

        // Published so other systems (controllers) can map tracking-space poses
        // into the same world frame the head uses.
        internal static Vector3 BaseWorldPos;
        internal static Quaternion BaseWorldRot = Quaternion.identity;
        internal static Vector3 RecenterPos;
        internal static Quaternion RecenterRotInv = Quaternion.identity;
        internal static bool HasBase;

        internal static Vector3 TrackingToWorldPos(Vector3 trackingPos) =>
            BaseWorldPos + BaseWorldRot * (RecenterRotInv * (trackingPos - RecenterPos));

        internal static Quaternion TrackingToWorldRot(Quaternion trackingRot) =>
            BaseWorldRot * (RecenterRotInv * trackingRot);

        private void OnEnable()
        {
            Application.onBeforeRender += Apply;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= Apply;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                recentered = false;
                Plugin.Log.LogInfo("[VRRig] recenter requested (F10)");
            }
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (!VRRuntimeBootstrap.Active)
                return;
            var cam = VRCameraGate.CurrentVRCamera != null ? VRCameraGate.CurrentVRCamera : Camera.main;
            if (cam == null)
                return;

            var device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!device.isValid)
                return;
            if (!device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion hmdRot))
                return;
            device.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 hmdPos);

            // Capture the game's authored camera pose once per frame (first call
            // happens after Cinemachine/cutscene writes, before rendering);
            // later calls in the same frame recompose from the same base.
            if (baseFrame != Time.frameCount || !ReferenceEquals(baseCam, cam))
            {
                baseFrame = Time.frameCount;
                baseCam = cam;
                basePos = cam.transform.position;
                baseRot = cam.transform.rotation;
            }

            if (!recentered)
            {
                recentered = true;
                recenterPos = hmdPos;
                // Only take out the yaw so the horizon stays level.
                var fwd = hmdRot * Vector3.forward;
                fwd.y = 0f;
                recenterRotInv = Quaternion.Inverse(
                    fwd.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(fwd, Vector3.up) : Quaternion.identity);
                Plugin.Log.LogInfo("[VRRig] recentered");
            }

            Quaternion localRot = recenterRotInv * hmdRot;
            Vector3 localPos = recenterRotInv * (hmdPos - recenterPos);

            cam.transform.rotation = baseRot * localRot;
            cam.transform.position = basePos + baseRot * localPos;

            BaseWorldPos = basePos;
            BaseWorldRot = baseRot;
            RecenterPos = recenterPos;
            RecenterRotInv = recenterRotInv;
            HasBase = true;
        }
    }
}
