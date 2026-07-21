using System;
using System.IO;
using UnityEngine;
using Valve.VR;

namespace HotdVR
{
    /// <summary>
    /// OpenVR overlay card shown while XR passes are suspended during loads:
    /// an HMD-locked panel telling the player the game is loading and to pull
    /// the trigger when the (invisible) prompt is ready. Entirely
    /// compositor-side (SetOverlayFromFile) - the game renders nothing, so it
    /// has none of the crash surface that resuming XR mid-load had.
    /// </summary>
    internal static class VRLoadingOverlay
    {
        private static ulong handle = OpenVR.k_ulOverlayHandleInvalid;
        private static bool createFailed;
        private static bool ready;

        private static string PngPath(bool readyCard) => Path.Combine(
            Path.GetDirectoryName(typeof(VRLoadingOverlay).Assembly.Location) ?? "",
            readyCard ? "loading-overlay-ready.png" : "loading-overlay.png");

        /// <summary>Swaps the card between LOADING and PULL-THE-TRIGGER. Driven
        /// by the gate's mid-load camera-stability signal - a proxy for "the
        /// prompt is probably up", hence the retry wording on the card.</summary>
        internal static void SetReady(bool value)
        {
            if (value == ready)
                return;
            ready = value;
            if (handle == OpenVR.k_ulOverlayHandleInvalid)
                return;
            try
            {
                var overlay = OpenVR.Overlay;
                if (overlay == null)
                    return;
                var err = overlay.SetOverlayFromFile(handle, PngPath(ready));
                if (err != EVROverlayError.None)
                    Plugin.Log.LogWarning($"[VRGate] loading overlay swap failed: {err}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VRGate] loading overlay swap error: {e.Message}");
            }
        }

        internal static void SetVisible(bool show)
        {
            if (!Plugin.Cfg.LoadingOverlay.Value)
                return;
            try
            {
                var overlay = OpenVR.Overlay;
                if (overlay == null)
                    return;

                if (show && handle == OpenVR.k_ulOverlayHandleInvalid && !createFailed)
                    Create(overlay);
                if (handle == OpenVR.k_ulOverlayHandleInvalid)
                    return;

                if (!show)
                    SetReady(false); // next load starts back at the LOADING card
                var err = show ? overlay.ShowOverlay(handle) : overlay.HideOverlay(handle);
                if (err == EVROverlayError.None)
                    Plugin.Log.LogInfo($"[VRGate] loading overlay {(show ? "shown" : "hidden")} (frame {Time.frameCount})");
                else
                    Plugin.Log.LogWarning($"[VRGate] loading overlay {(show ? "show" : "hide")} failed: {err}");
            }
            catch (Exception e)
            {
                createFailed = true; // never let overlay trouble touch the render path
                Plugin.Log.LogWarning($"[VRGate] loading overlay error: {e.Message}");
            }
        }

        private static void Create(CVROverlay overlay)
        {
            var err = overlay.CreateOverlay("hotdvr.loading", "HOTD VR Loading", ref handle);
            if (err != EVROverlayError.None)
            {
                createFailed = true;
                Plugin.Log.LogWarning($"[VRGate] loading overlay create failed: {err}");
                return;
            }

            string png = PngPath(ready);
            err = overlay.SetOverlayFromFile(handle, png);
            if (err != EVROverlayError.None)
            {
                createFailed = true;
                Plugin.Log.LogWarning($"[VRGate] loading overlay texture failed: {err} ({png})");
                overlay.DestroyOverlay(handle);
                handle = OpenVR.k_ulOverlayHandleInvalid;
                return;
            }

            overlay.SetOverlayWidthInMeters(handle, 1.4f);
            // HMD-locked: identity rotation, slightly below eye line, 2m ahead
            // (-Z is forward in OpenVR device space).
            var t = new HmdMatrix34_t
            {
                m0 = 1f, m1 = 0f, m2 = 0f, m3 = 0f,
                m4 = 0f, m5 = 1f, m6 = 0f, m7 = -0.1f,
                m8 = 0f, m9 = 0f, m10 = 1f, m11 = -2f
            };
            overlay.SetOverlayTransformTrackedDeviceRelative(handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref t);
            Plugin.Log.LogInfo("[VRGate] loading overlay created");
        }

        internal static void Destroy()
        {
            if (handle == OpenVR.k_ulOverlayHandleInvalid)
                return;
            try { OpenVR.Overlay?.DestroyOverlay(handle); } catch { }
            handle = OpenVR.k_ulOverlayHandleInvalid;
        }
    }
}
