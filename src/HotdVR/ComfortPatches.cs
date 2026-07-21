using HarmonyLib;

namespace HotdVR
{
    /// <summary>
    /// VR comfort: neutralize camera shake, hit recoil and FOV zooms while VR
    /// is active. In VR the projection is HMD-controlled and artificial camera
    /// motion is a fast path to motion sickness.
    /// </summary>
    internal static class ComfortPatches
    {
        public static void Apply(Harmony harmony)
        {
            var playerCam = AccessTools.TypeByName("HD_PlayerCamera");
            if (playerCam == null)
            {
                Plugin.Log.LogWarning("[Comfort] HD_PlayerCamera not found");
                return;
            }
            var skip = new HarmonyMethod(typeof(ComfortPatches), nameof(SkipWhenVR));
            harmony.Patch(AccessTools.Method(playerCam, "Shake"), prefix: skip);
            harmony.Patch(AccessTools.Method(playerCam, "ApplyHitRecoil"), prefix: skip);
            foreach (var m in AccessTools.GetDeclaredMethods(playerCam))
            {
                if (m.Name == "Zoom" || m.Name == "StartCameraShake" || m.Name == "StartCameraZoom")
                    harmony.Patch(m, prefix: skip);
            }
            Plugin.Log.LogInfo("[Comfort] camera shake/recoil/zoom patches applied");
        }

        private static bool SkipWhenVR()
        {
            return !VRRuntimeBootstrap.Active; // false = skip original while VR runs
        }
    }
}
