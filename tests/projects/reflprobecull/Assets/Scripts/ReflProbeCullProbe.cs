using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Which reflection probes a scriptable pipeline's culling makes visible (CullingResults
/// .visibleReflectionProbes), measured with <see cref="ReflProbeCullPipeline"/>: a camera at the
/// origin looking along +z (field of view 60, a square target, near 0.3, far 50) culls one set of
/// probes at a time.
/// - grid.*: 399 probes of one box (size, center offset, rotation, scale per set) on a grid in the
///   horizontal plane, each its own probe; the value is one bit per probe, row by row (z, then x),
///   1 = visible.
/// - sort.*: three visible probes of different importance and size under each sorting criterion
///   and the parameters' own (sort.default), in culling order; sort.fields: what the first lists.
/// - anchor.*: a probe behind the camera (culled alone) and a cube renderer in view whose
///   probeAnchor is the probe's transform or another transform, the renderer out of view, culled by
///   its layer, or with reflection probes off, and the probe disabled.
/// - notexture: a probe in view without a texture.
/// Writes values.jsonl into REFL_PROBE_CULL_OUT and exits.
/// </summary>
public sealed class ReflProbeCullProbe : MonoBehaviour
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder m_Out = new StringBuilder();
    Camera m_Camera;
    Cubemap m_Cube;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REFL_PROBE_CULL_OUT"))) return;
        new GameObject("ReflProbeCullProbe").AddComponent<ReflProbeCullProbe>();
    }

    void Put(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t")).Append("\"}\n");

    static string F(float f) => f.ToString("R", Inv);
    static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("REFL_PROBE_CULL_OUT");
        foreach (var existing in FindObjectsByType<Camera>(FindObjectsSortMode.None)) existing.enabled = false;
        foreach (var existing in FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) existing.enabled = false;
        m_Cube = new Cubemap(8, TextureFormat.RGBA32, false) { name = "ProbeCube" };
        var cameraObject = new GameObject("ProbeCamera");
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.fieldOfView = 60;
        m_Camera.nearClipPlane = 0.3f;
        m_Camera.farClipPlane = 50;
        m_Camera.targetTexture = new RenderTexture(32, 32, 24);
        ReflProbeCullPipeline.Camera = m_Camera;
        GraphicsSettings.defaultRenderPipeline = ScriptableObject.CreateInstance<ReflProbeCullPipeline>();
        yield return null;
        bool failed = false;
        IEnumerator[] steps = { Grids(), Sorting(), Anchors(), NoTexture() };
        foreach (var step in steps)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!step.MoveNext()) break;
                    current = step.Current;
                }
                catch (Exception e)
                {
                    Put("error", e.GetType().Name + ": " + e.Message);
                    failed = true;
                    break;
                }
                yield return current;
            }
            if (failed) break;
        }
        GraphicsSettings.defaultRenderPipeline = null;
        if (!failed) m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    ReflectionProbe Probe(string name, Vector3 position, Vector3 size, Vector3 center, Quaternion rotation, Vector3 scale, bool texture = true)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        if (texture) probe.customBakedTexture = m_Cube;
        probe.size = size;
        probe.center = center;
        return probe;
    }

    /// <summary>Renders the camera once (a frame after the scene changed) and gives the culling's list.</summary>
    IEnumerator Cull(List<string> into)
    {
        yield return null;
        int before = ReflProbeCullPipeline.Culls;
        m_Camera.Render();
        into.Clear();
        if (ReflProbeCullPipeline.Culls == before) into.Add("not culled");
        else into.AddRange(ReflProbeCullPipeline.Visible);
    }

    IEnumerator Grids()
    {
        var sets = new (string name, Vector3 size, Vector3 center, Vector3 euler, Vector3 scale)[]
        {
            ("yaw0", new Vector3(16, 3, 6), new Vector3(2, 0, 3), Vector3.zero, Vector3.one),
            ("yaw30", new Vector3(16, 3, 6), new Vector3(2, 0, 3), new Vector3(0, 30, 0), Vector3.one),
            ("yaw63", new Vector3(16, 3, 6), new Vector3(2, 0, 3), new Vector3(0, 63, 0), Vector3.one),
            ("yaw90", new Vector3(16, 3, 6), new Vector3(2, 0, 3), new Vector3(0, 90, 0), Vector3.one),
            ("tilt", new Vector3(16, 3, 6), new Vector3(2, 1, 3), new Vector3(25, 63, -15), Vector3.one),
            ("scaled", new Vector3(16, 3, 6), new Vector3(2, 0, 3), new Vector3(0, 30, 0), new Vector3(2, 1, 0.5f)),
            ("long45", new Vector3(40, 2, 2), Vector3.zero, new Vector3(0, -45, 0), Vector3.one),
        };
        var visible = new List<string>();
        bool planesWritten = false;
        foreach (var set in sets)
        {
            var probes = new List<ReflectionProbe>();
            int index = 0;
            for (int z = -12; z <= 60; z += 4)
                for (int x = -40; x <= 40; x += 4)
                    probes.Add(Probe("g" + index++, new Vector3(x, 0, z), set.size, set.center, Quaternion.Euler(set.euler), set.scale));
            yield return Cull(visible);
            if (!planesWritten)
            {
                planesWritten = true;
                Put("planes", string.Join(" ", ReflProbeCullPipeline.Planes.Select(p => V(p.normal) + "/" + F(p.distance))));
                Put("camera.projection", m_Camera.projectionMatrix.ToString("R"));
            }
            var set1 = new HashSet<string>(visible);
            var bits = new StringBuilder();
            for (int i = 0; i < probes.Count; i++) bits.Append(set1.Contains("g" + i) ? '1' : '0');
            Put("grid." + set.name, bits.ToString());
            Put("grid." + set.name + ".others", string.Join(",", visible.Where(n => !n.StartsWith("g", StringComparison.Ordinal))));
            Put("grid." + set.name + ".bounds.g0", probes[0].bounds.ToString("R"));
            foreach (var p in probes) Destroy(p.gameObject);
        }
    }

    IEnumerator Sorting()
    {
        var a = Probe("a.imp1.small", new Vector3(-3, 0, 12), new Vector3(2, 2, 2), Vector3.zero, Quaternion.identity, Vector3.one);
        var b = Probe("b.imp2.large", new Vector3(0, 0, 12), new Vector3(8, 8, 8), Vector3.zero, Quaternion.identity, Vector3.one);
        var c = Probe("c.imp1.large", new Vector3(3, 0, 12), new Vector3(6, 6, 6), Vector3.zero, Quaternion.identity, Vector3.one);
        // Ties: d has c's importance and size (made after it), e a's importance and size.
        var d = Probe("d.imp1.large", new Vector3(3, 2, 12), new Vector3(6, 6, 6), Vector3.zero, Quaternion.identity, Vector3.one);
        var e = Probe("e.imp1.small", new Vector3(-3, 2, 12), new Vector3(2, 2, 2), Vector3.zero, Quaternion.identity, Vector3.one);
        b.importance = 2;
        b.blendDistance = 3;
        b.boxProjection = true;
        var visible = new List<string>();
        ReflProbeCullPipeline.Criteria = null;
        yield return Cull(visible);
        Put("sort.default", string.Join(",", visible));
        var first = ReflProbeCullPipeline.First;
        Put("sort.fields", first);
        Put("sort.defaultCriteria", ReflProbeCullPipeline.DefaultCriteria);
        foreach (ReflectionProbeSortingCriteria criteria in Enum.GetValues(typeof(ReflectionProbeSortingCriteria)))
        {
            ReflProbeCullPipeline.Criteria = criteria;
            yield return Cull(visible);
            Put("sort." + criteria, string.Join(",", visible));
        }
        ReflProbeCullPipeline.Criteria = null;
        // Enabled again later: does the culling order follow creation or enabling?
        a.enabled = false;
        yield return null;
        a.enabled = true;
        ReflProbeCullPipeline.Criteria = ReflectionProbeSortingCriteria.None;
        yield return Cull(visible);
        Put("sort.None.reenabled", string.Join(",", visible));
        ReflProbeCullPipeline.Criteria = null;
        // A probe on a layer the camera's culling mask leaves out.
        b.gameObject.layer = 31;
        m_Camera.cullingMask = ~(1 << 31);
        yield return Cull(visible);
        Put("layer.maskedOut", string.Join(",", visible));
        m_Camera.cullingMask = -1;
        b.gameObject.layer = 0;
        foreach (var p in new[] { a, b, c, d, e }) Destroy(p.gameObject);
        yield return null;
        // What "size" compares: volume, extents' length and the longest side order these four
        // boxes differently (p4, p3, p2, p1 / p4, p1, p3, p2 / p1, p4, p3, p2 largest first).
        var sized = new[]
        {
            Probe("p1", new Vector3(-6, 0, 20), new Vector3(10, 1, 1), Vector3.zero, Quaternion.identity, Vector3.one),
            Probe("p2", new Vector3(-2, 0, 20), new Vector3(3, 3, 3), Vector3.zero, Quaternion.identity, Vector3.one),
            Probe("p3", new Vector3(2, 0, 20), new Vector3(6, 6, 1), Vector3.zero, Quaternion.identity, Vector3.one),
            Probe("p4", new Vector3(6, 0, 20), new Vector3(7, 7, 7), Vector3.zero, Quaternion.identity, Vector3.one),
        };
        ReflProbeCullPipeline.Criteria = ReflectionProbeSortingCriteria.Size;
        yield return Cull(visible);
        Put("sort.Size.metric", string.Join(",", visible));
        ReflProbeCullPipeline.Criteria = null;
        foreach (var p in sized) Destroy(p.gameObject);
    }

    IEnumerator Anchors()
    {
        var far = Probe("far", new Vector3(0, 0, -60), new Vector3(10, 10, 10), Vector3.zero, Quaternion.identity, Vector3.one);
        var empty = new GameObject("empty anchor");
        empty.transform.position = new Vector3(0, 0, -60);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "cube";
        Destroy(cube.GetComponent<Collider>());
        var renderer = cube.GetComponent<MeshRenderer>();
        var visible = new List<string>();

        cube.transform.position = new Vector3(0, 0, 10);
        yield return Cull(visible);
        Put("anchor.none", string.Join(",", visible));

        renderer.probeAnchor = far.transform;
        yield return Cull(visible);
        Put("anchor.probe", string.Join(",", visible));

        renderer.probeAnchor = empty.transform;
        yield return Cull(visible);
        Put("anchor.emptyInBox", string.Join(",", visible));

        renderer.probeAnchor = far.transform;
        cube.transform.position = new Vector3(0, 0, -20);
        yield return Cull(visible);
        Put("anchor.rendererOutOfView", string.Join(",", visible));

        cube.transform.position = new Vector3(0, 0, 10);
        cube.layer = 31;
        m_Camera.cullingMask = ~(1 << 31);
        yield return Cull(visible);
        Put("anchor.rendererLayerCulled", string.Join(",", visible));
        cube.layer = 0;
        m_Camera.cullingMask = -1;

        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        yield return Cull(visible);
        Put("anchor.usageOff", string.Join(",", visible));
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

        renderer.enabled = false;
        yield return Cull(visible);
        Put("anchor.rendererDisabled", string.Join(",", visible));
        renderer.enabled = true;

        far.enabled = false;
        yield return Cull(visible);
        Put("anchor.probeDisabled", string.Join(",", visible));
        far.enabled = true;

        cube.SetActive(false);
        yield return Cull(visible);
        Put("anchor.rendererInactive", string.Join(",", visible));
        cube.SetActive(true);

        // Two renderers anchored to the probe; and where an anchored probe goes in each sorting.
        var second = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        second.name = "second";
        Destroy(second.GetComponent<Collider>());
        second.transform.position = new Vector3(2, 0, 10);
        second.GetComponent<MeshRenderer>().probeAnchor = far.transform;
        var small = Probe("v.imp2.small", new Vector3(-2, 0, 15), new Vector3(2, 2, 2), Vector3.zero, Quaternion.identity, Vector3.one);
        var large = Probe("w.imp1.large", new Vector3(2, 0, 15), new Vector3(20, 20, 20), Vector3.zero, Quaternion.identity, Vector3.one);
        small.importance = 2;
        far.importance = 1;
        yield return Cull(visible);
        Put("anchor.twoRenderers", string.Join(",", visible));
        foreach (ReflectionProbeSortingCriteria criteria in Enum.GetValues(typeof(ReflectionProbeSortingCriteria)))
        {
            ReflProbeCullPipeline.Criteria = criteria;
            yield return Cull(visible);
            Put("anchor.order." + criteria, string.Join(",", visible));
        }
        ReflProbeCullPipeline.Criteria = null;
        Destroy(small.gameObject);
        Destroy(large.gameObject);
        Destroy(second);

        // The anchor in the box of a probe that is itself in view: listed once?
        var near = Probe("near", new Vector3(0, 0, 20), new Vector3(4, 4, 4), Vector3.zero, Quaternion.identity, Vector3.one);
        renderer.probeAnchor = near.transform;
        yield return Cull(visible);
        Put("anchor.probeInView", string.Join(",", visible));

        Destroy(near.gameObject);
        Destroy(cube);
        Destroy(empty);
        Destroy(far.gameObject);
    }

    IEnumerator NoTexture()
    {
        var probe = Probe("notexture", new Vector3(0, 0, 12), new Vector3(4, 4, 4), Vector3.zero, Quaternion.identity, Vector3.one, texture: false);
        var visible = new List<string>();
        yield return Cull(visible);
        Put("notexture", string.Join(",", visible));
        Destroy(probe.gameObject);
    }
}
