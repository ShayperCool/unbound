using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $PROPERTYIDS_OUT (tests/value_probe.py rows): which of the names
/// Assets/PropertyIds/Candidates.txt lists (property names of Unity's built-in shaders and engine
/// globals) have shader property ids before Play mode's first script runs, in the order of their
/// ids, and where a project shader's own names come. Values never contain ids, only their order.
/// </summary>
public class PropertyIdsProbe : MonoBehaviour
{
    const string Variable = "PROPERTYIDS_OUT";

    static readonly List<(string Key, string Value)> rows = new List<(string, string)>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Measure()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        // A name no shader has: its id comes after every name registered so far.
        int marker = Shader.PropertyToID("__propertyids_marker");
        var candidates = File.ReadAllLines(Path.Combine(Application.dataPath, "PropertyIds", "Candidates.txt")).Where(l => l.Length > 0).ToArray();
        var ids = candidates.Select(name => (name, id: Shader.PropertyToID(name))).ToArray();
        var before = ids.Where(c => c.id < marker).OrderBy(c => c.id).ThenBy(c => c.name, StringComparer.Ordinal).ToArray();
        rows.Add(("builtin/count", before.Length.ToString()));
        rows.Add(("builtin/order", string.Join("\n", before.Select(c => c.name))));
        rows.Add(("builtin/negative", string.Join("\n", before.Where(c => c.id < 0).Select(c => c.name))));
        // Names that share an id, each group in name order.
        rows.Add(("builtin/shared", string.Join("\n", before.GroupBy(c => c.id).Where(g => g.Count() > 1)
            .Select(g => string.Join(" ", g.Select(c => c.name).OrderBy(n => n, StringComparer.Ordinal))))));
        // A project shader's own names, beside a built-in name it declares too.
        var shader = Shader.Find("Probe/Ids");
        rows.Add(("project/shader", shader == null ? "null" : shader.name));
        int own = Shader.PropertyToID("_ProbeOwnFirst"), other = Shader.PropertyToID("_ProbeOwnSecond");
        int last = before.Length > 0 ? before[before.Length - 1].id : int.MinValue;
        rows.Add(("project/after_builtin", (own > last && other > last).ToString()));
        rows.Add(("project/before_marker", (own < marker) + " " + (other < marker)));
        rows.Add(("project/declared_order", (own < other).ToString()));
        rows.Add(("project/main_tex_first", (Shader.PropertyToID("_MainTex") < own).ToString()));
        // The built-in names before the project shader's first own name (the engine's, registered
        // before any project content) and those after it, each in id order.
        int first = Math.Min(own, other);
        rows.Add(("builtin/before_project", string.Join("\n", before.Where(c => c.id < first).Select(c => c.name))));
        rows.Add(("builtin/after_project", string.Join("\n", before.Where(c => c.id > first).Select(c => c.name))));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("PropertyIdsProbe").AddComponent<PropertyIdsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        using (var output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false)))
        {
            foreach (var (key, value) in rows) output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");
            output.Write("{\"k\":\"complete\",\"v\":true}\n");
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
