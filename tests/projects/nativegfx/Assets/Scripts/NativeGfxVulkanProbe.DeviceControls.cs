using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
public partial class NativeGfxVulkanProbe
{
    [DllImport(Plugin)] static extern void NativeGfxVk_SetDevice(int index, IntPtr pointer);
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_DeviceEvent();
    [DllImport(Plugin)] static extern void NativeGfxVk_SetDeviceBuffer(IntPtr pointer);
    void DeviceLogged(string key, Action action)
    {
        logs.Clear(); action(); Value(key + ".logs", L(logs)); logs.Clear();
    }
    void DeviceControls()
    {
        foreach (int samples in new[] { 1, 2, 4 })
        foreach (int depth in new[] { 0, 16, 24 })
        foreach (bool bound in new[] { false, true })
        {
            string key = "device.msaa." + samples + "." + depth + "." + bound;
            var rt = new RenderTexture(8, 4, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { antiAliasing = samples, bindTextureMS = bound, filterMode = FilterMode.Point };
            DeviceLogged(key + ".create", () => Value(key + ".created", B(rt.Create())));
            var previous = RenderTexture.active;
            if (rt.IsCreated())
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(.125f, .25f, .5f, 1), .375f);
                RenderTexture.active = previous;
            }
            NativeGfxVk_SetDeviceBuffer(rt.IsCreated() && samples > 1 && !bound ? rt.colorBuffer.GetNativeRenderBufferPtr() : IntPtr.Zero);
            NativeGfxVk_SetDevice(0, rt.GetNativeTexturePtr());
            NativeGfxVk_SetDevice(1, rt.GetNativeDepthBufferPtr());
            Value(key + ".equal", B(rt.GetNativeTexturePtr() == rt.GetNativeDepthBufferPtr()));
            NativeGfxVk_Step(key);
            DeviceLogged(key, () => GL.IssuePluginEvent(NativeGfxVk_DeviceEvent(), 0));
            rt.Release(); DestroyImmediate(rt);
        }
        NativeGfxVk_SetDeviceBuffer(IntPtr.Zero);
        DepthOnlyPointers();
        ExplicitDepthFormats();
    }
    void DepthOnlyPointers()
    {
        foreach (int samples in new[] { 1, 2, 4 })
        foreach (bool bound in new[] { false, true })
        {
            string key = "device.depthOnly." + samples + "." + bound;
            var rt = new RenderTexture(8, 4, 16, RenderTextureFormat.Depth)
                { antiAliasing = samples, bindTextureMS = bound };
            DeviceLogged(key + ".create", () => Value(key + ".created", B(rt.Create())));
            NativeGfxVk_SetDevice(0, rt.GetNativeTexturePtr());
            NativeGfxVk_SetDevice(1, rt.GetNativeDepthBufferPtr());
            Value(key + ".equal", B(rt.GetNativeTexturePtr() == rt.GetNativeDepthBufferPtr()));
            NativeGfxVk_Step(key);
            DeviceLogged(key, () => GL.IssuePluginEvent(NativeGfxVk_DeviceEvent(), 0));
            rt.Release(); DestroyImmediate(rt);
        }
    }
    void ExplicitDepthFormats()
    {
        foreach (var format in new[] { UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat,
            UnityEngine.Experimental.Rendering.GraphicsFormat.D32_SFloat_S8_UInt })
        foreach (int samples in new[] { 1, 4 })
        foreach (bool bound in new[] { false, true })
        {
            string key = "device.depthFormat." + format + "." + samples + "." + bound;
            var rt = new RenderTexture(8, 4, 16, RenderTextureFormat.Depth)
                { depthStencilFormat = format, antiAliasing = samples, bindTextureMS = bound };
            DeviceLogged(key + ".create", () => Value(key + ".created", B(rt.Create())));
            NativeGfxVk_SetDevice(0, rt.GetNativeTexturePtr());
            NativeGfxVk_SetDevice(1, rt.GetNativeDepthBufferPtr());
            Value(key + ".equal", B(rt.GetNativeTexturePtr() == rt.GetNativeDepthBufferPtr()));
            NativeGfxVk_Step(key);
            DeviceLogged(key, () => GL.IssuePluginEvent(NativeGfxVk_DeviceEvent(), 0));
            rt.Release(); DestroyImmediate(rt);
        }
    }

}
