using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace HotdVR
{
    /// <summary>
    /// Diagnostic Harmony patches into HDRP's internal XRSystem to find out why
    /// XR passes are not created (black HMD while desktop shows the flat game).
    /// </summary>
    internal static class HdrpXrDiag
    {
        private static int setupCount;
        private static int createCount;
        private static bool lastRefresh;
        private static bool loggedFirstCreate;

        public static void Apply(Harmony harmony)
        {
            var t = AccessTools.TypeByName("UnityEngine.Rendering.HighDefinition.XRSystem");
            if (t == null)
            {
                Plugin.Log.LogWarning("[HdrpDiag] XRSystem type not found");
                return;
            }
            harmony.Patch(AccessTools.Method(t, "RefreshXrSdk"),
                postfix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(RefreshPost)));
            harmony.Patch(AccessTools.Method(t, "SetupFrame"),
                prefix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(SetupPre)));
            harmony.Patch(AccessTools.Method(t, "CreateLayoutFromXrSdk"),
                postfix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(CreatePost)));

            var hdrp = AccessTools.TypeByName("UnityEngine.Rendering.HighDefinition.HDRenderPipeline");
            harmony.Patch(AccessTools.Method(hdrp, "TryCalculateFrameParameters"),
                postfix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(TryCalcPost)));
            harmony.Patch(AccessTools.Method(hdrp, "TryCull"),
                postfix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(TryCullPost)));
            harmony.Patch(AccessTools.Method(hdrp, "ExecuteWithRenderGraph"),
                prefix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(ExecutePre)));
            Plugin.Log.LogInfo("[HdrpDiag] XRSystem+HDRP patches applied");
        }

        private static int calcCount, cullCount, execCount;

        private static void TryCalcPost(Camera camera, object xrPass, bool __result)
        {
            calcCount++;
            bool xrEnabled = Traverse.Create(xrPass).Property("enabled").GetValue<bool>();
            if (!xrEnabled) return;
            if (calcCount % 300 < 2 || !__result)
                Plugin.Log.LogInfo($"[HdrpDiag] TryCalcFrameParams #{calcCount} cam='{camera.name}' xr=True result={__result}");
        }

        private static void TryCullPost(Camera camera, bool __result)
        {
            cullCount++;
            if (cullCount % 300 < 2 || !__result)
                Plugin.Log.LogInfo($"[HdrpDiag] TryCull #{cullCount} cam='{camera.name}' result={__result}");
        }

        private static void ExecutePre(object renderRequest)
        {
            execCount++;
            if (execCount % 300 >= 3 && execCount > 10) return;
            var tr = Traverse.Create(renderRequest);
            var hdCam = tr.Field("hdCamera");
            string camName = hdCam.Property("camera").GetValue<Camera>()?.name ?? "?";
            var xr = hdCam.Property("xr");
            bool xrEnabled = xr.Property("enabled").GetValue<bool>();
            int multipassId = xrEnabled ? xr.Property("multipassId").GetValue<int>() : -1;
            var target = tr.Field("target");
            var targetId = target.Field("id").GetValue();
            Plugin.Log.LogInfo($"[HdrpDiag] Execute #{execCount} cam='{camName}' xrEnabled={xrEnabled} multipassId={multipassId} targetId={targetId}");
        }

        private static void RefreshPost(bool __result)
        {
            if (__result != lastRefresh)
            {
                Plugin.Log.LogInfo($"[HdrpDiag] RefreshXrSdk transitioned -> {__result}");
                lastRefresh = __result;
            }
        }

        private static void SetupPre(Camera[] cameras, bool singlePassAllowed, bool singlePassTestModeActive)
        {
            setupCount++;
            if (setupCount != 1 && setupCount % 300 != 0)
                return;

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            bool xrActive = displays.Count > 0 && displays[0].running;
            Plugin.Log.LogInfo($"[HdrpDiag] SetupFrame #{setupCount}: cameras={cameras.Length} xrActive={xrActive} singlePassAllowed={singlePassAllowed} createLayoutHits={createCount}");
            foreach (var cam in cameras)
            {
                if (cam == null) continue;
                var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
                bool gate = cam.cameraType == CameraType.Game && cam.targetTexture == null && (hd == null || hd.xrRendering);
                Plugin.Log.LogInfo($"[HdrpDiag]   cam '{cam.name}' type={cam.cameraType} tt={(cam.targetTexture != null ? cam.targetTexture.name : "null")} xrRendering={(hd != null ? hd.xrRendering.ToString() : "n/a")} => gate={gate}");
            }
        }

        private static void CreatePost()
        {
            createCount++;
            if (!loggedFirstCreate)
            {
                loggedFirstCreate = true;
                Plugin.Log.LogInfo("[HdrpDiag] CreateLayoutFromXrSdk FIRST HIT - HDRP is creating XR passes");
            }
        }
    }
}
