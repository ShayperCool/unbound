using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Plays each controller on a fresh generic rig with fixed Animator.Update steps and
/// records the animated local transforms and script floats after every step.</summary>
public class AdditiveProbe : MonoBehaviour
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("AdditiveProbe").AddComponent<AdditiveProbe>();

    static Transform Child(Transform parent, string name, Vector3 position, Vector3 euler, Vector3 scale)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = position;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = scale;
        return t;
    }

    static string Snapshot(Animator animator)
    {
        var root = animator.transform;
        Transform a = root.Find("A"), b = root.Find("A/B"), c = root.Find("C"), d = root.Find("D");
        var target = root.GetComponent<AdditiveTarget>();
        var weights = new string[animator.layerCount];
        for (int i = 0; i < weights.Length; i++) weights[i] = F(animator.GetLayerWeight(i));
        return "{\"a.p\":" + V(a.localPosition) + ",\"a.r\":" + Q(a.localRotation) + ",\"a.s\":" + V(a.localScale)
            + ",\"b.p\":" + V(b.localPosition) + ",\"b.r\":" + Q(b.localRotation) + ",\"b.s\":" + V(b.localScale)
            + ",\"c.p\":" + V(c.localPosition) + ",\"c.r\":" + Q(c.localRotation) + ",\"d.p\":" + V(d.localPosition)
            + ",\"f\":" + F(target.f) + ",\"g\":" + F(target.g) + ",\"w\":[" + string.Join(",", weights) + "]}";
    }

    /// <summary>Eleven updates (0, then ten of 0.125 s); <paramref name="before"/> runs before each.</summary>
    static void Case(string key, string controller, Action<Animator, int> before = null)
    {
        var root = new GameObject("Rig");
        root.AddComponent<AdditiveTarget>();
        var a = Child(root.transform, "A", new Vector3(1, 2, 3), new Vector3(10, 20, 30), new Vector3(1.5f, 0.5f, 2f));
        Child(a, "B", new Vector3(0.2f, 1f, -0.3f), new Vector3(5, 0, 15), Vector3.one);
        Child(root.transform, "C", new Vector3(-1, 0, 2), new Vector3(0, 45, 0), Vector3.one);
        Child(root.transform, "D", new Vector3(0, 0, 1), Vector3.zero, Vector3.one);
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
        for (int step = 0; step <= 10; step++)
        {
            before?.Invoke(animator, step);
            animator.Update(step == 0 ? 0f : 0.125f);
            s_Output.Append("{\"k\":\"").Append(key).Append(".step").Append(step).Append("\",\"v\":").Append(Snapshot(animator)).Append("}\n");
        }
        DestroyImmediate(root);
    }

    static Action<Animator, int> At(int step, Action<Animator> action) => (animator, s) => { if (s == step) action(animator); };

    void Start()
    {
        Case("base", "Base");
        Case("basic", "Basic");
        Case("basic.poke", "Basic", At(4, animator =>
        {
            animator.transform.Find("C").localPosition = new Vector3(5, 5, 5);
            animator.transform.Find("A").localScale = new Vector3(9, 9, 9);
            animator.GetComponent<AdditiveTarget>().g = 100f;
        }));
        foreach (float w in new[] { -0.5f, 0f, 0.25f, 0.5f, 0.75f, 1.5f, 2f, 3f })
            Case("weight." + F(w), "Basic", At(0, animator => animator.SetLayerWeight(1, w)));
        foreach (float w in new[] { 0.5f, 1f, 1.5f, 2f })
            Case("big." + F(w), "Big", At(0, animator => animator.SetLayerWeight(1, w)));
        Case("weight.change", "Basic", (animator, step) =>
        {
            if (step == 3) animator.SetLayerWeight(1, 0.4f);
            if (step == 6) animator.SetLayerWeight(1, 1.7f);
            if (step == 9) animator.SetLayerWeight(1, 0f);
        });
        Case("weight.negative", "Basic", (animator, step) =>
        {
            if (step == 0) animator.SetLayerWeight(1, 0.5f);
            if (step == 3) animator.SetLayerWeight(1, -0.5f);
            if (step == 6) animator.SetLayerWeight(1, 0.25f);
            if (step == 8) animator.SetLayerWeight(1, -2f);
        });
        Case("defaultweight", "DefaultWeight");
        Case("nobase", "NoBase");
        Case("nobase.state", "NoBaseState");
        Case("refclip", "RefClip");
        Case("refself", "RefSelf");
        Case("refpartial", "RefPartial");
        Case("refchild", "RefChild");
        Case("once", "Once");
        Case("start", "Start");
        Case("mask.a", "MaskA");
        Case("mask.noroot", "MaskNoRoot");
        Case("mask.sparse", "MaskSparse");
        Case("over.mask.noroot", "OverMaskNoRoot");
        Case("over.mask.sparse", "OverMaskSparse");
        Case("stack", "Stack");
        Case("over.negative", "OverAbove", (animator, step) =>
        {
            if (step == 0) animator.SetLayerWeight(2, 0.5f);
            if (step == 3) animator.SetLayerWeight(2, -1f);
            if (step == 6) animator.SetLayerWeight(2, 0.25f);
        });
        Case("overabove", "OverAbove");
        Case("baseadditive", "BaseAdditive");
        Case("sync", "Sync", At(3, animator => animator.SetBool("go", true)));
        foreach (string name in new[] { "WdOn", "WdOff", "EmptyOn", "EmptyOff", "OverWdOn", "OverWdOff", "OverEmptyOn", "OverEmptyOff" })
            Case(name.ToLowerInvariant(), name, (animator, step) =>
            {
                if (step == 0 && name.StartsWith("Over")) animator.SetLayerWeight(1, 0.5f);
                if (step == 3) animator.SetBool("q", true);
                if (step == 7) animator.SetBool("q", false);
            });
        Case("emptyfirst", "EmptyFirst", (animator, step) =>
        {
            if (step == 3) animator.SetBool("q", true);
            if (step == 7) animator.SetBool("q", false);
        });
        // An override layer starting in an empty state without write defaults, at full and half weight.
        foreach (float w in new[] { 1f, 0.5f })
            Case("overemptyfirst." + F(w), "OverEmptyFirst", (animator, step) =>
            {
                if (step == 0) animator.SetLayerWeight(1, w);
                if (step == 3) animator.SetBool("q", true);
                if (step == 7) animator.SetBool("q", false);
            });
        Case("tree", "Tree", (animator, step) =>
        {
            if (step == 0) animator.SetFloat("blend", 0.3f);
            if (step == 5) animator.SetFloat("blend", 0.8f);
        });
        // Transitions between states with and without write defaults (both ways), in the base,
        // an override and an additive layer, at full and half layer weight; the ".poke" cases
        // also set channels from a script in the first transition.
        foreach (string kind in new[] { "Base", "Over", "Add" })
        foreach (string empty in new[] { "", "Empty" })
        foreach (string order in new[] { "OnOff", "OffOn" })
        {
            string name = "Mix" + kind + empty + order;
            foreach (float w in kind == "Base" ? new[] { 1f } : new[] { 1f, 0.5f })
                foreach (bool poke in new[] { false, true })
                {
                    if (poke && w != 1f) continue;
                    Case(name.ToLowerInvariant() + (kind == "Base" ? "" : "." + F(w)) + (poke ? ".poke" : ""), name, (animator, step) =>
                    {
                        if (step == 0 && kind != "Base") animator.SetLayerWeight(1, w);
                        if (step == 3) animator.SetBool("q", true);
                        if (step == 7) animator.SetBool("q", false);
                        if (poke && step == 4)
                        {
                            animator.transform.Find("C").localPosition = new Vector3(5, 5, 5);
                            animator.transform.Find("A/B").localPosition = new Vector3(4, 4, 4);
                            animator.GetComponent<AdditiveTarget>().g = 100f;
                        }
                    });
                }
        }

        string output = Environment.GetEnvironmentVariable("ANIMATOR_ADDITIVE_OUT") ?? "/tmp/animatoradditive";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, s_Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
