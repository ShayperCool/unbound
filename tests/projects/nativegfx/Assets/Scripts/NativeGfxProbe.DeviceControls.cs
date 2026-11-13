using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public partial class NativeGfxProbe
{
    void DeviceControls()
    {
        foreach (int samples in new[] { 1, 2, 4 })
        foreach (int depth in new[] { 0, 16, 24 })
        foreach (bool bound in new[] { false, true })
        {
            string key = "device.msaa." + samples + "." + depth + "." + bound;
            var rt = new RenderTexture(8, 4, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { antiAliasing = samples, bindTextureMS = bound, filterMode = FilterMode.Point };
            Logged(key + ".create", () => Value(key + ".created", B(rt.Create())));
            var previous = RenderTexture.active;
            if (rt.IsCreated())
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(.125f, .25f, .5f, 1), .375f);
                RenderTexture.active = previous;
            }
            for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
            NativeGfx_SetShape(0, "color", rt.GetNativeTexturePtr());
            NativeGfx_SetShape(1, "depth", rt.GetNativeDepthBufferPtr());
            Value(key + ".equal", B(rt.GetNativeTexturePtr() == rt.GetNativeDepthBufferPtr()));
            Logged(key, () => Value(key + ".images", L(TextureEvent(0))));
            if (depth > 0 && (!bound || samples == 1))
                Logged(key + ".read", () => Value(key + ".depth", L(TextureEvent(5))));
            if (depth > 0 && (!bound || samples == 1) && rt.IsCreated())
            {
                var material = new Material(Shader.Find("Probe/NativeGfx/DeviceDepth"));
                Graphics.Blit(Texture2D.whiteTexture, rt, material);
                DestroyImmediate(material);
                Logged(key + ".drawRead", () => Value(key + ".drawDepth", L(TextureEvent(5))));
                var destination = new RenderTexture(8, 4, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                destination.Create();
                rt.ResolveAntiAliasedSurface(destination);
                Logged(key + ".destinationRead", () => Value(key + ".destinationDepth", L(TextureEvent(5))));
                destination.Release(); DestroyImmediate(destination);
                RenderTexture.active = rt;
                GL.Viewport(new Rect(4, 0, 4, 4));
                GL.Clear(true, false, Color.black, .625f);
                GL.Viewport(new Rect(0, 0, 8, 4));
                RenderTexture.active = previous;
                Logged(key + ".partialRead", () => Value(key + ".partialDepth", L(TextureEvent(5))));
                var redraw = new Material(Shader.Find("Probe/NativeGfx/DeviceDepth"));
                Graphics.Blit(Texture2D.whiteTexture, rt, redraw);
                DestroyImmediate(redraw);
                Logged(key + ".beforeResolveRead", () => Value(key + ".beforeResolveDepth", L(TextureEvent(5))));
                rt.ResolveAntiAliasedSurface();
                Logged(key + ".resolveRead", () => Value(key + ".resolveDepth", L(TextureEvent(5))));
                var sample = new Material(Shader.Find("Probe/NativeGfx/DeviceDepthSample"));
                sample.SetTexture("_Depth", rt, RenderTextureSubElement.Depth);
                var readback = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit(Texture2D.whiteTexture, readback, sample);
                Logged(key + ".sampleRead", () => Value(key + ".sampleDepth", L(TextureEvent(5))));
                DestroyImmediate(sample); RenderTexture.ReleaseTemporary(readback);

            }
            rt.Release();
            DestroyImmediate(rt);
        }
        DepthOnlyPointers();
        ExplicitDepthFormats();
        SampleResolveControl();
        ImplicitDepthControl();
    }
    void SampleResolveControl()
    {
        foreach (int samples in new[] { 2, 4, 8 })
        {
            string key = "device.sampleResolve." + samples;
            var rt = new RenderTexture(8, 4, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = samples };
            rt.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = rt; GL.Clear(true, true, Color.black, .75f);
            RenderTexture.active = previous;
            var material = new Material(Shader.Find("Probe/NativeGfx/DeviceSamples"));
            Graphics.Blit(Texture2D.whiteTexture, rt, material);
            for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
            NativeGfx_SetShape(1, "depth", rt.GetNativeDepthBufferPtr());
            Logged(key + ".before", () => Value(key + ".beforeDepth", L(TextureEvent(5))));
            rt.ResolveAntiAliasedSurface();
            Logged(key + ".after", () => Value(key + ".afterDepth", L(TextureEvent(5))));
            Value(key + ".sampled", Q(SampleDepth(rt)));
            DestroyImmediate(material); rt.Release(); DestroyImmediate(rt);
        }
    }
    string SampleDepth(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var sampling = new Material(Shader.Find("Probe/NativeGfx/DeviceDepthSample"));
        sampling.SetTexture("_Depth", rt, RenderTextureSubElement.Depth);
        var output = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        output.Create(); Graphics.Blit(Texture2D.whiteTexture, output, sampling);
        var cpu = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        RenderTexture.active = output; cpu.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        RenderTexture.active = previous;
        string value = cpu.GetPixel(0, 0).r.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        DestroyImmediate(cpu); DestroyImmediate(output); DestroyImmediate(sampling);
        return value;
    }
    void ImplicitDepthControl()
    {
        foreach (int samples in new[] { 2, 4, 8 })
        {
            string key = "device.depthRead." + samples;
            var rt = new RenderTexture(8, 4, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = samples };
            rt.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = rt; GL.Clear(true, true, Color.black, .75f); RenderTexture.active = previous;
            var material = new Material(Shader.Find("Probe/NativeGfx/DeviceSamples"));
            Graphics.Blit(Texture2D.whiteTexture, rt, material);
            for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
            NativeGfx_SetShape(1, "depth", rt.GetNativeDepthBufferPtr());
            Logged(key + ".before", () => Value(key + ".beforeDepth", L(TextureEvent(5))));
            Value(key + ".sampled", Q(SampleDepth(rt)));
            Logged(key + ".after", () => Value(key + ".afterDepth", L(TextureEvent(5))));
            DestroyImmediate(material); rt.Release(); DestroyImmediate(rt);
        }
    }
    void DepthOnlyPointers()
    {
        foreach (int samples in new[] { 1, 2, 4 })
        foreach (bool bound in new[] { false, true })
        {
            string key = "device.depthOnly." + samples + "." + bound;
            var rt = new RenderTexture(8, 4, 16, RenderTextureFormat.Depth)
                { antiAliasing = samples, bindTextureMS = bound };
            Logged(key + ".create", () => Value(key + ".created", B(rt.Create())));
            for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
            NativeGfx_SetShape(0, "color", rt.GetNativeTexturePtr());
            NativeGfx_SetShape(1, "depth", rt.GetNativeDepthBufferPtr());
            Value(key + ".equal", B(rt.GetNativeTexturePtr() == rt.GetNativeDepthBufferPtr()));
            Logged(key, () => Value(key + ".images", L(TextureEvent(0))));
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
            Logged(key + ".create", () => Value(key + ".created", B(rt.Create())));
            for (int i = 0; i < 32; i++) NativeGfx_SetShape(i, "", IntPtr.Zero);
            NativeGfx_SetShape(0, "color", rt.GetNativeTexturePtr());
            NativeGfx_SetShape(1, "depth", rt.GetNativeDepthBufferPtr());
            Value(key + ".equal", B(rt.GetNativeTexturePtr() == rt.GetNativeDepthBufferPtr()));
            Logged(key, () => Value(key + ".images", L(TextureEvent(0))));
            rt.Release(); DestroyImmediate(rt);
        }
    }

}
