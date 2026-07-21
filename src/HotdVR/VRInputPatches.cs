using HarmonyLib;

namespace HotdVR
{
    /// <summary>
    /// Bridges VR controller buttons into the game's input system at the
    /// Rewired seam: every game/menu query goes through Rewired.Player, so
    /// OR-ing VR states into GetButton*/GetAxis covers gameplay, menus and
    /// popups uniformly (keyboard/mouse keep working alongside).
    ///
    /// Mapping (right-handed; hands swap with LeftHanded config):
    ///   trigger        -> Shoot(1)            grip     -> Reload(2)
    ///   A              -> Accept(9)/Skip(11)/Revive(87)
    ///   B              -> Cancel(10)/BuyToken(88)
    ///   aim stick click-> NextWeapon(13)      off X    -> Flashlight(47)
    ///   off menu btn   -> Pause(16)           off stick-> NavX(5)/NavY(6)
    /// </summary>
    internal static class VRInputPatches
    {
        private const int SystemPlayerId = 9999999;

        public static void Apply(Harmony harmony)
        {
            var player = typeof(Rewired.Player);
            harmony.Patch(AccessTools.Method(player, "GetButtonDown", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(VRInputPatches), nameof(GetButtonDownPostfix)));
            harmony.Patch(AccessTools.Method(player, "GetButton", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(VRInputPatches), nameof(GetButtonPostfix)));
            harmony.Patch(AccessTools.Method(player, "GetButtonUp", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(VRInputPatches), nameof(GetButtonUpPostfix)));
            harmony.Patch(AccessTools.Method(player, "GetNegativeButtonDown", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(VRInputPatches), nameof(GetNegativeButtonDownPostfix)));
            harmony.Patch(AccessTools.Method(player, "GetNegativeButton", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(VRInputPatches), nameof(GetNegativeButtonPostfix)));
            harmony.Patch(AccessTools.Method(player, "GetAxis", new[] { typeof(int) }),
                postfix: new HarmonyMethod(typeof(VRInputPatches), nameof(GetAxisPostfix)));
            Plugin.Log.LogInfo("[VRInput] Rewired bridge patches applied");
        }

        private static bool IsGameplayTarget(Rewired.Player p) => p.id == 0;
        private static bool IsMenuTarget(Rewired.Player p) => p.id == 0 || p.id == SystemPlayerId;

        // Menu navigation goes through Rewired's uGUI input module, whose move
        // gate (GetRawMoveVector) requires GetAxis(5/6) AND the HELD digital
        // states with matching sign: GetButton(int) for positive deflection,
        // GetNegativeButton(int) for negative (decompiled 2026-07-21). The
        // module self-repeats at ~10 steps/s while held.
        private const float NavHold = 0.5f;

        private static void GetButtonDownPostfix(Rewired.Player __instance, int actionId, ref bool __result)
        {
            if (__result || !VRRuntimeBootstrap.Active) return;
            if (IsGameplayTarget(__instance))
            {
                switch (actionId)
                {
                    case 1: __result |= VRControllers.TriggerDown; return;   // Shoot
                    case 2: __result |= VRControllers.GripDown; return;      // Reload
                    case 13: __result |= VRControllers.StickClickDown; return; // NextWeapon
                    case 47: __result |= VRControllers.XDown; return;        // Flashlight
                    case 87: __result |= VRControllers.ADown; return;        // Revive
                    case 88: __result |= VRControllers.BDown; return;        // BuyToken
                }
            }
            if (IsMenuTarget(__instance))
            {
                switch (actionId)
                {
                    case 9: __result |= VRControllers.ADown; return;         // Accept
                    case 10: __result |= VRControllers.BDown; return;        // Cancel
                    case 16: __result |= VRControllers.MenuDown || VRControllers.YDown; return; // Pause (menu btn or off-hand Y)
                    case 5: __result |= VRControllers.StickRightDown; return; // NavX +
                    case 6: __result |= VRControllers.StickUpDown; return;   // NavY +
                    case 17: __result |= VRControllers.StickLeftDown; return; // PageLeft
                    case 18: __result |= VRControllers.StickRightDown; return; // PageRight
                }
            }
        }

        private static void GetNegativeButtonDownPostfix(Rewired.Player __instance, int actionId, ref bool __result)
        {
            if (__result || !VRRuntimeBootstrap.Active || !IsMenuTarget(__instance)) return;
            switch (actionId)
            {
                case 5: __result |= VRControllers.StickLeftDown; return;
                case 6: __result |= VRControllers.StickDownDown; return;
            }
        }

        private static void GetNegativeButtonPostfix(Rewired.Player __instance, int actionId, ref bool __result)
        {
            if (__result || !VRRuntimeBootstrap.Active || !IsMenuTarget(__instance)) return;
            switch (actionId)
            {
                case 5: __result |= VRControllers.LeftStick.x < -NavHold; return;
                case 6: __result |= VRControllers.LeftStick.y < -NavHold; return;
            }
        }

        private static void GetButtonPostfix(Rewired.Player __instance, int actionId, ref bool __result)
        {
            if (__result || !VRRuntimeBootstrap.Active) return;
            if (IsGameplayTarget(__instance))
            {
                switch (actionId)
                {
                    case 1: __result |= VRControllers.TriggerHeld; return;
                    case 2: __result |= VRControllers.GripHeld; return;
                }
            }
            if (IsMenuTarget(__instance))
            {
                switch (actionId)
                {
                    case 11: __result |= VRControllers.AHeld; return;         // Skip (hold)
                    case 5: __result |= VRControllers.LeftStick.x > NavHold; return; // NavX held +
                    case 6: __result |= VRControllers.LeftStick.y > NavHold; return; // NavY held +
                }
            }
        }

        private static void GetButtonUpPostfix(Rewired.Player __instance, int actionId, ref bool __result)
        {
            if (__result || !VRRuntimeBootstrap.Active || !IsGameplayTarget(__instance)) return;
            if (actionId == 1)
                __result |= VRControllers.TriggerUp;
        }

        private static void GetAxisPostfix(Rewired.Player __instance, int actionId, ref float __result)
        {
            if (!VRRuntimeBootstrap.Active || !IsMenuTarget(__instance)) return;
            if (__result != 0f) return;
            switch (actionId)
            {
                case 5: __result = VRControllers.LeftStick.x; return;         // NavX
                case 6: __result = VRControllers.LeftStick.y; return;         // NavY
            }
        }
    }
}
