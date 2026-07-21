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

        public static Camera CurrentVRCamera { get; private set; }

        public static void Enforce(Camera[] cameras)
        {
            if (!VRRuntimeBootstrap.Active)
                return;

            // The gameplay/menu main camera is tagged MainCamera in this game
            // (menu 'Main Camera', chapters 'cam_MainCamera').
            Camera main = Camera.main;
            CurrentVRCamera = main;

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
                    if (loggedCameras.Add(cam.GetInstanceID()))
                        Plugin.Log.LogInfo($"[VRGate] '{cam.name}' xrRendering={allowXr}");
                }
            }
        }
    }
}
