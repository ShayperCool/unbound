using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Terrain details: for each terrain of the scene (detail instance counts, coverage) the detail
/// data (resolution, patches, prototypes, layers read back), every patch's and layer's instances
/// from ComputeDetailInstanceTransforms at two densities, the coverage; then images of the grass
/// from a camera. Then the same for the terrains of the scene `Density`, loaded additively, with
/// their prototypes' validation, a third density, the legacy distribution and the logged messages;
/// then for the scene `Aligned`. Writes <c>grass.jsonl</c> and PNGs into <c>GRASS_PROBE_OUT</c>, then quits.
/// </summary>
public class GrassProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("GrassProbe").AddComponent<GrassProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("GRASS_PROBE_OUT") ?? "/tmp/grass";
        Directory.CreateDirectory(dir);
        // Mathf.PerlinNoise (detail sizes and colours follow it), negative and large inputs too.
        var perlin = new List<string>();
        foreach (float x in new[] { -300.25f, -2.2f, -0.5f, 0f, 0.1f, 0.5f, 1.3f, 10.5f, 255.5f, 300.25f })
            foreach (float y in new[] { -40.3f, -0.25f, 0f, 0.25f, 2.7f, 260.6f })
                perlin.Add(F(Mathf.PerlinNoise(x, y)));
        Record("perlin", $"\"values\":[{string.Join(",", perlin)}],\"oneD\":[{F(Mathf.PerlinNoise1D(-3.5f))},{F(Mathf.PerlinNoise1D(0.25f))},{F(Mathf.PerlinNoise1D(10.7f))}]");

        // Messages the terrain API logs, recorded with their types at the end.
        var messages = new List<string>();
        Application.LogCallback onLog = (message, stack, type) => messages.Add($"{type}: {message}");
        Application.logMessageReceived += onLog;
        // The probe scene's terrains, their records and images; then the scene `Density`'s.
        var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderBy(t => t.name).ToArray();
        foreach (var terrain in terrains)
            RecordTerrain(terrain, false);

        // Images: each terrain from above at an angle (waving is off in the data).
        var target = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
        var camera = new GameObject("camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.4f, 0.5f, 0.7f, 1f);
        camera.targetTexture = target;
        var light = new GameObject("light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        yield return null;
        foreach (var terrain in terrains)
            Render(camera, target, terrain, dir);

        yield return SceneManager.LoadSceneAsync("Density", LoadSceneMode.Additive);
        var added = FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => !terrains.Contains(t)).OrderBy(t => t.name).ToArray();
        foreach (var terrain in added)
            RecordTerrain(terrain, true);
        // QualitySettings.useLegacyDetailDistribution: the density terrains again, then back.
        QualitySettings.useLegacyDetailDistribution = true;
        foreach (var terrain in added)
            if (terrain.name.StartsWith("Density"))
                RecordInstances("legacy", terrain, 1f);
        QualitySettings.useLegacyDetailDistribution = false;
        Application.logMessageReceived -= onLog;
        foreach (var group in messages.GroupBy(m => m))
            Record("log", $"\"message\":\"{group.Key}\",\"count\":{group.Count()}");
        yield return null;
        foreach (var terrain in added)
            Render(camera, target, terrain, dir);

        // The scene `Aligned`: grass textures aligned to steep ground, and one of zero width.
        var before = FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        yield return SceneManager.LoadSceneAsync("Aligned", LoadSceneMode.Additive);
        var aligned = FindObjectsByType<Terrain>(FindObjectsSortMode.None).Where(t => !before.Contains(t)).OrderBy(t => t.name).ToArray();
        foreach (var terrain in aligned)
            RecordTerrain(terrain, true);
        yield return null;
        foreach (var terrain in aligned)
            Render(camera, target, terrain, dir);

        File.WriteAllText(Path.Combine(dir, "grass.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>A terrain's detail data, layers and instances; for the added terrains also what
    /// their prototypes validate to, and the density terrains at a third density.</summary>
    void RecordTerrain(Terrain terrain, bool added)
    {
        var data = terrain.terrainData;
        Record("data", $"\"terrain\":\"{terrain.name}\",\"mode\":\"{data.detailScatterMode}\",\"resolution\":{data.detailResolution},\"perPatch\":{data.detailResolutionPerPatch},\"patches\":{data.detailPatchCount},\"width\":{data.detailWidth},\"height\":{data.detailHeight},\"prototypes\":{data.detailPrototypes.Length},\"supported\":[{string.Join(",", data.GetSupportedLayers(0, 0, data.detailWidth, data.detailHeight))}],\"objectDistance\":{F(terrain.detailObjectDistance)},\"objectDensity\":{F(terrain.detailObjectDensity)}");
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            var values = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);
            long sum = 0;
            var rows = new List<string>();
            for (int y = 0; y < data.detailHeight; y += 9)
            {
                var row = new List<string>();
                for (int x = 0; x < data.detailWidth; x += 5) row.Add(values[y, x].ToString());
                rows.Add("[" + string.Join(",", row) + "]");
            }
            foreach (var v in values) sum += v;
            Record("layer", $"\"terrain\":\"{terrain.name}\",\"layer\":{layer},\"sum\":{sum},\"samples\":[{string.Join(",", rows)}],\"coverage\":{F(data.ComputeDetailCoverage(layer))}");
        }
        if (added)
        {
            var prototypes = data.detailPrototypes;
            for (int i = 0; i < prototypes.Length; i++)
            {
                bool valid = prototypes[i].Validate(out var error);
                Record("prototype", $"\"terrain\":\"{terrain.name}\",\"layer\":{i},\"valid\":{(valid ? "true" : "false")},\"error\":\"{error}\"");
            }
            Record("coverage_index", $"\"terrain\":\"{terrain.name}\",\"values\":[{F(data.ComputeDetailCoverage(-1))},{F(data.ComputeDetailCoverage(prototypes.Length))}]");
        }
        foreach (var density in terrain.name.StartsWith("Density") ? new[] { 1f, 0.5f, 0.3f } : new[] { 1f, 0.5f })
            RecordInstances("instances", terrain, density);
    }

    static void Render(Camera camera, RenderTexture target, Terrain terrain, string dir)
    {
        var origin = terrain.transform.position;
        camera.transform.position = origin + new Vector3(16f, 8f, -6f);
        camera.transform.LookAt(origin + new Vector3(16f, 1f, 14f));
        camera.Render();
        Save(target, Path.Combine(dir, $"{terrain.name}.png"));
    }

    void RecordInstances(string kind, Terrain terrain, float density)
    {
        var data = terrain.terrainData;
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            for (int py = 0; py < data.detailPatchCount; py++)
                for (int px = 0; px < data.detailPatchCount; px++)
                {
                    var instances = data.ComputeDetailInstanceTransforms(px, py, layer, density, out var bounds);
                    var list = instances.Take(40).Select(i => $"[{F(i.posX)},{F(i.posY)},{F(i.posZ)},{F(i.scaleXZ)},{F(i.scaleY)},{F(i.rotationY)}]");
                    Record(kind, $"\"terrain\":\"{terrain.name}\",\"density\":{F(density)},\"layer\":{layer},\"patch\":[{px},{py}],\"count\":{instances.Length},\"bounds\":[{F(bounds.min.x)},{F(bounds.min.y)},{F(bounds.min.z)},{F(bounds.max.x)},{F(bounds.max.y)},{F(bounds.max.z)}],\"first\":[{string.Join(",", list)}]");
                }
    }

    static void Save(RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Destroy(texture);
    }
}
