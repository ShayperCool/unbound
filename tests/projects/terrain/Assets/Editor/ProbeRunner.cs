using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/terrain -executeMethod ProbeRunner.Create` makes the
/// probe's assets once (textures, terrain layers, a TerrainData asset and the scene that holds its
/// terrain); `-executeMethod ProbeRunner.Record` with <c>TERRAIN_PROBE_OUT</c> set plays the scene,
/// where <see cref="TerrainProbe"/> records and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    const int HeightmapResolution = 65;
    const int AlphamapResolution = 64;

    public static void Create()
    {
        foreach (var path in new[] { "Assets/Terrain", "Assets/Textures", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Terrain");
        Directory.CreateDirectory("Assets/Textures");

        var grass = MakeTexture("Grass", (x, y) => Color.Lerp(new Color(0.20f, 0.45f, 0.15f), new Color(0.35f, 0.60f, 0.20f), Noise(x, y)));
        var rock = MakeTexture("Rock", (x, y) => ((x / 8 + y / 8) % 2 == 0) ? new Color(0.50f, 0.48f, 0.45f) : new Color(0.32f, 0.30f, 0.28f));
        var sand = MakeTexture("Sand", (x, y) => Color.Lerp(new Color(0.85f, 0.75f, 0.50f), new Color(0.70f, 0.60f, 0.40f), Noise(y, x)));

        var layers = new[]
        {
            MakeLayer("Grass", grass, new Vector2(4f, 4f), Vector2.zero),
            MakeLayer("Rock", rock, new Vector2(2.5f, 2.5f), new Vector2(0.5f, 0.25f)),
            MakeLayer("Sand", sand, new Vector2(6f, 3f), Vector2.zero),
        };

        // The asset first: its alphamaps and holes are textures inside it.
        var data = new TerrainData();
        AssetDatabase.CreateAsset(data, "Assets/Terrain/ProbeTerrain.asset");
        data.heightmapResolution = HeightmapResolution;
        data.size = new Vector3(40f, 8f, 40f);
        var heights = new float[HeightmapResolution, HeightmapResolution];
        for (int y = 0; y < HeightmapResolution; y++)
            for (int x = 0; x < HeightmapResolution; x++)
            {
                float u = x / (float)(HeightmapResolution - 1), v = y / (float)(HeightmapResolution - 1);
                float hill = Mathf.Exp(-((u - 0.6f) * (u - 0.6f) + (v - 0.55f) * (v - 0.55f)) * 18f);
                float ridge = 0.25f * Mathf.Sin(u * 9f) * Mathf.Cos(v * 7f) + 0.25f;
                heights[y, x] = Mathf.Clamp01(0.6f * hill + 0.3f * ridge + 0.05f);
            }
        data.SetHeights(0, 0, heights);
        data.alphamapResolution = AlphamapResolution;
        data.baseMapResolution = 128;
        data.terrainLayers = layers;
        var alphas = new float[AlphamapResolution, AlphamapResolution, 3];
        for (int y = 0; y < AlphamapResolution; y++)
            for (int x = 0; x < AlphamapResolution; x++)
            {
                float u = x / (float)(AlphamapResolution - 1), v = y / (float)(AlphamapResolution - 1);
                float height = data.GetInterpolatedHeight(u, v) / data.size.y;
                float steep = Mathf.Clamp01(data.GetSteepness(u, v) / 40f);
                float sandWeight = Mathf.Clamp01((0.18f - height) * 8f);
                float rockWeight = (1f - sandWeight) * steep;
                alphas[y, x, 0] = 1f - sandWeight - rockWeight;
                alphas[y, x, 1] = rockWeight;
                alphas[y, x, 2] = sandWeight;
            }
        data.SetAlphamaps(0, 0, alphas);
        // A square hole near a corner.
        var holes = new bool[HeightmapResolution - 1, HeightmapResolution - 1];
        for (int y = 0; y < HeightmapResolution - 1; y++)
            for (int x = 0; x < HeightmapResolution - 1; x++)
                holes[y, x] = !(x >= 6 && x < 12 && y >= 50 && y < 56);
        data.SetHoles(0, 0, holes);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var terrain = Terrain.CreateTerrainGameObject(data);
        terrain.name = "Terrain";
        terrain.transform.position = new Vector3(-20f, 0f, -20f);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
        sun.color = Color.white;
        sun.intensity = 1f;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    static float Noise(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    static Texture2D MakeTexture(string name, System.Func<int, int, Color> color)
    {
        var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                texture.SetPixel(x, y, color(x, y));
        texture.Apply();
        string path = $"Assets/Textures/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static TerrainLayer MakeLayer(string name, Texture2D diffuse, Vector2 tileSize, Vector2 tileOffset)
    {
        var layer = new TerrainLayer { diffuseTexture = diffuse, tileSize = tileSize, tileOffset = tileOffset };
        AssetDatabase.CreateAsset(layer, $"Assets/Terrain/{name}.terrainlayer");
        return layer;
    }
}
