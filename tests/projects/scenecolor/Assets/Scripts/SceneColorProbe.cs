using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Transparent surfaces that read what is behind them as water graphs do, under the
/// Universal Render Pipeline: the camera's opaque texture (Scene Color) a little displaced, and
/// the camera's depth texture (Scene Depth), each alone and together: with the camera's opaque
/// and depth textures asked for, the depth copied after the transparent objects (the renderer's
/// default), after the opaque ones, and through Forward+ with depth priming forced (no copy: the
/// prepass makes the depth texture), the first and the last again with screen space ambient
/// occlusion (its prepass of depth and normals); and with neither texture asked for; then with a
/// terrain in place of the ground, drawn as patches and instanced, the depth copied after the
/// opaque objects and as the third scenario with screen space ambient occlusion; and drawn at a
/// render scale of 0.8, on the plane and on the instanced terrain. Each
/// scenario's image is written into SCENE_COLOR_OUT (and values.jsonl), then the run exits. See
/// README.md.</summary>
public class SceneColorProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("SceneColorProbe").AddComponent<SceneColorProbe>();

    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;

    void Value(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    static GameObject Shape(PrimitiveType type, string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        return go;
    }

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("SCENE_COLOR_OUT");
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) renderer.enabled = false;
        foreach (var other in FindObjectsByType<Light>(FindObjectsSortMode.None)) other.enabled = false;
        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        asset.renderScale = 1f;
        asset.upscalingFilter = UpscalingFilterSelection.Linear;
        var camera = Camera.main;
        var data = camera.GetUniversalAdditionalCameraData();
        data.antialiasing = AntialiasingMode.None;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.2f, 0.4f, 1f);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 100f;
        camera.fieldOfView = 60f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 3f, -6f), Quaternion.Euler(28f, 0f, 0f));
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // A lit ground of two colours, and coloured shapes standing on it, partly under the water.
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.SetColor("_BaseColor", new Color(0.7f, 0.6f, 0.4f, 1f));
        var red = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        red.SetColor("_BaseColor", Color.red);
        var green = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        green.SetColor("_BaseColor", Color.green);
        var ground = Shape(PrimitiveType.Plane, "Ground", Vector3.zero, new Vector3(2f, 1f, 2f));
        ground.GetComponent<MeshRenderer>().sharedMaterial = lit;
        var terrain = MakeTerrain();
        terrain.gameObject.SetActive(false);
        Shape(PrimitiveType.Cube, "Stripe", new Vector3(0f, 0.05f, 1f), new Vector3(9f, 0.1f, 0.4f)).GetComponent<MeshRenderer>().sharedMaterial = green;
        foreach (float x in new[] { -3f, 0f, 3f })
        {
            Shape(PrimitiveType.Sphere, "Ball", new Vector3(x - 0.5f, 0.3f, 0.2f), Vector3.one * 0.9f).GetComponent<MeshRenderer>().sharedMaterial = red;
            Shape(PrimitiveType.Cube, "Post", new Vector3(x + 0.6f, 0.6f, 1.8f), new Vector3(0.3f, 1.2f, 0.3f)).GetComponent<MeshRenderer>().sharedMaterial = green;
        }
        // Three water surfaces a little above the ground, one of each mode, left to right.
        for (int mode = 0; mode < 3; mode++)
        {
            var water = Shape(PrimitiveType.Quad, "Water" + mode, new Vector3(-3f + 3f * mode, 0.4f, 0.8f), new Vector3(2.8f, 3.2f, 1f));
            water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var material = new Material(Shader.Find("Probe/SceneColorWater"));
            material.SetFloat("_Mode", mode);
            water.GetComponent<MeshRenderer>().sharedMaterial = material;
            water.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // UniversalRenderPipelineAsset exposes its renderers' data only internally.
        var rendererData = (UniversalRendererData)((ScriptableRendererData[])typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(asset))[0];
        // The ground: 0 the plane, 1 the terrain, 2 the terrain drawn instanced; the render scale.
        foreach (var (name, textures, mode, priming, copy, ssao, floor, scale) in new[]
        {
            ("after_transparents", true, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterTransparents, false, 0, 1f),
            ("after_opaques", true, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterOpaques, false, 0, 1f),
            ("forward_plus_primed", true, RenderingMode.ForwardPlus, DepthPrimingMode.Forced, CopyDepthMode.AfterTransparents, false, 0, 1f),
            ("after_transparents_ssao", true, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterTransparents, true, 0, 1f),
            ("forward_plus_primed_ssao", true, RenderingMode.ForwardPlus, DepthPrimingMode.Forced, CopyDepthMode.AfterTransparents, true, 0, 1f),
            ("none", false, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterOpaques, false, 0, 1f),
            ("terrain_after_opaques", true, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterOpaques, false, 1, 1f),
            ("terrain_forward_plus_primed_ssao", true, RenderingMode.ForwardPlus, DepthPrimingMode.Forced, CopyDepthMode.AfterTransparents, true, 1, 1f),
            ("terrain_instanced_after_opaques", true, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterOpaques, false, 2, 1f),
            ("terrain_instanced_forward_plus_primed_ssao", true, RenderingMode.ForwardPlus, DepthPrimingMode.Forced, CopyDepthMode.AfterTransparents, true, 2, 1f),
            ("scaled_after_opaques", true, RenderingMode.Forward, DepthPrimingMode.Disabled, CopyDepthMode.AfterOpaques, false, 0, 0.8f),
            ("scaled_forward_plus_primed_ssao", true, RenderingMode.ForwardPlus, DepthPrimingMode.Forced, CopyDepthMode.AfterTransparents, true, 0, 0.8f),
            ("scaled_terrain_instanced_forward_plus_primed_ssao", true, RenderingMode.ForwardPlus, DepthPrimingMode.Forced, CopyDepthMode.AfterTransparents, true, 2, 0.8f),
        })
        {
            ground.SetActive(floor == 0);
            terrain.gameObject.SetActive(floor != 0);
            terrain.drawInstanced = floor == 2;
            asset.renderScale = scale;
            // Screen space ambient occlusion of its default source (depth and normals): its
            // prepass of depth and normals is the one a renderer with it draws.
            ScreenSpaceAmbientOcclusion occlusion = null;
            if (ssao)
            {
                occlusion = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                occlusion.name = "SSAO";
                CreateSettings(occlusion);
                rendererData.rendererFeatures.Add(occlusion);
                rendererData.SetDirty();
            }
            rendererData.renderingMode = mode;
            rendererData.depthPrimingMode = priming;
            rendererData.copyDepthMode = copy;
            data.requiresColorTexture = textures;
            data.requiresDepthTexture = textures;
            // A few frames: the pipeline's first frames after a change settle its targets.
            for (int i = 0; i < 4; i++) yield return null;
            Value(name + ".color", data.requiresColorTexture ? "true" : "false");
            Value(name + ".depth", data.requiresDepthTexture ? "true" : "false");
            Record(target, name);
            if (occlusion != null)
            {
                rendererData.rendererFeatures.Remove(occlusion);
                rendererData.SetDirty();
                DestroyImmediate(occlusion);
            }
        }
        Value("complete", "true");
        int code = 0;
        if (string.IsNullOrEmpty(m_Dir)) { Debug.LogError("SCENE_COLOR_OUT is not set"); code = 1; }
        else File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    /// <summary>A terrain of the render pipeline's terrain shader and one checkered layer in
    /// place of the plane, 12 units square, level with it but for a trough under the water
    /// (0.4 deep along z = 0.8).</summary>
    static Terrain MakeTerrain()
    {
        var checker = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                checker.SetPixel(x, y, ((x + y) & 1) == 0 ? new Color(0.7f, 0.6f, 0.4f, 1f) : new Color(0.4f, 0.3f, 0.2f, 1f));
        checker.Apply();
        var layer = new TerrainLayer { diffuseTexture = checker, tileSize = new Vector2(1f, 1f) };
        var terrainData = new TerrainData { heightmapResolution = 33 };
        terrainData.size = new Vector3(12f, 2f, 12f);
        terrainData.terrainLayers = new[] { layer };
        var heights = new float[33, 33];
        for (int z = 0; z < 33; z++)
        {
            // World z of the row: -4 + 12 z / 32; the trough's depth falls off over a unit.
            float worldZ = -4f + 12f * z / 32f;
            float trough = 0.4f * Mathf.Clamp01(1f - Mathf.Abs(worldZ - 0.8f));
            for (int x = 0; x < 33; x++) heights[z, x] = (1f - trough) / 2f;
        }
        terrainData.SetHeights(0, 0, heights);
        var terrain = new GameObject("Terrain").AddComponent<Terrain>();
        terrain.terrainData = terrainData;
        terrain.transform.position = new Vector3(-6f, -1f, -4f);
        terrain.materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
        terrain.materialTemplate.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
        return terrain;
    }

    /// <summary>Creates a feature's nested settings objects as a feature the Inspector added
    /// has them: ScriptableObject.CreateInstance leaves a serializable class field without an
    /// initializer null, in the Editor too.</summary>
    static void CreateSettings(ScriptableRendererFeature feature)
    {
        const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var settings = feature.GetType().GetField("m_Settings", Fields).GetValue(feature);
        foreach (var field in settings.GetType().GetFields(Fields))
            if (field.FieldType.IsClass && field.FieldType.IsSerializable && field.GetValue(settings) == null)
                field.SetValue(settings, Activator.CreateInstance(field.FieldType));
    }

    void Record(RenderTexture target, string name)
    {
        if (string.IsNullOrEmpty(m_Dir)) return;
        var read = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        Directory.CreateDirectory(m_Dir);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), read.EncodeToPNG());
    }
}
