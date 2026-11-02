using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Records the light probe APIs on the baked Main scene, then on Main with Extra loaded
/// additively, as JSON lines (key, value) in <c>LIGHT_PROBES_PROBE_OUT</c>, then quits:
/// the probes' positions, coefficients and cells; LightProbes.GetInterpolatedProbe and
/// CalculateInterpolatedLightAndOcclusionProbes (both overloads, and their argument errors);
/// LightProbesQuery (tetrahedron index hints, the batch overload, Dispose); what
/// MaterialPropertyBlock.CopySHCoefficientArraysFrom / CopyProbeOcclusionArrayFrom store;
/// spheres drawn with Graphics.DrawMeshInstanced and MeshRenderers whose probes come from those
/// blocks (LightProbeUsage.CustomProvided), and instanced with BlendProbes, as pixels; the
/// tetrahedralization events around Tetrahedralize and additive load/unload.
/// </summary>
public class LightProbesProbe : MonoBehaviour
{
    const int Width = 320, Height = 180, ProbeLayer = 8;
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Events = new List<string>();
    static string s_Step = "load";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Subscribe()
    {
        LightProbes.lightProbesUpdated += () => s_Events.Add($"lightProbesUpdated@{s_Step}");
        LightProbes.tetrahedralizationCompleted += () => s_Events.Add($"tetrahedralizationCompleted@{s_Step}");
        LightProbes.needsRetetrahedralization += () => s_Events.Add($"needsRetetrahedralization@{s_Step}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("LightProbesProbe").AddComponent<LightProbesProbe>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"{F(v.x)} {F(v.y)} {F(v.z)}";
    static string V(Vector4 v) => $"{F(v.x)} {F(v.y)} {F(v.z)} {F(v.w)}";
    static string Sh(SphericalHarmonicsL2 sh) => string.Join(" ", Enumerable.Range(0, 27).Select(i => F(sh[i / 9, i % 9])));
    static void Put(string key, string value) =>
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ")).Append("\"}\n");
    static void Try(string key, Action action)
    {
        try { action(); Put(key, "ok"); }
        catch (Exception e) { Put(key, e.GetType().Name + ": " + e.Message); }
    }
    static void Events(string key)
    {
        Put("events " + key, s_Events.Count == 0 ? "none" : string.Join(" ", s_Events));
        s_Events.Clear();
    }

    static readonly Vector3[] Points =
    {
        new Vector3(0.3f, 1.1f, -0.4f), new Vector3(-1.2f, 0.8f, 1.3f), new Vector3(1.5f, 2.0f, -1.5f),
        Vector3.zero, // replaced by probe 5's position
        new Vector3(0f, 3.5f, 0f), new Vector3(0f, -1f, 0f), new Vector3(3.2f, 1f, 0f), new Vector3(10f, 1f, 10f),
        new Vector3(5.1f, 1.2f, 0.1f), new Vector3(-2.5f, 0.5f, -2.5f),
    };

    static void Probes(string key)
    {
        var probes = LightmapSettings.lightProbes;
        if (probes == null) { Put(key + " probes", "null"); return; }
        Put(key + " counts", $"{probes.count} {probes.countSelf} {probes.cellCount} {probes.cellCountSelf}");
        Put(key + " positions", string.Join(" ", probes.positions.Select(V)));
        var baked = probes.bakedProbes;
        for (int i = 0; i < baked.Length; i++) Put($"{key} baked {i}", Sh(baked[i]));
        foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
        {
            var shared = LightProbes.GetSharedLightProbesForScene(scene);
            Put($"{key} shared {scene.name}", shared == null ? "null"
                : $"{shared.count} {shared.countSelf} {shared.cellCount} {shared.cellCountSelf} same {ReferenceEquals(shared, probes)} {string.Join(" ", shared.GetPositionsSelf().Select(V))}");
        }
    }

    static void Interpolate(string key, Vector3[] points)
    {
        for (int i = 0; i < points.Length; i++)
        {
            LightProbes.GetInterpolatedProbe(points[i], null, out var sh);
            Put($"{key} interpolated {i}", Sh(sh));
        }
        var shs = new SphericalHarmonicsL2[points.Length];
        var occlusion = new Vector4[points.Length];
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(points, shs, occlusion);
        for (int i = 0; i < points.Length; i++)
        {
            Put($"{key} calculated {i}", Sh(shs[i]));
            Put($"{key} occlusion {i}", V(occlusion[i]));
        }
        var probes = LightmapSettings.lightProbes;
        if (probes != null)
        {
            var at = probes.positions;
            var occAt = new Vector4[at.Length];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(at, null, occAt);
            Put($"{key} occlusion at probes", string.Join(" ", occAt.Select(V)));
        }
        var hinted = new GameObject("Hinted", typeof(MeshFilter), typeof(MeshRenderer)).GetComponent<MeshRenderer>();
        for (int i = 0; i < points.Length; i++)
        {
            LightProbes.GetInterpolatedProbe(points[i], hinted, out var sh);
            Put($"{key} renderer interpolated {i}", Sh(sh));
        }
        DestroyImmediate(hinted.gameObject);
        using (var query = new LightProbesQuery(Allocator.Persistent))
        {
            for (int i = 0; i < points.Length; i++)
            {
                int index = 0;
                query.CalculateInterpolatedLightAndOcclusionProbe(points[i], ref index, out var sh, out var occ);
                Put($"{key} query {i}", $"{index} {Sh(sh)} {V(occ)}");
            }
        }
    }

    void Lights()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None).OrderBy(l => l.name))
        {
            var o = l.bakingOutput;
            Put($"bakingOutput {l.name}", $"{o.probeOcclusionLightIndex} {o.occlusionMaskChannel} {o.lightmapBakeType} {o.mixedLightingMode} {o.isBaked}");
        }
    }

    static void Static(Vector3[] points)
    {
        // The List overload: what the result lists hold afterwards.
        var shList = new List<SphericalHarmonicsL2> { default, default };
        var occList = new List<Vector4> { Vector4.one * 7f };
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(points.Take(3).ToList(), shList, occList);
        Put("list counts", $"{shList.Count} {occList.Count}");
        for (int i = 0; i < shList.Count; i++) Put($"list sh {i}", Sh(shList[i]));
        for (int i = 0; i < occList.Count; i++) Put($"list occlusion {i}", V(occList[i]));
        var bigList = Enumerable.Repeat(default(SphericalHarmonicsL2), 5).ToList();
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(points.Take(2).ToList(), bigList, null);
        Put("list larger count", bigList.Count.ToString());
        var onlyOcc = new List<Vector4>();
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(points.Take(2).ToList(), null, onlyOcc);
        Put("list occlusion only", string.Join(" ", onlyOcc.Select(V)));

        var three = points.Take(3).ToArray();
        Try("array null positions", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes((Vector3[])null, new SphericalHarmonicsL2[3], new Vector4[3]));
        Try("array short sh", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes(three, new SphericalHarmonicsL2[2], new Vector4[3]));
        Try("array short occlusion", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes(three, new SphericalHarmonicsL2[3], new Vector4[2]));
        Try("array null both", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes(three, null, null));
        var longer = new SphericalHarmonicsL2[5];
        var longerOcc = Enumerable.Repeat(Vector4.one * 9f, 5).ToArray();
        Try("array longer", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes(three, longer, longerOcc));
        Put("array longer tail", $"{Sh(longer[3])} {V(longerOcc[4])}");
        Try("list null positions", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes((List<Vector3>)null, new List<SphericalHarmonicsL2>(), new List<Vector4>()));
        Try("list null both", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes(three.ToList(), null, null));
        var empty = new SphericalHarmonicsL2[0];
        Try("array empty positions", () => LightProbes.CalculateInterpolatedLightAndOcclusionProbes(new Vector3[0], empty, new Vector4[0]));
    }

    static void Query(Vector3[] points)
    {
        var probes = LightmapSettings.lightProbes;
        int cells = probes != null ? probes.cellCount : 0;
        var query = new LightProbesQuery(Allocator.Persistent);
        Put("query created", query.IsCreated.ToString());
        int chain = 0;
        for (int i = 0; i < points.Length; i++)
        {
            query.CalculateInterpolatedLightAndOcclusionProbe(points[i], ref chain, out var sh, out var occ);
            Put($"query chain {i}", $"{chain} {Sh(sh)} {V(occ)}");
        }
        foreach (int start in new[] { -1, cells - 1, cells, cells + 5, int.MaxValue })
        {
            int index = start;
            SphericalHarmonicsL2 sh = default;
            Vector4 occ = default;
            var q = query;
            Try($"query start {(start == cells - 1 ? "last" : start == cells ? "count" : start == cells + 5 ? "count+5" : start.ToString())}",
                () => q.CalculateInterpolatedLightAndOcclusionProbe(points[0], ref index, out sh, out occ));
            Put($"query start result {start - cells}", $"{index} {Sh(sh)} {V(occ)}");
        }
        // The batch overload: tetrahedron indices in and out.
        var positions = new NativeArray<Vector3>(points, Allocator.Persistent);
        var indices = new NativeArray<int>(points.Length, Allocator.Persistent);
        for (int i = 0; i < indices.Length; i++) indices[i] = i % 2 == 0 ? 0 : Mathf.Max(cells - 1, 0);
        var shs = new NativeArray<SphericalHarmonicsL2>(points.Length, Allocator.Persistent);
        var occs = new NativeArray<Vector4>(points.Length, Allocator.Persistent);
        query.CalculateInterpolatedLightAndOcclusionProbes(positions, indices, shs, occs);
        for (int i = 0; i < points.Length; i++) Put($"query batch {i}", $"{indices[i]} {Sh(shs[i])} {V(occs[i])}");
        var shortShs = new NativeArray<SphericalHarmonicsL2>(points.Length - 1, Allocator.Persistent);
        var shortIdx = new NativeArray<int>(points.Length - 1, Allocator.Persistent);
        var q2 = query;
        Try("query batch short sh", () => q2.CalculateInterpolatedLightAndOcclusionProbes(positions, indices, shortShs, occs));
        Try("query batch short indices", () => q2.CalculateInterpolatedLightAndOcclusionProbes(positions, shortIdx, shs, occs));
        var longer = new NativeArray<SphericalHarmonicsL2>(points.Length + 2, Allocator.Persistent);
        var longerIdx = new NativeArray<int>(points.Length + 2, Allocator.Persistent);
        var longerOcc = new NativeArray<Vector4>(points.Length + 2, Allocator.Persistent);
        Try("query batch longer outputs", () => q2.CalculateInterpolatedLightAndOcclusionProbes(positions, longerIdx, longer, longerOcc));
        Put("query batch longer tail", $"{longerIdx[points.Length]} {V(longerOcc[points.Length + 1])}");
        positions.Dispose(); indices.Dispose(); shs.Dispose(); occs.Dispose(); shortShs.Dispose(); shortIdx.Dispose();
        longer.Dispose(); longerIdx.Dispose(); longerOcc.Dispose();

        // Created before the probes change: what it interpolates afterwards.
        if (probes != null)
        {
            var original = probes.bakedProbes;
            var doubled = original.Select(sh => sh * 2f).ToArray();
            s_Step = "set bakedProbes";
            probes.bakedProbes = doubled;
            int index = 0;
            query.CalculateInterpolatedLightAndOcclusionProbe(points[0], ref index, out var before, out _);
            Put("query after bakedProbes set", Sh(before));
            using (var fresh = new LightProbesQuery(Allocator.Temp))
            {
                index = 0;
                fresh.CalculateInterpolatedLightAndOcclusionProbe(points[0], ref index, out var after, out _);
                Put("fresh query after bakedProbes set", Sh(after));
            }
            LightProbes.GetInterpolatedProbe(points[0], null, out var direct);
            Put("interpolated after bakedProbes set", Sh(direct));
            probes.bakedProbes = original;
            Events("bakedProbes set");
        }

        query.Dispose();
        Put("query disposed", query.IsCreated.ToString());
        var disposed = query;
        Try("query dispose twice", () => disposed.Dispose());
        Try("query use disposed", () => { int i = 0; disposed.CalculateInterpolatedLightAndOcclusionProbe(points[0], ref i, out _, out _); });
        Put("query default created", default(LightProbesQuery).IsCreated.ToString());
        Try("query default dispose", () => default(LightProbesQuery).Dispose());
        var jobQuery = new LightProbesQuery(Allocator.TempJob);
        var handle = jobQuery.Dispose(default);
        handle.Complete();
        Put("query job dispose", jobQuery.IsCreated.ToString());
    }

    static SphericalHarmonicsL2[] Synthetic()
    {
        var colors = new[] { new Color(1f, 0.2f, 0.1f), new Color(0.2f, 1f, 0.3f), new Color(0.2f, 0.4f, 1f), new Color(1f, 0.9f, 0.2f), new Color(0.8f, 0.3f, 1f) };
        var dirs = new[] { new Vector3(0.3f, 0.8f, -0.5f), new Vector3(-0.7f, 0.2f, -0.6f), new Vector3(0f, -0.4f, -0.9f), new Vector3(0.9f, 0.1f, 0.3f), new Vector3(-0.2f, 0.95f, 0.1f) };
        var result = new SphericalHarmonicsL2[5];
        for (int i = 0; i < 5; i++)
        {
            result[i].AddAmbientLight(colors[(i + 2) % 5] * 0.15f);
            result[i].AddDirectionalLight(dirs[i].normalized, colors[i], 0.6f);
        }
        return result;
    }

    static readonly string[] ShNames = { "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };

    static void Block(string key, MaterialPropertyBlock block)
    {
        foreach (var name in ShNames.Append("unity_ProbesOcclusion"))
        {
            var values = block.GetVectorArray(name);
            Put($"{key} {name}", values == null ? "null" : $"{values.Length}: {string.Join(" ", values.Select(V))}");
        }
        Put($"{key} empty", block.isEmpty.ToString());
    }

    static void PropertyBlocks(SphericalHarmonicsL2[] sh)
    {
        var a = new MaterialPropertyBlock();
        a.CopySHCoefficientArraysFrom(sh);
        Block("mpb array", a);
        var b = new MaterialPropertyBlock();
        b.CopySHCoefficientArraysFrom(sh.ToList());
        Block("mpb list", b);
        // A block without the arrays gets arrays of count elements; a later copy writes into
        // them (copies past their end are not recorded: the Editor writes out of bounds).
        var c = new MaterialPropertyBlock();
        c.CopySHCoefficientArraysFrom(sh, 1, 0, 2);
        Block("mpb range", c);
        c.CopySHCoefficientArraysFrom(sh.ToList(), 4, 1, 1);
        Block("mpb range again", c);
        var g = new MaterialPropertyBlock();
        g.CopySHCoefficientArraysFrom(sh);
        g.CopySHCoefficientArraysFrom(sh, 0, 2, 2);
        Block("mpb range into full", g);
        var d = new MaterialPropertyBlock();
        d.SetVectorArray("unity_SHAr", new Vector4[3] { Vector4.one, Vector4.one, Vector4.one });
        d.CopySHCoefficientArraysFrom(sh);
        Block("mpb preset", d);
        var e = new MaterialPropertyBlock();
        Try("mpb null array", () => e.CopySHCoefficientArraysFrom((SphericalHarmonicsL2[])null));
        Try("mpb null list", () => e.CopySHCoefficientArraysFrom((List<SphericalHarmonicsL2>)null));
        Try("mpb negative source", () => e.CopySHCoefficientArraysFrom(sh, -1, 0, 1));
        Try("mpb negative dest", () => e.CopySHCoefficientArraysFrom(sh, 0, -1, 1));
        Try("mpb negative count", () => e.CopySHCoefficientArraysFrom(sh, 0, 0, -1));
        Try("mpb count beyond", () => e.CopySHCoefficientArraysFrom(sh, 4, 0, 2));
        Try("mpb zero count", () => e.CopySHCoefficientArraysFrom(sh, 0, 0, 0));
        Try("mpb empty array", () => e.CopySHCoefficientArraysFrom(new SphericalHarmonicsL2[0]));
        Block("mpb after errors", e);

        var occ = new[] { new Vector4(0.1f, 0.2f, 0.3f, 0.4f), new Vector4(1f, 0f, 0.5f, 0.25f), new Vector4(0.9f, 0.8f, 0.7f, 0.6f) };
        var o = new MaterialPropertyBlock();
        o.CopyProbeOcclusionArrayFrom(occ);
        Block("mpb occlusion", o);
        var o2 = new MaterialPropertyBlock();
        o2.CopyProbeOcclusionArrayFrom(occ.ToList(), 1, 0, 2);
        Block("mpb occlusion range", o2);
        o2.CopyProbeOcclusionArrayFrom(occ, 0, 1, 1);
        Block("mpb occlusion range again", o2);
        var o3 = new MaterialPropertyBlock();
        Try("mpb occlusion null", () => o3.CopyProbeOcclusionArrayFrom((Vector4[])null));
        Try("mpb occlusion null list", () => o3.CopyProbeOcclusionArrayFrom((List<Vector4>)null));
        Try("mpb occlusion count beyond", () => o3.CopyProbeOcclusionArrayFrom(occ, 2, 0, 2));
        Try("mpb occlusion negative dest", () => o3.CopyProbeOcclusionArrayFrom(occ, 0, -1, 1));
    }

    IEnumerator Start()
    {
        for (int i = 0; i < 3; i++) yield return null;
        var output = Environment.GetEnvironmentVariable("LIGHT_PROBES_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) { Quit(); yield break; }

        var probes = LightmapSettings.lightProbes;
        var points = (Vector3[])Points.Clone();
        if (probes != null && probes.count > 5) points[3] = probes.positions[5];
        Events("start");
        Lights();
        Probes("main");
        Interpolate("main", points);
        Static(points);
        Query(points);
        PropertyBlocks(Synthetic());

        s_Step = "Tetrahedralize main";
        LightProbes.Tetrahedralize();
        Events("Tetrahedralize main");
        Put("main after Tetrahedralize counts", LightmapSettings.lightProbes == null ? "null"
            : $"{LightmapSettings.lightProbes.count} {LightmapSettings.lightProbes.cellCount} same {ReferenceEquals(probes, LightmapSettings.lightProbes)}");
        Interpolate("main retetrahedralized", points);
        yield return null;
        Events("frame after Tetrahedralize main");

        s_Step = "render";
        Render(output);
        yield return null;
        Events("render");

        // Extra, additively.
        s_Step = "load Extra";
        SceneManager.LoadScene("Extra", LoadSceneMode.Additive);
        Events("LoadScene call");
        yield return null;
        s_Step = "frame after load";
        yield return null;
        Events("after additive load");
        Put("additive same object", ReferenceEquals(probes, LightmapSettings.lightProbes).ToString());
        Lights();
        Probes("additive");
        Interpolate("additive", points);
        // TetrahedralizeAsync is not recorded: when its job completes (and its probes apply)
        // varies between Editor runs, from within frames to not before the next scene unload.
        s_Step = "Tetrahedralize additive";
        LightProbes.Tetrahedralize();
        Events("Tetrahedralize additive");
        Probes("additive tetrahedralized");
        Interpolate("additive tetrahedralized", points);
        s_Step = "unload Extra";
        var unload = SceneManager.UnloadSceneAsync("Extra");
        while (!unload.isDone) yield return null;
        s_Step = "frame after unload";
        yield return null;
        Events("after unload");
        Probes("unloaded");
        Interpolate("unloaded", points);
        s_Step = "Tetrahedralize unloaded";
        LightProbes.Tetrahedralize();
        Events("Tetrahedralize unloaded");
        Probes("unloaded tetrahedralized");
        Interpolate("unloaded tetrahedralized", points);

        File.WriteAllText(output, s_Out.ToString());
        Debug.Log("light probes probe written to " + output);
        Quit();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Three rows of spheres on their own layer, the lights off: instanced with the
    /// synthetic probes from a block (CustomProvided), instanced with BlendProbes, and
    /// MeshRenderers whose block holds one synthetic probe (CustomProvided).</summary>
    void Render(string output)
    {
        var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in lights) l.enabled = false;
        var camera = Camera.main;
        camera.cullingMask = 1 << ProbeLayer;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        primitive.SetActive(false);
        var material = new Material(Shader.Find("Standard")) { color = Color.white, enableInstancing = true };
        material.SetFloat("_Glossiness", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetFloat("_GlossyReflections", 0f);
        material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        var plain = new Material(material) { enableInstancing = false };

        var sh = Synthetic();
        float[] xs = { -1.6f, -0.8f, 0f, 0.8f, 1.6f };
        Matrix4x4 At(float x, float y) => Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, Vector3.one * 0.55f);
        var custom = xs.Select(x => At(x, 2.0f)).ToArray();
        var blended = xs.Select(x => At(x, 1.2f)).ToArray();
        var block = new MaterialPropertyBlock();
        block.CopySHCoefficientArraysFrom(sh);
        Graphics.DrawMeshInstanced(mesh, 0, material, custom, custom.Length, block, ShadowCastingMode.Off, false, ProbeLayer, camera, LightProbeUsage.CustomProvided);
        Graphics.DrawMeshInstanced(mesh, 0, material, blended, blended.Length, null, ShadowCastingMode.Off, false, ProbeLayer, camera, LightProbeUsage.BlendProbes);
        var renderers = new List<GameObject>();
        for (int i = 0; i < xs.Length; i++)
        {
            var go = new GameObject("Custom " + i, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = ProbeLayer;
            go.transform.position = new Vector3(xs[i], 0.4f, 0f);
            go.transform.localScale = Vector3.one * 0.55f;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = plain;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.CustomProvided;
            var one = new MaterialPropertyBlock();
            one.CopySHCoefficientArraysFrom(sh, i, 0, 1);
            r.SetPropertyBlock(one);
            renderers.Add(go);
        }
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.ChangeExtension(output, ".png"), texture.EncodeToPNG());
        var pixels = texture.GetPixels32();
        // The centre and four points around it of every sphere (projected while the camera
        // still draws into the target).
        foreach (var (row, y) in new[] { ("custom", 2.0f), ("blended", 1.2f), ("renderer", 0.4f) })
        {
            for (int i = 0; i < xs.Length; i++)
            {
                var samples = new List<string>();
                foreach (var d in new[] { Vector3.zero, new Vector3(0.18f, 0f, 0f), new Vector3(-0.18f, 0f, 0f), new Vector3(0f, 0.18f, 0f), new Vector3(0f, -0.18f, 0f) })
                {
                    var s = camera.WorldToScreenPoint(new Vector3(xs[i], y, 0f) + d);
                    var c = pixels[Mathf.FloorToInt(s.y) * Width + Mathf.FloorToInt(s.x)];
                    samples.Add($"{c.r} {c.g} {c.b}");
                }
                Put($"pixels {row} {i}", string.Join(" ", samples));
            }
        }
        camera.targetTexture = null;
        foreach (var go in renderers) Destroy(go);
        foreach (var l in lights) l.enabled = true;
        camera.cullingMask = ~0;
    }
}
