using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -quit -projectPath tests/projects/terrainlod -executeMethod ProbeRunner.Create`
/// makes the probe's assets once: two generated textures and terrain layers, two bumpy 257² tiles
/// over 400 × 60 × 400 units painted with both (the second east of the first, its heights
/// continuing them), and the scene with both terrains (drawn without instancing) and a
/// directional light. `-executeMethod ProbeRunner.Record` with
/// <c>TERRAIN_LOD_PROBE_OUT</c> set plays the scene, where <see cref="TerrainLodProbe"/> records.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Terrain", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Terrain");
        Directory.CreateDirectory("Assets/Scenes");
        var rock = Layer("Rock", (x, y) => new Color(0.45f + 0.08f * Mathf.Sin(x * 0.4f), 0.42f, 0.38f + 0.06f * Mathf.Cos(y * 0.3f), 1f), 12f);
        var grass = Layer("Grass", (x, y) => new Color(0.25f, 0.45f + 0.1f * Mathf.Sin((x + y) * 0.2f), 0.18f, 1f), 9f);

        var data = Bumpy("Bumpy", 0, rock, grass);
        var east = Bumpy("BumpyEast", 256, rock, grass);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = "Bumpy";
        terrainObject.GetComponent<Terrain>().drawInstanced = false;
        // A second tile east of it, its heights continuing across the seam: connected tiles.
        var eastObject = Terrain.CreateTerrainGameObject(east);
        eastObject.name = "BumpyEast";
        eastObject.transform.position = new Vector3(400f, 0f, 0f);
        eastObject.GetComponent<Terrain>().drawInstanced = false;
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(35f, 60f, 0f);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    /// <summary>A bumpy 257² terrain over 400 × 60 × 400 units painted with both layers, its height
    /// function read from column offset on (so a tile offset by 256 continues one at 0).</summary>
    static TerrainData Bumpy(string name, int offset, TerrainLayer rock, TerrainLayer grass)
    {
        var data = new TerrainData();
        AssetDatabase.CreateAsset(data, $"Assets/Terrain/{name}.asset");
        data.heightmapResolution = 257;
        data.size = new Vector3(400f, 60f, 400f);
        data.alphamapResolution = 64;
        data.baseMapResolution = 256;
        var heights = new float[257, 257];
        for (int y = 0; y < 257; y++)
            for (int column = 0; column < 257; column++)
            {
                int x = column + offset;
                float h = 0.35f + 0.18f * Mathf.Sin(x * 0.045f) * Mathf.Cos(y * 0.038f) + 0.08f * Mathf.Sin(x * 0.23f + y * 0.17f)
                    + 0.04f * Mathf.Sin(x * 0.71f) * Mathf.Sin(y * 0.63f);
                // A ridge and a crater give the error estimate sharp places.
                h += 0.15f * Mathf.Max(0f, 1f - Mathf.Abs(x - 1.2f * y + 40f) / 12f);
                float d = Mathf.Sqrt((x - 180f) * (x - 180f) + (y - 70f) * (y - 70f));
                h -= 0.12f * Mathf.Max(0f, 1f - d / 25f);
                heights[y, column] = Mathf.Clamp01(h);
            }
        data.SetHeights(0, 0, heights);
        data.terrainLayers = new[] { rock, grass };
        var alphas = new float[64, 64, 2];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float g = Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(x * 0.2f + y * 0.11f));
                alphas[y, x, 0] = 1f - g;
                alphas[y, x, 1] = g;
            }
        data.SetAlphamaps(0, 0, alphas);
        EditorUtility.SetDirty(data);
        return data;
    }

    static TerrainLayer Layer(string name, System.Func<int, int, Color> color, float tile)
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                texture.SetPixel(x, y, color(x, y));
        File.WriteAllBytes($"Assets/Terrain/{name}.png", texture.EncodeToPNG());
        AssetDatabase.ImportAsset($"Assets/Terrain/{name}.png");
        var layer = new TerrainLayer { diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Terrain/{name}.png"), tileSize = new Vector2(tile, tile) };
        AssetDatabase.CreateAsset(layer, $"Assets/Terrain/{name}.terrainlayer");
        return layer;
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
