using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace HotdVR
{
    /// <summary>
    /// VR performance: strips expensive HDRP features at the frame-settings
    /// seam while VR is active (no pipeline rebuild needed - the aggregate is
    /// recomputed per camera per frame). Multipass renders everything twice,
    /// so each win counts double.
    /// </summary>
    internal static class PerfPatches
    {
        public static void Apply(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(FrameSettings), "AggregateFrameSettings",
                new[] { typeof(FrameSettings).MakeByRefType(), typeof(Camera), typeof(HDAdditionalCameraData), typeof(HDRenderPipelineAsset), typeof(HDRenderPipelineAsset) });
            if (method == null)
            {
                Plugin.Log.LogWarning("[Perf] FrameSettings.AggregateFrameSettings not found");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PerfPatches), nameof(AggregatePostfix)));
            Plugin.Log.LogInfo("[Perf] frame settings reducer applied");
        }

        private static void AggregatePostfix(ref FrameSettings aggregatedFrameSettings)
        {
            if (!VRRuntimeBootstrap.Active)
                return;
            if (Plugin.Cfg.DisableSSR.Value)
                aggregatedFrameSettings.SetEnabled(FrameSettingsField.SSR, false);
            if (Plugin.Cfg.DisableVolumetrics.Value)
                aggregatedFrameSettings.SetEnabled(FrameSettingsField.Volumetrics, false);
            if (Plugin.Cfg.DisableContactShadows.Value)
                aggregatedFrameSettings.SetEnabled(FrameSettingsField.ContactShadows, false);
            if (Plugin.Cfg.DisableSSAO.Value)
                aggregatedFrameSettings.SetEnabled(FrameSettingsField.SSAO, false);
        }
    }
}
