using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HotdVR
{
    /// <summary>
    /// Owns VR lifecycle: starts the XR runtime shortly after boot and logs
    /// stereo state so milestones are verifiable from BepInEx logs alone.
    /// </summary>
    public class VRSystems : MonoBehaviour
    {
        private void Update()
        {
            if (!VRRuntimeBootstrap.Active && Input.GetKeyDown(KeyCode.F9))
            {
                Plugin.Log.LogInfo("[VR] F9 pressed - starting VR on demand");
                StartCoroutine(StartVR());
            }
        }

        private IEnumerator Start()
        {
            // Let the engine finish its first frames before starting XR.
            yield return null;
            yield return null;

            if (Plugin.Cfg.AutoStartVR.Value)
                yield return StartVR();
        }

        private IEnumerator StartVR()
        {
            if (VRRuntimeBootstrap.Active)
                yield break;

            // SteamVR can be momentarily busy (e.g. it is still launching, or a
            // previous app is releasing the session) - retry transient errors.
            const int maxAttempts = 5;
            bool started = false;
            for (int attempt = 1; attempt <= maxAttempts && !started; attempt++)
            {
                started = VRRuntimeBootstrap.TryStart();
                if (!started)
                {
                    if (!VRRuntimeBootstrap.IsTransientError(VRRuntimeBootstrap.LastInitError))
                        break;
                    Plugin.Log.LogWarning($"[VR] transient init error ({VRRuntimeBootstrap.LastInitError}), retry {attempt}/{maxAttempts} in 5s...");
                    yield return new WaitForSecondsRealtime(5f);
                }
            }

            if (!started)
            {
                Plugin.Log.LogError("[VR] VR not started - game continues flat.");
                yield break;
            }

            // Log render state periodically - GetRenderPassCount()==2 proves
            // HDRP picked up the display subsystem in multipass mode; the
            // render-health counters show whether frames actually render.
            int i = 0;
            while (true)
            {
                yield return new WaitForSecondsRealtime(i < 6 ? 2f : 15f);
                LogStereoState(i++);
            }
        }

        private void LogStereoState(int index)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            if (displays.Count == 0)
            {
                Plugin.Log.LogWarning($"[VR/state{index}] no display subsystem instances");
                return;
            }

            var d = displays[0];
            int passes = -1;
            try { passes = d.GetRenderPassCount(); } catch { }
            var cam = Camera.main;
            Plugin.Log.LogInfo(
                $"[VR/state{index}] running={d.running} renderPasses={passes} " +
                $"camera='{(cam != null ? cam.name : "<none>")}' stereo={(cam != null && cam.stereoEnabled)} " +
                $"eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} device='{XRSettings.loadedDeviceName}' " +
                $"renderHealth: rendered={HdrpXrDiag.renderedFrames} skipped={HdrpXrDiag.skippedFrames}");
            DumpCameras($"state{index}");
        }

        // HDRP only creates XR passes for a camera when cameraType==Game,
        // targetTexture==null and HDAdditionalCameraData.xrRendering==true -
        // dump all three per camera to find why the HMD stays black.
        internal static void DumpCameras(string tag)
        {
            var cams = new List<Camera>(Camera.allCameras);
            if (Camera.main != null && !cams.Contains(Camera.main))
                cams.Add(Camera.main);
            Plugin.Log.LogInfo($"[Cam/{tag}] allCamerasCount={Camera.allCamerasCount} dumping {cams.Count}");
            foreach (var cam in cams)
            {
                string hdInfo = "no-hd-data";
                var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                if (hd != null)
                    hdInfo = $"xrRendering={hd.xrRendering} passthrough={hd.fullscreenPassthrough} customRender={hd.hasCustomRender}";
                Plugin.Log.LogInfo(
                    $"[Cam/{tag}] '{cam.name}' type={cam.cameraType} enabled={cam.enabled} depth={cam.depth} " +
                    $"targetTexture={(cam.targetTexture != null ? cam.targetTexture.name : "null")} " +
                    $"targetDisplay={cam.targetDisplay} stereoEye={cam.stereoTargetEye} {hdInfo}");
            }
        }

        private void OnApplicationQuit()
        {
            VRRuntimeBootstrap.TryStop();
        }
    }
}
