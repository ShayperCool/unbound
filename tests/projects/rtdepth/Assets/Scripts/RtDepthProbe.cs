using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Render texture sub-elements and built-in render targets.
///
/// Formats: a camera draws flat quads at known depths with stencil references and a depth ramp
/// into render textures of each depth/stencil format; materials and command buffers bind the
/// target's Default, Color, Depth and Stencil elements to filtered, float-load and uint-load
/// shaders, whose float outputs are read back. Targets: a camera drawing to the screen and to a
/// render texture runs command buffers that read, sample and draw into CameraTarget,
/// CurrentActive, Depth and ResolvedDepth. API values, log messages and every image are recorded.
///
/// With <c>RTDEPTH_PROBE_MODE=controls</c> it records the controls instead (<see cref="Controls"/>):
/// screen readback, screen blits and GL.wireframe from a screen camera's callbacks with and
/// without a command buffer, and depth elements and integer loads in further binding paths.
/// </summary>
public class RtDepthProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const int W = 64, H = 32;
    static readonly int Src = Shader.PropertyToID("_Src");
    static readonly Color Sentinel = new Color(0.125f, -1f, -2f, 0.5f);

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    bool capturing;
    string directory;
    Camera cam;
    Shader sampleTex, loadFloat, loadUint;
    Material write;
    Texture2D dummy, stripes, magenta;
    Mesh quad;
    int renders;
    string activeDuringRender;
    bool controls;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("RTDEPTH_PROBE_OUT") == null) return;
        new GameObject("RtDepthProbe").AddComponent<RtDepthProbe>();
    }

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";

    void OnLog(string condition, string stack, LogType type)
    {
        if (capturing) logs.Add(type + ": " + condition);
    }

    void BeginLogs() { logs.Clear(); capturing = true; }
    void EndLogs(string key)
    {
        capturing = false;
        var s = new StringBuilder("[");
        for (int i = 0; i < logs.Count; i++) s.Append(i > 0 ? "," : "").Append(Q(logs[i]));
        Value(key + ".log", s.Append("]").ToString());
        logs.Clear();
    }

    void Describe(string key, RenderTexture rt)
    {
        if (rt == null) { Value(key, "null"); return; }
        Value(key, "{\"format\":" + Q(rt.format.ToString()) + ",\"graphicsFormat\":" + Q(rt.graphicsFormat.ToString())
            + ",\"depthStencilFormat\":" + Q(rt.depthStencilFormat.ToString()) + ",\"depth\":" + rt.depth
            + ",\"stencilFormat\":" + Q(rt.stencilFormat.ToString()) + ",\"antiAliasing\":" + rt.antiAliasing
            + ",\"size\":[" + rt.width + "," + rt.height + "],\"supportsStencil\":" + B(RenderTexture.SupportsStencil(rt))
            + ",\"created\":" + B(rt.IsCreated()) + "}");
    }

    static string Name(RenderTexture rt) => rt == null ? "null" : "[" + rt.width + "," + rt.height + "," + Q(rt.graphicsFormat.ToString()) + "," + Q(rt.depthStencilFormat.ToString()) + "," + rt.antiAliasing + "]";

    void Read(string key, RenderTexture rt, int width = W, int height = H)
    {
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = saved;
        var pixels = image.GetPixels();
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, key + ".rgba"))))
            foreach (var c in pixels) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        Value(key + ".pixels", pixels.Length.ToString());
        DestroyImmediate(image);
    }

    static RenderTexture Output()
    {
        var rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { filterMode = FilterMode.Point };
        rt.Create();
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Sentinel);
        RenderTexture.active = saved;
        return rt;
    }

    GameObject Quad(string name, Vector3[] corners, Color color, int reference)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        var mesh = new Mesh { vertices = corners, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
        mesh.RecalculateBounds();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var m = new Material(write);
        m.SetColor("_Color", color);
        m.SetInt("_Ref", reference);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    static Vector3[] Rect3(float x0, float x1, float y0, float y1, float z0, float z1) =>
        new[] { new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1) };

    /// <summary>The scene: the camera at z -10 looking along +z (near 1, far 11, an orthographic
    /// height of 2 at aspect 2), four flat quads at view depths 3, 5, 7 and 9 with stencil
    /// references 1, 7, 128 and 255 over the left half (the top quarter left clear) and a ramp
    /// from view depth 1.5 to 10.5 with reference 42 over the right half.</summary>
    void MakeScene()
    {
        float[] depths = { 3, 5, 7, 9 };
        int[] refs = { 1, 7, 128, 255 };
        Color[] colors = { new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1) };
        for (int i = 0; i < 4; i++)
            Quad("Flat" + i, Rect3(-2 + 0.5f * i, -1.5f + 0.5f * i, -1, 0.5f, depths[i] - 10, depths[i] - 10), colors[i], refs[i]);
        Quad("Ramp", Rect3(0, 2, -1, 1, -8.5f, 0.5f), new Color(0.5f, 0.25f, 1, 1), 42);
    }

    void Configure(bool perspective)
    {
        cam.transform.position = new Vector3(0, 0, -10);
        cam.transform.rotation = Quaternion.identity;
        cam.orthographic = !perspective;
        cam.orthographicSize = 1;
        cam.fieldOfView = 60;
        cam.nearClipPlane = 1;
        cam.farClipPlane = 11;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.25f, 0.5f, 0.75f, 1);
        cam.allowMSAA = false;
        cam.allowHDR = false;
        cam.depthTextureMode = DepthTextureMode.None;
        cam.aspect = 2;
    }

    void Start()
    {
        controls = Environment.GetEnvironmentVariable("RTDEPTH_PROBE_MODE") == "controls";
        directory = Environment.GetEnvironmentVariable("RTDEPTH_PROBE_OUT");
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        int code = 0;
        yield return null;
        IEnumerator steps = null;
        try
        {
            QualitySettings.antiAliasing = 0;
            Debug.Log("RTDEPTH_DEVICE " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName + " " + SystemInfo.graphicsDeviceVersion);
            sampleTex = Shader.Find("Probe/SampleTex");
            loadFloat = Shader.Find("Probe/LoadFloat");
            loadUint = Shader.Find("Probe/LoadUint");
            write = new Material(Shader.Find("Probe/DepthWrite"));
            dummy = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            dummy.SetPixel(0, 0, new Color(0, 0, 0, 0)); dummy.Apply();
            magenta = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            magenta.SetPixel(0, 0, new Color(1, 0, 1, 1)); magenta.Apply();
            stripes = new Texture2D(4, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            stripes.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white, Color.cyan, Color.magenta, Color.yellow, Color.black });
            stripes.Apply();
            quad = new Mesh { vertices = Rect3(-0.25f, 0.25f, -0.25f, 0.25f, 0, 0), triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            quad.RecalculateBounds();
            Shader.SetGlobalTexture(Src, magenta);
            if (!controls) Api();
            MakeScene();
            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.enabled = false;
            Configure(false);
            Camera.onPostRender += OnAnyPostRender;
            if (controls)
            {
                Camera.onPreRender += OnControlPreRender;
                steps = Controls();
            }
            else
            {
                Formats();
                steps = Targets();
            }
        }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 1;
        }
        while (steps != null)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; break; }
            if (!more) break;
            yield return steps.Current;
        }
        try { if (code == 0 && !controls) Stencils(); }
        catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
        Value("complete", B(code == 0));
        Flush();
        Debug.Log("RTDEPTH_DONE " + directory + " " + code);
        Application.logMessageReceived -= OnLog;
        if (finished != null) finished(code);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }

    // ------------------------------------------------------------ API values

    static readonly GraphicsFormat[] DepthFormats =
    {
        GraphicsFormat.None, GraphicsFormat.D16_UNorm, GraphicsFormat.D24_UNorm, GraphicsFormat.D24_UNorm_S8_UInt,
        GraphicsFormat.D32_SFloat, GraphicsFormat.D32_SFloat_S8_UInt, GraphicsFormat.S8_UInt, GraphicsFormat.D16_UNorm_S8_UInt,
    };

    static readonly GraphicsFormatUsage[] Usages =
    {
        GraphicsFormatUsage.Sample, GraphicsFormatUsage.Linear, GraphicsFormatUsage.Render, GraphicsFormatUsage.LoadStore,
        GraphicsFormatUsage.MSAA2x, GraphicsFormatUsage.MSAA4x, GraphicsFormatUsage.StencilSampling,
    };

    void Api()
    {
        Value("api.device", Q(SystemInfo.graphicsDeviceType.ToString()));
        Value("api.reversedZ", B(SystemInfo.usesReversedZBuffer));
        Value("api.supportsDepth", B(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth)));
        Value("api.supportsShadowmap", B(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Shadowmap)));
        Value("api.defaultDepthStencil", Q(SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil).ToString()));
        Value("api.defaultShadow", Q(SystemInfo.GetGraphicsFormat(DefaultFormat.Shadow).ToString()));
        foreach (var f in DepthFormats)
        {
            foreach (var u in Usages)
                Value("api.supported." + f + "." + u, B(SystemInfo.IsFormatSupported(f, u)));
            Value("api.compatibleRender." + f, Q(SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Render).ToString()));
            Value("api.depthBits." + f, GraphicsFormatUtility.GetDepthBits(f).ToString());
            Value("api.isStencil." + f, B(GraphicsFormatUtility.IsStencilFormat(f)));
            Value("api.isDepth." + f, B(GraphicsFormatUtility.IsDepthFormat(f)));
        }
        foreach (int bits in new[] { 0, 8, 16, 24, 32, 40 })
            Value("api.forBits." + bits, Q(GraphicsFormatUtility.GetDepthStencilFormat(bits).ToString()));
        foreach (BuiltinRenderTextureType t in Enum.GetValues(typeof(BuiltinRenderTextureType)))
            Value("api.rti." + t, Q(new RenderTargetIdentifier(t).ToString()));
        var rt = new RenderTexture(W, H, 24);
        Value("api.rti.equal.camera", B(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget) == BuiltinRenderTextureType.CameraTarget));
        Value("api.rti.equal.cameraActive", B(new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget) == BuiltinRenderTextureType.CurrentActive));
        Value("api.rti.equal.rtTexture", B(new RenderTargetIdentifier(rt) == new RenderTargetIdentifier((Texture)rt)));
        Value("api.rti.equal.rtMip", B(new RenderTargetIdentifier(rt) == new RenderTargetIdentifier(rt, 0, CubemapFace.Unknown, 0)));
        Value("api.rti.equal.rtAllSlices", B(new RenderTargetIdentifier(rt) == new RenderTargetIdentifier(rt, 0, CubemapFace.Unknown, -1)));
        Value("api.rti.equal.rtName", B(new RenderTargetIdentifier(rt) == new RenderTargetIdentifier("_Src")));
        Value("api.rti.equal.names", B(new RenderTargetIdentifier("_Src") == new RenderTargetIdentifier(Src)));
        DestroyImmediate(rt);
        var m = new Material(sampleTex);
        foreach (RenderTextureSubElement e in Enum.GetValues(typeof(RenderTextureSubElement)))
        {
            var t = new RenderTexture(W, H, 24);
            BeginLogs();
            m.SetTexture(Src, t, e);
            Value("api.material." + e + ".get", Q(m.GetTexture(Src) == t ? "rt" : m.GetTexture(Src) == null ? "null" : "other"));
            Value("api.material." + e + ".has", B(m.HasTexture(Src)));
            Value("api.material." + e + ".created", B(t.IsCreated()));
            EndLogs("api.material." + e);
            m.SetTexture(Src, null);
            DestroyImmediate(t);
        }
        DestroyImmediate(m);
    }

    // ------------------------------------------------------------ formats

    struct Config
    {
        public string Name;
        public Func<RenderTexture> Make;
        public bool Perspective;
    }

    static RenderTexture Descriptor(GraphicsFormat color, GraphicsFormat depth, int samples = 1) =>
        new RenderTexture(new RenderTextureDescriptor(W, H, color, depth) { msaaSamples = samples });

    static readonly Config[] Configs =
    {
        new Config { Name = "bits16", Make = () => new RenderTexture(W, H, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) },
        new Config { Name = "bits24", Make = () => new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) },
        new Config { Name = "bits32", Make = () => new RenderTexture(W, H, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) },
        new Config { Name = "D16", Make = () => Descriptor(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D16_UNorm) },
        new Config { Name = "D24S8", Make = () => Descriptor(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt) },
        new Config { Name = "D32", Make = () => Descriptor(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D32_SFloat) },
        new Config { Name = "D32S8", Make = () => Descriptor(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D32_SFloat_S8_UInt) },
        new Config { Name = "depthOnly24", Make = () => new RenderTexture(W, H, 24, RenderTextureFormat.Depth) },
        new Config { Name = "depthOnlyD32S8", Make = () => Descriptor(GraphicsFormat.None, GraphicsFormat.D32_SFloat_S8_UInt) },
        new Config { Name = "noDepth", Make = () => new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) },
        new Config { Name = "msaa2", Make = () => Descriptor(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt, 2) },
        new Config { Name = "perspectiveD24S8", Make = () => Descriptor(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt), Perspective = true },
    };

    enum Bind { Material, Default, Color, Depth, Stencil, GlobalPlain, GlobalColor, GlobalDepth, GlobalStencil }

    RenderTexture Source(Config config, string key)
    {
        RenderTexture rt = null;
        BeginLogs();
        try
        {
            rt = config.Make();
            rt.Create();
            Describe(key + ".rt", rt);
            Configure(config.Perspective);
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
        }
        catch (Exception e) { Value(key + ".error", Q(e.GetType().Name + ": " + e.Message)); }
        EndLogs(key + ".render");
        return rt;
    }

    /// <summary>Unsigned integer loads, last: a renderer that cannot bind them may stop.</summary>
    void Stencils()
    {
        foreach (var config in Configs)
        {
            string key = "fmt." + config.Name;
            var rt = Source(config, key + ".uintSource");
            if (rt == null) continue;
            Sample(key + ".stencil.uint", rt, Bind.Stencil, loadUint);
            Sample(key + ".globalStencil.uint", rt, Bind.GlobalStencil, loadUint);
            rt.Release();
            DestroyImmediate(rt);
            Flush();
        }
    }

    void Flush() => File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());

    void Formats()
    {
        foreach (var config in Configs)
        {
            string key = "fmt." + config.Name;
            var rt = Source(config, key);
            if (rt == null) continue;
            Sample(key + ".material.tex", rt, Bind.Material, sampleTex);
            Sample(key + ".default.tex", rt, Bind.Default, sampleTex);
            Sample(key + ".color.tex", rt, Bind.Color, sampleTex);
            Sample(key + ".depth.tex", rt, Bind.Depth, sampleTex);
            Sample(key + ".depth.load", rt, Bind.Depth, loadFloat);
            Sample(key + ".stencil.tex", rt, Bind.Stencil, sampleTex);
            Sample(key + ".material.load", rt, Bind.Material, loadFloat);
            Sample(key + ".global.tex", rt, Bind.GlobalPlain, sampleTex);
            Sample(key + ".globalColor.tex", rt, Bind.GlobalColor, sampleTex);
            Sample(key + ".globalDepth.tex", rt, Bind.GlobalDepth, sampleTex);
            rt.Release();
            DestroyImmediate(rt);
            Flush();
        }
    }

    void Sample(string key, RenderTexture rt, Bind bind, Shader shader)
    {
        var output = Output();
        var m = new Material(shader);
        BeginLogs();
        try
        {
            if (bind <= Bind.Stencil)
            {
                if (bind == Bind.Material) m.SetTexture(Src, rt);
                else m.SetTexture(Src, rt, bind == Bind.Default ? RenderTextureSubElement.Default : bind == Bind.Color ? RenderTextureSubElement.Color
                    : bind == Bind.Depth ? RenderTextureSubElement.Depth : RenderTextureSubElement.Stencil);
                Graphics.Blit(dummy, output, m);
            }
            else
            {
                var cb = new CommandBuffer { name = "probe " + key };
                if (bind == Bind.GlobalPlain) cb.SetGlobalTexture(Src, rt);
                else cb.SetGlobalTexture(Src, rt, bind == Bind.GlobalColor ? RenderTextureSubElement.Color
                    : bind == Bind.GlobalDepth ? RenderTextureSubElement.Depth : RenderTextureSubElement.Stencil);
                cb.Blit(dummy, output, m);
                Graphics.ExecuteCommandBuffer(cb);
                cb.Release();
                Shader.SetGlobalTexture(Src, magenta);
            }
        }
        catch (Exception e) { Value(key + ".error", Q(e.GetType().Name + ": " + e.Message)); }
        EndLogs(key);
        Read(key, output);
        output.Release();
        DestroyImmediate(output);
        DestroyImmediate(m);
    }

    // ------------------------------------------------------------ built-in targets

    void OnAnyPostRender(Camera c)
    {
        if (c != cam) return;
        renders++;
        activeDuringRender = Name(RenderTexture.active) + "," + Name(c.activeTexture);
        OnControlPostRender();
    }

    static readonly string[] Cases =
    {
        "none", "blitCam", "blitActive", "globalCam", "globalCamColor", "globalCamDepth", "globalDepth", "globalActive",
        "globalResolvedDepth", "targetCam", "targetActive", "blitToCam", "blitToActive",
    };

    CommandBuffer Commands(string name, RenderTexture output, RenderTexture scratch, Material global, Material marker, Material depthGlobal)
    {
        var cb = new CommandBuffer { name = "probe " + name };
        var camera = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
        var active = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
        var markerAt = Matrix4x4.TRS(new Vector3(-1, 0.5f, -8), Quaternion.identity, Vector3.one);
        switch (name)
        {
            case "blitCam": cb.Blit(camera, output); break;
            case "blitActive": cb.Blit(active, output); break;
            case "globalCam": cb.SetGlobalTexture(Src, camera); cb.Blit(dummy, output, global); break;
            case "globalCamColor": cb.SetGlobalTexture(Src, camera, RenderTextureSubElement.Color); cb.Blit(dummy, output, global); break;
            case "globalCamDepth": cb.SetGlobalTexture(Src, camera, RenderTextureSubElement.Depth); cb.Blit(dummy, output, depthGlobal); break;
            case "globalDepth": cb.SetGlobalTexture(Src, BuiltinRenderTextureType.Depth); cb.Blit(dummy, output, depthGlobal); break;
            case "globalActive": cb.SetGlobalTexture(Src, active); cb.Blit(dummy, output, global); break;
            case "globalResolvedDepth": cb.SetGlobalTexture(Src, BuiltinRenderTextureType.ResolvedDepth); cb.Blit(dummy, output, depthGlobal); break;
            case "targetCam":
                cb.SetRenderTarget(scratch); cb.ClearRenderTarget(true, true, Color.green);
                cb.SetRenderTarget(camera); cb.DrawMesh(quad, markerAt, marker);
                break;
            case "targetActive":
                cb.SetRenderTarget(scratch); cb.ClearRenderTarget(true, true, Color.green);
                cb.SetRenderTarget(active); cb.DrawMesh(quad, markerAt, marker);
                break;
            case "blitToCam": cb.Blit(stripes, camera); break;
            case "blitToActive": cb.Blit(stripes, active); break;
        }
        return cb;
    }

    void ReadFinal(string key, RenderTexture target)
    {
        if (target != null) { Read(key, target); return; }
        Value(key + ".screen", "[" + Screen.width + "," + Screen.height + "]");
        var saved = RenderTexture.active;
        RenderTexture.active = null;
        Read(key, null);
        RenderTexture.active = saved;
    }

    IEnumerator Targets()
    {
        var marker = new Material(write);
        marker.SetColor("_Color", new Color(1, 0, 1, 1));
        marker.SetInt("_Ref", 99);
        var cameraTarget = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        cameraTarget.Create();
        var scratch = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        scratch.Create();
        foreach (bool toTexture in new[] { false, true })
        foreach (bool hdr in new[] { false, true })
        foreach (var e in new[] { CameraEvent.AfterForwardOpaque, CameraEvent.AfterEverything })
        foreach (var name in Cases)
        {
            string key = "target." + (toTexture ? "texture" : "screen") + (hdr ? ".hdr." : ".ldr.") + e + "." + name;
            Configure(false);
            cam.allowHDR = hdr;
            cam.targetTexture = toTexture ? cameraTarget : null;
            var output = Output();
            var saved = RenderTexture.active;
            RenderTexture.active = scratch;
            GL.Clear(true, true, Sentinel);
            RenderTexture.active = saved;
            var global = new Material(sampleTex);
            var depthGlobal = new Material(sampleTex);
            var cb = Commands(name, output, scratch, global, marker, depthGlobal);
            cam.AddCommandBuffer(e, cb);
            renders = 0;
            activeDuringRender = null;
            cam.enabled = true;
            BeginLogs();
            yield return new WaitForEndOfFrame();
            EndLogs(key);
            Value(key + ".renders", renders.ToString());
            Value(key + ".active", Q(activeDuringRender));
            ReadFinal(key + ".final", toTexture ? cameraTarget : null);
            Read(key + ".out", output);
            if (name.StartsWith("target")) Read(key + ".scratch", scratch);
            cam.enabled = false;
            cam.RemoveCommandBuffer(e, cb);
            cb.Release();
            Shader.SetGlobalTexture(Src, magenta);
            output.Release();
            DestroyImmediate(output);
            DestroyImmediate(global);
            DestroyImmediate(depthGlobal);
            Flush();
            yield return null;
        }
        cam.targetTexture = null;
        cameraTarget.Release();
        scratch.Release();
    }

    // ------------------------------------------------------------ controls

    string control;
    float[] postImage;

    void OnControlPreRender(Camera c)
    {
        if (c == cam && control is "wireframe" or "wireframeKept") GL.wireframe = true;
    }

    void OnControlPostRender()
    {
        switch (control)
        {
            case "postRead": postImage = ScreenBytes(); break;
            case "postBlit": Graphics.Blit(stripes, (RenderTexture)null); break;
            case "wireframe": GL.wireframe = false; break;
        }
    }

    /// <summary>The screen (the active target being null) read into an 8-bit sRGB texture, as a
    /// screenshot does.</summary>
    float[] ScreenBytes()
    {
        var image = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
        image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        image.Apply();
        var pixels = image.GetPixels();
        DestroyImmediate(image);
        var result = new float[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            result[4 * i] = pixels[i].r; result[4 * i + 1] = pixels[i].g;
            result[4 * i + 2] = pixels[i].b; result[4 * i + 3] = pixels[i].a;
        }
        return result;
    }

    void WriteImage(string key, float[] pixels)
    {
        if (pixels == null) { Value(key + ".pixels", "null"); return; }
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, key + ".rgba"))))
            foreach (var v in pixels) w.Write(v);
        Value(key + ".pixels", (pixels.Length / 4).ToString());
    }

    /// <summary>
    /// Screen camera (LDR, the 64x32 game view), each without and with an empty command buffer
    /// at AfterEverything: <c>postRead</c> reads the screen in OnPostRender; <c>postBlit</c> blits
    /// a texture to the screen in OnPostRender (the screen read after the frame);
    /// <c>wireframe</c> draws with GL.wireframe on from OnPreRender to OnPostRender,
    /// <c>wireframeKept</c> from OnPreRender to the end of the frame. Then render
    /// targets: <c>mainDepth</c> binds a target's Depth element as a material's _MainTex drawn by
    /// a camera; <c>texelSize</c> returns _Src_TexelSize of a Depth element; <c>attachedDepth</c>
    /// clears target B's depth through SetRenderTarget(A, B) after both depth elements were
    /// sampled, then samples both; <c>grabUint</c> loads a GrabPass texture as unsigned
    /// integers, twice with different colors; <c>uintArray</c> loads an unbound
    /// Texture2DArray&lt;uint4&gt;.
    /// </summary>
    IEnumerator Controls()
    {
        const int Layer = 31;
        cam.cullingMask = ~(1 << Layer);
        foreach (var name in new[] { "postRead", "postBlit", "wireframe", "wireframeKept" })
        foreach (bool withBuffer in new[] { false, true })
        {
            string key = "ctl." + name + (withBuffer ? ".buffer" : ".none");
            Configure(false);
            cam.targetTexture = null;
            var cb = new CommandBuffer { name = "probe empty" };
            if (withBuffer) cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);
            control = name;
            postImage = null;
            renders = 0;
            cam.enabled = true;
            BeginLogs();
            yield return new WaitForEndOfFrame();
            EndLogs(key);
            control = null;
            GL.wireframe = false;
            Value(key + ".renders", renders.ToString());
            if (name == "postRead") WriteImage(key, postImage);
            else WriteImage(key + ".final", ScreenBytes());
            cam.enabled = false;
            if (withBuffer) cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
            cb.Release();
            Flush();
            yield return null;
        }

        var ctlCam = new GameObject("ControlCamera").AddComponent<Camera>();
        ctlCam.enabled = false;
        ctlCam.transform.position = new Vector3(0, 0, -5);
        ctlCam.orthographic = true;
        ctlCam.orthographicSize = 1;
        ctlCam.nearClipPlane = 1;
        ctlCam.farClipPlane = 10;
        ctlCam.clearFlags = CameraClearFlags.Nothing;
        ctlCam.cullingMask = 1 << Layer;
        ctlCam.allowHDR = false;
        ctlCam.allowMSAA = false;
        ctlCam.aspect = 2;
        var view = Quad("ControlQuad", Rect3(-2, 2, -1, 1, 0, 0), Color.white, 0);
        view.layer = Layer;
        var mesh = view.GetComponent<MeshFilter>().sharedMesh;
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        var viewRenderer = view.GetComponent<MeshRenderer>();
        void Draw(string key, Material m)
        {
            var output = Output();
            viewRenderer.sharedMaterial = m;
            ctlCam.targetTexture = output;
            BeginLogs();
            ctlCam.Render();
            EndLogs(key);
            ctlCam.targetTexture = null;
            Read(key, output);
            output.Release();
            DestroyImmediate(output);
        }

        var source = Source(Configs[4], "ctl.source");
        var sampleMain = new Material(Shader.Find("Probe/SampleMain"));
        sampleMain.SetTexture("_MainTex", source, RenderTextureSubElement.Depth);
        Draw("ctl.mainDepth", sampleMain);
        Sample("ctl.texelSize", source, Bind.Depth, Shader.Find("Probe/TexelSize"));
        source.Release();
        DestroyImmediate(source);
        Flush();

        var a = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var b = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        a.Create(); b.Create();
        foreach (var t in new[] { a, b })
        {
            var saved = RenderTexture.active;
            RenderTexture.active = t;
            GL.Clear(true, true, Color.black, 1f);
            RenderTexture.active = saved;
        }
        Sample("ctl.attachedDepth.beforeA", a, Bind.Depth, sampleTex);
        Sample("ctl.attachedDepth.beforeB", b, Bind.Depth, sampleTex);
        var clear = new CommandBuffer { name = "probe attached depth" };
        clear.SetRenderTarget(new RenderTargetIdentifier(a), new RenderTargetIdentifier(b));
        clear.ClearRenderTarget(true, false, Color.black, 0.375f);
        BeginLogs();
        Graphics.ExecuteCommandBuffer(clear);
        EndLogs("ctl.attachedDepth.clear");
        clear.Release();
        Sample("ctl.attachedDepth.B", b, Bind.Depth, sampleTex);
        Sample("ctl.attachedDepth.A", a, Bind.Depth, sampleTex);
        a.Release(); b.Release();
        DestroyImmediate(a); DestroyImmediate(b);
        Flush();

        var grab = new Material(Shader.Find("Probe/GrabUint"));
        grab.SetColor("_Color", new Color(1, 0, 1, 1));
        Draw("ctl.grabUint.first", grab);
        grab.SetColor("_Color", new Color(0, 1, 1, 1));
        Draw("ctl.grabUint.second", grab);
        Flush();

        var bound = Output();
        Sample("ctl.uintArray", bound, Bind.Material, Shader.Find("Probe/UintArray"));
        bound.Release();
        DestroyImmediate(bound);
        Flush();
        DestroyImmediate(view);
        DestroyImmediate(ctlCam.gameObject);
        cam.cullingMask = ~0;
    }
}
