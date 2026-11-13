using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// `Unity -batchmode -quit -projectPath tests/projects/urpterrain -executeMethod ProbeRunner.Create`
/// makes the probe's assets once: five terrain layers (plain colors in two shades; metallic 0, 0.5
/// and 1; smoothness 0 to 0.8; one layer's diffuse map without alpha, one with a constant
/// smoothness), a flat 200 × 200 terrain painted in stripes that blend neighbouring layers, drawn
/// with the Universal Render Pipeline's default terrain material, a smooth metallic sphere with the
/// pipeline's Lit shader beside it, a directional light, and the scene. `-executeMethod
/// ProbeRunner.Record` with <c>URP_TERRAIN_PROBE_OUT</c> set plays the scene, where
/// <see cref="UrpTerrainProbe"/> records.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Terrain", "Assets/Scenes" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Terrain");
        Directory.CreateDirectory("Assets/Scenes");
        var colors = new[] { new Color(0.8f, 0.2f, 0.2f), new Color(0.2f, 0.7f, 0.2f), new Color(0.2f, 0.3f, 0.8f), new Color(0.9f, 0.8f, 0.2f), new Color(0.9f, 0.9f, 0.9f) };
        var metallic = new[] { 0f, 0.5f, 1f, 0f, 0.5f };
        var layers = new TerrainLayer[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            int index = i;
            // The second layer's map has no alpha (an RGB24 PNG); the others carry alpha 1 and 0.8.
            var texture = Texture($"Assets/Terrain/Layer{i}.png", 8, index == 1, (x, y) =>
            {
                var c = ((x + y) & 1) == 0 ? colors[index] : colors[index] * 0.8f;
                c.a = ((x + y) & 1) == 0 ? 1f : 0.8f;
                return c;
            });
            layers[i] = new TerrainLayer
            {
                diffuseTexture = texture,
                tileSize = new Vector2(8f, 8f),
                metallic = metallic[i],
                smoothness = 0.2f * i,
                smoothnessSource = i == 3 ? TerrainLayerSmoothnessSource.Constant : TerrainLayerSmoothnessSource.DiffuseAlphaChannel,
            };
            AssetDatabase.CreateAsset(layers[i], $"Assets/Terrain/Layer{i}.terrainlayer");
        }
        var data = new TerrainData();
        AssetDatabase.CreateAsset(data, "Assets/Terrain/Flat.asset");
        data.heightmapResolution = 33;
        data.alphamapResolution = 64;
        data.baseMapResolution = 128;
        data.size = new Vector3(200f, 10f, 200f);
        data.terrainLayers = layers;
        var weights = new float[64, 64, colors.Length];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float t = x / 64f * colors.Length;
                int a = Mathf.Min((int)t, colors.Length - 1), b = (a + 1) % colors.Length;
                float f = (y & 16) == 0 ? 0f : t - a;
                weights[y, x, a] = 1f - f;
                weights[y, x, b] += f;
            }
        data.SetAlphamaps(0, 0, weights);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = "Flat";
        terrainObject.transform.position = new Vector3(-100f, 0f, 0f);
        var terrain = terrainObject.GetComponent<Terrain>();
        terrain.materialTemplate = GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
        terrain.drawInstanced = false;
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Metal";
        sphere.transform.position = new Vector3(0f, 2f, 30f);
        sphere.transform.localScale = Vector3.one * 4f;
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.9f));
        lit.SetFloat("_Metallic", 1f);
        lit.SetFloat("_Smoothness", 0.9f);
        AssetDatabase.CreateAsset(lit, "Assets/Terrain/Metal.mat");
        sphere.GetComponent<Renderer>().sharedMaterial = lit;
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(35f, 60f, 0f);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static Texture2D Texture(string path, int size, bool opaque, System.Func<int, int, Color> color)
    {
        var texture = new Texture2D(size, size, opaque ? TextureFormat.RGB24 : TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, color(x, y));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
