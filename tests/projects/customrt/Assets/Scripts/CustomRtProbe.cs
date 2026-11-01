using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;

/// <summary>
/// Custom render textures: initialization (color, texture and color, materials), update modes
/// and periods, Update/Initialize calls, update zones (spaces, rotation, passes, wrapping,
/// batches, swaps), double buffering and the self textures, cubemap faces, volumes, the
/// variables the shaders see, `.asset` loading, the manager's events and where in the frame
/// updates happen. Images are read back as RGBA floats; values and events go to values.jsonl.
///
/// The frame points: <c>u</c> after the probe's own Update, <c>b</c>/<c>a</c> right before/after
/// PostLateUpdate.UpdateCustomRenderTextures, <c>e</c> at the end of PostLateUpdate.
/// </summary>
public class CustomRtProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    static CustomRtProbe s_Probe;

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> events = new List<string>();
    readonly Dictionary<string, CustomRenderTexture> tracked = new Dictionary<string, CustomRenderTexture>();
    readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    string directory;
    string section = "";
    int sectionFrame;
    string point = "boot";
    IEnumerator steps;
    Texture2D pixel;
    Shader coords, self, selfCube, self3D, depend, init, initTwo, leak;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("CUSTOMRT_PROBE_OUT") == null) return;
        new GameObject("CustomRtProbe").AddComponent<CustomRtProbe>();
    }

    // ------------------------------------------------------------------ recording

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Q(f.ToString("R")) : f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static string V(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";
    static string V3(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string L(IEnumerable<string> items)
    {
        var s = new StringBuilder("[");
        bool first = true;
        foreach (var i in items) { s.Append(first ? "" : ",").Append(Q(i)); first = false; }
        return s.Append("]").ToString();
    }

    void OnLog(string condition, string stack, LogType type)
    {
        if (condition.StartsWith("[customrt]")) return;
        logs.Add(type + ": " + condition);
    }

    void Event(string kind, CustomRenderTexture t, int count)
    {
        if (t == null || !t.name.StartsWith("crt.")) return;
        events.Add(sectionFrame + ":" + point + ":" + kind + ":" + t.name + (kind == "update" ? ":" + count : ""));
    }

    /// <summary>All pixels of a 2D render texture (or a face/slice copied out of a cube or volume).</summary>
    void Read(string key, RenderTexture rt, int element = -1)
    {
        var source = rt;
        RenderTexture copy = null;
        if (element >= 0)
        {
            copy = new RenderTexture(rt.width, rt.height, 0, rt.graphicsFormat) { filterMode = FilterMode.Point };
            copy.Create();
            Graphics.CopyTexture(rt, element, 0, copy, 0, 0);
            source = copy;
        }
        var saved = RenderTexture.active;
        RenderTexture.active = source;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        RenderTexture.active = saved;
        var pixels = image.GetPixels();
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, key + ".rgba"))))
            foreach (var c in pixels) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        Value(key + ".pixels", pixels.Length.ToString());
        DestroyImmediate(image);
        if (copy != null) { copy.Release(); DestroyImmediate(copy); }
    }

    /// <summary>The bottom-left pixel of a tracked texture.</summary>
    Color First(RenderTexture rt)
    {
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        pixel.Apply();
        RenderTexture.active = saved;
        return pixel.GetPixel(0, 0);
    }

    /// <summary>At a frame point: the tracked textures' first pixel (and whether they are created).</summary>
    void Point(string name)
    {
        point = name;
        if (tracked.Count == 0) return;
        var s = new StringBuilder("{");
        bool first = true;
        foreach (var pair in tracked)
        {
            if (pair.Value == null) continue;
            s.Append(first ? "" : ",").Append(Q(pair.Key)).Append(":");
            s.Append(pair.Value.IsCreated() ? V(First(pair.Value)) : "null");
            var other = pair.Value.doubleBuffered ? pair.Value.GetDoubleBufferRenderTexture() : null;
            if (other != null) s.Append(",").Append(Q(pair.Key + ".other")).Append(":").Append(other.IsCreated() ? V(First(other)) : "null");
            first = false;
        }
        Value("t." + section + "." + sectionFrame + "." + name, s.Append("}").ToString());
    }

    void Begin(string name)
    {
        section = name;
        sectionFrame = 0;
        logs.Clear();
        events.Clear();
        tracked.Clear();
    }

    void End()
    {
        foreach (var o in owned)
            if (o != null)
            {
                if (o is RenderTexture rt) rt.Release();
                DestroyImmediate(o);
            }
        owned.Clear();
        tracked.Clear();
        Value("ev." + section, L(events));
        Value("log." + section, L(logs));
        events.Clear();
        logs.Clear();
        section = "";
    }

    T Own<T>(T o) where T : UnityEngine.Object { owned.Add(o); return o; }

    Material Mat(Shader shader, int mode = -1)
    {
        var m = Own(new Material(shader));
        if (mode >= 0) m.SetFloat("_Mode", mode);
        return m;
    }

    CustomRenderTexture Crt(string name, int w, int h, RenderTextureFormat format = RenderTextureFormat.ARGBFloat)
    {
        var crt = Own(new CustomRenderTexture(w, h, format, RenderTextureReadWrite.Linear) { name = "crt." + name, filterMode = FilterMode.Point });
        return crt;
    }

    void Describe(string key, CustomRenderTexture t)
    {
        if (t == null) { Value(key, "null"); return; }
        Value(key, "{\"initializationSource\":" + Q(t.initializationSource.ToString())
            + ",\"initializationColor\":" + C(t.initializationColor)
            + ",\"initializationTexture\":" + B(t.initializationTexture != null)
            + ",\"initializationMaterial\":" + B(t.initializationMaterial != null)
            + ",\"initializationMode\":" + Q(t.initializationMode.ToString())
            + ",\"updateMode\":" + Q(t.updateMode.ToString())
            + ",\"updatePeriod\":" + F(t.updatePeriod)
            + ",\"material\":" + B(t.material != null)
            + ",\"shaderPass\":" + t.shaderPass
            + ",\"cubemapFaceMask\":" + t.cubemapFaceMask
            + ",\"doubleBuffered\":" + B(t.doubleBuffered)
            + ",\"wrapUpdateZones\":" + B(t.wrapUpdateZones)
            + ",\"updateZoneSpace\":" + Q(t.updateZoneSpace.ToString())
            + ",\"format\":" + Q(t.format.ToString())
            + ",\"graphicsFormat\":" + Q(t.graphicsFormat.ToString())
            + ",\"depth\":" + t.depth
            + ",\"dimension\":" + Q(t.dimension.ToString())
            + ",\"volumeDepth\":" + t.volumeDepth
            + ",\"size\":[" + t.width + "," + t.height + "]"
            + ",\"sRGB\":" + B(t.sRGB)
            + ",\"useMipMap\":" + B(t.useMipMap)
            + ",\"filterMode\":" + Q(t.filterMode.ToString())
            + ",\"wrapMode\":" + Q(t.wrapMode.ToString())
            + ",\"created\":" + B(t.IsCreated())
            + ",\"hideFlags\":" + Q(t.hideFlags.ToString())
            + ",\"name\":" + Q(t.name)
            + ",\"doubleBuffer\":" + DoubleBuffer(t)
            + "}");
    }

    static string DoubleBuffer(CustomRenderTexture t)
    {
        var d = t.GetDoubleBufferRenderTexture();
        if (d == null) return "null";
        return "{\"size\":[" + d.width + "," + d.height + "],\"graphicsFormat\":" + Q(d.graphicsFormat.ToString())
            + ",\"dimension\":" + Q(d.dimension.ToString()) + ",\"created\":" + B(d.IsCreated())
            + ",\"filterMode\":" + Q(d.filterMode.ToString()) + ",\"type\":" + Q(d.GetType().Name) + "}";
    }

    static string Zones(CustomRenderTexture t)
    {
        var list = new List<CustomRenderTextureUpdateZone>();
        t.GetUpdateZones(list);
        var s = new StringBuilder("[");
        for (int i = 0; i < list.Count; i++)
        {
            var z = list[i];
            s.Append(i > 0 ? "," : "").Append("{\"center\":").Append(V3(z.updateZoneCenter)).Append(",\"size\":").Append(V3(z.updateZoneSize))
                .Append(",\"rotation\":").Append(F(z.rotation)).Append(",\"pass\":").Append(z.passIndex).Append(",\"swap\":").Append(B(z.needSwap)).Append("}");
        }
        return s.Append("]").ToString();
    }

    static CustomRenderTextureUpdateZone Zone(Vector3 center, Vector3 size, float rotation = 0, int pass = 0, bool swap = false) =>
        new CustomRenderTextureUpdateZone { updateZoneCenter = center, updateZoneSize = size, rotation = rotation, passIndex = pass, needSwap = swap };

    // ------------------------------------------------------------------ frame points

    struct BeforeCrt { }
    struct AfterCrt { }
    struct EndOfFrame { }

    static PlayerLoopSystem Hook<T>(Action action) => new PlayerLoopSystem { type = typeof(T), updateDelegate = () => action() };

    void InstallHooks()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(PostLateUpdate)) continue;
            var subs = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
            int at = subs.FindIndex(s => s.type == typeof(PostLateUpdate.UpdateCustomRenderTextures));
            subs.Insert(at + 1, Hook<AfterCrt>(() => s_Probe?.Point("a")));
            subs.Insert(at, Hook<BeforeCrt>(() => s_Probe?.Point("b")));
            subs.Add(Hook<EndOfFrame>(() => { if (s_Probe != null) { s_Probe.Point("e"); s_Probe.sectionFrame++; } }));
            loop.subSystemList[i].subSystemList = subs.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    // ------------------------------------------------------------------ the run

    void Start()
    {
        s_Probe = this;
        directory = Environment.GetEnvironmentVariable("CUSTOMRT_PROBE_OUT");
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += OnLog;
        pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        coords = Shader.Find("CustomRT/Coords");
        self = Shader.Find("CustomRT/Self");
        selfCube = Shader.Find("CustomRT/SelfCube");
        self3D = Shader.Find("CustomRT/Self3D");
        depend = Shader.Find("CustomRT/Depend");
        init = Shader.Find("CustomRT/Init");
        initTwo = Shader.Find("CustomRT/InitTwo");
        leak = Shader.Find("CustomRT/Leak");
        CustomRenderTextureManager.textureLoaded += t => Event("loaded", t, 0);
        CustomRenderTextureManager.textureUnloaded += t => Event("unloaded", t, 0);
        CustomRenderTextureManager.updateTriggered += (t, n) => Event("update", t, n);
        CustomRenderTextureManager.initializeTriggered += t => Event("init", t, 0);
        Time.captureDeltaTime = 0.05f;
        InstallHooks();
        steps = Run();
    }

    void Update()
    {
        point = "script";
        if (steps == null) return;
        bool more;
        try { more = steps.MoveNext(); }
        catch (Exception e)
        {
            Value("fatal", Q(section + ": " + e));
            more = false;
        }
        Point("u");
        if (!more) Finish();
    }

    void Finish()
    {
        steps = null;
        Value("complete", "true");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
        Debug.Log("[customrt] complete");
        if (finished != null) finished(0);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit(0);
#endif
        }
    }

    IEnumerator Run()
    {
        foreach (var s in Api()) yield return s;
        foreach (var s in Init()) yield return s;
        foreach (var s in Modes()) yield return s;
        foreach (var s in Calls()) yield return s;
        foreach (var s in Period()) yield return s;
        foreach (var s in Zones()) yield return s;
        foreach (var s in DoubleBuffering()) yield return s;
        foreach (var s in Dependency()) yield return s;
        foreach (var s in Cubes()) yield return s;
        foreach (var s in Volumes()) yield return s;
        foreach (var s in Variables()) yield return s;
        foreach (var s in Assets()) yield return s;
    }

    /// <summary>Constructors, defaults, zones and the manager's list.</summary>
    IEnumerable Api()
    {
        Begin("api");
        Describe("api.ctor.wh", Own(new CustomRenderTexture(8, 4) { name = "crt.wh" }));
        Describe("api.ctor.format", Own(new CustomRenderTexture(8, 4, RenderTextureFormat.ARGBHalf) { name = "crt.format" }));
        Describe("api.ctor.readWrite", Own(new CustomRenderTexture(8, 4, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "crt.readWrite" }));
        Describe("api.ctor.default", Own(new CustomRenderTexture(8, 4, UnityEngine.Experimental.Rendering.DefaultFormat.HDR) { name = "crt.defaultFormat" }));
        Describe("api.ctor.graphics", Own(new CustomRenderTexture(8, 4, UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16_SFloat) { name = "crt.graphics" }));
        var z = Crt("zones", 8, 8);
        Value("api.zones.initial", Zones(z));
        z.SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 0.25f, 1), 15, 1, true), Zone(new Vector3(1, 2, 3), new Vector3(4, 5, 6)) });
        Value("api.zones.set", Zones(z));
        z.ClearUpdateZones();
        Value("api.zones.cleared", Zones(z));
        try { z.SetUpdateZones(null); Value("api.zones.null", Zones(z)); }
        catch (Exception e) { Value("api.zones.null", Q(e.GetType().Name)); }
        var all = new List<CustomRenderTexture>();
        CustomRenderTextureManager.GetAllCustomRenderTextures(all);
        var names = new List<string>();
        foreach (var t in all) if (t.name.StartsWith("crt.")) names.Add(t.name);
        names.Sort(StringComparer.Ordinal);
        Value("api.all.uncreated", L(names));
        z.Create();
        Describe("api.created", z);
        all.Clear();
        CustomRenderTextureManager.GetAllCustomRenderTextures(all);
        names.Clear();
        foreach (var t in all) if (t.name.StartsWith("crt.")) names.Add(t.name);
        names.Sort(StringComparer.Ordinal);
        Value("api.all.created", L(names));
        yield return null;
        Describe("api.created.next", z);
        all.Clear();
        CustomRenderTextureManager.GetAllCustomRenderTextures(all);
        names.Clear();
        foreach (var t in all) if (t.name.StartsWith("crt.")) names.Add(t.name);
        names.Sort(StringComparer.Ordinal);
        Value("api.all.next", L(names));
        End();
        yield return null;
    }

    /// <summary>Initialization sources, read after the next update point.</summary>
    IEnumerable Init()
    {
        Begin("init");
        var tex = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        tex.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 128), new Color32(0, 0, 255, 64), new Color32(64, 128, 192, 255) });
        tex.Apply();
        var color = new Color(0.25f, 0.5f, 0.75f, 0.5f);
        var cases = new List<(string, CustomRenderTexture)>();
        CustomRenderTexture Make(string n, RenderTextureFormat f = RenderTextureFormat.ARGBFloat)
        {
            var t = Crt("init." + n, 4, 4, f);
            t.initializationMode = CustomRenderTextureUpdateMode.OnDemand;
            t.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            cases.Add((n, t));
            return t;
        }
        var c = Make("color"); c.initializationColor = color;
        c = Make("color.srgb", RenderTextureFormat.ARGB32); c.initializationColor = color;
        c = Make("texcolor"); c.initializationColor = color; c.initializationTexture = tex;
        c = Make("texture"); c.initializationTexture = tex;
        c = Make("default");
        c = Make("material"); c.initializationSource = CustomRenderTextureInitializationSource.Material; c.initializationMaterial = Mat(init, 0); c.initializationColor = color;
        c = Make("material.two"); c.initializationSource = CustomRenderTextureInitializationSource.Material; c.initializationMaterial = Mat(initTwo);
        c = Make("material.null"); c.initializationSource = CustomRenderTextureInitializationSource.Material; c.initializationColor = color; c.initializationTexture = tex;
        c = Make("material.ignored"); c.initializationMaterial = Mat(initTwo); c.initializationColor = color;
        c = Make("update"); c.initializationColor = color; c.material = Mat(self); c.doubleBuffered = true;
        foreach (var (n, t) in cases) tracked[n] = t;
        foreach (var (n, t) in cases) t.Initialize();
        c.Update();
        Value("init.created.afterCall", B(cases[0].Item2.IsCreated()));
        yield return null;
        foreach (var (n, t) in cases)
        {
            Describe("init." + n + ".state", t);
            Read("init." + n, t);
        }
        End();
        yield return null;
    }

    /// <summary>Update and initialization modes over frames (a counter adding 1 per update).</summary>
    IEnumerable Modes()
    {
        Begin("modes");
        CustomRenderTexture Counter(string n, CustomRenderTextureUpdateMode initMode, CustomRenderTextureUpdateMode updateMode, bool withMaterial = true)
        {
            var t = Crt("modes." + n, 2, 2);
            t.initializationColor = new Color(0.5f, 0, 0, 0);
            t.initializationMode = initMode;
            t.updateMode = updateMode;
            if (withMaterial) t.material = Mat(self);
            t.doubleBuffered = true;
            tracked[n] = t;
            return t;
        }
        Counter("onload.realtime", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.Realtime);
        Counter("onload.onload", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.OnLoad);
        Counter("onload.ondemand", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.OnDemand);
        Counter("realtime.realtime", CustomRenderTextureUpdateMode.Realtime, CustomRenderTextureUpdateMode.Realtime);
        Counter("realtime.onload", CustomRenderTextureUpdateMode.Realtime, CustomRenderTextureUpdateMode.OnLoad);
        Counter("ondemand.realtime", CustomRenderTextureUpdateMode.OnDemand, CustomRenderTextureUpdateMode.Realtime);
        Counter("ondemand.ondemand", CustomRenderTextureUpdateMode.OnDemand, CustomRenderTextureUpdateMode.OnDemand);
        Counter("nomaterial.realtime", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.Realtime, false);
        Counter("onload.ondemand.call", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.OnDemand).Update();
        Counter("ondemand.ondemand.call", CustomRenderTextureUpdateMode.OnDemand, CustomRenderTextureUpdateMode.OnDemand).Update();
        Counter("onload.realtime.call", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.Realtime).Update();
        var single = Counter("onload.realtime.single", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.Realtime);
        single.doubleBuffered = false;
        var created = Counter("created.realtime", CustomRenderTextureUpdateMode.OnLoad, CustomRenderTextureUpdateMode.Realtime);
        created.Create();
        for (int i = 0; i < 4; i++)
        {
            var all = new List<CustomRenderTexture>();
            CustomRenderTextureManager.GetAllCustomRenderTextures(all);
            var names = new List<string>();
            foreach (var t in all) if (t != null && t.name.StartsWith("crt.")) names.Add(t.name);
            names.Sort(StringComparer.Ordinal);
            Value("modes.all." + i, L(names));
            yield return null;
        }
        End();
        yield return null;
    }

    /// <summary>Update(count) and Initialize() calls on running textures, from script Update.</summary>
    IEnumerable Calls()
    {
        Begin("calls");
        CustomRenderTexture Counter(string n, CustomRenderTextureUpdateMode updateMode)
        {
            var t = Crt("calls." + n, 2, 2);
            t.initializationColor = new Color(0.5f, 0, 0, 0);
            t.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
            t.updateMode = updateMode;
            t.material = Mat(self);
            t.doubleBuffered = true;
            tracked[n] = t;
            return t;
        }
        var demand = Counter("ondemand", CustomRenderTextureUpdateMode.OnDemand);
        var realtime = Counter("realtime", CustomRenderTextureUpdateMode.Realtime);
        var single = Counter("single", CustomRenderTextureUpdateMode.OnDemand);
        single.doubleBuffered = false;
        yield return null;
        point = "script.update1";
        demand.Update();
        realtime.Update();
        single.Update(2);
        yield return null;
        point = "script.update3";
        demand.Update(3);
        realtime.Update(3);
        demand.Update(2);
        yield return null;
        point = "script.initialize";
        demand.Initialize();
        realtime.Initialize();
        yield return null;
        point = "script.initializeUpdate";
        demand.Initialize();
        demand.Update();
        yield return null;
        point = "script.update0";
        demand.Update(0);
        yield return null;
        End();
        yield return null;
    }

    /// <summary>updatePeriod at a fixed 0.05 s delta time, then with a time scale of 0.</summary>
    IEnumerable Period()
    {
        Begin("period");
        foreach (var p in new[] { 0.0f, 0.04f, 0.05f, 0.12f, 1.0f })
        {
            var t = Crt("period." + p.ToString("R", System.Globalization.CultureInfo.InvariantCulture), 2, 2);
            t.initializationColor = Color.clear;
            t.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
            t.updateMode = CustomRenderTextureUpdateMode.Realtime;
            t.updatePeriod = p;
            t.material = Mat(self);
            t.doubleBuffered = true;
            tracked[t.name.Substring(4)] = t;
        }
        for (int i = 0; i < 12; i++) yield return null;
        Time.timeScale = 0;
        for (int i = 0; i < 3; i++) yield return null;
        Time.timeScale = 1;
        yield return null;
        End();
        yield return null;
    }

    /// <summary>Update zones: spaces, rotation, passes, wrapping, more than a batch, swaps.</summary>
    IEnumerable Zones()
    {
        Begin("zones");
        var cases = new List<(string, CustomRenderTexture)>();
        CustomRenderTexture Make(string n, int pass = 0, int w = 16, int h = 8)
        {
            var t = Crt("zones." + n, w, h);
            t.initializationColor = new Color(-1, -1, -1, -1);
            t.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
            t.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            t.material = Mat(coords);
            t.shaderPass = pass;
            cases.Add((n, t));
            return t;
        }
        Make("none");
        Make("none.info", 1);
        Make("none.params", 2);
        Make("normalized").SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.25f, 0), new Vector3(0.5f, 0.25f, 1)) });
        Make("rotated").SetUpdateZones(new[] { Zone(new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 1), 30) });
        var pixel = Make("pixel");
        pixel.updateZoneSpace = CustomRenderTextureUpdateZoneSpace.Pixel;
        pixel.SetUpdateZones(new[] { Zone(new Vector3(4, 6, 0), new Vector3(6, 2, 1)) });
        var pixelInfo = Make("pixel.params", 2);
        pixelInfo.updateZoneSpace = CustomRenderTextureUpdateZoneSpace.Pixel;
        pixelInfo.SetUpdateZones(new[] { Zone(new Vector3(8, 4, 0), new Vector3(16, 8, 1)) });
        Make("passes").SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 1, 1), 0, 1), Zone(new Vector3(0.75f, 0.5f, 0), new Vector3(0.5f, 1, 1), 0, 0) });
        Make("passes.shaderPass", 2).SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 1, 1), 0, 1), Zone(new Vector3(0.75f, 0.5f, 0), new Vector3(0.5f, 1, 1), 0, -1) });
        var wrap = Make("wrap");
        wrap.wrapUpdateZones = true;
        wrap.SetUpdateZones(new[] { Zone(new Vector3(0.95f, 0.9f, 0), new Vector3(0.25f, 0.5f, 1)) });
        Make("nowrap").SetUpdateZones(new[] { Zone(new Vector3(0.95f, 0.9f, 0), new Vector3(0.25f, 0.5f, 1)) });
        var many = new List<CustomRenderTextureUpdateZone>();
        for (int i = 0; i < 20; i++) many.Add(Zone(new Vector3((i % 5 + 0.5f) / 5f, (i / 5 + 0.5f) / 4f, 0), new Vector3(0.2f, 0.25f, 1), 0, 1));
        Make("many", 1, 20, 8).SetUpdateZones(many.ToArray());
        Make("overlap", 1).SetUpdateZones(new[] { Zone(new Vector3(0.4f, 0.5f, 0), new Vector3(0.5f, 0.5f, 1), 0, 1), Zone(new Vector3(0.6f, 0.5f, 0), new Vector3(0.5f, 0.5f, 1), 0, 1) });
        yield return null;
        foreach (var (n, t) in cases) t.Update();
        yield return null;
        foreach (var (n, t) in cases) { Read("zones." + n, t); Value("zones." + n + ".zones", Zones(t)); }
        End();
        yield return null;
    }

    /// <summary>Double buffering: the self texture, partial zones, needSwap, the second buffer.</summary>
    IEnumerable DoubleBuffering()
    {
        Begin("double");
        var cases = new List<(string, CustomRenderTexture)>();
        CustomRenderTexture Make(string n, bool db)
        {
            var t = Crt("double." + n, 8, 4);
            t.initializationColor = new Color(0.5f, 0, 0, 0);
            t.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
            t.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            t.material = Mat(self);
            t.doubleBuffered = db;
            cases.Add((n, t));
            return t;
        }
        var whole = Make("whole", true);
        var partial = Make("partial", true);
        partial.SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 1, 1)) });
        var swaps = Make("swaps", true);
        swaps.SetUpdateZones(new[] {
            Zone(new Vector3(0.5f, 0.5f, 0), new Vector3(1, 1, 1)),
            Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 1, 1), 0, 0, true),
            Zone(new Vector3(0.75f, 0.5f, 0), new Vector3(0.5f, 1, 1), 0, 0, false),
            Zone(new Vector3(0.5f, 0.5f, 0), new Vector3(1, 1, 1), 0, 0, true) });
        var noswap = Make("noswap", true);
        noswap.SetUpdateZones(new[] { Zone(new Vector3(0.5f, 0.5f, 0), new Vector3(1, 1, 1)), Zone(new Vector3(0.5f, 0.5f, 0), new Vector3(1, 1, 1)) });
        var single = Make("single", false);
        single.SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 1, 1)) });
        Describe("double.whole.before", whole);
        yield return null;
        Describe("double.whole.loaded", whole);
        foreach (var (n, t) in cases) Read("double." + n + ".0", t);
        foreach (var (n, t) in cases) t.Update();
        yield return null;
        Describe("double.whole.updated", whole);
        foreach (var (n, t) in cases)
        {
            Read("double." + n + ".1", t);
            if (t.doubleBuffered) Read("double." + n + ".1.other", t.GetDoubleBufferRenderTexture());
            t.Update(2);
        }
        yield return null;
        foreach (var (n, t) in cases)
        {
            Read("double." + n + ".3", t);
            if (t.doubleBuffered) Read("double." + n + ".3.other", t.GetDoubleBufferRenderTexture());
        }
        whole.doubleBuffered = false;
        Describe("double.whole.off", whole);
        End();
        yield return null;
    }

    /// <summary>A texture whose material reads another one made after it (and one made before it).</summary>
    IEnumerable Dependency()
    {
        Begin("depend");
        var earlySource = Crt("depend.early.source", 2, 2);
        earlySource.updateMode = CustomRenderTextureUpdateMode.Realtime;
        earlySource.initializationColor = Color.clear;
        earlySource.material = Mat(self);
        earlySource.doubleBuffered = true;
        var lateReader = Crt("depend.late.reader", 2, 2);
        lateReader.updateMode = CustomRenderTextureUpdateMode.Realtime;
        lateReader.initializationColor = Color.clear;
        var lateMaterial = Mat(depend);
        lateMaterial.SetTexture("_Other", earlySource);
        lateReader.material = lateMaterial;
        tracked["early.source"] = earlySource;
        tracked["late.reader"] = lateReader;
        var reader = Crt("depend.reader", 2, 2);
        reader.updateMode = CustomRenderTextureUpdateMode.Realtime;
        reader.initializationColor = Color.clear;
        var source = Crt("depend.source", 2, 2);
        source.updateMode = CustomRenderTextureUpdateMode.Realtime;
        source.initializationColor = Color.clear;
        source.material = Mat(self);
        source.doubleBuffered = true;
        var m = Mat(depend);
        m.SetTexture("_Other", source);
        reader.material = m;
        tracked["reader"] = reader;
        tracked["source"] = source;
        for (int i = 0; i < 3; i++) yield return null;
        End();
        yield return null;
    }

    /// <summary>Cubemaps: face masks, the direction and face variables, the self cube.</summary>
    IEnumerable Cubes()
    {
        Begin("cube");
        CustomRenderTexture Make(string n, Shader shader, int pass)
        {
            var t = Crt("cube." + n, 4, 4);
            t.dimension = TextureDimension.Cube;
            t.initializationColor = new Color(-1, -1, -1, -1);
            t.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
            t.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            t.material = Mat(shader);
            t.shaderPass = pass;
            return t;
        }
        var all = Make("all", coords, 3);
        var masked = Make("masked", coords, 3);
        masked.cubemapFaceMask = 0b100101;
        var info = Make("info", coords, 1);
        var initDir = Make("init", coords, 3);
        initDir.initializationSource = CustomRenderTextureInitializationSource.Material;
        initDir.initializationMaterial = Mat(init, 1);
        var initMasked = Make("init.masked", coords, 3);
        initMasked.initializationSource = CustomRenderTextureInitializationSource.Material;
        initMasked.initializationMaterial = Mat(init, 1);
        initMasked.cubemapFaceMask = 0b000110;
        var selfCubeTex = Make("self", selfCube, 0);
        selfCubeTex.doubleBuffered = true;
        selfCubeTex.initializationColor = new Color(0.5f, 0, 0, 0);
        var cubeParams = Make("params", coords, 2);
        var cubeInit = Make("init.info", coords, 3);
        cubeInit.initializationSource = CustomRenderTextureInitializationSource.Material;
        cubeInit.initializationMaterial = Mat(init, 0);
        var cases = new[] { ("all", all), ("masked", masked), ("info", info), ("params", cubeParams), ("self", selfCubeTex) };
        yield return null;
        foreach (var (_, t) in cases) t.Update();
        yield return null;
        selfCubeTex.Update(2);
        yield return null;
        foreach (var (n, t) in cases)
            for (int face = 0; face < 6; face++) Read("cube." + n + "." + face, t, face);
        for (int face = 0; face < 6; face++) Read("cube.init." + face, initDir, face);
        for (int face = 0; face < 6; face++) Read("cube.init.masked." + face, initMasked, face);
        for (int face = 0; face < 6; face++) Read("cube.init.info." + face, cubeInit, face);
        Describe("cube.self.state", selfCubeTex);
        End();
        yield return null;
    }

    /// <summary>Volumes: slices, the parameters and the self volume.</summary>
    IEnumerable Volumes()
    {
        Begin("volume");
        CustomRenderTexture Make(string n, Shader shader, int pass)
        {
            var t = Crt("volume." + n, 4, 2);
            t.dimension = TextureDimension.Tex3D;
            t.volumeDepth = 4;
            t.depth = 0;
            t.initializationColor = new Color(-1, -1, -1, -1);
            t.initializationMode = CustomRenderTextureUpdateMode.OnLoad;
            t.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            t.material = Mat(shader);
            t.shaderPass = pass;
            return t;
        }
        var cases = new List<(string, CustomRenderTexture)> { ("coords", Make("coords", coords, 0)), ("params", Make("params", coords, 2)), ("info", Make("info", coords, 1)) };
        var zoned = Make("zoned", coords, 2);
        zoned.SetUpdateZones(new[] { Zone(new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1, 1, 0.5f)) });
        cases.Add(("zoned", zoned));
        var selfVolume = Make("self", self3D, 0);
        selfVolume.doubleBuffered = true;
        selfVolume.initializationColor = new Color(0.5f, 0, 0, 0);
        cases.Add(("self", selfVolume));
        yield return null;
        foreach (var (n, t) in cases)
            for (int slice = 0; slice < 4; slice++) Read("volume." + n + ".init." + slice, t, slice);
        foreach (var (_, t) in cases) t.Update();
        yield return null;
        foreach (var (n, t) in cases)
            for (int slice = 0; slice < 4; slice++) Read("volume." + n + "." + slice, t, slice);
        End();
        yield return null;
    }

    /// <summary>The custom render texture variables a blit sees after an update.</summary>
    IEnumerable Variables()
    {
        Begin("variables");
        var t = Crt("variables", 4, 2);
        t.updateMode = CustomRenderTextureUpdateMode.OnDemand;
        t.material = Mat(coords);
        t.SetUpdateZones(new[] { Zone(new Vector3(0.25f, 0.5f, 0), new Vector3(0.5f, 1, 1), 10) });
        var output = Own(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        var m = Mat(leak);
        Graphics.Blit(null, output, m, 0);
        Read("variables.before.info", output);
        Graphics.Blit(null, output, m, 1);
        Read("variables.before.params", output);
        t.Update();
        yield return null;
        Graphics.Blit(null, output, m, 0);
        Read("variables.after.info", output);
        Graphics.Blit(null, output, m, 1);
        Read("variables.after.params", output);
        End();
        yield return null;
    }

    /// <summary>`.asset` textures loaded from Resources.</summary>
    IEnumerable Assets()
    {
        Begin("assets");
        point = "script.load";
        var counter = Resources.Load<CustomRenderTexture>("crt.asset.counter");
        var zoned = Resources.Load<CustomRenderTexture>("crt.asset.zoned");
        Describe("assets.counter.loaded", counter);
        Describe("assets.zoned.loaded", zoned);
        Value("assets.zoned.zones", counter == null || zoned == null ? "null" : Zones(zoned));
        if (counter != null) tracked["counter"] = counter;
        if (zoned != null) tracked["zoned"] = zoned;
        yield return null;
        yield return null;
        if (counter != null) Read("assets.counter", counter);
        if (zoned != null) Read("assets.zoned", zoned);
        Describe("assets.counter.after", counter);
        point = "script.unload";
        tracked.Clear();
        Resources.UnloadAsset(counter);
        Resources.UnloadAsset(zoned);
        yield return null;
        End();
        yield return null;
    }
}
