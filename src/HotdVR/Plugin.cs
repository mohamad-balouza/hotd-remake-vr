using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace HotdVR
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("The House of the Dead Remake.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.hotdremake.vrmod";
        public const string Name = "HOTD Remake VR";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static VRConfig Cfg;

        private void Awake()
        {
            Log = Logger;
            Cfg = new VRConfig(Config);

            Log.LogInfo($"{Name} {Version} loading...");
            Log.LogInfo($"Unity {Application.unityVersion}, platform {Application.platform}, gfx {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Log.LogInfo($"Render pipeline asset: {(rp != null ? rp.GetType().FullName + " '" + rp.name + "'" : "<built-in>")}");

            if (!Cfg.VREnabled.Value)
            {
                Log.LogWarning("VR disabled via config (VREnabled=false). Game runs flat.");
                return;
            }

            // The game pauses when its window loses focus; in VR the desktop
            // window is frequently unfocused while playing, so keep running.
            Application.runInBackground = true;

            var harmony = new HarmonyLib.Harmony(Guid);
            HdrpXrDiag.Apply(harmony);
            ComfortPatches.Apply(harmony);
            VRAimPatches.Apply(harmony);
            VRInputPatches.Apply(harmony);
            PerfPatches.Apply(harmony);

            var runner = new GameObject("HotdVR");
            DontDestroyOnLoad(runner);
            runner.AddComponent<VRDiagnostics>();
            runner.AddComponent<VRSystems>();
            runner.AddComponent<EyeCapture>();
            runner.AddComponent<VRUiProjector>();
            runner.AddComponent<VRHudAnchor>();
            runner.AddComponent<VRCameraRig>();
            runner.AddComponent<VRControllers>();
        }
    }

    internal enum HudMode { CameraSpace, WorldFollow }

    internal class VRConfig
    {
        public readonly ConfigEntry<bool> VREnabled;
        public readonly ConfigEntry<bool> AutoStartVR;
        public readonly ConfigEntry<bool> ProjectUiToVR;
        public readonly ConfigEntry<HudMode> HudMode;
        public readonly ConfigEntry<float> HudDistance;
        public readonly ConfigEntry<float> HudScale;
        public readonly ConfigEntry<float> HudFollowSpeed;
        public readonly ConfigEntry<float> HudYawDeadzoneDegrees;
        public readonly ConfigEntry<string> HudWorldSpaceCanvases;
        public readonly ConfigEntry<bool> VerboseLogging;
        public readonly ConfigEntry<bool> FrameTimeStats;
        public readonly ConfigEntry<bool> LeftHanded;
        public readonly ConfigEntry<bool> ShowLaser;
        public readonly ConfigEntry<float> AimPitchOffset;
        public readonly ConfigEntry<float> LoadingGraceSeconds;
        public readonly ConfigEntry<bool> PromptPhaseXR;
        public readonly ConfigEntry<float> RenderScale;
        public readonly ConfigEntry<bool> DisableSSR;
        public readonly ConfigEntry<bool> DisableVolumetrics;
        public readonly ConfigEntry<bool> DisableContactShadows;
        public readonly ConfigEntry<bool> DisableSSAO;

        public VRConfig(ConfigFile config)
        {
            VREnabled = config.Bind("General", "VREnabled", true,
                "Master switch. Set false to launch the game flat with the mod inert.");
            AutoStartVR = config.Bind("General", "AutoStartVR", true,
                "Start VR automatically shortly after boot. If false, press F9 in game to start VR.");
            ProjectUiToVR = config.Bind("UI", "ProjectUiToVR", true,
                "Convert the game's 2D overlay UI (menus, HUD) to camera space so it is visible in the headset.");
            HudMode = config.Bind("UI", "HudMode", HotdVR.HudMode.CameraSpace,
                "CameraSpace: HUD rigidly head-locked at HudDistance (original behavior). WorldFollow: "
                + "chapter HUD canvases float on a world-space panel that lazily follows the ride camera "
                + "(cockpit style). Menus always stay camera-space.");
            HudDistance = config.Bind("UI", "HudDistance", 1.2f,
                "Distance (meters) of the projected UI plane / world HUD panel from the camera (0.5-3).");
            HudScale = config.Bind("UI", "HudScale", 1.0f,
                "Size multiplier of the world-space HUD panel (WorldFollow mode only, 0.2-3).");
            HudFollowSpeed = config.Bind("UI", "HudFollowSpeed", 4.0f,
                "How quickly the world-space HUD chases the ride camera (WorldFollow mode, higher = snappier).");
            HudYawDeadzoneDegrees = config.Bind("UI", "HudYawDeadzoneDegrees", 25f,
                "The world-space HUD ignores ride-camera heading changes smaller than this (degrees).");
            HudWorldSpaceCanvases = config.Bind("UI", "HudWorldSpaceCanvases",
                "canvas_MainCanvas,prefab_BossHPCanvas,prefab_DialogUI,prefab_HintsCanvas",
                "Comma-separated canvas name prefixes that go world-space in WorldFollow mode. Everything "
                + "else (pause/death/menus/fades) stays camera-space.");
            LeftHanded = config.Bind("Controls", "LeftHanded", false,
                "Aim with the left controller instead of the right.");
            ShowLaser = config.Bind("Controls", "ShowLaser", true,
                "Show the laser pointer and 3D reticle from the aim hand. Toggle in-game: hold the "
                + "off-hand stick click 0.6s with the stick centered, or press F8.");
            AimPitchOffset = config.Bind("Controls", "AimPitchOffset", 45f,
                "Downward tilt (degrees) of the aim ray relative to the controller, approximating a pistol barrel. 0 = controller forward.");
            LoadingGraceSeconds = config.Bind("Stability", "LoadingGraceSeconds", 1.5f,
                "Seconds to keep XR passes suspended after a loading screen disappears, letting the new "
                + "chapter's cameras settle before stereo submission restarts (guards the native Submit "
                + "crash on chapter transitions). The headset stays on the loading view slightly longer "
                + "than the flat window does. 0 = resume immediately (old behavior). Clamped to 0-10.");
            PromptPhaseXR = config.Bind("Stability", "PromptPhaseXR", true,
                "Resume XR during long loading phases once the main camera has been stable for ~3 seconds, "
                + "so interactive prompts ('Shoot to start') are visible in the headset instead of a black "
                + "screen. The churny first seconds of every load stay suspended regardless.");
            RenderScale = config.Bind("Performance", "RenderScale", 1.0f,
                "Eye render target scale (0.5-1.5). Lower = sharper performance, softer image. Applied at VR start.");
            DisableSSR = config.Bind("Performance", "DisableSSR", true,
                "Disable screen-space reflections in VR (large GPU win, minor visual difference).");
            DisableVolumetrics = config.Bind("Performance", "DisableVolumetrics", false,
                "Disable volumetric fog/lighting in VR (large GPU win, but loses atmosphere).");
            DisableContactShadows = config.Bind("Performance", "DisableContactShadows", true,
                "Disable contact shadows in VR (moderate GPU win).");
            DisableSSAO = config.Bind("Performance", "DisableSSAO", false,
                "Disable screen-space ambient occlusion in VR (moderate GPU win, flattens lighting).");
            VerboseLogging = config.Bind("Debug", "VerboseLogging", true,
                "Extra diagnostic logging (subsystem dumps, per-scene camera info).");
            FrameTimeStats = config.Bind("Debug", "FrameTimeStats", true,
                "Collect CPU frame-time and HDRP submit-time percentiles, reported in the periodic [VR/state] lines.");
        }
    }
}
