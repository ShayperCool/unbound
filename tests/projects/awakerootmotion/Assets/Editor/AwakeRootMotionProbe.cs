using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what an exception in Awake does to a script component (and, as
/// controls, exceptions in OnEnable and Start), and Animator.hasRootMotion for generic and
/// humanoid rigs (see README.md). Steps run one per frame so that Start and Update have run
/// between them.
/// </summary>
[InitializeOnLoad]
public static class AwakeRootMotionProbe
{
    const string Variable = "AWAKE_ROOT_MOTION_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();
    static readonly Dictionary<string, GameObject> s_Objects = new Dictionary<string, GameObject>();
    static IEnumerator<bool> s_Steps;

    static AwakeRootMotionProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error) s_Logs.Add(type + ": " + message);
            };
            s_Steps = Steps();
            EditorApplication.update += Tick;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Put(string key, object value)
    {
        if (value is bool b) { s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(b ? "true" : "false").Append("}\n"); return; }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"")
            .Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");
    }

    /// <summary>The callbacks logged since the last call (those starting with
    /// <paramref name="only"/>).</summary>
    static void TakeCalls(string key, string only = null)
    {
        Put(key + ".calls", string.Join(",", only == null ? Calls.Log : Calls.Log.Where(c => c.StartsWith(only, StringComparison.Ordinal))));
        Calls.Log.Clear();
        Put(key + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static void State(string key, Behaviour b) =>
        Put(key, b == null ? "null" : "enabled=" + b.enabled + " activeAndEnabled=" + b.isActiveAndEnabled);

    static GameObject Make(string name, bool active)
    {
        var go = new GameObject(name);
        go.SetActive(active);
        s_Objects[name] = go;
        return go;
    }

    static IEnumerator<bool> Steps()
    {
        yield return true;
        Calls.Log.Clear();
        s_Logs.Clear();

        // An active object: the neighbour first, then the failing script added.
        var active = Make("active", true);
        active.AddComponent<Neighbour>();
        var thrower = active.AddComponent<ThrowAwake>();
        State("awake.active.added", thrower);
        TakeCalls("awake.active.add");
        yield return true;
        yield return true;
        State("awake.active.later", thrower);
        TakeCalls("awake.active.frames");

        // Enabled again by a script: OnEnable, Start and Update, no second Awake.
        thrower.enabled = true;
        State("awake.active.reenabled", thrower);
        TakeCalls("awake.active.reenable");
        yield return true;
        yield return true;
        TakeCalls("awake.active.reenableFrames");

        // An inactive object activated with the failing script on it, then deactivated and
        // activated again.
        var inactive = Make("inactive", false);
        var second = inactive.AddComponent<ThrowAwake>();
        inactive.AddComponent<Neighbour>();
        State("awake.inactive.added", second);
        TakeCalls("awake.inactive.add");
        inactive.SetActive(true);
        State("awake.inactive.activated", second);
        TakeCalls("awake.inactive.activate");
        yield return true;
        yield return true;
        TakeCalls("awake.inactive.frames");
        inactive.SetActive(false);
        inactive.SetActive(true);
        State("awake.inactive.again", second);
        TakeCalls("awake.inactive.again");
        yield return true;

        // A script disabled before its object first activates still gets Awake.
        var disabled = Make("disabled", false);
        var third = disabled.AddComponent<ThrowAwake>();
        third.enabled = false;
        disabled.SetActive(true);
        State("awake.disabled.activated", third);
        TakeCalls("awake.disabled.activate");
        yield return true;
        third.enabled = true;
        State("awake.disabled.enabled", third);
        TakeCalls("awake.disabled.enable");
        yield return true;
        yield return true;
        TakeCalls("awake.disabled.frames");

        // A copy of an object holding the failing script (Instantiate of an active original).
        var copy = Object.Instantiate(active);
        copy.name = "copy";
        State("awake.copy.instantiated", copy.GetComponent<ThrowAwake>());
        State("awake.copy.neighbour", copy.GetComponent<Neighbour>());
        TakeCalls("awake.copy.instantiate");
        yield return true;
        yield return true;
        TakeCalls("awake.copy.frames");

        // Controls: an exception in OnEnable or in Start does not disable the script.
        var controls = Make("controls", true);
        var onEnable = controls.AddComponent<ThrowEnable>();
        var onStart = controls.AddComponent<ThrowStart>();
        State("control.enable.added", onEnable);
        State("control.start.added", onStart);
        TakeCalls("control.add");
        yield return true;
        yield return true;
        State("control.enable.later", onEnable);
        State("control.start.later", onStart);
        TakeCalls("control.frames");

        // Removing the failing script: OnDisable only when it was enabled again, then OnDestroy
        // (only its own callbacks: where the delivery falls among the frame's Updates depends on
        // the frame's fixed steps).
        Object.Destroy(thrower);
        yield return true;
        TakeCalls("awake.active.destroy", "active.ThrowAwake.");
        Object.Destroy(second);
        yield return true;
        TakeCalls("awake.inactive.destroy", "inactive.ThrowAwake.");

        foreach (var step in RootMotion()) yield return step;
    }

    // ------------------------------------------------------------ Animator.hasRootMotion

    static readonly string[] Models = { "Walker", "WalkerNoNode", "WalkerNoAvatar", "Mover", "MoverAvatar", "HumanA" };

    static string Controller(string model) => model == "HumanA" ? "HumanA" : "C_" + model + "_Full";

    static void Motion(string key, Animator a) =>
        Put(key, "hasRootMotion=" + a.hasRootMotion + " isHuman=" + a.isHuman + " avatar=" + (a.avatar != null ? a.avatar.name + ":" + a.avatar.isValid + ":" + a.avatar.isHuman : "null")
            + " controller=" + (a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "null") + " apply=" + a.applyRootMotion);

    static IEnumerable<bool> RootMotion()
    {
        var animators = new Dictionary<string, Animator>();
        foreach (var model in Models)
        {
            var source = Resources.Load<GameObject>(model);
            if (source == null) { Put("motion." + model, "missing"); continue; }
            var instance = Object.Instantiate(source);
            instance.name = model;
            var animator = instance.GetComponent<Animator>();
            Put("motion." + model + ".component", animator != null);
            if (animator == null) animator = instance.AddComponent<Animator>();
            animators[model] = animator;
            Motion("motion." + model + ".instantiated", animator);
            animator.applyRootMotion = false;
            Motion("motion." + model + ".applyOff", animator);
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(Controller(model));
            Motion("motion." + model + ".controller", animator);
            animator.applyRootMotion = true;
            Motion("motion." + model + ".applyOn", animator);
            animator.Update(0);
            Motion("motion." + model + ".updated", animator);
        }
        yield return true;
        foreach (var (model, animator) in animators)
        {
            Motion("motion." + model + ".frame", animator);
            // Another controller: the walker's clips have no curves of the mover's root and
            // the mover's no curves of the walker's root node.
            var other = model.StartsWith("Walker") ? "C_Mover_Full" : model == "HumanA" ? "C_Walker_Full" : "C_Walker_Full";
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(other);
            animator.Update(0);
            Motion("motion." + model + ".otherController", animator);
            animator.runtimeAnimatorController = null;
            Motion("motion." + model + ".noController", animator);
            animator.enabled = false;
            Motion("motion." + model + ".disabled", animator);
            animator.enabled = true;
        }
        yield return true;
        // Avatars exchanged: the walker's (root node Hips) on the one without a root node, the
        // humanoid's on a generic model, and no avatar.
        var pairs = new[] { ("WalkerNoNode", "Walker"), ("Walker", "WalkerNoNode"), ("Mover", "MoverAvatar"), ("WalkerNoAvatar", "HumanA"), ("HumanA", null) };
        foreach (var (target, from) in pairs)
        {
            if (!animators.TryGetValue(target, out var animator)) continue;
            animator.avatar = from == null ? null : animators.TryGetValue(from, out var f) ? f.avatar : null;
            Motion("motion." + target + ".avatarFrom" + (from ?? "None"), animator);
            animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(Controller(target));
            animator.Rebind();
            Motion("motion." + target + ".avatarFrom" + (from ?? "None") + ".rebound", animator);
        }
        // Avatars built at run time for the walker: with its Hips, its Leg (a child of Hips) and
        // no root motion transform, one that cannot be found, and a humanoid's description.
        var built = new List<(string, Avatar)>();
        if (animators.TryGetValue("WalkerNoAvatar", out var walker))
        {
            foreach (var root in new[] { "Hips", "Leg", "", "Missing" })
            {
                var avatar = AvatarBuilder.BuildGenericAvatar(walker.gameObject, root);
                avatar.name = "Built" + (root.Length == 0 ? "None" : root);
                built.Add((avatar.name, avatar));
                walker.avatar = avatar;
                Motion("motion.WalkerNoAvatar.built" + (root.Length == 0 ? "None" : root), walker);
                walker.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_WalkerNoAvatar_Full");
                walker.Rebind();
                walker.Update(0);
                Motion("motion.WalkerNoAvatar.built" + (root.Length == 0 ? "None" : root) + ".updated", walker);
            }
        }
        if (animators.TryGetValue("HumanA", out var human))
        {
            var imported = Resources.Load<GameObject>("HumanA").GetComponent<Animator>().avatar;
            var avatar = AvatarBuilder.BuildHumanAvatar(human.gameObject, imported.humanDescription);
            avatar.name = "BuiltHuman";
            built.Add((avatar.name, avatar));
            human.avatar = avatar;
            human.Rebind();
            Motion("motion.HumanA.builtHuman", human);
        }
        yield return true;
        foreach (var (name, avatar) in built)
            Put("motion.built." + name, "valid=" + avatar.isValid + " human=" + avatar.isHuman);
        // An Animator added to an empty object.
        var bare = Make("bare", true).AddComponent<Animator>();
        Motion("motion.bare", bare);
        yield return true;
    }
}
