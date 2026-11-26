using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Records the SpeedTree 8 wind constants a tree draws with, frame by frame, while wind zones come,
/// change and go (see README.md). Play mode at a captured 30 frames per second, built-in render
/// pipeline. Each probe tree is a copy of a sample tree's level (its Tree keeps the asset's wind)
/// whose mesh is a quad and whose material writes the constant buffers SpeedTreeWind and
/// SpeedTreeWindHistory (16 float4 each, as the SpeedTree 8 shaders declare them, _ST_WindAnimation
/// last) into a 32x1 float target; one orthographic camera per tree renders it every frame. A
/// SpeedTree 9 tree's material writes its 11 + 11 constants into the same target. At the recorded
/// frames: Time.time and each tree's drawn constants, floats as the hex of their bits. Writes
/// values.jsonl into SPEEDTREE_PROBE_OUT and exits.
/// </summary>
public sealed class SpeedTree8WindProbe : MonoBehaviour
{
    static readonly string[] k_Names =
    {
        "_ST_WindVector", "_ST_WindGlobal", "_ST_WindBranch", "_ST_WindBranchTwitch", "_ST_WindBranchWhip",
        "_ST_WindBranchAnchor", "_ST_WindBranchAdherences", "_ST_WindTurbulences", "_ST_WindLeaf1Ripple",
        "_ST_WindLeaf1Tumble", "_ST_WindLeaf1Twitch", "_ST_WindLeaf2Ripple", "_ST_WindLeaf2Tumble",
        "_ST_WindLeaf2Twitch", "_ST_WindFrondRipple", "_ST_WindAnimation",
        "_ST_WindVectorHistory", "_ST_WindGlobalHistory", "_ST_WindBranchHistory", "_ST_WindBranchTwitchHistory",
        "_ST_WindBranchWhipHistory", "_ST_WindBranchAnchorHistory", "_ST_WindBranchAdherencesHistory",
        "_ST_WindTurbulencesHistory", "_ST_WindLeaf1RippleHistory", "_ST_WindLeaf1TumbleHistory",
        "_ST_WindLeaf1TwitchHistory", "_ST_WindLeaf2RippleHistory", "_ST_WindLeaf2TumbleHistory",
        "_ST_WindLeaf2TwitchHistory", "_ST_WindFrondRippleHistory", "_ST_WindAnimationHistory",
    };
    const int Current = 16;

    /// <summary>The SpeedTree 9 constants (tests/projects/speedtree9wind's dump).</summary>
    static readonly string[] k_Names9 =
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
        1, 2, 3, 4, 5, 9, 10, 11, 12, 13, 15, 20, 30, 45, 50, 51, 52, 55, 70, 75, 76, 77, 80, 90, 100,
        105, 106, 107, 110, 115, 120, 121, 125, 130, 131, 132, 135, 140, 160, 200, 201, 220, 250,
        300, 330, 331, 335, 350, 400, 450, 500, 550, 600, 650, 700, 750, 800, 850, 899, 900, 901, 902, 903, 905, 910,
        930, 960, 1000, 1001, 1002, 1005,
        // A spherical zone reaches FieldNear (1010) and is moved out of its reach (1100).
        1011, 1012, 1013, 1015, 1020, 1030, 1040, 1050, 1060, 1080, 1099,
        1100, 1101, 1102, 1103, 1105, 1110, 1120, 1130, 1150, 1170, 1200, 1249, 1250, 1251, 1252,
        1300, 1301, 1302, 1350, 1400, 1450, 1500, 1550, 1560, 1561, 1562, 1563, 1564, 1565, 1566, 1567, 1568, 1569, 1570, 1600, 1650,
    };
    const int LastFrame = 1650;

    /// <summary>The scene whose wind blows from the first frame (a directional zone saved in it):
    /// the trees from the start and nothing changed.</summary>
    static readonly int[] k_RecordedFirst =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 15, 20, 30, 45, 60, 90, 120, 150, 200, 300, 450,
    };
    const int LastFrameFirst = 450;

    /// <summary>The scene of the spherical zones' later cases: a zone that reaches a tree while its
    /// asset's wind rests (after it blew), and a strong zone that reaches a tree for 13 seconds
    /// under a weak directional wind before it goes (the tree's long return).</summary>
    static readonly int[] k_RecordedZones =
    {
        1, 5, 10, 11, 12, 20, 40, 41, 42, 100, 200, 300, 400, 460, 500, 519,
        520, 521, 522, 523, 525, 530, 540, 560, 600, 639, 640, 641, 642, 645, 660, 699,
        700, 701, 702, 703, 705, 710, 711, 712, 713, 715, 720, 750, 800, 900, 1000, 1100, 1109,
        1110, 1111, 1112, 1113, 1115, 1120, 1130, 1150, 1200, 1250, 1300, 1350, 1400, 1450,
    };
    const int LastFrameZones = 1450;

    readonly StringBuilder m_Out = new StringBuilder();
    /// <summary>Each tree's camera, the constants it records (a renderer without per-object motion
    /// vectors records the current ones only), the earlier tree of its asset, if any, and the
    /// names of its constants (SpeedTree 8 or 9).</summary>
    readonly List<(string Name, Camera Camera, bool History, string Twin, string[] Names)> m_Trees = new List<(string, Camera, bool, string, string[])>();
    /// <summary>The wind vector each tree drew with this frame.</summary>
    readonly Dictionary<string, Color> m_Vectors = new Dictionary<string, Color>();
    RenderTexture m_Target;
    Texture2D m_Read;
    GameObject m_Field, m_Directional, m_Calm;
    Material m_Dump, m_Dump9;
    WindZone m_A, m_B, m_Far, m_Again, m_Near, m_Gone, m_Strong;
    int m_Frame;
    bool m_Failed, m_First, m_Zones;

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
            m_First = SceneManager.GetActiveScene().name == "FirstWind";
            m_Zones = SceneManager.GetActiveScene().name == "ZonesWind";
            var field = m_Field = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/v8/Broadleaf_Desktop_Field.st");
            if (field == null) throw new Exception("the staged sample tree Broadleaf_Desktop_Field.st is not there");
            m_Target = new RenderTexture(k_Names.Length, 1, 0, RenderTextureFormat.ARGBFloat);
            m_Read = new Texture2D(k_Names.Length, 1, TextureFormat.RGBAFloat, false);
            m_Dump = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Wind8Dump.shader"));
            Tree("FieldA", null, field, 0, m_Dump, k_Names, 29, new Vector3(100f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Camera);
            // The same asset turned and scaled, drawing per-object motion vectors (the history constants).
            Tree("FieldB", null, field, 0, m_Dump, k_Names, 30, new Vector3(200f, 0f, 0f), Quaternion.Euler(0f, 40f, 0f), new Vector3(1.5f, 1.5f, 1.5f),
                MotionVectorGenerationMode.Object);
            var level0 = field.transform.GetChild(0).GetComponent<Tree>();
            Rec("hasSpeedTreeWind/level0", level0 != null && level0.hasSpeedTreeWind ? "true" : "false");
            Rec("levels", field.transform.childCount.ToString());
            if (m_First) return;
            if (m_Zones)
            {
                // A tree a spherical zone reaches while the asset's wind rests, and one a strong
                // zone reaches for long.
                Tree("FieldCalm", null, field, 0, m_Dump, k_Names, 27, new Vector3(400f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
                Tree("FieldLong", null, field, 0, m_Dump, k_Names, 26, new Vector3(500f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
                return;
            }
            // A tree a spherical zone reaches later (its own wind while it does).
            Tree("FieldNear", null, field, 0, m_Dump, k_Names, 27, new Vector3(400f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
            // The asset's billboard level (its last level with a Tree).
            int billboard = LastTreeLevel(field);
            Rec("billboardLevel", billboard.ToString());
            Tree("FieldBillboard", null, field, billboard, m_Dump, k_Names, 26, new Vector3(500f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
            // A copy of the tree whose file makes both branch levels' adherence directional.
            m_Directional = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees/v8/Field_Directional.st");
            if (m_Directional == null) throw new Exception("the staged variant Field_Directional.st is not there");
            var children = new StringBuilder();
            foreach (Transform child in m_Directional.transform) children.Append(child.name).Append(child.GetComponent<MeshFilter>() != null ? "[mesh]" : "").Append(' ');
            Rec("directional/children", children.ToString().TrimEnd());
            Tree("FieldDirectional", null, m_Directional, FirstTreeLevel(m_Directional), m_Dump, k_Names, 25, new Vector3(600f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
            // A SpeedTree 9 tree (its wind settles before this tree's: its responses are shorter).
            m_Calm = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Trees9/Calm.st9");
            if (m_Calm == null) throw new Exception("the staged SpeedTree 9 tree Calm.st9 is not there");
            m_Dump9 = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/WindDump.shader"));
            Tree("Calm9", null, m_Calm, 0, m_Dump9, k_Names9, 24, new Vector3(700f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
        }
        catch (Exception e)
        {
            Debug.LogError("speedtree8 wind probe setup failed: " + e);
            m_Failed = true;
        }
    }

    static int FirstTreeLevel(GameObject asset)
    {
        for (int i = 0; i < asset.transform.childCount; i++)
            if (asset.transform.GetChild(i).GetComponent<Tree>() != null && asset.transform.GetChild(i).GetComponent<MeshFilter>() != null) return i;
        throw new Exception(asset.name + " has no level with a Tree and a mesh");
    }

    static int LastTreeLevel(GameObject asset)
    {
        for (int i = asset.transform.childCount - 1; i >= 0; i--)
            if (asset.transform.GetChild(i).GetComponent<Tree>() != null) return i;
        throw new Exception("the asset has no level with a Tree");
    }

    /// <summary>A copy of an asset's level drawing a quad with a dump material on a layer of its
    /// own, and an orthographic camera that sees only it. A tree with a twin (an earlier tree of
    /// the same asset) is one that first draws late: until it draws its own wind it reads what an
    /// earlier draw left, so it records whether its wind vector is its twin's, and its constants
    /// only when it is.</summary>
    void Tree(string name, string twin, GameObject asset, int level, Material dump, string[] names, int layer, Vector3 at, Quaternion rotation, Vector3 scale,
        MotionVectorGenerationMode motion)
    {
        var copy = Instantiate(asset.transform.GetChild(level).gameObject, at, rotation);
        copy.name = name;
        copy.layer = layer;
        copy.transform.localScale = scale;
        copy.GetComponent<MeshFilter>().sharedMesh = Quad();
        var renderer = copy.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { dump };
        renderer.motionVectorGenerationMode = motion;
        m_Trees.Add((name, MakeCamera(name, at, layer), motion == MotionVectorGenerationMode.Object, twin, names));
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
        if (m_First) return;
        if (m_Zones)
        {
            // A wind that blows and goes: the asset's wind eases to rest (by about frame 460).
            if (m_Frame == 10) m_Gone = Zone("Gone", null, WindZoneMode.Directional, 0.6f, Quaternion.Euler(0f, 120f, 0f), Vector3.zero);
            if (m_Frame == 40) m_Gone.gameObject.SetActive(false);
            // At rest, a spherical zone reaches FieldCalm, and goes out of its reach.
            if (m_Frame == 520) m_Near = Zone("Near", null, WindZoneMode.Spherical, 1.2f, Quaternion.identity, new Vector3(397f, 0f, -2f));
            if (m_Frame == 640) m_Near.transform.position = new Vector3(400f, 0f, 500f);
            // A weak wind (FieldCalm's own wind returns to the asset's), and a strong spherical
            // zone beside FieldLong for 400 frames.
            if (m_Frame == 700) Zone("Weak", null, WindZoneMode.Directional, 0.15f, Quaternion.Euler(0f, -45f, 0f), Vector3.zero);
            if (m_Frame == 710) m_Strong = Zone("Strong", null, WindZoneMode.Spherical, 3f, Quaternion.identity, new Vector3(497f, 0f, -2f));
            if (m_Frame == 1110) m_Strong.transform.position = new Vector3(500f, 0f, 500f);
            return;
        }
        // A global value of _ST_WindAnimation (which the wind leaves unwritten), and later a
        // material one, set before the trees draw with wind and long after.
        if (m_Frame == 2) Shader.SetGlobalVector("_ST_WindAnimation", new Vector4(1f, 2f, 3f, 4f));
        if (m_Frame == 1299) m_Dump.SetVector("_ST_WindAnimation", new Vector4(5f, 6f, 7f, 8f));
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
        // While the SpeedTree 8 wind still eases to rest and the SpeedTree 9 wind has settled, a
        // SpeedTree 9 tree first draws.
        if (m_Frame == 330)
            Tree("Calm9Late", "Calm9", m_Calm, 0, m_Dump9, k_Names9, 23, new Vector3(800f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
        // Calm again: the wind eases to rest (this tree's responses are 14 seconds); long after it
        // has, a wind again, and a tree that first draws in it.
        if (m_Frame == 900) m_Again = Zone("Again", null, WindZoneMode.Directional, 0.5f, Quaternion.Euler(0f, -45f, 0f), Vector3.zero);
        // The wind goes once its direction and strength have arrived: the strength eases to rest
        // after the direction.
        if (m_Frame == 1250) m_Again.gameObject.SetActive(false);
        if (m_Frame == 1000)
            Tree("FieldLate", "FieldB", m_Field, 0, m_Dump, k_Names, 28, new Vector3(300f, 0f, 0f), Quaternion.identity, Vector3.one, MotionVectorGenerationMode.Object);
        // A spherical zone beside FieldNear (its wind toward the tree, stronger than the
        // directional one), moved out of every tree's reach later.
        if (m_Frame == 1010) m_Near = Zone("Near", null, WindZoneMode.Spherical, 1.2f, Quaternion.identity, new Vector3(397f, 0f, -2f));
        if (m_Frame == 1100) m_Near.transform.position = new Vector3(400f, 0f, 500f);
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
        bool record = Array.IndexOf(m_First ? k_RecordedFirst : m_Zones ? k_RecordedZones : k_Recorded, m_Frame) >= 0;
        string frame = "f" + m_Frame.ToString("000");
        if (record) Rec(frame + "/time", F(Time.time));
        foreach (var (name, camera, history, twin, names) in m_Trees)
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
            int current = names == k_Names ? Current : names.Length / 2;
            for (int i = 0; i < names.Length; i++)
            {
                if (i >= current && !history) continue;
                // Calm turns its first branch level off: those constants (and their history) are
                // never written (tests/projects/speedtree9wind).
                if (names == k_Names9 && (i % current == 5 || i % current == 6)) continue;
                var c = m_Read.GetPixel(i, 0);
                if (names[i].StartsWith("_ST_WindAnimation", StringComparison.Ordinal))
                {
                    // The wind leaves _ST_WindAnimation unwritten: before a tree draws with wind it
                    // reads the global (or material) value; once the wind's constants are bound,
                    // whatever the engine's buffer holds there (not repeatable), so only whether
                    // it is the global or material value is recorded. Not in the scenes of the
                    // other profiles, which set neither.
                    if (m_First || m_Zones) continue;
                    if (m_Frame < 10) Rec(frame + "/" + name + "/" + names[i], F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a));
                    else Rec(frame + "/" + name + "/" + names[i] + "/globalOrMaterial",
                        c == new Color(1f, 2f, 3f, 4f) || c == new Color(5f, 6f, 7f, 8f) ? "true" : "false");
                    continue;
                }
                Rec(frame + "/" + name + "/" + names[i], F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a));
            }
        }
        if (m_Frame < (m_First ? LastFrameFirst : m_Zones ? LastFrameZones : LastFrame)) return;
        string dir = Environment.GetEnvironmentVariable("SPEEDTREE_PROBE_OUT");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
        EditorApplication.Exit(0);
    }
}
