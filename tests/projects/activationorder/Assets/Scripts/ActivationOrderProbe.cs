using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// OnEnable/OnDisable order when an already-awake hierarchy is deactivated and reactivated, by
/// plain <c>GameObject.SetActive</c> and by the two <c>GameObject.SetGameObjectsActive</c>
/// overloads with one valid instance ID. Two first-wake controls are kept apart.
///
/// The probe builds every node itself (<see cref="Nodes"/>): P with children C1 and C2, and G
/// under C1; an untouched root U; W (child W1) and V (child V1), inactive before their recorders
/// are added. GameObjects are created in <see cref="CreateOrder"/>, parented in table order (so
/// C1 is P's first child), the inactive ones deactivated, then one recorder is added to each node
/// in <see cref="AddOrder"/>. Creation and recorder order deliberately differ from the hierarchy
/// order, so a creation, instance or component order cannot pass for a traversal.
///
/// This class is both the driver and the recorder. Every instance records its Awake, OnEnable,
/// Start and OnDisable in one raw trace, under its GameObject's authored label, with its own
/// activeSelf, activeInHierarchy, enabled and isActiveAndEnabled at that moment. The driver is
/// the instance on the "driver" root made by <see cref="Boot"/>; its GameObject is never
/// toggled, so it adds no callback to the observed lists unless Unity calls it.
///
/// Writes <c>UNBOUND_PROBE_ACTIVATIONORDER_OUT</c>/values.jsonl, one <c>{"k":key,"v":value}</c>
/// per line. A read records its value under its key or, when it throws, the exception under
/// <c>key.exception</c>; a step (a call made for its effect) records only its exception; a block
/// ends with <c>block.log</c>. Every trace entry lands in exactly one list: boot.calls,
/// setup.calls, settle.calls, an operation's calls, or capture.strayCalls. Instance IDs, absolute
/// frames, real times and device names go to diagnostics.jsonl, never into the record. The last
/// lines are <c>capture.unscopedLogs</c> and the single marker <c>{"k":"complete","v":true}</c>;
/// the file is written once, through a temporary name. Then <see cref="finishedCode"/> is set;
/// the Editor runner polls it. This script never ends Play mode or quits.
/// </summary>
public class ActivationOrderProbe : MonoBehaviour
{
    public const string OutputVariable = "UNBOUND_PROBE_ACTIVATIONORDER_OUT";

    /// <summary>Null until the record is written; then 0 (complete) or 3 (fatal).</summary>
    public static int? finishedCode;

    const string DriverLabel = "driver";
    const string Unlabelled = "unlabelled";
    const string Plain = "plain";
    const string Span = "span";
    const string Native = "nativeArray";

    /// <summary>Frames between the set-up and the compared operations (Start completes).</summary>
    const int SettleFrames = 2;

    /// <summary>Frames after the record is written, before the runner may leave Play mode.</summary>
    const int EndFrames = 5;

    /// <summary>The authored nodes: label, parent label (null: a root), authored activeSelf.</summary>
    static readonly (string Label, string Parent, bool Active)[] Nodes =
    {
        ("P", null, true),
        ("C1", "P", true),
        ("G", "C1", true),
        ("C2", "P", true),
        ("U", null, true),
        ("W", null, false),
        ("W1", "W", true),
        ("V", null, false),
        ("V1", "V", true),
    };

    static readonly string[] CreateOrder = { "G", "C2", "P", "C1", "U", "W1", "W", "V1", "V" };
    static readonly string[] AddOrder = { "C2", "C1", "P", "G", "U", "W1", "W", "V1", "V" };

    /// <summary>The operations in order (group "op": already awake; "wake": first wake).</summary>
    static readonly (string Group, string Name, string Call, string Target, bool Value)[] Ops =
    {
        ("op", "plainOff", Plain, "P", false),
        ("op", "plainOn", Plain, "P", true),
        ("op", "spanOff", Span, "P", false),
        ("op", "spanOn", Span, "P", true),
        ("op", "nativeOff", Native, "P", false),
        ("op", "nativeOn", Native, "P", true),
        ("wake", "plainOn", Plain, "W", true),
        ("wake", "spanOn", Span, "V", true),
    };

    // ------------------------------------------------------------------ shared state

    static readonly Dictionary<GameObject, string> Labels = new Dictionary<GameObject, string>();
    static readonly List<string> Trace = new List<string>();
    static bool closed;

    int awakes, enables, starts, disables;

    readonly StringBuilder records = new StringBuilder();
    readonly StringBuilder diagnostics = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> unscoped = new List<string>();
    readonly List<string> strays = new List<string>();
    readonly Dictionary<string, GameObject> objects = new Dictionary<string, GameObject>();
    readonly Dictionary<string, ActivationOrderProbe> recorders = new Dictionary<string, ActivationOrderProbe>();
    bool capturing;
    int consumed;
    int startFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(OutputVariable) == null) return;
        finishedCode = null;
        closed = false;
        Labels.Clear();
        Trace.Clear();
        var driver = new GameObject(DriverLabel);
        Labels[driver] = DriverLabel;
        driver.AddComponent<ActivationOrderProbe>();
    }

    // ------------------------------------------------------------------ recorder

    void Awake() { awakes++; Note("Awake"); }
    void OnEnable() { enables++; Note("OnEnable"); }
    void OnDisable() { disables++; Note("OnDisable"); }

    /// <summary>One raw trace entry: label, callback and this behaviour's own state now.</summary>
    void Note(string callback)
    {
        if (closed) return;
        string label = Labels.TryGetValue(gameObject, out var l) ? l : Unlabelled;
        Trace.Add("{\"n\":" + Q(label) + ",\"cb\":" + Q(callback) + ",\"self\":["
                  + B(gameObject.activeSelf) + "," + B(gameObject.activeInHierarchy) + ","
                  + B(enabled) + "," + B(isActiveAndEnabled) + "]}");
    }

    // ------------------------------------------------------------------ record

    void Value(string k, string json) { records.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(json).Append("}\n"); }

    static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string B(bool b) => b ? "true" : "false";
    static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
    static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Q)) + "]";
    static string Error(Exception e) => Q(e.GetType().Name + ": " + e.Message);

    /// <summary>A setup value a step failed to produce: the dependent reads record this instead.</summary>
    static T Have<T>(T value) where T : class => value ?? throw new InvalidOperationException("the setup step did not produce a value");

    void OnLog(string condition, string stack, LogType type)
    {
        (capturing ? logs : unscoped).Add(type + ": " + condition);
    }

    void Read(string k, Func<string> json)
    {
        string value;
        try { value = json(); }
        catch (Exception e) { Value(k + ".exception", Error(e)); return; }
        Value(k, value);
    }

    void Step(string k, Action action)
    {
        try { action(); }
        catch (Exception e) { Value(k + ".exception", Error(e)); }
    }

    void Block(string key, Action body)
    {
        logs.Clear();
        capturing = true;
        try { body(); }
        catch (Exception e) { Value(key + ".exception", Error(e)); }
        capturing = false;
        Value(key + ".log", List(logs));
        logs.Clear();
    }

    /// <summary>The trace entries since the last list, in raw order.</summary>
    string Since()
    {
        string json = "[" + string.Join(",", Trace.Skip(consumed)) + "]";
        consumed = Trace.Count;
        return json;
    }

    /// <summary>Records the entries since the last list under <paramref name="key"/>.</summary>
    void Take(string key) => Value(key, Since());

    /// <summary>Entries outside every list (none expected) go to capture.strayCalls, with where.</summary>
    void Stray(string where)
    {
        if (Trace.Count > consumed) strays.Add("{\"at\":" + Q(where) + ",\"calls\":" + Since() + "}");
    }

    /// <summary>A raw diagnostic (absolute frame, real time, device, ids): never compared.</summary>
    void Diagnostic(string what, string extra)
    {
        diagnostics.Append("{\"what\":").Append(Q(what))
            .Append(",\"frame\":").Append(N(Time.frameCount))
            .Append(",\"realtime\":").Append(Time.realtimeSinceStartupAsDouble.ToString("R", CultureInfo.InvariantCulture))
            .Append(",\"device\":").Append(Q(SystemInfo.graphicsDeviceName))
            .Append(",\"extra\":").Append(extra ?? "null")
            .Append("}\n");
    }

    /// <summary>Frames since the driver's Start (a relative marker).</summary>
    int RelativeFrame() => Time.frameCount - startFrame;

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        starts++;
        Note("Start");
        if (!Labels.TryGetValue(gameObject, out var label) || label != DriverLabel) yield break;
        Application.logMessageReceived += OnLog;
        startFrame = Time.frameCount;
        Diagnostic("start", null);
        int code = 0;
        try
        {
            Context();
            Take("boot.calls");
            Setup();
        }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 3;
        }
        // Awake, OnEnable and Start of the set-up complete before the compared operations.
        for (int i = 0; i < SettleFrames; i++) yield return null;
        try
        {
            if (code == 0)
            {
                Settle();
                foreach (var op in Ops) Operation(op);
            }
        }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 3;
        }
        Stray("end");
        closed = true;
        Application.logMessageReceived -= OnLog;
        Value("capture.strayCalls", "[" + string.Join(",", strays) + "]");
        Value("capture.unscopedLogs", List(unscoped));
        if (code == 0) Value("complete", "true");
        string directory = Environment.GetEnvironmentVariable(OutputVariable);
        string temporary = Path.Combine(directory, "values.jsonl.partial");
        File.WriteAllText(temporary, records.ToString());
        File.Move(temporary, Path.Combine(directory, "values.jsonl"));
        Diagnostic("written", null);
        File.WriteAllText(Path.Combine(directory, "diagnostics.jsonl"), diagnostics.ToString());
        for (int i = 0; i < EndFrames; i++) yield return null;
        finishedCode = code;
    }

    void Context()
    {
        Block("context", () =>
        {
            Read("context.unityVersion", () => Q(Application.unityVersion));
            Read("context.isBatchMode", () => B(Application.isBatchMode));
            Read("context.platform", () => Q(Application.platform.ToString()));
            Read("context.graphicsDeviceType", () => Q(SystemInfo.graphicsDeviceType.ToString()));
        });
    }

    GameObject Go(string label) => objects.TryGetValue(label, out var go) ? go : null;

    void Setup()
    {
        Block("setup", () =>
        {
            foreach (var label in CreateOrder)
                Step("setup." + label + ".step[create]", () =>
                {
                    var go = new GameObject(label);
                    Labels[go] = label;
                    objects[label] = go;
                });
            foreach (var n in Nodes)
                if (n.Parent != null)
                    Step("setup." + n.Label + ".step[parent]", () => Have(Go(n.Label)).transform.SetParent(Have(Go(n.Parent)).transform, false));
            foreach (var n in Nodes)
                if (!n.Active)
                    Step("setup." + n.Label + ".step[deactivate]", () => Have(Go(n.Label)).SetActive(false));
            foreach (var label in AddOrder)
                Step("setup." + label + ".step[recorder]", () => recorders[label] = Have(Go(label)).AddComponent<ActivationOrderProbe>());
            Take("setup.calls");
            Read("setup.tree", Tree);
            Diagnostic("setup", "{" + string.Join(",", Nodes.Select(n => Q(n.Label) + ":["
                + (Go(n.Label) != null ? N(Go(n.Label).GetInstanceID()) : "null") + ","
                + (recorders.TryGetValue(n.Label, out var r) && r != null ? N(r.GetInstanceID()) : "null") + "]")) + "}");
        });
    }

    /// <summary>The actual hierarchy, pre-order through Transform.GetChild from each authored root:
    /// label, parent label, sibling index, depth and child count.</summary>
    string Tree()
    {
        var rows = new List<string>();
        void Visit(Transform t, int depth)
        {
            string label = Labels.TryGetValue(t.gameObject, out var l) ? l : Unlabelled;
            string parent = t.parent == null ? null : Labels.TryGetValue(t.parent.gameObject, out var p) ? p : Unlabelled;
            rows.Add("{\"n\":" + Q(label) + ",\"parent\":" + Q(parent) + ",\"sibling\":" + N(t.GetSiblingIndex())
                     + ",\"depth\":" + N(depth) + ",\"children\":" + N(t.childCount) + "}");
            for (int i = 0; i < t.childCount; i++) Visit(t.GetChild(i), depth + 1);
        }
        foreach (var n in Nodes)
            if (n.Parent == null) Visit(Have(Go(n.Label)).transform, 0);
        return "[" + string.Join(",", rows) + "]";
    }

    /// <summary>One node's state: the GameObject's and its recorder's flags, its parent and sibling
    /// index, and how often each callback was recorded on it.</summary>
    string State(string label)
    {
        var go = Have(Go(label));
        var r = recorders.TryGetValue(label, out var found) ? Have(found) : throw new InvalidOperationException("no recorder");
        var parent = go.transform.parent;
        string parentLabel = parent == null ? null : Labels.TryGetValue(parent.gameObject, out var p) ? p : Unlabelled;
        return "{\"activeSelf\":" + B(go.activeSelf) + ",\"activeInHierarchy\":" + B(go.activeInHierarchy)
               + ",\"enabled\":" + B(r.enabled) + ",\"isActiveAndEnabled\":" + B(r.isActiveAndEnabled)
               + ",\"parent\":" + Q(parentLabel) + ",\"sibling\":" + N(go.transform.GetSiblingIndex())
               + ",\"counts\":{\"Awake\":" + N(r.awakes) + ",\"OnEnable\":" + N(r.enables)
               + ",\"Start\":" + N(r.starts) + ",\"OnDisable\":" + N(r.disables) + "}}";
    }

    void States(string key)
    {
        foreach (var n in Nodes) Read(key + "." + n.Label, () => State(n.Label));
    }

    void Settle()
    {
        Block("settle", () =>
        {
            Take("settle.calls");
            Read("settle.relativeFrame", () => N(RelativeFrame()));
            States("settle.state");
        });
    }

    void Operation((string Group, string Name, string Call, string Target, bool Value) op)
    {
        string key = op.Group + "." + op.Name;
        Block(key, () =>
        {
            Value(key + ".request", "{\"call\":" + Q(op.Call) + ",\"target\":" + Q(op.Target) + ",\"value\":" + B(op.Value) + "}");
            Read(key + ".relativeFrame", () => N(RelativeFrame()));
            States(key + ".before");
            var ids = default(NativeArray<int>);
            if (op.Call == Native)
                Step(key + ".step[NativeArray]", () => ids = new NativeArray<int>(new[] { Have(Go(op.Target)).GetInstanceID() }, Allocator.Persistent));
            Stray(key + ".returned");
            Read(key + ".returned", () =>
            {
                var target = Have(Go(op.Target));
                if (op.Call == Plain) target.SetActive(op.Value);
                else if (op.Call == Span) GameObject.SetGameObjectsActive(new ReadOnlySpan<int>(new[] { target.GetInstanceID() }), op.Value);
                else GameObject.SetGameObjectsActive(ids, op.Value);
                return Q("void");
            });
            Take(key + ".calls");
            if (op.Call == Native) Step(key + ".step[Dispose]", () => ids.Dispose());
            States(key + ".after");
        });
    }
}
