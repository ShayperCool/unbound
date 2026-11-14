using System;
using System.Runtime.InteropServices;
using UnityEngine;

public partial class NativeGfxVulkanProbe
{
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_AllocationEvent();

    void AllocationControls()
    {
        var previous = RenderTexture.active;
        var previousLogs = logs.ToArray();
        logs.Clear();
        foreach (int samples in new[] { 2, 4, 8 })
        foreach (bool bound in new[] { false, true })
        foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf })
        {
            string key = "allocation." + samples + "." + bound + "." + format;
            var rt = new RenderTexture(7, 3, 0, format, RenderTextureReadWrite.Linear)
                { antiAliasing = samples, bindTextureMS = bound };
            Value(key + ".created", B(rt.Create()));
            if (rt.IsCreated())
            {
                NativeGfxVk_SetDeviceBuffer(rt.colorBuffer.GetNativeRenderBufferPtr());
                Action<string> observe = suffix =>
                {
                    NativeGfxVk_Step(key + suffix);
                    GL.IssuePluginEvent(NativeGfxVk_AllocationEvent(), 0);
                };
                observe(".fresh");
                RenderTexture.active = rt;
                GL.Clear(false, true, Color.red);
                RenderTexture.active = previous;
                observe(".cleared");
                rt.GetNativeTexturePtr();
                observe(".pointer");
            }
            RenderTexture.active = previous;
            rt.Release(); DestroyImmediate(rt);
        }
        NativeGfxVk_SetDeviceBuffer(IntPtr.Zero);
        Value("allocation.logs", L(logs));
        logs.Clear(); logs.AddRange(previousLogs);
    }
}
