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
        private IEnumerator Start()
        {
            // Let the engine finish its first frames before starting XR.
            yield return null;
            yield return null;

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

            // Log render state over the first seconds - GetRenderPassCount()==2
            // proves HDRP picked up the display subsystem in multipass mode.
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(2f);
                LogStereoState(i);
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
                $"eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} device='{XRSettings.loadedDeviceName}'");
        }

        private void OnApplicationQuit()
        {
            VRRuntimeBootstrap.TryStop();
        }
    }
}
