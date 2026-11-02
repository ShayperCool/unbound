using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Pose, LazyLoadReference&lt;T&gt;, GameObject.SetGameObjectsActive, GameObject.InstantiateGameObjects
/// and OnDemandRendering. Each event is one JSON line with the frame (counted from the probe's
/// Start), the scenario, the object and the event; instance ids are written as the names of the
/// objects they belong to. Writes CORE_APIS_OUT and exits at the last frame.
/// </summary>
public class CoreProbe : MonoBehaviour
{
    public static bool Finished;
    /// <summary>Set by the windowed Editor's runner, which quits from Edit mode.</summary>
    public static Action finished;
    static string s_Scenario = "-";
    static int s_Frame0 = -1;
    static readonly StringBuilder s_Log = new StringBuilder();
    static readonly Dictionary<int, string> s_Names = new Dictionary<int, string>();

    /// <summary>The OnDemandRendering window: its first frame (Time.frameCount) and length.</summary>
    static int s_Window = -1;
    static int s_RenderedAtWindow;
    const int WindowFrames = 16;

    public static int Frame => s_Frame0 < 0 ? -1 : Time.frameCount - s_Frame0;

    public static void Record(string who, string message) =>
        s_Log.Append($"{{\"f\":{Frame},\"s\":\"{s_Scenario}\",\"who\":\"{P.Esc(who)}\",\"e\":\"{P.Esc(Names(message))}\"}}\n");

    static bool InWindow => s_Window >= 0 && Time.frameCount - s_Window <= WindowFrames;

    /// <summary>A render-window event: the frame inside the window, frameCount modulo 6 (so the
    /// render pattern is visible for intervals 1, 2 and 3) and willCurrentFrameRender.</summary>
    public static void RenderEvent(string who, string e)
    {
        if (!InWindow) return;
        s_Log.Append($"{{\"w\":{Time.frameCount - s_Window},\"m6\":{Time.frameCount % 6},\"i\":{OnDemandRendering.renderFrameInterval}," +
                     $"\"rendered\":{Time.renderedFrameCount - s_RenderedAtWindow}," +
                     $"\"will\":{P.B(OnDemandRendering.willCurrentFrameRender)},\"who\":\"{P.Esc(who)}\",\"e\":\"{P.Esc(e)}\"}}\n");
    }

    static void Name(Object o, string name)
    {
        if (o != null) s_Names[o.GetInstanceID()] = name;
    }

    /// <summary>Instance ids in JsonUtility text, by name.</summary>
    static string JsonIds(string json) => System.Text.RegularExpressions.Regex.Replace(json, "(\"(?:m_)?[iI]nstanceID\":)(-?\\d+)",
        m => m.Groups[1].Value + "\"" + Id(int.Parse(m.Groups[2].Value)) + "\"");

    static string Id(int id) => id == 0 ? "0" : s_Names.TryGetValue(id, out var n) ? "#" + n : (id > 0 ? "#unknown+" : "#unknown-");

    /// <summary>Replaces every known instance id in a text by its object's name.</summary>
    static string Names(string text)
    {
        if (s_Names.Count == 0 || text.IndexOfAny("0123456789".ToCharArray()) < 0) return text;
        var b = new StringBuilder();
        for (int i = 0; i < text.Length;)
        {
            int j = i;
            if (text[j] == '-' && j + 1 < text.Length && char.IsDigit(text[j + 1])) j++;
            if (j < text.Length && char.IsDigit(text[j]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                int k = j;
                while (k < text.Length && char.IsDigit(text[k])) k++;
                if (int.TryParse(text.Substring(i, k - i), out var id) && s_Names.TryGetValue(id, out var name))
                {
                    b.Append('#').Append(name);
                    i = k;
                    continue;
                }
                b.Append(text, i, k - i);
                i = k;
                continue;
            }
            b.Append(text[i]);
            i++;
        }
        return b.ToString();
    }

    readonly Dictionary<int, Action> m_Steps = new Dictionary<int, Action>();
    GameObject m_A, m_A1, m_B, m_C, m_D, m_D1, m_E, m_F, m_Src, m_SrcOff;
    LazyHolder m_Holder;
    Scene m_Second, m_Gone;
    Camera m_ScreenCam, m_RtCam;

    void Awake()
    {
        Application.logMessageReceived += (condition, stack, type) => Record("log", type + ": " + condition);
    }

    void Start()
    {
        s_Frame0 = Time.frameCount;
        m_A = GameObject.Find("A");
        m_A1 = m_A.transform.Find("A1").gameObject;
        m_C = GameObject.Find("C");
        m_E = GameObject.Find("E");
        m_F = GameObject.Find("F");
        m_Src = GameObject.Find("SrcParent").transform.Find("Src").gameObject;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.gameObject.scene != gameObject.scene) continue;
            if (t.name == "B") m_B = t.gameObject;
            if (t.name == "D") m_D = t.gameObject;
            if (t.name == "SrcOff") m_SrcOff = t.gameObject;
        }
        m_D1 = m_D.transform.Find("D1").gameObject;
        m_Holder = FindAnyObjectByType<LazyHolder>();
        m_ScreenCam = GameObject.Find("ScreenCam").GetComponent<Camera>();
        m_RtCam = GameObject.Find("RtCam").GetComponent<Camera>();
        m_RtCam.targetTexture = new RenderTexture(64, 64, 24);
        foreach (var go in new[] { m_A, m_A1, m_B, m_C, m_D, m_D1, m_E, m_F, m_Src, m_SrcOff, m_Holder.gameObject })
        {
            Name(go, go.name);
            Name(go.transform, go.name + ".transform");
            Name(go.GetComponent<Msg>(), go.name + ".Msg");
        }
        m_Second = SceneManager.CreateScene("Second");
        m_Gone = SceneManager.CreateScene("Gone");

        Camera.onPreCull += c => { if (Ours(c)) RenderEvent(c.name, "Camera.onPreCull"); };
        Camera.onPreRender += c => { if (Ours(c)) RenderEvent(c.name, "Camera.onPreRender"); };
        Camera.onPostRender += c => { if (Ours(c)) RenderEvent(c.name, "Camera.onPostRender"); };
        Canvas.willRenderCanvases += () => RenderEvent("Canvas", "willRenderCanvases");
        StartCoroutine(EndOfFrames());

        s_Scenario = "pose";
        Poses();
        s_Scenario = "lazy";
        Lazy();
        Schedule();
    }

    static bool Ours(Camera c) => c != null && (c.name == "ScreenCam" || c.name == "RtCam");

    void At(int frame, string scenario, Action action) => m_Steps[frame] = () =>
    {
        s_Scenario = scenario;
        try { action(); }
        catch (Exception e) { Record("probe", "step threw " + P.Ex(e)); }
    };

    void Update()
    {
        if (m_Steps.TryGetValue(Frame, out var step)) step();
        if (s_Window < 0 && Frame >= 16 && Time.frameCount % 6 == 1)
        {
            s_Window = Time.frameCount;
            s_RenderedAtWindow = Time.renderedFrameCount;
            s_Scenario = "ondemand";
            OnDemandRendering.renderFrameInterval = 3;
        }
        if (s_Window >= 0)
        {
            int w = Time.frameCount - s_Window;
            if (w == 7) OnDemandRendering.renderFrameInterval = 2;
            if (w == 12) OnDemandRendering.renderFrameInterval = 1;
            if (w > WindowFrames + 1) Finish();
        }
        RenderEvent("probe", "Update");
    }

    void LateUpdate() => RenderEvent("probe", "LateUpdate");

    void OnGUI()
    {
        if (Event.current.type == EventType.Layout) RenderEvent("probe", "OnGUI Layout");
        if (Event.current.type == EventType.Repaint) RenderEvent("probe", "OnGUI Repaint");
    }

    IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            RenderEvent("probe", "WaitForEndOfFrame");
            // A change at the end of a frame: does the next frame render with it?
            if (s_Window >= 0 && Time.frameCount - s_Window == 13)
            {
                OnDemandRendering.renderFrameInterval = 2;
                RenderEvent("probe", "interval 2 at end of frame");
            }
        }
    }

    void Schedule()
    {
        At(1, "active.batch-off", () => Active(new[] { m_A, m_B, m_C }, false));
        At(2, "active.batch-on", () => Active(new[] { m_C, m_A, m_B }, true));
        At(3, "active.duplicates", () =>
        {
            Active(new[] { m_A, m_A }, false);
            Active(new[] { m_A, m_A }, true);
        });
        At(4, "active.invalid", () =>
        {
            Ids(new[] { 0, int.MaxValue - 1, m_C.transform.GetInstanceID(), m_C.GetComponent<Msg>().GetInstanceID(), m_A.GetInstanceID() }, false);
            Active(new[] { m_A }, true);
            var temp = new GameObject("Temp");
            temp.AddComponent<Msg>();
            Name(temp, "Temp");
            int id = temp.GetInstanceID();
            DestroyImmediate(temp);
            Ids(new[] { id, m_C.GetInstanceID() }, false);
            Active(new[] { m_C }, true);
        });
        At(5, "active.hierarchy", () =>
        {
            Active(new[] { m_A1, m_A }, false);
            Active(new[] { m_A, m_A1 }, true);
            Active(new[] { m_A, m_A1 }, false);
            Active(new[] { m_A1, m_A }, true);
            Active(new[] { m_D1 }, false);
            Active(new[] { m_D1 }, true);
            Active(new[] { m_D, m_D1 }, true);
            Active(new[] { m_D }, false);
        });
        At(6, "active.reentrant", () =>
        {
            Active(new[] { m_E, m_F }, false);
            Active(new[] { m_F, m_E }, true);
        });
        At(7, "active.nativearray", () =>
        {
            Try("default", () => GameObject.SetGameObjectsActive(default(NativeArray<int>), false));
            var empty = new NativeArray<int>(0, Allocator.Temp);
            Try("empty", () => GameObject.SetGameObjectsActive(empty, false));
            empty.Dispose();
            Try("empty span", () => GameObject.SetGameObjectsActive(ReadOnlySpan<int>.Empty, false));
            var ids = new NativeArray<int>(new[] { m_C.GetInstanceID(), m_A.GetInstanceID() }, Allocator.Persistent);
            Try("off", () => GameObject.SetGameObjectsActive(ids, false));
            State();
            Try("on", () => GameObject.SetGameObjectsActive(ids, true));
            State();
            ids.Dispose();
            Try("disposed", () => GameObject.SetGameObjectsActive(ids, false));
            State();
        });

        At(9, "instantiate.scene", () => Instantiate("copies", m_Src.GetInstanceID(), 3, default));
        At(10, "instantiate.prefab", () => Instantiate("item", Resources.Load<GameObject>("Item").GetInstanceID(), 2, m_Second));
        At(11, "instantiate.inactive", () =>
        {
            var copies = Instantiate("off", m_SrcOff.GetInstanceID(), 1, default);
            if (copies != null && copies[0] != null)
            {
                Record("probe", "activate the copy");
                copies[0].SetActive(true);
            }
            SceneManager.UnloadSceneAsync(m_Gone);
        });
        At(13, "instantiate.errors", () =>
        {
            Make(0, 0, out var a, out var b, created: false);
            Try("count 0 uninitialized", () => GameObject.InstantiateGameObjects(m_Src.GetInstanceID(), 0, a, b));
            Make(5, 5, out a, out b);
            Try("count 0 length 5", () => GameObject.InstantiateGameObjects(m_Src.GetInstanceID(), 0, a, b));
            Try("count 2 length 5", () => GameObject.InstantiateGameObjects(m_Src.GetInstanceID(), 2, a, b));
            Dispose(a, b);
            Make(2, 3, out a, out b);
            Try("lengths 2 and 3", () => GameObject.InstantiateGameObjects(m_Src.GetInstanceID(), 2, a, b));
            Dispose(a, b);
            Make(0, 0, out a, out b);
            Try("count -1", () => GameObject.InstantiateGameObjects(m_Src.GetInstanceID(), -1, a, b));
            Dispose(a, b);
            Instantiate("source 0", 0, 1, default);
            Instantiate("source missing", int.MaxValue - 1, 1, default);
            Instantiate("source transform", m_Src.transform.GetInstanceID(), 1, default);
            Instantiate("source component", m_Src.GetComponent<Msg>().GetInstanceID(), 1, default);
            Instantiate("source material", m_Holder.mat.instanceID, 1, default);
            var temp = new GameObject("Temp2");
            Name(temp, "Temp2");
            int id = temp.GetInstanceID();
            DestroyImmediate(temp);
            Instantiate("source destroyed", id, 1, default);
            Record("probe", $"gone scene valid={P.B(m_Gone.IsValid())} loaded={P.B(m_Gone.isLoaded)}");
            Instantiate("unloaded scene", m_Src.GetInstanceID(), 1, m_Gone);
        });
        At(15, "ondemand.settings", RenderSettingsCases);
    }

    // ---------------------------------------------------------------- Pose

    static readonly Quaternion[] s_Rotations =
    {
        Quaternion.identity,
        new Quaternion(0.1f, 0.2f, 0.3f, 0.9273618f),
        new Quaternion(-0.5f, 0.5f, -0.5f, -0.5f),
        new Quaternion(1f, 2f, 3f, 4f),
        new Quaternion(0f, 0f, 0f, 0f),
        new Quaternion(0.70710677f, 0f, 0f, 0.70710677f),
        new Quaternion(-0.0123f, 0.9876f, 0.1357f, -0.0789f),
    };

    static readonly Vector3[] s_Positions =
    {
        Vector3.zero,
        new Vector3(1f, 2f, 3f),
        new Vector3(-1.5e-3f, 1e5f, 3.25f),
        new Vector3(0.1f, -0.2f, 0.3f),
        new Vector3(123456.7f, -0.000321f, 7f),
        new Vector3(-0f, 0f, -0f),
        new Vector3(1e-30f, -3.4e37f, 2.5f),
    };

    void Poses()
    {
        var poses = new List<Pose>();
        for (int i = 0; i < s_Rotations.Length; i++) poses.Add(new Pose(s_Positions[i], s_Rotations[i]));
        Record("pose", "identity " + P.PB(Pose.identity) + " default " + P.PB(default(Pose)));
        Record("pose", "identity==default " + P.B(Pose.identity == default(Pose)) + " identity==identity " + P.B(Pose.identity == Pose.identity));
        for (int i = 0; i < poses.Count; i++)
        {
            var p = poses[i];
            Record("pose" + i, "fields " + P.PB(p));
            Record("pose" + i, "forward " + P.VB(p.forward) + " right " + P.VB(p.right) + " up " + P.VB(p.up));
            Record("pose" + i, "ToString " + p.ToString() + " | F1 " + p.ToString("F1") + " | G9 " + p.ToString("G9") +
                               " | empty " + p.ToString("") + " | null " + p.ToString(null));
            Record("pose" + i, "hash " + p.GetHashCode());
            for (int j = 0; j < poses.Count; j++)
                Record("pose" + i, "by pose" + j + " " + P.PB(p.GetTransformedBy(poses[j])));
        }

        var parent = new GameObject("PoseParent").transform;
        parent.localPosition = new Vector3(3f, -1f, 2f);
        parent.localRotation = new Quaternion(0.2f, -0.3f, 0.1f, 0.9273618f);
        parent.localScale = new Vector3(2f, 0.5f, -1.5f);
        var lhs = new GameObject("PoseLhs").transform;
        lhs.SetParent(parent, false);
        lhs.localPosition = new Vector3(0.25f, 1.5f, -4f);
        lhs.localRotation = new Quaternion(-0.1f, 0.4f, 0.2f, 0.8888194f);
        lhs.localScale = new Vector3(1f, 3f, 0.75f);
        Record("pose", "lhs transform position " + P.VB(lhs.position) + " rotation " + P.QB(lhs.rotation));
        for (int i = 0; i < poses.Count; i++)
        {
            Record("pose" + i, "by transform " + P.PB(poses[i].GetTransformedBy(lhs)));
            Record("pose" + i, "by root " + P.PB(poses[i].GetTransformedBy(parent)));
        }
        Try("by null transform", () => Record("pose", P.PB(Pose.identity.GetTransformedBy((Transform)null))));
        Destroy(parent.gameObject);

        var a = new Pose(new Vector3(1f, 2f, 3f), new Quaternion(0.1f, 0.2f, 0.3f, 0.9273618f));
        var cases = new (string Name, Pose Other)[]
        {
            ("same", a),
            ("position+1e-6", new Pose(a.position + new Vector3(1e-6f, 0f, 0f), a.rotation)),
            ("position+1e-3", new Pose(a.position + new Vector3(1e-3f, 0f, 0f), a.rotation)),
            ("rotation.w+1e-7", new Pose(a.position, new Quaternion(a.rotation.x, a.rotation.y, a.rotation.z, a.rotation.w + 1e-7f))),
            ("negated rotation", new Pose(a.position, new Quaternion(-a.rotation.x, -a.rotation.y, -a.rotation.z, -a.rotation.w))),
            ("negative zero", new Pose(new Vector3(-0f, 0f, 0f), Quaternion.identity)),
            ("nan", new Pose(new Vector3(float.NaN, 0f, 0f), Quaternion.identity)),
        };
        foreach (var (name, other) in cases)
        {
            var self = name == "negative zero" ? new Pose(Vector3.zero, Quaternion.identity) : name == "nan" ? other : a;
            Record("pose.eq", $"{name} == {P.B(self == other)} != {P.B(self != other)} Equals {P.B(self.Equals(other))} " +
                              $"Equals(object) {P.B(self.Equals((object)other))} hashes {self.GetHashCode()} {other.GetHashCode()}");
        }
        Record("pose.eq", "Equals(null) " + P.B(a.Equals(null)) + " Equals(string) " + P.B(a.Equals("pose")) +
                          " Equals(boxed position) " + P.B(a.Equals((object)a.position)));
        IEquatable<Pose> equatable = a;
        Record("pose.eq", "IEquatable " + P.B(equatable.Equals(a)));

        Record("pose.data", "scene pose " + P.PB(m_HolderPose()) + " poses " + m_Holder.poses.Length +
                            (m_Holder.poses.Length > 1 ? " [1] " + P.PB(m_Holder.poses[1]) : ""));
        var json = JsonUtility.ToJson(a);
        Record("pose.data", "json " + json);
        Record("pose.data", "from json " + P.PB(JsonUtility.FromJson<Pose>(json)));
        var clone = Object.Instantiate(m_Holder);
        Record("pose.data", "clone pose " + P.PB(clone.pose));
        Destroy(clone.gameObject);
    }

    Pose m_HolderPose() => m_Holder.pose;

    // ---------------------------------------------------------------- LazyLoadReference

    static string Describe<T>(string label, LazyLoadReference<T> r) where T : Object
    {
        var b = new StringBuilder(label);
        b.Append(" isSet=").Append(P.B(r.isSet));
        b.Append(" id=").Append(Id(r.instanceID));
        b.Append(" sign=").Append(Math.Sign(r.instanceID));
        try { b.Append(" isBroken=").Append(P.B(r.isBroken)); }
        catch (Exception e) { b.Append(" isBroken threw ").Append(P.ExType(e)); }
        try
        {
            var asset = r.asset;
            b.Append(" asset=").Append(asset == null ? (ReferenceEquals(asset, null) ? "null" : "fake-null") : asset.name + ":" + asset.GetType().Name);
            if (asset != null) b.Append(" sameId=").Append(P.B(asset.GetInstanceID() == r.instanceID));
        }
        catch (Exception e)
        {
            b.Append(" asset threw ").Append(P.ExType(e));
        }
        return b.ToString();
    }

    void Lazy()
    {
        var h = m_Holder;
        Record("lazy.scene", "data isSet " + P.B(h.data.isSet) + " (no load)");
        Name(h.mat.asset, "LazyMat");
        Record("lazy.scene", Describe("mat", h.mat));
        Record("lazy.scene", Describe("data", h.data));
        if (h.data.isSet && h.data.asset != null) Name(h.data.asset, "LazyData");
        Record("lazy.scene", Describe("data", h.data) + " value=" + (h.data.asset != null ? h.data.asset.value.ToString() : "-"));
        if (h.prefab.asset != null) Name(h.prefab.asset, "LazyPrefab");
        Record("lazy.scene", Describe("prefab", h.prefab));
        Record("lazy.scene", Describe("none", h.none));
        Record("lazy.scene", Describe("broken", h.broken));
        Record("lazy.scene", Describe("wrongType", h.wrongType));
        Record("lazy.scene", Describe("sceneRef", h.sceneRef));
        Record("lazy.scene", "mats " + h.mats.Length);
        for (int i = 0; i < h.mats.Length; i++) Record("lazy.scene", Describe("mats" + i, h.mats[i]));

        var runtime = new Material(h.mat.asset);
        Name(runtime, "RuntimeMat");
        Record("lazy.runtime", Describe("default", default(LazyLoadReference<Material>)));
        Record("lazy.runtime", Describe("ctor 0", new LazyLoadReference<Material>(0)));
        Record("lazy.runtime", Describe("ctor missing", new LazyLoadReference<Material>(int.MaxValue - 1)));
        Record("lazy.runtime", Describe("ctor null", new LazyLoadReference<Material>((Material)null)));
        Record("lazy.runtime", Describe("ctor asset", new LazyLoadReference<Material>(h.mat.asset)));
        Try("ctor runtime", () => Record("lazy.runtime", Describe("ctor runtime", new LazyLoadReference<Material>(runtime))));
        Try("implicit runtime", () => { LazyLoadReference<Material> r = runtime; Record("lazy.runtime", Describe("implicit runtime", r)); });
        Try("implicit asset", () => { LazyLoadReference<Material> r = h.mat.asset; Record("lazy.runtime", Describe("implicit asset", r)); });
        Try("implicit null", () => { LazyLoadReference<Material> r = (Material)null; Record("lazy.runtime", Describe("implicit null", r)); });
        Try("implicit id", () => { LazyLoadReference<Material> r = runtime.GetInstanceID(); Record("lazy.runtime", Describe("implicit id", r)); });
        Try("setter", () =>
        {
            var r = new LazyLoadReference<Material>(h.mat.asset);
            r.asset = null;
            Record("lazy.runtime", Describe("set null", r));
            r.instanceID = runtime.GetInstanceID();
            Record("lazy.runtime", Describe("set id runtime", r));
            r.asset = h.mat.asset;
            Record("lazy.runtime", Describe("set asset", r));
            r.asset = runtime;
        });
        Try("scene object", () =>
        {
            var r = new LazyLoadReference<GameObject> { instanceID = m_C.GetInstanceID() };
            Record("lazy.runtime", Describe("scene object by id", r));
            Record("lazy.runtime", Describe("prefab by implicit", (LazyLoadReference<GameObject>)h.prefab.asset));
            r.asset = m_C;
        });
        Try("wrong type", () =>
        {
            LazyLoadReference<Texture2D> r = h.mat.instanceID;
            Record("lazy.runtime", Describe("material as texture", r));
            LazyLoadReference<Object> o = m_C.transform.GetInstanceID();
            Record("lazy.runtime", Describe("transform as object", o));
        });
        var destroyed = new Material(h.mat.asset);
        Name(destroyed, "DestroyedMat");
        var lazyDestroyed = new LazyLoadReference<Material> { instanceID = destroyed.GetInstanceID() };
        DestroyImmediate(destroyed);
        Record("lazy.runtime", Describe("destroyed runtime", lazyDestroyed));

        // Copies: Instantiate and JsonUtility.
        h.sceneRef = new LazyLoadReference<GameObject> { instanceID = h.gameObject.GetInstanceID() };
        var clone = Object.Instantiate(h);
        Name(clone.gameObject, "HolderClone");
        Record("lazy.copy", Describe("clone mat", clone.mat));
        Record("lazy.copy", Describe("clone sceneRef", clone.sceneRef));
        Record("lazy.copy", Describe("clone broken", clone.broken));
        Destroy(clone.gameObject);
        var json = JsonUtility.ToJson(new LazyJson { mat = h.mat });
        Record("lazy.json", "json " + JsonIds(json));
        var back = JsonUtility.FromJson<LazyJson>(json);
        Record("lazy.json", Describe("from json", back.mat));
        var fromId = JsonUtility.FromJson<LazyJson>("{\"x\":5,\"mat\":{\"instanceID\":" + h.mat.instanceID + "}}");
        Record("lazy.json", Describe("from instanceID json", fromId.mat) + " x=" + fromId.x);
        Record("lazy.json", "holder json " + JsonIds(JsonUtility.ToJson(h)));
        Destroy(runtime);
    }

    // ---------------------------------------------------------------- SetGameObjectsActive

    void State()
    {
        var b = new StringBuilder("state");
        foreach (var go in new[] { m_A, m_A1, m_B, m_C, m_D, m_D1, m_E, m_F })
            b.Append(' ').Append(go.name).Append('=').Append(P.B(go.activeSelf)).Append(P.B(go.activeInHierarchy));
        Record("probe", b.ToString());
    }

    void Active(GameObject[] objects, bool active)
    {
        var ids = new int[objects.Length];
        for (int i = 0; i < ids.Length; i++) ids[i] = objects[i].GetInstanceID();
        Ids(ids, active);
    }

    void Ids(int[] ids, bool active)
    {
        var names = new List<string>();
        foreach (var id in ids) names.Add(Id(id));
        Record("probe", "SetGameObjectsActive [" + string.Join(",", names) + "] " + P.B(active));
        Try("span", () => GameObject.SetGameObjectsActive(new ReadOnlySpan<int>(ids), active));
        State();
    }

    void Try(string label, Action action)
    {
        try { action(); Record("probe", label + " returned"); }
        catch (Exception e) { Record("probe", label + " threw " + P.Ex(e)); }
    }

    // ---------------------------------------------------------------- InstantiateGameObjects

    static void Make(int a, int b, out NativeArray<int> first, out NativeArray<int> second, bool created = true)
    {
        first = created ? new NativeArray<int>(a, Allocator.Persistent) : default;
        second = created ? new NativeArray<int>(b, Allocator.Persistent) : default;
        for (int i = 0; i < a && created; i++) first[i] = 99;
        for (int i = 0; i < b && created; i++) second[i] = 99;
    }

    static void Dispose(NativeArray<int> a, NativeArray<int> b)
    {
        if (a.IsCreated) a.Dispose();
        if (b.IsCreated) b.Dispose();
    }

    GameObject[] Instantiate(string label, int source, int count, Scene scene)
    {
        Make(count, count, out var ids, out var transforms);
        Record("probe", $"InstantiateGameObjects {label} source={Id(source)} count={count} scene={(scene.IsValid() ? scene.name : "default")}");
        GameObject[] copies = null;
        try
        {
            GameObject.InstantiateGameObjects(source, count, ids, transforms, scene);
            Record("probe", label + " returned");
            copies = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                var go = Resources.InstanceIDToObject(ids[i]) as GameObject;
                var tr = Resources.InstanceIDToObject(transforms[i]) as Transform;
                copies[i] = go;
                if (go == null)
                {
                    Record("copy" + i, $"id={(ids[i] == 99 ? "99" : Id(ids[i]))} transform={(transforms[i] == 99 ? "99" : Id(transforms[i]))} no GameObject");
                    continue;
                }
                Name(go, label + i);
                Name(go.transform, label + i + ".transform");
                Record("copy" + i, Copy(go, tr, ids[i]));
            }
            var roots = new List<string>();
            foreach (var root in (scene.IsValid() && scene.isLoaded ? scene : SceneManager.GetActiveScene()).GetRootGameObjects())
                if (root.name.StartsWith("Src") || root.name.StartsWith("Item")) roots.Add(root.name + (copies != null && Array.IndexOf(copies, root) >= 0 ? "*" : ""));
            Record("probe", "roots " + string.Join(",", roots));
        }
        catch (Exception e)
        {
            Record("probe", label + " threw " + P.Ex(e));
            var values = new List<string>();
            for (int i = 0; i < ids.Length; i++) values.Add(ids[i] == 99 ? "99" : Id(ids[i]));
            if (ids.Length > 0) Record("probe", "ids after " + string.Join(",", values));
        }
        Dispose(ids, transforms);
        return copies;
    }

    string Copy(GameObject go, Transform tr, int id)
    {
        var t = go.transform;
        var b = new StringBuilder();
        b.Append("name=").Append(go.name);
        b.Append(" idSign=").Append(Math.Sign(id));
        b.Append(" transformMatches=").Append(P.B(tr == t));
        b.Append(" scene=").Append(go.scene.name);
        b.Append(" parent=").Append(t.parent != null ? t.parent.name : "-");
        b.Append(" sibling=").Append(t.GetSiblingIndex());
        b.Append(" active=").Append(P.B(go.activeSelf)).Append(P.B(go.activeInHierarchy));
        b.Append(" local=").Append(P.V(t.localPosition)).Append(' ').Append(P.V(t.localEulerAngles)).Append(' ').Append(P.V(t.localScale));
        b.Append(" world=").Append(P.V(t.position)).Append(' ').Append(P.V(t.lossyScale));
        b.Append(" children=");
        for (int i = 0; i < t.childCount; i++) b.Append(i > 0 ? "," : "").Append(t.GetChild(i).name);
        var msg = go.GetComponent<Msg>();
        if (msg != null && msg.target != null)
        {
            var target = msg.target as GameObject;
            b.Append(" target=").Append(target != null ? (target.transform.parent == t ? "own " : "other ") + target.name : msg.target.name);
        }
        return b.ToString();
    }

    // ---------------------------------------------------------------- OnDemandRendering

    static string Rate() => "effective=" + OnDemandRendering.effectiveRenderFrameRate;

    void RenderSettingsCases()
    {
        Record("probe", $"interval={OnDemandRendering.renderFrameInterval} will={P.B(OnDemandRendering.willCurrentFrameRender)} {Rate()} " +
                        $"targetFrameRate={Application.targetFrameRate} vSync={QualitySettings.vSyncCount}");
        foreach (var v in new[] { 0, -3, 2, 5, 1 })
        {
            OnDemandRendering.renderFrameInterval = v;
            Record("probe", $"set {v} -> {OnDemandRendering.renderFrameInterval}");
        }
        int target = Application.targetFrameRate, vsync = QualitySettings.vSyncCount;
        foreach (var (tfr, vs, interval) in new[] { (-1, 0, 1), (60, 0, 1), (60, 0, 2), (60, 0, 3), (50, 0, 3), (45, 0, 2), (0, 0, 2), (30, 1, 2), (-1, 1, 1), (-1, 2, 3) })
        {
            Application.targetFrameRate = tfr;
            QualitySettings.vSyncCount = vs;
            OnDemandRendering.renderFrameInterval = interval;
            Record("probe", $"targetFrameRate={tfr} vSync={vs} interval={interval} {Rate()}");
        }
        Application.targetFrameRate = target;
        QualitySettings.vSyncCount = vsync;
        OnDemandRendering.renderFrameInterval = 1;
    }

    void Finish()
    {
        if (Finished) return;
        Finished = true;
        OnDemandRendering.renderFrameInterval = 1;
        Record("probe", "finish");
        var output = Environment.GetEnvironmentVariable("CORE_APIS_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, s_Log.ToString());
        }
        if (finished != null)
        {
            finished();
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
        Application.Quit();
    }
}
