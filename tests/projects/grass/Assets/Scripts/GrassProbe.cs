using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Terrain details: for each terrain of the scene (detail instance counts, coverage) the detail
/// data (resolution, patches, prototypes, layers read back), every patch's and layer's instances
/// from ComputeDetailInstanceTransforms at two densities, the coverage; then images of the grass
/// from a camera. Writes <c>grass.jsonl</c> and PNGs into <c>GRASS_PROBE_OUT</c>, then quits.
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

        var terrains = FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderBy(t => t.name).ToArray();
        foreach (var terrain in terrains)
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
            foreach (var density in new[] { 1f, 0.5f })
                for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
                    for (int py = 0; py < data.detailPatchCount; py++)
                        for (int px = 0; px < data.detailPatchCount; px++)
                        {
                            var instances = data.ComputeDetailInstanceTransforms(px, py, layer, density, out var bounds);
                            var list = instances.Take(40).Select(i => $"[{F(i.posX)},{F(i.posY)},{F(i.posZ)},{F(i.scaleXZ)},{F(i.scaleY)},{F(i.rotationY)}]");
                            Record("instances", $"\"terrain\":\"{terrain.name}\",\"density\":{F(density)},\"layer\":{layer},\"patch\":[{px},{py}],\"count\":{instances.Length},\"bounds\":[{F(bounds.min.x)},{F(bounds.min.y)},{F(bounds.min.z)},{F(bounds.max.x)},{F(bounds.max.y)},{F(bounds.max.z)}],\"first\":[{string.Join(",", list)}]");
                        }
        }

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
        {
            var origin = terrain.transform.position;
            camera.transform.position = origin + new Vector3(16f, 8f, -6f);
            camera.transform.LookAt(origin + new Vector3(16f, 1f, 14f));
            camera.Render();
            Save(target, Path.Combine(dir, $"{terrain.name}.png"));
        }

        File.WriteAllText(Path.Combine(dir, "grass.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
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
