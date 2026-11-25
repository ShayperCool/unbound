using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $LODBOUNDS_OUT (tests/value_probe.py rows): for LODGroups of cube
/// renderers placed with fractional positions, rotations and scales, what RecalculateBounds gives
/// (the local reference point and size), and the inputs it works from (each renderer's bounds,
/// the group's local-to-world and world-to-local matrices), as numbers compared within 4e-6.
/// </summary>
public class LodBoundsProbe : MonoBehaviour
{
    const string Variable = "LODBOUNDS_OUT";
    static StreamWriter output;

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    /// <summary>A row of numbers (compared within the probe's tolerance).</summary>
    static void Numbers(string key, IEnumerable<float> values) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":[" + string.Join(",", values.Select(N)) + "]}\n");

    static string N(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    static IEnumerable<float> V(Vector3 v) { yield return v.x; yield return v.y; yield return v.z; }
    static IEnumerable<float> M(Matrix4x4 m) => Enumerable.Range(0, 16).Select(i => m[i]);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("LodBoundsProbe").AddComponent<LodBoundsProbe>();
    }

    /// <summary>A whole-number ratio: the same float in every scripting runtime.</summary>
    static float Q(int n, int d) => n / (float)d;

    static Renderer Cube(Transform parent, string name, Vector3 position, Vector3 euler, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        return go.GetComponent<Renderer>();
    }

    /// <summary>The group's reference point and size as they are, then after RecalculateBounds,
    /// and the inputs: the group's matrices, each renderer's bounds, local bounds and matrix.</summary>
    static void Record(string key, LODGroup group, IEnumerable<Renderer> inputs)
    {
        Numbers(key + "/before", V(group.localReferencePoint).Append(group.size));
        group.RecalculateBounds();
        Numbers(key + "/result", V(group.localReferencePoint).Append(group.size));
        Numbers(key + "/group", M(group.transform.localToWorldMatrix).Concat(M(group.transform.worldToLocalMatrix)));
        int i = 0;
        foreach (var r in inputs)
        {
            if (r == null) { Row(key + "/renderer" + i++, "null"); continue; }
            var b = r.bounds;
            Numbers(key + "/renderer" + i++, V(b.center).Concat(V(b.extents)).Concat(V(r.localBounds.center)).Concat(V(r.localBounds.extents))
                .Concat(M(r.transform.localToWorldMatrix)));
        }
    }

    /// <summary>A group under its own transform with three renderers, one per level.</summary>
    static (LODGroup, Renderer[]) Group(string name, Vector3 position, Vector3 euler, Vector3 scale, (Vector3 p, Vector3 e, Vector3 s)[] children)
    {
        var root = new GameObject(name);
        root.transform.position = position;
        root.transform.eulerAngles = euler;
        root.transform.localScale = scale;
        var renderers = children.Select((c, i) => Cube(root.transform, name + "_" + i, c.p, c.e, c.s)).ToArray();
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(renderers.Select((r, i) => new LOD(Q(6, 10) / (i + 1), new[] { r })).ToArray());
        return (group, renderers);
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message.Replace("\"", "'")); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static void Run()
    {
        // Three renderers of fractional placement and size.
        var children = new[]
        {
            (new Vector3(Q(61, 100), Q(1283, 100), Q(-13, 100)), Vector3.zero, new Vector3(Q(812, 100), Q(2625, 100), Q(774, 100))),
            (new Vector3(Q(64, 100), Q(1282, 100), Q(-12, 100)), Vector3.zero, new Vector3(Q(819, 100), Q(2624, 100), Q(784, 100))),
            (new Vector3(Q(85, 100), Q(1282, 100), Q(-12, 100)), Vector3.zero, new Vector3(Q(792, 100), Q(2624, 100), Q(794, 100))),
        };
        var rotated = new[]
        {
            (new Vector3(Q(61, 100), Q(133, 100), Q(-13, 100)), new Vector3(0, 30, 0), new Vector3(Q(13, 10), Q(7, 10), Q(9, 10))),
            (new Vector3(Q(-47, 100), Q(82, 100), Q(31, 100)), new Vector3(15, 0, 40), new Vector3(Q(9, 10), Q(11, 10), Q(5, 10))),
            (new Vector3(Q(23, 100), Q(-58, 100), Q(77, 100)), new Vector3(70, 20, 10), new Vector3(Q(3, 10), Q(17, 10), Q(6, 10))),
        };
        var cases = new (string name, Vector3 p, Vector3 e, Vector3 s, (Vector3, Vector3, Vector3)[] c)[]
        {
            ("identity", Vector3.zero, Vector3.zero, Vector3.one, children),
            ("translated", new Vector3(Q(13, 10), Q(-27, 10), Q(9, 20)), Vector3.zero, Vector3.one, children),
            ("rotated", Vector3.zero, new Vector3(10, 30, 0), Vector3.one, children),
            ("scaled", Vector3.zero, Vector3.zero, new Vector3(2, Q(1, 2), Q(3, 2)), children),
            ("placed", new Vector3(Q(-31, 10), Q(17, 10), Q(5, 1)), new Vector3(25, -40, 15), new Vector3(Q(13, 10), Q(13, 10), Q(13, 10)), rotated),
            ("skewed", new Vector3(Q(7, 10), 0, Q(-11, 10)), new Vector3(0, 45, 20), new Vector3(Q(3, 1), 1, Q(1, 2)), rotated),
            ("children", Vector3.zero, Vector3.zero, Vector3.one, rotated),
        };
        foreach (var c in cases)
        {
            var (group, renderers) = Group(c.name, c.p, c.e, c.s, c.c);
            Record("case/" + c.name, group, renderers);
            // The same renderers in the other order (the last level first).
            var reversed = renderers.Reverse().ToArray();
            group.SetLODs(reversed.Select((r, i) => new LOD(Q(6, 10) / (i + 1), new[] { r })).ToArray());
            Record("case/" + c.name + "/reversed", group, reversed);
            // All in one level.
            group.SetLODs(new[] { new LOD(Q(5, 10), renderers) });
            Record("case/" + c.name + "/onelevel", group, renderers);
        }

        // What counts: a disabled renderer, one of an inactive object, a null entry, a renderer
        // twice, a level without renderers, a group without any.
        var (g, rs) = Group("counted", new Vector3(Q(1, 3), 0, 0), Vector3.zero, Vector3.one, rotated);
        rs[1].enabled = false;
        Record("counted/disabled", g, rs);
        rs[1].enabled = true;
        rs[2].gameObject.SetActive(false);
        Record("counted/inactive", g, rs);
        rs[2].gameObject.SetActive(true);
        g.SetLODs(new[] { new LOD(Q(6, 10), new[] { rs[0], null }), new LOD(Q(3, 10), new[] { rs[1], rs[0] }) });
        Record("counted/null_and_twice", g, new[] { rs[0], null, rs[1], rs[0] });
        g.SetLODs(new[] { new LOD(Q(6, 10), new[] { rs[2] }), new LOD(Q(3, 10), new Renderer[0]) });
        Record("counted/empty_level", g, new[] { rs[2] });
        // No renderers at all: the values set after the levels, then recalculated.
        g.SetLODs(new[] { new LOD(Q(6, 10), new Renderer[0]) });
        g.size = 7;
        g.localReferencePoint = new Vector3(1, 2, 3);
        Record("counted/none", g, new Renderer[0]);
        // Whether SetLODs recalculates: values set, then levels with renderers given.
        g.size = 7;
        g.localReferencePoint = new Vector3(1, 2, 3);
        g.SetLODs(new[] { new LOD(Q(6, 10), new[] { rs[0], rs[1] }) });
        Numbers("counted/setlods", V(g.localReferencePoint).Append(g.size));
    }
}
