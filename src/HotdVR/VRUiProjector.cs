using System.Collections;
using UnityEngine;

namespace HotdVR
{
    /// <summary>
    /// Makes the game's 2D UI visible in the headset: screen-space-overlay
    /// canvases never reach the XR eye textures, so while VR is active they are
    /// converted to screen-space-camera on the current VR camera at a short
    /// plane distance. First-pass VR UI - refined later (world-space panel).
    /// </summary>
    public class VRUiProjector : MonoBehaviour
    {
        private const float PlaneDistance = 1.0f;

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
            var vrCamera = VRCameraGate.CurrentVRCamera != null ? VRCameraGate.CurrentVRCamera : Camera.main;
            if (vrCamera == null)
                return;

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
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = vrCamera;
                    canvas.planeDistance = PlaneDistance;
                    Plugin.Log.LogInfo($"[VRUi] canvas '{canvas.name}' -> camera space on '{vrCamera.name}'");
                }
                else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != vrCamera && canvas.worldCamera == null)
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
    }
}
