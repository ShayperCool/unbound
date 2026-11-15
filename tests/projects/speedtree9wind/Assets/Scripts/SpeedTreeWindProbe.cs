using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records the SpeedTree 9 wind constants trees draw with, frame by frame, while wind zones come,
/// change and go (see README.md). Play mode at a captured 30 frames per second. Each probe tree is
/// a copy of a tree asset's first level (its Tree keeps the asset's wind) whose mesh is a quad and
/// whose material writes the constant buffers SpeedTreeWind and SpeedTreeWindHistory into a 22x1
/// float target; one orthographic camera per tree renders it every frame. At the recorded frames:
/// Time.time and each tree's drawn constants, floats as the hex of their bits. Writes
/// values.jsonl into SPEEDTREE_PROBE_OUT and exits.
/// </summary>
public sealed class SpeedTreeWindProbe : MonoBehaviour
{
    static readonly string[] k_Names =
    {
        "_ST_WindVector", "_ST_TreeExtents_SharedHeightStart", "_ST_BranchStretchLimits",
        "_ST_Shared_NoisePosTurbulence_Independence", "_ST_Shared_Bend_Oscillation_Turbulence_Flexibility",
        "_ST_Branch1_NoisePosTurbulence_Independence", "_ST_Branch1_Bend_Oscillation_Turbulence_Flexibility",
        "_ST_Branch2_NoisePosTurbulence_Independence", "_ST_Branch2_Bend_Oscillation_Turbulence_Flexibility",
        "_ST_Ripple_NoisePosTurbulence_Independence", "_ST_Ripple_Planar_Directional_Flexibility_Shimmer",
        "_ST_HistoryWindVector", "_ST_HistoryTreeExtents_SharedHeightStart", "_ST_HistoryBranchStretchLimits",
        "_ST_HistoryShared_NoisePosTurbulence_Independence", "_ST_HistoryShared_Bend_Oscillation_Turbulence_Flexibility",
        "_ST_HistoryBranch1_NoisePosTurbulence_Independence", "_ST_HistoryBranch1_Bend_Oscillation_Turbulence_Flexibility",
        "_ST_HistoryBranch2_NoisePosTurbulence_Independence", "_ST_HistoryBranch2_Bend_Oscillation_Turbulence_Flexibility",
        "_ST_HistoryRipple_NoisePosTurbulence_Independence", "_ST_HistoryRipple_Planar_Directional_Flexibility_Shimmer",
    };

    static readonly int[] k_Recorded =
    {
        1, 2, 10, 11, 12, 13, 15, 20, 30, 45, 51, 52, 55, 70, 76, 77, 80, 90, 100,
        106, 107, 115, 121, 125, 131, 132, 140, 160, 200, 201, 250, 300, 301, 330, 336, 340,
        341, 345, 351, 353, 355, 361, 362, 370, 400, 401, 402, 410, 420,
        // Spherical zones reaching trees.
        425, 426, 427, 428, 429, 430, 432, 435, 440, 450, 460, 480, 490, 491, 492, 493, 495, 500, 501, 502, 503, 505,
        510, 520, 530, 540, 541, 542, 545, 550, 560, 570, 580, 581, 582, 585, 590, 600, 620, 640, 660, 700, 740, 780,
    };
    const int LastFrame = 780;

    readonly StringBuilder m_Out = new StringBuilder();
    /// <summary>Each tree's camera and the constants it records: a renderer without per-object
    /// motion vectors gets no SpeedTreeWindHistory binding (the buffer holds whatever an earlier
    /// draw left), so only its 11 current constants; and a wind part its asset turns off is never
    /// written (its constants hold whatever the memory held), so not those.</summary>
    readonly List<(string Name, Camera Camera, bool[] Recorded, string Twin)> m_Trees = new List<(string, Camera, bool[], string)>();
    /// <summary>The tree extents each tree drew with this frame.</summary>
    readonly Dictionary<string, Color> m_Extents = new Dictionary<string, Color>();
    RenderTexture m_Target;
    Texture2D m_Read;
    GameObject m_Gusty, m_Calm;
    Material m_Dump;
    WindZone m_A, m_B, m_Sphere, m_Still, m_Again, m_NearA, m_NearB;
    int m_Frame;
    bool m_Failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("SpeedTreeWindProbe").AddComponent<SpeedTreeWindProbe>();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");

    void Awake()
    {
        try
        {
            var gusty = m_Gusty = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/Gusty.st9");
            var calm = m_Calm = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/Calm.st9");
            m_Target = new RenderTexture(22, 1, 0, RenderTextureFormat.ARGBFloat);
            m_Read = new Texture2D(22, 1, TextureFormat.RGBAFloat, false);
            var dump = m_Dump = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/WindDump.shader"));
            Tree("GustyA", gusty, dump, 29, new Vector3(100f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Camera);
            // The same asset turned and scaled, drawing per-object motion vectors (the history constants).
            Tree("GustyB", gusty, dump, 30, new Vector3(200f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f), new Vector3(1.5f, 1.5f, 1.5f),
                MotionVectorGenerationMode.Object);
            // Calm has no first branch level (constants 5 and 6).
            Tree("Calm", calm, dump, 31, new Vector3(300f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object, 5, 6);

            Rec("hasSpeedTreeWind/level0", gusty.transform.GetChild(0).GetComponent<Tree>().hasSpeedTreeWind ? "true" : "false");
            Rec("hasSpeedTreeWind/level1", gusty.transform.GetChild(1).GetComponent<Tree>().hasSpeedTreeWind ? "true" : "false");
            Rec("hasSpeedTreeWind/added", new GameObject("PlainTree").AddComponent<Tree>().hasSpeedTreeWind ? "true" : "false");
        }
        catch (Exception e)
        {
            Debug.LogError("speedtree wind probe setup failed: " + e);
            m_Failed = true;
        }
    }

    /// <summary>A copy of the asset's first level drawing a quad with the dump material on a
    /// layer of its own, and an orthographic camera that sees only it. A tree with a twin (an
    /// earlier tree of the same asset) is one that first draws late: until it draws its own wind
    /// it reads what an earlier draw left, so it records whether its extents are its twin's, and
    /// its constants only when they are.</summary>
    void Tree(string name, GameObject prefab, Material material, int layer, Vector3 at, Quaternion rotation, Vector3 scale,
        MotionVectorGenerationMode motion, params int[] partsOff) => Tree(name, null, prefab, material, layer, at, rotation, scale, motion, partsOff);

    void Tree(string name, string twin, GameObject prefab, Material material, int layer, Vector3 at, Quaternion rotation, Vector3 scale,
        MotionVectorGenerationMode motion, params int[] partsOff)
    {
        var recorded = new bool[k_Names.Length];
        for (int i = 0; i < recorded.Length; i++) recorded[i] = motion == MotionVectorGenerationMode.Object || i < k_Names.Length / 2;
        foreach (int i in partsOff)
        {
            recorded[i] = false;
            recorded[i + k_Names.Length / 2] = false;
        }
        var copy = Instantiate(prefab.transform.GetChild(0).gameObject, at, rotation);
        copy.name = name;
        copy.layer = layer;
        copy.transform.localScale = scale;
        var quad = new Mesh
        {
            vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) },
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
            triangles = new[] { 0, 2, 1, 0, 3, 2 },
        };
        copy.GetComponent<MeshFilter>().sharedMesh = quad;
        var renderer = copy.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { material };
        renderer.motionVectorGenerationMode = motion;
        var camera = new GameObject(name + "Camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(at + new Vector3(0f, 0f, -5f), Quaternion.identity);
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.cullingMask = 1 << layer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.targetTexture = m_Target;
        camera.enabled = false;
        m_Trees.Add((name, camera, recorded, twin));
    }

    void Update()
    {
        if (m_Failed) return;
        m_Frame++;
        // Changes made here reach the wind update of the next frame.
        if (m_Frame == 10) m_A = Zone("A", null, WindZoneMode.Directional, 0.6f, Quaternion.Euler(0f, 30f, 0f), Vector3.zero);
        if (m_Frame == 50) m_A.windMain = 1.4f;
        if (m_Frame == 75) m_A.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
        if (m_Frame == 105)
        {
            // Under a mirrored, turned parent: the zone's forward follows the parent's scale sign.
            var mirror = new GameObject("Mirror").transform;
            mirror.SetPositionAndRotation(new Vector3(3f, 0f, 0f), Quaternion.Euler(0f, 70f, 10f));
            mirror.localScale = new Vector3(-1f, 2f, 1f);
            m_B = Zone("B", mirror, WindZoneMode.Directional, 0.3f, Quaternion.Euler(20f, -60f, 0f), Vector3.zero);
        }
        if (m_Frame == 120) m_Sphere = Zone("Far", null, WindZoneMode.Spherical, 2f, Quaternion.identity, new Vector3(0f, 0f, 500f));
        if (m_Frame == 130)
        {
            m_A.gameObject.SetActive(false);
            m_B.gameObject.SetActive(false);
            m_Sphere.gameObject.SetActive(false);
        }
        // The winds have settled by then. Trees that first draw now: one with no wind in the
        // world, one under a spherical zone alone, one under a directional zone without wind.
        if (m_Frame == 300)
            Tree("LateSettled", "GustyB", m_Gusty, m_Dump, 26, new Vector3(400f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
        if (m_Frame == 335)
            Tree("LateSpherical", "GustyB", m_Gusty, m_Dump, 27, new Vector3(500f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
        if (m_Frame == 351)
            Tree("LateStill", "Calm", m_Calm, m_Dump, 28, new Vector3(600f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object, 5, 6);
        // A spherical zone alone, then a directional zone without wind, then a wind again.
        if (m_Frame == 340) m_Sphere.gameObject.SetActive(true);
        if (m_Frame == 350)
        {
            m_Sphere.gameObject.SetActive(false);
            m_Still = Zone("Still", null, WindZoneMode.Directional, 0f, Quaternion.Euler(0f, 90f, 0f), Vector3.zero);
        }
        if (m_Frame == 360)
        {
            m_Still.gameObject.SetActive(false);
            m_Again = Zone("Again", null, WindZoneMode.Directional, 0.5f, Quaternion.Euler(0f, -45f, 0f), Vector3.zero);
        }
        // The same wind drawn through the built-in render pipeline.
        if (m_Frame == 400)
        {
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
        }
        // A spherical zone reaching GustyA (a local wind of its own), then moved out of reach
        // (the local wind returns to the asset's wind); another reaching GustyB (per-object motion
        // vectors), then the directional wind goes (spherical wind only), then both zones go.
        if (m_Frame == 425) m_NearA = Zone("NearA", null, WindZoneMode.Spherical, 1.2f, Quaternion.identity, new Vector3(98f, 0.5f, 1f), 4f);
        if (m_Frame == 490) m_NearA.transform.position = new Vector3(100f, 0f, -60f);
        if (m_Frame == 500) m_NearB = Zone("NearB", null, WindZoneMode.Spherical, 0.8f, Quaternion.identity, new Vector3(201.5f, -0.5f, -2f), 5f);
        if (m_Frame == 540) m_Again.gameObject.SetActive(false);
        if (m_Frame == 580)
        {
            m_NearA.gameObject.SetActive(false);
            m_NearB.gameObject.SetActive(false);
        }
    }

    static WindZone Zone(string name, Transform parent, WindZoneMode mode, float main, Quaternion rotation, Vector3 position, float radius = 10f)
    {
        var go = new GameObject("Wind" + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        var zone = go.AddComponent<WindZone>();
        zone.mode = mode;
        zone.radius = radius;
        zone.windMain = main;
        return zone;
    }

    void LateUpdate()
    {
        if (m_Failed)
        {
            EditorApplication.Exit(1);
            return;
        }
        bool record = Array.IndexOf(k_Recorded, m_Frame) >= 0;
        string frame = "f" + m_Frame.ToString("000");
        if (record)
        {
            Rec(frame + "/time", F(Time.time));
            Rec(frame + "/pipeline", GraphicsSettings.currentRenderPipeline == null ? "builtin" : GraphicsSettings.currentRenderPipeline.GetType().Name);
        }
        foreach (var (name, camera, recordedConstants, twin) in m_Trees)
        {
            camera.Render();
            if (!record) continue;
            var previous = RenderTexture.active;
            RenderTexture.active = m_Target;
            m_Read.ReadPixels(new Rect(0, 0, 22, 1), 0, 0);
            m_Read.Apply();
            RenderTexture.active = previous;
            m_Extents[name] = m_Read.GetPixel(1, 0);
            if (twin != null)
            {
                bool own = m_Extents[name] == m_Extents[twin];
                Rec(frame + "/" + name + "/ownWind", own ? "true" : "false");
                if (!own) continue;
            }
            for (int i = 0; i < k_Names.Length; i++)
            {
                if (!recordedConstants[i]) continue;
                var c = m_Read.GetPixel(i, 0);
                Rec(frame + "/" + name + "/" + k_Names[i], F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a));
            }
        }
        if (m_Frame < LastFrame) return;
        string dir = Environment.GetEnvironmentVariable("SPEEDTREE_PROBE_OUT");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.Exit(0);
    }
}
