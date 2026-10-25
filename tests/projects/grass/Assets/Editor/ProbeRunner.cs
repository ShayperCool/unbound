using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -quit -projectPath tests/projects/grass -executeMethod ProbeRunner.Create` makes
/// the probe's assets once: a grass blade texture, a rock prefab, two TerrainData assets with the
/// same detail prototypes and painted detail layers (one scattering by instance counts, one by
/// coverage), and the scene with a terrain for each. `-executeMethod ProbeRunner.Record` with
/// <c>GRASS_PROBE_OUT</c> set plays the scene, where <see cref="GrassProbe"/> records and exits.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Grass", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Grass");
        Directory.CreateDirectory("Assets/Scenes");

        // A grass blade: opaque in a tapering column, transparent around.
        var blade = new Texture2D(16, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 16; x++)
            {
                float half = 7f * (1f - y / 34f);
                bool inside = Mathf.Abs(x - 7.5f) <= half;
                blade.SetPixel(x, y, inside ? new Color(0.9f, 0.95f, 0.85f, 1f) : new Color(0f, 0f, 0f, 0f));
            }
        File.WriteAllBytes("Assets/Grass/Blade.png", blade.EncodeToPNG());
        AssetDatabase.ImportAsset("Assets/Grass/Blade.png");
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Grass/Blade.png");
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var bladeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Grass/Blade.png");

        var rockObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rockObject.name = "Rock";
        Object.DestroyImmediate(rockObject.GetComponent<Collider>());
        var rock = PrefabUtility.SaveAsPrefabAsset(rockObject, "Assets/Grass/Rock.prefab");
        Object.DestroyImmediate(rockObject);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var counts = MakeData("Counts", DetailScatterMode.InstanceCountMode, bladeTexture, rock);
        var coverage = MakeData("Coverage", DetailScatterMode.CoverageMode, bladeTexture, rock);
        var a = Terrain.CreateTerrainGameObject(counts);
        a.name = "Counts";
        var b = Terrain.CreateTerrainGameObject(coverage);
        b.name = "Coverage";
        b.transform.position = new Vector3(40f, 0f, 0f);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static TerrainData MakeData(string name, DetailScatterMode mode, Texture2D blade, GameObject rock)
    {
        var data = new TerrainData();
        AssetDatabase.CreateAsset(data, $"Assets/Grass/{name}.asset");
        data.heightmapResolution = 33;
        data.size = new Vector3(32f, 4f, 32f);
        var heights = new float[33, 33];
        for (int y = 0; y < 33; y++)
            for (int x = 0; x < 33; x++)
                heights[y, x] = 0.2f + 0.15f * Mathf.Sin(x * 0.3f) * Mathf.Cos(y * 0.25f);
        data.SetHeights(0, 0, heights);
        data.SetDetailScatterMode(mode);
        data.SetDetailResolution(64, 16);
        data.detailPrototypes = new[]
        {
            new DetailPrototype
            {
                prototypeTexture = blade, renderMode = DetailRenderMode.Grass, usePrototypeMesh = false,
                minWidth = 0.5f, maxWidth = 1.2f, minHeight = 0.4f, maxHeight = 1.1f,
                noiseSeed = 123, noiseSpread = 0.2f, healthyColor = new Color(0.3f, 0.9f, 0.2f), dryColor = new Color(0.8f, 0.7f, 0.1f),
            },
            new DetailPrototype
            {
                prototypeTexture = blade, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false,
                minWidth = 1f, maxWidth = 1f, minHeight = 1f, maxHeight = 2f, noiseSeed = 7, noiseSpread = 0.5f,
            },
            new DetailPrototype
            {
                prototype = rock, renderMode = DetailRenderMode.VertexLit, usePrototypeMesh = true, useInstancing = false,
                minWidth = 0.3f, maxWidth = 0.6f, minHeight = 0.2f, maxHeight = 0.5f, noiseSeed = 99, noiseSpread = 1f,
                alignToGround = 0.5f, positionJitter = 0.8f,
            },
            new DetailPrototype
            {
                prototype = rock, renderMode = DetailRenderMode.VertexLit, usePrototypeMesh = true, useInstancing = true,
                minWidth = 0.2f, maxWidth = 0.2f, minHeight = 0.2f, maxHeight = 0.2f, noiseSeed = 5, density = 0.5f,
            },
        };
        for (int layer = 0; layer < 4; layer++)
        {
            var values = new int[64, 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int v = layer switch
                    {
                        0 => (x + 2 * y) % 7 < 4 ? (x * 3 + y) % 9 : 0,
                        1 => x > 40 && y < 20 ? 3 : 0,
                        2 => (x % 9 == 0 && y % 7 == 0) ? 2 : 0,
                        _ => (x / 8 + y / 8) % 2 == 0 && x < 32 ? 1 : 0,
                    };
                    values[y, x] = mode == DetailScatterMode.CoverageMode ? Mathf.Min(255, v * 28) : v;
                }
            data.SetDetailLayer(0, 0, layer, values);
        }
        data.wavingGrassAmount = 0f;
        data.wavingGrassStrength = 0f;
        data.wavingGrassSpeed = 0f;
        EditorUtility.SetDirty(data);
        return data;
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
