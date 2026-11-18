using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

// Queued scene loads under the per-frame integration time limit that
// Application.backgroundLoadingPriority selects. A busy wait in PlainA's Awake makes a
// scene's integration take a known minimum time, so a case lies clearly beyond the limit.
// Below the limit the frames depend on the Editor's own integration cost and machine load:
// such cases record their frames in the sidecar, and the canonical record checks each gap
// between consecutive completions against the time the preloading pass measured.
public sealed partial class SceneLoadLoop
{
    sealed class Tracked
    {
        public string Name;
        public AsyncOperation Op;
        public bool Load = true;
        public int Order, Frame = -1, RequestFrame;
        public string Phase, LastPending = "none";
        public readonly HashSet<int> ReadyAtPass = new HashSet<int>();
        public readonly HashSet<int> AllowedAtPass = new HashSet<int>();
    }

    // Main-thread integration limits in milliseconds (Scripting API 6000.0,
    // Application.backgroundLoadingPriority: Low 2, BelowNormal 4, Normal 10, High 50).
    static int LimitMilliseconds(ThreadPriority priority)
    {
        switch (priority)
        {
            case ThreadPriority.Low: return 2;
            case ThreadPriority.BelowNormal: return 4;
            case ThreadPriority.Normal: return 10;
            case ThreadPriority.High: return 50;
            default: return 4;
        }
    }

    static int s_AwakeDelayMs;
    static string s_AwakeDelayScene;
    static bool s_Budget;
    static bool s_BudgetCanonical;
    static string s_BudgetCase;
    static int s_BudgetFirst;
    static int s_Disposable;
    static List<Tracked> s_BudgetOps;
    static readonly Dictionary<int, double> s_PassMilliseconds = new Dictionary<int, double>();
    static readonly Stopwatch s_Preloading = new Stopwatch();

    public static void AwakeDelay(GameObject target)
    {
        if (s_AwakeDelayMs <= 0 || target.name != "PlainA") return;
        if (s_AwakeDelayScene != "*" && target.scene.name != s_AwakeDelayScene) return;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < s_AwakeDelayMs) { }
    }

    static void BudgetPreloading(bool before)
    {
        if (!s_Budget || s_BudgetCase == null) return;
        int frame = Time.frameCount - s_BudgetFirst;
        if (before)
        {
            foreach (var t in s_BudgetOps)
            {
                if (!t.Op.isDone && t.Op.progress >= .9f) t.ReadyAtPass.Add(frame);
                if (t.Op.allowSceneActivation) t.AllowedAtPass.Add(frame);
            }
            s_Preloading.Restart();
            return;
        }
        s_Preloading.Stop();
        double ms = s_Preloading.Elapsed.TotalMilliseconds;
        s_PassMilliseconds[frame] = ms;
        Diagnostics.Add("budget|" + s_BudgetCase + "|" + frame + "|preloading.ms=" + ms.ToString("0.00", CultureInfo.InvariantCulture));
    }

    static void BudgetLine(string text, bool canonical)
    {
        string line = "budget|" + s_BudgetCase + "|" + text;
        if (canonical) Lines.Add(line);
        else Diagnostics.Add(line);
    }

    static string Brief(AsyncOperation op) =>
        op.progress.ToString("0.000", CultureInfo.InvariantCulture) + "," + op.isDone + "," + op.allowSceneActivation;

    static Tracked LoadTracked(string name, string scene, LoadSceneMode mode = LoadSceneMode.Additive) =>
        new Tracked { Name = name, Op = SceneManager.LoadSceneAsync(scene, mode) };

    static Tracked UnloadDisposable(string name)
    {
        var scene = SceneManager.CreateScene("BudgetDisposable" + (++s_Disposable));
        return new Tracked { Name = name, Op = SceneManager.UnloadSceneAsync(scene), Load = false };
    }

    static Tracked UnloadUnused(string name) =>
        new Tracked { Name = name, Op = Resources.UnloadUnusedAssets(), Load = false };

    // A pass that integrates X continues while its rounded elapsed time is below the limit.
    // Continuing, the next queued operation Y, if already prepared, integrates in the same pass
    // (an unload or unused-asset operation) or is armed there and integrates in the next pass
    // (a scene load); otherwise each takes one pass more. Passes within 0.6 ms of the rounding
    // boundary, Y not yet prepared or held, and a Y whose own time is inside the pass are not
    // decided by this rule and are skipped.
    static string CheckLimit(List<Tracked> ops, int limit)
    {
        var done = ops.FindAll(t => t.Frame >= 0);
        done.Sort((a, b) => a.Order.CompareTo(b.Order));
        var problems = new List<string>();
        int checkedGaps = 0;
        for (int i = 1; i < done.Count; i++)
        {
            Tracked x = done[i - 1], y = done[i];
            if (x.Phase != "preloading.before" || y.Phase != "preloading.before") continue;
            if (!s_PassMilliseconds.TryGetValue(x.Frame, out double ms)) continue;
            if (Math.Abs(ms - (limit - .5)) < .6) continue;
            bool under = Math.Floor(ms + .5) < limit;
            int predicted;
            if (y.Load)
            {
                if (!y.ReadyAtPass.Contains(x.Frame)) continue;
                predicted = under ? 1 : 2;
                if (!y.AllowedAtPass.Contains(x.Frame + predicted)) continue;
            }
            else
            {
                if (y.RequestFrame >= x.Frame) continue;
                predicted = under ? 0 : 1;
                if (!under && y.Frame == x.Frame) continue;
            }
            checkedGaps++;
            int gap = y.Frame - x.Frame;
            if (gap != predicted)
                problems.Add(x.Name + ">" + y.Name + ":gap=" + gap + ",expected=" + predicted
                    + ",ms=" + ms.ToString("0.00", CultureInfo.InvariantCulture));
        }
        Diagnostics.Add("budget|" + s_BudgetCase + "|limit.checked=" + checkedGaps);
        return problems.Count == 0 ? "consistent" : "inconsistent|" + string.Join("|", problems);
    }

    static IEnumerator BudgetCase(string name, bool exactFrames, ThreadPriority priority, int delayMs, string delayScene,
        Func<List<Tracked>> request, Action<List<Tracked>, int> perFrame = null, bool exactOrder = true, bool exactLoadOrder = true)
    {
        Application.backgroundLoadingPriority = priority;
        s_AwakeDelayMs = delayMs;
        s_AwakeDelayScene = delayScene;
        yield return null;
        s_BudgetCase = name;
        s_BudgetFirst = Time.frameCount;
        s_PassMilliseconds.Clear();
        s_Order = 0;
        var ops = s_BudgetOps = request();
        foreach (var t in ops) Attach(t);
        var readBack = Application.backgroundLoadingPriority;
        BudgetLine("priority=" + readBack + ",delay=" + delayMs + (delayMs > 0 ? "@" + delayScene : ""), true);
        while (true)
        {
            yield return null;
            int frame = Time.frameCount - s_BudgetFirst;
            var states = new List<string>();
            bool pending = false;
            foreach (var t in ops)
            {
                states.Add(t.Name + ":" + Brief(t.Op));
                if (t.Op.isDone) continue;
                pending = true;
                t.LastPending = t.Op.progress.ToString("0.000", CultureInfo.InvariantCulture) + "," + t.Op.allowSceneActivation;
            }
            BudgetLine("f=" + frame + "|" + string.Join("|", states), false);
            int count = ops.Count;
            perFrame?.Invoke(ops, frame);
            if (!pending && ops.Count == count) break;
            if (frame > 40) throw new Exception("budget case timeout " + name);
        }
        foreach (var t in ops)
        {
            BudgetLine(t.Name + "|order=" + t.Order + ",phase=" + t.Phase + ",pending=" + t.LastPending, exactOrder);
            BudgetLine(t.Name + "|frame=" + t.Frame, exactFrames);
        }
        var loads = ops.FindAll(t => t.Load && t.Frame >= 0);
        loads.Sort((a, b) => a.Order.CompareTo(b.Order));
        BudgetLine("loads.order=" + string.Join(",", loads.ConvertAll(t => t.Name)), exactLoadOrder);
        BudgetLine("limit=" + CheckLimit(ops, LimitMilliseconds(readBack)), true);
        s_AwakeDelayMs = 0;
        s_BudgetCase = null;
        // Return to the boot scene alone before the next case.
        var unloads = new List<AsyncOperation>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && (scene.name == "First" || scene.name == "Second") && SceneManager.sceneCount - unloads.Count > 1)
                unloads.Add(SceneManager.UnloadSceneAsync(scene));
        }
        foreach (var op in unloads) yield return op;
        yield return null;
        yield return null;
    }

    static int s_Order;

    static Tracked Attach(Tracked t)
    {
        t.RequestFrame = Time.frameCount - s_BudgetFirst;
        Diagnostics.Add("budget|" + s_BudgetCase + "|request|" + t.RequestFrame + "|" + t.Name + "|" + Brief(t.Op));
        t.Op.completed += op => { t.Order = ++s_Order; t.Frame = Time.frameCount - s_BudgetFirst; t.Phase = Phase; };
        return t;
    }

    static void Release(List<Tracked> ops, int index, int frame, int at)
    {
        if (frame == at) ops[index].Op.allowSceneActivation = true;
    }

    IEnumerator QueueBudgetControl()
    {
        s_Budget = true;
        var initial = Application.backgroundLoadingPriority;
        Lines.Add("budget|initial|priority=" + initial);
        var high = ThreadPriority.High;
        var below = ThreadPriority.BelowNormal;
        var additive = LoadSceneMode.Additive;
        var single = LoadSceneMode.Single;
        Func<List<Tracked>> two = () => new List<Tracked> { LoadTracked("first", "First"), LoadTracked("second", "Second") };
        Func<List<Tracked>> three = () => new List<Tracked> { LoadTracked("first", "First"), LoadTracked("second", "Second"), LoadTracked("third", "First") };
        Func<LoadSceneMode, LoadSceneMode, Func<List<Tracked>>> modes = (a, b) =>
            () => new List<Tracked> { LoadTracked("first", "First", a), LoadTracked("second", "Second", b) };

        // The first queue of the session includes one-time costs; its timing is not a control.
        yield return BudgetCase("warmup", false, below, 0, "*", two);
        // The time limit follows the priority read back after the assignment.
        yield return BudgetCase("two.high.d0", false, high, 0, "*", two);
        yield return BudgetCase("two.belownormal.d2", false, below, 2, "First", two);
        yield return BudgetCase("two.low.d2", true, ThreadPriority.Low, 2, "First", two);
        yield return BudgetCase("two.high.d2", true, high, 2, "First", two);
        yield return BudgetCase("two.normal.d2", true, ThreadPriority.Normal, 2, "First", two);
        yield return BudgetCase("two.high.d25", true, high, 25, "First", two);
        yield return BudgetCase("two.belownormal.d25", true, below, 25, "First", two);
        yield return BudgetCase("two.high.d70", true, high, 70, "First", two);
        yield return BudgetCase("two.belownormal.d0", false, below, 0, "*", two);
        yield return BudgetCase("three.belownormal.d0", false, below, 0, "*", three);
        yield return BudgetCase("three.belownormal.d70", true, below, 70, "*", three);
        yield return BudgetCase("unload.belownormal.d0", false, below, 0, "*",
            () => new List<Tracked> { LoadTracked("first", "First"), UnloadDisposable("unload"), LoadTracked("second", "Second") });
        yield return BudgetCase("unload.belownormal.d70", false, below, 70, "First",
            () => new List<Tracked> { LoadTracked("first", "First"), UnloadDisposable("unload"), LoadTracked("second", "Second") });
        yield return BudgetCase("heldsecond.belownormal.d0", true, below, 0, "*",
            () => { var ops = two(); ops[1].Op.allowSceneActivation = false; return ops; },
            (ops, frame) => Release(ops, 1, frame, 5));
        yield return BudgetCase("heldfirst.belownormal.d0", false, below, 0, "*",
            () => { var ops = two(); ops[0].Op.allowSceneActivation = false; return ops; },
            (ops, frame) => Release(ops, 0, frame, 4));
        yield return BudgetCase("heldfirst.belownormal.d70", true, below, 70, "First",
            () => { var ops = two(); ops[0].Op.allowSceneActivation = false; return ops; },
            (ops, frame) => Release(ops, 0, frame, 4));
        // Resources.UnloadUnusedAssets shares the queue; the loader starts nothing after it
        // until it completes.
        yield return BudgetCase("unused.alone", true, below, 0, "*",
            () => new List<Tracked> { UnloadUnused("unused") });
        // AsyncOperation.priority orders operations that have not started. A request issued to an
        // idle loader can start before its priority is set, so this order is only a diagnostic.
        yield return BudgetCase("priority.request", false, below, 0, "*",
            () => { var ops = three(); ops[2].Op.priority = 2; ops[1].Op.priority = 1; return ops; },
            exactOrder: false, exactLoadOrder: false);
        // The unused-asset operation keeps every load of this frame unstarted until it completes,
        // so the priorities order the three loads. Whether the loader took that operation
        // before the priorities were set varies, so only the loads' order is exact.
        yield return BudgetCase("priority.blocked", false, below, 0, "*",
            () => {
                var ops = new List<Tracked> { UnloadUnused("unused") };
                ops.AddRange(three());
                ops[3].Op.priority = 2;
                ops[2].Op.priority = 1;
                return ops;
            }, exactOrder: false);
        // Once started, a later priority change has no effect.
        yield return BudgetCase("priority.later", false, below, 0, "*", three,
            (ops, frame) => { if (frame == 1) { ops[2].Op.priority = 2; ops[1].Op.priority = 1; } });
        yield return BudgetCase("additive.then.single", false, below, 0, "*", modes(additive, single));
        // Single loads replace the boot scene; the driver persists. The single integration's own
        // time decides whether the following cleanup shares its pass; the delay makes it exceed
        // the limit, and the d0 repeats stay in the sidecar.
        yield return BudgetCase("single.then.additive.d0", false, below, 0, "*", modes(single, additive));
        yield return BudgetCase("single.then.additive.d70", true, below, 70, "First", modes(single, additive));
        yield return BudgetCase("single.then.single.d0", false, below, 0, "*", modes(single, single));
        yield return BudgetCase("single.then.single.d70", true, below, 70, "First", modes(single, single));
        yield return BudgetCase("single.then.later.additive.d70", true, below, 70, "First",
            () => new List<Tracked> { LoadTracked("first", "First", single) },
            (ops, frame) => { if (frame == 2) ops.Add(Attach(LoadTracked("second", "Second"))); });
#if UNITY_EDITOR
        // Without the Editor's forced unload after a Single load nothing waits behind it.
        bool force = UnityEditor.EditorSettings.forceAssetUnloadAndGCOnSceneLoad;
        Lines.Add("budget|forceAssetUnloadAndGCOnSceneLoad=" + force);
        UnityEditor.EditorSettings.forceAssetUnloadAndGCOnSceneLoad = false;
        yield return BudgetCase("noforce.single.then.additive.d70", true, below, 70, "First", modes(single, additive));
        yield return BudgetCase("noforce.single.then.later.additive.d70", true, below, 70, "First",
            () => new List<Tracked> { LoadTracked("first", "First", single) },
            (ops, frame) => { if (frame == 2) ops.Add(Attach(LoadTracked("second", "Second"))); });
        UnityEditor.EditorSettings.forceAssetUnloadAndGCOnSceneLoad = force;
#endif
        Application.backgroundLoadingPriority = initial;
        s_Budget = false;
    }
}
