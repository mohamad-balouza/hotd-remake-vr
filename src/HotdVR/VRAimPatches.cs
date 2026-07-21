using HarmonyLib;
using UnityEngine;

namespace HotdVR
{
    /// <summary>
    /// The lightgun core: the motion controller IS the gun.
    /// - HD_Weapon.Fire gets the controller's world ray instead of the
    ///   crosshair-derived ScreenPointToRay
    /// - HD_Crosshair.IsInsideScreenSpace reflects whether the controller
    ///   points at the visible scene (preserves the classic point-off-screen
    ///   -to-reload gesture)
    /// - the 2D crosshair is hidden (a 3D laser/reticle replaces it)
    /// - mouse/gyro crosshair movement is disabled while VR runs
    /// - the flashlight follows the controller ray
    /// </summary>
    internal static class VRAimPatches
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(HD_Weapon), "Fire"),
                prefix: new HarmonyMethod(typeof(VRAimPatches), nameof(FirePrefix)));

            harmony.Patch(AccessTools.Method(typeof(HD_Crosshair), "IsInsideScreenSpace"),
                postfix: new HarmonyMethod(typeof(VRAimPatches), nameof(IsInsideScreenSpacePostfix)));

            harmony.Patch(AccessTools.Method(typeof(HD_InputManager), "handleAiming"),
                prefix: new HarmonyMethod(typeof(VRAimPatches), nameof(SkipWhenVRAiming)));

            harmony.Patch(AccessTools.Method(typeof(HD_FlashlightController), "getRayToCrosshair"),
                postfix: new HarmonyMethod(typeof(VRAimPatches), nameof(RayToCrosshairPostfix)));

            var cursorModule = AccessTools.TypeByName("MegaPixel.ApplicationManagement.MP_CursorSetterModule");
            if (cursorModule != null)
                harmony.Patch(AccessTools.Method(cursorModule, "changeCursorVisibility"),
                    prefix: new HarmonyMethod(typeof(VRAimPatches), nameof(CursorVisibilityPrefix)));

            Plugin.Log.LogInfo("[VRAim] aim patches applied");
        }

        // HD_Weapon.Fire(in Ray _aim) - swap in the controller world ray.
        // Only player 1's weapons; flat co-op player 2 keeps normal aiming.
        private static void FirePrefix(HD_Weapon __instance, ref Ray _aim)
        {
            if (!VRRuntimeBootstrap.Active || !VRControllers.AimValid)
                return;
            var owner = __instance.Owner;
            if (owner != null && owner.ThisPlayer != PlayerType.Player1)
                return;
            _aim = VRControllers.AimRay;
        }

        private static void IsInsideScreenSpacePostfix(ref bool __result)
        {
            if (VRRuntimeBootstrap.Active && VRControllers.AimValid)
                __result = VRControllers.AimOnScreen;
        }

        private static bool SkipWhenVRAiming()
        {
            // While VR runs, the crosshair is not input-driven (the controller
            // ray replaces it); skipping also disables the gyro path.
            return !VRRuntimeBootstrap.Active;
        }

        private static void RayToCrosshairPostfix(ref Ray __result)
        {
            if (VRRuntimeBootstrap.Active && VRControllers.AimValid)
                __result = VRControllers.AimRay;
        }

        private static bool CursorVisibilityPrefix(ref bool _shouldBeVisible)
        {
            if (VRRuntimeBootstrap.Active)
                _shouldBeVisible = false;
            return true;
        }
    }
}
