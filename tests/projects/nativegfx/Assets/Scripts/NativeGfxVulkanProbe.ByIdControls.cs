using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public partial class NativeGfxVulkanProbe
{
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_ByIdBlit();
    [DllImport(Plugin, EntryPoint = "NativeGfxVk_ByIdPointer")] static extern void NativeGfxVk_ByIdPointerNow(IntPtr pointer);
    static void NativeGfxVk_ByIdPointer(IntPtr pointer) { WaitForRenderThread(); NativeGfxVk_ByIdPointerNow(pointer); }
    [DllImport(Plugin, EntryPoint = "NativeGfxVk_ByIdRecord")] static extern int NativeGfxVk_ByIdRecordNow(byte[] buffer, int capacity);
    static int NativeGfxVk_ByIdRecord(byte[] buffer, int capacity) { WaitForRenderThread(); return NativeGfxVk_ByIdRecordNow(buffer, capacity); }

    /// <summary>Recorded last (`byId.*`): a custom blit's source texture ID given to
    /// AccessTextureByID, for ARGB32 and ARGBHalf 7x3 targets of 1, 2, 4 and 8 samples with and
    /// without bindTextureMS: whether its image is the one GetNativeTexturePtr names, its samples,
    /// format, extent and usage, and what the texture samples before and after the plugin clears
    /// that image.</summary>
    void ByIdControls()
    {
        var previousLogs = logs.ToArray();
        var previous = RenderTexture.active;
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf })
        foreach (int samples in new[] { 1, 2, 4, 8 })
        foreach (bool bound in new[] { false, true })
        {
            if (samples == 1 && bound) continue;
            logs.Clear();
            string key = "byId." + format + "." + samples + "." + bound;
            var source = new RenderTexture(7, 3, 0, format, RenderTextureReadWrite.Linear)
                { antiAliasing = samples, bindTextureMS = bound, filterMode = FilterMode.Point };
            var dest = new RenderTexture(7, 3, 0, format, RenderTextureReadWrite.Linear);
            try
            {
                Value(key + ".created", B(source.Create() && dest.Create()));
                if (!source.IsCreated() || !dest.IsCreated()) continue;
                NativeGfxVk_ByIdPointer(source.GetNativeTexturePtr());
                RenderTexture.active = source;
                GL.Clear(false, true, new Color(.75f, .5f, .25f, 1));
                RenderTexture.active = previous;
                MsaaControls.Sample(Value, key + ".before", source, bound);
                var cmd = new CommandBuffer();
                try
                {
                    cmd.IssuePluginCustomBlit(NativeGfxVk_ByIdBlit(), 17, source, dest, 0, 0);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                finally { cmd.Release(); }
                var bytes = new byte[512];
                int length = NativeGfxVk_ByIdRecord(bytes, bytes.Length);
                Value(key + ".native", Q(Encoding.UTF8.GetString(bytes, 0, Math.Min(length, bytes.Length - 1))));
                MsaaControls.Sample(Value, key + ".after", source, bound);
                Value(key + ".logs", L(logs));
            }
            finally
            {
                RenderTexture.active = previous;
                DestroyImmediate(source);
                DestroyImmediate(dest);
            }
        }
        logs.Clear();
        logs.AddRange(previousLogs);
    }
}
