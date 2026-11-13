using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The GPU Resident Drawer probe: in the shader graph probe's URP scene (Forward+, the drawer set
/// to Instanced Drawing, the BatchRendererGroup variants kept) it builds ordinary MeshRenderers at
/// run time: a ground, a row of cubes with URP Lit, Unlit and Simple Lit materials, a renderer with
/// two sub-meshes and two materials, a moving and a turning cube, LOD groups at several distances
/// (one moving away, one cross-fading, one with animated cross-fading, which the drawer leaves
/// out), a transparent sphere, and renderers the drawer leaves to the ordinary path (a material
/// property block, a shader without DOTS instancing, an OnWillRenderObject script, the package's
/// DisallowGPUDrivenRendering), a wall and a cube behind it. Between frames it changes a material's
/// colour, a renderer's material, disables, destroys, deactivates and reactivates renderers, edits
/// a mesh, sets and clears a property block, adds a renderer, edits a filter's own mesh instance,
/// groups renderers drawn since the start into a new LOD group and moves a renderer to a layer the
/// camera does not draw. The main camera renders into a
/// 320x180 texture; the image of each capture frame goes to <c>$RESIDENT_DRAWER_OUT/f&lt;frame&gt;.png</c>
/// and the drawer's state, the renderers it holds and the renderers' isVisible to
/// <c>$RESIDENT_DRAWER_OUT/values.jsonl</c>, then the Editor quits.
/// The drawer's culling statistics are not read: turning them on turns URP's debug display on.
/// </summary>
public class ResidentDrawerProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;
    /// <summary>The frames whose image is recorded (read back in the following frame's Update).</summary>
    static readonly int[] Captures = { 8, 14, 20, 26, 32, 38, 44, 50, 56 };
    const int LastFrame = 58;
    /// <summary>A layer the main camera does not draw.</summary>
    const int HiddenLayer = 30;
    /// <summary>The seconds without a compiling shader the warm-up waits for (the Editor only).</summary>
    const float QuietSeconds = 3f;

    static readonly List<string> s_Rows = new List<string>();
    static string s_Out;
    static readonly Type s_Drawer = Type.GetType("UnityEngine.Rendering.GPUResidentDrawer, Unity.RenderPipelines.GPUDriven.Runtime");

    readonly Dictionary<string, Renderer> m_Renderers = new Dictionary<string, Renderer>();
    Camera m_Camera;
    Light m_Light;
    RenderTexture m_Target;
    Texture2D m_Readback;
    Material m_Red, m_Green, m_Blue, m_Yellow, m_Grey, m_Glass, m_NoDots;
    Transform m_Mover, m_Spinner, m_LodMover, m_Ungrouped;
    Mesh m_TwoMat;
    int m_Frame;
    /// <summary>Frames of the warm-up so far without a shader compiling (the Editor compiles
    /// variants asynchronously: a group's draws, made from jobs, cannot compile one at once).</summary>
    int m_Quiet;
    bool m_Started;
    float m_QuietSince = -1;
    int m_WarmupFrames;
    /// <summary>Diagnostics of the run (not compared): warm-up length, shader state.</summary>
    readonly StringBuilder m_Debug = new StringBuilder();
    readonly Dictionary<int, string> m_Names = new Dictionary<int, string>();
    readonly HashSet<string> m_Failed = new HashSet<string>();
    static readonly Type s_DispatcherType = Type.GetType("UnityEngine.ObjectDispatcher, UnityEngine.CoreModule");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ResidentDrawerProbe").AddComponent<ResidentDrawerProbe>();

    void Start()
    {
        s_Out = Environment.GetEnvironmentVariable("RESIDENT_DRAWER_OUT");
        if (!string.IsNullOrEmpty(s_Out)) Directory.CreateDirectory(s_Out);
        Time.captureFramerate = 30;
        Application.targetFrameRate = 30;
        var graph = GameObject.Find("Graph Sphere");
        if (graph != null) graph.SetActive(false);
        m_Camera = Camera.main;
        m_Target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        m_Camera.targetTexture = m_Target;
        m_Camera.cullingMask &= ~(1 << HiddenLayer);
        m_Readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        m_Light = FindAnyObjectByType<Light>();
        m_Light.shadows = LightShadows.Hard;
        Row("device", SystemInfo.graphicsDeviceType.ToString());
#if UNITY_EDITOR
        Row("brgVariants", UnityEditor.Rendering.EditorGraphicsSettings.batchRendererGroupShaderStrippingMode.ToString());
#endif
        Row("drawer.enabled.start", DrawerEnabled());
        // The scene's own renderers (its Lit sphere) by their objects' names.
        foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            m_Names[renderer.GetInstanceID()] = renderer.gameObject.name;
        try
        {
            CreateDispatcher();
            Build();
        }
        catch (Exception e)
        {
            Row("exception.build", e.GetType().Name + ": " + e.Message);
        }
    }

    static bool DrawerEnabled() =>
        s_Drawer != null && (bool)s_Drawer.GetMethod("IsEnabled", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

    Material Lit(string shader, Color color, string name)
    {
        var material = new Material(Shader.Find(shader)) { name = name };
        material.SetColor("_BaseColor", color);
        m_Names[material.GetInstanceID()] = name;
        return material;
    }

    void Build()
    {
        m_Red = Lit("Universal Render Pipeline/Lit", new Color(0.9f, 0.15f, 0.1f), "Red");
        m_Green = Lit("Universal Render Pipeline/Lit", new Color(0.2f, 0.8f, 0.25f), "Green");
        m_Blue = Lit("Universal Render Pipeline/Unlit", new Color(0.2f, 0.35f, 0.95f), "Blue");
        m_Yellow = Lit("Universal Render Pipeline/Simple Lit", new Color(0.95f, 0.85f, 0.2f), "Yellow");
        m_Grey = Lit("Universal Render Pipeline/Lit", new Color(0.55f, 0.55f, 0.6f), "Grey");
        m_Glass = Lit("Universal Render Pipeline/Lit", new Color(0.3f, 0.9f, 0.95f, 0.5f), "Glass");
        // URP Lit as transparent (alpha blended), as its material inspector sets it up.
        m_Glass.SetFloat("_Surface", 1);
        m_Glass.SetFloat("_Blend", 0);
        m_Glass.SetOverrideTag("RenderType", "Transparent");
        m_Glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m_Glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m_Glass.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m_Glass.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m_Glass.SetFloat("_ZWrite", 0);
        m_Glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m_Glass.renderQueue = (int)RenderQueue.Transparent;
        m_Glass.SetShaderPassEnabled("DepthOnly", false);
        m_Glass.SetShaderPassEnabled("ShadowCaster", false);
        m_NoDots = new Material(Shader.Find("Probe/NoDots")) { name = "NoDots" };
        m_Names[m_NoDots.GetInstanceID()] = "NoDots";
        m_NoDots.SetColor("_BaseColor", new Color(0.9f, 0.4f, 0.9f));

        Make("Ground", PrimitiveType.Plane, new Vector3(0, 0, 6), m_Grey).transform.localScale = new Vector3(2.5f, 1, 2.5f);
        var row = new[] { m_Red, m_Green, m_Red, m_Blue, m_Yellow, m_Green };
        for (int i = 0; i < row.Length; i++)
            Make("Cube" + i, PrimitiveType.Cube, new Vector3(-3.75f + 1.5f * i, 0.5f, 2), row[i]);

        // One renderer, one mesh of two sub-meshes, two materials.
        var cube = Make("TwoMat", PrimitiveType.Cube, new Vector3(-1.5f, 1.9f, 3), m_Red);
        var source = cube.GetComponent<MeshFilter>().sharedMesh;
        m_TwoMat = new Mesh { name = "TwoMat" };
        m_Names[m_TwoMat.GetInstanceID()] = "TwoMat";
        m_TwoMat.vertices = source.vertices;
        m_TwoMat.normals = source.normals;
        m_TwoMat.uv = source.uv;
        var triangles = source.triangles;
        m_TwoMat.subMeshCount = 2;
        var first = new int[18];
        var second = new int[triangles.Length - 18];
        Array.Copy(triangles, first, 18);
        Array.Copy(triangles, 18, second, 0, second.Length);
        m_TwoMat.SetTriangles(first, 0);
        m_TwoMat.SetTriangles(second, 1);
        cube.GetComponent<MeshFilter>().sharedMesh = m_TwoMat;
        cube.GetComponent<MeshRenderer>().sharedMaterials = new[] { m_Red, m_Blue };

        m_Mover = Make("Mover", PrimitiveType.Cube, new Vector3(-4, 3.2f, 4), m_Green).transform;
        m_Spinner = Make("Spinner", PrimitiveType.Cube, new Vector3(-3.5f, 2.2f, 3.5f), m_Yellow).transform;

        var steps = new[] { 0.5f, 0.2f, 0.05f };
        MakeLod("LodNear", new Vector3(-2, 1, -3.5f), steps, LODFadeMode.None, false);
        MakeLod("LodMid", new Vector3(2.5f, 1, 0), steps, LODFadeMode.None, false);
        MakeLod("LodFar", new Vector3(-3, 1, 10), steps, LODFadeMode.None, false);
        m_LodMover = MakeLod("LodMover", new Vector3(0.5f, 1, -4), steps, LODFadeMode.None, false).transform;
        var far = new[] { 0.15f, 0.05f, 0.01f };
        MakeLod("LodFade", new Vector3(-1, 2.6f, -0.2f), far, LODFadeMode.CrossFade, false);
        MakeLod("LodAnimated", new Vector3(3.5f, 2.6f, 2), far, LODFadeMode.CrossFade, true);

        // Levels without a group until frame 46: all three draw.
        m_Ungrouped = new GameObject("Ungrouped").transform;
        m_Ungrouped.position = new Vector3(1.9f, 0.7f, -3.2f);
        m_Ungrouped.localScale = Vector3.one * 2;
        var types = new[] { PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule };
        var materials = new[] { m_Red, m_Green, m_Blue };
        for (int i = 0; i < types.Length; i++)
        {
            var level = Make("Ungrouped.lod" + i, types[i], Vector3.zero, materials[i]).transform;
            level.SetParent(m_Ungrouped, false);
            level.localPosition = new Vector3(0.25f * (i - 1), 0, 0);
            level.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        }

        Make("Glass", PrimitiveType.Sphere, new Vector3(2.2f, 0.8f, 0.4f), m_Glass);

        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(0.95f, 0.2f, 0.85f));
        Make("Mpb", PrimitiveType.Cube, new Vector3(-4.5f, 0.5f, 4), m_Green).GetComponent<MeshRenderer>().SetPropertyBlock(block);
        Make("NoDots", PrimitiveType.Cube, new Vector3(-3, 0.5f, 5), m_NoDots);
        Make("Callback", PrimitiveType.Cube, new Vector3(3, 0.5f, 5), m_Yellow).AddComponent<WillRender>();
        var disallowed = Make("Disallowed", PrimitiveType.Cube, new Vector3(4.5f, 0.5f, 4), m_Blue);
        var disallow = Type.GetType("UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime");
        Row("disallowType", disallow != null);
        if (disallow != null) disallowed.AddComponent(disallow);

        var wall = Make("Wall", PrimitiveType.Cube, new Vector3(2.5f, 1.5f, 7), m_Grey);
        wall.transform.localScale = new Vector3(4, 3, 0.3f);
        Make("Hidden", PrimitiveType.Cube, new Vector3(2.5f, 1, 10), m_Red);
    }

    GameObject Make(string name, PrimitiveType type, Vector3 position, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = position;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        m_Renderers[name] = renderer;
        m_Names[renderer.GetInstanceID()] = name;
        return go;
    }

    GameObject MakeLod(string name, Vector3 position, float[] heights, LODFadeMode fade, bool animate)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        root.transform.localScale = Vector3.one * 2;
        var types = new[] { PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule };
        var materials = new[] { m_Red, m_Green, m_Blue };
        var lods = new LOD[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            var level = Make(name + ".lod" + i, types[i], position, materials[i]);
            level.transform.SetParent(root.transform, false);
            level.transform.localPosition = Vector3.zero;
            level.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            lods[i] = new LOD(heights[i], new Renderer[] { level.GetComponent<MeshRenderer>() }) { fadeTransitionWidth = 0.5f };
        }
        var group = root.AddComponent<LODGroup>();
        m_Names[group.GetInstanceID()] = name;
        group.fadeMode = fade;
        group.animateCrossFading = animate;
        group.SetLODs(lods);
        group.RecalculateBounds();
        return root;
    }

    void Update()
    {
        if (!m_Started)
        {
            // The scripted frames start once no shader has compiled for ten frames and three
            // seconds (the Editor starts compiling a variant some frames after it is first drawn).
            m_WarmupFrames++;
            if (Compiling())
            {
                m_Quiet = 0;
                m_QuietSince = -1;
                return;
            }
            if (m_QuietSince < 0) m_QuietSince = Time.realtimeSinceStartup;
            if (++m_Quiet < 10 || Time.realtimeSinceStartup - m_QuietSince < QuietSeconds) return;
            m_Started = true;
            Application.targetFrameRate = -1;
            m_Debug.Append("warmup frames ").Append(m_WarmupFrames).Append('\n');
            DebugShaders();
        }
        m_Frame++;
        if (Array.IndexOf(Captures, m_Frame - 1) >= 0) Guarded("capture", () => Capture(m_Frame - 1));
        Guarded("step", () => Step(m_Frame));
        if (m_Frame == LastFrame) Finish();
    }

    /// <summary>Runs a part of the frame; an exception becomes a row, and the frame's other parts
    /// still run.</summary>
    void Guarded(string part, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            // Once per part and frame (warm-up frames all count as frame 0).
            var inner = e.InnerException ?? e;
            if (m_Failed.Add(part + m_Frame)) Row("exception." + part + ".f" + m_Frame, inner.GetType().Name + ": " + inner.Message);
        }
    }

    /// <summary>The frame's motion and its scripted change, before the drawer dispatches the
    /// frame's changes after LateUpdate.</summary>
    void Step(int frame)
    {
        m_Mover.position = new Vector3(-4 + 0.18f * frame, 3.2f, 4);
        m_Spinner.rotation = Quaternion.Euler(0, 6f * frame, 0);
        m_LodMover.position = new Vector3(0.5f, 1, -4 + 0.25f * frame);
        switch (frame)
        {
            case 12:
                m_Red.SetColor("_BaseColor", new Color(1, 0.55f, 0.1f));
                break;
            case 18:
                m_Renderers["Cube1"].sharedMaterial = m_Blue;
                m_Renderers["Cube5"].enabled = false;
                break;
            case 24:
                Destroy(m_Renderers["Cube3"].gameObject);
                m_Renderers.Remove("Cube3");
                var vertices = m_TwoMat.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].y *= 1.5f;
                m_TwoMat.vertices = vertices;
                m_TwoMat.RecalculateBounds();
                break;
            case 30:
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", new Color(0.95f, 0.2f, 0.85f));
                m_Renderers["Cube2"].SetPropertyBlock(block);
                m_Renderers["Cube0"].gameObject.SetActive(false);
                break;
            case 36:
                m_Renderers["Cube0"].gameObject.SetActive(true);
                m_Renderers["Mpb"].SetPropertyBlock(null);
                break;
            case 40:
                Make("Late", PrimitiveType.Sphere, new Vector3(-0.8f, 0.5f, 0.5f), m_Yellow);
                break;
            case 42:
                // The filter's own instance of its mesh, then edited.
                var mesh = m_Renderers["Cube4"].GetComponent<MeshFilter>().mesh;
                var stretched = mesh.vertices;
                for (int i = 0; i < stretched.Length; i++) stretched[i].y = stretched[i].y * 2 + 0.5f;
                mesh.vertices = stretched;
                mesh.RecalculateBounds();
                break;
            case 46:
                // A group made for renderers drawn since the start.
                var lods = new LOD[m_Ungrouped.childCount];
                var heights = new[] { 0.5f, 0.2f, 0.05f };
                for (int i = 0; i < lods.Length; i++)
                    lods[i] = new LOD(heights[i], new Renderer[] { m_Ungrouped.GetChild(i).GetComponent<MeshRenderer>() });
                var group = m_Ungrouped.gameObject.AddComponent<LODGroup>();
                m_Names[group.GetInstanceID()] = "Ungrouped";
                group.SetLODs(lods);
                group.RecalculateBounds();
                break;
            case 52:
                m_Renderers["Cube1"].gameObject.layer = HiddenLayer;
                break;
        }
    }

    void DebugShaders()
    {
        foreach (var material in new[] { m_Red, m_Green, m_Blue, m_Yellow, m_Grey, m_Glass, m_NoDots })
        {
            if (material == null) continue;
            var shader = material.shader;
            m_Debug.Append(material.name).Append(' ').Append(shader.name).Append(" supported ").Append(shader.isSupported);
#if UNITY_EDITOR
            m_Debug.Append(" messages ").Append(UnityEditor.ShaderUtil.GetShaderMessageCount(shader));
            foreach (var message in UnityEditor.ShaderUtil.GetShaderMessages(shader))
                m_Debug.Append("\n  ").Append(message.severity).Append(' ').Append(message.message).Append(' ').Append(message.platform);
#endif
            m_Debug.Append('\n');
        }
    }

    static bool Compiling()
    {
#if UNITY_EDITOR
        return UnityEditor.ShaderUtil.anythingCompiling;
#else
        return false;
#endif
    }

    /// <summary>A new ObjectDispatcher's history length (internal, through reflection); the
    /// dispatcher tracks nothing (in the Editor one of the probe's tracking the drawer's types
    /// left the drawer without the renderers made before it).</summary>
    void CreateDispatcher()
    {
        if (s_DispatcherType == null) return;
        var dispatcher = (IDisposable)Activator.CreateInstance(s_DispatcherType);
        Row("dispatcher.maxHistory", s_DispatcherType.GetProperty("maxDispatchHistoryFramesCount").GetValue(dispatcher));
        dispatcher.Dispose();
    }

    void Capture(int frame)
    {
        string prefix = "f" + frame;
        RenderTexture.active = m_Target;
        m_Readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        m_Readback.Apply();
        RenderTexture.active = null;
        if (!string.IsNullOrEmpty(s_Out)) File.WriteAllBytes(Path.Combine(s_Out, prefix + ".png"), m_Readback.EncodeToPNG());
        Row(prefix + ".drawer.enabled", DrawerEnabled());
        var names = new List<string>(m_Renderers.Keys);
        names.Sort(StringComparer.Ordinal);
        foreach (var name in names)
            Row(prefix + ".visible." + name, m_Renderers[name].isVisible);
        var callback = m_Renderers["Callback"].GetComponent<WillRender>();
        Row(prefix + ".willRenderCalled", callback.calls > 0);
        RecordDrawer(prefix);
    }

    /// <summary>A NativeArray's elements (none when it was not created).</summary>
    static Array ToManaged(object nativeArray)
    {
        var type = nativeArray.GetType();
        if (!(bool)type.GetProperty("IsCreated").GetValue(nativeArray)) return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        return (Array)type.GetMethod("ToArray", Type.EmptyTypes).Invoke(nativeArray, null);
    }

    /// <summary>The renderers the drawer holds instances of (its CPU instance data), by name in
    /// name order.</summary>
    void RecordDrawer(string prefix)
    {
        if (s_Drawer == null) return;
        var drawer = s_Drawer.GetProperty("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        if (drawer == null) return;
        var context = s_Drawer.GetField("m_BatchersContext", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(drawer);
        var shared = context.GetType().GetProperty("sharedInstanceData").GetValue(context);
        var ids = (int[])ToManaged(shared.GetType().GetField("rendererGroupIDs").GetValue(shared));
        var names = new List<string>();
        foreach (int id in ids) names.Add(m_Names.TryGetValue(id, out var name) ? name : "?");
        names.Sort(StringComparer.Ordinal);
        // "?": a renderer the drawer holds that the probe does not know (none expected).
        Row(prefix + ".drawer.renderers", string.Join(",", names));
    }

    void Finish()
    {
        Row("complete", true);
        DebugShaders();
        if (!string.IsNullOrEmpty(s_Out))
        {
            File.WriteAllText(Path.Combine(s_Out, "values.jsonl"), string.Join("\n", s_Rows) + "\n");
            File.WriteAllText(Path.Combine(s_Out, "debug.txt"), m_Debug.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Row(string key, object value)
    {
        var line = new StringBuilder("{\"k\":\"").Append(key).Append("\",\"v\":");
        switch (value)
        {
            case null: line.Append("null"); break;
            case bool b: line.Append(b ? "true" : "false"); break;
            case string s: line.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append('"'); break;
            default: line.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
        s_Rows.Add(line.Append('}').ToString());
    }
}
