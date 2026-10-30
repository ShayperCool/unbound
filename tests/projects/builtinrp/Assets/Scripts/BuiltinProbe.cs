using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// The built-in render pipeline's camera textures, events and per-object rules, as the Editor
/// renders them: which camera events fire and in what order (and what the depth, depth-normals and
/// motion-vector textures hold at each), the textures themselves, [ImageEffectOpaque], disabled
/// shader passes, transparency sorting, render texture assets, per-material property blocks,
/// Graphics.DrawMesh lighting, the main light's culling mask and cookie, and SubShader LODs.
/// Images go to <c>BUILTIN_PROBE_OUT</c> as PNGs, values to <c>values.jsonl</c> there; then it quits.
/// </summary>
public class BuiltinProbe : MonoBehaviour
{
    const int Width = 200, Height = 150;
    readonly List<UnityEngine.Object> m_Case = new List<UnityEngine.Object>();
    readonly StringBuilder m_Values = new StringBuilder();
    string m_Dir;
    Material m_Increment, m_Sample, m_Show, m_Effects;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BuiltinProbe").AddComponent<BuiltinProbe>();

    // ------------------------------------------------------------------ output

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";
    static string C(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string B(bool b) => b ? "true" : "false";

    static string M(Matrix4x4 m)
    {
        var sb = new StringBuilder("[");
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                sb.Append(r + c == 0 ? "" : ",").Append(F(m[r, c]));
        return sb.Append(']').ToString();
    }

    void Value(string key, string json) => m_Values.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(json).Append("}\n");

    T Add<T>(T o) where T : UnityEngine.Object
    {
        m_Case.Add(o);
        return o;
    }

    void Clear()
    {
        foreach (var o in m_Case) if (o != null) DestroyImmediate(o);
        m_Case.Clear();
    }

    // ------------------------------------------------------------------ scene pieces

    Material Standard(Color color)
    {
        var m = Add(new Material(Shader.Find("Standard")));
        m.color = color;
        m.SetFloat("_Glossiness", 0.5f);
        return m;
    }

    GameObject Primitive(PrimitiveType type, Vector3 position, Material material, float scale = 1f)
    {
        var go = Add(GameObject.CreatePrimitive(type));
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>A ground plane, two spheres and a cube.</summary>
    void Stage(Material material)
    {
        var ground = Primitive(PrimitiveType.Plane, Vector3.zero, material);
        ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Primitive(PrimitiveType.Sphere, new Vector3(-1f, 0.5f, 0f), material);
        Primitive(PrimitiveType.Sphere, new Vector3(1f, 0.5f, 0f), material);
        Primitive(PrimitiveType.Cube, new Vector3(0f, 0.5f, 1f), material);
    }

    Light Directional(Vector3 euler, Color color, float intensity)
    {
        var go = Add(new GameObject("directional"));
        go.transform.rotation = Quaternion.Euler(euler);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        return l;
    }

    Light Point(Vector3 position, Color color, float range, float intensity)
    {
        var go = Add(new GameObject("point"));
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        return l;
    }

    Camera MakeCamera(RenderTexture target = null)
    {
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 2.2f, -4.5f);
        go.transform.LookAt(new Vector3(0f, 0.3f, 0.3f));
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
        camera.targetTexture = target != null ? target : Add(new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32));
        return camera;
    }

    Camera OrthoCamera(float size = 2f)
    {
        var go = Add(new GameObject("camera"));
        go.transform.position = new Vector3(0f, 0f, -10f);
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = size;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.targetTexture = Add(new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32));
        return camera;
    }

    static Texture2D Read(RenderTexture rt, TextureFormat format = TextureFormat.RGBA32)
    {
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, format, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = active;
        return texture;
    }

    void Save(RenderTexture rt, string name)
    {
        var texture = Read(rt);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), texture.EncodeToPNG());
        DestroyImmediate(texture);
    }

    /// <summary>Renders the camera and saves its image.</summary>
    void Capture(Camera camera, string name)
    {
        camera.Render();
        Save(camera.targetTexture, name);
    }

    static Texture2D Checker()
    {
        var t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                bool on = ((x / 8) + (y / 8)) % 2 == 0;
                t.SetPixel(x, y, on ? Color.white : new Color(0.2f, 0.2f, 0.2f, 0f));
            }
        t.wrapMode = TextureWrapMode.Repeat;
        t.Apply();
        return t;
    }

    // ------------------------------------------------------------------ camera events

    /// <summary>Command buffers at every camera event: each copies a clock (then advances it),
    /// and samples the depth, depth-normals and motion-vector textures at the middle of the view,
    /// so what fired, in which order, and what the textures held then can be read back.</summary>
    sealed class EventLog
    {
        readonly Dictionary<CameraEvent, RenderTexture[]> m_Slots = new Dictionary<CameraEvent, RenderTexture[]>();
        readonly List<RenderTexture> m_All = new List<RenderTexture>();
        readonly List<(CameraEvent, CommandBuffer)> m_Buffers = new List<(CameraEvent, CommandBuffer)>();
        readonly Camera m_Camera;

        readonly Dictionary<string, RenderTexture> m_Messages = new Dictionary<string, RenderTexture>();
        ClockMessages m_Script;

        public EventLog(Camera camera, Material increment, Material sample)
        {
            m_Camera = camera;
            var clock = New(new Color(0f, 0f, 0f, 1f));
            var tmp = New(Color.clear);
            var junk = New(Color.clear);
            m_Script = camera.gameObject.AddComponent<ClockMessages>();
            m_Script.clock = clock;
            m_Script.tmp = tmp;
            m_Script.increment = increment;
            m_Script.preRender = m_Messages["OnPreRender"] = New(Color.clear);
            m_Script.renderObject = m_Messages["OnRenderObject"] = New(Color.clear);
            m_Script.postRender = m_Messages["OnPostRender"] = New(Color.clear);
            foreach (CameraEvent e in Enum.GetValues(typeof(CameraEvent)))
            {
                var slots = new[] { New(Color.clear), New(Color.clear), New(Color.clear), New(Color.clear) };
                m_Slots[e] = slots;
                var cmd = new CommandBuffer { name = "log " + e };
                cmd.Blit(clock, slots[0]);
                cmd.Blit(clock, tmp, increment, 0);
                cmd.Blit(tmp, clock);
                cmd.Blit(clock, slots[1], sample, 0);
                cmd.Blit(clock, slots[2], sample, 1);
                cmd.Blit(clock, slots[3], sample, 2);
                // Whatever draws next into the active target lands here.
                cmd.Blit(clock, junk);
                camera.AddCommandBuffer(e, cmd);
                m_Buffers.Add((e, cmd));
            }
        }

        RenderTexture New(Color clear)
        {
            var rt = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rt.Create();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, clear);
            RenderTexture.active = active;
            m_All.Add(rt);
            return rt;
        }

        static Color Pixel(RenderTexture rt)
        {
            var t = Read(rt, TextureFormat.RGBAFloat);
            var c = t.GetPixel(0, 0);
            DestroyImmediate(t);
            return c;
        }

        /// <summary>Per event that fired, in order: its clock and the three samples.</summary>
        public void Write(BuiltinProbe probe, string prefix)
        {
            var fired = new List<(int Clock, CameraEvent Event)>();
            foreach (var pair in m_Slots)
            {
                var clock = Pixel(pair.Value[0]);
                if (clock.a < 0.5f) continue;
                fired.Add((Mathf.RoundToInt(clock.r * 255f / 8f), pair.Key));
            }
            fired.Sort((a, b) => a.Clock.CompareTo(b.Clock));
            var order = new StringBuilder("[");
            foreach (var (clock, e) in fired) order.Append(order.Length > 1 ? "," : "").Append(S(e.ToString()));
            probe.Value(prefix + ".order", order.Append(']').ToString());
            // The messages' places: the clock they saw.
            var messages = new List<(int Clock, string Name)>();
            foreach (var pair in m_Messages)
            {
                var clock = Pixel(pair.Value);
                if (clock.a < 0.5f) continue;
                messages.Add((Mathf.RoundToInt(clock.r * 255f / 8f), pair.Key));
            }
            messages.Sort((a, b) => a.Clock.CompareTo(b.Clock));
            var all = new List<(int Clock, string Name)>();
            foreach (var (clock, e) in fired) all.Add((clock * 2, e.ToString()));
            foreach (var (clock, name) in messages) all.Add((clock * 2 - 1, name));
            all.Sort((a, b) => a.Clock.CompareTo(b.Clock));
            var withMessages = new StringBuilder("[");
            foreach (var (clock, name) in all) withMessages.Append(withMessages.Length > 1 ? "," : "").Append(S(name));
            probe.Value(prefix + ".orderWithMessages", withMessages.Append(']').ToString());
            DestroyImmediate(m_Script);
            foreach (var (clock, e) in fired)
            {
                var slots = m_Slots[e];
                probe.Value(prefix + "." + e + ".clock", clock.ToString(CultureInfo.InvariantCulture));
                probe.Value(prefix + "." + e + ".depth", C(Pixel(slots[1])));
                probe.Value(prefix + "." + e + ".depthnormals", C(Pixel(slots[2])));
                probe.Value(prefix + "." + e + ".motion", C(Pixel(slots[3])));
            }
            foreach (var (e, cmd) in m_Buffers) m_Camera.RemoveCommandBuffer(e, cmd);
            foreach (var rt in m_All) DestroyImmediate(rt);
        }
    }

    /// <summary>One camera render of the stage with the event log.</summary>
    void LogEvents(string name, DepthTextureMode mode, Action<Camera> setup = null)
    {
        Stage(Standard(Color.white));
        var camera = MakeCamera();
        camera.depthTextureMode = mode;
        setup?.Invoke(camera);
        var log = new EventLog(camera, m_Increment, m_Sample);
        camera.Render();
        log.Write(this, "events." + name);
        Clear();
    }

    // ------------------------------------------------------------------ camera textures

    /// <summary>A command buffer after everything that shows one of the camera's textures.</summary>
    RenderTexture ShowAfterEverything(Camera camera, int pass, RenderTextureFormat format = RenderTextureFormat.ARGB32)
    {
        var output = Add(new RenderTexture(Width, Height, 0, format, RenderTextureReadWrite.Linear));
        var cmd = new CommandBuffer { name = "show" };
        cmd.Blit(BuiltinRenderTextureType.CurrentActive, output, m_Show, pass);
        camera.AddCommandBuffer(CameraEvent.AfterEverything, cmd);
        return output;
    }

    /// <summary>The stage with what each camera texture leaves out: a transparent sphere, a
    /// cutout quad and a sphere whose shader has neither a RenderType nor a shadow caster.</summary>
    void TextureStage()
    {
        Stage(Standard(Color.white));
        var transparent = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")));
        transparent.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Primitive(PrimitiveType.Sphere, new Vector3(-0.4f, 0.6f, -1.2f), transparent, 0.7f);
        var cutout = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse")));
        cutout.mainTexture = Add(Checker());
        var quad = Primitive(PrimitiveType.Quad, new Vector3(0.6f, 0.6f, -1f), cutout, 0.9f);
        quad.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        Primitive(PrimitiveType.Sphere, new Vector3(0f, 1.3f, 0.2f), Add(new Material(Shader.Find("Probe/NoRenderType"))), 0.5f);
    }

    void CameraTextures()
    {
        TextureStage();
        var camera = MakeCamera();
        camera.depthTextureMode = DepthTextureMode.Depth;
        var depth = ShowAfterEverything(camera, 0);
        Capture(camera, "depth_color");
        Save(depth, "depth");
        Clear();

        TextureStage();
        camera = MakeCamera();
        camera.depthTextureMode = DepthTextureMode.DepthNormals;
        var normals = ShowAfterEverything(camera, 1);
        Capture(camera, "depthnormals_color");
        Save(normals, "depthnormals");
        Clear();

        // A shadowed sun gives the camera a depth texture of its own.
        TextureStage();
        var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        sun.shadows = LightShadows.Hard;
        camera = MakeCamera();
        depth = ShowAfterEverything(camera, 0);
        Capture(camera, "depth_shadows_color");
        Save(depth, "depth_shadows");
        Clear();
    }

    // ------------------------------------------------------------------ matrices

    void Matrices()
    {
        var camera = MakeCamera();
        Value("matrices.fresh.projection", M(camera.projectionMatrix));
        Value("matrices.fresh.nonJittered", M(camera.nonJitteredProjectionMatrix));
        Value("matrices.fresh.previousViewProjection", M(camera.previousViewProjectionMatrix));
        var custom = Matrix4x4.Perspective(30f, 2f, 0.5f, 50f);
        camera.projectionMatrix = custom;
        Value("matrices.setProjection.projection", M(camera.projectionMatrix));
        Value("matrices.setProjection.nonJittered", M(camera.nonJitteredProjectionMatrix));
        var jitter = Matrix4x4.Translate(new Vector3(0.01f, -0.02f, 0f)) * custom;
        camera.nonJitteredProjectionMatrix = custom;
        camera.projectionMatrix = jitter;
        Value("matrices.jittered.projection", M(camera.projectionMatrix));
        Value("matrices.jittered.nonJittered", M(camera.nonJitteredProjectionMatrix));
        camera.ResetProjectionMatrix();
        Value("matrices.reset.projection", M(camera.projectionMatrix));
        Value("matrices.reset.nonJittered", M(camera.nonJitteredProjectionMatrix));
        camera.nonJitteredProjectionMatrix = custom;
        Value("matrices.setNonJittered.projection", M(camera.projectionMatrix));
        Value("matrices.setNonJittered.nonJittered", M(camera.nonJitteredProjectionMatrix));
        camera.ResetProjectionMatrix();
        Value("matrices.useJitteredForTransparent", B(camera.useJitteredProjectionMatrixForTransparentRendering));
        Clear();
    }

    /// <summary>Motion vectors over three frames: a first render, then the camera and a cube
    /// moved, then nothing moved.</summary>
    IEnumerator Motion()
    {
        Stage(Standard(Color.white));
        var cube = Primitive(PrimitiveType.Cube, new Vector3(0f, 0.6f, -0.8f), Standard(new Color(0.3f, 0.6f, 1f)), 0.6f);
        var camera = MakeCamera();
        camera.enabled = false;
        camera.depthTextureMode = DepthTextureMode.MotionVectors;
        var image = ShowAfterEverything(camera, 2);
        var raw = ShowAfterEverything(camera, 3, RenderTextureFormat.ARGBFloat);
        var log = new EventLog(camera, m_Increment, m_Sample);
        Value("motion.frame1.before.previousViewProjection", M(camera.previousViewProjectionMatrix));
        camera.Render();
        Value("motion.frame1.after.previousViewProjection", M(camera.previousViewProjectionMatrix));
        Value("motion.frame1.viewProjection", M(GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix));
        Value("motion.frame1.plainViewProjection", M(camera.projectionMatrix * camera.worldToCameraMatrix));
        log.Write(this, "events.motion_frame1");
        Save(image, "motion_frame1");
        WriteMotion(raw, "motion.frame1");
        yield return null;

        camera.transform.position += new Vector3(0.1f, 0.05f, 0f);
        cube.transform.position += new Vector3(0.25f, 0f, 0f);
        log = new EventLog(camera, m_Increment, m_Sample);
        Value("motion.frame2.before.previousViewProjection", M(camera.previousViewProjectionMatrix));
        camera.Render();
        Value("motion.frame2.after.previousViewProjection", M(camera.previousViewProjectionMatrix));
        log.Write(this, "events.motion_frame2");
        Save(image, "motion_frame2");
        WriteMotion(raw, "motion.frame2");
        Capture(camera, "motion_frame2_color");
        yield return null;

        camera.Render();
        Save(image, "motion_frame3");
        WriteMotion(raw, "motion.frame3");
        Clear();
    }

    /// <summary>Motion vectors of a camera the player loop renders each frame: nothing moves,
    /// then the camera and a cube move, then nothing moves again.</summary>
    IEnumerator MotionAuto()
    {
        Stage(Standard(Color.white));
        var cube = Primitive(PrimitiveType.Cube, new Vector3(0f, 0.6f, -0.8f), Standard(new Color(0.3f, 0.6f, 1f)), 0.6f);
        var camera = MakeCamera();
        camera.depthTextureMode = DepthTextureMode.MotionVectors;
        var image = ShowAfterEverything(camera, 2);
        var raw = ShowAfterEverything(camera, 3, RenderTextureFormat.ARGBFloat);
        for (int frame = 1; frame <= 5; frame++)
        {
            yield return null;
            Value("motion_auto.frame" + frame + ".previousViewProjection", M(camera.previousViewProjectionMatrix));
            Save(image, "motion_auto_frame" + frame);
            WriteMotion(raw, "motion_auto.frame" + frame);
            if (frame == 2)
            {
                camera.transform.position += new Vector3(0.1f, 0.05f, 0f);
                cube.transform.position += new Vector3(0.25f, 0f, 0f);
            }
        }
        Clear();
    }

    void WriteMotion(RenderTexture raw, string prefix)
    {
        var t = Read(raw, TextureFormat.RGBAFloat);
        var sb = new StringBuilder("[");
        for (int y = 15; y < Height; y += 30)
            for (int x = 20; x < Width; x += 40)
            {
                var c = t.GetPixel(x, y);
                sb.Append(sb.Length > 1 ? "," : "").Append("[" + F(c.r) + "," + F(c.g) + "]");
            }
        Value(prefix + ".samples", sb.Append(']').ToString());
        DestroyImmediate(t);
    }

    // ------------------------------------------------------------------ image effects

    void ImageEffects()
    {
        foreach (var (name, opaque, tint) in new[] { ("effect_opaque", true, false), ("effect_both", true, true), ("effect_plain", false, true) })
        {
            Stage(Standard(Color.white));
            Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
            var transparent = Add(new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse")));
            transparent.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Primitive(PrimitiveType.Sphere, new Vector3(0f, 0.7f, -1f), transparent, 0.9f);
            var camera = MakeCamera();
            if (opaque) camera.gameObject.AddComponent<OpaqueInvert>().material = m_Effects;
            if (tint) camera.gameObject.AddComponent<Tint>().material = m_Effects;
            Capture(camera, name);
            Clear();
        }
    }

    // ------------------------------------------------------------------ disabled passes

    void DisabledPasses()
    {
        Stage(Standard(Color.white));
        var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        sun.shadows = LightShadows.Hard;
        var noShadow = Standard(new Color(1f, 0.4f, 0.4f));
        noShadow.SetShaderPassEnabled("ShadowCaster", false);
        Primitive(PrimitiveType.Cube, new Vector3(0.2f, 1.2f, -0.9f), noShadow, 0.5f);
        var asset = Resources.Load<Material>("NoShadow");
        Primitive(PrimitiveType.Sphere, new Vector3(-1f, 1.3f, -0.8f), asset, 0.5f);
        var noBase = Standard(new Color(0.4f, 1f, 0.4f));
        noBase.SetShaderPassEnabled("ForwardBase", false);
        Primitive(PrimitiveType.Sphere, new Vector3(1.2f, 1.2f, -0.8f), noBase, 0.5f);
        var unlit = Add(new Material(Shader.Find("Unlit/Color")));
        unlit.color = Color.yellow;
        unlit.SetShaderPassEnabled("Always", false);
        Primitive(PrimitiveType.Quad, new Vector3(-0.3f, 0.3f, -1.5f), unlit, 0.4f);
        var camera = MakeCamera();
        Capture(camera, "disabled_passes");

        Value("passes.noShadow.ShadowCaster", B(noShadow.GetShaderPassEnabled("ShadowCaster")));
        Value("passes.noShadow.SHADOWCASTER", B(noShadow.GetShaderPassEnabled("SHADOWCASTER")));
        Value("passes.noShadow.shadowcaster", B(noShadow.GetShaderPassEnabled("shadowcaster")));
        Value("passes.noShadow.ForwardBase", B(noShadow.GetShaderPassEnabled("ForwardBase")));
        Value("passes.asset.ShadowCaster", B(asset.GetShaderPassEnabled("ShadowCaster")));
        Value("passes.asset.ForwardBase", B(asset.GetShaderPassEnabled("ForwardBase")));
        Value("passes.noBase.ForwardBase", B(noBase.GetShaderPassEnabled("ForwardBase")));
        Value("passes.unlit.Always", B(unlit.GetShaderPassEnabled("Always")));
        var copy = new Material(noShadow);
        Value("passes.copy.ShadowCaster", B(copy.GetShaderPassEnabled("ShadowCaster")));
        noShadow.SetShaderPassEnabled("shadowcaster", true);
        Value("passes.reenabled.ShadowCaster", B(noShadow.GetShaderPassEnabled("ShadowCaster")));
        DestroyImmediate(copy);
        Clear();
    }

    // ------------------------------------------------------------------ transparency sorting

    Sprite m_Sprite, m_BottomSprite;

    SpriteRenderer SpriteAt(Vector3 position, Color color, Sprite sprite = null, float scale = 1.5f)
    {
        var go = Add(new GameObject("sprite"));
        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite != null ? sprite : m_Sprite;
        r.color = color;
        return r;
    }

    /// <summary>Three overlapping sprites whose order differs along z, y and x.</summary>
    void Sprites()
    {
        SpriteAt(new Vector3(-0.4f, 0f, 0f), Color.red);
        SpriteAt(new Vector3(0f, -0.4f, 1f), Color.green);
        SpriteAt(new Vector3(0.4f, 0.4f, -1f), Color.blue);
    }

    void Sorting()
    {
        var white = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        var pixels = new Color32[256];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        white.SetPixels32(pixels);
        white.Apply();
        m_Sprite = Sprite.Create(white, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        m_BottomSprite = Sprite.Create(white, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0f), 16f);

        Value("sorting.graphics.mode", S(GraphicsSettings.transparencySortMode.ToString()));
        Value("sorting.graphics.axis", V(GraphicsSettings.transparencySortAxis));

        Sprites();
        var camera = OrthoCamera();
        Value("sorting.camera.mode", S(camera.transparencySortMode.ToString()));
        Value("sorting.camera.axis", V(camera.transparencySortAxis));
        Capture(camera, "sort_project");
        Clear();

        Sprites();
        camera = OrthoCamera();
        camera.transparencySortMode = TransparencySortMode.Orthographic;
        Capture(camera, "sort_orthographic");
        Clear();

        Sprites();
        camera = OrthoCamera();
        camera.transparencySortMode = TransparencySortMode.CustomAxis;
        camera.transparencySortAxis = new Vector3(1f, 0f, 0f);
        Capture(camera, "sort_camera_axis");
        Clear();

        Sprites();
        camera = OrthoCamera();
        camera.orthographic = false;
        camera.fieldOfView = 25f;
        camera.transparencySortMode = TransparencySortMode.Perspective;
        Capture(camera, "sort_perspective");
        Clear();

        // The project's setting changed from script.
        var savedMode = GraphicsSettings.transparencySortMode;
        var savedAxis = GraphicsSettings.transparencySortAxis;
        GraphicsSettings.transparencySortMode = TransparencySortMode.Orthographic;
        Sprites();
        camera = OrthoCamera();
        Capture(camera, "sort_graphics_orthographic");
        Clear();
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = new Vector3(-1f, 0f, 0f);
        Sprites();
        camera = OrthoCamera();
        Capture(camera, "sort_graphics_axis");
        Clear();
        GraphicsSettings.transparencySortMode = savedMode;
        GraphicsSettings.transparencySortAxis = savedAxis;

        // Sort points: a sprite pivoted at its bottom sorts at its bounds' centre or its pivot.
        foreach (var (name, point) in new[] { ("sort_point_center", SpriteSortPoint.Center), ("sort_point_pivot", SpriteSortPoint.Pivot) })
        {
            var low = SpriteAt(new Vector3(-0.3f, -0.6f, 0f), Color.red, m_BottomSprite);
            low.spriteSortPoint = point;
            SpriteAt(new Vector3(0.3f, -0.2f, 0f), Color.green);
            camera = OrthoCamera();
            Capture(camera, name);
            Clear();
        }

        // Renderer priority (ignored by the built-in pipeline) among sprites at the same depth.
        foreach (var (name, priority) in new[] { ("sort_priority_none", 0), ("sort_priority_first", 1), ("sort_priority_negative", -1) })
        {
            var first = SpriteAt(new Vector3(-0.3f, 0f, 0f), Color.red);
            first.rendererPriority = priority;
            SpriteAt(new Vector3(0.3f, 0f, 0f), Color.green);
            camera = OrthoCamera();
            Capture(camera, name);
            Value("sorting." + name + ".rendererPriority", first.rendererPriority.ToString(CultureInfo.InvariantCulture));
            Clear();
        }
        DestroyImmediate(m_Sprite);
        DestroyImmediate(m_BottomSprite);
        DestroyImmediate(white);
    }

    // ------------------------------------------------------------------ render texture assets

    void WriteTexture(string prefix, RenderTexture rt)
    {
        Value(prefix + ".name", S(rt.name));
        Value(prefix + ".size", "[" + rt.width + "," + rt.height + "," + rt.volumeDepth + "]");
        Value(prefix + ".depth", rt.depth.ToString(CultureInfo.InvariantCulture));
        Value(prefix + ".format", S(rt.format.ToString()));
        Value(prefix + ".graphicsFormat", S(rt.graphicsFormat.ToString()));
        Value(prefix + ".depthStencilFormat", S(rt.depthStencilFormat.ToString()));
        Value(prefix + ".antiAliasing", rt.antiAliasing.ToString(CultureInfo.InvariantCulture));
        Value(prefix + ".useMipMap", B(rt.useMipMap));
        Value(prefix + ".autoGenerateMips", B(rt.autoGenerateMips));
        Value(prefix + ".mipmapCount", rt.mipmapCount.ToString(CultureInfo.InvariantCulture));
        Value(prefix + ".dimension", S(rt.dimension.ToString()));
        Value(prefix + ".filterMode", S(rt.filterMode.ToString()));
        Value(prefix + ".wrapMode", "[" + S(rt.wrapModeU.ToString()) + "," + S(rt.wrapModeV.ToString()) + "," + S(rt.wrapModeW.ToString()) + "]");
        Value(prefix + ".anisoLevel", rt.anisoLevel.ToString(CultureInfo.InvariantCulture));
        Value(prefix + ".mipMapBias", F(rt.mipMapBias));
        Value(prefix + ".enableRandomWrite", B(rt.enableRandomWrite));
        Value(prefix + ".sRGB", B(rt.sRGB));
        Value(prefix + ".useDynamicScale", B(rt.useDynamicScale));
        Value(prefix + ".bindTextureMS", B(rt.bindTextureMS));
        Value(prefix + ".memorylessMode", S(rt.memorylessMode.ToString()));
        Value(prefix + ".shadowSamplingMode", S(rt.descriptor.shadowSamplingMode.ToString()));
        Value(prefix + ".isPowerOfTwo", B(rt.isPowerOfTwo));
    }

    void RenderTextureAssets()
    {
        foreach (var name in new[] { "ProbeTarget", "ProbeHalf", "ProbeCube", "ProbeVolume", "ProbeShadow" })
        {
            var rt = Resources.Load<RenderTexture>(name);
            if (rt == null)
            {
                Value("rt." + name, "null");
                continue;
            }
            WriteTexture("rt." + name, rt);
        }
        var target = Resources.Load<RenderTexture>("ProbeTarget");
        Stage(Standard(Color.white));
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var camera = MakeCamera(target);
        camera.Render();
        Value("rt.ProbeTarget.isCreated", B(target.IsCreated()));
        Save(target, "rt_asset");
        Clear();
    }

    // ------------------------------------------------------------------ property blocks

    /// <summary>Two quads side by side, submesh 0 on the left, 1 on the right.</summary>
    static Mesh TwoSubmeshes()
    {
        var mesh = new Mesh { name = "two" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0f),
            new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
        };
        mesh.subMeshCount = 2;
        mesh.SetTriangles(new[] { 0, 1, 2, 2, 3, 0 }, 0);
        mesh.SetTriangles(new[] { 4, 5, 6, 6, 7, 4 }, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    void PropertyBlocks()
    {
        var mesh = Add(TwoSubmeshes());
        var shader = Shader.Find("Probe/TwoColors");
        MeshRenderer Make(float x)
        {
            var go = Add(new GameObject("two"));
            go.transform.position = new Vector3(x, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { Add(new Material(shader)), Add(new Material(shader)) };
            return r;
        }
        MaterialPropertyBlock Block(string name, Color c)
        {
            var b = new MaterialPropertyBlock();
            b.SetColor(name, c);
            return b;
        }
        // Renderer-wide tint, then the right half's colour.
        var r1 = Make(-2.25f);
        r1.SetPropertyBlock(Block("_Tint", new Color(1f, 1f, 0f, 1f)));
        r1.SetPropertyBlock(Block("_Color", new Color(0f, 1f, 1f, 1f)), 1);
        // The left half's colour alone.
        var r2 = Make(-0.75f);
        r2.SetPropertyBlock(Block("_Color", Color.red), 0);
        // The right half's colour first, then a renderer-wide tint.
        var r3 = Make(0.75f);
        r3.SetPropertyBlock(Block("_Color", Color.blue), 1);
        r3.SetPropertyBlock(Block("_Tint", new Color(1f, 0f, 1f, 1f)));
        // Both halves' own blocks with the same name as the renderer-wide one.
        var r4 = Make(2.25f);
        r4.SetPropertyBlock(Block("_Color", new Color(0.5f, 0.5f, 0.5f, 1f)));
        r4.SetPropertyBlock(Block("_Color", Color.green), 0);

        var camera = OrthoCamera(2.1f);
        Capture(camera, "property_blocks");

        int i = 0;
        foreach (var r in new[] { r1, r2, r3, r4 })
        {
            i++;
            string p = "blocks.r" + i;
            Value(p + ".has", B(r.HasPropertyBlock()));
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            Value(p + ".all.empty", B(b.isEmpty));
            Value(p + ".all._Color", C(b.GetColor("_Color")));
            Value(p + ".all._Tint", C(b.GetColor("_Tint")));
            for (int m = 0; m < 2; m++)
            {
                b = new MaterialPropertyBlock();
                r.GetPropertyBlock(b, m);
                Value(p + "." + m + ".empty", B(b.isEmpty));
                Value(p + "." + m + "._Color", C(b.GetColor("_Color")));
                Value(p + "." + m + "._Tint", C(b.GetColor("_Tint")));
            }
        }
        r1.SetPropertyBlock(null);
        Value("blocks.r1.clearedAll.has", B(r1.HasPropertyBlock()));
        r1.SetPropertyBlock(null, 1);
        Value("blocks.r1.clearedIndex.has", B(r1.HasPropertyBlock()));
        Clear();
    }

    // ------------------------------------------------------------------ Graphics.DrawMesh

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return mesh;
    }

    void DrawMeshes()
    {
        var ground = Primitive(PrimitiveType.Plane, Vector3.zero, Standard(Color.white));
        ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 0.8f);
        sun.shadows = LightShadows.Hard;
        var point = Point(new Vector3(0.6f, 1.2f, -1.2f), new Color(1f, 0.3f, 0.2f), 3f, 2f);
        point.renderMode = LightRenderMode.ForcePixel;
        var sphere = PrimitiveMesh(PrimitiveType.Sphere);
        var cube = PrimitiveMesh(PrimitiveType.Cube);
        var material = Standard(Color.white);
        material.enableInstancing = true;
        var camera = MakeCamera();
        camera.cullingMask = ~(1 << 8);
        // Casts and receives; neither; on a layer the camera does not see.
        Graphics.DrawMesh(sphere, Matrix4x4.TRS(new Vector3(-1f, 0.5f, 0f), Quaternion.identity, Vector3.one), material, 0, camera, 0, null, ShadowCastingMode.On, true);
        Graphics.DrawMesh(sphere, Matrix4x4.TRS(new Vector3(1f, 0.5f, 0f), Quaternion.identity, Vector3.one), material, 0, camera, 0, null, ShadowCastingMode.Off, false);
        Graphics.DrawMesh(cube, Matrix4x4.TRS(new Vector3(0f, 0.5f, 1f), Quaternion.identity, Vector3.one), material, 8, camera);
        // RenderMesh: a floating cube casting and receiving.
        var rp = new RenderParams(material) { camera = camera, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, layer = 0 };
        Graphics.RenderMesh(rp, cube, 0, Matrix4x4.TRS(new Vector3(0f, 1.3f, -0.6f), Quaternion.Euler(20f, 30f, 0f), Vector3.one * 0.4f));
        // Instanced cubes casting shadows, ShadowsOnly ones.
        var matrices = new Matrix4x4[3];
        for (int i = 0; i < 3; i++) matrices[i] = Matrix4x4.TRS(new Vector3(-1.2f + 1.2f * i, 0.15f, -1.4f), Quaternion.identity, Vector3.one * 0.3f);
        Graphics.DrawMeshInstanced(cube, 0, material, matrices, 3, null, ShadowCastingMode.On, true, 0, camera);
        var only = new Matrix4x4[1] { Matrix4x4.TRS(new Vector3(-0.6f, 1.6f, -0.2f), Quaternion.identity, Vector3.one * 0.35f) };
        Graphics.DrawMeshInstanced(cube, 0, material, only, 1, null, ShadowCastingMode.ShadowsOnly, true, 0, camera);
        Capture(camera, "draw_mesh");
        Clear();
    }

    // ------------------------------------------------------------------ main light

    void MainLight()
    {
        Stage(Standard(Color.white));
        var excluded = Primitive(PrimitiveType.Sphere, new Vector3(0f, 1.2f, -0.8f), Standard(Color.white), 0.6f);
        excluded.layer = 8;
        var sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        sun.cullingMask = ~(1 << 8);
        var camera = MakeCamera();
        Capture(camera, "main_light_mask");
        // A second, dimmer sun every layer sees.
        Directional(new Vector3(30f, 120f, 0f), new Color(1f, 0.5f, 0.2f), 0.6f);
        Capture(camera, "main_light_mask_second");
        Clear();

        Stage(Standard(Color.white));
        sun = Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        sun.cookie = Add(Checker());
        sun.cookieSize = 1.5f;
        camera = MakeCamera();
        Capture(camera, "main_light_cookie");
        Point(new Vector3(0f, 1.2f, -0.6f), new Color(0.3f, 0.6f, 1f), 3f, 2f);
        Capture(camera, "main_light_cookie_point");
        Clear();
    }

    // ------------------------------------------------------------------ shader LOD

    void ShaderLod()
    {
        var lod = Shader.Find("Probe/LodColors");
        var none = Shader.Find("Probe/LodNone");
        var high = Shader.Find("Probe/LodHigh");
        int savedGlobal = Shader.globalMaximumLOD;
        Value("lod.globalMaximumLOD", savedGlobal.ToString(CultureInfo.InvariantCulture));
        Value("lod.maximumLOD", lod.maximumLOD.ToString(CultureInfo.InvariantCulture));
        var a = Primitive(PrimitiveType.Quad, new Vector3(-1.2f, 0f, 0f), Add(new Material(lod)), 1f);
        var b = Primitive(PrimitiveType.Quad, new Vector3(0f, 0f, 0f), Add(new Material(none)), 1f);
        var c = Primitive(PrimitiveType.Quad, new Vector3(1.2f, 0f, 0f), Add(new Material(high)), 1f);
        var camera = OrthoCamera(1f);
        void Sample(string name)
        {
            camera.Render();
            var t = Read(camera.targetTexture);
            var sb = new StringBuilder("[");
            foreach (var x in new[] { -1.2f, 0f, 1.2f })
            {
                var p = camera.WorldToScreenPoint(new Vector3(x, 0f, 0f));
                var color = t.GetPixel((int)p.x, (int)p.y);
                sb.Append(sb.Length > 1 ? "," : "").Append(C(color));
            }
            Value("lod." + name, sb.Append(']').ToString());
            Value("lod." + name + ".passCount", lod.passCount.ToString(CultureInfo.InvariantCulture));
            DestroyImmediate(t);
        }
        Sample("default");
        lod.maximumLOD = 250;
        Sample("shader250");
        lod.maximumLOD = 150;
        Sample("shader150");
        lod.maximumLOD = 50;
        Sample("shader50");
        lod.maximumLOD = -1;
        Shader.globalMaximumLOD = 250;
        Sample("global250");
        Shader.globalMaximumLOD = 50;
        Sample("global50");
        Shader.globalMaximumLOD = 0;
        Sample("global0");
        lod.maximumLOD = 300;
        Sample("global0_shader300");
        lod.maximumLOD = -1;
        Shader.globalMaximumLOD = savedGlobal;
        Sample("restored");
        Clear();
    }

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("BUILTIN_PROBE_OUT");
        if (!string.IsNullOrEmpty(m_Dir))
        {
            Directory.CreateDirectory(m_Dir);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.05f);
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.skybox = null;
            m_Increment = new Material(Shader.Find("Probe/Increment"));
            m_Sample = new Material(Shader.Find("Probe/SampleTextures"));
            m_Show = new Material(Shader.Find("Probe/ShowTextures"));
            m_Effects = new Material(Shader.Find("Probe/Invert"));

            bool soft = QualitySettings.softParticles;
            Value("quality.softParticles", B(soft));
            QualitySettings.softParticles = false;
            LogEvents("plain", DepthTextureMode.None);
            LogEvents("depth", DepthTextureMode.Depth);
            LogEvents("depthnormals", DepthTextureMode.DepthNormals);
            LogEvents("depth_depthnormals", DepthTextureMode.Depth | DepthTextureMode.DepthNormals);
            LogEvents("motion", DepthTextureMode.MotionVectors);
            LogEvents("shadows", DepthTextureMode.None, c => Directional(new Vector3(50f, -30f, 0f), Color.white, 1f).shadows = LightShadows.Hard);
            LogEvents("effects", DepthTextureMode.None, c =>
            {
                c.gameObject.AddComponent<OpaqueInvert>().material = m_Effects;
                c.gameObject.AddComponent<Tint>().material = m_Effects;
            });
            LogEvents("effect_opaque", DepthTextureMode.None, c => c.gameObject.AddComponent<OpaqueInvert>().material = m_Effects);
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            LogEvents("skybox", DepthTextureMode.None, c =>
            {
                c.clearFlags = CameraClearFlags.Skybox;
                RenderSettings.skybox = sky;
            });
            RenderSettings.skybox = null;
            LogEvents("skybox_none", DepthTextureMode.None, c => c.clearFlags = CameraClearFlags.Skybox);
            LogEvents("skybox_motion", DepthTextureMode.MotionVectors, c =>
            {
                // Away from the other cameras: its motion differs from what they left bound.
                c.transform.position += new Vector3(0.3f, 0f, 0f);
                c.clearFlags = CameraClearFlags.Skybox;
                RenderSettings.skybox = sky;
            });
            RenderSettings.skybox = null;
            DestroyImmediate(sky);
            QualitySettings.softParticles = true;
            LogEvents("soft_particles", DepthTextureMode.None);
            QualitySettings.softParticles = soft;

            CameraTextures();
            Matrices();
            ImageEffects();
            DisabledPasses();
            Sorting();
            RenderTextureAssets();
            PropertyBlocks();
            DrawMeshes();
            MainLight();
            ShaderLod();
            yield return Motion();
            yield return MotionAuto();

            File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Values.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
