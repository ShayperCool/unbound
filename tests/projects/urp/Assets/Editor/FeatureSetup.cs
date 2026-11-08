using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp -executeMethod FeatureSetup.Create`:
/// authors what the renderer feature scenarios load (<c>Assets/Resources/FeatureAssets.asset</c>):
/// a decal base map (colored rings, alpha falling off to the edge) and normal map (ridges),
/// materials of URP's own Decal Shader Graph and of a copy of it with angle fade enabled, and two
/// Single Channel textures (Alpha and Red).</summary>
public static class FeatureSetup
{
    const string Dir = "Assets/Features";
    const string DecalGraph = "Packages/com.unity.render-pipelines.universal/Shaders/Decal.shadergraph";

    public static void Create()
    {
        Directory.CreateDirectory(Dir);
        var baseMap = LightingSetup.WriteTexture(Dir, "DecalBase", 64, 64, (x, y) =>
        {
            float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
            var c = r < 10f ? new Color(1f, 0.85f, 0.1f) : ((int)(r / 5f)) % 2 == 0 ? new Color(0.1f, 0.3f, 0.9f) : new Color(0.95f, 0.95f, 0.95f);
            if (x < 32 && y < 32) c *= 0.5f;
            return new Color(c.r, c.g, c.b, Mathf.Clamp01((30f - r) / 8f));
        }, importer =>
        {
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
        });
        var normalMap = LightingSetup.WriteTexture(Dir, "DecalNormal", 64, 64, (x, y) =>
        {
            // Concentric ridges: the tangent-space normal tilts along the radius.
            float dx = x - 31.5f, dy = y - 31.5f, r = Mathf.Max(Mathf.Sqrt(dx * dx + dy * dy), 1e-3f);
            float slope = 0.8f * Mathf.Sin(r * 0.6f);
            var n = new Vector3(-slope * dx / r, -slope * dy / r, 1f).normalized;
            return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }, importer =>
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Clamp;
        });
        System.Func<int, int, Color> ramp = (x, y) => new Color(x / 31f, y / 31f, 0.5f, ((x + y) % 7) / 6f);
        var singleAlpha = LightingSetup.WriteTexture(Dir, "SingleAlpha", 32, 32, ramp, importer =>
        {
            importer.textureType = TextureImporterType.SingleChannel;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            SetSingleChannel(importer, 0);
        });
        var singleRed = LightingSetup.WriteTexture(Dir, "SingleRed", 32, 32, ramp, importer =>
        {
            importer.textureType = TextureImporterType.SingleChannel;
            SetSingleChannel(importer, 1);
        });

        // URP's Decal Shader Graph as shipped, and the same graph with angle fade enabled.
        var decal = DecalMaterial(AssetDatabase.LoadAssetAtPath<Shader>(DecalGraph), "Decal", baseMap, normalMap);
        string graph = File.ReadAllText(DecalGraph);
        if (!graph.Contains("\"angleFade\": false")) throw new System.InvalidOperationException("Decal.shadergraph has no angleFade option");
        File.WriteAllText($"{Dir}/DecalAngleFade.shadergraph", graph.Replace("\"angleFade\": false", "\"angleFade\": true"));
        AssetDatabase.ImportAsset($"{Dir}/DecalAngleFade.shadergraph");
        var decalAngleFade = DecalMaterial(AssetDatabase.LoadAssetAtPath<Shader>($"{Dir}/DecalAngleFade.shadergraph"), "DecalAngleFade", baseMap, normalMap);

        var holder = ScriptableObject.CreateInstance<FeatureAssets>();
        holder.decal = decal;
        holder.decalAngleFade = decalAngleFade;
        holder.singleAlpha = singleAlpha;
        holder.singleRed = singleRed;
        Directory.CreateDirectory("Assets/Resources");
        AssetDatabase.CreateAsset(holder, "Assets/Resources/FeatureAssets.asset");
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    /// <summary>TextureImporterSettings.singleChannelComponent: 0 Alpha, 1 Red.</summary>
    internal static void SetSingleChannel(TextureImporter importer, int component)
    {
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.singleChannelComponent = (TextureImporterSingleChannelComponent)component;
        importer.SetTextureSettings(settings);
    }

    static Material DecalMaterial(Shader shader, string name, Texture baseMap, Texture normalMap)
    {
        if (shader == null) throw new System.InvalidOperationException($"no shader for {name}");
        var m = new Material(shader) { enableInstancing = true };
        m.SetTexture("Base_Map", baseMap);
        m.SetTexture("Normal_Map", normalMap);
        AssetDatabase.CreateAsset(m, $"{Dir}/{name}.mat");
        return m;
    }
}
