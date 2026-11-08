using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>The renderer feature scenarios: URP's Screen Space Ambient Occlusion and Decal
/// features, each scenario one setting, the feature created for the scenario and removed after it
/// (so its per-frame state, such as SSAO's blue noise index, starts the same way); and the
/// texture formats (texture-formats.jsonl) of the cookie, Single Channel and lightmap textures.</summary>
public partial class UrpProbe
{
    static readonly string[] s_FeatureKeywords = { "_SCREEN_SPACE_OCCLUSION", "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3",
        "_DECAL_NORMAL_BLEND_LOW", "_DECAL_NORMAL_BLEND_MEDIUM", "_DECAL_NORMAL_BLEND_HIGH", "_DECAL_LAYERS", "_LIGHT_LAYERS" };

    void FeatureScenarios(string dir, UniversalRendererData rendererData)
    {
        var assets = Resources.Load<FeatureAssets>("FeatureAssets");
        // Closer to the cube than the other scenarios' camera.
        var camera = Camera.main.transform;
        var (cameraPosition, cameraRotation) = (camera.position, camera.rotation);
        camera.SetPositionAndRotation(new Vector3(-0.5f, 1.4f, -2.6f), Quaternion.Euler(20f, 0f, 0f));
        // A lit wall behind the objects (creases with the ground and the cube) and a bright
        // ambient probe: the occlusion darkens mostly ambient light.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(wall.GetComponent<Collider>());
        wall.name = "Wall";
        wall.transform.position = new Vector3(-0.3f, 0.75f, 0.9f);
        wall.transform.localScale = new Vector3(4f, 1.5f, 0.3f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = GameObject.Find("Plane").GetComponent<MeshRenderer>().sharedMaterial;
        var ambientProbe = RenderSettings.ambientProbe;
        var bright = new SphericalHarmonicsL2();
        bright.AddAmbientLight(new Color(0.8f, 0.8f, 0.8f));
        RenderSettings.ambientProbe = bright;
        Scenario(dir, "ssao_off", () => { }, () => { });
        SsaoScenario(dir, rendererData, "ssao", null);
        SsaoScenario(dir, rendererData, "ssao_depth", s => Set(s, "Source", "Depth"));
        SsaoScenario(dir, rendererData, "ssao_depth_normals_high", s => { Set(s, "Source", "Depth"); Set(s, "NormalSamples", "High"); });
        SsaoScenario(dir, rendererData, "ssao_interleaved", s => Set(s, "AOMethod", "InterleavedGradient"));
        SsaoScenario(dir, rendererData, "ssao_samples_high", s => Set(s, "Samples", "High"));
        SsaoScenario(dir, rendererData, "ssao_blur_medium", s => Set(s, "BlurQuality", "Medium"));
        SsaoScenario(dir, rendererData, "ssao_blur_low", s => Set(s, "BlurQuality", "Low"));
        SsaoScenario(dir, rendererData, "ssao_after_opaque", s => Set(s, "AfterOpaque", true));
        SsaoScenario(dir, rendererData, "ssao_downsample", s => Set(s, "Downsample", true));
        SsaoScenario(dir, rendererData, "ssao_params", s =>
        {
            Set(s, "Intensity", 1.5f);
            Set(s, "Radius", 0.12f);
            Set(s, "Falloff", 8f);
            Set(s, "DirectLightingStrength", 0.8f);
        });
        rendererData.renderingMode = RenderingMode.ForwardPlus;
        SsaoScenario(dir, rendererData, "ssao_forward_plus", null);
        rendererData.renderingMode = RenderingMode.Forward;
        RenderSettings.ambientProbe = ambientProbe;
        DestroyImmediate(wall);

        // A projector above the ground in front of the cube, projecting down onto both,
        // configured before it is enabled (as a serialized projector is: the renderingLayerMask
        // setter does not update an enabled projector's cached data).
        Action<Material, Action<DecalProjector>> place = (material, configure) =>
        {
            var projector = new GameObject("Decal Projector");
            projector.SetActive(false);
            projector.transform.SetPositionAndRotation(new Vector3(-0.6f, 1.3f, -0.6f), Quaternion.Euler(90f, 0f, 0f));
            var p = projector.AddComponent<DecalProjector>();
            p.material = material;
            p.size = new Vector3(1.8f, 1.8f, 1.6f);
            p.pivot = new Vector3(0f, 0f, 0.8f);
            configure?.Invoke(p);
            projector.SetActive(true);
        };
        DecalScenario(dir, rendererData, "decal_screen_space", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decal, null));
        DecalScenario(dir, rendererData, "decal_normal_blend_medium", s => { Set(s, "technique", "ScreenSpace"); Set(s, "screenSpaceSettings.normalBlend", "Medium"); }, () => place(assets.decal, null));
        DecalScenario(dir, rendererData, "decal_normal_blend_high", s => { Set(s, "technique", "ScreenSpace"); Set(s, "screenSpaceSettings.normalBlend", "High"); }, () => place(assets.decal, null));
        DecalScenario(dir, rendererData, "decal_automatic", null, () => place(assets.decal, null));
        // DBuffer on an OpenGL device: the feature draws nothing.
        DecalScenario(dir, rendererData, "decal_dbuffer", s => Set(s, "technique", "DBuffer"), () => place(assets.decal, null));
        DecalScenario(dir, rendererData, "decal_size_pivot", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decal, p =>
        {
            p.size = new Vector3(2.6f, 1.2f, 1.6f);
            p.pivot = new Vector3(0.4f, -0.3f, 0.8f);
        }));
        DecalScenario(dir, rendererData, "decal_uv", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decal, p =>
        {
            p.uvScale = new Vector2(2f, 1.5f);
            p.uvBias = new Vector2(0.25f, -0.1f);
        }));
        DecalScenario(dir, rendererData, "decal_fade_factor", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decal, p => p.fadeFactor = 0.4f));
        DecalScenario(dir, rendererData, "decal_draw_distance", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decal, p =>
        {
            p.drawDistance = 3.5f;
            p.fadeScale = 0.6f;
        }));
        DecalScenario(dir, rendererData, "decal_angle_fade", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decalAngleFade, p =>
        {
            p.startAngleFade = 20f;
            p.endAngleFade = 70f;
        }));
        Renderer cube = GameObject.Find("Cube").GetComponent<MeshRenderer>();
        uint cubeMask = cube.renderingLayerMask;
        DecalScenario(dir, rendererData, "decal_layers", s => { Set(s, "technique", "ScreenSpace"); Set(s, "decalLayers", true); }, () =>
        {
            // Only the cube shares the projector's layer.
            cube.renderingLayerMask = 1u | 4u;
            place(assets.decal, p => p.renderingLayerMask = 4u);
        }, () => cube.renderingLayerMask = cubeMask);
        rendererData.renderingMode = RenderingMode.ForwardPlus;
        DecalScenario(dir, rendererData, "decal_forward_plus", s => Set(s, "technique", "ScreenSpace"), () => place(assets.decal, null));
        rendererData.renderingMode = RenderingMode.Forward;
        camera.SetPositionAndRotation(cameraPosition, cameraRotation);
    }

    /// <summary>One scenario with a new Screen Space Ambient Occlusion feature, its settings
    /// changed by <paramref name="configure"/>.</summary>
    void SsaoScenario(string dir, UniversalRendererData rendererData, string name, Action<object> configure)
    {
        s_ExtraKeywords = s_FeatureKeywords;
        var feature = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
        feature.name = "SSAO";
        var settings = Settings(feature);
        configure?.Invoke(settings);
        Scenario(dir, name, () => AddFeature(rendererData, feature), () => RemoveFeature(rendererData, feature));
    }

    /// <summary>One scenario with a new Decal feature and the projector <paramref name="place"/>
    /// makes (destroyed after it).</summary>
    void DecalScenario(string dir, UniversalRendererData rendererData, string name, Action<object> configure, Action place, Action restore = null)
    {
        s_ExtraKeywords = s_FeatureKeywords;
        var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
        feature.name = "Decal";
        var settings = Settings(feature);
        configure?.Invoke(settings);
        Scenario(dir, name, () => { AddFeature(rendererData, feature); place(); }, () =>
        {
            RemoveFeature(rendererData, feature);
            var projector = FindObjectsByType<DecalProjector>(FindObjectsSortMode.None);
            foreach (var p in projector) DestroyImmediate(p.gameObject);
            restore?.Invoke();
        });
    }

    static void AddFeature(UniversalRendererData rendererData, ScriptableRendererFeature feature)
    {
        rendererData.rendererFeatures.Add(feature);
        rendererData.SetDirty();
    }

    static void RemoveFeature(UniversalRendererData rendererData, ScriptableRendererFeature feature)
    {
        rendererData.rendererFeatures.Remove(feature);
        rendererData.SetDirty();
        DestroyImmediate(feature);
    }

    /// <summary>The feature's serialized settings object (internal to URP), its nested settings
    /// objects created as a feature the Inspector added has them: ScriptableObject.CreateInstance
    /// leaves a serializable class field without an initializer null, in the Editor too.</summary>
    static object Settings(ScriptableRendererFeature feature)
    {
        var settings = feature.GetType().GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(feature);
        foreach (var field in settings.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (field.FieldType.IsClass && field.FieldType.IsSerializable && field.GetValue(settings) == null)
                field.SetValue(settings, Activator.CreateInstance(field.FieldType));
        return settings;
    }

    /// <summary>Sets a settings field by its (dotted) name; enum values by their name.</summary>
    static void Set(object settings, string path, object value)
    {
        var parts = path.Split('.');
        object target = settings;
        for (int i = 0; i < parts.Length - 1; i++)
            target = Field(target, parts[i]).GetValue(target);
        var field = Field(target, parts[^1]);
        field.SetValue(target, value is string s && field.FieldType.IsEnum ? Enum.Parse(field.FieldType, s) : value);
    }

    static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(target.GetType().Name, name);

    /// <summary>The format of the cookie and Single Channel textures.</summary>
    static void RecordTextureFormats(string path)
    {
        var lighting = Resources.Load<LightingTextures>("LightingTextures");
        var features = Resources.Load<FeatureAssets>("FeatureAssets");
        var lines = new StringBuilder();
        foreach (var texture in new[] { lighting.cookieGray, lighting.cookieOpaque, lighting.cookieColor, lighting.cookieCube, features.singleAlpha, features.singleRed })
        {
            string format = texture is Texture2D t2 ? t2.format.ToString() : texture is Cubemap c ? c.format.ToString() : "-";
            lines.Append("{\"key\":\"").Append(texture.name).Append(" format\",\"value\":\"").Append(format).Append("\"}\n");
            lines.Append("{\"key\":\"").Append(texture.name).Append(" graphicsFormat\",\"value\":\"").Append(texture.graphicsFormat).Append("\"}\n");
        }
        File.AppendAllText(path, lines.ToString());
    }

    /// <summary>The loaded scene's lightmaps: format and, read back raw through a float target,
    /// each channel's largest stored value.</summary>
    static void RecordLightmaps(string path, string setting)
    {
        var lines = new StringBuilder();
        var maps = LightmapSettings.lightmaps;
        for (int i = 0; i < maps.Length; i++)
        {
            foreach (var (kind, texture) in new[] { ("color", maps[i].lightmapColor), ("dir", maps[i].lightmapDir), ("shadowmask", maps[i].shadowMask) })
            {
                if (texture == null) continue;
                string key = $"{setting} lightmap {i} {kind}";
                lines.Append("{\"key\":\"").Append(key).Append(" format\",\"value\":\"").Append(texture.format).Append("\"}\n");
                var target = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat);
                Graphics.Blit(texture, target);
                var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readback.Apply();
                RenderTexture.active = null;
                var max = new float[4];
                foreach (var p in readback.GetPixels())
                {
                    max[0] = Mathf.Max(max[0], p.r);
                    max[1] = Mathf.Max(max[1], p.g);
                    max[2] = Mathf.Max(max[2], p.b);
                    max[3] = Mathf.Max(max[3], p.a);
                }
                lines.Append("{\"key\":\"").Append(key).Append(" max\",\"value\":[")
                    .Append(string.Join(",", max.Select(f => f.ToString("R", CultureInfo.InvariantCulture)))).Append("]}\n");
                target.Release();
                DestroyImmediate(readback);
            }
        }
        File.AppendAllText(path, lines.ToString());
    }
}
