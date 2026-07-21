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
        private static string prevCameraSet = "";

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
                prefix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(TryCullValidatePre)),
                postfix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(TryCullPost)));
            harmony.Patch(AccessTools.Method(hdrp, "ExecuteWithRenderGraph"),
                prefix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(ExecutePre)),
                postfix: new HarmonyMethod(typeof(HdrpXrDiag), nameof(ExecutePost)));
            Plugin.Log.LogInfo("[HdrpDiag] XRSystem+HDRP patches applied");
        }

        private static int calcCount, cullCount, execCount;
        private static string lastCameraSet = "";
        private static float lastInvalidLog;

        private static bool IsFinite(in Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++)
                if (float.IsNaN(m[i]) || float.IsInfinity(m[i]))
                    return false;
            return true;
        }

        internal static int skippedFrames, renderedFrames, repairedFrames;
        private static bool loggedMatrixDetail;
        private static float lastRepairLog;
        private static readonly Dictionary<int, CachedCulling> lastValidCulling = new Dictionary<int, CachedCulling>();

        private struct CachedCulling
        {
            public int frame;
            public UnityEngine.Rendering.ScriptableCullingParameters cullingParams;
        }

        // The OpenVR XR provider returns NaN culling matrices for the second
        // multipass eye (and for all passes during scene-load hitches). Feeding
        // them to the native renderer crashes in ScriptableRenderContext.Submit.
        // Repair: substitute the last valid culling params seen for the same
        // camera this/last few frames (in practice the left eye's - the culling
        // frustums differ only by the eye offset). If nothing recent is cached,
        // skip the camera render for that frame.
        private static bool TryCullValidatePre(Camera camera, ref UnityEngine.Rendering.ScriptableCullingParameters cullingParams, ref bool __result)
        {
            var cull = cullingParams.cullingMatrix;
            var stereoView = cullingParams.stereoViewMatrix;
            var stereoProj = cullingParams.stereoProjectionMatrix;
            bool valid = IsFinite(cull) && IsFinite(stereoView) && IsFinite(stereoProj)
                         && Mathf.Abs(cull.determinant) > 1e-12f;
            int camId = camera.GetInstanceID();

            if (valid)
            {
                lastValidCulling[camId] = new CachedCulling { frame = Time.frameCount, cullingParams = cullingParams };
                renderedFrames++;
                return true;
            }

            if (lastValidCulling.TryGetValue(camId, out var cached) && Time.frameCount - cached.frame <= 5)
            {
                cullingParams = cached.cullingParams;
                repairedFrames++;
                if (Time.realtimeSinceStartup - lastRepairLog > 10f || VRCameraGate.InDiagWindow)
                {
                    lastRepairLog = Time.realtimeSinceStartup;
                    Plugin.Log.LogInfo($"[HdrpDiag] repaired degenerate culling with cached params f={Time.frameCount} cam='{camera.name}' (repaired={repairedFrames} rendered={renderedFrames} skipped={skippedFrames})");
                }
                return true;
            }

            skippedFrames++;
            if (Time.realtimeSinceStartup - lastInvalidLog > 5f || VRCameraGate.InDiagWindow)
            {
                lastInvalidLog = Time.realtimeSinceStartup;
                Plugin.Log.LogWarning($"[HdrpDiag] DEGENERATE culling params, no recent cache - skipping render f={Time.frameCount} cam='{camera.name}' (skipped={skippedFrames} rendered={renderedFrames} repaired={repairedFrames})");
                if (!loggedMatrixDetail)
                {
                    loggedMatrixDetail = true;
                    Plugin.Log.LogWarning($"[HdrpDiag] cullingMatrix det={cull.determinant} finite={IsFinite(cull)}\n{cull}");
                    Plugin.Log.LogWarning($"[HdrpDiag] stereoView finite={IsFinite(stereoView)}\n{stereoView}");
                    Plugin.Log.LogWarning($"[HdrpDiag] stereoProj finite={IsFinite(stereoProj)}\n{stereoProj}");
                }
            }
            __result = false;
            return false; // skip original TryCull
        }

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

        // Main-thread time spent inside ExecuteWithRenderGraph (render-graph
        // compile + command submission), summed across the calls of one frame
        // (multipass = 2 XR calls + any flat cameras). NOT GPU time, but the
        // right number for attributing HDRP-side CPU cost per frame.
        private static readonly FrameStats renderStats = new FrameStats();
        private static readonly double msPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private static long execStartTicks;
        private static int execFrame = -1;
        private static float frameAccumMs;
        private static bool frameWasSuspended;

        internal static string EmitRenderStats() => renderStats.EmitAndReset("render(cpu-submit)");

        private static void ExecutePost()
        {
            if (execStartTicks != 0)
            {
                frameAccumMs += (float)((System.Diagnostics.Stopwatch.GetTimestamp() - execStartTicks) * msPerTick);
                execStartTicks = 0;
            }
        }

        private static void ExecutePre(object renderRequest)
        {
            if (VRRuntimeBootstrap.Active && Plugin.Cfg.FrameTimeStats.Value)
            {
                int f = Time.frameCount;
                if (f != execFrame)
                {
                    if (execFrame >= 0)
                        renderStats.Add(frameAccumMs, frameWasSuspended);
                    execFrame = f;
                    frameAccumMs = 0f;
                    frameWasSuspended = VRCameraGate.InLoadingEpisode;
                }
                execStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            execCount++;
            // Un-throttled inside the gate's diagnostic window: the resume
            // frames after a load are where the transition crash lived, and
            // this line (camera/pass/target) is what attributes it.
            if (execCount % 300 >= 3 && execCount > 10 && !VRCameraGate.InDiagWindow) return;
            var tr = Traverse.Create(renderRequest);
            var hdCam = tr.Field("hdCamera");
            string camName = hdCam.Property("camera").GetValue<Camera>()?.name ?? "?";
            var xr = hdCam.Property("xr");
            bool xrEnabled = xr.Property("enabled").GetValue<bool>();
            int multipassId = xrEnabled ? xr.Property("multipassId").GetValue<int>() : -1;
            var target = tr.Field("target");
            var targetId = target.Field("id").GetValue();
            Plugin.Log.LogInfo($"[HdrpDiag] Execute #{execCount} f={Time.frameCount} cam='{camName}' xrEnabled={xrEnabled} multipassId={multipassId} targetId={targetId}");
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
            VRCameraGate.Enforce(cameras);

            // Log camera composition changes (scene transitions). The game
            // alternates two camera sets every frame during loads - suppress
            // A/B/A/B flapping by only logging sets unseen in the last two.
            var names = new List<string>();
            foreach (var c in cameras)
                if (c != null) names.Add(c.name);
            names.Sort();
            string set = string.Join("|", names);
            if (set != lastCameraSet && set != prevCameraSet)
                Plugin.Log.LogInfo($"[HdrpDiag] camera set changed (frame {setupCount}, f={Time.frameCount}): [{set}]");
            if (set != lastCameraSet)
            {
                prevCameraSet = lastCameraSet;
                lastCameraSet = set;
            }

            // Per-frame camera-set timeline across gate transitions, even when
            // the A/B flap suppression above hides the set-change lines.
            if (VRCameraGate.InDiagWindow && Plugin.Cfg.VerboseLogging.Value)
                Plugin.Log.LogInfo($"[HdrpDiag] f={Time.frameCount} setup cams=[{set}] suspended={VRCameraGate.LoadingScreenActive} vrCam='{(VRCameraGate.CurrentVRCamera != null ? VRCameraGate.CurrentVRCamera.name : "<none>")}'");

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
