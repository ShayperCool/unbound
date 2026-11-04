using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The default-order recorder: the only class of this file that a scene can reference (its file
/// name). Its message set is the admitted activation order recorder's: Awake, OnEnable, Start and
/// OnDisable, each counting itself and noting one raw trace entry. It has no OnDestroy, Update or
/// other message, so a callback list it records describes this class only, not every script type.
///
/// <see cref="slot"/> names one of an object's recorders (serialized; the scene authors it, a
/// runtime add sets it). Every entry carries the authored node label, the slot, the recorder type,
/// the callback, its own activeSelf/activeInHierarchy/enabled/isActiveAndEnabled and, inside a
/// built-in case, the public built-in state read at that moment (<see cref="MechanismTrace.Peer"/>).
///
/// <see cref="MechanismDriver"/> runs the matrix; <see cref="Contact2DObserver"/> records 2D
/// contact messages. Writes <c>UNBOUND_PROBE_ACTIVATIONMECHANISMS_OUT</c>/values.jsonl and
/// diagnostics.jsonl; then <see cref="finishedCode"/> is set and the Editor runner polls it.
/// </summary>
public class ActivationMechanismsProbe : MonoBehaviour
{
    public const string OutputVariable = "UNBOUND_PROBE_ACTIVATIONMECHANISMS_OUT";

    /// <summary>Null until the record is written; then 0 (complete) or 3 (fatal).</summary>
    public static int? finishedCode;

    /// <summary>Which of its object's recorders this is.</summary>
    public string slot;

    internal int awakes, enables, starts, disables;

    void Awake() { awakes++; Note("Awake"); }
    void OnEnable() { enables++; Note("OnEnable"); }
    void Start() { starts++; Note("Start"); }
    void OnDisable() { disables++; Note("OnDisable"); }

    void Note(string callback)
    {
        if (string.IsNullOrEmpty(slot)) slot = MechanismTrace.PendingSlot ?? "";
        MechanismTrace.Note(this, slot, MechanismTrace.Recorder, callback, null, null);
    }
}

/// <summary>Records 2D contact begin/end messages of its own GameObject (no Stay, no OnDisable):
/// the other collider's label and, for collisions, the contact count, in the same raw trace.</summary>
public class Contact2DObserver : MonoBehaviour
{
    void OnCollisionEnter2D(Collision2D collision) { Collision("OnCollisionEnter2D", collision); }
    void OnCollisionExit2D(Collision2D collision) { Collision("OnCollisionExit2D", collision); }
    void OnTriggerEnter2D(Collider2D other) { Trigger("OnTriggerEnter2D", other); }
    void OnTriggerExit2D(Collider2D other) { Trigger("OnTriggerExit2D", other); }

    void Collision(string callback, Collision2D collision)
    {
        string other;
        int? contacts;
        try { other = MechanismTrace.Label(collision.collider != null ? collision.collider.gameObject : null); }
        catch (Exception e) { other = "exception: " + e.GetType().Name; }
        try { contacts = collision.contactCount; }
        catch (Exception) { contacts = -1; }
        MechanismTrace.Note(this, MechanismTrace.ObserverSlot, MechanismTrace.Observer, callback, other, contacts);
    }

    void Trigger(string callback, Collider2D other)
    {
        string label;
        try { label = MechanismTrace.Label(other != null ? other.gameObject : null); }
        catch (Exception e) { label = "exception: " + e.GetType().Name; }
        MechanismTrace.Note(this, MechanismTrace.ObserverSlot, MechanismTrace.Observer, callback, label, null);
    }
}

/// <summary>The shared raw trace. Entries are appended in emitted order only; nothing sorts,
/// removes or rewrites them. The trace closes before the record is written.</summary>
public static class MechanismTrace
{
    public const string Recorder = "recorder";
    public const string Observer = "observer";
    public const string Driver = "driver";
    public const string ObserverSlot = "o";
    public const string DriverSlot = "d";
    public const string Unlabelled = "unlabelled";

    /// <summary>The scene's authored node names (Assets/Scenes/Probe.unity).</summary>
    public static readonly string[] SceneLabels = { "S", "S1", "S2", "S21", "S3" };

    public static readonly Dictionary<GameObject, string> Labels = new Dictionary<GameObject, string>();
    public static readonly List<string> Trace = new List<string>();
    public static bool closed;

    /// <summary>The slot of the recorder being added at runtime (read by its first callback).</summary>
    public static string PendingSlot;

    /// <summary>Inside a built-in case: the case's public built-in state, read at every entry.</summary>
    public static Func<string> Peer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        Labels.Clear();
        Trace.Clear();
        closed = false;
        PendingSlot = null;
        Peer = null;
        ActivationMechanismsProbe.finishedCode = null;
    }

    /// <summary>A runtime object's authored label, a scene node's authored name, or "unlabelled".</summary>
    public static string Label(GameObject go)
    {
        if (go == null) return null;
        if (Labels.TryGetValue(go, out var label)) return label;
        return Array.IndexOf(SceneLabels, go.name) >= 0 ? go.name : Unlabelled;
    }

    /// <summary>One raw trace entry (fixed keys): label, slot, type, callback, other, contacts,
    /// own state, built-in peer state.</summary>
    public static void Note(Behaviour c, string slot, string type, string callback, string other, int? contacts)
    {
        if (closed) return;
        var go = c.gameObject;
        string peer = "null";
        if (Peer != null)
        {
            try { peer = Peer(); }
            catch (Exception e) { peer = "{\"exception\":" + Q(e.GetType().Name + ": " + e.Message) + "}"; }
        }
        Trace.Add("{\"n\":" + Q(Label(go)) + ",\"r\":" + Q(slot) + ",\"t\":" + Q(type) + ",\"cb\":" + Q(callback)
                  + ",\"other\":" + Q(other) + ",\"contacts\":" + (contacts.HasValue ? N(contacts.Value) : "null")
                  + ",\"self\":[" + B(go.activeSelf) + "," + B(go.activeInHierarchy) + "," + B(c.enabled) + ","
                  + B(c.isActiveAndEnabled) + "],\"peer\":" + peer + "}");
    }

    public static string Q(string s)
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

    public static string B(bool b) => b ? "true" : "false";
    public static string N(long n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>A public value read for its JSON, or {"exception": ...} when the read throws.</summary>
    public static string Field(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return "{\"exception\":" + Q(e.GetType().Name + ": " + e.Message) + "}"; }
    }
}

/// <summary>
/// Builds and runs the activation mechanism matrix in one Play mode session. The driver's class
/// has one message (Start, a coroutine) and its own root, which is never toggled; it notes its
/// Start with type "driver". Every node is built from the authored tables below; the scene
/// supplies the scene-loaded tree S. Keys, phases and ids: see the README.
///
/// Canonical values are labels, booleans, counts, relative frames and raw callback lists in
/// emitted order. Instance ids, absolute frames, real time and device names go to
/// diagnostics.jsonl only. Built-in cases restore the Physics2D globals they set in finally.
/// Nothing is destroyed; cases are isolated by their own objects and positions.
/// </summary>
public class MechanismDriver : MonoBehaviour
{
    const string DriverLabel = "driver";

    /// <summary>Frames between the set-up and the compared operations (Start completes).</summary>
    const int SettleFrames = 2;

    /// <summary>Frames after the record is written, before the runner may leave Play mode.</summary>
    const int EndFrames = 5;

    /// <summary>The explicit 2D step of the built-in cases (Script simulation mode).</summary>
    const float Step2D = 0.02f;

    /// <summary>Runtime nodes: tree, label, parent label (null: a root), authored activeSelf at the
    /// recorder adds. base is the admitted baseline tree (U an untouched control root).</summary>
    static readonly (string Tree, string Label, string Parent, bool Active)[] Nodes =
    {
        ("base", "P", null, true),
        ("base", "C1", "P", true),
        ("base", "G", "C1", true),
        ("base", "C2", "P", true),
        ("base", "U", null, true),
        ("offA", "A", null, true),
        ("offA", "A1", "A", true),
        ("offA", "A11", "A1", true),
        ("offA", "A2", "A", true),
        ("offA", "A21", "A2", true),
        ("mirror", "M", null, true),
        ("mirror", "M1", "M", true),
        ("mirror", "M11", "M1", true),
        ("mirror", "M2", "M", true),
        ("mirror", "M21", "M2", true),
        ("on1", "R", null, true),
        ("on1", "RY", "R", true),
        ("on1", "RX", "R", false),
        ("on1", "RZ", "R", true),
        ("on3", "L", null, true),
        ("on3", "LY", "L", true),
        ("on3", "LX", "L", false),
        ("on3", "LZ", "L", true),
    };

    /// <summary>GameObject construction order (mirror is offA's order with the subtrees swapped).</summary>
    static readonly string[] CreateOrder =
    {
        "G", "C2", "P", "C1", "U",
        "A21", "A1", "A", "A11", "A2",
        "M11", "M2", "M", "M21", "M1",
        "RZ", "RX", "R", "RY",
        "LZ", "LX", "L", "LY",
    };

    /// <summary>Recorder AddComponent order (RX/LX are self-inactive when added: no Awake yet).</summary>
    static readonly string[] AddOrder =
    {
        "C2", "C1", "P", "G", "U",
        "A1", "A21", "A", "A11", "A2",
        "M2", "M11", "M", "M21", "M1",
        "RX", "R", "RZ", "RY",
        "LX", "L", "LZ", "LY",
    };

    /// <summary>After all adds: SetActive(true) of these self-inactive nodes (their first Awake).</summary>
    static readonly string[] WakeOrder = { "RX", "LX" };

    /// <summary>After the wakes: these recorders get enabled = false, then enabled = true.</summary>
    static readonly string[] ToggleOrder = { "L" };

    /// <summary>Runtime recorders added to scene nodes after load: label, slot.</summary>
    static readonly (string Label, string Slot)[] SceneAdds = { ("S1", "rt") };

    /// <summary>The already-awake operations in order: name, tree, target, SetActive value.</summary>
    static readonly (string Name, string Tree, string Target, bool Value)[] Ops =
    {
        ("baseOff", "base", "P", false),
        ("baseOn", "base", "P", true),
        ("offAOff", "offA", "A", false),
        ("offAOn", "offA", "A", true),
        ("mirrorOff", "mirror", "M", false),
        ("mirrorOn", "mirror", "M", true),
        ("on1Off", "on1", "R", false),
        ("on1On", "on1", "R", true),
        ("on3Off", "on3", "L", false),
        ("on3On", "on3", "L", true),
        ("on2Off", "scene", "S", false),
        ("on2On", "scene", "S", true),
    };

    /// <summary>2D built-in cases: name, partner is a trigger, Physics2D.callbacksOnDisable, scripts
    /// added before the built-in on every node. Case i builds B{i}P (Rigidbody2D, Kinematic, full
    /// kinematic contacts, recorder, observer) with child B{i}C (CircleCollider2D 0.25, recorder,
    /// observer) and the external partner B{i}E (static BoxCollider2D 2x2, recorder, observer).</summary>
    static readonly (string Name, bool Trigger, bool Callbacks, bool ScriptsFirst)[] Contacts =
    {
        ("solidOnScripts", false, true, true),
        ("solidOnBuiltins", false, true, false),
        ("triggerOnScripts", true, true, true),
        ("triggerOnBuiltins", true, true, false),
        ("solidOffScripts", false, false, true),
        ("solidOffBuiltins", false, false, false),
        ("triggerOffScripts", true, false, true),
        ("triggerOffBuiltins", true, false, false),
    };

    /// <summary>Non-physics built-in cases: name, scripts first. Case i builds F{i}P with child F{i}C,
    /// each with a ParticleSystem (playing, emission off, 2 resp. 3 emitted particles) and a recorder.</summary>
    static readonly (string Name, bool ScriptsFirst)[] Particles =
    {
        ("psScripts", true),
        ("psBuiltins", false),
    };

    static readonly int[] Emitted = { 2, 3 };

    readonly StringBuilder records = new StringBuilder();
    readonly StringBuilder diagnostics = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> unscoped = new List<string>();
    readonly List<string> strays = new List<string>();
    readonly Dictionary<string, GameObject> objects = new Dictionary<string, GameObject>();
    readonly ContactPoint2D[] contactBuffer = new ContactPoint2D[16];
    bool capturing;
    int consumed;
    int startFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(ActivationMechanismsProbe.OutputVariable) == null) return;
        var driver = new GameObject(DriverLabel);
        MechanismTrace.Labels[driver] = DriverLabel;
        driver.AddComponent<MechanismDriver>();
    }

    // ------------------------------------------------------------------ record

    static string Q(string s) => MechanismTrace.Q(s);
    static string B(bool b) => MechanismTrace.B(b);
    static string N(long n) => MechanismTrace.N(n);
    static string List(IEnumerable<string> items) => "[" + string.Join(",", items.Select(Q)) + "]";
    static string Error(Exception e) => Q(e.GetType().Name + ": " + e.Message);

    /// <summary>A setup value a step failed to produce: the dependent reads record this instead.</summary>
    static T Have<T>(T value) where T : class => value ?? throw new InvalidOperationException("the setup step did not produce a value");

    void Value(string k, string json) { records.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(json).Append("}\n"); }

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
        var trace = MechanismTrace.Trace;
        string json = "[" + string.Join(",", trace.Skip(consumed)) + "]";
        consumed = trace.Count;
        return json;
    }

    void Take(string key) => Value(key, Since());

    /// <summary>Entries outside every list (none expected) go to capture.strayCalls, with where.</summary>
    void Stray(string where)
    {
        if (MechanismTrace.Trace.Count > consumed) strays.Add("{\"at\":" + Q(where) + ",\"calls\":" + Since() + "}");
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

    GameObject Go(string label) => objects.TryGetValue(label, out var go) ? go : null;

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        MechanismTrace.Note(this, MechanismTrace.DriverSlot, MechanismTrace.Driver, "Start", null, null);
        Application.logMessageReceived += OnLog;
        startFrame = Time.frameCount;
        Diagnostic("start", null);
        int code = 0;
        try
        {
            Context();
            Boot("boot");
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
                for (int i = 0; i < Contacts.Length; i++) ContactCase(Contacts[i], i);
                for (int i = 0; i < Particles.Length; i++) ParticleCase(Particles[i], i);
            }
        }
        catch (Exception e)
        {
            Value("fatal", Q(e.ToString()));
            code = 3;
        }
        Stray("end");
        MechanismTrace.closed = true;
        Application.logMessageReceived -= OnLog;
        Value("capture.strayCalls", "[" + string.Join(",", strays) + "]");
        Value("capture.unscopedLogs", List(unscoped));
        if (code == 0) Value("complete", "true");
        string directory = Environment.GetEnvironmentVariable(ActivationMechanismsProbe.OutputVariable);
        string temporary = Path.Combine(directory, "values.jsonl.partial");
        File.WriteAllText(temporary, records.ToString());
        File.Move(temporary, Path.Combine(directory, "values.jsonl"));
        Diagnostic("written", null);
        File.WriteAllText(Path.Combine(directory, "diagnostics.jsonl"), diagnostics.ToString());
        for (int i = 0; i < EndFrames; i++) yield return null;
        ActivationMechanismsProbe.finishedCode = code;
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

    /// <summary>The scene load: every entry so far (scene Awake/OnEnable in their actual order, the
    /// driver's Start), the active scene's root names and the scene tree read back.</summary>
    void Boot(string key)
    {
        Block(key, () =>
        {
            Take(key + ".calls");
            Read(key + ".sceneRoots", () => List(SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.name)));
            Step(key + ".step[find]", () =>
            {
                var root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == MechanismTrace.SceneLabels[0]);
                foreach (var t in Have(root).GetComponentsInChildren<Transform>(true))
                    if (Array.IndexOf(MechanismTrace.SceneLabels, t.gameObject.name) >= 0 && !objects.ContainsKey(t.gameObject.name))
                        objects[t.gameObject.name] = t.gameObject;
                foreach (var label in MechanismTrace.SceneLabels) Have(Go(label));
            });
            Read(key + ".scene", () => Forest(new[] { MechanismTrace.SceneLabels[0] }));
            Diagnostic("ids:" + key, Ids(MechanismTrace.SceneLabels));
        });
    }

    void Setup()
    {
        Block("setup", () =>
        {
            foreach (var label in CreateOrder)
                Step("setup." + label + ".step[create]", () =>
                {
                    var go = new GameObject(label);
                    MechanismTrace.Labels[go] = label;
                    objects[label] = go;
                });
            foreach (var n in Nodes)
                if (n.Parent != null)
                    Step("setup." + n.Label + ".step[parent]", () => Have(Go(n.Label)).transform.SetParent(Have(Go(n.Parent)).transform, false));
            foreach (var n in Nodes)
                if (!n.Active)
                    Step("setup." + n.Label + ".step[deactivate]", () => Have(Go(n.Label)).SetActive(false));
            foreach (var label in AddOrder)
                Step("setup." + label + ".step[recorder]", () => AddRecorder(Have(Go(label)), "r"));
            foreach (var label in WakeOrder)
                Step("setup." + label + ".step[wake]", () => Have(Go(label)).SetActive(true));
            foreach (var label in ToggleOrder)
                Step("setup." + label + ".step[toggle]", () =>
                {
                    var recorder = Have(Have(Go(label)).GetComponent<ActivationMechanismsProbe>());
                    recorder.enabled = false;
                    recorder.enabled = true;
                });
            foreach (var add in SceneAdds)
                Step("setup." + add.Label + ".step[recorder:" + add.Slot + "]", () => AddRecorder(Have(Go(add.Label)), add.Slot));
            Take("setup.calls");
            Read("setup.tree", () => Forest(Nodes.Where(n => n.Parent == null).Select(n => n.Label).Concat(new[] { MechanismTrace.SceneLabels[0] })));
            Diagnostic("ids:setup", Ids(CoreLabels()));
        });
    }

    static ActivationMechanismsProbe AddRecorder(GameObject go, string slot)
    {
        MechanismTrace.PendingSlot = slot;
        try
        {
            var recorder = go.AddComponent<ActivationMechanismsProbe>();
            if (string.IsNullOrEmpty(recorder.slot)) recorder.slot = slot;
            return recorder;
        }
        finally
        {
            MechanismTrace.PendingSlot = null;
        }
    }

    static IEnumerable<string> CoreLabels() => Nodes.Select(n => n.Label).Concat(MechanismTrace.SceneLabels);

    static string[] TreeLabels(string tree) =>
        tree == "scene" ? MechanismTrace.SceneLabels : Nodes.Where(n => n.Tree == tree).Select(n => n.Label).ToArray();

    /// <summary>A component's identity in the record: its type, with the slot for a recorder.</summary>
    static string ComponentName(Component c)
    {
        if (c == null) return "missing";
        var recorder = c as ActivationMechanismsProbe;
        return recorder != null ? c.GetType().Name + ":" + recorder.slot : c.GetType().Name;
    }

    /// <summary>The actual hierarchy, pre-order through Transform.GetChild from each root: label,
    /// parent, sibling index, depth, child count and the component list in its actual order.</summary>
    string Forest(IEnumerable<string> roots)
    {
        var rows = new List<string>();
        void Visit(Transform t, int depth)
        {
            string parent = t.parent == null ? null : MechanismTrace.Label(t.parent.gameObject);
            rows.Add("{\"n\":" + Q(MechanismTrace.Label(t.gameObject)) + ",\"parent\":" + Q(parent)
                     + ",\"sibling\":" + N(t.GetSiblingIndex()) + ",\"depth\":" + N(depth) + ",\"children\":" + N(t.childCount)
                     + ",\"components\":" + List(t.gameObject.GetComponents<Component>().Select(ComponentName)) + "}");
            for (int i = 0; i < t.childCount; i++) Visit(t.GetChild(i), depth + 1);
        }
        foreach (var root in roots) Visit(Have(Go(root)).transform, 0);
        return "[" + string.Join(",", rows) + "]";
    }

    /// <summary>Instance ids of nodes, their Transforms and every component, in component order
    /// (diagnostics only).</summary>
    string Ids(IEnumerable<string> labels)
    {
        return "{" + string.Join(",", labels.Select(label =>
        {
            var go = Go(label);
            if (go == null) return Q(label) + ":null";
            return Q(label) + ":{\"go\":" + N(go.GetInstanceID()) + ",\"transform\":" + N(go.transform.GetInstanceID())
                   + ",\"components\":[" + string.Join(",", go.GetComponents<Component>().Select(c =>
                       "[" + Q(ComponentName(c)) + "," + (c == null ? "null" : N(c.GetInstanceID())) + "]")) + "]}";
        })) + "}";
    }

    /// <summary>One node's state: its GameObject flags, parent and sibling index, and each recorder's
    /// slot, flags and recorded callback counts in component order.</summary>
    string State(string label)
    {
        var go = Have(Go(label));
        var parent = go.transform.parent;
        var recorders = go.GetComponents<ActivationMechanismsProbe>();
        return "{\"activeSelf\":" + B(go.activeSelf) + ",\"activeInHierarchy\":" + B(go.activeInHierarchy)
               + ",\"parent\":" + Q(parent == null ? null : MechanismTrace.Label(parent.gameObject))
               + ",\"sibling\":" + N(go.transform.GetSiblingIndex())
               + ",\"recorders\":[" + string.Join(",", recorders.Select(r =>
                   "{\"r\":" + Q(r.slot) + ",\"enabled\":" + B(r.enabled) + ",\"isActiveAndEnabled\":" + B(r.isActiveAndEnabled)
                   + ",\"counts\":{\"Awake\":" + N(r.awakes) + ",\"OnEnable\":" + N(r.enables) + ",\"Start\":" + N(r.starts)
                   + ",\"OnDisable\":" + N(r.disables) + "}}")) + "]}";
    }

    string States(IEnumerable<string> labels) => "{" + string.Join(",", labels.Select(l => Q(l) + ":" + MechanismTrace.Field(() => State(l)))) + "}";

    void Settle()
    {
        Block("settle", () =>
        {
            Take("settle.calls");
            Read("settle.relativeFrame", () => N(RelativeFrame()));
            foreach (var label in CoreLabels()) Read("settle.state." + label, () => State(label));
        });
    }

    void Operation((string Name, string Tree, string Target, bool Value) op)
    {
        string key = "op." + op.Name;
        var labels = TreeLabels(op.Tree);
        Block(key, () =>
        {
            Value(key + ".request", "{\"target\":" + Q(op.Target) + ",\"value\":" + B(op.Value) + "}");
            Read(key + ".relativeFrame", () => N(RelativeFrame()));
            foreach (var label in labels) Read(key + ".before." + label, () => State(label));
            Stray(key + ".returned");
            Read(key + ".returned", () =>
            {
                Have(Go(op.Target)).SetActive(op.Value);
                return Q("void");
            });
            Take(key + ".calls");
            foreach (var label in labels) Read(key + ".after." + label, () => State(label));
        });
    }

    // ------------------------------------------------------------------ built-in cases

    GameObject Node(string label, Vector3 position, string parent)
    {
        var go = new GameObject(label);
        MechanismTrace.Labels[go] = label;
        objects[label] = go;
        go.transform.position = position;
        if (parent != null) go.transform.SetParent(Have(Go(parent)).transform, true);
        return go;
    }

    static string Globals() =>
        "{\"simulationMode\":" + Q(Physics2D.simulationMode.ToString()) + ",\"callbacksOnDisable\":" + B(Physics2D.callbacksOnDisable) + "}";

    /// <summary>The 2D public state of a case: collider/body/partner flags, touching and contact counts.</summary>
    string PhysicsState(Rigidbody2D body, Collider2D collider, Collider2D partner)
    {
        var buffer = contactBuffer;
        return "{\"colliderEnabled\":" + MechanismTrace.Field(() => B(Have(collider).enabled))
               + ",\"colliderActive\":" + MechanismTrace.Field(() => B(Have(collider).isActiveAndEnabled))
               + ",\"bodySimulated\":" + MechanismTrace.Field(() => B(Have(body).simulated))
               + ",\"attached\":" + MechanismTrace.Field(() => B(Have(collider).attachedRigidbody == body))
               + ",\"partnerActive\":" + MechanismTrace.Field(() => B(Have(partner).isActiveAndEnabled))
               + ",\"touching\":" + MechanismTrace.Field(() => B(Physics2D.IsTouching(Have(collider), Have(partner))))
               + ",\"colliderContacts\":" + MechanismTrace.Field(() => N(Have(collider).GetContacts(buffer)))
               + ",\"partnerContacts\":" + MechanismTrace.Field(() => N(Have(partner).GetContacts(buffer))) + "}";
    }

    string CaseState(string[] labels, string builtinKey, Func<string> builtin) =>
        "{\"nodes\":" + States(labels) + ",\"" + builtinKey + "\":" + MechanismTrace.Field(builtin) + "}";

    string ComponentLists(string[] labels) =>
        "{" + string.Join(",", labels.Select(l => Q(l) + ":" + List(Have(Go(l)).GetComponents<Component>().Select(ComponentName)))) + "}";

    void ContactCase((string Name, bool Trigger, bool Callbacks, bool ScriptsFirst) c, int index)
    {
        string key = "contact." + c.Name;
        string p = "B" + index + "P", child = "B" + index + "C", partner = "B" + index + "E";
        var labels = new[] { p, child, partner };
        Block(key, () =>
        {
            Value(key + ".request", "{\"trigger\":" + B(c.Trigger) + ",\"callbacksOnDisable\":" + B(c.Callbacks)
                  + ",\"layout\":" + Q(c.ScriptsFirst ? "scriptsFirst" : "builtinsFirst") + ",\"target\":" + Q(p) + "}");
            var mode = Physics2D.simulationMode;
            bool callbacks = Physics2D.callbacksOnDisable;
            Read(key + ".globals", Globals);
            Rigidbody2D body = null;
            Collider2D collider = null, box = null;
            try
            {
                Step(key + ".step[globals]", () =>
                {
                    Physics2D.simulationMode = SimulationMode2D.Script;
                    Physics2D.callbacksOnDisable = c.Callbacks;
                });
                float x = 20f * (index + 1);
                Step(key + ".step[build]", () =>
                {
                    Node(partner, new Vector3(x, 0, 0), null);
                    Node(p, new Vector3(x - 0.5f, 0, 0), null);
                    Node(child, new Vector3(x - 0.5f, 0, 0), p);
                });
                Step(key + ".step[components]", () =>
                {
                    var pGo = Have(Go(p));
                    var cGo = Have(Go(child));
                    var eGo = Have(Go(partner));
                    if (c.ScriptsFirst) { AddRecorder(pGo, "r"); pGo.AddComponent<Contact2DObserver>(); }
                    body = pGo.AddComponent<Rigidbody2D>();
                    body.bodyType = RigidbodyType2D.Kinematic;
                    body.gravityScale = 0;
                    body.useFullKinematicContacts = true;
                    if (!c.ScriptsFirst) { AddRecorder(pGo, "r"); pGo.AddComponent<Contact2DObserver>(); }
                    if (c.ScriptsFirst) { AddRecorder(cGo, "r"); cGo.AddComponent<Contact2DObserver>(); }
                    var circle = cGo.AddComponent<CircleCollider2D>();
                    circle.radius = 0.25f;
                    collider = circle;
                    if (!c.ScriptsFirst) { AddRecorder(cGo, "r"); cGo.AddComponent<Contact2DObserver>(); }
                    if (c.ScriptsFirst) { AddRecorder(eGo, "r"); eGo.AddComponent<Contact2DObserver>(); }
                    var square = eGo.AddComponent<BoxCollider2D>();
                    square.size = new Vector2(2, 2);
                    square.isTrigger = c.Trigger;
                    box = square;
                    if (!c.ScriptsFirst) { AddRecorder(eGo, "r"); eGo.AddComponent<Contact2DObserver>(); }
                });
                Take(key + ".setup.calls");
                Read(key + ".components", () => ComponentLists(labels));
                Diagnostic("ids:" + key, Ids(labels));
                MechanismTrace.Peer = () => PhysicsState(body, collider, box);
                Read(key + ".sim1.returned", () => B(Physics2D.Simulate(Step2D)));
                Take(key + ".sim1.calls");
                Read(key + ".sim2.returned", () => B(Physics2D.Simulate(Step2D)));
                Take(key + ".sim2.calls");
                Read(key + ".before", () => CaseState(labels, "physics", () => PhysicsState(body, collider, box)));
                Stray(key + ".returned");
                Read(key + ".returned", () =>
                {
                    Have(Go(p)).SetActive(false);
                    return Q("void");
                });
                Take(key + ".calls");
                Read(key + ".after", () => CaseState(labels, "physics", () => PhysicsState(body, collider, box)));
                Read(key + ".sim3.returned", () => B(Physics2D.Simulate(Step2D)));
                Take(key + ".sim3.calls");
                Read(key + ".final", () => CaseState(labels, "physics", () => PhysicsState(body, collider, box)));
            }
            finally
            {
                MechanismTrace.Peer = null;
                Step(key + ".step[restore]", () =>
                {
                    Physics2D.simulationMode = mode;
                    Physics2D.callbacksOnDisable = callbacks;
                });
                Read(key + ".restored", Globals);
            }
        });
    }

    /// <summary>Each case system's playing/emitting/paused/stopped flags and particle count.</summary>
    string ParticleState(string[] labels) =>
        "{" + string.Join(",", labels.Select(l =>
        {
            var ps = Go(l) != null ? Go(l).GetComponent<ParticleSystem>() : null;
            return Q(l) + ":{\"isPlaying\":" + MechanismTrace.Field(() => B(Have(ps).isPlaying))
                   + ",\"isEmitting\":" + MechanismTrace.Field(() => B(Have(ps).isEmitting))
                   + ",\"isPaused\":" + MechanismTrace.Field(() => B(Have(ps).isPaused))
                   + ",\"isStopped\":" + MechanismTrace.Field(() => B(Have(ps).isStopped))
                   + ",\"particleCount\":" + MechanismTrace.Field(() => N(Have(ps).particleCount)) + "}";
        })) + "}";

    void ParticleCase((string Name, bool ScriptsFirst) c, int index)
    {
        string key = "particles." + c.Name;
        string p = "F" + index + "P", child = "F" + index + "C";
        var labels = new[] { p, child };
        Block(key, () =>
        {
            Value(key + ".request", "{\"layout\":" + Q(c.ScriptsFirst ? "scriptsFirst" : "builtinsFirst") + ",\"target\":" + Q(p) + "}");
            try
            {
                float y = 20f * (index + 1);
                Step(key + ".step[build]", () =>
                {
                    Node(p, new Vector3(0, y, 0), null);
                    Node(child, new Vector3(0, y, 0), p);
                });
                Step(key + ".step[components]", () =>
                {
                    foreach (var label in labels)
                    {
                        var go = Have(Go(label));
                        if (c.ScriptsFirst) AddRecorder(go, "r");
                        go.AddComponent<ParticleSystem>();
                        if (!c.ScriptsFirst) AddRecorder(go, "r");
                    }
                });
                Step(key + ".step[configure]", () =>
                {
                    foreach (var label in labels)
                    {
                        var ps = Have(Have(Go(label)).GetComponent<ParticleSystem>());
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = ps.main;
                        main.playOnAwake = false;
                        var emission = ps.emission;
                        emission.enabled = false;
                        ps.Play(false);
                    }
                });
                Take(key + ".setup.calls");
                Read(key + ".components", () => ComponentLists(labels));
                Diagnostic("ids:" + key, Ids(labels));
                Step(key + ".step[emit]", () =>
                {
                    for (int i = 0; i < labels.Length; i++) Have(Have(Go(labels[i])).GetComponent<ParticleSystem>()).Emit(Emitted[i]);
                });
                MechanismTrace.Peer = () => ParticleState(labels);
                Read(key + ".before", () => CaseState(labels, "particles", () => ParticleState(labels)));
                Stray(key + ".returned");
                Read(key + ".returned", () =>
                {
                    Have(Go(p)).SetActive(false);
                    return Q("void");
                });
                Take(key + ".calls");
                Read(key + ".after", () => CaseState(labels, "particles", () => ParticleState(labels)));
            }
            finally
            {
                MechanismTrace.Peer = null;
            }
        });
    }
}
