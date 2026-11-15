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

    /// <summary>
    /// `-executeMethod ProbeRunner.CreateMasks` adds, beside the assets above (which stay as they
    /// are), the <c>Masks</c> scene: the probe scene's lighting and light with two flat 100 × 100
    /// terrains instead of its terrain and sphere. <c>Masked</c> has five layers (two splat passes):
    /// mask maps with remapped ranges on the first, third, fourth and fifth, the second without one
    /// (its smoothness from its diffuse alpha), a normal map on the third, metallic from the third's
    /// mask, opacity as density on the fourth. <c>Density</c> has four of these layers (one pass),
    /// the density layer among them.
    /// </summary>
    public static void CreateMasks()
    {
        foreach (var path in new[] { "Assets/Masks", "Assets/Scenes/Masks.unity" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Masks");
        var colors = new[] { new Color(0.6f, 0.55f, 0.45f), new Color(0.3f, 0.6f, 0.25f), new Color(0.55f, 0.55f, 0.6f), new Color(0.7f, 0.5f, 0.3f), new Color(0.8f, 0.8f, 0.75f) };
        var layers = new TerrainLayer[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            int index = i;
            // Alpha 1 and 0.2 in a checker: the smoothness of a layer without a mask, the density
            // of the fourth.
            var diffuse = Texture($"Assets/Masks/Diffuse{i}.png", 16, false, (x, y) =>
            {
                bool even = ((x / 2 + y / 2) & 1) == 0;
                var c = even ? colors[index] : colors[index] * 0.85f;
                c.a = even ? 1f : 0.2f;
                return c;
            });
            var layer = new TerrainLayer { diffuseTexture = diffuse, tileSize = new Vector2(10f, 10f), smoothness = 0.5f };
            if (i != 1)
            {
                // R metallic (1 on the third layer's squares), G occlusion, B height, A smoothness,
                // in a finer checker than the diffuse map.
                layer.maskMapTexture = Texture($"Assets/Masks/Mask{i}.png", 16, false, (x, y) =>
                {
                    bool even = ((x + y) & 1) == 0;
                    return new Color(index == 2 && even ? 1f : 0f, even ? 1f : 0.25f, even ? 0.8f : 0.2f, even ? 0.9f : 0.3f);
                }, linear: true);
                layer.maskMapRemapMin = new Vector4(0f, i == 4 ? 0f : 0.3f, 0f, 0f);
                layer.maskMapRemapMax = new Vector4(1f, i == 3 ? 1.1f : 1f, 1f, i == 0 ? 0.3f : 1f);
            }
            if (i == 2)
            {
                layer.normalMapTexture = Texture("Assets/Masks/Normal2.png", 16, true, (x, y) =>
                {
                    float nx = 0.45f * Mathf.Sin(x * Mathf.PI / 4f), ny = 0.45f * Mathf.Cos(y * Mathf.PI / 4f);
                    var n = new Vector3(nx, ny, 1f).normalized;
                    return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }, normalMap: true);
                layer.normalScale = 0.8f;
            }
            if (i == 3)
            {
                // Opacity as density: the diffuse alpha weighs the layer against its neighbours.
                layer.diffuseRemapMin = new Vector4(0f, 0f, 0f, 1f);
                layer.diffuseRemapMax = new Vector4(0.9f, 1f, 0.8f, 1f);
            }
            AssetDatabase.CreateAsset(layer, $"Assets/Masks/Layer{i}.terrainlayer");
            layers[i] = layer;
        }

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity", OpenSceneMode.Single);
        foreach (var name in new[] { "Flat", "Metal" })
            Object.DestroyImmediate(GameObject.Find(name));
        MaskedTerrain("Masked", layers, new Vector3(-105f, 0f, 0f));
        MaskedTerrain("Density", new[] { layers[0], layers[1], layers[2], layers[3] }, new Vector3(5f, 0f, 0f));
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Masks.unity");
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == "Assets/Scenes/Masks.unity");
        scenes.Add(new EditorBuildSettingsScene("Assets/Scenes/Masks.unity", true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    /// <summary>A flat terrain of the given layers in stripes along x, every other band of rows
    /// blending each stripe into the next.</summary>
    static void MaskedTerrain(string name, TerrainLayer[] layers, Vector3 position)
    {
        var data = new TerrainData();
        AssetDatabase.CreateAsset(data, $"Assets/Masks/{name}.asset");
        data.heightmapResolution = 33;
        data.alphamapResolution = 64;
        data.baseMapResolution = 128;
        data.size = new Vector3(100f, 10f, 100f);
        data.terrainLayers = layers;
        int count = layers.Length;
        var weights = new float[64, 64, count];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float t = x / 64f * count;
                int a = Mathf.Min((int)t, count - 1), b = (a + 1) % count;
                float f = (y & 16) == 0 ? 0f : Mathf.Min(1f, (t - a) * 1.5f);
                weights[y, x, a] = 1f - f;
                weights[y, x, b] += f;
            }
        data.SetAlphamaps(0, 0, weights);
        var terrainObject = Terrain.CreateTerrainGameObject(data);
        terrainObject.name = name;
        terrainObject.transform.position = position;
        var terrain = terrainObject.GetComponent<Terrain>();
        terrain.materialTemplate = GraphicsSettings.currentRenderPipeline.defaultTerrainMaterial;
        terrain.drawInstanced = false;
    }

    static Texture2D Texture(string path, int size, bool opaque, System.Func<int, int, Color> color, bool linear = false, bool normalMap = false)
    {
        var texture = new Texture2D(size, size, opaque ? TextureFormat.RGB24 : TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, color(x, y));
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        if (linear || normalMap)
        {
            // Uncompressed: the layers' values as authored.
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (normalMap) importer.textureType = TextureImporterType.NormalMap;
            else importer.sRGBTexture = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
