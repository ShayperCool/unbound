using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Normalized 16-bit, packed 10-10-10-2 and integer textures and render textures: what the device
/// says it supports per usage, and the exact bits of clears, shader and kernel writes, blending,
/// point and bilinear sampling, CPU texels, ReadPixels and AsyncGPUReadback. Every texture is 2x1:
/// texel 0 gets A, texel 1 gets B. Values go to <c>TEXTURE_FORMATS_PROBE_OUT</c> (a JSON-lines
/// file), the last record is <c>complete: true</c>.
/// </summary>
public static class TexFormatsProbe
{
    static readonly List<string> Rows = new List<string>();
    static readonly List<Object> Owned = new List<Object>();
    static readonly Color A = new Color(0.1234567f, 0.7654321f, 0.5f, 0.6789f);
    static readonly Color B = new Color(1.25f, -0.25f, 0.000004f, 0.33333334f);
    static readonly Color Over = new Color(0.9f, 0.2f, 0.3f, 0.25f);
    static readonly Color Sentinel = new Color(9f, 9f, 9f, 9f);
    // Draws never bind a texture, so no target is sampled while it is drawn into (except in
    // Feedback, whose material samples its target on purpose).
    static Material s_Mat, s_Sample, s_Load, s_Feed;
    static ComputeShader s_Kernels;
    static RenderTexture s_Out2, s_Out1;
    /// <summary><c>TEXTURE_FORMATS_PROBE_ONLY</c>: run only the cases whose key starts with one of
    /// its comma-separated prefixes.</summary>
    static string s_Only;
    static bool Runs(string key) => string.IsNullOrEmpty(s_Only)
        || Array.Exists(s_Only.Split(','), p => key.StartsWith(p + ".", StringComparison.Ordinal) || key == p);

    static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static void Value(string key, string json) => Rows.Add("{\"k\":" + Json(key) + ",\"v\":" + json + "}");
    static void Try(string key, Func<string> json)
    {
        try { Value(key, json()); }
        catch (Exception e) { Value(key, Json("throws:" + e.GetType().Name)); }
    }
    static string B2(bool b) => b ? "true" : "false";
    static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return Json(sb.ToString());
    }
    static string Bits(Color c)
    {
        string s = "";
        for (int i = 0; i < 4; i++) s += (i == 0 ? "" : ",") + BitConverter.SingleToInt32Bits(c[i]).ToString("x8", CultureInfo.InvariantCulture);
        return s;
    }
    static string Texels(Texture2D t) => Json(Bits(t.GetPixel(0, 0)) + ";" + Bits(t.GetPixel(1, 0)));
    static T Own<T>(T o) where T : Object { Owned.Add(o); return o; }

    /// <summary>The usages the device reports for a format, and its compatible formats.</summary>
    static void Support(string key, GraphicsFormat format)
    {
        Value(key + ".graphicsFormat", Json(format.ToString()));
        var usages = new List<string>();
        foreach (GraphicsFormatUsage usage in Enum.GetValues(typeof(GraphicsFormatUsage)))
            if (usage != GraphicsFormatUsage.None && SystemInfo.IsFormatSupported(format, usage)) usages.Add(Json(usage.ToString()));
        Value(key + ".usages", "[" + string.Join(",", usages) + "]");
        Value(key + ".compatible.render", Json(SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Render).ToString()));
        Value(key + ".compatible.sample", Json(SystemInfo.GetCompatibleFormat(format, GraphicsFormatUsage.Sample).ToString()));
    }

    static void Clear(RenderTexture rt, Color color)
    {
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, color);
        RenderTexture.active = active;
    }

    /// <summary>Both texels through ReadPixels into RGBAFloat (over a sentinel), the raw bytes of
    /// AsyncGPUReadback's default request and its RGBAFloat request.</summary>
    static void Read(string key, RenderTexture rt)
    {
        ReadPixels(key, rt);
        ReadBack(key, rt);
    }

    /// <summary>AsyncGPUReadback only: ReadPixels from an integer target is a GL error on the
    /// Editor's device and leaves stale texels, which are not recorded.</summary>
    static void ReadBack(string key, RenderTexture rt)
    {
        Try(key + ".async", () => Readback(AsyncGPUReadback.Request(rt)));
        Try(key + ".asyncfloat", () => Readback(AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat)));
    }

    static void ReadPixels(string key, RenderTexture rt)
    {
        Try(key + ".read", () =>
        {
            var t = Own(new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true));
            t.SetPixels(new[] { Sentinel, Sentinel });
            t.Apply();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            t.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
            t.Apply();
            RenderTexture.active = active;
            return Texels(t);
        });
    }

    static string Readback(AsyncGPUReadbackRequest request)
    {
        request.WaitForCompletion();
        if (request.hasError) return Json("error");
        return Hex(request.GetData<byte>().ToArray());
    }

    static string Sampled(Texture source, bool filter)
    {
        var target = filter ? s_Out1 : s_Out2;
        var mode = source.filterMode;
        source.filterMode = filter ? FilterMode.Bilinear : FilterMode.Point;
        Graphics.Blit(source, target, s_Sample, 3);
        source.filterMode = mode;
        return Floats(target);
    }

    static string Floats(RenderTexture target)
    {
        var t = Own(new Texture2D(target.width, 1, TextureFormat.RGBAFloat, false, true));
        var active = RenderTexture.active;
        RenderTexture.active = target;
        t.ReadPixels(new Rect(0, 0, target.width, 1), 0, 0);
        t.Apply();
        RenderTexture.active = active;
        return target.width == 1 ? Json(Bits(t.GetPixel(0, 0))) : Texels(t);
    }

    static RenderTexture Target(RenderTextureDescriptor d)
    {
        var rt = Own(new RenderTexture(d));
        rt.filterMode = FilterMode.Point;
        return rt;
    }

    static RenderTextureDescriptor Descriptor(GraphicsFormat format) =>
        new RenderTextureDescriptor(2, 1, format, 0) { msaaSamples = 1, useMipMap = false, sRGB = false };

    // ------------------------------------------------------------------ normalized render textures

    static void NormTarget(string key, RenderTextureFormat format)
    {
        Value(key + ".supported", B2(SystemInfo.SupportsRenderTextureFormat(format)));
        Value(key + ".blending", B2(SystemInfo.SupportsBlendingOnRenderTextureFormat(format)));
        Value(key + ".randomWrite", B2(SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format)));
        var rt = Own(new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear));
        rt.filterMode = FilterMode.Point;
        Support(key, rt.graphicsFormat);
        Value(key + ".create", B2(rt.Create()));
        Clear(rt, A);
        Read(key + ".clear", rt);
        s_Mat.SetVector("_Color", B);
        Graphics.Blit(null, rt, s_Mat, 0);
        Read(key + ".solid", rt);
        s_Mat.SetVector("_Color", A);
        s_Mat.SetVector("_Color2", B);
        Graphics.Blit(null, rt, s_Mat, 2);
        Read(key + ".split", rt);
        // ReadPixels into a normalized 16-bit texture: its raw texels.
        Try(key + ".split.read64", () =>
        {
            var t = Own(new Texture2D(2, 1, TextureFormat.RGBA64, false, true));
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            t.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
            RenderTexture.active = active;
            return Hex(t.GetRawTextureData());
        });
        Try(key + ".sample", () => Sampled(rt, false));
        Try(key + ".filter", () => Sampled(rt, true));
        Clear(rt, A);
        s_Mat.SetVector("_Color", Over);
        Graphics.Blit(null, rt, s_Mat, 1);
        Read(key + ".blend", rt);

        var d = rt.descriptor;
        d.enableRandomWrite = true;
        var rw = Target(d);
        Value(key + ".kernel.create", B2(rw.Create()));
        Try(key + ".kernel.dispatch", () =>
        {
            int kernel = s_Kernels.FindKernel("WriteNorm");
            s_Kernels.SetTexture(kernel, "_Norm", rw);
            s_Kernels.Dispatch(kernel, 1, 1, 1);
            return "true";
        });
        Read(key + ".kernel", rw);

        d = rt.descriptor;
        d.msaaSamples = 4;
        var msaa = Target(d);
        Value(key + ".msaa.create", B2(msaa.Create()));
        s_Mat.SetVector("_Color", A);
        s_Mat.SetVector("_Color2", B);
        Graphics.Blit(null, msaa, s_Mat, 2);
        Read(key + ".msaa", msaa);
        // Once sampled (which resolves it), the readback again.
        Try(key + ".msaa.sampled", () => Sampled(msaa, false));
        ReadBack(key + ".msaa.sampled", msaa);
        // A second write must not replace the last resolved image until a resolve is requested.
        s_Mat.SetVector("_Color", new Color(.2f, .4f, .6f, .8f));
        Graphics.Blit(null, msaa, s_Mat, 0);
        Read(key + ".msaa.history.redraw", msaa);
        msaa.ResolveAntiAliasedSurface();
        ReadBack(key + ".msaa.history.explicit", msaa);
        s_Mat.SetVector("_Color", Color.red);
        Graphics.Blit(null, msaa, s_Mat, 0);
        ReadBack(key + ".msaa.history.unsampled", msaa);
        Try(key + ".msaa.history.sample", () => Sampled(msaa, false));
        ReadBack(key + ".msaa.history.afterSample", msaa);
        var destinationDescriptor = msaa.descriptor;
        destinationDescriptor.msaaSamples = 1;
        var destination = Target(destinationDescriptor);
        destination.Create();
        s_Mat.SetVector("_Color", Color.blue);
        Graphics.Blit(null, msaa, s_Mat, 0);
        // Bind a source to a pass that does not read it.
        Graphics.Blit(msaa, destination, s_Mat, 0);
        ReadBack(key + ".msaa.history.ignoredSource", msaa);
        s_Mat.SetVector("_Color", Color.green);
        Graphics.Blit(null, msaa, s_Mat, 0);
        msaa.ResolveAntiAliasedSurface(destination);
        ReadBack(key + ".msaa.history.destinationSource", msaa);
        Read(key + ".msaa.history.destination", destination);
        Try(key + ".msaa.history.destinationSample", () => Sampled(msaa, false));
        ReadBack(key + ".msaa.history.afterDestinationSample", msaa);
        s_Mat.SetVector("_Color", Color.yellow);
        Graphics.Blit(null, msaa, s_Mat, 0);
        int loadMsaa = s_Kernels.FindKernel("ReadNorm");
        s_Kernels.SetTexture(loadMsaa, "_Source", msaa);
        ReadBack(key + ".msaa.history.computeBound", msaa);
        using (var pixels = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 16))
        {
            s_Kernels.SetBuffer(loadMsaa, "_Sampled", pixels);
            s_Kernels.Dispatch(loadMsaa, 1, 1, 1);
            var colors = new Color[2]; pixels.GetData(colors);
            Value(key + ".msaa.history.computeLoad", Json(Bits(colors[0]) + ";" + Bits(colors[1])));
        }
        ReadBack(key + ".msaa.history.computeDispatched", msaa);
        ReadPixels(key + ".msaa.history.computeCurrent", msaa);
        RenderTexture.active = destination;
        ReadBack(key + ".msaa.history.targetBound", msaa);
        GL.Clear(false, true, Color.magenta);
        ReadBack(key + ".msaa.history.targetCleared", msaa);
        s_Mat.SetVector("_Color", Color.cyan);
        Graphics.Blit(null, msaa, s_Mat, 0);
        s_Mat.SetTexture("_MainTex", null);
        Graphics.Blit(null, destination, s_Mat, 0);
        ReadBack(key + ".msaa.history.otherDraw", msaa);



    }

    // ------------------------------------------------------------------ integer render textures

    struct IntCase
    {
        public string Key;
        public GraphicsFormat Format;
        public RenderTextureFormat? Legacy;
        public bool Signed;
        public int Bits;
    }

    static readonly IntCase[] IntTargets =
    {
        new IntCase { Key = "RInt", Legacy = RenderTextureFormat.RInt, Signed = true, Bits = 32 },
        new IntCase { Key = "RGInt", Legacy = RenderTextureFormat.RGInt, Signed = true, Bits = 32 },
        new IntCase { Key = "ARGBInt", Legacy = RenderTextureFormat.ARGBInt, Signed = true, Bits = 32 },
        new IntCase { Key = "RGBAUShort", Legacy = RenderTextureFormat.RGBAUShort, Signed = false, Bits = 16 },
        new IntCase { Key = "R8_UInt", Format = GraphicsFormat.R8_UInt, Signed = false, Bits = 8 },
        new IntCase { Key = "R8_SInt", Format = GraphicsFormat.R8_SInt, Signed = true, Bits = 8 },
        new IntCase { Key = "R8G8B8A8_UInt", Format = GraphicsFormat.R8G8B8A8_UInt, Signed = false, Bits = 8 },
        new IntCase { Key = "R8G8B8A8_SInt", Format = GraphicsFormat.R8G8B8A8_SInt, Signed = true, Bits = 8 },
        new IntCase { Key = "R16_UInt", Format = GraphicsFormat.R16_UInt, Signed = false, Bits = 16 },
        new IntCase { Key = "R16_SInt", Format = GraphicsFormat.R16_SInt, Signed = true, Bits = 16 },
        new IntCase { Key = "R16G16_SInt", Format = GraphicsFormat.R16G16_SInt, Signed = true, Bits = 16 },
        new IntCase { Key = "R16G16B16A16_SInt", Format = GraphicsFormat.R16G16B16A16_SInt, Signed = true, Bits = 16 },
        new IntCase { Key = "R32_UInt", Format = GraphicsFormat.R32_UInt, Signed = false, Bits = 32 },
        new IntCase { Key = "R32G32B32A32_UInt", Format = GraphicsFormat.R32G32B32A32_UInt, Signed = false, Bits = 32 },
    };

    /// A full-target quad drawn with <paramref name="pass"/> of the probe's shader into the active
    /// <paramref name="rt"/>, then a clear of its color (to the integer clear color) or of its
    /// depth only, with no change of target in between.
    static void DrawThenClear(RenderTexture rt, int pass, bool color)
    {
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        GL.LoadOrtho();
        s_Mat.SetPass(pass);
        GL.Begin(GL.QUADS);
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.End();
        GL.PopMatrix();
        GL.Clear(!color, color, new Color(3f, -2f, 1000.5f, 0.5f));
        RenderTexture.active = active;
    }

    static int WritePass(IntCase c) => 6 + (c.Bits == 8 ? 0 : c.Bits == 16 ? 2 : 4) + (c.Signed ? 0 : 1);
    static string Kernel(IntCase c) => "Write" + (c.Signed ? "Int" : "UInt") + c.Bits;

    static string Loaded(Texture source, bool signed)
    {
        s_Load.SetTexture(signed ? "_IntTex" : "_UIntTex", source);
        Graphics.Blit(null, s_Out2, s_Load, signed ? 4 : 5);
        s_Load.SetTexture(signed ? "_IntTex" : "_UIntTex", null);
        return Floats(s_Out2);
    }

    static void IntTarget(IntCase c)
    {
        string key = "rt." + c.Key;
        RenderTexture rt;
        if (c.Legacy.HasValue)
        {
            var format = c.Legacy.Value;
            Value(key + ".supported", B2(SystemInfo.SupportsRenderTextureFormat(format)));
            Value(key + ".blending", B2(SystemInfo.SupportsBlendingOnRenderTextureFormat(format)));
            Value(key + ".randomWrite", B2(SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format)));
            rt = Own(new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear));
            rt.filterMode = FilterMode.Point;
        }
        else rt = Target(Descriptor(c.Format));
        Support(key, rt.graphicsFormat);
        Value(key + ".create", B2(rt.Create()));
        Clear(rt, new Color(3f, -2f, 1000.5f, 0.5f));
        ReadBack(key + ".clear", rt);
        Try(key + ".clear.load", () => Loaded(rt, c.Signed));
        Graphics.Blit(null, rt, s_Mat, WritePass(c));
        ReadBack(key + ".draw", rt);
        Try(key + ".load", () => Loaded(rt, c.Signed));
        // A target with depth: drawn into, then cleared in color, then drawn into and cleared in
        // depth only, while it stays the active target.
        var deep = rt.descriptor;
        deep.depthBufferBits = 24;
        var withDepth = Target(deep);
        Try(key + ".redraw.clear.load", () =>
        {
            DrawThenClear(withDepth, WritePass(c), true);
            return Loaded(withDepth, c.Signed);
        });
        Try(key + ".redraw.depthclear.load", () =>
        {
            DrawThenClear(withDepth, WritePass(c), false);
            return Loaded(withDepth, c.Signed);
        });

        var d = rt.descriptor;
        d.enableRandomWrite = true;
        var rw = Target(d);
        Value(key + ".kernel.create", B2(rw.Create()));
        Try(key + ".kernel.dispatch", () =>
        {
            int kernel = s_Kernels.FindKernel(Kernel(c));
            s_Kernels.SetTexture(kernel, c.Signed ? "_Int" : "_UInt", rw);
            s_Kernels.Dispatch(kernel, 1, 1, 1);
            return "true";
        });
        ReadBack(key + ".kernel", rw);
        Try(key + ".kernel.load", () => Loaded(rw, c.Signed));
    }

    // ------------------------------------------------------------------ feedback

    /// A draw whose material samples the target it draws into: through Blit's <c>_MainTex</c>
    /// (source and destination the same texture) and through another property. Each pass writes
    /// the sampled texel swizzled (yzwx), over a target holding A and B.
    static void Feedback(string key, RenderTextureFormat format)
    {
        var rt = Own(new RenderTexture(2, 1, 0, format, RenderTextureReadWrite.Linear));
        rt.filterMode = FilterMode.Point;
        Value(key + ".create", B2(rt.Create()));
        s_Mat.SetVector("_Color", A);
        s_Mat.SetVector("_Color2", B);
        Graphics.Blit(null, rt, s_Mat, 2);
        Graphics.Blit(rt, rt, s_Feed, 12);
        Read(key + ".blit", rt);
        Graphics.Blit(null, rt, s_Mat, 2);
        s_Feed.SetTexture("_FeedTex", rt);
        Graphics.Blit(null, rt, s_Feed, 13);
        s_Feed.SetTexture("_FeedTex", null);
        Read(key + ".property", rt);
    }

    // ------------------------------------------------------------------ textures

    static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(i * 37 + 11);
        return bytes;
    }

    /// <summary>A Texture2D: CPU texels and raw data after SetPixel, the GPU's point and bilinear
    /// samples, raw data set through SetPixelData, ReadPixels from an RGBAFloat target and
    /// AsyncGPUReadback.</summary>
    static void NormTexture(string key, Func<Texture2D> make, bool hasTextureFormat)
    {
        Texture2D t;
        try { t = Own(make()); }
        catch (Exception e) { Value(key + ".new", Json("throws:" + e.GetType().Name)); return; }
        // A format the device cannot sample logs an error and leaves a destroyed texture.
        Value(key + ".new", B2(t != null));
        if (t == null) return;
        t.filterMode = FilterMode.Point;
        Support(key, t.graphicsFormat);
        if (hasTextureFormat) Value(key + ".supported", B2(SystemInfo.SupportsTextureFormat(t.format)));
        Value(key + ".format", Json(t.format.ToString()));
        Try(key + ".setpixel", () => { t.SetPixel(0, 0, A); t.SetPixel(1, 0, B); return Texels(t); });
        Try(key + ".setpixel.raw", () => Hex(t.GetRawTextureData()));
        Try(key + ".apply", () => { t.Apply(false, false); return "true"; });
        Try(key + ".sample", () => Sampled(t, false));
        Try(key + ".filter", () => Sampled(t, true));
        Try(key + ".async", () => Readback(AsyncGPUReadback.Request(t)));
        Try(key + ".pixeldata", () =>
        {
            t.SetPixelData(Pattern(t.GetRawTextureData().Length), 0);
            return Texels(t);
        });
        Try(key + ".pixeldata.apply", () => { t.Apply(false, false); return "true"; });
        Try(key + ".pixeldata.sample", () => Sampled(t, false));
        Try(key + ".readpixels", () =>
        {
            s_Mat.SetVector("_Color", A);
            s_Mat.SetVector("_Color2", B);
            Graphics.Blit(null, s_Out2, s_Mat, 2);
            var active = RenderTexture.active;
            RenderTexture.active = s_Out2;
            t.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
            RenderTexture.active = active;
            return Texels(t);
        });
        Try(key + ".readpixels.raw", () => Hex(t.GetRawTextureData()));
    }

    static void IntTexture(string key, GraphicsFormat format, bool signed)
    {
        Texture2D t;
        try { t = Own(new Texture2D(2, 1, format, TextureCreationFlags.None)); }
        catch (Exception e) { Value(key + ".new", Json("throws:" + e.GetType().Name)); return; }
        Value(key + ".new", B2(t != null));
        if (t == null) return;
        t.filterMode = FilterMode.Point;
        Support(key, t.graphicsFormat);
        Value(key + ".format", Json(t.format.ToString()));
        Try(key + ".pixeldata", () =>
        {
            t.SetPixelData(Pattern(t.GetRawTextureData().Length), 0);
            return Hex(t.GetRawTextureData());
        });
        Try(key + ".getpixel", () => Texels(t));
        Try(key + ".apply", () => { t.Apply(false, false); return "true"; });
        Try(key + ".load", () => Loaded(t, signed));
        Try(key + ".async", () => Readback(AsyncGPUReadback.Request(t)));
    }

    static void MsaaCopiesAndLayers()
    {
        var d = new RenderTextureDescriptor(2, 1, RenderTextureFormat.ARGB32, 0)
        { msaaSamples = 4, sRGB = false };
        var source = Target(d); source.Create();
        var destination = Target(d); destination.Create();
        s_Mat.SetVector("_Color", Color.green); Graphics.Blit(null, source, s_Mat, 0); source.ResolveAntiAliasedSurface();
        ReadBack("rt.msaaCopies.sourceInitial", source);
        source.ResolveAntiAliasedSurface();
        ReadBack("rt.msaaCopies.sourceRepeated", source);
        s_Mat.SetVector("_Color", Color.red); Graphics.Blit(null, destination, s_Mat, 0); destination.ResolveAntiAliasedSurface();
        ReadBack("rt.msaaCopies.destinationInitial", destination);
        Graphics.CopyTexture(source, destination);
        ReadBack("rt.msaaCopies.beforeResolve", destination);
        ReadPixels("rt.msaaCopies.beforeResolveCurrent", destination);
        destination.ResolveAntiAliasedSurface();
        ReadBack("rt.msaaCopies.afterResolve", destination);
        // Copying a newer attachment distinguishes current pixels from the source's last resolve.
        s_Mat.SetVector("_Color", Color.blue); Graphics.Blit(null, source, s_Mat, 0);
        Graphics.CopyTexture(source, destination);
        ReadBack("rt.msaaCopies.dirtySource", destination);
        Graphics.SetRenderTarget(destination);
        destination.ResolveAntiAliasedSurface();
        ReadBack("rt.msaaCopies.dirtySourceResolved", destination);
        s_Mat.SetVector("_Color", Color.magenta); Graphics.Blit(null, destination, s_Mat, 0);
        Graphics.CopyTexture(source, destination);
        ReadBack("rt.msaaCopies.dirtyDestination", destination);
        Graphics.SetRenderTarget(source);
        ReadBack("rt.msaaCopies.dirtyDestinationSwitched", destination);
        d.dimension = TextureDimension.Tex2DArray; d.volumeDepth = 2;
        var array = Target(d);
        Value("rt.msaaLayers.create", B2(array.Create()));
        Value("rt.msaaLayers.samples", array.antiAliasing.ToString(CultureInfo.InvariantCulture));
        for (int layer = 0; layer < 2; layer++)
        {
            Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, layer);
            GL.Clear(false, true, Color.green);
        }
        array.ResolveAntiAliasedSurface();
        for (int layer = 0; layer < 2; layer++)
        {
            Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, layer);
            GL.Clear(false, true, Color.red);
            var pixels = Own(new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true));
            pixels.ReadPixels(new Rect(0, 0, 2, 1), 0, 0, false);
            Value("rt.msaaLayers." + layer + ".current", Texels(pixels));
            Try("rt.msaaLayers." + layer + ".retained", () => Readback(AsyncGPUReadback.Request(array, 0, 0, 2, 0, 1, layer, 1, TextureFormat.RGBAFloat)));
        }
    }

    static void MsaaMips()
    {
        var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)
        { msaaSamples = 4, useMipMap = true, autoGenerateMips = true };
        var source = Target(d);
        Value("rt.msaaMips.beforeCreate", source.mipmapCount.ToString(CultureInfo.InvariantCulture));
        Value("rt.msaaMips.create", B2(source.Create()));
        Value("rt.msaaMips.metadata", "[" + source.antiAliasing + "," + B2(source.useMipMap) + "," + source.mipmapCount + "]");
        var destination = Own(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        destination.Create();
        void ReadMip(string step)
        {
            Try("rt.msaaMips." + step + ".async", () => Readback(AsyncGPUReadback.Request(source, 1)));
            Try("rt.msaaMips." + step + ".asyncfloat", () => Readback(AsyncGPUReadback.Request(source, 1, TextureFormat.RGBAFloat)));
        }
        s_Mat.SetVector("_Color", Color.red); Graphics.Blit(null, source, s_Mat, 0);
        RenderTexture.active = destination; GL.Clear(false, true, Color.clear);
        ReadMip("red");
        s_Mat.SetVector("_Color", Color.green); Graphics.Blit(null, source, s_Mat, 0);
        ReadMip("beforeSwitch");
        RenderTexture.active = destination; ReadMip("afterSwitch");
        GL.Clear(false, true, Color.clear); ReadMip("afterClear");
        s_Mat.SetVector("_Color", Color.blue); Graphics.Blit(null, source, s_Mat, 0);
        source.ResolveAntiAliasedSurface(); ReadMip("explicit");
        source.Release(); Value("rt.msaaMips.afterRelease", source.mipmapCount.ToString(CultureInfo.InvariantCulture));
        source.antiAliasing = 1; Value("rt.msaaMips.afterSamplesChange", source.mipmapCount.ToString(CultureInfo.InvariantCulture));
        source.Create(); Value("rt.msaaMips.singleRecreate", source.mipmapCount.ToString(CultureInfo.InvariantCulture));
        source.Release(); Value("rt.msaaMips.singleRelease", source.mipmapCount.ToString(CultureInfo.InvariantCulture));
        source.useMipMap = false; source.useMipMap = true;
        Value("rt.msaaMips.flagReset", source.mipmapCount.ToString(CultureInfo.InvariantCulture));
        foreach (var dimension in new[] { TextureDimension.Tex2D, TextureDimension.Tex3D })
        {
            d.dimension = dimension; d.volumeDepth = dimension == TextureDimension.Tex3D ? 4 : 1;
            var control = Target(d);
            string key = "rt.msaaMips.control." + dimension;
            Value(key + ".before", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
            Value(key + ".create", B2(control.Create()));
            Value(key + ".after", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
            Value(key + ".descriptor", control.descriptor.mipCount.ToString(CultureInfo.InvariantCulture));
            if (dimension == TextureDimension.Tex2D)
            {
                control.Release(); control.useMipMap = false; control.useMipMap = true;
                Value(key + ".flagReset", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
                control.Create(); Value(key + ".recreated", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
                control.Release(); control.width = 8;
                Value(key + ".widthReset", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
                control.Create(); control.Release(); control.height = 16;
                Value(key + ".heightReset", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
                control.Create(); control.Release(); var resized = control.descriptor; resized.width = 32; control.descriptor = resized;
                Value(key + ".descriptorReset", control.mipmapCount.ToString(CultureInfo.InvariantCulture));
            }

        }

    }

    public static void Record()
    {
        Rows.Clear();
        s_Only = Environment.GetEnvironmentVariable("TEXTURE_FORMATS_PROBE_ONLY");
        s_Mat = Own(new Material(Shader.Find("Probe/TexFormats")));
        s_Sample = Own(new Material(s_Mat.shader));
        s_Load = Own(new Material(s_Mat.shader));
        s_Feed = Own(new Material(s_Mat.shader));
        s_Kernels = Resources.Load<ComputeShader>("TexFormats");
        s_Out2 = Target(Descriptor(GraphicsFormat.R32G32B32A32_SFloat));
        s_Out1 = Own(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        try
        {
            foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.R16, RenderTextureFormat.RG32,
                         RenderTextureFormat.ARGB64, RenderTextureFormat.ARGB2101010 })
                if (Runs("rt." + format)) NormTarget("rt." + format, format);
            foreach (var c in IntTargets) if (Runs("rt." + c.Key)) IntTarget(c);
            if (Runs("rt.msaaMips")) MsaaMips();
            if (Runs("rt.msaaCopies") || Runs("rt.msaaLayers")) MsaaCopiesAndLayers();
            foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBFloat })
                if (Runs("feedback." + format)) Feedback("feedback." + format, format);
            foreach (var format in new[] { TextureFormat.R16, TextureFormat.RG32, TextureFormat.RGBA64 })
                if (Runs("tex." + format)) NormTexture("tex." + format, () => new Texture2D(2, 1, format, false, true), true);
            if (Runs("tex.A2B10G10R10_UNormPack32"))
                NormTexture("tex.A2B10G10R10_UNormPack32",
                    () => new Texture2D(2, 1, GraphicsFormat.A2B10G10R10_UNormPack32, TextureCreationFlags.None), false);
            foreach (var (key, format, signed) in new[] { ("tex.R8G8B8A8_UInt", GraphicsFormat.R8G8B8A8_UInt, false),
                         ("tex.R16_UInt", GraphicsFormat.R16_UInt, false), ("tex.R16G16B16A16_SInt", GraphicsFormat.R16G16B16A16_SInt, true),
                         ("tex.R32_UInt", GraphicsFormat.R32_UInt, false), ("tex.R32G32B32A32_SInt", GraphicsFormat.R32G32B32A32_SInt, true) })
                if (Runs(key)) IntTexture(key, format, signed);
        }
        catch (Exception e)
        {
            Value("fatal", Json(e.GetType().Name + ": " + e.Message));
            Debug.LogException(e);
        }
        foreach (var o in Owned) if (o != null) Object.DestroyImmediate(o);
        Owned.Clear();
        Value("complete", "true");
        File.WriteAllLines(Environment.GetEnvironmentVariable("TEXTURE_FORMATS_PROBE_OUT"), Rows);
    }

    /// <summary>Unbound runs the probe in Play mode, one frame in (once its renderer runs), and
    /// quits; the Editor runs it from <c>TexFormatsRunner.Record</c> in batch mode.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Start()
    {
        if (Environment.GetEnvironmentVariable("TEXTURE_FORMATS_PROBE_OUT") == null) return;
        new GameObject("TexFormatsProbe").AddComponent<TexFormatsPlayer>();
    }
}

public class TexFormatsPlayer : MonoBehaviour
{
    System.Collections.IEnumerator Start()
    {
        yield return null;
        TexFormatsProbe.Record();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
