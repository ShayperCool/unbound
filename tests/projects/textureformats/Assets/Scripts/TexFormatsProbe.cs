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

    // ------------------------------------------------------------------ readback of target formats

    /// <summary>Targets read back whatever their ReadPixels usage answers: the default LDR
    /// formats (B8G8R8A8 on Metal), packed and shared-exponent formats, and one- and two-channel
    /// formats (what fills the channels they lack).</summary>
    static readonly GraphicsFormat[] ReadbackFormats =
    {
        GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.B8G8R8A8_UNorm, GraphicsFormat.B8G8R8A8_SRGB,
        GraphicsFormat.B4G4R4A4_UNormPack16, GraphicsFormat.A1R5G5B5_UNormPack16, GraphicsFormat.A2B10G10R10_UNormPack32,
        GraphicsFormat.E5B9G9R9_UFloatPack32, GraphicsFormat.R8_UNorm, GraphicsFormat.R16_SFloat, GraphicsFormat.R32G32_SFloat,
        GraphicsFormat.B8G8R8A8_SNorm, GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormat.R5G6B5_UNormPack16,
        GraphicsFormat.B5G6R5_UNormPack16, GraphicsFormat.R5G5B5A1_UNormPack16, GraphicsFormat.B5G5R5A1_UNormPack16,
        GraphicsFormat.A2R10G10B10_UNormPack32, GraphicsFormat.A2R10G10B10_XRUNormPack32, GraphicsFormat.R10G10B10_XRUNormPack32,
        GraphicsFormat.A10R10G10B10_XRUNormPack32, GraphicsFormat.B10G11R11_UFloatPack32,
    };
    static readonly List<string> s_Logs = new List<string>();

    static string Logged(Func<string> json)
    {
        s_Logs.Clear();
        string value = json();
        return "{\"value\":" + value + ",\"logs\":[" + string.Join(",", s_Logs.ConvertAll(Json)) + "]}";
    }

    static void ReadbackTarget(string key, GraphicsFormat format)
    {
        Support(key, format);
        if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render)) return;
        var rt = Own(new RenderTexture(2, 1, format, GraphicsFormat.None) { filterMode = FilterMode.Point });
        Value(key + ".create", B2(rt.Create()));
        Clear(rt, new Color(0.25f, 0.5f, 0.75f, 0.625f));
        Application.LogCallback log = (message, stack, type) => { if (type != LogType.Log) s_Logs.Add(type + ": " + message); };
        Application.logMessageReceived += log;
        try
        {
            foreach (var destination in new[] { TextureFormat.RGBAFloat, TextureFormat.RGBA32 })
                Try(key + ".read." + destination, () => Logged(() =>
                {
                    var t = Own(new Texture2D(2, 1, destination, false, true));
                    t.SetPixels(new[] { new Color(0.1f, 0.2f, 0.3f, 0.4f), new Color(0.1f, 0.2f, 0.3f, 0.4f) });
                    t.Apply();
                    var active = RenderTexture.active;
                    RenderTexture.active = rt;
                    t.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
                    t.Apply();
                    RenderTexture.active = active;
                    return Texels(t);
                }));
            Try(key + ".async", () => Logged(() => Readback(AsyncGPUReadback.Request(rt))));
            // Metal answers A2B10G10R10's float request with memory it never wrote (two captures
            // differ): only whether it fails, and what it logs.
            bool undefined = format == GraphicsFormat.A2B10G10R10_UNormPack32;
            Try(key + ".asyncfloat", () => Logged(() =>
            {
                var request = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAFloat);
                if (!undefined) return Readback(request);
                request.WaitForCompletion();
                return Json(request.hasError ? "error" : "data");
            }));
        }
        finally { Application.logMessageReceived -= log; }
    }

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
        Try("rt.msaaCopies.sampleBeforeReadPixels", () => Sampled(destination, false));
        ReadBack("rt.msaaCopies.afterFirstSample", destination);
        ReadPixels("rt.msaaCopies.beforeResolveCurrent", destination);
        Try("rt.msaaCopies.sampleCopied", () => Sampled(destination, false));
        Graphics.SetRenderTarget(destination);
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
        Graphics.CopyTexture(source, destination);
        ReadBack("rt.msaaCopies.unboundDestination", destination);
        Try("rt.msaaCopies.sampleUnboundDestination", () => Sampled(destination, false));
        ReadBack("rt.msaaCopies.afterUnboundSample", destination);
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
        s_Sample.SetTexture("_ArrayTex", array);
        for (int layer = 0; layer < 2; layer++)
        {
            s_Sample.SetFloat("_Layer", layer);
            Graphics.Blit(null, s_Out2, s_Sample, 14);
            Try("rt.msaaLayers." + layer + ".sample", () => Floats(s_Out2));
        }
        ReadBack("rt.msaaLayers.afterSwitch", array);
        Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, 1);
        array.ResolveAntiAliasedSurface();
        ReadBack("rt.msaaLayers.explicit", array);
        Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, 0); GL.Clear(false, true, Color.blue);
        Graphics.SetRenderTarget(array, 0, CubemapFace.Unknown, 1); GL.Clear(false, true, Color.magenta);
        array.ResolveAntiAliasedSurface();
        for (int layer = 0; layer < 2; layer++)
            Try("rt.msaaLayers.distinct." + layer, () => Readback(AsyncGPUReadback.Request(array, 0, 0, 2, 0, 1, layer, 1, TextureFormat.RGBAFloat)));
        d.volumeDepth = 1;
        var singleLayer = Target(d); singleLayer.Create();
        Graphics.SetRenderTarget(singleLayer, 0, CubemapFace.Unknown, 0); GL.Clear(false, true, Color.green);
        singleLayer.ResolveAntiAliasedSurface();
        GL.Clear(false, true, Color.red);
        Graphics.SetRenderTarget(destination);
        ReadBack("rt.msaaLayers.singleLayerSwitched", singleLayer);
        s_Sample.SetTexture("_ArrayTex", singleLayer); s_Sample.SetFloat("_Layer", 0);
        Graphics.Blit(null, s_Out2, s_Sample, 14);
        Try("rt.msaaLayers.singleLayerSample", () => Floats(s_Out2));
    }

    /// <summary>Texture2DArray.Load from a 2x1 ARGBFloat array target of three layers with mips:
    /// each layer's mip 0 holds two texels of its own, its mip 1 one color of its own; the layer
    /// and mip come from material floats.</summary>
    static void ArrayLoad()
    {
        var d = Descriptor(GraphicsFormat.R32G32B32A32_SFloat);
        d.dimension = TextureDimension.Tex2DArray; d.volumeDepth = 3;
        d.useMipMap = true; d.autoGenerateMips = false;
        var array = Target(d);
        Value("rt.arrayLoad.create", B2(array.Create()));
        for (int layer = 0; layer < 3; layer++)
        {
            float f = layer + 1;
            s_Mat.SetVector("_Color", new Color(0.125f * f, 0.25f, 0.5f, 1f));
            s_Mat.SetVector("_Color2", new Color(0.5f, 0.0625f * f, 0.75f, 0.5f));
            Graphics.Blit(null, array, s_Mat, 2, layer);
            Graphics.SetRenderTarget(array, 1, CubemapFace.Unknown, layer);
            GL.Clear(false, true, new Color(0.03125f * f, 0.875f, 0.375f, 0.25f));
        }
        Graphics.SetRenderTarget(null as RenderTexture);
        s_Sample.SetTexture("_ArrayTex", array);
        foreach (var (name, pass) in new[] { ("signed", 15), ("unsigned", 16) })
            for (int layer = 0; layer < 3; layer++)
                for (int mip = 0; mip < 2; mip++)
                {
                    s_Sample.SetFloat("_Layer", layer);
                    s_Sample.SetFloat("_Mip", mip);
                    Graphics.Blit(null, s_Out2, s_Sample, pass);
                    Try("rt.arrayLoad." + name + "." + layer + "." + mip, () => Floats(s_Out2));
                }
        s_Sample.SetTexture("_ArrayTex", null);
    }

    static void Unorm16Byte()
    {
        ushort[] codes = { 0, 1, 2, 3, 4, 127, 255, 256, 257, 258, 509, 1027,
            1028, 1029, 2047, 4095, 8193, 15841, 21407, 32767, 32768, 32769,
            40001, 48005, 53217, 60329, 61235, 65533, 65534, 65535 };
        foreach (var (name, format, channels) in new[]
        {
            ("r", TextureFormat.R16, 1),
            ("rg", TextureFormat.RG32, 2),
            ("rgba", TextureFormat.RGBA64, 4),
        })
        {
            string key = "rt.unorm16Byte." + name;
            var input = Own(new Texture2D(codes.Length, 1, format, false, true));
            var pixels = new ushort[codes.Length * channels];
            for (int i = 0; i < codes.Length; ++i)
                for (int c = 0; c < channels; ++c)
                    pixels[i * channels + c] = codes[(i + c * 7) % codes.Length];
            input.SetPixelData(pixels, 0);
            input.Apply(false, true);
            var desc = new RenderTextureDescriptor(codes.Length, 1, input.graphicsFormat, 0);
            var render = Target(desc);
            Value(key + ".created", B2(render.Create()));
            Graphics.CopyTexture(input, render);
            Value(key + ".texture.raw", Readback(AsyncGPUReadback.Request(input)));
            Value(key + ".render.raw", Readback(AsyncGPUReadback.Request(render)));
            foreach (var (label, targetFormat) in new[]
            {
                ("rgba8", TextureFormat.RGBA32),
                ("argb8", TextureFormat.ARGB32),
                ("bgra8", TextureFormat.BGRA32),
                ("rgb8", TextureFormat.RGB24),
                ("r8", TextureFormat.R8),
                ("rg8", TextureFormat.RG16),
                ("half", TextureFormat.RGBAHalf),
            })
            {
                Value(key + ".texture." + label, Readback(AsyncGPUReadback.Request(input, 0, targetFormat)));
                Value(key + ".render." + label, Readback(AsyncGPUReadback.Request(render, 0, targetFormat)));
            }
        }
    }

    static string CompletedReadback(AsyncGPUReadbackRequest request) =>
        request.hasError || !request.done ? Json("error") : Hex(request.GetData<byte>().ToArray());

    static void CommandResolveDepthSource()
    {
        foreach (int samples in new[] { 1, 4 })
        {
            string key = "rt.commandResolve.depthOnly." + samples;
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.Depth, 24)
                { msaaSamples = samples };
            var source = Target(desc);
            Value(key + ".sourceCreated", B2(source.Create()));
            Value(key + ".sourceFormat", Json(source.graphicsFormat.ToString()));
            var destination = Target(new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 24)
                { sRGB = false });
            destination.Create();
            RenderTexture.active = destination;
            GL.Clear(false, true, Color.green);
            string Read() =>
                Readback(AsyncGPUReadback.Request(destination, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32));
            Value(key + ".before", Read());

            using var cb = new CommandBuffer();
            cb.SetRenderTarget(source);
            cb.ResolveAntiAliasedSurface(source, destination);
            var logs = new List<string>();
            void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
            Application.logMessageReceived += OnLog;
            try { Graphics.ExecuteCommandBuffer(cb); }
            finally { Application.logMessageReceived -= OnLog; }
            Value(key + ".logs", Json(string.Join(" | ", logs)));
            Value(key + ".after", Read());
        }
    }

    static void CommandResolveDestroyedTarget()
    {
        foreach (bool destroyBeforeRecord in new[] { false, true })
        {
            string key = "rt.commandResolve.destroyedTarget." + (destroyBeforeRecord ? "record" : "execute");
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0)
                { msaaSamples = 4, sRGB = false };
            var source = Target(desc);
            source.Create();
            desc.msaaSamples = 1;
            var target = Target(desc);
            target.Create();
            RenderTexture.active = source;
            using var cb = new CommandBuffer();
            cb.ClearRenderTarget(false, true, Color.blue);
            string before = null, after = null;
            cb.RequestAsyncReadback(source, 0, TextureFormat.RGBA32, r => before = CompletedReadback(r));
            if (destroyBeforeRecord) Object.DestroyImmediate(target);
            cb.ResolveAntiAliasedSurface(source, target);
            if (!destroyBeforeRecord) Object.DestroyImmediate(target);
            cb.RequestAsyncReadback(source, 0, TextureFormat.RGBA32, r => after = CompletedReadback(r));
            Graphics.ExecuteCommandBuffer(cb);
            AsyncGPUReadback.WaitAllRequests();
            Value(key + ".changed", B2(before != after));
            Value(key + ".retained", after);
            Value(key + ".keptActive", B2(RenderTexture.active == source));
        }
    }

    static void CommandResolveDisplayPlacement()
    {
        int previousAA = QualitySettings.antiAliasing;
        bool previousWrite = GL.sRGBWrite;
        try { CommandResolveDisplayPlacementCases(); }
        finally
        {
            QualitySettings.antiAliasing = previousAA;
            GL.sRGBWrite = previousWrite;
        }
    }

    static void CommandResolveDisplayPlacementCases()
    {
        QualitySettings.antiAliasing = 0;
        void Read(string key, RenderTexture rt, int x = 0, int y = 0)
        {
            Value(key + ".byte", Readback(AsyncGPUReadback.Request(rt, 0, x, 1, y, 1, 0, 1, TextureFormat.RGBA32)));
            Value(key + ".float", Readback(AsyncGPUReadback.Request(rt, 0, x, 1, y, 1, 0, 1, TextureFormat.RGBAFloat)));
        }
        foreach (var format in new[] { GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.R8G8B8A8_SRGB })
        foreach (var write in new[] { false, true })
        {
            string key = "rt.commandResolve.destinationEncoding." + format + "." + write;
            var desc = new RenderTextureDescriptor(8, 8, format, 0) { msaaSamples = 4 };
            var source = Target(desc);
            Value(key + ".sourceCreated", B2(source.Create()));
            desc.msaaSamples = 1;
            var dst = Target(desc);
            Value(key + ".destCreated", B2(dst.Create()));
            RenderTexture.active = source;
            GL.Clear(false, true, Color.red);
            RenderTexture.active = dst;
            GL.Clear(false, true, Color.green);
            RenderTexture.active = null;
            GL.sRGBWrite = write;
            GL.Clear(false, true, new Color(.11f, .31f, .73f, .42f));
            using var cb = new CommandBuffer();
            cb.ResolveAntiAliasedSurface(source, dst);
            cb.ResolveAntiAliasedSurface(source);
            Graphics.ExecuteCommandBuffer(cb);
            Read(key + ".explicit", dst);
            Read(key + ".implicit", source);
        }
        foreach (var hdr in new[] { false, true })
        foreach (var aa in new[] { 0, 4 })
        foreach (int x in new[] { 4, 0, -2 })
        {
            string key = "rt.commandResolve.partialScreen." + hdr + "." + aa + "." + x;
            var (source, dst) = ResolvePartialDisplay(hdr, aa, new Rect(x, 5, 14, 13));
            Read(key + ".origin", dst, 0, 0);
            Read(key + ".within", dst, 6, 7);
            Read(key + ".beyondIntermediate", dst, 20, 20);
            Value(key + ".justInside", CommandResolvePixel(dst, TextureFormat.RGBA32, Math.Max(0, x), 5));
            Value(key + ".justOutside", CommandResolvePixel(dst, TextureFormat.RGBA32, x + 14, 5));
            Value(key + ".retainedWithin", CommandResolvePixel(source, TextureFormat.RGBA32, 6, 7));
            Value(key + ".retainedBeyond", CommandResolvePixel(source, TextureFormat.RGBA32, 20, 20));
        }

        // Compare entire rows across both edges. Half pixels and unequal endpoint rounding
        // distinguish the displayed extent from truncating the rectangle's width and height.
        var fractional = new[]
        {
            new Rect(-2.5f, 5f, 14f, 13f),
            new Rect(-3.5f, 5f, 14f, 13f),
            new Rect(4.25f, 5.25f, 13.25f, 12.25f),
            new Rect(4.75f, 5.75f, 13.25f, 12.25f),
            new Rect(4.5f, 5.5f, 13.5f, 12.5f),
            new Rect(4.2f, 5.2f, 13.9f, 12.9f),
        };
        foreach (bool hdr in new[] { false, true })
        foreach (int aa in new[] { 0, 4 })
        for (int i = 0; i < fractional.Length; ++i)
        {
            string key = "rt.commandResolve.fractionalDisplay." + hdr + "." + aa + "." + i;
            var (_, dst) = ResolvePartialDisplay(hdr, aa, fractional[i]);
            Value(key + ".row", Readback(AsyncGPUReadback.Request(dst, 0, 0, 24, 7, 1, 0, 1, TextureFormat.RGBA32)));
            Value(key + ".column", Readback(AsyncGPUReadback.Request(dst, 0, 6, 1, 0, 24, 0, 1, TextureFormat.RGBA32)));
        }
    }

    static (RenderTexture source, RenderTexture destination) ResolvePartialDisplay(bool hdr, int aa, Rect viewport)
    {
        QualitySettings.antiAliasing = aa;
        GL.sRGBWrite = false;
        var desc = new RenderTextureDescriptor(24, 24, RenderTextureFormat.ARGB32, 0) { msaaSamples = 4, sRGB = false };
        var source = Target(desc);
        source.Create();
        desc.msaaSamples = 1;
        var dst = Target(desc);
        dst.Create();
        RenderTexture.active = source;
        GL.Clear(false, true, Color.red);
        RenderTexture.active = dst;
        GL.Clear(false, true, Color.green);
        RenderTexture.active = null;
        GL.Clear(false, true, Color.blue);
        var camera = Own(new GameObject("Partial resolve camera")).AddComponent<Camera>();
        camera.enabled = false;
        camera.allowHDR = hdr;
        camera.allowMSAA = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.1f, .7f, .3f, .4f);
        camera.cullingMask = 0;
        camera.pixelRect = viewport;
        using var cb = new CommandBuffer();
        cb.ResolveAntiAliasedSurface(source, dst);
        cb.ResolveAntiAliasedSurface(source);
        camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
        try { camera.Render(); }
        finally { camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb); }
        return (source, dst);
    }

    static void CommandResolveFramebufferColors()
    {
        int previousAA = QualitySettings.antiAliasing;
        try
        {
            QualitySettings.antiAliasing = 0;
            foreach (var format in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGBFloat, RenderTextureFormat.R8 })
            foreach (bool srgbWrite in new[] { false, true })
            {
                string key = "rt.commandResolve.screenColor." + format + "." + srgbWrite;
                var desc = new RenderTextureDescriptor(8, 8, format, 0) { msaaSamples = 4, sRGB = false };
                var source = Target(desc);
                source.Create();
                desc.msaaSamples = 1;
                var destination = Target(desc);
                destination.Create();
                RenderTexture.active = destination;
                GL.Clear(false, true, Color.green);
                RenderTexture.active = source;
                GL.Clear(false, true, Color.red);
                RenderTexture.active = null;
                GL.sRGBWrite = srgbWrite;
                GL.Clear(false, true, new Color32(31, 127, 220, 161));

                using var cb = new CommandBuffer();
                cb.ResolveAntiAliasedSurface(source, destination);
                cb.ResolveAntiAliasedSurface(source);
                Graphics.ExecuteCommandBuffer(cb);
                Value(key + ".external", CommandResolvePixel(destination, TextureFormat.RGBA32));
                Value(key + ".implicit", CommandResolvePixel(source, TextureFormat.RGBA32));
                if (format != RenderTextureFormat.R8)
                    Value(key + ".float", CommandResolvePixel(destination, TextureFormat.RGBAFloat));
                GL.sRGBWrite = false;
            }

            foreach (int antialias in new[] { 0, 4 })
            foreach (bool hdr in new[] { false, true })
            foreach (bool callerSrgb in new[] { false, true })
            foreach (var cameraFormat in new[] { RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGBFloat })
            {
                string key = "rt.commandResolve.screenCamera." + antialias + "." + hdr + "." + callerSrgb + "." + cameraFormat;
                QualitySettings.antiAliasing = antialias;
                var desc = new RenderTextureDescriptor(8, 8, cameraFormat, 0)
                    { msaaSamples = 4, sRGB = false };
                var source = Target(desc);
                source.Create();
                desc.msaaSamples = 1;
                var destination = Target(desc);
                var other = Target(desc);
                destination.Create();
                other.Create();
                RenderTexture.active = destination;
                GL.Clear(false, true, Color.green);
                RenderTexture.active = source;
                GL.Clear(false, true, Color.red);
                RenderTexture.active = null;
                GL.Clear(false, true, Color.white);

                var camera = Own(new GameObject("Screen resolve camera")).AddComponent<Camera>();
                camera.enabled = false;
                camera.allowHDR = hdr;
                camera.allowMSAA = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(1.625f, .305f, .7f, .375f);
                camera.cullingMask = 0;
                using var cb = new CommandBuffer();
                cb.ResolveAntiAliasedSurface(source, destination);
                cb.ResolveAntiAliasedSurface(source);
                camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                GL.sRGBWrite = callerSrgb;
                try { camera.Render(); }
                finally { camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb); }
                Value(key + ".external", CommandResolvePixel(destination, TextureFormat.RGBAFloat));
                Value(key + ".implicit", CommandResolvePixel(source, TextureFormat.RGBAFloat));
                Value(key + ".callerSrgbWrite", B2(GL.sRGBWrite));

                RenderTexture.active = destination;
                GL.Clear(false, true, Color.green);
                RenderTexture.active = null;
                cb.Clear();
                cb.SetRenderTarget(other);
                cb.ClearRenderTarget(false, true, Color.yellow);
                cb.ResolveAntiAliasedSurface(source, destination);
                camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
                try { camera.Render(); }
                finally { camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb); }
                Value(key + ".otherBound", CommandResolvePixel(destination, TextureFormat.RGBAHalf));
                GL.sRGBWrite = false;
            }

            QualitySettings.antiAliasing = 0;
            var wide = new RenderTextureDescriptor(Screen.width + 4, Screen.height + 4,
                RenderTextureFormat.ARGB32, 0) { msaaSamples = 4, sRGB = false };
            var wideSource = Target(wide);
            wideSource.Create();
            wide.msaaSamples = 1;
            var wideTarget = Target(wide);
            wideTarget.Create();
            RenderTexture.active = wideTarget;
            GL.Clear(false, true, Color.green);
            RenderTexture.active = null;
            GL.Clear(false, true, Color.blue);
            // A single off-origin rectangle distinguishes an unscaled copy from a fill.
            GL.Viewport(new Rect(2, 2, 3, 3));
            GL.Clear(false, true, Color.magenta);
            GL.Viewport(new Rect(0, 0, Screen.width, Screen.height));
            using var clipped = new CommandBuffer();
            clipped.ResolveAntiAliasedSurface(wideSource, wideTarget);
            Graphics.ExecuteCommandBuffer(clipped);
            const string crop = "rt.commandResolve.screenCrop";
            Value(crop + ".near", CommandResolvePixel(wideTarget, TextureFormat.RGBA32, 0, 0));
            Value(crop + ".rectangle", CommandResolvePixel(wideTarget, TextureFormat.RGBA32, 3, 3));
            Value(crop + ".rightOfRectangle", CommandResolvePixel(wideTarget, TextureFormat.RGBA32, 5, 3));
            Value(crop + ".aboveRectangle", CommandResolvePixel(wideTarget, TextureFormat.RGBA32, 3, 5));
            Value(crop + ".outsideX", CommandResolvePixel(wideTarget, TextureFormat.RGBA32, wide.width - 1, wide.height - 5));
            Value(crop + ".outsideY", CommandResolvePixel(wideTarget, TextureFormat.RGBA32, wide.width - 5, wide.height - 1));
        }
        finally { QualitySettings.antiAliasing = previousAA; }
    }

    static string CommandResolvePixel(RenderTexture target, TextureFormat format, int x = 0, int y = 0) =>
        Readback(AsyncGPUReadback.Request(target, 0, x, 1, y, 1, 0, 1, format));

    static void CommandResolveBackbuffer()
    {
        foreach (string mode in new[]
        {
            "external", "externalSingle", "externalMultisampled", "explicitSelf", "implicitSelf", "boundImplicitSelf",
        })
        {
            string key = "rt.commandResolve.backbuffer." + mode;
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0)
                { msaaSamples = mode == "externalSingle" ? 1 : 4,
                  bindMS = mode == "boundImplicitSelf", sRGB = false };
            var source = Target(desc);
            source.Create();
            desc.msaaSamples = mode == "externalMultisampled" ? 4 : 1;
            desc.bindMS = false;
            var target = Target(desc);
            target.Create();
            RenderTexture.active = source;
            GL.Clear(false, true, Color.red);
            RenderTexture.active = target;
            GL.Clear(false, true, Color.green);
            RenderTexture.active = null;
            GL.Clear(false, true, Color.blue);

            string Read(RenderTexture rt) =>
                Readback(AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32));
            string sourceBefore = Read(source);
            Value(key + ".before", Read(target));
            using var cb = new CommandBuffer();
            if (mode == "explicitSelf") cb.ResolveAntiAliasedSurface(source, source);
            else if (mode.EndsWith("ImplicitSelf", StringComparison.Ordinal) || mode == "implicitSelf")
                cb.ResolveAntiAliasedSurface(source);
            else cb.ResolveAntiAliasedSurface(source, target);
            var logs = new List<string>();
            void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
            Application.logMessageReceived += OnLog;
            try { Graphics.ExecuteCommandBuffer(cb); }
            finally { Application.logMessageReceived -= OnLog; }
            Value(key + ".logs", Json(string.Join(" | ", logs)));
            Value(key + ".target", Read(target));
            string sourceAfter = Read(source);
            Value(key + ".sourceUnchanged", B2(sourceAfter == sourceBefore));
            if (mode != "boundImplicitSelf") Value(key + ".sourceAfter", sourceAfter);
            Value(key + ".binding", B2(RenderTexture.active == null));
        }
    }

    static void CommandResolveCamera()
    {
        var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 24)
            { msaaSamples = 4, sRGB = false };
        var source = Target(desc);
        source.Create();
        desc.msaaSamples = 1;
        var destination = Target(desc);
        destination.Create();
        RenderTexture.active = destination;
        GL.Clear(false, true, Color.green);
        string Read() =>
            Readback(AsyncGPUReadback.Request(destination, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32));
        Value("rt.commandResolve.camera.before", Read());

        var camera = Own(new GameObject("Resolve camera")).AddComponent<Camera>();
        camera.enabled = false;
        camera.targetTexture = source;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.red;
        camera.cullingMask = 0;
        using var cb = new CommandBuffer();
        cb.ResolveAntiAliasedSurface(source, destination);
        camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
        var logs = new List<string>();
        void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
        Application.logMessageReceived += OnLog;
        try { camera.Render(); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
        }
        Value("rt.commandResolve.camera.logs", Json(string.Join(" | ", logs)));
        Value("rt.commandResolve.camera.color", Read());
        Value("rt.commandResolve.camera.restored", B2(RenderTexture.active == destination));
    }

    static void CommandResolveInactiveSelf()
    {
        foreach (var (name, count, bind) in new[] { ("single", 1, false), ("ordinary", 4, false), ("bound", 4, true) })
        foreach (bool explicitTarget in new[] { false, true })
        {
            string key = "rt.commandResolve.inactiveSelf." + name + (explicitTarget ? ".explicit" : ".implicit");
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0)
                { msaaSamples = count, bindMS = bind, sRGB = false };
            var source = Target(desc);
            source.Create();
            desc.msaaSamples = 1;
            desc.bindMS = false;
            var other = Target(desc);
            other.Create();
            RenderTexture.active = source;
            GL.Clear(false, true, Color.red);
            RenderTexture.active = other;
            GL.Clear(false, true, Color.blue);
            string Read() => Readback(AsyncGPUReadback.Request(source, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32));
            string before = Read();
            using var cb = new CommandBuffer();
            if (explicitTarget) cb.ResolveAntiAliasedSurface(source, source);
            else cb.ResolveAntiAliasedSurface(source);
            var logs = new List<string>();
            void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
            Application.logMessageReceived += OnLog;
            try { Graphics.ExecuteCommandBuffer(cb); }
            finally { Application.logMessageReceived -= OnLog; }
            Value(key + ".logs", Json(string.Join(" | ", logs)));
            Value(key + ".unchanged", B2(before == Read()));
            Value(key + ".binding", B2(RenderTexture.active == other));
        }
    }

    static void CommandResolveSubresources()
    {
        foreach (bool command in new[] { false, true })
        foreach (string mode in new[] { "arrayTo2D", "cubeFace", "mipLevel" })
        {
            string key = "rt.commandResolve.subresource." + (command ? "command." : "direct.") + mode;
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0)
                { sRGB = false, useMipMap = mode == "mipLevel", autoGenerateMips = false };
            if (mode.StartsWith("array", StringComparison.Ordinal))
            {
                desc.dimension = TextureDimension.Tex2DArray;
                desc.volumeDepth = 2;
            }
            if (mode == "cubeFace") desc.dimension = TextureDimension.Cube;
            var source = Target(desc);
            if (mode == "arrayTo2D")
            {
                desc.dimension = TextureDimension.Tex2D;
                desc.volumeDepth = 1;
            }
            var destination = Target(desc);
            Value(key + ".sourceCreated", B2(source.Create()));
            Value(key + ".destinationCreated", B2(destination.Create()));

            int Mip(int i) => mode == "mipLevel" ? i : 0;
            CubemapFace Face(int i) => mode == "cubeFace" ? (CubemapFace)i : CubemapFace.Unknown;
            int Layer(int i) => mode.StartsWith("array", StringComparison.Ordinal) ? i : 0;
            int ReadLayer(int i) => mode == "mipLevel" ? 0 : i;
            for (int i = 0; i < 2; ++i)
            {
                Graphics.SetRenderTarget(source, Mip(i), Face(i), Layer(i));
                GL.Clear(false, true, i == 0 ? Color.red : Color.blue);
                if (mode != "arrayTo2D" || i == 0)
                {
                    Graphics.SetRenderTarget(destination, Mip(i), Face(i), Layer(i));
                    GL.Clear(false, true, Color.green);
                }
            }

            string Read(RenderTexture rt, int i, int layer) =>
                Readback(AsyncGPUReadback.Request(rt, Mip(i), 0, 1, 0, 1, layer, 1, TextureFormat.RGBA32));
            for (int i = 0; i < 2; ++i)
                Value(key + ".source" + i, Read(source, i, ReadLayer(i)));
            int outputs = mode == "arrayTo2D" ? 1 : 2;
            for (int i = 0; i < outputs; ++i)
                Value(key + ".before" + i, Read(destination, i, ReadLayer(i)));

            var logs = new List<string>();
            void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
            Application.logMessageReceived += OnLog;
            try
            {
                if (command)
                {
                    using var cb = new CommandBuffer();
                    cb.SetRenderTarget(source, Mip(1), Face(1), Layer(1));
                    cb.ResolveAntiAliasedSurface(source, destination);
                    Graphics.ExecuteCommandBuffer(cb);
                }
                else
                {
                    Graphics.SetRenderTarget(source, Mip(1), Face(1), Layer(1));
                    source.ResolveAntiAliasedSurface(destination);
                }
            }
            finally { Application.logMessageReceived -= OnLog; }
            Value(key + ".logs", Json(string.Join(" | ", logs)));
            for (int i = 0; i < outputs; ++i)
                Value(key + "." + i, Read(destination, i, ReadLayer(i)));
        }
    }

    static void CommandResolveAttachments()
    {
        foreach (string mode in new[]
        {
            "firstColor", "secondColor", "widthSmaller", "widthLarger",
        })
        {
            string key = "rt.commandResolve.attachment." + mode;
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 24)
                { msaaSamples = 4, sRGB = false };
            var source = Target(desc);
            var other = Target(desc);
            source.Create();
            other.Create();
            desc.msaaSamples = 1;
            if (mode == "widthSmaller") desc.width = 4;
            if (mode == "widthLarger") desc.width = 12;
            var destination = Target(desc);
            destination.Create();
            RenderTexture.active = source;
            GL.Clear(false, true, Color.red);
            RenderTexture.active = other;
            GL.Clear(false, true, Color.blue);
            RenderTexture.active = destination;
            GL.Clear(false, true, Color.green);

            using var cb = new CommandBuffer();
            if (mode == "firstColor") cb.SetRenderTarget(new RenderTargetIdentifier[] { source, other }, source);
            else if (mode == "secondColor") cb.SetRenderTarget(new RenderTargetIdentifier[] { other, source }, other);
            else cb.SetRenderTarget(source);
            cb.ResolveAntiAliasedSurface(source, destination);
            var logs = new List<string>();
            void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
            Application.logMessageReceived += OnLog;
            try { Graphics.ExecuteCommandBuffer(cb); }
            finally { Application.logMessageReceived -= OnLog; }
            Value(key + ".logs", Json(string.Join(" | ", logs)));
            Value(key + ".first", Readback(AsyncGPUReadback.Request(destination, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32)));
            Value(key + ".last", Readback(AsyncGPUReadback.Request(destination, 0, desc.width - 1, 1, 0, 1, 0, 1, TextureFormat.RGBA32)));
            Value(key + ".restored", B2(RenderTexture.active == destination));
        }
    }

    static void CommandResolveLifetime()
    {
        foreach (string mode in new[]
        {
            "singleInactive", "releaseSource", "recreateSource",
            "releaseDestination", "sourceAlreadyActive",
        })
        {
            string key = "rt.commandResolve.lifetime." + mode;
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 0)
                { msaaSamples = mode == "singleInactive" ? 1 : 4, sRGB = false };
            var source = Target(desc);
            source.Create();
            desc.msaaSamples = 1;
            var destination = Target(desc);
            destination.Create();
            var other = Target(desc);
            other.Create();
            RenderTexture.active = source;
            GL.Clear(false, true, Color.red);
            RenderTexture.active = destination;
            GL.Clear(false, true, Color.green);
            RenderTexture.active = other;
            GL.Clear(false, true, Color.blue);

            using var cb = new CommandBuffer();
            cb.ResolveAntiAliasedSurface(source, destination);
            if (mode == "releaseSource" || mode == "recreateSource") source.Release();
            if (mode == "releaseDestination") destination.Release();
            if (mode == "recreateSource")
            {
                source.Create();
                RenderTexture.active = source;
                GL.Clear(false, true, Color.yellow);
            }
            if (mode == "sourceAlreadyActive") RenderTexture.active = source;
            var logs = new List<string>();
            void OnLog(string message, string _, LogType type) => logs.Add(type + " " + message);
            Application.logMessageReceived += OnLog;
            try { Graphics.ExecuteCommandBuffer(cb); }
            finally { Application.logMessageReceived -= OnLog; }
            Value(key + ".logs", Json(string.Join(" | ", logs)));
            if (source != null) Value(key + ".sourceCreated", B2(source.IsCreated()));
            if (destination != null)
            {
                Value(key + ".destinationCreated", B2(destination.IsCreated()));
                if (destination.IsCreated())
                    Value(key + ".color", Readback(AsyncGPUReadback.Request(destination, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32)));
            }
        }
    }

    sealed class ResolveLogObserver : ILogHandler
    {
        public int Count { get; private set; }
        public void LogFormat(LogType type, Object context, string format, params object[] args) => Count++;
        public void LogException(Exception exception, Object context) => Count++;
    }

    static void CommandResolveEdges()
    {
        foreach (var (name, fromSamples, toSamples, fromCreated, toCreated, bindSource) in new[]
        {
            ("single", 1, 1, true, true, true),
            ("multiDestination", 4, 4, true, true, true),
            ("uncreatedDestination", 4, 1, true, false, true),
            ("uncreatedSource", 4, 1, false, true, false),
            ("otherActive", 4, 1, true, true, false),
        })
        {
            string key = "rt.commandResolve.edge." + name;
            var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 24)
                { msaaSamples = fromSamples, sRGB = false };
            var source = Target(desc);
            desc.msaaSamples = toSamples;
            var destination = Target(desc);
            desc.msaaSamples = 1;
            var other = Target(desc);
            other.Create();
            RenderTexture.active = other;
            GL.Clear(false, true, Color.blue);
            if (toCreated)
            {
                destination.Create();
                RenderTexture.active = destination;
                GL.Clear(false, true, Color.green);
            }
            if (fromCreated)
            {
                source.Create();
                RenderTexture.active = source;
                GL.Clear(false, true, Color.red);
            }
            RenderTexture.active = other;
            using var cb = new CommandBuffer();
            cb.SetRenderTarget(bindSource ? source : other);
            var during = new List<string>();
            void OnLog(string condition, string _, LogType type) => during.Add(type + " " + condition);
            ILogHandler previousHandler = Debug.unityLogger.logHandler;
            var observer = new ResolveLogObserver();
            Debug.unityLogger.logHandler = observer;
            Application.logMessageReceived += OnLog;
            try
            {
                cb.ResolveAntiAliasedSurface(source, destination);
                Value(key + ".recordLog", Json(string.Join(" | ", during)));
                during.Clear();
                Graphics.ExecuteCommandBuffer(cb);
                Value(key + ".executeLog", Json(string.Join(" | ", during)));
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                Debug.unityLogger.logHandler = previousHandler;
            }
            Value(key + ".managedHandlerCalls", observer.Count.ToString(CultureInfo.InvariantCulture));
            Value(key + ".sourceCreated", B2(source.IsCreated()));
            Value(key + ".destinationCreated", B2(destination.IsCreated()));
            Value(key + ".preservesCallerTarget", B2(RenderTexture.active == other));
            if (destination.IsCreated())
                Value(key + ".color", Readback(AsyncGPUReadback.Request(destination, 0, 0, 1, 0, 1, 0, 1, TextureFormat.RGBA32)));
        }
    }

    static void CommandResolves()
    {
        var saved = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        GL.sRGBWrite = false;
        try
        {
            using (var invalid = new CommandBuffer())
                Try("rt.commandResolve.nullSource", () =>
                {
                    invalid.ResolveAntiAliasedSurface(null);
                    return invalid.sizeInBytes.ToString(CultureInfo.InvariantCulture);
                });
            using (var invalid = new CommandBuffer())
            {
                string parameter = null;
                try { invalid.ResolveAntiAliasedSurface(null); }
                catch (ArgumentNullException e) { parameter = e.ParamName; }
                Value("rt.commandResolve.nullSourceParameter", Json(parameter ?? ""));
            }

            foreach (bool bind in new[] { false, true })
            foreach (string mode in new[] { "external", "implicitSelf", "explicitSelf" })
            {
                string key = "rt.commandResolve." + (bind ? "bound." : "ordinary.") + mode;
                var desc = new RenderTextureDescriptor(8, 8, RenderTextureFormat.ARGB32, 24)
                    { msaaSamples = 4, bindMS = bind, sRGB = false };
                var source = Target(desc);
                Value(key + ".created", B2(source.Create()));
                desc.msaaSamples = 1;
                desc.bindMS = false;
                var destination = Target(desc);
                destination.Create();

                string Retained(RenderTexture rt) => Readback(AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32));
                string initial = Retained(source);
                RenderTexture.active = destination;
                GL.Clear(false, true, new Color32(63, 31, 133, 201));
                string sentinel = Retained(destination);
                var currentColor = new Color32(91, 123, 157, 183);

                if (bind) RenderTexture.active = source;
                var caller = RenderTexture.active;
                using var cb = new CommandBuffer();
                if (!bind) cb.SetRenderTarget(source);
                cb.ClearRenderTarget(true, true, currentColor);
                string before = null, after = null;
                cb.RequestAsyncReadback(source, 0, TextureFormat.RGBA32, r => before = CompletedReadback(r));
                int sizeBefore = cb.sizeInBytes;
                if (mode == "external") cb.ResolveAntiAliasedSurface(source, destination);
                else if (mode == "implicitSelf") cb.ResolveAntiAliasedSurface(source);
                else cb.ResolveAntiAliasedSurface(source, source);
                Value(key + ".size", (cb.sizeInBytes - sizeBefore).ToString(CultureInfo.InvariantCulture));
                cb.RequestAsyncReadback(source, 0, TextureFormat.RGBA32, r => after = CompletedReadback(r));
                Graphics.ExecuteCommandBuffer(cb);
                AsyncGPUReadback.WaitAllRequests();
                Value(key + ".beforeReady", B2(before != null && before != Json("error")));
                Value(key + ".afterReady", B2(after != null && after != Json("error")));
                Value(key + ".beforeMatchesInitial", B2(before == initial));
                Value(key + ".insideHistoryUnchanged", B2(before == after));
                Value(key + ".outsideHistoryUnchanged", B2(Retained(source) == initial));
                string external = Retained(destination);
                Value(key + ".destinationUnchanged", B2(external == sentinel));
                if (mode == "external")
                    Value(key + ".destinationBytes", external);
                Value(key + ".callerTargetRestored", B2(RenderTexture.active == caller));
            }
            CommandResolveEdges();
            CommandResolveLifetime();
            CommandResolveAttachments();
            CommandResolveSubresources();
            CommandResolveInactiveSelf();
            CommandResolveCamera();
            CommandResolveBackbuffer();
            CommandResolveFramebufferColors();
            CommandResolveDisplayPlacement();
            CommandResolveDepthSource();
            CommandResolveDestroyedTarget();
        }
        finally
        {
            RenderTexture.active = saved;
            GL.sRGBWrite = srgb;
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

    // Typed partial clears and self-feedback, with outside-viewport and float-target controls.
    static void Q58Cases()
    {
        var packed = new IntCase { Key = "A2B10G10R10_UIntPack32", Format = GraphicsFormat.A2B10G10R10_UIntPack32, Signed = false, Bits = 32 };
        if (Runs("rt." + packed.Key)) IntTarget(packed);
        foreach (var format in new[] { GraphicsFormat.R16_SNorm, GraphicsFormat.R16G16_SNorm, GraphicsFormat.R16G16B16_SNorm, GraphicsFormat.R16G16B16A16_SNorm })
        {
            string key = "rt." + format;
            if (!Runs(key)) continue;
            Support(key, format);
            RenderTexture rt = null;
            Try(key + ".create", () => { rt = Target(Descriptor(format)); return B2(rt.Create()); });
            if (rt == null || !rt.IsCreated()) continue;
            Value(key + ".allocatedFormat", Json(rt.graphicsFormat.ToString()));
            Clear(rt, B); Read(key + ".clear", rt);
            s_Mat.SetVector("_Color", A); s_Mat.SetVector("_Color2", B);
            Graphics.Blit(null, rt, s_Mat, 2); Read(key + ".split", rt);
            Try(key + ".sample", () => Sampled(rt, false));
            Try(key + ".filter", () => Sampled(rt, true));
            Clear(rt, B); s_Mat.SetVector("_Color", Over);
            Graphics.Blit(null, rt, s_Mat, 1); Read(key + ".blend", rt);
            var fresh = Target(Descriptor(format)); fresh.Create();
            s_Mat.SetVector("_Color", A); s_Mat.SetVector("_Color2", B);
            Graphics.Blit(null, fresh, s_Mat, 2); Read(key + ".freshDraw", fresh);
            var edge = new Color(-1f, -1.25f, 1.25f, -.5f);
            Clear(rt, edge); Read(key + ".endpoint.clear", rt);
            s_Mat.SetVector("_Color", edge);
            Graphics.Blit(null, rt, s_Mat, 0); Read(key + ".endpoint.draw", rt);
            bool writable = SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore);
            Value(key + ".kernel.supported", B2(writable));
            if (writable)
            {
                var d = Descriptor(format); d.enableRandomWrite = true;
                var rw = Target(d); Value(key + ".kernel.create", B2(rw.Create()));
                int kernel = s_Kernels.FindKernel("WriteNorm");
                s_Kernels.SetTexture(kernel, "_Norm", rw); s_Kernels.Dispatch(kernel, 1, 1, 1);
                Read(key + ".kernel", rw);
            }
        }
        if (Runs("rt.readControl"))
        foreach (var format in new[] { GraphicsFormat.R32_SFloat, GraphicsFormat.R16_SFloat, GraphicsFormat.R32G32_SFloat,
            GraphicsFormat.R16_UNorm, GraphicsFormat.R16G16_UNorm, GraphicsFormat.R8G8_UNorm, GraphicsFormat.R8G8B8A8_UNorm })
        {
            string key = "rt.readControl." + format;
            var rt = Target(Descriptor(format)); rt.Create();
            s_Mat.SetVector("_Color", A); s_Mat.SetVector("_Color2", B);
            Graphics.Blit(null, rt, s_Mat, 2); Read(key, rt);
        }
        if (Runs("rt.readControl"))
        foreach (var format in new[] { GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.R8G8_SNorm })
        {
            string key = "rt.readControl." + format;
            var rt = Target(Descriptor(format)); rt.Create();
            s_Mat.SetVector("_Color", A); s_Mat.SetVector("_Color2", B);
            Graphics.Blit(null, rt, s_Mat, 2); ReadPixels(key, rt);
        }
        if (Runs("rt.readControl"))
        foreach (var format in new[] { GraphicsFormat.R8G8B8A8_SNorm, GraphicsFormat.R16G16B16A16_SNorm })
        {
            int max = format == GraphicsFormat.R8G8B8A8_SNorm ? 127 : 32767;
            var rt = Target(Descriptor(format)); rt.Create();
            var values = max == 127 ? new[] { 0, 1, 15, 16, 63, 64, 126, 127 }
                : new[] { 0, 64, 193, 322, 16383, 16384, 32766, 32767 };
            for (int sign = 0; sign < 2; sign++)
            {
                float At(int i) => (sign == 0 ? 1 : -1) * (values[i] + .25f) / max;
                s_Mat.SetVector("_Color", new Color(At(0), At(1), At(2), At(3)));
                s_Mat.SetVector("_Color2", new Color(At(4), At(5), At(6), At(7)));
                Graphics.Blit(null, rt, s_Mat, 2);
                string key = "rt.readControl.transfer." + format + "." + sign;
                Value(key + ".sample", Sampled(rt, false)); ReadPixels(key, rt);
            }
        }
        var extra = Own(new Material(Shader.Find("Probe/TexFormatsQ58")));
        foreach (var c in IntTargets) Q58Integer(c, extra);
        Q58Integer(packed, extra);
        if (Runs("rt.partial.float")) Q58Depth("rt.partial.float", Target(new RenderTextureDescriptor(2, 1, RenderTextureFormat.ARGBFloat, 24)), extra);
    }

    static void Q58Integer(IntCase c, Material extra)
    {
        string key = "rt.partial." + c.Key;
        if (Runs(key))
        {
            var d = c.Legacy.HasValue ? new RenderTextureDescriptor(2, 1, c.Legacy.Value, 24) : Descriptor(c.Format);
            d.depthBufferBits = 24;
            var rt = Target(d); rt.Create();
            foreach (bool command in new[] { false, true })
            {
                string path = key + (command ? ".command" : ".gl");
                var previous = RenderTexture.active; RenderTexture.active = rt;
                GL.Viewport(new Rect(0, 0, 2, 1));
                GL.Clear(true, true, new Color(0, 0, 0, 0), .25f);
                GL.PushMatrix(); GL.LoadOrtho(); s_Mat.SetPass(WritePass(c));
                GL.Begin(GL.QUADS);
                GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                GL.End(); GL.PopMatrix();
                var clear = new Color(3f, -2f, 1000.5f, .5f);
                if (command)
                {
                    using (var cmd = new CommandBuffer())
                    {
                        cmd.SetRenderTarget(rt); cmd.SetViewport(new Rect(1, 0, 1, 1));
                        cmd.ClearRenderTarget(false, true, clear); Graphics.ExecuteCommandBuffer(cmd);
                    }
                }
                else { GL.Viewport(new Rect(1, 0, 1, 1)); GL.Clear(false, true, clear); }
                GL.Viewport(new Rect(0, 0, 2, 1)); RenderTexture.active = previous;
                Try(path + ".color.load", () => Loaded(rt, c.Signed));
            }
            Q58Depth(key, rt, extra);
        }
        key = "feedback.integer." + c.Key;
        if (!Runs(key)) return;
        var target = c.Legacy.HasValue ? Own(new RenderTexture(2, 1, 0, c.Legacy.Value, RenderTextureReadWrite.Linear)) : Target(Descriptor(c.Format));
        target.filterMode = FilterMode.Point; target.Create();
        Graphics.Blit(null, target, s_Mat, WritePass(c));
        Try(key + ".initial", () => Loaded(target, c.Signed));
        extra.SetTexture(c.Signed ? "_IntFeed" : "_UIntFeed", target);
        for (int i = 0; i < 2; i++)
        {
            Graphics.Blit(null, target, extra, c.Signed ? 0 : 1);
            Try(key + ".draw" + i, () => Loaded(target, c.Signed));
        }
        extra.SetTexture(c.Signed ? "_IntFeed" : "_UIntFeed", null);
    }

    static void Q58Depth(string key, RenderTexture rt, Material extra)
    {
        foreach (bool command in new[] { false, true })
        {
            var previous = RenderTexture.active; RenderTexture.active = rt;
            GL.Viewport(new Rect(0, 0, 2, 1)); GL.Clear(true, true, Color.black, .25f);
            if (command)
            {
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(rt); cmd.SetViewport(new Rect(1, 0, 1, 1));
                    cmd.ClearRenderTarget(true, false, Color.white, .75f); Graphics.ExecuteCommandBuffer(cmd);
                }
            }
            else { GL.Viewport(new Rect(1, 0, 1, 1)); GL.Clear(true, false, Color.white, .75f); }
            GL.Viewport(new Rect(0, 0, 2, 1)); RenderTexture.active = previous;
            extra.SetTexture("_DepthTex", rt, RenderTextureSubElement.Depth);
            Graphics.Blit(null, s_Out2, extra, 2);
            Value(key + (command ? ".command.depth" : ".gl.depth"), Floats(s_Out2));
            extra.SetTexture("_DepthTex", null);
        }
    }

    static void OpaquePadding()
    {
        var rgb = Target(Descriptor(GraphicsFormat.R16G16B16_SNorm));
        var rgba = Target(Descriptor(GraphicsFormat.R16G16B16A16_SNorm));
        rgb.Create(); rgba.Create();
        var previous = RenderTexture.active;
        Graphics.SetRenderTarget(new[] { rgb.colorBuffer, rgba.colorBuffer }, rgb.depthBuffer);
        GL.Viewport(new Rect(0, 0, 2, 1));
        GL.Clear(false, true, new Color(.5f, .25f, -.5f, .25f));
        RenderTexture.active = previous;
        Try("rt.padding.mrt.rgb", () => Sampled(rgb, false));
        Try("rt.padding.mrt.rgba", () => Sampled(rgba, false));
        var grab = Own(new Material(Shader.Find("Probe/TexFormatsGrab")));
        Graphics.Blit(null, rgb, grab);
        Try("rt.padding.grab", () => Sampled(rgb, false));
        using (var cmd = new CommandBuffer())
        {
            cmd.Blit(null, rgb, grab);
            Graphics.ExecuteCommandBuffer(cmd);
        }
        Try("rt.padding.grab.command", () => Sampled(rgb, false));
        var go = Own(new GameObject("PaddingCamera"));
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = .5f; camera.aspect = 2f;
        camera.nearClipPlane = .1f; camera.farClipPlane = 10f;
        camera.transform.position = new Vector3(0, 0, -2);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.5f, .25f, -.5f, .25f);
        camera.cullingMask = 1 << 30; camera.targetTexture = rgb;
        var quad = Own(GameObject.CreatePrimitive(PrimitiveType.Quad)); quad.layer = 30;
        quad.transform.localScale = new Vector3(2f, 1f, 1f);
        quad.GetComponent<Renderer>().sharedMaterial = grab;
        camera.Render();
        Try("rt.padding.grab.renderer", () => Sampled(rgb, false));
        quad.SetActive(false);
        camera.targetTexture = null;

    }

    public static void Record()
    {
        Rows.Clear();
        s_Only = Environment.GetEnvironmentVariable("TEXTURE_FORMATS_PROBE_ONLY");
        const string byteReadback = "rt.unorm16Byte";
        bool byteReadbackRequested = s_Only != null && Array.IndexOf(s_Only.Split(','), byteReadback) >= 0;
        var device = SystemInfo.graphicsDeviceType;
        if (byteReadbackRequested && device is not (GraphicsDeviceType.OpenGLCore or GraphicsDeviceType.Vulkan))
        {
            Value("fatal", Json($"The R16 byte readback controls require OpenGL Core or Vulkan. Active API is {device}."));
            WriteCapture();
            return;
        }
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
            // Only when named: the OpenGL Core and Vulkan references were recorded without them.
            foreach (var format in ReadbackFormats)
                if (!string.IsNullOrEmpty(s_Only) && Runs("readback." + format)) ReadbackTarget("readback." + format, format);
            if (Runs("rt.msaaMips")) MsaaMips();
            if (byteReadbackRequested)
            {
                Value(byteReadback + ".device", Json(device.ToString()));
                Value(byteReadback + ".release", Json(Application.unityVersion));
                Unorm16Byte();
            }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore && s_Only == "rt.commandResolve")
                CommandResolves();
            if (Runs("rt.msaaCopies") || Runs("rt.msaaLayers")) MsaaCopiesAndLayers();
            if (Runs("rt.arrayLoad")) ArrayLoad();
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
            Q58Cases();
            if (Runs("rt.padding")) OpaquePadding();
        }
        catch (Exception e)
        {
            Value("fatal", Json(e.GetType().Name + ": " + e.Message));
            Debug.LogException(e);
        }
        foreach (var o in Owned) if (o != null) Object.DestroyImmediate(o);
        Owned.Clear();
        WriteCapture();
    }

    static void WriteCapture()
    {
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
