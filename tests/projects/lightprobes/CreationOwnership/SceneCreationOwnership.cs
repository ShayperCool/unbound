using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creation-ownership witness (suite "creation-ownership", schema 1): which scene owns, and what
/// becomes of, objects made while a SceneManager Single load unloads the outgoing scene.
/// JSON lines go to <c>LIGHT_PROBES_CREATION_OUT</c>: a source row, then rows by sequence index,
/// then <c>{"k":"complete","v":true}</c>; the Editor then quits. The unordered
/// FindObjectsByType results are written in their returned order, by the same sequence index, to
/// <c>LIGHT_PROBES_CREATION_RAW_OUT</c>; the main output holds them sorted by alias (ordinal).
///
/// Setup (Main active): Resources.Load of the tiny prefab, and "destroyer", a new root in Main
/// whose tracker makes a root in its OnDestroy.
/// t1, LoadScene("Extra", Single); the first sceneUnloaded handler makes
///   (i) "i-new", new GameObject + tracker; (ii) "ii-prefab", Instantiate of the loaded prefab;
///   (iii) "iii-child", new GameObject + tracker, SetParent(ii), then SetParent(null);
///   the destroyer's OnDestroy makes (iv) "iv-ondestroy", new GameObject + tracker.
/// t2, LoadScene("Main", Single); the first sceneUnloaded handler calls CreateScene("Made") and
///   SetActiveScene(Made).
/// t3, LoadScene("Extra", Single); the handler only records.
/// Points (declared rows, each exactly once): setup; tN.a.start (first sceneUnloaded of tN);
/// t1.a.i, t1.a.ii, t1.a.iii.parented, t1.a.iii.detached, t1.a.iv; t2.a.made; tN.b (first
/// sceneLoaded of tN); tN.c (the first controller LateUpdate after it). Every other event is
/// recorded too. Each snapshot holds the active scene, the listed scenes, every scene seen so far
/// (name, path, build index, valid, loaded, active, listed, roots), the global light probe counts,
/// and for every declared alias: existence, scene, parent, root, scene-root membership,
/// GameObject.Find and FindObjectsByType membership and its message status.
/// Exceptions and log messages are recorded as behaviour. Scenes, objects and LightProbes are
/// named by stable aliases (first-seen order or the declared alias), never by handle or id.
///
/// Before EditorApplication.Exit every handler is unsubscribed and the output closed; the
/// trackers' shutdown messages then neither query the engine nor write.
/// </summary>
public class SceneCreationOwnership : MonoBehaviour
{
    const string Suite = "creation-ownership";
    const int Schema = 1;
    const int TransitionFrames = 600;
    static readonly string[] Aliases = { "destroyer", "i-new", "ii-prefab", "iii-child", "iv-ondestroy" };
    static readonly string[] Targets = { null, "Extra", "Main", "Extra" };

    static string s_Out, s_RawOut;
    static int s_Seq;
    static string s_Stage = "play";
    static string s_NextAlias;
    static int s_Transition, s_TransitionFrame;
    static bool s_UnloadedSeen, s_LoadedSeen, s_FramePending, s_FrameDone;
    static GameObject s_Prefab;
    static Scene s_Made;
    static SceneCreationOwnership s_Controller;
    static readonly Dictionary<string, GameObject> s_Objects = new Dictionary<string, GameObject>();
    static readonly List<int> s_SceneHandles = new List<int>();
    static readonly List<Scene> s_Scenes = new List<Scene>();
    static readonly List<LightProbes> s_Probes = new List<LightProbes>();

    public static int Epoch;
    public static bool Open => !string.IsNullOrEmpty(s_Out);

    // ------------------------------------------------------------------ JSON

    static string Q(string s) => s == null ? "null"
        : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
    static string B(bool b) => b ? "true" : "false";

    static string Obj(params string[] pairs)
    {
        var text = new StringBuilder("{");
        for (int i = 0; i < pairs.Length; i += 2)
            text.Append(i == 0 ? "" : ",").Append(Q(pairs[i])).Append(':').Append(pairs[i + 1]);
        return text.Append('}').ToString();
    }

    static string Arr(IEnumerable<string> values) => "[" + string.Join(",", values) + "]";

    static string Safe(Func<string> json)
    {
        try { return json(); }
        catch (Exception e) { return Q("!" + e.GetType().Name + ": " + e.Message); }
    }

    static string Thrown(Exception e) => Q(e.GetType().Name + ": " + e.Message);

    // ------------------------------------------------------------------ aliases

    static string Ref(Scene scene)
    {
        if (scene.handle == 0) return "none";
        int index = s_SceneHandles.IndexOf(scene.handle);
        if (index < 0)
        {
            s_SceneHandles.Add(scene.handle);
            s_Scenes.Add(scene);
            index = s_SceneHandles.Count - 1;
        }
        return "scene" + index;
    }

    static string ObjRef(GameObject go)
    {
        if (ReferenceEquals(go, null)) return "null";
        foreach (var pair in s_Objects)
            if (ReferenceEquals(pair.Value, go)) return pair.Key;
        if (go == null) return "destroyed";
        if (s_Controller != null && ReferenceEquals(go, s_Controller.gameObject)) return "controller";
        return "name:" + go.name;
    }

    static string TransformRef(Transform transform) => transform == null ? "null" : ObjRef(transform.gameObject);

    /// <summary>The declared alias of the object whose tracker wakes now.</summary>
    public static string TakeAlias(GameObject go)
    {
        foreach (var pair in s_Objects)
            if (ReferenceEquals(pair.Value, go)) { s_NextAlias = null; return pair.Key; }
        string alias = s_NextAlias ?? "unexpected:" + go.name;
        s_NextAlias = null;
        if (!s_Objects.ContainsKey(alias)) s_Objects[alias] = go;
        return alias;
    }

    // ------------------------------------------------------------------ state

    static string Probes()
    {
        var probes = LightmapSettings.lightProbes;
        if (probes == null) return Q("null");
        int index = s_Probes.FindIndex(o => ReferenceEquals(o, probes));
        if (index < 0) { s_Probes.Add(probes); index = s_Probes.Count - 1; }
        return Q($"probes{index} {probes.count} {probes.countSelf} {probes.cellCount} {probes.cellCountSelf}");
    }

    static List<Scene> Listed()
    {
        var list = new List<Scene>();
        for (int i = 0; i < SceneManager.sceneCount; i++) list.Add(SceneManager.GetSceneAt(i));
        return list;
    }

    static string SceneBrief(Scene scene) => Obj(
        "ref", Q(Ref(scene)),
        "name", Safe(() => Q(scene.name)),
        "valid", Safe(() => B(scene.IsValid())),
        "loaded", Safe(() => B(scene.isLoaded)),
        "active", Safe(() => B(scene == SceneManager.GetActiveScene())),
        "listed", Safe(() => B(Listed().Contains(scene))));

    static string SceneFull(Scene scene) => Obj(
        "ref", Q(Ref(scene)),
        "name", Safe(() => Q(scene.name)),
        "path", Safe(() => Q(scene.path)),
        "buildIndex", Safe(() => scene.buildIndex.ToString()),
        "valid", Safe(() => B(scene.IsValid())),
        "loaded", Safe(() => B(scene.isLoaded)),
        "active", Safe(() => B(scene == SceneManager.GetActiveScene())),
        "listed", Safe(() => B(Listed().Contains(scene))),
        "rootCount", Safe(() => scene.rootCount.ToString()),
        "roots", Safe(() =>
        {
            var names = new List<string>();
            foreach (var root in scene.GetRootGameObjects()) names.Add(Q(ObjRef(root)));
            return Arr(names);
        }));

    static string ObjectState(string alias, HashSet<GameObject> byType)
    {
        if (!s_Objects.TryGetValue(alias, out var go)) return Obj("created", "false");
        if (go == null) return Obj("created", "true", "alive", "false");
        var tracker = Safe(() => Q(go.GetComponent<CreationOwnershipTracker>() == null ? "null" : "found"));
        return Obj(
            "created", "true",
            "alive", "true",
            "name", Safe(() => Q(go.name)),
            "activeSelf", Safe(() => B(go.activeSelf)),
            "activeInHierarchy", Safe(() => B(go.activeInHierarchy)),
            "scene", Safe(() => SceneBrief(go.scene)),
            "parent", Safe(() => Q(TransformRef(go.transform.parent))),
            "root", Safe(() => Q(TransformRef(go.transform.root))),
            "inSceneRoots", Safe(() => B(Array.IndexOf(go.scene.GetRootGameObjects(), go) >= 0)),
            "find", Safe(() =>
            {
                var found = GameObject.Find(go.name);
                return Q(found == null ? "null" : ReferenceEquals(found, go) ? "self" : "other " + ObjRef(found));
            }),
            "findByType", B(byType != null && byType.Contains(go)),
            "tracker", tracker,
            "status", Safe(() =>
            {
                var t = go.GetComponent<CreationOwnershipTracker>();
                return t == null ? "null" : Q(t.status);
            }),
            "updatedThisEpoch", Safe(() =>
            {
                var t = go.GetComponent<CreationOwnershipTracker>();
                return t == null ? "null" : B(t.updateEpoch == Epoch);
            }));
    }

    static void Snapshot(string point, string at, string args)
    {
        if (!Open) return;
        HashSet<GameObject> byType = null;
        var raw = new List<string>();
        var sorted = new List<string>();
        string byTypeJson = Safe(() =>
        {
            var found = UnityEngine.Object.FindObjectsByType<CreationOwnershipTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            byType = new HashSet<GameObject>();
            foreach (var tracker in found)
            {
                byType.Add(tracker.gameObject);
                raw.Add(ObjRef(tracker.gameObject));
            }
            sorted.AddRange(raw);
            sorted.Sort(string.CompareOrdinal);
            var quoted = new List<string>();
            foreach (var alias in sorted) quoted.Add(Q(alias));
            return Arr(quoted);
        });
        var objects = new List<string>();
        foreach (var alias in Aliases) objects.Add(Q(alias) + ":" + ObjectState(alias, byType));
        string listed = Safe(() =>
        {
            var refs = new List<string>();
            foreach (var scene in Listed()) refs.Add(Q(Ref(scene)));
            return Arr(refs);
        });
        if (s_Controller != null) Safe(() => Ref(s_Controller.gameObject.scene));
        string active = Safe(() => SceneBrief(SceneManager.GetActiveScene()));
        var seen = new List<string>();
        for (int i = 0; i < s_Scenes.Count; i++) seen.Add(SceneFull(s_Scenes[i]));
        string state = Obj(
            "active", active,
            "sceneCount", Safe(() => SceneManager.sceneCount.ToString()),
            "loadedSceneCount", Safe(() => SceneManager.loadedSceneCount.ToString()),
            "listed", listed,
            "controllerScene", s_Controller == null ? "null" : Safe(() => Q(Ref(s_Controller.gameObject.scene))),
            "scenes", Arr(seen),
            "lightProbes", Safe(Probes),
            "findByType", byTypeJson,
            "objects", "{" + string.Join(",", objects) + "}");
        // The number is taken once the state is read, so rows the reading raised come first.
        int seq = s_Seq++;
        Write(Obj("seq", seq.ToString(), "stage", Q(s_Stage), "kind", Q("snapshot"), "point", Q(point),
            "at", Q(at), "args", Q(args), "dframe", DFrame(), "state", state));
        if (!string.IsNullOrEmpty(s_RawOut))
        {
            var quoted = new List<string>();
            foreach (var alias in raw) quoted.Add(Q(alias));
            File.AppendAllText(s_RawOut, Obj("seq", seq.ToString(), "at", Q(at), "findByTypeRaw", Arr(quoted)) + "\n");
        }
    }

    static string DFrame() => s_Transition == 0 ? "null" : (Time.frameCount - s_TransitionFrame).ToString();

    static void Write(string line)
    {
        if (Open) File.AppendAllText(s_Out, line + "\n");
    }

    static void Row(string kind, string at, params string[] fields)
    {
        if (!Open) return;
        var pairs = new List<string> { "seq", (s_Seq++).ToString(), "stage", Q(s_Stage), "kind", Q(kind), "at", Q(at), "dframe", DFrame() };
        pairs.AddRange(fields);
        Write(Obj(pairs.ToArray()));
    }

    static void Error(string at, Exception e) => Row("error", at, "exception", e == null ? "null" : Thrown(e));

    public static void Lifecycle(CreationOwnershipTracker tracker, string message)
    {
        if (!Open) return;
        Row("lifecycle", message, "alias", Q(tracker.alias),
            "scene", Safe(() => SceneBrief(tracker.gameObject.scene)),
            "activeScene", Safe(() => Q(Ref(SceneManager.GetActiveScene()))));
    }

    // ------------------------------------------------------------------ creations

    static void Register(string alias, GameObject go)
    {
        if (go != null && !s_Objects.ContainsKey(alias)) s_Objects[alias] = go;
    }

    /// <summary>Runs one creation; its exception is behaviour, recorded on the create row.</summary>
    static void Create(string step, Func<string> body)
    {
        string result, exception = "null";
        try { result = body(); }
        catch (Exception e) { result = "null"; exception = Thrown(e); }
        Row("create", step, "result", result, "exception", exception);
    }

    static GameObject NewTracked(string alias, string name)
    {
        s_NextAlias = alias;
        var go = new GameObject(name);
        Register(alias, go);
        try { go.AddComponent<CreationOwnershipTracker>(); }
        finally
        {
            if (s_NextAlias != null) { s_NextAlias = null; Row("note", "no Awake inside AddComponent", "alias", Q(alias)); }
        }
        return go;
    }

    static void CreateInUnloaded()
    {
        Create("i new GameObject", () => Q(ObjRef(NewTracked("i-new", "CreatedNew"))));
        Snapshot("t1.a.i", "after (i)", "");
        Create("ii Instantiate prefab", () =>
        {
            s_NextAlias = "ii-prefab";
            GameObject instance;
            try { instance = UnityEngine.Object.Instantiate(s_Prefab); }
            finally
            {
                if (s_NextAlias != null) { s_NextAlias = null; Row("note", "no Awake inside Instantiate", "alias", Q("ii-prefab")); }
            }
            bool sameAsAwake = s_Objects.TryGetValue("ii-prefab", out var awoke) && ReferenceEquals(awoke, instance);
            Register("ii-prefab", instance);
            return Obj("instance", Q(ObjRef(instance)), "sameAsAwake", B(sameAsAwake));
        });
        Snapshot("t1.a.ii", "after (ii)", "");
        Create("iii new child", () => Q(ObjRef(NewTracked("iii-child", "CreatedChild"))));
        Create("iii SetParent(ii)", () =>
        {
            s_Objects["iii-child"].transform.SetParent(s_Objects["ii-prefab"].transform);
            return Q("returned");
        });
        Snapshot("t1.a.iii.parented", "after (iii) SetParent(ii)", "");
        Create("iii SetParent(null)", () =>
        {
            s_Objects["iii-child"].transform.SetParent(null);
            return Q("returned");
        });
        Snapshot("t1.a.iii.detached", "after (iii) SetParent(null)", "");
    }

    public static void CreateInOnDestroy()
    {
        if (!Open) return;
        if (s_Transition != 1 || s_Objects.ContainsKey("iv-ondestroy"))
        {
            Row("note", "destroyer OnDestroy outside t1; nothing created");
            return;
        }
        try
        {
            Create("iv new GameObject in OnDestroy", () => Q(ObjRef(NewTracked("iv-ondestroy", "CreatedInOnDestroy"))));
            Snapshot("t1.a.iv", "after (iv)", "");
        }
        catch (Exception e) { Error("OnDestroy creation", e); }
    }

    static void MakeScene()
    {
        Create("CreateScene Made", () =>
        {
            s_Made = SceneManager.CreateScene("Made");
            return SceneBrief(s_Made);
        });
        Create("SetActiveScene Made", () => B(SceneManager.SetActiveScene(s_Made)));
        Snapshot("t2.a.made", "after CreateScene + SetActiveScene", "");
    }

    // ------------------------------------------------------------------ observer

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Subscribe()
    {
        s_Out = Environment.GetEnvironmentVariable("LIGHT_PROBES_CREATION_OUT");
        if (string.IsNullOrEmpty(s_Out)) { s_Out = null; return; }
        s_RawOut = Environment.GetEnvironmentVariable("LIGHT_PROBES_CREATION_RAW_OUT");
        File.WriteAllText(s_Out, "");
        if (!string.IsNullOrEmpty(s_RawOut)) File.WriteAllText(s_RawOut, "");
        var aliases = new List<string>();
        foreach (var alias in Aliases) aliases.Add(Q(alias));
        Write(Obj("k", Q("source"), "suite", Q(Suite), "schema", Schema.ToString(),
            "release", Q(Application.unityVersion), "aliases", Arr(aliases)));
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        Application.logMessageReceived += OnLog;
    }

    static void Unsubscribe()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Application.logMessageReceived -= OnLog;
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (!Open) return;
        Row("log", type.ToString(), "message", Q(condition));
    }

    static void OnSceneUnloaded(Scene scene)
    {
        if (!Open) return;
        try
        {
            bool first = s_Transition > 0 && !s_UnloadedSeen;
            if (first) s_UnloadedSeen = true;
            Snapshot(first ? $"t{s_Transition}.a.start" : null, "sceneUnloaded", Ref(scene));
            if (!first) return;
            if (s_Transition == 1) CreateInUnloaded();
            else if (s_Transition == 2) MakeScene();
        }
        catch (Exception e) { Error("sceneUnloaded handler", e); }
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Open) return;
        try
        {
            bool first = s_Transition > 0 && !s_LoadedSeen;
            if (first) { s_LoadedSeen = true; Epoch++; }
            Snapshot(first ? $"t{s_Transition}.b" : null, "sceneLoaded", Ref(scene) + " " + mode);
            if (first) s_FramePending = true;
        }
        catch (Exception e) { Error("sceneLoaded handler", e); }
    }

    static void OnActiveSceneChanged(Scene from, Scene to)
    {
        if (!Open) return;
        try { Snapshot(null, "activeSceneChanged", Ref(from) + " -> " + Ref(to)); }
        catch (Exception e) { Error("activeSceneChanged handler", e); }
    }

    // ------------------------------------------------------------------ controller

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (!Open) return;
        var controller = new GameObject("SceneCreationOwnership");
        DontDestroyOnLoad(controller);
        s_Controller = controller.AddComponent<SceneCreationOwnership>();
    }

    void LateUpdate()
    {
        if (!Open || !s_FramePending) return;
        s_FramePending = false;
        try { Snapshot($"t{s_Transition}.c", "LateUpdate after sceneLoaded", ""); }
        catch (Exception e) { Error("LateUpdate", e); }
        s_FrameDone = true;
    }

    static void Step(string at, Action body)
    {
        try { body(); }
        catch (Exception e) { Error(at, e); }
    }

    static void Setup()
    {
        s_Stage = "setup";
        Create("Resources.Load CreationTiny", () =>
        {
            s_Prefab = Resources.Load<GameObject>("CreationTiny");
            return Q(s_Prefab == null ? "null" : "prefab " + s_Prefab.name);
        });
        Create("destroyer new GameObject", () =>
        {
            var go = NewTracked("destroyer", "CreationDestroyer");
            go.GetComponent<CreationOwnershipTracker>().createOnDestroy = true;
            return Q(ObjRef(go));
        });
        Snapshot("setup", "after setup", "");
    }

    static void Begin(int transition)
    {
        s_Transition = transition;
        s_TransitionFrame = Time.frameCount;
        s_UnloadedSeen = s_LoadedSeen = s_FramePending = s_FrameDone = false;
        s_Stage = "t" + transition;
        Epoch++;
        Snapshot(null, "before LoadScene " + Targets[transition] + " Single", "");
        SceneManager.LoadScene(Targets[transition], LoadSceneMode.Single);
        Snapshot(null, "LoadScene returned", "");
    }

    IEnumerator Start()
    {
        for (int i = 0; i < 3; i++) yield return null;
        Step("setup", Setup);
        yield return null;
        Step("frame after setup", () => Snapshot(null, "frame after setup", ""));
        for (int transition = 1; transition <= 3; transition++)
        {
            Step("begin t" + transition, () => Begin(transition));
            int frames = 0;
            while (!s_FrameDone && frames < TransitionFrames) { frames++; yield return null; }
            if (!s_FrameDone) Error($"t{transition} reached no LateUpdate after sceneLoaded within {TransitionFrames} frames", null);
            yield return null;
            Step("frame after t" + transition, () => Snapshot(null, "frame after t" + transition + " LateUpdate", ""));
        }
        Finish();
    }

    static void Finish()
    {
        s_Stage = "done";
        Unsubscribe();
        string output = s_Out;
        // Shutdown destroys the remaining trackers: their messages must neither query nor write.
        s_Out = null;
        s_RawOut = null;
        File.AppendAllText(output, "{\"k\":\"complete\",\"v\":true}\n");
        Debug.Log("scene creation ownership witness written to " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
