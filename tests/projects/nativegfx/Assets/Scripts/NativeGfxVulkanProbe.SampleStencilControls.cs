using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public partial class NativeGfxVulkanProbe
{
    [DllImport(Plugin)] static extern void NativeGfxVk_StencilPosition(int x, int y);
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_StencilAttachmentEvent();
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_StencilChainEvent();
    void SampleStencilControls()
    {
        var previousLogs = logs.ToArray(); logs.Clear();
        var previous = RenderTexture.active;
        var fence = new RenderTexture(1, 1, 0); fence.Create();
        var material = new Material(Shader.Find("Probe/NativeGfx/SampleStencil"));
        try
        {
            foreach (var format in new[] { GraphicsFormat.D24_UNorm_S8_UInt, GraphicsFormat.D32_SFloat_S8_UInt })
            foreach (int samples in new[] { 1, 2, 4, 8 })
            {
                var descriptor = new RenderTextureDescriptor(7, 3) {
                    graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    depthStencilFormat = format, msaaSamples = samples
                };
                var rt = new RenderTexture(descriptor);
                string key = "sampleStencil." + format + "." + samples;
                try
                {
                    Value(key + ".created", B(rt.Create()));
                    if (!rt.IsCreated()) continue;
                    IntPtr pointer = rt.GetNativeDepthBufferPtr();
                    Action<string> read = step => {
                        foreach (int x in new[] { 0, 6 })
                        {
                            NativeGfxVk_StencilPosition(x, 1);
                            NativeGfxVk_StencilPrepare(pointer, 0);
                            GL.IssuePluginEvent(NativeGfxVk_StencilEvent(), eventBase + Outside);
                            Pixels(fence, 1, 1);
                            Value(key + "." + step + ".x" + x, NativeGfxVk_StencilValue().ToString());
                        }
                    };
                    RenderTexture.active = rt; GL.Clear(true, true, Color.black, .75f);
                    RenderTexture.active = previous;
                    material.SetInt("_Mask", 1); material.SetInt("_Ref", 37); material.SetInt("_Half", 0);
                    Graphics.Blit(Texture2D.whiteTexture, rt, material); RenderTexture.active = previous;
                    material.SetInt("_Mask", 254); material.SetInt("_Ref", 91);
                    Graphics.Blit(Texture2D.whiteTexture, rt, material); RenderTexture.active = previous;
                    read("divergent");
                    rt.ResolveAntiAliasedSurface(); read("self");
                    material.SetInt("_Mask", 1); material.SetInt("_Ref", 203); material.SetInt("_Half", 1);
                    Graphics.Blit(Texture2D.whiteTexture, rt, material); RenderTexture.active = previous;
                    read("partial");
                    rt.ResolveAntiAliasedSurface(); read("partialSelf");
                    // Diverge the published image from the samples before the explicit call.
                    NativeGfxVk_StencilPrepare(pointer, 1);
                    GL.IssuePluginEvent(NativeGfxVk_StencilEvent(), eventBase + Outside);
                    Pixels(fence, 1, 1); read("overwritten");
                    rt.ResolveAntiAliasedSurface(); read("forcedSelf");
                    RenderTexture.active = rt; GL.Clear(false, true, Color.blue);
                    RenderTexture.active = previous; read("colorOnly");
                    NativeGfxVk_StencilPrepare(pointer, 1);
                    GL.IssuePluginEvent(NativeGfxVk_StencilEvent(), eventBase + Outside);
                    Pixels(fence, 1, 1); read("colorDrawBefore");
                    Graphics.Blit(Texture2D.whiteTexture, rt); RenderTexture.active = previous;
                    read("colorDraw");
                    RenderTexture.active = rt;
                    GL.IssuePluginEvent(NativeGfxVk_StencilAttachmentEvent(), eventBase + Inside);
                    RenderTexture.active = previous;
                    read("pluginPartial");
                    rt.ResolveAntiAliasedSurface(); read("pluginSelf");
                    NativeGfxVk_StencilPrepare(pointer, 0);
                    RenderTexture.active = rt;
                    GL.IssuePluginEvent(NativeGfxVk_StencilChainEvent(), eventBase + Inside);
                    RenderTexture.active = previous; Pixels(fence, 1, 1);
                    Value(key + ".sameCallbackRead", NativeGfxVk_StencilValue().ToString());
                    read("sameCallbackAfter");
                }
                finally { RenderTexture.active = previous; DestroyImmediate(rt); }
            }
        }
        finally
        {
            NativeGfxVk_StencilPosition(0, 0); NativeGfxVk_StencilDestroy();
            RenderTexture.active = previous; DestroyImmediate(material); DestroyImmediate(fence);
        }
        Value("sampleStencil.logs", L(logs)); logs.Clear(); logs.AddRange(previousLogs);
    }
}
