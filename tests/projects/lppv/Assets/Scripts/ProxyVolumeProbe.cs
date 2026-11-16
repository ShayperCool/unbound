using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Light probe proxy volumes over the baked probes of tests/projects/lightprobes' Main scene
/// (LPPV_OUT): for each case, what a draw through the volume reads (Hidden/ProxyVolumeDump: the
/// volume's constants, the draw's probe constants and every texel of unity_ProbeVolumeSH) and the
/// volume's settings as the API answers them; then a Standard object lit through the volume and
/// the same object with Blend Probes, as images.
/// </summary>
public class ProxyVolumeProbe : MonoBehaviour
{
    const int DumpLayer = 30, ImageLayer = 29;
    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    string output;
    Material dump;
    Mesh quad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("LPPV_OUT") == null) return;
        new GameObject("ProxyVolumeProbe").AddComponent<ProxyVolumeProbe>();
    }

    void Value(string k, string json) => records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n");
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";
    static string V3(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    T Keep<T>(T o) where T : UnityEngine.Object { made.Add(o); return o; }
    void OnLog(string condition, string stack, LogType type) => logs.Add(type + ": " + condition);

    IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("LPPV_OUT");
        Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        int code = 0;
        try { Setup(); }
        catch (Exception e) { Value("error", Q(e.ToString())); code = 1; }
        // Volumes are built in a later frame.
        for (int i = 0; i < 3; i++) yield return null;
        if (code == 0)
        {
            try { Measure(); }
            catch (Exception e) { Value("error", Q(e.ToString())); code = 1; }
        }
        Value("logs", "[" + string.Join(",", logs.Select(Q)) + "]");
        Value("complete", "true");
        File.WriteAllText(Path.Combine(output, "values.jsonl"), records.ToString());
        Application.logMessageReceived -= OnLog;
        foreach (var o in made) if (o != null) DestroyImmediate(o);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    GameObject Box(string name, Vector3 position, Quaternion rotation, Vector3 scale, int layer, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = layer;
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        made.Add(go);
        return go;
    }

    /// <summary>What a draw through <paramref name="volume"/> reads: a quad on the dump layer in
    /// front of its own camera, its renderer using the volume (override).</summary>
    void Dump(string key, LightProbeProxyVolume volume)
    {
        Value(key + ".settings", "[" + Q(volume.boundingBoxMode.ToString()) + "," + Q(volume.resolutionMode.ToString()) + ","
            + Q(volume.probePositionMode.ToString()) + "," + Q(volume.refreshMode.ToString()) + "," + Q(volume.qualityMode.ToString()) + ","
            + Q(volume.dataFormat.ToString()) + "," + volume.gridResolutionX + "," + volume.gridResolutionY + "," + volume.gridResolutionZ + ","
            + F(volume.probeDensity) + "]");
        Value(key + ".bounds", "[" + V3(volume.boundsGlobal.center) + "," + V3(volume.boundsGlobal.size) + "]");
        var go = new GameObject("dump " + key) { layer = DumpLayer };
        made.Add(go);
        go.transform.position = new Vector3(0f, -50f, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = dump;
        renderer.lightProbeUsage = LightProbeUsage.UseProxyVolume;
        renderer.lightProbeProxyVolumeOverride = volume.gameObject;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var target = Keep(new RenderTexture(128, 1026, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        target.Create();
        var camera = new GameObject("dump camera").AddComponent<Camera>();
        made.Add(camera.gameObject);
        camera.enabled = false;
        camera.transform.position = new Vector3(0f, -50f, -2f);
        camera.cullingMask = 1 << DumpLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(-4f, -4f, -4f, -4f);
        camera.targetTexture = target;
        camera.Render();
        var cpu = new Texture2D(128, 1026, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        cpu.ReadPixels(new Rect(0, 0, 128, 1026), 0, 0);
        RenderTexture.active = previous;
        var pixels = cpu.GetPixels();
        DestroyImmediate(cpu);
        go.SetActive(false);
        Color At(int x, int y) => pixels[y * 128 + x];
        Value(key + ".params", V(At(0, 0)));
        Value(key + ".worldToObject", "[" + V(At(1, 0)) + "," + V(At(2, 0)) + "," + V(At(3, 0)) + "," + V(At(4, 0)) + "]");
        Value(key + ".sizeInv", V(At(5, 0)));
        Value(key + ".min", V(At(6, 0)));
        Value(key + ".centre", V(At(7, 0)));
        Value(key + ".sh", "[" + string.Join(",", Enumerable.Range(0, 7).Select(i => V(At(i, 1)))) + "]");
        // The texture's width from the texel size (params.z = 1 / width); its height and depth from
        // the resolution the volume reports, else up to the largest (32) while texels are written.
        var parameters = At(0, 0);
        int w = parameters.b > 0f ? Mathf.RoundToInt(1f / parameters.b) : 0;
        // Every texel row of a volume up to 128 probes; of a larger one its size in probes and
        // its first and last rows.
        var rows = new List<string>();
        int ry = 0, rz = 0;
        for (int z = 0; z < 32; z++)
            for (int y = 0; y < 32; y++)
            {
                var row = Enumerable.Range(0, Math.Min(w, 128)).Select(x => At(x, 2 + 32 * z + y)).ToList();
                if (row.All(c => c == Color.clear)) continue;
                ry = Math.Max(ry, y + 1);
                rz = Math.Max(rz, z + 1);
                rows.Add("[" + z + "," + y + "," + string.Join(",", row.Select(c => V(c))) + "]");
            }
        Value(key + ".cells", "[" + w / 4 + "," + ry + "," + rz + "]");
        if (w / 4 * ry * rz > 128 && rows.Count > 2) rows = new List<string> { rows[0], rows[rows.Count - 1] };
        Value(key + ".texels", "[" + string.Join(",", rows) + "]");
    }

    void Image(string key, Camera camera)
    {
        var target = Keep(new RenderTexture(160, 120, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = null;
        var cpu = new Texture2D(160, 120, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        cpu.ReadPixels(new Rect(0, 0, 160, 120), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(output, key + ".png"), cpu.EncodeToPNG());
        DestroyImmediate(cpu);
    }

    GameObject plain, lit;
    readonly List<(string Key, LightProbeProxyVolume Volume)> volumes = new List<(string, LightProbeProxyVolume)>();

    void Setup()
    {
        dump = Keep(new Material(Shader.Find("Hidden/ProxyVolumeDump")));
        quad = Keep(new Mesh { name = "dump quad" });
        quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        quad.bounds = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
        var standard = Keep(new Material(Shader.Find("Standard")) { color = Color.white });
        standard.SetFloat("_Glossiness", 0.1f);
        Value("isFeatureSupported", LightProbeProxyVolume.isFeatureSupported ? "true" : "false");
        var ambient = RenderSettings.ambientProbe;
        Value("ambientProbe", "[" + string.Join(",", Enumerable.Range(0, 27).Select(i => F(ambient[i / 9, i % 9]))) + "]");

        // Defaults over a box stretched through the lamp's probes.
        plain = Box("plain", new Vector3(-0.4f, 1.3f, -0.6f), Quaternion.identity, new Vector3(3.2f, 1.6f, 0.6f), 0, standard);
        volumes.Add(("default", plain.AddComponent<LightProbeProxyVolume>()));

        // The image's box: its renderer uses its volume (a gradient of probes along it).
        lit = Box("lit", new Vector3(-0.4f, 1.3f, -0.6f), Quaternion.identity, new Vector3(3.2f, 1.6f, 0.6f), 0, standard);
        lit.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.UseProxyVolume;
        var litVolume = lit.AddComponent<LightProbeProxyVolume>();
        litVolume.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
        litVolume.gridResolutionX = 8; litVolume.gridResolutionY = 4; litVolume.gridResolutionZ = 2;
        litVolume.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        volumes.Add(("lit", litVolume));

        // World bounds, a custom resolution, cell corners, half texels.
        var turned = Box("turned", new Vector3(0.2f, 1.1f, 0.8f), Quaternion.Euler(10f, 35f, 0f), new Vector3(2.4f, 1.2f, 0.8f), 0, standard);
        var world = turned.AddComponent<LightProbeProxyVolume>();
        world.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.AutomaticWorld;
        world.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
        world.gridResolutionX = 4; world.gridResolutionY = 2; world.gridResolutionZ = 3;
        world.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        world.dataFormat = LightProbeProxyVolume.DataFormat.HalfFloat;
        volumes.Add(("world", world));

        // The same object in local bounds (rotated: the shader's local space).
        var local = Box("local", turned.transform.position, turned.transform.rotation, turned.transform.localScale, 0, standard);
        var localVolume = local.AddComponent<LightProbeProxyVolume>();
        localVolume.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
        localVolume.gridResolutionX = 4; localVolume.gridResolutionY = 2; localVolume.gridResolutionZ = 3;
        volumes.Add(("local", localVolume));

        // Custom bounds, Low quality (L0 only), a denser automatic grid.
        var custom = Box("custom", new Vector3(-1f, 0.8f, 0f), Quaternion.identity, new Vector3(1f, 1f, 1f), 0, standard);
        var customVolume = custom.AddComponent<LightProbeProxyVolume>();
        customVolume.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
        customVolume.sizeCustom = new Vector3(2.5f, 1.5f, 2f);
        customVolume.originCustom = new Vector3(0.3f, 0.2f, -0.1f);
        customVolume.qualityMode = LightProbeProxyVolume.QualityMode.Low;
        customVolume.probeDensity = 2f;
        volumes.Add(("custom", customVolume));
        // Automatic resolution sweep (custom bounds, density 1), world bounds of an
        // unrotated scaled box and of a far rotated one, custom bounds on a rotated scaled object.
        float[][] sizes = { new[] { 1f, 2f, 3f }, new[] { 4f, 5f, 7f }, new[] { 8f, 9f, 16f }, new[] { 17f, 31f, 40f }, new[] { 0.3f, 1.5f, 2.5f } };
        for (int i = 0; i < sizes.Length; i++)
        {
            var go = Box("res" + i, new Vector3(0f, 1f, 0f), Quaternion.identity, Vector3.one, 0, standard);
            var v = go.AddComponent<LightProbeProxyVolume>();
            v.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
            v.sizeCustom = new Vector3(sizes[i][0], sizes[i][1], sizes[i][2]);
            v.probeDensity = 1f;
            v.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
            volumes.Add(("res" + i, v));
        }
        var scaled = Box("scaledworld", new Vector3(-0.5f, 1.2f, 0f), Quaternion.identity, new Vector3(2f, 1f, 0.5f), 0, standard);
        var sw = scaled.AddComponent<LightProbeProxyVolume>();
        sw.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.AutomaticWorld;
        sw.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        volumes.Add(("scaledworld", sw));
        var scaledLocal = Box("scaledlocal", new Vector3(-0.5f, 1.2f, 0f), Quaternion.identity, new Vector3(2f, 1f, 0.5f), 0, standard);
        var sl = scaledLocal.AddComponent<LightProbeProxyVolume>();
        sl.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        sl.probeDensity = 4f;
        volumes.Add(("scaledlocal", sl));
        var rc = Box("rotatedcustom", new Vector3(0.5f, 1.2f, -0.5f), Quaternion.Euler(0f, 30f, 0f), new Vector3(2f, 1f, 1f), 0, standard);
        var rcv = rc.AddComponent<LightProbeProxyVolume>();
        rcv.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
        rcv.sizeCustom = new Vector3(2f, 1f, 1.5f);
        rcv.originCustom = new Vector3(0.2f, 0.1f, 0f);
        rcv.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        rcv.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
        rcv.gridResolutionX = 2; rcv.gridResolutionY = 2; rcv.gridResolutionZ = 2;
        volumes.Add(("rotatedcustom", rcv));
        float[][] corner = { new[] { 1f, 2f, 3f }, new[] { 4f, 5f, 6f }, new[] { 7f, 8f, 9f }, new[] { 12f, 1.5f, 2.5f } };
        for (int i = 0; i < corner.Length; i++)
        {
            var go = Box("corner" + i, new Vector3(0f, 1f, 0f), Quaternion.identity, Vector3.one, 0, standard);
            var v = go.AddComponent<LightProbeProxyVolume>();
            v.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
            v.sizeCustom = new Vector3(corner[i][0], corner[i][1], corner[i][2]);
            v.probeDensity = 1f;
            v.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCorner;
            volumes.Add(("corner" + i, v));
        }
        var ties = Box("ties", new Vector3(0f, 1f, 0f), Quaternion.identity, Vector3.one, 0, standard);
        var tv = ties.AddComponent<LightProbeProxyVolume>();
        tv.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
        tv.sizeCustom = new Vector3(6f, 12f, 24f);
        tv.probeDensity = 1f;
        tv.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        volumes.Add(("ties", tv));
        // AutomaticWorld on a mesh whose local bounds are 3 x 1 x 2 around (0.5, 0, 0), rotated and scaled.
        var odd = new Mesh { name = "odd" };
        odd.vertices = new[] { new Vector3(-1f, -0.5f, -1f), new Vector3(2f, -0.5f, -1f), new Vector3(2f, 0.5f, 1f), new Vector3(-1f, 0.5f, 1f) };
        odd.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        odd.RecalculateBounds();
        Keep(odd);
        var oddGo = new GameObject("oddworld");
        made.Add(oddGo);
        oddGo.transform.SetPositionAndRotation(new Vector3(-0.3f, 1.1f, 0.2f), Quaternion.Euler(0f, 20f, 0f));
        oddGo.transform.localScale = new Vector3(1.5f, 1f, 1f);
        oddGo.AddComponent<MeshFilter>().sharedMesh = odd;
        oddGo.AddComponent<MeshRenderer>().sharedMaterial = standard;
        var ow = oddGo.AddComponent<LightProbeProxyVolume>();
        ow.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.AutomaticWorld;
        ow.probePositionMode = LightProbeProxyVolume.ProbePositionMode.CellCenter;
        volumes.Add(("oddworld", ow));
        var oddLocal = Instantiate(oddGo);
        made.Add(oddLocal);
        oddLocal.GetComponent<LightProbeProxyVolume>().boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.AutomaticLocal;
        volumes.Add(("oddlocal", oddLocal.GetComponent<LightProbeProxyVolume>()));
        int n = 0;
        foreach (var quality in new[] { LightProbeProxyVolume.QualityMode.Normal, LightProbeProxyVolume.QualityMode.Low })
            foreach (var mode in new[] { LightProbeProxyVolume.ProbePositionMode.CellCenter, LightProbeProxyVolume.ProbePositionMode.CellCorner })
                foreach (var density in new[] { 2f, 0.5f })
                {
                    var go = Box("d" + n, new Vector3(0f, 1f, 0f), Quaternion.identity, Vector3.one, 0, standard);
                    var v = go.AddComponent<LightProbeProxyVolume>();
                    v.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
                    v.sizeCustom = new Vector3(2.5f, 1.5f, 6f);
                    v.probeDensity = density;
                    v.probePositionMode = mode;
                    v.qualityMode = quality;
                    volumes.Add(("d" + n + "_" + quality + "_" + mode + "_" + density, v));
                    n++;
                }
        // Automatic bounds of renderers that use the volume: own and a child's.
        foreach (var inWorld in new[] { false, true })
        {
            var parent = new GameObject(inWorld ? "ownworld" : "ownlocal");
            made.Add(parent);
            parent.transform.SetPositionAndRotation(new Vector3(-0.3f, 1.1f, 0.2f), Quaternion.Euler(0f, 20f, 0f));
            parent.transform.localScale = new Vector3(1.5f, 1f, 1f);
            parent.AddComponent<MeshFilter>().sharedMesh = odd;
            var pr = parent.AddComponent<MeshRenderer>();
            pr.sharedMaterial = standard;
            pr.lightProbeUsage = LightProbeUsage.UseProxyVolume;
            var child = Box("child", Vector3.zero, Quaternion.identity, new Vector3(0.5f, 2f, 0.5f), 0, standard);
            child.transform.SetParent(parent.transform, false);
            child.transform.localPosition = new Vector3(-1.5f, 0.5f, 0f);
            child.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.UseProxyVolume;
            var v = parent.AddComponent<LightProbeProxyVolume>();
            v.boundingBoxMode = inWorld ? LightProbeProxyVolume.BoundingBoxMode.AutomaticWorld : LightProbeProxyVolume.BoundingBoxMode.AutomaticLocal;
            v.probeDensity = 2f;
            volumes.Add((parent.name, v));
        }
    }

    void Measure()
    {
        foreach (var (key, volume) in volumes) Dump(key, volume);
        // The lit box through its volume, then with Blend Probes (lights off: probes only).
        foreach (var (key, volume) in volumes) if (volume.gameObject != lit) volume.gameObject.SetActive(false);
        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.InstanceID)) light.enabled = false;
        lit.layer = ImageLayer;
        var camera = new GameObject("image camera").AddComponent<Camera>();
        made.Add(camera.gameObject);
        camera.enabled = false;
        camera.transform.position = new Vector3(-0.4f, 1.3f, -4f);
        camera.cullingMask = 1 << ImageLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var renderer = lit.GetComponent<Renderer>();
        Image("volume", camera);
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        Image("blend", camera);
    }
}
