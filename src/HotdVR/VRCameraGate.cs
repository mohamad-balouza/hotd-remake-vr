using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace HotdVR
{
    /// <summary>
    /// Enforces the single-XR-camera rule: only the designated main camera may
    /// create XR passes. The game runs several Game-type cameras with no target
    /// texture (camera_Loading, cam_Video, cam_Ammo...) - if more than one takes
    /// XR passes in a frame, the OpenVR provider double-submits the eye
    /// swapchain and crashes natively in ScriptableRenderContext.Submit.
    /// Called from the XRSystem.SetupFrame prefix each frame.
    /// </summary>
    internal static class VRCameraGate
    {
        private static readonly HashSet<int> loggedCameras = new HashSet<int>();
        private static int lastVrCamId;

        public static Camera CurrentVRCamera { get; private set; }

        /// <summary>Frame index of the last suspend/resume flip or VR-camera
        /// identity change. Diagnostic throttles are bypassed for a window
        /// after this so transition crash frames are attributable.
        /// Init well below any real frame (not int.MinValue - subtraction
        /// in InDiagWindow would overflow).</summary>
        internal static int LastStateChangeFrame = -1000;

        /// <summary>True within 120 frames of a gate state change.</summary>
        internal static bool InDiagWindow => Time.frameCount - LastStateChangeFrame <= 120;

        /// <summary>True while a load screen camera is active - XR passes are
        /// fully suspended then (chapter transitions crash natively in Submit
        /// when XR rendering runs across the camera churn of a scene load).</summary>
        public static bool LoadingScreenActive { get; private set; }

        public static void Enforce(Camera[] cameras)
        {
            if (!VRRuntimeBootstrap.Active)
                return;

            bool loading = false;
            foreach (var cam in cameras)
                if (cam != null && cam.name == "camera_Loading")
                    loading = true;
            bool flipped = loading != LoadingScreenActive;
            if (flipped)
                LoadingScreenActive = loading;

            // The gameplay/menu main camera is tagged MainCamera in this game
            // (menu 'Main Camera', chapters 'cam_MainCamera').
            Camera main = LoadingScreenActive ? null : Camera.main;
            CurrentVRCamera = main;
            int mainId = main != null ? main.GetInstanceID() : 0;

            if (flipped)
            {
                LastStateChangeFrame = Time.frameCount;
                if (loading)
                    Plugin.Log.LogInfo($"[VRGate] loading screen started - XR passes suspended (frame {Time.frameCount}, {RenderHealth()})");
                else
                    Plugin.Log.LogInfo($"[VRGate] XR passes resumed (frame {Time.frameCount}, camera='{CamName(main)}' id={mainId}, cams=[{CameraSet(cameras)}], {RenderHealth()})");
            }
            else if (mainId != lastVrCamId)
            {
                // The historical transition crash hit at the frame the next
                // chapter's cam_MainCamera appeared - a camera identity
                // change, not necessarily a gate flip.
                LastStateChangeFrame = Time.frameCount;
                Plugin.Log.LogInfo($"[VRGate] VR camera changed -> '{CamName(main)}' id={mainId} (frame {Time.frameCount}, cams=[{CameraSet(cameras)}])");
            }
            lastVrCamId = mainId;

            foreach (var cam in cameras)
            {
                if (cam == null)
                    continue;
                bool allowXr = main != null && ReferenceEquals(cam, main);
                var hd = cam.GetComponent<HDAdditionalCameraData>();
                if (hd == null)
                {
                    if (allowXr)
                        continue; // default is xrRendering=true, nothing to do
                    hd = cam.gameObject.AddComponent<HDAdditionalCameraData>();
                }
                if (hd.xrRendering != allowXr)
                {
                    hd.xrRendering = allowXr;
                    if (loggedCameras.Add(cam.GetInstanceID()) || InDiagWindow)
                        Plugin.Log.LogInfo($"[VRGate] '{cam.name}' xrRendering={allowXr} (frame {Time.frameCount})");
                }
            }
        }

        private static string CamName(Camera cam) => cam != null ? cam.name : "<none>";

        private static string RenderHealth() =>
            $"renderHealth rendered={HdrpXrDiag.renderedFrames} repaired={HdrpXrDiag.repairedFrames} skipped={HdrpXrDiag.skippedFrames}";

        private static string CameraSet(Camera[] cameras)
        {
            var names = new List<string>();
            foreach (var c in cameras)
                if (c != null)
                    names.Add(c.name);
            names.Sort();
            return string.Join("|", names);
        }
    }
}
