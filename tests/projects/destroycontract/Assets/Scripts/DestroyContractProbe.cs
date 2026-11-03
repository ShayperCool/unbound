using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// Manual <c>Object.Destroy</c> contract witness: public state, lookups and lifecycle callbacks
/// around Destroy requests made from Update, LateUpdate and after TriggerEndOfFrameCallbacks.
///
/// Thirteen subjects with predeclared labels are created at boot, each with one
/// <see cref="DestroyContractObserver"/>. The case table requests one Destroy per case at a fixed
/// probe step (cases are staggered so a case's deferred work never shares a frame with another
/// case, except the three <c>same.*</c> cases that repeat the existing trail fixture's same-step
/// layout). Every case reads its subject (and its child) just before the request and just after
/// it returns; every subject is also read at three fixed points per step (start of this driver's
/// Update and LateUpdate, and after TriggerEndOfFrameCallbacks).
///
/// Bracket markers surround every leaf of the default PlayerLoop that is current at boot: one
/// marker before and one after each leaf, nothing else changed (no leaf is removed, duplicated or
/// reordered; the read-back loop is verified). A marker only records which leaf is running, so a
/// callback's position is "in:Group.Leaf" (between that leaf's two markers), "after:Group.Leaf"
/// (between that leaf's after marker and the next leaf's before marker) or "boot". The after
/// marker of PostLateUpdate.TriggerEndOfFrameCallbacks also runs the probe's end-of-frame point.
///
/// The compared record (values.jsonl) holds only deterministic values: probe step, frame relative
/// to boot, authored labels, booleans, counts and exception type names. Real times, raw frame
/// numbers, instance IDs, visit counts and the device name go to diagnostics.jsonl. Lifecycle
/// callbacks are recorded in their raw order (per subject and globally), never sorted.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class DestroyContractProbe : MonoBehaviour
{
    public const string OutputVariable = "UNBOUND_PROBE_DESTROYCONTRACT_OUT";
    public const int FinalStep = 22;
    public const int CaptureFramerate = 50;

    /// <summary>Subjects in creation order: label, parent label (null: scene root), authored set-up.</summary>
    public static readonly (string Label, string Parent, string Setup)[] Subjects =
    {
        ("keep", null, "active"),
        ("go.update", null, "active"),
        ("go.late", null, "active"),
        ("go.eof", null, "active"),
        ("comp.update", null, "active"),
        ("comp.disabled", null, "disabled"),
        ("go.inactive", null, "inactive"),
        ("tree.parent", null, "active"),
        ("tree.child", "tree.parent", "active"),
        ("same.update", null, "active"),
        ("same.late", null, "active"),
        ("same.eof", null, "active"),
        ("reenter", null, "reenter"),
    };

    /// <summary>Cases: name, probe step, request point, subject, what is destroyed.</summary>
    public static readonly (string Name, int Step, string Point, string Subject, string Target)[] Cases =
    {
        ("go.update", 2, "update", "go.update", "gameObject"),
        ("go.late", 4, "late", "go.late", "gameObject"),
        ("go.eof", 6, "eof", "go.eof", "gameObject"),
        ("comp.update", 8, "update", "comp.update", "component"),
        ("comp.disabled", 10, "update", "comp.disabled", "gameObject"),
        ("go.inactive", 12, "update", "go.inactive", "gameObject"),
        ("tree.parent", 14, "update", "tree.parent", "gameObject"),
        ("same.update", 16, "update", "same.update", "gameObject"),
        ("same.late", 16, "late", "same.late", "gameObject"),
        ("same.eof", 16, "eof", "same.eof", "gameObject"),
        ("reenter", 19, "update", "reenter", "gameObject"),
    };

    /// <summary>The runner reads this after the record is written (0 complete, 3 probe fault).</summary>
    public static int? finishedCode;

    static DestroyContractProbe instance;
    static PlayerLoopSystem original;
    static bool installed;
    static bool closed;
    static int position = -1;
    static bool inside;
    static string[] leaves = new string[0];
    static bool[] fixedLeaf = new bool[0];
    static int eofLeaf = -1;
    static int[] beforeVisits = new int[0], afterVisits = new int[0];

    string output;
    int step = -1;
    int frame0;
    int[] lastVisits;
    bool balanced = true, everyLeafOnce = true;
    int frameChecks;
    readonly List<string> records = new List<string>();
    readonly Dictionary<string, List<string>> traces = new Dictionary<string, List<string>>();
    readonly List<string> order = new List<string>();
    readonly List<string> logs = new List<string>();
    readonly List<string> faults = new List<string>();
    readonly List<string> stepFrames = new List<string>();
    readonly List<string> fixedPerFrame = new List<string>();
    GameObject[] objects;
    DestroyContractObserver[] observers;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string path = Environment.GetEnvironmentVariable(OutputVariable);
        if (path == null) return;
        Time.captureFramerate = CaptureFramerate;
        instance = new GameObject("probe").AddComponent<DestroyContractProbe>();
        instance.Begin(path);
    }

    void Begin(string path)
    {
        output = path;
        frame0 = Time.frameCount;
        foreach (var s in Subjects) traces[s.Label] = new List<string>();
        Diagnostic("start");
        Application.logMessageReceived += OnLog;
        Value("context.unityVersion", Read(() => Q(Application.unityVersion)));
        Value("context.isBatchMode", Read(() => B(Application.isBatchMode)));
        Value("context.isEditor", Read(() => B(Application.isEditor)));
        Value("context.platform", Read(() => Q(Application.platform.ToString())));
        Value("context.graphicsDeviceType", Read(() => Q(SystemInfo.graphicsDeviceType.ToString())));
        Value("context.scene", Read(() => Q(SceneManager.GetActiveScene().name)));
        Value("context.captureFramerate", Read(() => I(Time.captureFramerate)));
        Value("context.fixedDeltaTime", Read(() => Time.fixedDeltaTime.ToString("R", CultureInfo.InvariantCulture)));
        var labels = new List<string>();
        foreach (var s in Subjects) labels.Add("[" + Q(s.Label) + "," + Q(s.Parent) + "," + Q(s.Setup) + "]");
        Value("setup.subjects", "[" + string.Join(",", labels) + "]");
        var cases = new List<string>();
        foreach (var c in Cases)
            cases.Add("[" + Q(c.Name) + "," + I(c.Step) + "," + Q(c.Point) + "," + Q(c.Subject) + "," + Q(c.Target) + "]");
        Value("setup.cases", "[" + string.Join(",", cases) + "]");
        Value("setup.finalStep", I(FinalStep));
        Guard("setup", CreateSubjects);
        Guard("loop", Install);
    }

    void CreateSubjects()
    {
        objects = new GameObject[Subjects.Length];
        observers = new DestroyContractObserver[Subjects.Length];
        for (int i = 0; i < Subjects.Length; i++)
        {
            var go = new GameObject(Subjects[i].Label);
            if (Subjects[i].Parent != null) go.transform.SetParent(objects[Index(Subjects[i].Parent)].transform, false);
            var observer = go.AddComponent<DestroyContractObserver>();
            objects[i] = go;
            observers[i] = observer;
            switch (Subjects[i].Setup)
            {
                case "disabled": observer.enabled = false; break;
                case "inactive": go.SetActive(false); break;
                case "reenter": observer.reenter = true; break;
            }
        }
    }

    static int Index(string label)
    {
        for (int i = 0; i < Subjects.Length; i++) if (Subjects[i].Label == label) return i;
        throw new ArgumentException("unknown subject " + label);
    }

    // ------------------------------------------------------------------ PlayerLoop brackets

    void Install()
    {
        original = PlayerLoop.GetCurrentPlayerLoop();
        var names = new List<string>();
        var groups = new List<string>();
        Collect(original, "", names, groups, 0);
        leaves = names.ToArray();
        fixedLeaf = new bool[leaves.Length];
        beforeVisits = new int[leaves.Length];
        afterVisits = new int[leaves.Length];
        for (int i = 0; i < leaves.Length; i++)
        {
            fixedLeaf[i] = leaves[i].StartsWith("FixedUpdate.", StringComparison.Ordinal);
            if (leaves[i] == "PostLateUpdate.TriggerEndOfFrameCallbacks") eofLeaf = i;
        }
        Value("loop.groups", List(groups));
        Value("loop.leaves", List(names));
        var wrapped = original;
        int next = 0;
        Wrap(ref wrapped, ref next);
        PlayerLoop.SetPlayerLoop(wrapped);
        installed = true;
        var problems = new List<string>();
        Verify(original, PlayerLoop.GetCurrentPlayerLoop(), "", problems);
        if (eofLeaf < 0) problems.Add("no PostLateUpdate.TriggerEndOfFrameCallbacks leaf");
        Value("loop.installed", B(problems.Count == 0));
        Value("loop.installProblems", List(problems));
    }

    static string Name(PlayerLoopSystem s) => s.type == null ? "?" : s.type.Name;
    static bool HasChildren(PlayerLoopSystem s) => s.subSystemList != null && s.subSystemList.Length > 0;

    static void Collect(PlayerLoopSystem node, string path, List<string> names, List<string> groups, int depth)
    {
        if (!HasChildren(node)) return;
        foreach (var child in node.subSystemList)
        {
            string p = path.Length == 0 ? Name(child) : path + "." + Name(child);
            if (depth == 0) groups.Add(Name(child));
            if (HasChildren(child)) Collect(child, p, names, groups, depth + 1);
            else names.Add(p);
        }
    }

    static void Wrap(ref PlayerLoopSystem node, ref int next)
    {
        if (!HasChildren(node)) return;
        var result = new List<PlayerLoopSystem>();
        foreach (var source in node.subSystemList)
        {
            var child = source;
            if (HasChildren(child))
            {
                Wrap(ref child, ref next);
                result.Add(child);
                continue;
            }
            int leaf = next++;
            result.Add(new PlayerLoopSystem { type = typeof(DestroyContractMarker), updateDelegate = () => Mark(leaf, true) });
            result.Add(child);
            result.Add(new PlayerLoopSystem { type = typeof(DestroyContractMarker), updateDelegate = () => Mark(leaf, false) });
        }
        node.subSystemList = result.ToArray();
    }

    /// <summary>The installed loop must be the original with exactly one marker before and after
    /// every leaf: same groups, same leaf types, update and loop-condition functions, same order.</summary>
    static void Verify(PlayerLoopSystem want, PlayerLoopSystem got, string path, List<string> problems)
    {
        if (want.type != got.type) problems.Add(path + ": type " + Name(got) + " instead of " + Name(want));
        if (!HasChildren(want))
        {
            if (HasChildren(got)) problems.Add(path + ": a leaf gained children");
            return;
        }
        var expect = new List<(PlayerLoopSystem node, bool marker)>();
        foreach (var child in want.subSystemList)
        {
            if (HasChildren(child)) expect.Add((child, false));
            else
            {
                expect.Add((default, true));
                expect.Add((child, false));
                expect.Add((default, true));
            }
        }
        var have = got.subSystemList ?? new PlayerLoopSystem[0];
        if (have.Length != expect.Count)
        {
            problems.Add(path + ": " + have.Length + " systems instead of " + expect.Count);
            return;
        }
        for (int i = 0; i < have.Length; i++)
        {
            if (expect[i].marker)
            {
                if (have[i].type != typeof(DestroyContractMarker) || HasChildren(have[i]))
                    problems.Add(path + "[" + i + "]: " + Name(have[i]) + " instead of a marker");
                continue;
            }
            var w = expect[i].node;
            string p = path.Length == 0 ? Name(w) : path + "." + Name(w);
            if (!HasChildren(w))
            {
                if (have[i].type != w.type || have[i].updateFunction != w.updateFunction
                    || have[i].loopConditionFunction != w.loopConditionFunction || HasChildren(have[i]))
                    problems.Add(p + ": leaf changed");
                continue;
            }
            if (have[i].loopConditionFunction != w.loopConditionFunction || have[i].updateFunction != w.updateFunction)
                problems.Add(p + ": group functions changed");
            Verify(w, have[i], p, problems);
        }
    }

    static void Mark(int leaf, bool enter)
    {
        if (closed) return;
        if (enter) beforeVisits[leaf]++;
        else afterVisits[leaf]++;
        position = leaf;
        inside = enter;
        if (!enter && leaf == eofLeaf && instance != null) instance.Guard("eof", instance.EndOfFrame);
    }

    static string Position() => position < 0 ? "boot" : (inside ? "in:" : "after:") + leaves[position];

    // ------------------------------------------------------------------ points

    void Update()
    {
        if (closed || objects == null) return;
        Guard("update", () =>
        {
            step++;
            stepFrames.Add(I(Time.frameCount - frame0));
            Snapshot("update");
            Requests("update");
        });
    }

    void LateUpdate()
    {
        if (closed || step < 0) return;
        Guard("late", () =>
        {
            Snapshot("late");
            Requests("late");
        });
    }

    void EndOfFrame()
    {
        if (closed || step < 0) return;
        Snapshot("eof");
        Requests("eof");
        CheckLoop();
        if (step == FinalStep) Finish();
    }

    /// <summary>Bracket integrity: every marker pair is balanced at the end-of-frame point, and
    /// between two end-of-frame points every leaf outside FixedUpdate ran exactly once.</summary>
    void CheckLoop()
    {
        for (int i = 0; i < leaves.Length; i++) if (beforeVisits[i] != afterVisits[i]) balanced = false;
        if (lastVisits != null)
        {
            frameChecks++;
            for (int i = 0; i < leaves.Length; i++)
                if (!fixedLeaf[i] && beforeVisits[i] - lastVisits[i] != 1) everyLeafOnce = false;
            int fixedVisits = -1;
            for (int i = 0; i < leaves.Length; i++)
                if (fixedLeaf[i]) { fixedVisits = beforeVisits[i] - lastVisits[i]; break; }
            fixedPerFrame.Add(I(fixedVisits));
        }
        lastVisits = (int[])beforeVisits.Clone();
    }

    void Snapshot(string point)
    {
        var listed = FindObjectsByType<DestroyContractObserver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < Subjects.Length; i++)
            Value("snap." + I(step) + "." + point + "." + Subjects[i].Label, State(i, listed));
    }

    void Requests(string point)
    {
        foreach (var c in Cases)
            if (c.Step == step && c.Point == point) Request(c.Name, c.Subject, c.Target);
    }

    void Request(string name, string subject, string target)
    {
        int index = Index(subject);
        var reads = new List<int>();
        for (int i = 0; i < Subjects.Length; i++)
            if (Subjects[i].Label == subject || Subjects[i].Parent == subject) reads.Add(i);
        var listed = FindObjectsByType<DestroyContractObserver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (int i in reads) Value("case." + name + ".before." + Subjects[i].Label, State(i, listed));
        Event(subject, "request");
        if (target == "component") Destroy(observers[index]);
        else Destroy(objects[index]);
        Event(subject, "returned");
        listed = FindObjectsByType<DestroyContractObserver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (int i in reads) Value("case." + name + ".after." + Subjects[i].Label, State(i, listed));
    }

    /// <summary>Public state of one subject, then its observer's own callback counts ("cb").</summary>
    string State(int i, DestroyContractObserver[] listed)
    {
        var go = objects[i];
        var c = observers[i];
        bool isListed = false;
        foreach (var o in listed) if (ReferenceEquals(o, c)) isListed = true;
        var b = new StringBuilder("{");
        b.Append("\"alive\":").Append(B(go != null));
        b.Append(",\"activeSelf\":").Append(Read(() => B(go.activeSelf)));
        b.Append(",\"activeInHierarchy\":").Append(Read(() => B(go.activeInHierarchy)));
        b.Append(",\"find\":").Append(Read(() => Same(GameObject.Find(Subjects[i].Label), go)));
        b.Append(",\"comp\":").Append(B(c != null));
        b.Append(",\"enabled\":").Append(Read(() => B(c.enabled)));
        b.Append(",\"isActiveAndEnabled\":").Append(Read(() => B(c.isActiveAndEnabled)));
        b.Append(",\"getComponent\":").Append(Read(() => Same(go.GetComponent<DestroyContractObserver>(), c)));
        b.Append(",\"listed\":").Append(B(isListed));
        b.Append(",\"parent\":").Append(Read(() => go.transform.parent == null ? "null" : Q(go.transform.parent.name)));
        b.Append(",\"childCount\":").Append(Read(() => I(go.transform.childCount)));
        b.Append(",\"cb\":{\"awake\":").Append(I(c.awakeCount)).Append(",\"enable\":").Append(I(c.enableCount))
            .Append(",\"start\":").Append(I(c.startCount)).Append(",\"update\":").Append(I(c.updateCount))
            .Append(",\"late\":").Append(I(c.lateCount)).Append(",\"eof\":").Append(I(c.eofCount))
            .Append(",\"disable\":").Append(I(c.disableCount)).Append(",\"destroy\":").Append(I(c.destroyCount)).Append("}}");
        return b.ToString();
    }

    static string Same(UnityEngine.Object found, UnityEngine.Object mine)
        => found == null ? "\"none\"" : ReferenceEquals(found, mine) ? "\"same\"" : "\"other\"";

    // ------------------------------------------------------------------ events and output

    /// <summary>A lifecycle event of a subject, in raw order: probe step, frame relative to boot,
    /// bracket position.</summary>
    public static void Event(string label, string what)
    {
        if (closed || instance == null) return;
        if (label == null || !instance.traces.TryGetValue(label, out var trace))
        {
            instance.faults.Add(Q("event of an unknown subject " + label + ": " + what));
            return;
        }
        trace.Add("{\"e\":" + Q(what) + ",\"s\":" + I(instance.step) + ",\"f\":" + I(Time.frameCount - instance.frame0)
                  + ",\"p\":" + Q(Position()) + "}");
        instance.order.Add(Q(label + ":" + what));
    }

    public static void Fault(string where, Exception e)
    {
        if (closed || instance == null) return;
        instance.faults.Add(Q(where + ": " + e.GetType().Name + ": " + e.Message));
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        if (closed) return;
        string first = (condition ?? "").Split('\n')[0];
        logs.Add("{\"t\":" + Q(type.ToString()) + ",\"m\":" + Q(first) + ",\"s\":" + I(step)
                 + ",\"f\":" + I(Time.frameCount - frame0) + ",\"p\":" + Q(Position()) + "}");
    }

    void Finish()
    {
        Value("loop.balanced", B(balanced));
        Value("loop.everyLeafOncePerFrame", B(everyLeafOnce));
        Value("loop.frameChecks", I(frameChecks));
        Value("steps.frame", "[" + string.Join(",", stepFrames) + "]");
        foreach (var s in Subjects) Value("trace." + s.Label, "[" + string.Join(",", traces[s.Label]) + "]");
        Value("order", "[" + string.Join(",", order) + "]");
        Value("logs", "[" + string.Join(",", logs) + "]");
        Value("faults", "[" + string.Join(",", faults) + "]");
        Value("complete", "true");
        int code = faults.Count == 0 ? 0 : 3;
        closed = true;
        Application.logMessageReceived -= OnLog;
        string partial = Path.Combine(output, "values.jsonl.partial");
        File.WriteAllText(partial, string.Join("\n", records) + "\n");
        File.Move(partial, Path.Combine(output, "values.jsonl"));
        Diagnostic("written");
        finishedCode = code;
    }

    void OnDestroy()
    {
        // Play mode exit: put back the loop read at boot (outside any PlayerLoop iteration).
        if (installed) PlayerLoop.SetPlayerLoop(original);
        installed = false;
        if (!closed) Application.logMessageReceived -= OnLog;
        closed = true;
    }

    /// <summary>Real times, raw frames, instance IDs, visit counts and the device name: never compared.</summary>
    void Diagnostic(string what)
    {
        var ids = new List<string>();
        if (objects != null)
            for (int i = 0; i < Subjects.Length; i++)
                ids.Add(Q(Subjects[i].Label) + ":[" + I(objects[i] is null ? 0 : objects[i].GetInstanceID()) + ","
                        + I(observers[i] is null ? 0 : observers[i].GetInstanceID()) + "]");
        string line = "{\"what\":" + Q(what) + ",\"utc\":" + Q(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
            + ",\"realtime\":" + Time.realtimeSinceStartupAsDouble.ToString("R", CultureInfo.InvariantCulture)
            + ",\"frame\":" + I(Time.frameCount) + ",\"device\":" + Q(SystemInfo.graphicsDeviceName)
            + ",\"ids\":{" + string.Join(",", ids) + "}"
            + ",\"before\":[" + Ints(beforeVisits) + "],\"after\":[" + Ints(afterVisits) + "]"
            + ",\"fixedPerFrame\":[" + string.Join(",", fixedPerFrame) + "]}\n";
        File.AppendAllText(Path.Combine(output, "diagnostics.jsonl"), line);
    }

    void Value(string key, string json)
    {
        if (closed) return;
        records.Add("{\"k\":" + Q(key) + ",\"v\":" + json + "}");
    }

    void Guard(string where, Action action)
    {
        try { action(); }
        catch (Exception e) { faults.Add(Q(where + ": " + e.GetType().Name + ": " + e.Message)); }
    }

    static string Read(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return Q("!" + e.GetType().Name); }
    }

    static string Ints(int[] values)
    {
        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++) parts[i] = I(values[i]);
        return string.Join(",", parts);
    }

    static string List(List<string> values)
    {
        var parts = new List<string>();
        foreach (var v in values) parts.Add(Q(v));
        return "[" + string.Join(",", parts) + "]";
    }

    static string B(bool b) => b ? "true" : "false";
    static string I(int i) => i.ToString(CultureInfo.InvariantCulture);

    static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}

/// <summary>The type of the bracket markers inserted around PlayerLoop leaves.</summary>
public struct DestroyContractMarker
{
}

/// <summary>One per subject. Lifecycle callbacks are recorded as events in raw order; per-frame
/// callbacks (Update, LateUpdate, end-of-frame coroutine) only count, so their relative order
/// between subjects never enters the record.</summary>
public class DestroyContractObserver : MonoBehaviour
{
    public string label;
    public bool reenter;
    bool reentered;
    public int awakeCount, enableCount, startCount, updateCount, lateCount, eofCount, disableCount, destroyCount;

    void Awake()
    {
        label = gameObject.name;
        awakeCount++;
        DestroyContractProbe.Event(label, "Awake");
    }

    void OnEnable()
    {
        enableCount++;
        DestroyContractProbe.Event(label, "OnEnable");
    }

    void Start()
    {
        startCount++;
        StartCoroutine(EndOfFrames());
    }

    void Update() => updateCount++;

    void LateUpdate() => lateCount++;

    IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            eofCount++;
        }
    }

    void OnDisable()
    {
        disableCount++;
        DestroyContractProbe.Event(label, "OnDisable");
        if (!reenter || reentered) return;
        // The reentrant case: a second Destroy of the same object from its own OnDisable.
        reentered = true;
        DestroyContractProbe.Event(label, "request2");
        try { Destroy(gameObject); }
        catch (Exception e) { DestroyContractProbe.Fault("reenter", e); }
        DestroyContractProbe.Event(label, "returned2");
    }

    void OnDestroy()
    {
        destroyCount++;
        DestroyContractProbe.Event(label, "OnDestroy");
    }
}
