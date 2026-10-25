using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Edit-mode experiments on detail placement (ComputeDetailInstanceTransforms on terrains
/// made in memory): `-executeMethod GrassExperiments.Run` with <c>GRASS_EXPERIMENT_OUT</c>.</summary>
public static class GrassExperiments
{
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static TerrainData Make(DetailScatterMode mode, int resolution, int perPatch, int noiseSeed, float jitter, (int x, int y, int layer, int count)[] cells, int layers = 1,
        float minWidth = 1, float maxWidth = 2, float minHeight = 1, float maxHeight = 2, float spread = 0.1f, float targetCoverage = 1f, float density = 1f, bool hills = false, bool densityScaling = false)
    {
        var data = new TerrainData();
        data.heightmapResolution = 33;
        data.size = new Vector3(32f, 4f, 32f);
        if (hills)
        {
            var heights = new float[33, 33];
            for (int y = 0; y < 33; y++)
                for (int x = 0; x < 33; x++)
                    heights[y, x] = 0.2f + 0.15f * Mathf.Sin(x * 0.3f) * Mathf.Cos(y * 0.25f);
            data.SetHeights(0, 0, heights);
        }
        data.SetDetailScatterMode(mode);
        data.SetDetailResolution(resolution, perPatch);
        var prototypes = new List<DetailPrototype>();
        for (int i = 0; i < layers; i++)
            prototypes.Add(new DetailPrototype { prototypeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Grass/Blade.png"), renderMode = DetailRenderMode.Grass, minWidth = minWidth, maxWidth = maxWidth, minHeight = minHeight, maxHeight = maxHeight, noiseSeed = noiseSeed, noiseSpread = spread, positionJitter = jitter, targetCoverage = targetCoverage, density = density, useDensityScaling = densityScaling });
        data.detailPrototypes = prototypes.ToArray();
        for (int layer = 0; layer < layers; layer++)
        {
            var values = new int[resolution, resolution];
            foreach (var c in cells) if (c.layer == layer) values[c.y, c.x] = c.count;
            data.SetDetailLayer(0, 0, layer, values);
        }
        return data;
    }

    public static void Perlin()
    {
        var output = new StringBuilder();
        var values = new List<string>();
        foreach (float x in new[] { -300.25f, -256.5f, -255.5f, -3.5f, -2.2f, -1.75f, -1f, -0.5f, -0.25f, 0.25f, 0.5f, 1.75f, 255.5f, 256.5f, 300.25f })
            foreach (float y in new[] { -40.3f, -2.5f, -0.75f, -0.25f, 0.25f, 0.75f, 5.1f, 260.6f })
                values.Add($"[{F(x)},{F(y)},{F(Mathf.PerlinNoise(x, y))}]");
        var ones = new List<string>();
        foreach (float x in new[] { -300.25f, -3.5f, -0.5f, 0f, 0.25f, 0.5f, 1.3f, 10.7f, 255.5f, 300.25f })
            ones.Add($"[{F(x)},{F(Mathf.PerlinNoise1D(x))}]");
        output.Append($"{{\"perlin\":[{string.Join(",", values)}],\"perlin1d\":[{string.Join(",", ones)}]}}\n");
        File.WriteAllText(System.Environment.GetEnvironmentVariable("GRASS_EXPERIMENT_OUT") ?? "/tmp/perlin.json", output.ToString());
    }

    public static void Run()
    {
        var output = new StringBuilder();
        void Experiment(string name, TerrainData data, int layer, int px, int py, float density = 1f)
        {
            var instances = data.ComputeDetailInstanceTransforms(px, py, layer, density, out var bounds);
            output.Append($"{{\"name\":\"{name}\",\"count\":{instances.Length},\"instances\":[{string.Join(",", instances.Select(i => $"[{F(i.posX)},{F(i.posY)},{F(i.posZ)},{F(i.scaleXZ)},{F(i.scaleY)},{F(i.rotationY)}]"))}]}}\n");
        }
        var cov = DetailScatterMode.CoverageMode;
        TerrainData C((int, int, int, int)[] cells, float w = 0.2f, float density = 1f, float jitter = 0f, float target = 1f, bool scaling = false, int seed = 0) =>
            Make(cov, 64, 16, seed, jitter, cells, minWidth: w, maxWidth: w, minHeight: 0.3f, maxHeight: 0.3f, targetCoverage: target, density: density, densityScaling: scaling);
        foreach (float w in new[] { 0.55f, 0.65f, 0.7f, 0.75f, 1.2f, 1.75f, 2.0f, 3.0f })
            for (int seed = 0; seed < 200; seed++)
                Experiment($"seedscan w{w} seed{seed}", C(new[] { (0, 0, 0, 255) }, w: w, seed: seed), 0, 0, 0);
        for (int seed = 0; seed < 200; seed++)
            Experiment($"pair w1.5 seed{seed}", C(new[] { (0, 0, 0, 255), (1, 0, 0, 255) }, w: 1.5f, seed: seed), 0, 0, 0);
        for (int seed = 0; seed < 200; seed++)
            Experiment($"pair w1 seed{seed}", C(new[] { (0, 0, 0, 255), (1, 0, 0, 255) }, w: 1f, seed: seed), 0, 0, 0);
        for (int seed = 0; seed < 200; seed++)
            Experiment($"cell55 w1.5 seed{seed}", C(new[] { (5, 5, 0, 255) }, w: 1.5f, seed: seed), 0, 0, 0);
        File.WriteAllText(System.Environment.GetEnvironmentVariable("GRASS_EXPERIMENT_OUT") ?? "/tmp/perlin.json", output.ToString());
    }
}
