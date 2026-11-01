using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Object.InstantiateAsync, AsyncInstantiateOperation and InstantiateParameters, frame by frame.
/// A marker before every player-loop system names the phase; each event is one JSON line with the
/// frame (from the probe's Start), the phase and the scenario. Watched operations are polled at
/// every marker and record each change of isDone/progress/Result/IsWaitingForSceneActivation.
/// Writes INSTANTIATE_ASYNC_OUT and exits at the last frame.
/// </summary>
public class AsyncProbe : MonoBehaviour
{
    public static bool Quiet;
    public static bool Finished;
    static string s_Phase = "-";
    static string s_Scenario = "-";
    static int s_Frame0 = -1;
    static readonly StringBuilder s_Log = new StringBuilder();
    static readonly List<(string Label, AsyncInstantiateOperation Op, string Last)> s_Watched = new List<(string, AsyncInstantiateOperation, string)>();

    public static int Frame => s_Frame0 < 0 ? -1 : Time.frameCount - s_Frame0;

    public static void Record(string who, string message, bool frameless = false) =>
        s_Log.Append($"{{\"f\":{(frameless ? -9 : Frame)},\"p\":\"{s_Phase}\",\"s\":\"{s_Scenario}\",\"who\":\"{P.Esc(who)}\",\"e\":\"{P.Esc(message)}\"}}\n");

    GameObject m_Prefab;
    Transform m_Parent;
    GameObject m_Template;
    GameObject m_Hidden;
    Scene m_Extra;
    AsyncInstantiateOperation<GameObject> m_Held, m_Waiting;
    CancellationTokenSource m_Later;
    readonly Dictionary<int, Action> m_Steps = new Dictionary<int, Action>();
    static readonly Vector3 s_Pos = new Vector3(5, 6, 7);
    static readonly Quaternion s_Rot = Quaternion.Euler(0, 45, 0);

    void Awake()
    {
        InstallMarkers();
        Application.logMessageReceived += (condition, stack, type) => Record("log", type + ": " + condition);
    }

    void Start()
    {
        s_Frame0 = Time.frameCount;
        m_Prefab = Resources.Load<GameObject>("Item");
        m_Parent = GameObject.Find("Parent").transform;
        m_Template = GameObject.Find("Template");
        foreach (var item in Resources.FindObjectsOfTypeAll<ItemProbe>())
            if (item.name == "Hidden") m_Hidden = item.gameObject;
        m_Extra = SceneManager.CreateScene("Extra");
        Record("probe", $"start prefab={m_Prefab.name} hidden={m_Hidden.name} active={P.B(m_Hidden.activeSelf)}");
        Record("probe", "early update order " + LoopOrder(PlayerLoop.GetCurrentPlayerLoop()));
        Record("probe", "default early update order " + LoopOrder(PlayerLoop.GetDefaultPlayerLoop()));
        Schedule();
    }

    // ---------------------------------------------------------------- phases and polling

    static void InstallMarkers()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.subSystemList == null) continue;
            var list = new List<PlayerLoopSystem>();
            foreach (var s in phase.subSystemList)
            {
                string name = s.type != null ? s.type.Name : "?";
                list.Add(new PlayerLoopSystem { type = typeof(AsyncProbe), updateDelegate = () => Enter(name) });
                list.Add(s);
            }
            phase.subSystemList = list.ToArray();
            loop.subSystemList[i] = phase;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    /// <summary>Where the integration system is among its neighbours that run engine work.</summary>
    static string LoopOrder(PlayerLoopSystem loop)
    {
        var watched = new[] { "UpdatePreloading", "UpdateAsyncInstantiate", "UpdateAsyncReadbackManager", "ScriptRunDelayedStartupFrame" };
        var order = new List<string>();
        foreach (var phase in loop.subSystemList)
            if (phase.type == typeof(UnityEngine.PlayerLoop.EarlyUpdate))
                foreach (var s in phase.subSystemList)
                    if (s.type != null && Array.IndexOf(watched, s.type.Name) >= 0) order.Add(s.type.Name);
        return string.Join(",", order);
    }

    static void Enter(string phase)
    {
        s_Phase = phase;
        Poll();
    }

    /// <summary>Progress below 0.9 measures the copy on Unity's background thread, whose speed
    /// is not deterministic, so only its range is recorded.</summary>
    static string Progress(float p) => p >= 1f ? "1" : p == 0.9f ? "0.9" : p >= 0f && p < 0.9f ? "<0.9" : "?" + P.F(p);

    /// <summary>What polling compares. IsWaitingForSceneActivation turns true when the background
    /// copy is done, at no fixed point, so it is recorded only by the steps, after they settle.</summary>
    static string Polled(AsyncInstantiateOperation op) =>
        $"done={P.B(op.isDone)} result={(op.Result == null ? "null" : op.Result.Length.ToString())} allow={P.B(op.allowSceneActivation)}";

    static string State(AsyncInstantiateOperation op) =>
        $"done={P.B(op.isDone)} progress={Progress(op.progress)} result={(op.Result == null ? "null" : op.Result.Length.ToString())} " +
        $"waiting={P.B(op.IsWaitingForSceneActivation())} allow={P.B(op.allowSceneActivation)}";

    static void Poll()
    {
        for (int i = 0; i < s_Watched.Count; i++)
        {
            var (label, op, last) = s_Watched[i];
            var state = Polled(op);
            if (state == last) continue;
            Record(label, "state " + state + " awakened=" + ItemProbe.Awakened);
            s_Watched[i] = (label, op, state);
        }
        s_Watched.RemoveAll(w => w.Op.isDone && w.Last == Polled(w.Op));
    }

    static void Watch(string label, AsyncInstantiateOperation op)
    {
        Record(label, $"created progressAtMost0.9={P.B(op.progress <= 0.9f)} " + Polled(op) + " awakened=" + ItemProbe.Awakened);
        s_Watched.Add((label, op, Polled(op)));
        op.completed += o =>
        {
            var result = ((AsyncInstantiateOperation)o).Result;
            Record(label, "completed " + State((AsyncInstantiateOperation)o) + " awakened=" + ItemProbe.Awakened + " " + Describe(result));
        };
    }

    static string Describe(Object[] objects)
    {
        if (objects == null) return "result=null";
        var b = new StringBuilder("[");
        for (int i = 0; i < objects.Length; i++)
        {
            if (i > 0) b.Append("; ");
            b.Append(Describe(objects[i]));
        }
        return b.Append("]").ToString();
    }

    static string Describe(Object o)
    {
        if ((object)o == null) return "null";
        if (o == null) return "destroyed " + o.GetType().Name;
        var t = o as Transform ?? (o as GameObject)?.transform ?? (o as Component)?.transform;
        if (t == null) return $"{o.GetType().Name} {o.name}";
        return $"{o.GetType().Name} {o.name} parent={(t.parent != null ? t.parent.name : "-")} scene={t.gameObject.scene.name} " +
               $"active={P.B(t.gameObject.activeSelf)} sibling={t.GetSiblingIndex()} pos={P.V(t.position)} local={P.V(t.localPosition)} " +
               $"rot={P.Q(t.rotation)} lrot={P.Q(t.localRotation)} scale={P.V(t.localScale)} lossy={P.V(t.lossyScale)}";
    }

    static void Try(string label, Func<object> action)
    {
        try
        {
            var r = action();
            if (r is AsyncInstantiateOperation op) Watch(label, op);
            else Record(label, "returned " + (r is Object o ? Describe(o) : r == null ? "null" : r.ToString()));
        }
        catch (Exception e)
        {
            Record(label, "threw " + P.Ex(e));
        }
    }

    static string Counts() =>
        $"awakened={ItemProbe.Awakened} alive={ItemProbe.Alive} found={Object.FindObjectsByType<ItemProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}";

    InstantiateParameters Params(bool parent, bool worldSpace = false, bool scene = false) =>
        new InstantiateParameters { parent = parent ? m_Parent : null, worldSpace = worldSpace, scene = scene ? m_Extra : default };

    // ---------------------------------------------------------------- scenarios

    /// <summary>One asynchronous operation every second frame: Unity completes operations in the
    /// order its background copies finish, so operations started together have no fixed order.</summary>
    void Schedule()
    {
        int f = 2;
        void At(string scenario, Action step, int gap = 2)
        {
            m_Steps[f] = () => { s_Scenario = scenario; step(); };
            f += gap;
        }
        void Op(string scenario, string label, Func<object> op) => At(scenario, () => Try(label, op));

        At("single", Single);
        var positions = new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(3, 0, 0) };
        var rotations = new[] { Quaternion.Euler(0, 10, 0), Quaternion.Euler(0, 20, 0), Quaternion.Euler(0, 30, 0) };
        Op("spans", "parent3", () => Object.InstantiateAsync(m_Prefab, 3, m_Parent, positions, rotations));
        Op("spans", "root3", () => Object.InstantiateAsync(m_Prefab, 3, positions, rotations));
        Op("spans", "count2single", () => Object.InstantiateAsync(m_Prefab, 2, s_Pos, s_Rot));
        Op("spans", "count2", () => Object.InstantiateAsync(m_Prefab, 2));
        Op("spans", "count2parent", () => Object.InstantiateAsync(m_Prefab, 2, m_Parent));
        Op("spans", "count2parentpos", () => Object.InstantiateAsync(m_Prefab, 2, m_Parent, s_Pos, s_Rot));

        var two = new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
        var one = new[] { Quaternion.Euler(0, 10, 0) };
        var rotTwo = new[] { Quaternion.Euler(0, 10, 0), Quaternion.Euler(0, 20, 0) };
        Op("mismatched", "count4pos2rot1", () => Object.InstantiateAsync(m_Prefab, 4, two, one));
        Op("mismatched", "count2pos3rot2", () => Object.InstantiateAsync(m_Prefab, 2, positions, rotTwo));
        Op("mismatched", "count3posEmptyRot1", () => Object.InstantiateAsync(m_Prefab, 3, ReadOnlySpan<Vector3>.Empty, one));
        Op("mismatched", "count3pos2rotEmpty", () => Object.InstantiateAsync(m_Prefab, 3, two, ReadOnlySpan<Quaternion>.Empty));
        Op("mismatched", "count3empty", () => Object.InstantiateAsync(m_Prefab, 3, ReadOnlySpan<Vector3>.Empty, ReadOnlySpan<Quaternion>.Empty));
        Op("mismatched", "params2pos2rot1", () => Object.InstantiateAsync(m_Prefab, 2, two, one, Params(true, true)));

        At("sync", SyncParameters);

        Op("async", "parentConvenience", () => Object.InstantiateAsync(m_Prefab, m_Parent));
        Op("async", "parentPosConvenience", () => Object.InstantiateAsync(m_Prefab, m_Parent, s_Pos, s_Rot));
        Op("async", "posConvenience", () => Object.InstantiateAsync(m_Prefab, s_Pos, s_Rot));
        Op("async", "default", () => Object.InstantiateAsync(m_Prefab, new InstantiateParameters()));
        Op("async", "parent", () => Object.InstantiateAsync(m_Prefab, Params(true)));
        Op("async", "parentWorld", () => Object.InstantiateAsync(m_Prefab, Params(true, true)));
        Op("async", "scene", () => Object.InstantiateAsync(m_Prefab, Params(false, false, true)));
        Op("async", "parentScene", () => Object.InstantiateAsync(m_Prefab, Params(true, false, true)));
        Op("async", "posParent", () => Object.InstantiateAsync(m_Prefab, s_Pos, s_Rot, Params(true)));
        Op("async", "posParentWorld", () => Object.InstantiateAsync(m_Prefab, s_Pos, s_Rot, Params(true, true)));
        Op("async", "posScene", () => Object.InstantiateAsync(m_Prefab, s_Pos, s_Rot, Params(false, false, true)));
        Op("async", "count2posParent", () => Object.InstantiateAsync(m_Prefab, 2, s_Pos, s_Rot, Params(true)));
        Op("async", "immutable", () => Object.InstantiateAsync(m_Prefab, new InstantiateParameters { originalImmutable = true, worldSpace = true }));
        Op("async", "parentToken", () => Object.InstantiateAsync(m_Prefab, 1, m_Parent, s_Pos, s_Rot, CancellationToken.None));
        Op("async", "spansToken", () => Object.InstantiateAsync(m_Prefab, 2, m_Parent, two, one, CancellationToken.None));

        Op("templates", "sceneTemplate", () => Object.InstantiateAsync(m_Template));
        Op("templates", "hiddenTemplate", () => Object.InstantiateAsync(m_Hidden));
        Op("templates", "componentTemplate", () => Object.InstantiateAsync(m_Template.GetComponent<ItemProbe>(), m_Parent));
        Op("templates", "transformPrefab", () => Object.InstantiateAsync(m_Prefab.transform));
        At("templates", Textures);
        At("modified", ModifiedSource, 4);

        At("held", HeldStart);
        At("held", HeldFrame, 1);
        At("held", HeldFrame, 1);
        At("held", HeldFrame, 1);
        At("held", () => { Record("probe", "allow " + Counts()); m_Held.allowSceneActivation = true; Record("probe", "allowed " + Counts()); }, 4);

        At("cancel", CancelNow);
        At("cancel", () => Try("preCancelled", () => { var pre = new CancellationTokenSource(); pre.Cancel(); return Object.InstantiateAsync(m_Prefab, 1, null, Vector3.zero, Quaternion.identity, pre.Token); }));
        At("cancel", () => { m_Later = new CancellationTokenSource(); Try("tokenLater", () => Object.InstantiateAsync(m_Prefab, 1, null, Vector3.zero, Quaternion.identity, m_Later.Token)); });
        At("cancel", () => { Record("probe", "token cancel " + Counts()); m_Later.Cancel(); Record("probe", "token cancelled " + Counts()); });
        At("cancel", WaitingStart);
        At("cancel", () => { Record("probe", "cancel waiting " + Counts()); m_Waiting.Cancel(); Record("probe", "cancelled waiting " + State(m_Waiting) + " " + Counts()); });
        At("cancel", () => { Record("probe", "frame " + State(m_Waiting) + " " + Counts()); m_Waiting.allowSceneActivation = true; Record("probe", "allowed " + State(m_Waiting) + " " + Counts()); });
        At("cancel", () => Record("probe", "after " + State(m_Waiting) + " " + Counts()), 4);

        At("wait", Wait, 4);
        At("await", Await, 4);
        At("awaitCancel", () => AwaitCancelled(false));
        At("awaitToken", () => AwaitCancelled(true), 4);
        At("slice", Slice, 12);
        At("errors", Errors, 4);
        At("pendingDestroy", PendingDestroy);
        At("pendingDestroy", ParentDestroyed, 6);
        At("finish", Finish);
    }

    void Update()
    {
        Poll();
        if (!m_Steps.TryGetValue(Frame, out var step)) return;
        step();
        // Gives Unity's background copy time to finish before the next frame, so each operation
        // integrates in the frame after its step.
        Thread.Sleep(50);
    }

    void Single()
    {
        Record("probe", "before " + Counts());
        var op = Object.InstantiateAsync(m_Prefab);
        Record("probe", "returned type=" + op.GetType().Name + " " + Counts());
        Watch("op", op);
        Record("probe", "result before done " + Describe(op.Result));
    }

    void SyncParameters()
    {
        Quiet = true;
        Try("default", () => Object.Instantiate(m_Prefab, new InstantiateParameters()));
        Try("parent", () => Object.Instantiate(m_Prefab, Params(true)));
        Try("parentWorld", () => Object.Instantiate(m_Prefab, Params(true, true)));
        Try("scene", () => Object.Instantiate(m_Prefab, Params(false, false, true)));
        Try("parentScene", () => Object.Instantiate(m_Prefab, Params(true, false, true)));
        Try("worldNoParent", () => Object.Instantiate(m_Prefab, Params(false, true)));
        Try("posParent", () => Object.Instantiate(m_Prefab, s_Pos, s_Rot, Params(true)));
        Try("posParentWorld", () => Object.Instantiate(m_Prefab, s_Pos, s_Rot, Params(true, true)));
        Try("posScene", () => Object.Instantiate(m_Prefab, s_Pos, s_Rot, Params(false, false, true)));
        Try("immutable", () => Object.Instantiate(m_Prefab, new InstantiateParameters { originalImmutable = true }));
        Try("component", () => Object.Instantiate(m_Template.GetComponent<ItemProbe>(), Params(true, true)));
        Try("transform", () => Object.Instantiate(m_Prefab.transform, Params(true)));
        Quiet = false;
        Record("probe", "sync awake order");
        Try("awake", () => Object.Instantiate(m_Prefab, s_Pos, s_Rot, Params(true, false, true)));
        Try("awakeScene", () => Object.Instantiate(m_Prefab, Params(false, false, true)));
    }

    void Textures()
    {
        var texture = new Texture2D(2, 2) { name = "Tex" };
        Try("texture", () => Object.InstantiateAsync(texture));
        Try("textureCount2", () => Object.InstantiateAsync(texture, 2));
        Try("textureParent", () => Object.InstantiateAsync(texture, m_Parent));
        Try("syncTexture", () => Object.Instantiate(texture, new InstantiateParameters()));
        Try("syncTextureParent", () => Object.Instantiate(texture, Params(true)));
    }

    void ModifiedSource()
    {
        var source = Object.Instantiate(m_Prefab);
        source.name = "Source";
        Record("probe", "source made " + Counts());
        Try("modified", () => Object.InstantiateAsync(source));
        source.name = "Renamed";
        source.transform.position = new Vector3(9, 9, 9);
        source.transform.GetChild(0).gameObject.SetActive(false);
        Record("probe", "source changed");
    }

    void HeldStart()
    {
        m_Held = Object.InstantiateAsync(m_Prefab, 2);
        m_Held.allowSceneActivation = false;
        Watch("held", m_Held);
        Record("probe", "held " + Counts());
        Thread.Sleep(50);
        Record("probe", "held settled " + State(m_Held));
    }

    void HeldFrame() => Record("probe", "frame " + State(m_Held) + " " + Counts());

    void CancelNow()
    {
        Record("probe", "before " + Counts());
        var now = Object.InstantiateAsync(m_Prefab);
        Watch("cancelNow", now);
        now.Cancel();
        Record("probe", "cancelNow cancelled " + Polled(now) + " " + Counts());
        now.Cancel();
        Record("probe", "cancelNow again " + Polled(now));
        Thread.Sleep(50);
        Record("probe", "cancelNow settled " + State(now));
    }

    void WaitingStart()
    {
        m_Waiting = Object.InstantiateAsync(m_Prefab);
        m_Waiting.allowSceneActivation = false;
        Watch("waitingCancel", m_Waiting);
        Record("probe", "waiting " + Counts());
        Thread.Sleep(50);
        Record("probe", "waiting settled " + State(m_Waiting));
    }

    void Wait()
    {
        var op = Object.InstantiateAsync(m_Prefab, 2);
        Watch("wait", op);
        Record("probe", "wait begin " + Counts());
        op.WaitForCompletion();
        Record("probe", "wait end " + State(op) + " " + Counts());
        op.WaitForCompletion();
        Record("probe", "wait again " + State(op));
        bool ran = false;
        op.completed += _ => ran = true;
        Record("probe", "late completed handler ran=" + P.B(ran));
    }

    async void Await()
    {
        Record("probe", "await begin");
        var op = Object.InstantiateAsync(m_Prefab);
        var awaiter = op.GetAwaiter();
        Record("probe", "awaiter isCompleted=" + P.B(awaiter.IsCompleted));
        var result = await op;
        Record("probe", "await resumed length=" + result.Length + " type=" + result.GetType().Name + " " + State(op));
        var again = await op;
        Record("probe", "await done op resumed length=" + again.Length);
        Record("probe", "awaiter after isCompleted=" + P.B(op.GetAwaiter().IsCompleted) + " result=" + op.GetAwaiter().GetResult().Length);
        // The same awaiter's result read again: once for the awaiter taken before completion, twice
        // for one taken after it.
        Record("probe", "early awaiter GetResult " + Results(() => awaiter.GetResult()));
        var late = op.GetAwaiter();
        Record("probe", "late awaiter GetResult " + Results(() => late.GetResult()) + " again " + Results(() => late.GetResult()));
    }

    /// <summary>Awaiting an operation cancelled by Cancel() or by an already cancelled token.</summary>
    async void AwaitCancelled(bool token)
    {
        var source = new CancellationTokenSource();
        if (token) source.Cancel();
        var op = token
            ? Object.InstantiateAsync(m_Prefab, 1, null, Vector3.zero, Quaternion.identity, source.Token)
            : Object.InstantiateAsync(m_Prefab);
        if (!token) op.Cancel();
        var awaiter = op.GetAwaiter();
        Record("probe", "cancelled awaiter isCompleted=" + P.B(awaiter.IsCompleted));
        try
        {
            var none = await op;
            Record("probe", "cancelled await resumed result=" + (none == null ? "null" : none.Length.ToString()) + " " + State(op));
        }
        catch (Exception e)
        {
            Record("probe", "cancelled await threw " + P.Ex(e) + " " + State(op));
        }
    }

    static string Results(Func<GameObject[]> read)
    {
        try
        {
            var r = read();
            return r == null ? "null" : "length=" + r.Length;
        }
        catch (Exception e) { return "threw " + P.Ex(e); }
    }

    /// <summary>The budget API, and one large operation under a tiny budget. How many frames the
    /// background copy takes is not deterministic, so its completion is recorded without a frame.</summary>
    void Slice()
    {
        var budget = AsyncInstantiateOperation.GetIntegrationTimeMS();
        Record("probe", "default budget " + P.F(budget));
        foreach (var value in new[] { 0f, -1f, float.NaN })
        {
            try { AsyncInstantiateOperation.SetIntegrationTimeMS(value); Record("probe", "set " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ok budget=" + P.F(AsyncInstantiateOperation.GetIntegrationTimeMS())); }
            catch (Exception e) { Record("probe", "set " + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " threw " + P.Ex(e)); }
        }
        AsyncInstantiateOperation.SetIntegrationTimeMS(0.0001f);
        Record("probe", "budget " + AsyncInstantiateOperation.GetIntegrationTimeMS().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Quiet = true;
        int before = ItemProbe.Awakened;
        var op = Object.InstantiateAsync(m_Prefab, 20);
        int last = -1;
        op.completed += _ =>
        {
            Record("probe", $"tiny budget completed awakened={ItemProbe.Awakened - before} wokenBeforeCompletionFrame={P.B(last > 0)} {State(op)}", frameless: true);
            AsyncInstantiateOperation.SetIntegrationTimeMS(budget);
        };
        StartCoroutine(Sample());
        System.Collections.IEnumerator Sample()
        {
            while (!op.isDone)
            {
                last = ItemProbe.Awakened - before;
                yield return null;
            }
        }
    }

    void Errors()
    {
        // The large operation's objects stay quiet: its completion frame is not deterministic.
        Quiet = false;
        Try("asyncNull", () => Object.InstantiateAsync<GameObject>(null));
        Try("syncNull", () => Object.Instantiate<GameObject>(null, new InstantiateParameters()));
        Try("syncPosNull", () => Object.Instantiate<GameObject>(null, s_Pos, s_Rot, new InstantiateParameters()));
        Try("count0", () => Object.InstantiateAsync(m_Prefab, 0));
        Try("countMinus", () => Object.InstantiateAsync(m_Prefab, -1));
        Try("count0Null", () => Object.InstantiateAsync<GameObject>(null, 0));
        var data = ScriptableObject.CreateInstance<ProbeData>();
        data.name = "Data";
        Try("asyncScriptable", () => Object.InstantiateAsync(data));
        Try("syncScriptable", () => Object.Instantiate(data, new InstantiateParameters()));
        Try("syncPosScriptable", () => Object.Instantiate(data, s_Pos, s_Rot, new InstantiateParameters()));
        var dead = new GameObject("Dead");
        Object.DestroyImmediate(dead);
        Try("asyncDestroyed", () => Object.InstantiateAsync(dead));
        Try("syncDestroyed", () => Object.Instantiate(dead, new InstantiateParameters()));
        Try("syncPosDestroyed", () => Object.Instantiate(dead, s_Pos, s_Rot, new InstantiateParameters()));
        var doomed = new GameObject("Doomed");
        Object.Destroy(doomed);
        Try("syncDoomedParent", () => Object.Instantiate(m_Prefab, new InstantiateParameters { parent = doomed.transform }));
        Try("asyncDoomedParent", () => Object.InstantiateAsync(m_Prefab, doomed.transform));
    }

    void PendingDestroy()
    {
        var source = Object.Instantiate(m_Prefab);
        source.name = "Source";
        var op = Object.InstantiateAsync(source);
        Watch("pending", op);
        Object.DestroyImmediate(source);
        Record("probe", "source destroyed " + Polled(op) + " " + Counts());
    }

    void ParentDestroyed()
    {
        var parent = new GameObject("Gone").transform;
        var orphan = Object.InstantiateAsync(m_Prefab, parent);
        Watch("parentDestroyed", orphan);
        Object.DestroyImmediate(parent.gameObject);
        Record("probe", "parent destroyed " + Polled(orphan) + " " + Counts());
    }

    void Finish()
    {
        s_Scenario = "finish";
        Record("probe", "finish " + Counts());
        Finished = true;
        var output = Environment.GetEnvironmentVariable("INSTANTIATE_ASYNC_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, s_Log.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
