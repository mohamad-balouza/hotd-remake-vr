using System.Collections;
using UnityEngine;

namespace HotdVR
{
    /// <summary>
    /// Makes the game's 2D UI visible in the headset: screen-space-overlay
    /// canvases never reach the XR eye textures, so while VR is active they are
    /// converted to screen-space-camera on the current VR camera. In
    /// HudMode.WorldFollow, allowlisted HUD canvases go to world space instead,
    /// driven by VRHudAnchor's lazy-follow pose - chapters only (the menu's
    /// 'Main Camera' UI stays camera-space for reliable navigation).
    /// </summary>
    public class VRUiProjector : MonoBehaviour
    {
        private string allowlistRaw;
        private string[] allowlist = new string[0];

        private IEnumerator Start()
        {
            var wait = new WaitForSecondsRealtime(1.0f);
            while (true)
            {
                yield return wait;
                if (!VRRuntimeBootstrap.Active || !Plugin.Cfg.ProjectUiToVR.Value)
                    continue;
                ConvertCanvases();
            }
        }

        private void ConvertCanvases()
        {
            // No Camera.main fallback: while XR is suspended (loads) the gate
            // parks CurrentVRCamera at null, and rebinding canvases to whatever
            // camera the churn surfaces just causes rebinding churn.
            var vrCamera = VRCameraGate.CurrentVRCamera;
            if (vrCamera == null)
                return;

            RefreshAllowlist();
            bool worldMode = Plugin.Cfg.HudMode.Value == HudMode.WorldFollow
                             && vrCamera.name == "cam_MainCamera";
            float planeDist = Mathf.Clamp(Plugin.Cfg.HudDistance.Value, 0.5f, 3f);

            foreach (var canvas in FindObjectsOfType<Canvas>())
            {
                // The 2D crosshair is replaced by the 3D laser/reticle: keep its
                // canvas out of the headset entirely (and its pixel math intact).
                if (canvas.GetComponentInChildren<HD_Crosshair>(true) != null)
                {
                    if (canvas.enabled)
                    {
                        canvas.enabled = false;
                        Plugin.Log.LogInfo($"[VRUi] crosshair canvas '{canvas.name}' hidden in VR");
                    }
                    continue;
                }

                if (worldMode && IsHudCanvas(canvas.name))
                {
                    if (canvas.renderMode != RenderMode.WorldSpace)
                        VRHudAnchor.Register(canvas, vrCamera);
                    continue;
                }
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    // Only revert canvases the anchor owns (mode flipped or we
                    // left the chapter) - natively world-space canvases were
                    // never registered and stay untouched.
                    if (VRHudAnchor.Unregister(canvas))
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = vrCamera;
                        canvas.planeDistance = planeDist;
                        Plugin.Log.LogInfo($"[VRUi] canvas '{canvas.name}' -> camera space on '{vrCamera.name}' (world follow off)");
                    }
                    continue;
                }
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = vrCamera;
                    canvas.planeDistance = planeDist;
                    Plugin.Log.LogInfo($"[VRUi] canvas '{canvas.name}' -> camera space on '{vrCamera.name}'");
                }
                else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                {
                    canvas.worldCamera = vrCamera;
                }
                else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != null && canvas.worldCamera != vrCamera && !canvas.worldCamera.isActiveAndEnabled)
                {
                    // Camera-space canvas whose camera went away (scene switch).
                    canvas.worldCamera = vrCamera;
                }
            }
        }

        private void RefreshAllowlist()
        {
            string raw = Plugin.Cfg.HudWorldSpaceCanvases.Value;
            if (raw == allowlistRaw)
                return;
            allowlistRaw = raw;
            var parts = raw.Split(',');
            var list = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length > 0)
                    list.Add(t);
            }
            allowlist = list.ToArray();
        }

        // Prefix match so 'prefab_HintsCanvas' covers 'prefab_HintsCanvas(Clone)'.
        private bool IsHudCanvas(string name)
        {
            foreach (var entry in allowlist)
                if (name.StartsWith(entry))
                    return true;
            return false;
        }
    }
}
