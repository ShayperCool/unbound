using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>`Unity -batchmode -quit -projectPath tests/projects/urp2d -executeMethod ProbeSetup.Create`:
/// makes a URP asset with the 2D renderer and the probe scene: an orthographic camera, a lit
/// backdrop and ground, two boxes casting 2D shadows, lit and unlit circles, and the 2D lights the
/// scenarios switch (global, point, spot, freeform, sprite), all but the global one disabled.</summary>
public static class ProbeSetup
{
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Settings");
        Directory.CreateDirectory("Assets/Art");
        Directory.CreateDirectory("Assets/Scenes");
        var rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/Probe2DRenderer.asset");
        var asset = UniversalRenderPipelineAsset.Create(rendererData);
        asset.msaaSampleCount = 1;
        asset.renderScale = 1f;
        AssetDatabase.CreateAsset(asset, "Assets/Settings/Probe2DURP.asset");
        GraphicsSettings.defaultRenderPipeline = asset;
        QualitySettings.renderPipeline = asset;

        var square = Sprite("Assets/Art/Square.png", 16, (x, y) => Color.white);
        var circle = Sprite("Assets/Art/Circle.png", 32, (x, y) =>
        {
            float dx = x - 15.5f, dy = y - 15.5f;
            return dx * dx + dy * dy <= 16f * 16f ? Color.white : Color.clear;
        });

        var lit = Material("Assets/Settings/SpriteLit.mat", "Universal Render Pipeline/2D/Sprite-Lit-Default");
        var unlit = Material("Assets/Settings/SpriteUnlit.mat", "Universal Render Pipeline/2D/Sprite-Unlit-Default");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        var cam = camera.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 3f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);

        Renderer("Backdrop", square, lit, new Vector3(0f, 0.5f, 0f), new Vector3(10f, 5f, 1f), new Color(0.55f, 0.55f, 0.6f), -10);
        Renderer("Ground", square, lit, new Vector3(0f, -2.4f, 0f), new Vector3(11f, 1.2f, 1f), new Color(0.45f, 0.35f, 0.25f), 0);
        var boxA = Renderer("Box A", square, lit, new Vector3(-1.6f, -1.3f, 0f), new Vector3(1f, 1f, 1f), new Color(0.2f, 0.4f, 0.9f), 1);
        var boxB = Renderer("Box B", square, lit, new Vector3(1.2f, -1.05f, 0f), new Vector3(0.6f, 1.5f, 1f), new Color(0.9f, 0.55f, 0.2f), 1);
        Renderer("Lit Circle", circle, lit, new Vector3(-3.2f, 1.2f, 0f), Vector3.one, new Color(0.3f, 0.85f, 0.35f), 2);
        Renderer("Unlit Circle", circle, unlit, new Vector3(3.2f, 1.2f, 0f), Vector3.one, new Color(0.85f, 0.3f, 0.8f), 2);
        foreach (var box in new[] { boxA, boxB })
        {
            var caster = box.AddComponent<ShadowCaster2D>();
            caster.selfShadows = false;
        }

        var global = Light("Global Light", Light2D.LightType.Global, Vector3.zero, Color.white, 0.25f);
        global.enabled = true;
        var point = Light("Point Light", Light2D.LightType.Point, new Vector3(-0.3f, 0.4f, 0f), new Color(1f, 0.6f, 0.3f), 1.3f);
        point.pointLightInnerRadius = 0.4f;
        point.pointLightOuterRadius = 3.5f;
        point.falloffIntensity = 0.5f;
        var spot = Light("Spot Light", Light2D.LightType.Point, new Vector3(2.2f, 2.2f, 0f), new Color(0.4f, 0.7f, 1f), 1.5f);
        spot.transform.rotation = Quaternion.Euler(0f, 0f, 150f);
        spot.pointLightInnerRadius = 0.5f;
        spot.pointLightOuterRadius = 5f;
        spot.pointLightInnerAngle = 25f;
        spot.pointLightOuterAngle = 60f;
        var freeform = Light("Freeform Light", Light2D.LightType.Freeform, new Vector3(-2f, 0.5f, 0f), new Color(0.5f, 1f, 0.6f), 1f);
        freeform.SetShapePath(new[] { new Vector3(-1f, -1f), new Vector3(1.2f, -0.8f), new Vector3(0.8f, 1.1f), new Vector3(-0.9f, 0.9f) });
        freeform.shapeLightFalloffSize = 0.6f;
        var spriteLight = Light("Sprite Light", Light2D.LightType.Sprite, new Vector3(1.5f, 0.5f, 0f), new Color(1f, 0.3f, 0.5f), 1.2f);
        spriteLight.lightCookieSprite = circle;
        spriteLight.transform.localScale = new Vector3(3f, 3f, 1f);

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    /// <summary>`-executeMethod ProbeSetup.AddSecondary`: a sprite with a normal map and a mask as
    /// secondary textures (`Assets/Resources/Bumpy*.png`), and the first light blend style reading
    /// the mask's red channel (sprites without a mask read white: unchanged).</summary>
    public static void AddSecondary()
    {
        Directory.CreateDirectory("Assets/Resources");
        // A dome: normals leaning out from the centre.
        var normalPath = "Assets/Resources/Bumpy_n.png";
        var t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                var n = new Vector3((x - 15.5f) / 16f, (y - 15.5f) / 16f, 0f);
                n.z = Mathf.Sqrt(Mathf.Max(0.05f, 1f - n.x * n.x - n.y * n.y));
                n.Normalize();
                t.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
            }
        File.WriteAllBytes(normalPath, t.EncodeToPNG());
        AssetDatabase.ImportAsset(normalPath);
        var ni = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        ni.textureType = TextureImporterType.NormalMap;
        ni.mipmapEnabled = false;
        ni.filterMode = FilterMode.Point;
        ni.textureCompression = TextureImporterCompression.Uncompressed;
        ni.SaveAndReimport();
        var maskPath = "Assets/Resources/Bumpy_m.png";
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                t.SetPixel(x, y, new Color(x < 16 ? 1f : 0.3f, 1f, 1f, 1f));
        File.WriteAllBytes(maskPath, t.EncodeToPNG());
        AssetDatabase.ImportAsset(maskPath);
        var mi = (TextureImporter)AssetImporter.GetAtPath(maskPath);
        mi.mipmapEnabled = false;
        mi.filterMode = FilterMode.Point;
        mi.textureCompression = TextureImporterCompression.Uncompressed;
        mi.SaveAndReimport();
        Sprite("Assets/Resources/Bumpy.png", 32, (x, y) => new Color(0.9f, 0.9f, 0.9f, 1f));
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Bumpy.png");
        importer.secondarySpriteTextures = new[]
        {
            new SecondarySpriteTexture { name = "_NormalMap", texture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) },
            new SecondarySpriteTexture { name = "_MaskTex", texture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath) },
        };
        importer.SaveAndReimport();
        var data = AssetDatabase.LoadAssetAtPath<Renderer2DData>("Assets/Settings/Probe2DRenderer.asset");
        var so = new SerializedObject(data);
        so.FindProperty("m_LightBlendStyles").GetArrayElementAtIndex(0).FindPropertyRelative("maskTextureChannel").intValue = 1;
        so.ApplyModifiedPropertiesWithoutUndo();
        // A point light that uses normal maps (disabled like the others).
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        var light = Light("Normal Light", Light2D.LightType.Point, new Vector3(-1.2f, 0.6f, 0f), new Color(1f, 0.85f, 0.6f), 1.4f);
        light.pointLightInnerRadius = 0.3f;
        light.pointLightOuterRadius = 4f;
        var lso = new SerializedObject(light);
        lso.FindProperty("m_NormalMapQuality").intValue = (int)Light2D.NormalMapQuality.Accurate;
        lso.FindProperty("m_NormalMapDistance").floatValue = 1.5f;
        lso.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static Sprite Sprite(string path, int size, System.Func<int, int, Color> pixel)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, pixel(x, y));
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = size;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Material Material(string path, string shader)
    {
        var material = new Material(Shader.Find(shader));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject Renderer(string name, Sprite sprite, Material material, Vector3 position, Vector3 scale, Color color, int order)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.position = position;
        go.transform.localScale = scale;
        var r = go.GetComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.sharedMaterial = material;
        r.color = color;
        r.sortingOrder = order;
        return go;
    }

    /// <summary>A 2D light lighting every sorting layer, disabled.</summary>
    static Light2D Light(string name, Light2D.LightType type, Vector3 position, Color color, float intensity)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var light = go.AddComponent<Light2D>();
        light.lightType = type;
        light.color = color;
        light.intensity = intensity;
        var so = new SerializedObject(light);
        var layers = so.FindProperty("m_ApplyToSortingLayers");
        var ids = SortingLayer.layers.Select(l => l.id).ToArray();
        layers.arraySize = ids.Length;
        for (int i = 0; i < ids.Length; i++) layers.GetArrayElementAtIndex(i).intValue = ids[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        light.enabled = false;
        return light;
    }
}
