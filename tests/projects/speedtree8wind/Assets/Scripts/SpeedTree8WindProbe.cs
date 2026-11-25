using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records the SpeedTree 8 wind constants a tree draws with, frame by frame, while wind zones come,
/// change and go (see README.md). Play mode at a captured 30 frames per second, built-in render
/// pipeline. Each probe tree is a copy of a sample tree's first level (its Tree keeps the asset's
/// wind) whose mesh is a quad and whose material writes the constant buffers SpeedTreeWind and
/// SpeedTreeWindHistory into a 30x1 float target; one orthographic camera per tree renders it every
/// frame. At the recorded frames: Time.time and each tree's drawn constants, floats as the hex of
/// their bits. Writes values.jsonl into SPEEDTREE_PROBE_OUT and exits.
/// </summary>
public sealed class SpeedTree8WindProbe : MonoBehaviour
{
    static readonly string[] k_Names =
    {
        "_ST_WindVector", "_ST_WindGlobal", "_ST_WindBranch", "_ST_WindBranchTwitch", "_ST_WindBranchWhip",
        "_ST_WindBranchAnchor", "_ST_WindBranchAdherences", "_ST_WindTurbulences", "_ST_WindLeaf1Ripple",
        "_ST_WindLeaf1Tumble", "_ST_WindLeaf1Twitch", "_ST_WindLeaf2Ripple", "_ST_WindLeaf2Tumble",
        "_ST_WindLeaf2Twitch", "_ST_WindFrondRipple",
        "_ST_WindVectorHistory", "_ST_WindGlobalHistory", "_ST_WindBranchHistory", "_ST_WindBranchTwitchHistory",
        "_ST_WindBranchWhipHistory", "_ST_WindBranchAnchorHistory", "_ST_WindBranchAdherencesHistory",
        "_ST_WindTurbulencesHistory", "_ST_WindLeaf1RippleHistory", "_ST_WindLeaf1TumbleHistory",
        "_ST_WindLeaf1TwitchHistory", "_ST_WindLeaf2RippleHistory", "_ST_WindLeaf2TumbleHistory",
        "_ST_WindLeaf2TwitchHistory", "_ST_WindFrondRippleHistory",
    };
    const int Current = 15;

    static readonly int[] k_Recorded =
    {
        1, 2, 9, 10, 11, 12, 13, 15, 20, 30, 45, 50, 51, 52, 55, 70, 75, 76, 77, 80, 90, 100,
        105, 106, 107, 110, 115, 120, 121, 125, 130, 131, 132, 135, 140, 160, 200, 201, 220, 250,
        260, 261, 262, 265, 270, 280, 300, 301, 302, 305, 320, 340, 360,
    };
    const int LastFrame = 360;

    readonly StringBuilder m_Out = new StringBuilder();
    /// <summary>Each tree's camera, the constants it records (a renderer without per-object motion
    /// vectors records the current ones only) and the earlier tree of its asset, if any.</summary>
    readonly List<(string Name, Camera Camera, bool History, string Twin)> m_Trees = new List<(string, Camera, bool, string)>();
    /// <summary>The wind vector each tree drew with this frame.</summary>
    readonly Dictionary<string, Color> m_Vectors = new Dictionary<string, Color>();
    RenderTexture m_Target;
    Texture2D m_Read;
    GameObject m_Field;
    Material m_Dump;
    WindZone m_A, m_B, m_Far, m_Again;
    int m_Frame;
    bool m_Failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Time.captureFramerate = 30;
        new GameObject("SpeedTree8WindProbe").AddComponent<SpeedTree8WindProbe>();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");

    void Awake()
    {
        try
        {
            var field = m_Field = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/v8/Broadleaf_Desktop_Field.st");
            if (field == null) throw new Exception("the staged sample tree Broadleaf_Desktop_Field.st is not there");
            m_Target = new RenderTexture(k_Names.Length, 1, 0, RenderTextureFormat.ARGBFloat);
            m_Read = new Texture2D(k_Names.Length, 1, TextureFormat.RGBAFloat, false);
            m_Dump = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Wind8Dump.shader"));
            Tree("FieldA", null, 29, new Vector3(100f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Camera);
            // The same asset turned and scaled, drawing per-object motion vectors (the history constants).
            Tree("FieldB", null, 30, new Vector3(200f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f), new Vector3(1.5f, 1.5f, 1.5f),
                MotionVectorGenerationMode.Object);
            var level0 = field.transform.GetChild(0).GetComponent<Tree>();
            Rec("hasSpeedTreeWind/level0", level0 != null && level0.hasSpeedTreeWind ? "true" : "false");
            Rec("levels", field.transform.childCount.ToString());
        }
        catch (Exception e)
        {
            Debug.LogError("speedtree8 wind probe setup failed: " + e);
            m_Failed = true;
        }
    }

    /// <summary>A copy of the asset's first level drawing a quad with the dump material on a layer
    /// of its own, and an orthographic camera that sees only it. A tree with a twin (an earlier
    /// tree of the same asset) is one that first draws late: until it draws its own wind it reads
    /// what an earlier draw left, so it records whether its wind vector is its twin's, and its
    /// constants only when it is.</summary>
    void Tree(string name, string twin, int layer, Vector3 at, Quaternion rotation, Vector3 scale, MotionVectorGenerationMode motion)
    {
        var copy = Instantiate(m_Field.transform.GetChild(0).gameObject, at, rotation);
        copy.name = name;
        copy.layer = layer;
        copy.transform.localScale = scale;
        copy.GetComponent<MeshFilter>().sharedMesh = Quad();
        var renderer = copy.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { m_Dump };
        renderer.motionVectorGenerationMode = motion;
        m_Trees.Add((name, MakeCamera(name, at, layer), motion == MotionVectorGenerationMode.Object, twin));
    }

    static Mesh Quad() => new Mesh
    {
        vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) },
        uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
        triangles = new[] { 0, 2, 1, 0, 3, 2 },
    };

    Camera MakeCamera(string name, Vector3 at, int layer)
    {
        var camera = new GameObject(name + "Camera").AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(at + new Vector3(0f, 0f, -5f), Quaternion.identity);
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.cullingMask = 1 << layer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.targetTexture = m_Target;
        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        camera.enabled = false;
        return camera;
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
        // A spherical zone that reaches no tree.
        if (m_Frame == 120) m_Far = Zone("Far", null, WindZoneMode.Spherical, 2f, Quaternion.identity, new Vector3(0f, 0f, 500f));
        if (m_Frame == 130)
        {
            m_A.gameObject.SetActive(false);
            m_B.gameObject.SetActive(false);
            m_Far.gameObject.SetActive(false);
        }
        // Calm again; then a wind again, and a tree that first draws in it.
        if (m_Frame == 260) m_Again = Zone("Again", null, WindZoneMode.Directional, 0.5f, Quaternion.Euler(0f, -45f, 0f), Vector3.zero);
        if (m_Frame == 300)
            Tree("FieldLate", "FieldB", 28, new Vector3(300f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
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
        if (record) Rec(frame + "/time", F(Time.time));
        foreach (var (name, camera, history, twin) in m_Trees)
        {
            camera.Render();
            if (!record) continue;
            var previous = RenderTexture.active;
            RenderTexture.active = m_Target;
            m_Read.ReadPixels(new Rect(0, 0, k_Names.Length, 1), 0, 0);
            m_Read.Apply();
            RenderTexture.active = previous;
            m_Vectors[name] = m_Read.GetPixel(0, 0);
            if (twin != null)
            {
                bool own = m_Vectors[name] == m_Vectors[twin];
                Rec(frame + "/" + name + "/sameVector", own ? "true" : "false");
                if (!own) continue;
            }
            for (int i = 0; i < k_Names.Length; i++)
            {
                if (i >= Current && !history) continue;
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
