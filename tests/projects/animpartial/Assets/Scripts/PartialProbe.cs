using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Plays clips that animate different parts of one rig: P (A's position and rotation, B's
/// position, C's rotation, D inactive, f), Q (A's x position and scale, D's position, g) and R
/// (A's position, f; 2 s). Records every part after each step, for legacy Animation (frames of
/// 0.125 s) and for Animator controllers (Animator.Update steps).</summary>
public class PartialProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder s_Output = new StringBuilder();
    static int s_Log;
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";

    const int Frames = 12;
    const float Step = 0.125f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("ANIM_PARTIAL_OUT") == null) return;
        Application.runInBackground = true;
        Application.logMessageReceived += (message, stack, kind) =>
        {
            if (stack.Contains("UnityEditor.")) return;
            s_Output.Append("{\"k\":\"log.").Append((s_Log++).ToString("D3")).Append("\",\"v\":{\"type\":")
                .Append(S(kind.ToString())).Append(",\"message\":").Append(S(message)).Append("}}\n");
        };
        new GameObject("PartialProbe").AddComponent<PartialProbe>();
    }

    static Transform Child(Transform parent, string name, Vector3 position, Vector3 euler, Vector3 scale)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = position;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = scale;
        return t;
    }

    static GameObject Rig()
    {
        var root = new GameObject("Rig");
        root.AddComponent<PartialTarget>();
        var a = Child(root.transform, "A", new Vector3(1, 2, 3), new Vector3(10, 20, 30), new Vector3(1.5f, 0.5f, 2f));
        Child(a, "B", new Vector3(0.2f, 1f, -0.3f), new Vector3(5, 0, 15), Vector3.one);
        Child(root.transform, "C", new Vector3(-1, 0, 2), new Vector3(0, 45, 0), Vector3.one).gameObject.AddComponent<MeshRenderer>();
        Child(root.transform, "D", new Vector3(0, 0, 1), Vector3.zero, Vector3.one);
        return root;
    }

    static string Snapshot(Transform root)
    {
        Transform a = root.Find("A"), b = root.Find("A/B"), c = root.Find("C"), d = root.Find("D");
        var target = root.GetComponent<PartialTarget>();
        return "\"a.p\":" + V(a.localPosition) + ",\"a.r\":" + Q(a.localRotation) + ",\"a.s\":" + V(a.localScale)
            + ",\"b.p\":" + V(b.localPosition) + ",\"b.r\":" + Q(b.localRotation)
            + ",\"c.p\":" + V(c.localPosition) + ",\"c.r\":" + Q(c.localRotation)
            + ",\"d.p\":" + V(d.localPosition) + ",\"d.on\":" + (d.gameObject.activeSelf ? "1" : "0")
            + ",\"f\":" + F(target.f) + ",\"g\":" + F(target.g)
            + ",\"t.on\":" + (target.enabled ? "1" : "0") + ",\"c.ren\":" + (c.GetComponent<MeshRenderer>().enabled ? "1" : "0")
            + ",\"b\":" + (target.b ? "1" : "0") + ",\"i\":" + target.i;
    }

    static void Record(string key, string values) =>
        s_Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(values == "true" ? values : "{" + values + "}").Append("}\n");

    /// <summary>Script writes between steps: an animated channel (B's position) and parts the
    /// running clips may not animate (C's position, A's scale, g).</summary>
    static void Poke(Transform root)
    {
        root.Find("C").localPosition = new Vector3(5, 5, 5);
        root.Find("A/B").localPosition = new Vector3(4, 4, 4);
        root.Find("A").localScale = new Vector3(9, 9, 9);
        root.GetComponent<PartialTarget>().g = 100f;
    }

    // ---- Animator controllers: Animator.Update(0), then ten of 0.125 s ----

    static void Case(string key, string controller, Action<Animator, int> before = null)
    {
        var root = Rig();
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
        for (int step = 0; step <= 10; step++)
        {
            before?.Invoke(animator, step);
            animator.Update(step == 0 ? 0f : Step);
            Record(key + ".step" + step, Snapshot(root.transform));
        }
        DestroyImmediate(root);
    }

    static void Animators()
    {
        Case("bools", "Bools");
        foreach (string wd in new[] { "On", "Off" })
        {
            string sw = "Switch" + wd, tree = "Tree" + wd, w = wd.ToLowerInvariant();
            // P to Q at step 3 (0.25 s), back at step 7.
            Case("switch." + w, sw, (animator, step) =>
            {
                if (step == 3) animator.SetBool("q", true);
                if (step == 7) animator.SetBool("q", false);
            });
            Case("switch." + w + ".poke", sw, (animator, step) =>
            {
                if (step == 3) animator.SetBool("q", true);
                if (step == 4 || step == 9) Poke(animator.transform);
                if (step == 7) animator.SetBool("q", false);
            });
            // Play without a transition: Q at step 3, P at step 7.
            Case("play." + w, sw, (animator, step) =>
            {
                if (step == 3) animator.Play("Q", 0, 0f);
                if (step == 7) animator.Play("P", 0, 0f);
            });
            // CrossFade over half the state (0.5 s normalized of 1 s).
            Case("crossfade." + w, sw, (animator, step) =>
            {
                if (step == 3) animator.CrossFade("Q", 0.5f, 0, 0f);
            });
            Case("tree." + w, tree, (animator, step) =>
            {
                if (step == 0) animator.SetFloat("blend", 0.3f);
                if (step == 5) animator.SetFloat("blend", 0.8f);
                if (step == 8) animator.SetFloat("blend", 1f);
            });
            Case("tree." + w + ".poke", tree, (animator, step) =>
            {
                if (step == 0) animator.SetFloat("blend", 0.3f);
                if (step == 4) Poke(animator.transform);
            });
            // The condition back to P holds when the transition to Q completes (q true for one step).
            Case("switch." + w + ".bounce", sw, (animator, step) =>
            {
                if (step == 3) animator.SetBool("q", true);
                if (step == 4) animator.SetBool("q", false);
            });
            // The condition to Q holds before the first update.
            Case("switch." + w + ".first", sw, (animator, step) =>
            {
                if (step == 0) animator.SetBool("q", true);
            });
            // Rebind after a script write: the defaults are read again from the current values.
            Case("rebind." + w, sw, (animator, step) =>
            {
                if (step == 2) { Poke(animator.transform); animator.Rebind(); }
                if (step == 3) animator.SetBool("q", true);
            });
        }
    }

    // ---- Legacy Animation: one rig per case, actions in Update, records in LateUpdate ----

    sealed class LegacyCase
    {
        public string Key;
        public Transform Root;
        public Animation Animation;
        public Action<LegacyCase, int> Act;
    }

    readonly List<LegacyCase> m_Legacy = new List<LegacyCase>();
    int m_Frame;

    void Legacy(string key, Action<LegacyCase, int> act, Action<Animation> setup = null)
    {
        var root = Rig();
        var animation = root.AddComponent<Animation>();
        animation.playAutomatically = false;
        foreach (string name in new[] { "LP", "LQ", "LR", "LS", "LBools" })
            animation.AddClip(Resources.Load<AnimationClip>(name), name);
        setup?.Invoke(animation);
        m_Legacy.Add(new LegacyCase { Key = key, Root = root.transform, Animation = animation, Act = act });
    }

    static Action<LegacyCase, int> At(params (int Frame, Action<LegacyCase> Action)[] actions) => (c, frame) =>
    {
        foreach (var (f, a) in actions)
            if (f == frame) a(c);
    };

    static string States(Animation animation)
    {
        var parts = new List<string>();
        foreach (string name in new[] { "LP", "LQ", "LR", "LS" })
        {
            var s = animation[name];
            parts.Add("\"" + name + "\":[" + (s.enabled ? "1" : "0") + "," + F(s.weight) + "," + F(s.time) + "]");
        }
        return string.Join(",", parts);
    }

    void LegacyCases()
    {
        Legacy("legacy.play", At((0, c => c.Animation.Play("LP")), (4, c => c.Animation.Play("LQ"))));
        Legacy("legacy.crossfade", At((0, c => c.Animation.Play("LP")), (3, c => c.Animation.CrossFade("LQ", 0.5f))));
        Legacy("legacy.crossfade.back", At((0, c => c.Animation.Play("LP")), (3, c => c.Animation.CrossFade("LQ", 0.25f)),
            (7, c => c.Animation.CrossFade("LP", 0.25f))));
        Legacy("legacy.blend", At((0, c => c.Animation.Play("LP")), (2, c => c.Animation.Blend("LQ", 0.5f, 0f))));
        Legacy("legacy.blend.fade", At((0, c => c.Animation.Play("LP")), (2, c => c.Animation.Blend("LQ", 1f, 0.5f))));
        Legacy("legacy.half", At((0, c => { c.Animation.Play("LP"); c.Animation["LP"].weight = 0.5f; })));
        Legacy("legacy.half.poke", At((0, c => { c.Animation.Play("LP"); c.Animation["LP"].weight = 0.5f; }),
            (4, c => Poke(c.Root))));
        // A script write before the clip first plays at half weight.
        Legacy("legacy.pokefirst", At((1, c => Poke(c.Root)),
            (2, c => { c.Animation.Play("LP"); c.Animation["LP"].weight = 0.5f; })));
        Legacy("legacy.two", At((0, c =>
        {
            c.Animation.Blend("LP", 0.3f, 0f);
            c.Animation.Blend("LQ", 0.3f, 0f);
        })));
        // P on layer 1 over Q on layer 0, at P weights 0.6 and 1 and Q weights 1 and 0.3.
        foreach (var (key, wp, wq) in new[] { ("legacy.layers", 0.6f, 1f), ("legacy.layers.low", 0.6f, 0.3f), ("legacy.layers.full", 1f, 1f) })
            Legacy(key, At((0, c =>
            {
                c.Animation.Blend("LQ", wq, 0f);
                c.Animation.Blend("LP", wp, 0f);
            })), animation => animation["LP"].layer = 1);
        // R below P (layer 1, 0.5): both animate A's position and f.
        Legacy("legacy.layers.r", At((0, c =>
        {
            c.Animation.Play("LR");
            c.Animation.Blend("LP", 0.5f, 0f);
        })), animation => animation["LP"].layer = 1);
        Legacy("legacy.additive", At((0, c =>
        {
            c.Animation.Play("LQ");
            c.Animation.Blend("LP", 0.5f, 0f);
        })), animation => { animation["LP"].blendMode = AnimationBlendMode.Additive; animation["LP"].layer = 1; });
        Legacy("legacy.stop", At((0, c => c.Animation.Play("LP")), (4, c => c.Animation.Stop()), (6, c => c.Animation.Play("LQ"))));
        Legacy("legacy.fadeout", At((0, c => c.Animation.Play("LP")), (3, c => c.Animation.Blend("LP", 0f, 0.5f))));
        Legacy("legacy.bools", At((0, c => c.Animation.Play("LBools"))));
        // A clip played once (0.5 s), and cross-faded in over 0.25 s, which also fades it out before its end.
        Legacy("legacy.once", At((0, c => c.Animation.Play("LS"))));
        Legacy("legacy.once.fade", At((0, c => c.Animation.Play("LP")), (1, c => c.Animation.CrossFade("LS", 0.25f))));
        // The GameObject deactivated for two frames.
        Legacy("legacy.deactivate", At((0, c => c.Animation.Play("LP")), (4, c => c.Root.gameObject.SetActive(false)),
            (6, c => c.Root.gameObject.SetActive(true))));
        Legacy("legacy.disable", At((0, c => c.Animation.Play("LP")), (4, c => c.Animation.enabled = false),
            (6, c => c.Animation.enabled = true)));
    }

    void Start()
    {
        Animators();
        LegacyCases();
        Time.captureDeltaTime = Step;
    }

    void Update()
    {
        foreach (var c in m_Legacy) c.Act(c, m_Frame);
    }

    void LateUpdate()
    {
        foreach (var c in m_Legacy)
            Record(c.Key + ".frame" + m_Frame, Snapshot(c.Root) + "," + States(c.Animation));
        if (++m_Frame < Frames) return;
        enabled = false;
        Record("complete", "true");

        File.WriteAllText(Environment.GetEnvironmentVariable("ANIM_PARTIAL_OUT"), s_Output.ToString());
        Finished = true;
    }
}
