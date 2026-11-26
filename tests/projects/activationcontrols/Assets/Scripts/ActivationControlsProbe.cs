using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $ACTIVATIONCONTROLS_OUT (tests/value_probe.py rows): the order of
/// OnDisable and OnEnable when an already-awake hierarchy is deactivated and reactivated, in
/// cases that tell apart the orders that fit tests/projects/activationorder's capture: the order
/// the recorders were added, the order they were last enabled, the order their GameObjects were
/// last activated, several recorders on one GameObject, and a recorder added after the others.
/// Each case builds its own tree P { C1 { G }, C2 } from code, waits for every Start, then toggles
/// P; the rows are the callbacks in raw order with the recorder's label and its GameObject's
/// activeSelf and activeInHierarchy at that moment.
/// </summary>
public class ActivationControlsProbe : MonoBehaviour
{
    const string Variable = "ACTIVATIONCONTROLS_OUT";
    static StreamWriter output;
    static readonly List<string> s_Trace = new List<string>();
    static bool s_Recording;

    public string Label;

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value.Replace("\"", "'") + "\"}\n");

    void Record(string callback)
    {
        if (!s_Recording || Label == null) return;
        s_Trace.Add(Label + ":" + callback + (gameObject.activeSelf ? "+" : "-") + (gameObject.activeInHierarchy ? "+" : "-"));
    }

    void OnEnable() => Record("E");
    void OnDisable() => Record("D");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        var driver = new GameObject("driver").AddComponent<ActivationControlsProbe>();
        driver.StartCoroutine(driver.Run());
    }

    static Dictionary<string, GameObject> Tree()
    {
        var nodes = new Dictionary<string, GameObject>();
        foreach (var name in new[] { "P", "C1", "G", "C2" }) nodes[name] = new GameObject(name);
        nodes["C1"].transform.SetParent(nodes["P"].transform, false);
        nodes["G"].transform.SetParent(nodes["C1"].transform, false);
        nodes["C2"].transform.SetParent(nodes["P"].transform, false);
        return nodes;
    }

    static ActivationControlsProbe Add(Dictionary<string, GameObject> nodes, string node, string label)
    {
        var r = nodes[node].AddComponent<ActivationControlsProbe>();
        r.Label = label;
        return r;
    }

    IEnumerator Cycle(string key, Dictionary<string, GameObject> nodes, Action before)
    {
        yield return null;
        yield return null;
        before?.Invoke();
        s_Trace.Clear();
        s_Recording = true;
        nodes["P"].SetActive(false);
        Row(key + ".off", string.Join(" ", s_Trace.ToArray()));
        s_Trace.Clear();
        nodes["P"].SetActive(true);
        Row(key + ".on", string.Join(" ", s_Trace.ToArray()));
        s_Recording = false;
        UnityEngine.Object.Destroy(nodes["P"]);
        yield return null;
    }

    IEnumerator Run()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));

        // add: recorders added C2, C1, P, G (the activationorder fixture's order).
        var t = Tree();
        foreach (var n in new[] { "C2", "C1", "P", "G" }) Add(t, n, n);
        yield return Cycle("add", t, null);

        // addHierarchy: recorders added in pre-order P, C1, G, C2.
        t = Tree();
        foreach (var n in new[] { "P", "C1", "G", "C2" }) Add(t, n, n);
        yield return Cycle("addHierarchy", t, null);

        // reenabled: added C2, C1, P, G, then C1's recorder disabled and enabled again.
        t = Tree();
        var rs = new Dictionary<string, ActivationControlsProbe>();
        foreach (var n in new[] { "C2", "C1", "P", "G" }) rs[n] = Add(t, n, n);
        yield return Cycle("reenabled", t, () => { rs["C1"].enabled = false; rs["C1"].enabled = true; });

        // reactivated: added C2, C1, P, G, then C2's GameObject deactivated and activated again.
        t = Tree();
        foreach (var n in new[] { "C2", "C1", "P", "G" }) Add(t, n, n);
        var t4 = t;
        yield return Cycle("reactivated", t, () => { t4["C2"].SetActive(false); t4["C2"].SetActive(true); });

        // two: two recorders on P, one added first and one last: P.a, C2, C1, G, P.b.
        t = Tree();
        Add(t, "P", "P.a");
        foreach (var n in new[] { "C2", "C1", "G" }) Add(t, n, n);
        Add(t, "P", "P.b");
        yield return Cycle("two", t, null);

        // late: added C2, C1, P, G; after Start, a second recorder added to C1.
        t = Tree();
        foreach (var n in new[] { "C2", "C1", "P", "G" }) Add(t, n, n);
        var t6 = t;
        yield return Cycle("late", t, () => Add(t6, "C1", "C1.late"));

        // created: GameObjects created G, C2, P, C1 (not in the hierarchy's order), recorders
        // added in pre-order.
        var created = new Dictionary<string, GameObject>();
        foreach (var name in new[] { "G", "C2", "P", "C1" }) created[name] = new GameObject(name);
        created["C1"].transform.SetParent(created["P"].transform, false);
        created["G"].transform.SetParent(created["C1"].transform, false);
        created["C2"].transform.SetParent(created["P"].transform, false);
        foreach (var n in new[] { "P", "C1", "G", "C2" }) Add(created, n, n);
        yield return Cycle("created", created, null);

        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
