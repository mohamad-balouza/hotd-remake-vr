using System.Collections.Generic;
using UnityEngine;

namespace HotdVR
{
    /// <summary>
    /// World-space lazy-follow anchor for HUD canvases (HudMode.WorldFollow).
    /// The anchor pose derives from VRCameraRig's published BASE pose - the
    /// game-authored camera pose (the ride vehicle in this rail shooter), not
    /// the post-HMD head pose - so the HUD behaves like a cockpit panel: it
    /// stays put when the player looks around and only chases the game's own
    /// camera motion. Yaw-only, with a deadzone + hysteresis so small rail
    /// wiggles don't wobble the panel. Canvas transforms are driven directly,
    /// never reparented (game scripts keep owning their objects).
    /// </summary>
    [DefaultExecutionOrder(30002)] // after VRCameraRig (30000) published the base
    public class VRHudAnchor : MonoBehaviour
    {
        private static readonly List<RectTransform> entries = new List<RectTransform>();

        private static Vector3 anchorPos;
        private static float anchorYaw;
        private static bool hasPose;
        private static bool easing;

        /// <summary>Converts the canvas to world space, scaled so it covers the
        /// same apparent field of view the camera-space version had at the same
        /// distance, and starts driving its transform.</summary>
        internal static void Register(Canvas canvas, Camera cam)
        {
            var rt = canvas.transform as RectTransform;
            if (rt == null || rt.rect.height <= 0f)
                return; // layout not ready yet - the 1s re-scan will retry

            for (int i = 0; i < entries.Count; i++)
                if (ReferenceEquals(entries[i], rt))
                    return;

            float dist = Mathf.Clamp(Plugin.Cfg.HudDistance.Value, 0.5f, 3f);
            float scale = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / rt.rect.height
                          * Mathf.Clamp(Plugin.Cfg.HudScale.Value, 0.2f, 3f);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam; // event camera for uGUI raycasters
            rt.localScale = Vector3.one * scale;
            entries.Add(rt);
            Plugin.Log.LogInfo($"[VRUi] canvas '{canvas.name}' -> world space (dist={dist:F1}m scale={scale:E2})");
        }

        /// <summary>True (and removed) if this canvas was one of ours - used by
        /// the projector to revert only mod-converted canvases, never canvases
        /// that are natively world-space.</summary>
        internal static bool Unregister(Canvas canvas)
        {
            var rt = canvas.transform as RectTransform;
            for (int i = 0; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i], rt))
                {
                    entries.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        private void LateUpdate()
        {
            if (!VRRuntimeBootstrap.Active)
                return;

            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i] == null)
                    entries.RemoveAt(i); // scene unloads destroy canvases

            if (entries.Count == 0)
            {
                hasPose = false; // next chapter snaps instead of easing across the map
                return;
            }
            if (Plugin.Cfg.HudMode.Value != HudMode.WorldFollow || !VRCameraRig.HasBase)
                return;

            Vector3 basePos = VRCameraRig.BaseWorldPos;
            if (!IsFinite(basePos))
                return; // headless null-driver poses can be NaN
            Vector3 fwd = VRCameraRig.BaseWorldRot * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f)
                return; // authored camera looking straight up/down - hold pose
            float targetYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            float k = 1f - Mathf.Exp(-Mathf.Clamp(Plugin.Cfg.HudFollowSpeed.Value, 0.5f, 20f) * Time.unscaledDeltaTime);
            float dist = Mathf.Clamp(Plugin.Cfg.HudDistance.Value, 0.5f, 3f);

            if (!hasPose)
            {
                hasPose = true;
                easing = false;
                anchorYaw = targetYaw;
                anchorPos = basePos + Quaternion.Euler(0f, anchorYaw, 0f) * new Vector3(0f, -0.1f, dist);
            }
            else
            {
                // Yaw: deadzone with hysteresis - start easing when the vehicle
                // heading drifts past the deadzone, keep easing until caught up.
                float deadzone = Mathf.Clamp(Plugin.Cfg.HudYawDeadzoneDegrees.Value, 0f, 90f);
                if (Mathf.Abs(Mathf.DeltaAngle(anchorYaw, targetYaw)) > deadzone)
                    easing = true;
                if (easing)
                {
                    anchorYaw = Mathf.LerpAngle(anchorYaw, targetYaw, k);
                    if (Mathf.Abs(Mathf.DeltaAngle(anchorYaw, targetYaw)) < 2f)
                        easing = false;
                }
                // Position: always eased so rail movement is followed smoothly.
                Vector3 targetPos = basePos + Quaternion.Euler(0f, anchorYaw, 0f) * new Vector3(0f, -0.1f, dist);
                anchorPos = Vector3.Lerp(anchorPos, targetPos, k);
            }

            var rot = Quaternion.Euler(0f, anchorYaw, 0f);
            foreach (var rt in entries)
                rt.SetPositionAndRotation(anchorPos, rot);
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}
