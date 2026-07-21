using System;
using System.Runtime.InteropServices;
using Unity.XR.OpenVR;
using UnityEngine;
using UnityEngine.XR.Management;

namespace HotdVR
{
    /// <summary>
    /// Brings up Unity's XR stack at runtime with the Valve OpenVR XR plugin.
    /// No serialized XR settings assets exist in the build; everything is
    /// created via ScriptableObject.CreateInstance. Stereo mode / init type /
    /// mirror view are read by the native provider from
    /// StreamingAssets/SteamVR/OpenVRSettings.asset (placed by the installer).
    /// </summary>
    internal static class VRRuntimeBootstrap
    {
        public static XRManagerSettings Manager { get; private set; }
        public static OpenVRLoader Loader { get; private set; }
        public static bool Active { get; private set; }

        // Valve's loader only holds its native tick delegate in a local, which
        // Mono's GC may collect while native code still calls the thunk. We
        // re-register with a delegate rooted here for the process lifetime.
        private static TickCallbackDelegate pinnedTick;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TickCallbackDelegate(int value);

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern void RegisterTickCallback([MarshalAs(UnmanagedType.FunctionPtr)] TickCallbackDelegate callback);

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern Valve.VR.EVRInitError GetInitializationResult();

        /// <summary>Last native EVRInitError observed after a failed init attempt.</summary>
        public static Valve.VR.EVRInitError LastInitError { get; private set; } = Valve.VR.EVRInitError.None;

        /// <summary>Errors worth retrying: SteamVR busy/starting rather than absent.</summary>
        public static bool IsTransientError(Valve.VR.EVRInitError error) =>
            error == Valve.VR.EVRInitError.Init_AnotherAppLaunching ||
            error == Valve.VR.EVRInitError.Init_Retry;

        public static bool TryStart()
        {
            if (Active)
                return true;

            try
            {
                // Awake() registers this instance as OpenVRSettings.s_Settings.
                var settings = ScriptableObject.CreateInstance<OpenVRSettings>();
                settings.StereoRenderingMode = OpenVRSettings.StereoRenderingModes.MultiPass;
                settings.InitializationType = OpenVRSettings.InitializationTypes.Scene;
                settings.MirrorView = OpenVRSettings.MirrorViewModes.Right;
                settings.ActionManifestFileRelativeFilePath = null;

                var general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                Manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                general.Manager = Manager;

                Loader = ScriptableObject.CreateInstance<OpenVRLoader>();
#pragma warning disable CS0618 // loaders getter returns the live list
                Manager.loaders.Add(Loader);
#pragma warning restore CS0618

                Plugin.Log.LogInfo("[VR] Initializing OpenVR loader...");
                Manager.InitializeLoaderSync();

                if (Manager.activeLoader == null)
                {
                    try { LastInitError = GetInitializationResult(); } catch { }
                    Plugin.Log.LogError($"[VR] OpenVR loader did not initialize (EVRInitError: {LastInitError}). " +
                                        "Check that SteamVR is installed and a headset session is available.");
                    return false;
                }

                // HDRP's XRSystemInit only applies sRGB to displays that exist at
                // boot; ours starts later, so set it before starting subsystems.
                var disp = Loader.GetLoadedSubsystem<UnityEngine.XR.XRDisplaySubsystem>();
                if (disp != null)
                {
                    disp.sRGB = true;
                    float scale = Mathf.Clamp(Plugin.Cfg.RenderScale.Value, 0.5f, 1.5f);
                    if (!Mathf.Approximately(scale, 1f))
                    {
                        disp.scaleOfAllRenderTargets = scale;
                        Plugin.Log.LogInfo($"[VR] eye render target scale = {scale}");
                    }
                }

                Manager.StartSubsystems();

                pinnedTick = OnNativeTick;
                RegisterTickCallback(pinnedTick);

                // Note: switching HDRP to its classic (non render-graph) path via
                // EnableRenderGraph(false) was tried here and crashes on level
                // load in this build (native crash in ScriptableRenderContext.
                // Submit). The render graph path must stay enabled.

                Active = true;
                var display = Loader.displaySubsystem;
                Plugin.Log.LogInfo($"[VR] XR started. display={(display != null ? "ok" : "NULL")} running={display?.running}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[VR] XR bring-up failed: {e}");
                TryStop();
                return false;
            }
        }

        public static void TryStop()
        {
            try
            {
                if (Manager != null && Manager.activeLoader != null)
                {
                    Manager.StopSubsystems();
                    Manager.DeinitializeLoader();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[VR] XR stop: {e.Message}");
            }
            Active = false;
        }

        private static void OnNativeTick(int value)
        {
            OpenVREvents.Update();
        }
    }
}
