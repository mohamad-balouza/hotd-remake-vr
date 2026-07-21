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
    ///
    /// Load handling is a three-state machine: Rendering -> Suspended while a
    /// camera_Loading camera exists -> Grace for LoadingGraceSeconds after it
    /// disappears -> Rendering. The grace tail exists because (a) the historical
    /// transition crash hit at the frame the next chapter's cam_MainCamera
    /// appeared, right at resume, and (b) the game alternates two camera sets
    /// per frame during loads, so camera_Loading is absent every other frame
    /// mid-load - the timer only expires once the flapping has actually stopped.
    /// </summary>
    internal static class VRCameraGate
    {
        private enum GateState { Rendering, Suspended, Grace }

        private static readonly HashSet<int> loggedCameras = new HashSet<int>();
        private static int lastVrCamId;
        private static GateState state = GateState.Rendering;
        private static float graceUntil;
        private static bool graceStartLogged;   // per load episode
        private static bool graceReentryLogged; // per load episode

        public static Camera CurrentVRCamera { get; private set; }

        /// <summary>Frame index of the last suspend/resume flip or VR-camera
        /// identity change. Diagnostic throttles are bypassed for a window
        /// after this so transition crash frames are attributable.
        /// Init well below any real frame (not int.MinValue - subtraction
        /// in InDiagWindow would overflow).</summary>
        internal static int LastStateChangeFrame = -1000;

        /// <summary>True within 120 frames of a gate state change.</summary>
        internal static bool InDiagWindow => Time.frameCount - LastStateChangeFrame <= 120;

        /// <summary>True while XR passes are suspended: a load screen camera is
        /// active, or the post-load grace period is still running (chapter
        /// transitions crash natively in Submit when XR rendering runs across
        /// the camera churn of a scene load).</summary>
        public static bool LoadingScreenActive => state != GateState.Rendering;

        public static void Enforce(Camera[] cameras)
        {
            if (!VRRuntimeBootstrap.Active)
                return;

            bool present = false;
            foreach (var cam in cameras)
                if (cam != null && cam.name == "camera_Loading")
                    present = true;

            float grace = Mathf.Clamp(Plugin.Cfg.LoadingGraceSeconds.Value, 0f, 10f);
            bool transitioned = false, resumed = false, graceResume = false;

            switch (state)
            {
                case GateState.Rendering:
                    if (present)
                    {
                        state = GateState.Suspended;
                        graceStartLogged = false;
                        graceReentryLogged = false;
                        transitioned = true;
                        LastStateChangeFrame = Time.frameCount;
                        Plugin.Log.LogInfo($"[VRGate] loading screen started - XR passes suspended (frame {Time.frameCount}, {RenderHealth()})");
                    }
                    break;

                case GateState.Suspended:
                    if (!present)
                    {
                        if (grace > 0f)
                        {
                            state = GateState.Grace;
                            graceUntil = Time.realtimeSinceStartup + grace;
                            transitioned = true;
                            if (!graceStartLogged)
                            {
                                graceStartLogged = true;
                                LastStateChangeFrame = Time.frameCount;
                                Plugin.Log.LogInfo($"[VRGate] loading screen ended - grace period started ({grace:F1}s, frame {Time.frameCount})");
                            }
                        }
                        else
                        {
                            state = GateState.Rendering;
                            transitioned = resumed = true;
                        }
                    }
                    break;

                case GateState.Grace:
                    if (present)
                    {
                        // Chained load or the mid-load A/B camera flap; the
                        // timer restarts when the loading camera next vanishes.
                        state = GateState.Suspended;
                        transitioned = true;
                        if (!graceReentryLogged)
                        {
                            graceReentryLogged = true;
                            Plugin.Log.LogInfo($"[VRGate] loading screen re-appeared during grace - suspension continues (frame {Time.frameCount})");
                        }
                    }
                    else if (Time.realtimeSinceStartup >= graceUntil)
                    {
                        state = GateState.Rendering;
                        transitioned = resumed = graceResume = true;
                    }
                    break;
            }

            // The gameplay/menu main camera is tagged MainCamera in this game
            // (menu 'Main Camera', chapters 'cam_MainCamera').
            Camera main = LoadingScreenActive ? null : Camera.main;
            CurrentVRCamera = main;
            int mainId = main != null ? main.GetInstanceID() : 0;

            if (resumed)
            {
                LastStateChangeFrame = Time.frameCount;
                Plugin.Log.LogInfo($"[VRGate] {(graceResume ? "grace period ended - XR passes resumed" : "XR passes resumed")} (frame {Time.frameCount}, camera='{CamName(main)}' id={mainId}, cams=[{CameraSet(cameras)}], {RenderHealth()})");
            }
            else if (!transitioned && mainId != lastVrCamId)
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
