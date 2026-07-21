using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR;

namespace HotdVR
{
    /// <summary>
    /// Diagnostic: periodically reads back the XR display's mirror-blit source
    /// texture (the same pixels the compositor receives) and writes a PNG next
    /// to the BepInEx log, so we can see what the HMD is being fed.
    /// </summary>
    public class EyeCapture : MonoBehaviour
    {
        private int shot;
        private UnityEngine.Rendering.CommandBuffer paintCmd;

        private IEnumerator Start()
        {
            // StartCoroutine(PaintEyeTargets()); // magenta display-path test (keep for debugging)
            var wait = new WaitForSecondsRealtime(10f);
            while (true)
            {
                yield return wait;
                shot %= 4;
                TryCapture();
            }
        }

        // Bypass HDRP entirely: clear the XR pass render targets to magenta at
        // end of frame. If the HMD/mirror shows magenta, the display/submit
        // side works and the fault is HDRP's write into the eye textures.
        private IEnumerator PaintEyeTargets()
        {
            paintCmd = new UnityEngine.Rendering.CommandBuffer { name = "HotdVR eye paint test" };
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetInstances(displays);
                if (displays.Count == 0 || !displays[0].running) continue;
                var display = displays[0];
                paintCmd.Clear();
                int passCount = display.GetRenderPassCount();
                for (int i = 0; i < passCount; i++)
                {
                    display.GetRenderPass(i, out var pass);
                    paintCmd.SetRenderTarget(pass.renderTarget);
                    paintCmd.ClearRenderTarget(false, true, Color.magenta);
                }
                Graphics.ExecuteCommandBuffer(paintCmd);
            }
        }

        private void TryCapture()
        {
            try
            {
                var displays = new List<XRDisplaySubsystem>();
                SubsystemManager.GetInstances(displays);
                if (displays.Count == 0 || !displays[0].running) return;
                var display = displays[0];

                int mode = display.GetPreferredMirrorBlitMode();
                if (!display.GetMirrorViewBlitDesc(null, out var desc, mode))
                {
                    Plugin.Log.LogWarning($"[EyeCap] no mirror blit desc (mode={mode})");
                    return;
                }
                Plugin.Log.LogInfo($"[EyeCap] blitParams={desc.blitParamsCount} nativeBlit={desc.nativeBlitAvailable} mode={mode}");
                if (desc.blitParamsCount == 0) return;

                desc.GetBlitParameter(0, out var bp);
                var src = bp.srcTex;
                if (src == null)
                {
                    Plugin.Log.LogWarning("[EyeCap] srcTex null");
                    return;
                }
                Plugin.Log.LogInfo($"[EyeCap] srcTex {src.width}x{src.height} dim={src.dimension} slice={bp.srcTexArraySlice} rect={bp.srcRect}");

                int w = Mathf.Min(src.width, 1280);
                int h = Mathf.Min(src.height, 1408);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                if (src.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray)
                    Graphics.Blit(src, rt, bp.srcTexArraySlice, 0);
                else
                    Graphics.Blit(src, rt);

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                string dir = Path.Combine(BepInEx.Paths.GameRootPath, "BepInEx");
                string file = Path.Combine(dir, $"eyecap_{shot}.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Destroy(tex);
                shot++;
                Plugin.Log.LogInfo($"[EyeCap] wrote {file}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[EyeCap] failed: {e.Message}");
            }
        }
    }
}
