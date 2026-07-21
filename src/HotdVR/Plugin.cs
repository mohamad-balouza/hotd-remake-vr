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

            var runner = new GameObject("HotdVR");
            DontDestroyOnLoad(runner);
            runner.AddComponent<VRDiagnostics>();
            runner.AddComponent<VRSystems>();
            runner.AddComponent<EyeCapture>();
            runner.AddComponent<VRUiProjector>();
        }
    }

    internal class VRConfig
    {
        public readonly ConfigEntry<bool> VREnabled;
        public readonly ConfigEntry<bool> AutoStartVR;
        public readonly ConfigEntry<bool> ProjectUiToVR;
        public readonly ConfigEntry<bool> VerboseLogging;

        public VRConfig(ConfigFile config)
        {
            VREnabled = config.Bind("General", "VREnabled", true,
                "Master switch. Set false to launch the game flat with the mod inert.");
            AutoStartVR = config.Bind("General", "AutoStartVR", true,
                "Start VR automatically shortly after boot. If false, press F9 in game to start VR.");
            ProjectUiToVR = config.Bind("UI", "ProjectUiToVR", true,
                "Convert the game's 2D overlay UI (menus, HUD) to camera space so it is visible in the headset.");
            VerboseLogging = config.Bind("Debug", "VerboseLogging", true,
                "Extra diagnostic logging (subsystem dumps, per-scene camera info).");
        }
    }
}
