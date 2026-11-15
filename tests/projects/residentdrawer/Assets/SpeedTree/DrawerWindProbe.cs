using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The speedtree profile (RESIDENT_DRAWER_SPEEDTREE): the SpeedTree 9 wind parameters the GPU
/// Resident Drawer gives tree instances, frame by frame, while a wind zone comes, changes and goes.
/// The trees are copies of the first level of tests/projects/speedtree9wind's trees (each keeps its
/// Tree and the asset's wind) drawing one quad each with Probe/DrawerWindDump, which writes the
/// instance's 11 wind parameters and their 11 history parameters into the tree's row of a 22xN
/// float target; one orthographic camera renders all of them once a frame. Game time stands still
/// (Time.timeScale 0) until shaders have compiled, so the wind's times do not depend on the
/// warm-up. Floats are recorded as the hex of their bits and compare exactly. Writes values.jsonl
/// into RESIDENT_DRAWER_OUT and exits.
/// </summary>
public sealed class DrawerWindProbe : MonoBehaviour
{
    const int Params = 22, Rows = 8;
    /// <summary>The seconds without a compiling shader the warm-up waits for (the Editor only).</summary>
    const float QuietSeconds = 3f;
    const int LastFrame = 380;
    static readonly int[] k_Recorded =
    {
        1, 2, 10, 11, 12, 13, 15, 20, 30, 50, 51, 52, 60, 75, 76, 77, 80, 90, 91, 95, 96, 97, 100, 101, 102, 110,
        130, 131, 140, 160, 200, 250, 290, 300, 310, 320, 321, 330, 331, 332, 340, 360, 380,
    };

    readonly StringBuilder m_Out = new StringBuilder();
    /// <summary>Each tree's row and the parameters recorded: a wind part its asset turns off is
    /// never written, so not those.</summary>
    readonly List<(string Name, int Row, bool[] Recorded)> m_Trees = new List<(string, int, bool[])>();
    readonly Dictionary<string, Transform> m_Transforms = new Dictionary<string, Transform>();
    Camera m_Camera;
    RenderTexture m_Target;
    Texture2D m_Read;
    GameObject m_Gusty, m_Calm;
    Material m_Dump;
    WindZone m_A, m_Still, m_Again;
    int m_Frame, m_Quiet;
    float m_QuietSince = -1;
    bool m_Started, m_Failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RESIDENT_DRAWER_SPEEDTREE"))) return;
        Time.captureFramerate = 30;
        Time.timeScale = 0f;
        new GameObject("DrawerWindProbe").AddComponent<DrawerWindProbe>();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");

    void Start()
    {
        try
        {
            if (Camera.main != null) Camera.main.enabled = false;
            var light = FindAnyObjectByType<Light>();
            if (light != null) light.shadows = LightShadows.None;
            var graph = GameObject.Find("Graph Sphere");
            if (graph != null) graph.SetActive(false);
#if UNITY_EDITOR
            m_Gusty = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/Gusty.st9");
            m_Calm = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/Calm.st9");
            m_Dump = new Material(UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/SpeedTree/DrawerWindDump.shader"));
#endif
            m_Target = new RenderTexture(Params, Rows, 0, RenderTextureFormat.ARGBFloat);
            m_Read = new Texture2D(Params, Rows, TextureFormat.RGBAFloat, false);
            m_Camera = new GameObject("WindCamera").AddComponent<Camera>();
            m_Camera.transform.SetPositionAndRotation(new Vector3(100f, 0f, -10f), Quaternion.identity);
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 20f;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.clear;
            m_Camera.targetTexture = m_Target;
            m_Camera.enabled = false;
            Tree("GustyA", 0, m_Gusty, new Vector3(90f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Camera);
            // The same asset turned and scaled, drawing per-object motion vectors.
            Tree("GustyB", 1, m_Gusty, new Vector3(100f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f), new Vector3(1.5f, 1.5f, 1.5f), MotionVectorGenerationMode.Object);
            // Calm has no first branch level (parameters 5 and 6).
            Tree("Calm", 2, m_Calm, new Vector3(110f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object, 5, 6);
        }
        catch (Exception e)
        {
            Debug.LogError("drawer wind probe setup failed: " + e);
            m_Failed = true;
        }
    }

    /// <summary>A copy of the asset's first level drawing a quad with the dump material into the
    /// tree's row of the target (its UVs say which, row 0 at the bottom), casting no shadows (a shadow culling would also
    /// see it).</summary>
    void Tree(string name, int row, GameObject prefab, Vector3 at, Quaternion rotation, Vector3 scale, MotionVectorGenerationMode motion,
        params int[] partsOff)
    {
        var recorded = new bool[Params];
        for (int i = 0; i < recorded.Length; i++) recorded[i] = true;
        foreach (int i in partsOff)
        {
            recorded[i] = false;
            recorded[i + Params / 2] = false;
        }
        var copy = Instantiate(prefab.transform.GetChild(0).gameObject, at, rotation);
        copy.name = name;
        copy.transform.localScale = scale;
        float v0 = (float)row / Rows, v1 = (float)(row + 1) / Rows;
        var quad = new Mesh
        {
            vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) },
            uv = new[] { new Vector2(0f, v0), new Vector2(1f, v0), new Vector2(1f, v1), new Vector2(0f, v1) },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };
        copy.GetComponent<MeshFilter>().sharedMesh = quad;
        var renderer = copy.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { m_Dump };
        renderer.motionVectorGenerationMode = motion;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        m_Trees.Add((name, row, recorded));
        m_Transforms[name] = copy.transform;
    }

    static bool Compiling()
    {
#if UNITY_EDITOR
        return UnityEditor.ShaderUtil.anythingCompiling;
#else
        return false;
#endif
    }

    void Update()
    {
        if (m_Failed) return;
        if (!m_Started)
        {
            // The scripted frames start once no shader has compiled for ten frames and three
            // seconds; game time starts with them.
            if (Compiling())
            {
                m_Quiet = 0;
                m_QuietSince = -1;
                return;
            }
            if (m_QuietSince < 0) m_QuietSince = Time.realtimeSinceStartup;
            if (++m_Quiet < 10 || Time.realtimeSinceStartup - m_QuietSince < QuietSeconds) return;
            m_Started = true;
            Time.timeScale = 1f;
        }
        m_Frame++;
        // Changes made here reach the wind update of the next frame.
        if (m_Frame == 10) m_A = Zone("A", WindZoneMode.Directional, 0.6f, Quaternion.Euler(0f, 30f, 0f));
        if (m_Frame == 50) m_A.windMain = 1.4f;
        if (m_Frame == 75) m_A.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        // Out of the camera's view and back: the drawer writes a tree that becomes visible again
        // with its history first.
        if (m_Frame == 90) m_Transforms["GustyB"].position = new Vector3(100f, 0f, -500f);
        if (m_Frame == 95) m_Transforms["GustyB"].position = new Vector3(100f, 0f, 0f);
        // Trees that first draw while the wind blows: camera and per-object motion vectors.
        if (m_Frame == 100)
        {
            Tree("LateA", 3, m_Gusty, new Vector3(95f, 5f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Camera);
            Tree("LateB", 4, m_Gusty, new Vector3(105f, 5f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
        }
        // The wind goes and settles (the world then has no wind), then a zone without wind, then a
        // wind again.
        if (m_Frame == 130) m_A.gameObject.SetActive(false);
        if (m_Frame == 320) m_Still = Zone("Still", WindZoneMode.Directional, 0f, Quaternion.Euler(0f, 90f, 0f));
        if (m_Frame == 330)
        {
            m_Still.gameObject.SetActive(false);
            m_Again = Zone("Again", WindZoneMode.Directional, 0.5f, Quaternion.Euler(0f, -45f, 0f));
        }
    }

    static WindZone Zone(string name, WindZoneMode mode, float main, Quaternion rotation)
    {
        var go = new GameObject("Wind" + name);
        go.transform.rotation = rotation;
        var zone = go.AddComponent<WindZone>();
        zone.mode = mode;
        zone.windMain = main;
        return zone;
    }

    void LateUpdate()
    {
        if (m_Failed)
        {
            Finish(1);
            return;
        }
        if (!m_Started) return;
        m_Camera.Render();
        if (Array.IndexOf(k_Recorded, m_Frame) >= 0)
        {
            string frame = "f" + m_Frame.ToString("000");
            Rec(frame + "/time", F(Time.time));
            Rec(frame + "/held", Held());
            var previous = RenderTexture.active;
            RenderTexture.active = m_Target;
            m_Read.ReadPixels(new Rect(0, 0, Params, Rows), 0, 0);
            m_Read.Apply();
            RenderTexture.active = previous;
            foreach (var (name, row, recorded) in m_Trees)
            {
                for (int i = 0; i < Params; i++)
                {
                    if (!recorded[i]) continue;
                    var c = m_Read.GetPixel(i, row);
                    Rec(frame + "/" + name + "/" + i, F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a));
                }
            }
        }
        if (m_Frame >= LastFrame) Finish(0);
    }

    /// <summary>The probe trees the drawer holds instances of (its CPU instance data), in name
    /// order; a tree it does not hold draws its dump without instance data (zeros).</summary>
    string Held()
    {
        var drawerType = Type.GetType("UnityEngine.Rendering.GPUResidentDrawer, Unity.RenderPipelines.GPUDriven.Runtime");
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var drawer = drawerType?.GetProperty("instance", flags)?.GetValue(null);
        if (drawer == null) return "none";
        var context = drawerType.GetField("m_BatchersContext", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(drawer);
        var shared = context.GetType().GetProperty("sharedInstanceData").GetValue(context);
        var ids = shared.GetType().GetField("rendererGroupIDs").GetValue(shared);
        var array = (int[])ids.GetType().GetMethod("ToArray", Type.EmptyTypes).Invoke(ids, null);
        var names = new List<string>();
        foreach (var (name, _, _) in m_Trees)
            if (Array.IndexOf(array, m_Transforms[name].GetComponent<MeshRenderer>().GetInstanceID()) >= 0) names.Add(name);
        names.Sort(StringComparer.Ordinal);
        return string.Join(",", names);
    }

    void Finish(int code)
    {
        string dir = Environment.GetEnvironmentVariable("RESIDENT_DRAWER_OUT");
        if (code == 0) m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
