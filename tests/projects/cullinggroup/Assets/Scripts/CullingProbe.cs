using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// CullingGroup: when the groups are evaluated and their callbacks sent within a frame (against
/// Update, LateUpdate, the cameras' culling messages and the end of the frame), the first frame,
/// the visibility of spheres against the target camera's frustum (edges, near and far planes,
/// behind an occluder with no occlusion data), distance bands at exact boundaries, a disabled
/// group, a null, disabled or second target camera, a manual Camera.Render, the sphere array read
/// by reference, the sphere count, EraseSwapBack, QueryIndices, and the API's errors (also after
/// Dispose), rejected distances, a null reference transform, the default reference point, and the
/// order of the groups' callbacks after a disposal. Writes CULLINGGROUP_PROBE_OUT after <see cref="EndFrame"/> frames.
/// </summary>
public class CullingProbe : MonoBehaviour
{
    const int EndFrame = 24;

    /// <summary>Quits the Editor's Play mode once the record is written (set by the Editor's runner).</summary>
    public static Action finished;

    Camera m_Main, m_Second, m_Disabled;
    Transform m_Reference, m_Witness;
    CullingGroup m_A, m_B, m_C, m_D, m_E, m_F, m_G, m_H, m_I, m_J;
    BoundingSphere[] m_SpheresA;
    readonly BoundingSphere[] m_SpheresB = { new BoundingSphere(new Vector3(0, 0, 10), 1), new BoundingSphere(new Vector3(0, 0, -10), 1) };
    readonly BoundingSphere[] m_SpheresC = { new BoundingSphere(new Vector3(0, 0, 10), 1) };
    readonly BoundingSphere[] m_SpheresD = { new BoundingSphere(new Vector3(0, 0, 10), 1), new BoundingSphere(new Vector3(0, 0, -10), 1) };
    // Seen by the second camera (looking back, no distances): in view, crossing the far plane,
    // beyond it, in view far down, between the camera and the near plane.
    readonly BoundingSphere[] m_SpheresE =
    {
        new BoundingSphere(new Vector3(0, 0, 10), 1), new BoundingSphere(new Vector3(0, 0, -10), 1),
        new BoundingSphere(new Vector3(0, 0, -100.5f), 1), new BoundingSphere(new Vector3(0, 0, -150), 1),
        new BoundingSphere(new Vector3(0, -3, -30), 1), new BoundingSphere(new Vector3(0, 0, -0.1f), 0.05f),
    };
    // At distance 7 from the origin: the band tells which distances were kept.
    readonly BoundingSphere[] m_SpheresF = { new BoundingSphere(new Vector3(0, 0, 8), 1) };
    readonly BoundingSphere[] m_SpheresG = { new BoundingSphere(new Vector3(0, 0, 8), 1) };
    readonly BoundingSphere[] m_SpheresH = { new BoundingSphere(new Vector3(0, 0, 8), 1) };
    readonly BoundingSphere[] m_SpheresI = { new BoundingSphere(new Vector3(0, 0, 10), 1) };
    readonly BoundingSphere[] m_SpheresJ = { new BoundingSphere(new Vector3(0, 0, 8.5f), 1) };
    int m_CountA;
    bool m_Done, m_Logging;

    static string Current() => ProbeLog.Q(Camera.current != null ? Camera.current.name : null);

    static string Ints(int[] values, int count)
    {
        var b = new StringBuilder("[");
        for (int i = 0; i < count && i < values.Length; i++) b.Append(i > 0 ? "," : "").Append(values[i]);
        return b.Append(']').ToString();
    }

    CullingGroup.StateChanged Listener(string group) => e => ProbeLog.Add("event", "g", ProbeLog.Q(group), "cam", Current(),
        "i", e.index.ToString(), "vis", ProbeLog.B(e.isVisible), "was", ProbeLog.B(e.wasVisible),
        "becameVis", ProbeLog.B(e.hasBecomeVisible), "becameInvis", ProbeLog.B(e.hasBecomeInvisible),
        "cur", e.currentDistance.ToString(), "prev", e.previousDistance.ToString());

    static string Try(Func<string> action)
    {
        try { return action(); }
        catch (Exception e) { return ProbeLog.Q(e.GetType().FullName + ": " + e.Message); }
    }

    static void Api(string what, Func<string> action) => ProbeLog.Add("api", "what", ProbeLog.Q(what), "result", Try(action));

    static void Api(string what, Action action) => Api(what, () => { action(); return "\"ok\""; });

    Camera MakeCamera(string name, float yaw, float depth)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.transform.rotation = Quaternion.Euler(0, yaw, 0);
        camera.fieldOfView = 60;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 100;
        camera.depth = depth;
        camera.clearFlags = depth == 0 ? CameraClearFlags.SolidColor : CameraClearFlags.Depth;
        camera.backgroundColor = Color.black;
        camera.gameObject.AddComponent<CameraHooks>();
        return camera;
    }

    void Awake()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        Time.captureFramerate = 60;
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ProbeDriver")).FirstOrDefault(t => t != null);
        type?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        Application.logMessageReceived += (message, stack, kind) =>
        {
            if (m_Logging) ProbeLog.Add("log", "type", ProbeLog.Q(kind.ToString()), "msg", ProbeLog.Q(message));
        };
        Camera.onPreCull += c => ProbeLog.Add("onPreCull", "cam", ProbeLog.Q(c.name));
        Camera.onPreRender += c => ProbeLog.Add("onPreRender", "cam", ProbeLog.Q(c.name));
        Camera.onPostRender += c => ProbeLog.Add("onPostRender", "cam", ProbeLog.Q(c.name));

        m_Main = MakeCamera("Main", 0, 0);
        m_Second = MakeCamera("Second", 180, 1);
        m_Disabled = MakeCamera("Disabled", 0, 2);
        m_Disabled.enabled = false;
        m_Disabled.transform.position = new Vector3(0, 0, -20);
        m_Reference = new GameObject("Reference").transform;

        var witness = GameObject.CreatePrimitive(PrimitiveType.Cube);
        witness.name = "Witness";
        witness.transform.position = new Vector3(-2, 0, 10);
        witness.AddComponent<Witness>();
        m_Witness = witness.transform;
        // A large occluder in front of sphere 5 (there is no occlusion data).
        var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occluder.name = "Occluder";
        occluder.transform.position = new Vector3(3, 0, 8);
        occluder.transform.localScale = new Vector3(3, 3, 0.2f);

        ApiChecks();

        m_SpheresA = new[]
        {
            new BoundingSphere(new Vector3(0, 0, 10), 1),      // 0 in view
            new BoundingSphere(new Vector3(0, 0, -10), 1),     // 1 behind the camera
            new BoundingSphere(new Vector3(0, 0, 150), 1),     // 2 beyond the far plane
            new BoundingSphere(new Vector3(11.5f, 0, 10), 1),  // 3 crossing the right plane
            new BoundingSphere(new Vector3(11.9f, 0, 10), 1),  // 4 just outside the right plane
            new BoundingSphere(new Vector3(3, 0, 15), 0.5f),   // 5 behind the occluder
            new BoundingSphere(new Vector3(0, 1, 10), 0),      // 6 radius 0 in view
            new BoundingSphere(new Vector3(0, 0, 0.1f), 0.05f), // 7 between the camera and the near plane
            new BoundingSphere(new Vector3(0, 0, 100.5f), 1),  // 8 crossing the far plane
            new BoundingSphere(new Vector3(0, -3, 30), 1),     // 9 in view
            new BoundingSphere(new Vector3(0, 0, 6), 1),       // 10 surface exactly at 5
            new BoundingSphere(new Vector3(0, 0, 5), 0),       // 11 center exactly at 5
            new BoundingSphere(new Vector3(0, 0, 5.5f), 1),    // 12 center 5.5, surface 4.5
            new BoundingSphere(new Vector3(0, 0, 10), 0),      // 13 exactly at 10
            new BoundingSphere(new Vector3(0, 0, 20), 0),      // 14 exactly at the last distance, 20
            new BoundingSphere(new Vector3(0, 0, 0), 2),       // 15 around the camera
            new BoundingSphere(new Vector3(0, 0, 25), 10),     // 16 center 25, surface 15
        };
        m_CountA = m_SpheresA.Length;
        m_A = new CullingGroup();
        m_A.onStateChanged = Listener("A");
        m_A.targetCamera = m_Main;
        m_A.SetBoundingSpheres(m_SpheresA);
        m_A.SetBoundingDistances(new float[] { 5, 10, 20 });
        m_A.SetDistanceReferencePoint(Vector3.zero);

        // No target camera and no distances.
        m_B = new CullingGroup();
        m_B.onStateChanged = Listener("B");
        m_B.SetBoundingSpheres(m_SpheresB);

        // Disabled from the start.
        m_C = new CullingGroup();
        m_C.enabled = false;
        m_C.onStateChanged = Listener("C");
        m_C.targetCamera = m_Main;
        m_C.SetBoundingSpheres(m_SpheresC);
        m_C.SetBoundingDistances(new float[] { 5 });

        // A disabled camera, rendered by hand once.
        m_D = new CullingGroup();
        m_D.onStateChanged = Listener("D");
        m_D.targetCamera = m_Disabled;
        m_D.SetBoundingSpheres(m_SpheresD);
        m_D.SetBoundingDistances(new float[] { 5 });
        m_D.SetDistanceReferencePoint(m_Disabled.transform);

        // The second screen camera, looking back.
        m_E = new CullingGroup();
        m_E.onStateChanged = Listener("E");
        m_E.targetCamera = m_Second;
        m_E.SetBoundingSpheres(m_SpheresE);

        // Rejected distances: unsorted, then too many, after valid ones.
        m_F = new CullingGroup();
        m_F.onStateChanged = Listener("F");
        m_F.targetCamera = m_Main;
        m_F.SetBoundingSpheres(m_SpheresF);
        m_F.SetBoundingDistances(new float[] { 5 });
        m_F.SetBoundingDistances(new float[] { 10, 5 });
        m_G = new CullingGroup();
        m_G.onStateChanged = Listener("G");
        m_G.targetCamera = m_Main;
        m_G.SetBoundingSpheres(m_SpheresG);
        m_G.SetBoundingDistances(new float[] { 5 });
        m_G.SetBoundingDistances(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        // A null reference transform after a reference point.
        m_H = new CullingGroup();
        m_H.onStateChanged = Listener("H");
        m_H.targetCamera = m_Main;
        m_H.SetBoundingSpheres(m_SpheresH);
        m_H.SetBoundingDistances(new float[] { 5, 50, 500 });
        m_H.SetDistanceReferencePoint(new Vector3(0, 0, -100));
        m_H.SetDistanceReferencePoint((Transform)null);

        // No reference point, a camera away from the origin.
        m_I = new CullingGroup();
        m_I.onStateChanged = Listener("I");
        m_I.targetCamera = m_Disabled;
        m_I.SetBoundingSpheres(m_SpheresI);
        m_I.SetBoundingDistances(new float[] { 15, 40 });
        // Eight distances (more than the supported seven), a sphere at 7.5.
        m_J = new CullingGroup();
        m_J.onStateChanged = Listener("J");
        m_J.targetCamera = m_Main;
        m_J.SetBoundingSpheres(m_SpheresJ);
        m_J.SetBoundingDistances(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        Snapshot("awake");
        StartCoroutine(EndOfFrames());
    }

    void Start() => Snapshot("start");

    /// <summary>Values and errors of the API outside the frame's evaluation.</summary>
    void ApiChecks()
    {
        m_Logging = true;
        var g = new CullingGroup();
        Api("enabled", () => ProbeLog.B(g.enabled));
        Api("targetCamera", () => ProbeLog.B(g.targetCamera == null));
        Api("onStateChanged", () => ProbeLog.B(g.onStateChanged == null));
        Api("empty IsVisible(0)", () => ProbeLog.B(g.IsVisible(0)));
        Api("empty GetDistance(0)", () => g.GetDistance(0).ToString());
        Api("empty QueryIndices(true)", () => g.QueryIndices(true, new int[4], 0).ToString());
        Api("empty QueryIndices(false)", () => g.QueryIndices(false, new int[4], 0).ToString());
        Api("empty SetBoundingSphereCount(1)", () => g.SetBoundingSphereCount(1));
        Api("empty count 1 QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        Api("SetBoundingSpheres(null)", () => g.SetBoundingSpheres(null));
        Api("null QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        var four = new BoundingSphere[4];
        for (int i = 0; i < four.Length; i++) four[i] = new BoundingSphere(new Vector3(i, 0, 10), 1);
        Api("SetBoundingSpheres(4)", () => g.SetBoundingSpheres(four));
        Api("fresh IsVisible(0)", () => ProbeLog.B(g.IsVisible(0)));
        Api("fresh GetDistance(0)", () => g.GetDistance(0).ToString());
        Api("fresh QueryIndices(true)", () => g.QueryIndices(true, new int[8], 0).ToString());
        Api("fresh QueryIndices(false)", () => { var r = new int[8]; int n = g.QueryIndices(false, r, 0); return "\"" + n + " " + Ints(r, n) + "\""; });
        Api("fresh QueryIndices(0)", () => { var r = new int[8]; int n = g.QueryIndices(0, r, 0); return "\"" + n + " " + Ints(r, n) + "\""; });
        Api("fresh QueryIndices(1)", () => g.QueryIndices(1, new int[8], 0).ToString());
        Api("fresh QueryIndices(false, 0)", () => g.QueryIndices(false, 0, new int[8], 0).ToString());
        Api("IsVisible(3)", () => ProbeLog.B(g.IsVisible(3)));
        Api("IsVisible(4)", () => ProbeLog.B(g.IsVisible(4)));
        Api("IsVisible(-1)", () => ProbeLog.B(g.IsVisible(-1)));
        Api("GetDistance(4)", () => g.GetDistance(4).ToString());
        Api("GetDistance(-1)", () => g.GetDistance(-1).ToString());
        Api("QueryIndices(null)", () => g.QueryIndices(false, null, 0).ToString());
        Api("QueryIndices(small)", () => { var r = new int[2]; int n = g.QueryIndices(false, r, 0); return "\"" + n + " " + Ints(r, n) + "\""; });
        Api("QueryIndices(first 2)", () => { var r = new int[8]; int n = g.QueryIndices(false, r, 2); return "\"" + n + " " + Ints(r, n) + "\""; });
        Api("QueryIndices(first 4)", () => g.QueryIndices(false, new int[8], 4).ToString());
        Api("QueryIndices(first 9)", () => g.QueryIndices(false, new int[8], 9).ToString());
        Api("QueryIndices(first -1)", () => g.QueryIndices(false, new int[8], -1).ToString());
        Api("QueryIndices(empty result)", () => g.QueryIndices(false, new int[0], 0).ToString());
        Api("SetBoundingSphereCount(5)", () => g.SetBoundingSphereCount(5));
        Api("count 5 QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        Api("SetBoundingSphereCount(-1)", () => g.SetBoundingSphereCount(-1));
        Api("count -1 QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        Api("SetBoundingSphereCount(0)", () => g.SetBoundingSphereCount(0));
        Api("count 0 QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        Api("SetBoundingSphereCount(2)", () => g.SetBoundingSphereCount(2));
        Api("count 2 IsVisible(3)", () => ProbeLog.B(g.IsVisible(3)));
        Api("count 2 QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        Api("SetBoundingSphereCount(4)", () => g.SetBoundingSphereCount(4));
        Api("SetBoundingDistances(null)", () => g.SetBoundingDistances(null));
        Api("SetBoundingDistances(empty)", () => g.SetBoundingDistances(new float[0]));
        Api("SetBoundingDistances(200)", () => g.SetBoundingDistances(Enumerable.Range(1, 200).Select(i => (float)i).ToArray()));
        Api("SetBoundingDistances(unsorted)", () => g.SetBoundingDistances(new float[] { 10, 5 }));
        Api("SetDistanceReferencePoint(null)", () => g.SetDistanceReferencePoint((Transform)null));
        Api("EraseSwapBack(9)", () => g.EraseSwapBack(9));
        Api("EraseSwapBack(-1)", () => g.EraseSwapBack(-1));
        Api("EraseSwapBack(0)", () => g.EraseSwapBack(0));
        Api("erased QueryIndices(false)", () => g.QueryIndices(false, new int[8], 0).ToString());
        Api("erased array", () => ProbeLog.Q(string.Join(";", four.Select(s => s.position.x.ToString()))));
        int size = 4;
        var ints = new[] { 10, 11, 12, 13 };
        Api("static EraseSwapBack(1)", () => { CullingGroup.EraseSwapBack(1, ints, ref size); return "\"" + size + " " + Ints(ints, 4) + "\""; });
        Api("static EraseSwapBack(5)", () => { CullingGroup.EraseSwapBack(5, ints, ref size); return "\"" + size + " " + Ints(ints, 4) + "\""; });
        Api("static EraseSwapBack size", () => size.ToString());
        Api("enabled=false", () => g.enabled = false);
        Api("disabled enabled", () => ProbeLog.B(g.enabled));
        Api("targetCamera=Main", () => g.targetCamera = Camera.allCameras.FirstOrDefault());
        Api("Dispose", () => g.Dispose());
        Api("Dispose again", () => g.Dispose());
        Api("disposed enabled", () => ProbeLog.B(g.enabled));
        Api("disposed enabled=true", () => g.enabled = true);
        Api("disposed targetCamera", () => ProbeLog.B(g.targetCamera == null));
        Api("disposed onStateChanged", () => ProbeLog.B(g.onStateChanged == null));
        Api("disposed onStateChanged=", () => g.onStateChanged = e => { });
        Api("disposed SetBoundingSpheres", () => g.SetBoundingSpheres(four));
        Api("disposed SetBoundingSphereCount", () => g.SetBoundingSphereCount(1));
        Api("disposed SetBoundingDistances", () => g.SetBoundingDistances(new float[] { 1 }));
        Api("disposed SetDistanceReferencePoint", () => g.SetDistanceReferencePoint(Vector3.one));
        Api("disposed IsVisible(0)", () => ProbeLog.B(g.IsVisible(0)));
        Api("disposed GetDistance(0)", () => g.GetDistance(0).ToString());
        Api("disposed QueryIndices", () => g.QueryIndices(true, new int[4], 0).ToString());
        Api("disposed EraseSwapBack", () => g.EraseSwapBack(0));
        m_Logging = false;
    }

    /// <summary>Every group's state as IsVisible and GetDistance give it (v: 1 visible, 0 not).</summary>
    void Snapshot(string where)
    {
        foreach (var (name, group, count) in new[] { ("A", m_A, m_CountA), ("B", m_B, 2), ("C", m_C, 1), ("D", m_D, 2), ("E", m_E, m_SpheresE.Length),
            ("F", m_F, 1), ("G", m_G, 1), ("H", m_H, 1), ("I", m_I, 1), ("J", m_J, 1) })
        {
            if (group == null) continue;
            var visible = new StringBuilder();
            var distance = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                // Spheres past the group's count throw: 'x'.
                try { visible.Append(group.IsVisible(i) ? '1' : '0'); }
                catch (IndexOutOfRangeException) { visible.Append('x'); }
                try { distance.Append(group.GetDistance(i)); }
                catch (IndexOutOfRangeException) { distance.Append('x'); }
                distance.Append(i + 1 < count ? "," : "");
            }
            ProbeLog.Add(where, "g", ProbeLog.Q(name), "vis", ProbeLog.Q(visible.ToString()), "dist", ProbeLog.Q(distance.ToString()));
        }
    }

    void Queries()
    {
        var r = new int[32];
        string Q(int n) => "\"" + n + " " + Ints(r, n) + "\"";
        ProbeLog.Add("query", "what", "\"visible\"", "r", Q(m_A.QueryIndices(true, r, 0)));
        ProbeLog.Add("query", "what", "\"invisible\"", "r", Q(m_A.QueryIndices(false, r, 0)));
        ProbeLog.Add("query", "what", "\"visible from 5\"", "r", Q(m_A.QueryIndices(true, r, 5)));
        for (int d = 0; d <= 4; d++)
        {
            ProbeLog.Add("query", "what", ProbeLog.Q("distance " + d), "r", Q(m_A.QueryIndices(d, r, 0)));
            ProbeLog.Add("query", "what", ProbeLog.Q("visible distance " + d), "r", Q(m_A.QueryIndices(true, d, r, 0)));
            ProbeLog.Add("query", "what", ProbeLog.Q("invisible distance " + d), "r", Q(m_A.QueryIndices(false, d, r, 0)));
        }
        var small = new int[3];
        int n3 = m_A.QueryIndices(false, small, 0);
        ProbeLog.Add("query", "what", "\"invisible into 3\"", "r", "\"" + n3 + " " + Ints(small, n3) + "\"");
    }

    void Update()
    {
        ProbeLog.frame++;
        int n = ProbeLog.frame;
        if (n > EndFrame)
        {
            if (m_Done) return;
            m_Done = true;
            ProbeLog.Add("end");
            ProbeLog.Write();
            finished?.Invoke();
            return;
        }
        ProbeLog.Add("Update");
        Snapshot("update");
        if (n == 0)
            ProbeLog.Add("camera", "aspect", ProbeLog.F(m_Main.aspect), "w", m_Main.pixelWidth.ToString(), "h", m_Main.pixelHeight.ToString());
        switch (n)
        {
            case 2:
                // The array is read by reference: no call after the change.
                m_SpheresA[0].position = new Vector3(0, 0, -10);
                m_Witness.position = new Vector3(-50, 0, 10);
                ProbeLog.Add("step", "what", "\"sphere 0 behind (array only), witness away\"");
                break;
            case 3:
                Queries();
                break;
            case 4:
                m_A.SetBoundingSphereCount(10);
                m_Witness.position = new Vector3(-2, 0, 10);
                ProbeLog.Add("step", "what", "\"A count 10, witness back\"");
                break;
            case 5:
                m_SpheresA[12].position = new Vector3(0, 0, 50);
                m_SpheresA[10].position = new Vector3(0, 0, -6);
                m_A.SetBoundingSphereCount(m_CountA);
                ProbeLog.Add("step", "what", "\"spheres 10 and 12 moved, A count 17\"");
                break;
            case 6:
                m_C.enabled = true;
                ProbeLog.Add("step", "what", "\"C enabled\"");
                break;
            case 7:
                m_A.SetDistanceReferencePoint(m_Reference);
                ProbeLog.Add("step", "what", "\"A reference transform\"");
                break;
            case 8:
                ProbeLog.Add("step", "what", "\"Disabled.Render begin\"");
                m_Disabled.Render();
                ProbeLog.Add("step", "what", "\"Disabled.Render end\"");
                Snapshot("rendered");
                break;
            case 9:
                m_A.enabled = false;
                m_SpheresA[0].position = new Vector3(0, 0, 10);
                ProbeLog.Add("step", "what", "\"A disabled, sphere 0 in view\"", "enabled", ProbeLog.B(m_A.enabled));
                break;
            case 11:
                m_A.enabled = true;
                ProbeLog.Add("step", "what", "\"A enabled\"");
                break;
            case 12:
                m_A.targetCamera = null;
                m_SpheresA[0].position = new Vector3(0, 0, -10);
                ProbeLog.Add("step", "what", "\"A target null, sphere 0 behind\"");
                break;
            case 14:
                m_A.targetCamera = m_Main;
                ProbeLog.Add("step", "what", "\"A target Main\"");
                break;
            case 16:
                m_A.SetBoundingDistances(new float[] { 3 });
                ProbeLog.Add("step", "what", "\"A distances {3}\"");
                break;
            case 17:
                m_A.EraseSwapBack(1);
                CullingGroup.EraseSwapBack(1, m_SpheresA, ref m_CountA);
                ProbeLog.Add("step", "what", "\"A EraseSwapBack(1)\"", "count", m_CountA.ToString());
                Queries();
                break;
            case 18:
                m_SpheresA = new[] { new BoundingSphere(new Vector3(0, 0, 10), 1), new BoundingSphere(new Vector3(0, 0, -10), 1), new BoundingSphere(new Vector3(0, 0, 2), 0.5f) };
                m_CountA = 3;
                m_A.SetBoundingSpheres(m_SpheresA);
                ProbeLog.Add("step", "what", "\"A new array of 3\"");
                Snapshot("replaced");
                break;
            case 19:
                Queries();
                break;
            case 20:
                m_B.Dispose();
                m_B = null;
                m_E.enabled = false;
                m_SpheresE[0].position = new Vector3(0, 0, -10);
                ProbeLog.Add("step", "what", "\"B disposed, E disabled\"");
                break;
            case 21:
                // The order of the groups' callbacks after one made before them is disposed.
                m_F.Dispose();
                m_F = null;
                m_SpheresA[0].position = new Vector3(0, 0, 2);
                m_SpheresG[0].position = new Vector3(0, 0, 2.5f);
                m_SpheresH[0].position = new Vector3(0, 0, -50);
                m_SpheresJ[0].position = new Vector3(0, 0, 2.5f);
                ProbeLog.Add("step", "what", "\"F disposed; A, G, H and J spheres moved\"");
                break;
            case 22:
                m_E.enabled = true;
                ProbeLog.Add("step", "what", "\"E enabled\"");
                break;
        }
    }

    void LateUpdate()
    {
        int n = ProbeLog.frame;
        if (n > EndFrame) return;
        ProbeLog.Add("LateUpdate");
        // Changes made after LateUpdate's scripts: a transform reference point, a sphere.
        if (n == 7)
        {
            m_Reference.position = new Vector3(0, 0, 4);
            ProbeLog.Add("step", "what", "\"reference moved to z 4 in LateUpdate\"");
        }
        if (n == 15)
        {
            m_SpheresA[3].position = new Vector3(20, 0, 10);
            ProbeLog.Add("step", "what", "\"sphere 3 out of view in LateUpdate\"");
        }
        Snapshot("lateupdate");
    }

    IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            if (ProbeLog.frame <= EndFrame) ProbeLog.Add("EndOfFrame");
        }
    }

    void OnDestroy()
    {
        m_A?.Dispose();
        m_B?.Dispose();
        m_C?.Dispose();
        m_D?.Dispose();
        m_E?.Dispose();
        m_F?.Dispose();
        m_G?.Dispose();
        m_H?.Dispose();
        m_I?.Dispose();
        m_J?.Dispose();
    }
}
