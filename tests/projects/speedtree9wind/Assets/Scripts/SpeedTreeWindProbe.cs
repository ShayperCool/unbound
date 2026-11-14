using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

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
        106, 107, 115, 121, 125, 131, 132, 140, 160, 200, 201,
    };
    const int LastFrame = 201;

    readonly StringBuilder m_Out = new StringBuilder();
    /// <summary>Each tree's camera and how many constants it records: a renderer without
    /// per-object motion vectors gets no SpeedTreeWindHistory binding (the buffer holds whatever
    /// an earlier draw left), so only its 11 current constants are recorded.</summary>
    readonly List<(string Name, Camera Camera, int Count)> m_Trees = new List<(string, Camera, int)>();
    RenderTexture m_Target;
    Texture2D m_Read;
    WindZone m_A, m_B, m_Sphere;
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
            var gusty = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/Gusty.st9");
            var calm = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/Calm.st9");
            m_Target = new RenderTexture(22, 1, 0, RenderTextureFormat.ARGBFloat);
            m_Read = new Texture2D(22, 1, TextureFormat.RGBAFloat, false);
            var dump = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/WindDump.shader"));
            Tree("GustyA", gusty, dump, 29, new Vector3(100f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Camera);
            // The same asset turned and scaled, drawing per-object motion vectors (the history constants).
            Tree("GustyB", gusty, dump, 30, new Vector3(200f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f), new Vector3(1.5f, 1.5f, 1.5f),
                MotionVectorGenerationMode.Object);
            Tree("Calm", calm, dump, 31, new Vector3(300f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);

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
    /// layer of its own, and an orthographic camera that sees only it.</summary>
    void Tree(string name, GameObject prefab, Material material, int layer, Vector3 at, Quaternion rotation, Vector3 scale,
        MotionVectorGenerationMode motion)
    {
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
        m_Trees.Add((name, camera, motion == MotionVectorGenerationMode.Object ? k_Names.Length : k_Names.Length / 2));
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
    }

    static WindZone Zone(string name, Transform parent, WindZoneMode mode, float main, Quaternion rotation, Vector3 position)
    {
        var go = new GameObject("Wind" + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        var zone = go.AddComponent<WindZone>();
        zone.mode = mode;
        zone.radius = 10f;
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
        if (record) Rec(frame + "/time", F(Time.time));
        foreach (var (name, camera, count) in m_Trees)
        {
            camera.Render();
            if (!record) continue;
            var previous = RenderTexture.active;
            RenderTexture.active = m_Target;
            m_Read.ReadPixels(new Rect(0, 0, 22, 1), 0, 0);
            m_Read.Apply();
            RenderTexture.active = previous;
            for (int i = 0; i < count; i++)
            {
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
