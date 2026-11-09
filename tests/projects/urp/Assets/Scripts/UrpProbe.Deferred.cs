using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>The rendering path scenarios (<c>URP_PROBE_DEFERRED_DIR</c>, a run of its own): each
/// scene recorded with the Universal Renderer's Forward and Deferred modes,
/// <c>&lt;name&gt;_forward</c> and <c>&lt;name&gt;_deferred</c>, and <c>modes.jsonl</c>, what each
/// recording rendered with: the device, <c>renderingModeActual</c> and the path's global keywords.
/// URP's Deferred path is unsupported on OpenGL devices (it renders Forward), so the Deferred
/// recordings are measured on a Vulkan device; the Forward recordings on both devices separate
/// device differences from rendering path differences.</summary>
public partial class UrpProbe
{
    static readonly string[] s_PathKeywords = { "_GBUFFER_NORMALS_OCT", "_DEFERRED_MIXED_LIGHTING", "_RENDER_PASS_ENABLED", "_LIGHT_LAYERS",
        "_WRITE_RENDERING_LAYERS", "_SCREEN_SPACE_OCCLUSION", "_DBUFFER_MRT1", "_DBUFFER_MRT2", "_DBUFFER_MRT3", "_DECAL_LAYERS",
        "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHT_SHADOWS", "_SHADOWS_SOFT",
        "LIGHTMAP_SHADOW_MIXING", "SHADOWS_SHADOWMASK", "_MIXED_LIGHTING_SUBTRACTIVE" };

    /// <summary>Called by <see cref="Scenario"/> after the recording, before the teardown.</summary>
    static Action<string> s_AfterRecord;

    IEnumerator DeferredScenarios(string dir)
    {
        DontDestroyOnLoad(gameObject);
        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var rendererData = (UniversalRendererData)((ScriptableRendererData[])typeof(UniversalRenderPipelineAsset)
            .GetField("m_RendererDataList", flags).GetValue(asset))[0];
        var additionalShadows = typeof(UniversalRenderPipelineAsset).GetField("m_AdditionalLightShadowsSupported", flags);
        var lightLayers = typeof(UniversalRenderPipelineAsset).GetField("m_SupportsLightLayers", flags);
        var modes = Path.Combine(dir, "modes.jsonl");
        File.WriteAllText(modes, "");
        s_ExtraKeywords = s_PathKeywords;
        s_AfterRecord = name => AppendModes(modes, name, asset);
        // Records the scenario once per rendering mode; `configure` adds the scene's content
        // (undone by `restore`) and may record through its own helper (the feature scenarios).
        void Both(string name, Action setup, Action teardown, Action<string> record = null)
        {
            foreach (var (mode, suffix) in new[] { (RenderingMode.Forward, "_forward"), (RenderingMode.Deferred, "_deferred") })
            {
                rendererData.renderingMode = mode;
                rendererData.SetDirty();
                if (record != null)
                {
                    setup();
                    record(name + suffix);
                    teardown();
                }
                else
                    Scenario(dir, name + suffix, setup, teardown);
            }
            rendererData.renderingMode = RenderingMode.Forward;
            rendererData.SetDirty();
        }

        var sun = FindObjectsByType<Light>(FindObjectsSortMode.None)[0];
        var sunRotation = sun.transform.rotation;
        var spheres = new GameObject[0];
        var materials = new Material[0];
        // Lit spheres beside the scene's cube (Lit) and unlit sphere: smooth metal, rough
        // dielectric and Simple Lit, the GBuffer's material kinds.
        Action addSpheres = () =>
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var metal = new Material(lit);
            metal.SetColor("_BaseColor", new Color(0.9f, 0.75f, 0.4f));
            metal.SetFloat("_Metallic", 0.9f);
            metal.SetFloat("_Smoothness", 0.85f);
            var rough = new Material(lit);
            rough.SetColor("_BaseColor", new Color(0.3f, 0.5f, 0.9f));
            rough.SetFloat("_Metallic", 0f);
            rough.SetFloat("_Smoothness", 0.2f);
            var simple = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            simple.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f));
            materials = new[] { metal, rough, simple };
            spheres = new GameObject[3];
            var positions = new[] { new Vector3(-1.6f, 0.5f, 0.2f), new Vector3(1.7f, 0.5f, 0.6f), new Vector3(0.6f, 0.35f, -1.2f) };
            for (int i = 0; i < 3; i++)
            {
                spheres[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                DestroyImmediate(spheres[i].GetComponent<Collider>());
                spheres[i].name = "Path Sphere " + i;
                spheres[i].transform.position = positions[i];
                spheres[i].transform.localScale = Vector3.one * (i == 2 ? 0.7f : 1f);
                spheres[i].GetComponent<MeshRenderer>().sharedMaterial = materials[i];
            }
        };
        Action removeSpheres = () =>
        {
            foreach (var s in spheres) DestroyImmediate(s);
            foreach (var m in materials) DestroyImmediate(m);
        };
        var locals = new System.Collections.Generic.List<GameObject>();
        Light AddLight(LightType type, Vector3 position, Quaternion rotation, Color color, float intensity, float range)
        {
            var o = new GameObject(type + " Light " + locals.Count);
            o.transform.SetPositionAndRotation(position, rotation);
            var l = o.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            locals.Add(o);
            return l;
        }
        Action clearLights = () =>
        {
            foreach (var o in locals) DestroyImmediate(o);
            locals.Clear();
        };
        Action pointAndSpot = () =>
        {
            AddLight(LightType.Point, new Vector3(0f, 1.2f, -1f), Quaternion.identity, new Color(1f, 0.3f, 0.2f), 2f, 4f);
            AddLight(LightType.Spot, new Vector3(1.2f, 3f, 0f), Quaternion.Euler(90f, 0f, 0f), new Color(0.2f, 0.4f, 1f), 3f, 6f).spotAngle = 40f;
        };

        // The main light alone and with a point and a spot light.
        Both("opaque", addSpheres, removeSpheres);
        Both("lights", () => { addSpheres(); pointAndSpot(); }, () => { removeSpheres(); clearLights(); });
        // Shadows from the main light, a spot and a point light.
        Both("shadows", () =>
        {
            addSpheres();
            additionalShadows.SetValue(asset, true);
            sun.shadows = LightShadows.Hard;
            sun.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
            var s = AddLight(LightType.Spot, new Vector3(-0.2f, 2.8f, -2f), Quaternion.identity, new Color(1f, 0.9f, 0.7f), 5f, 10f);
            s.transform.LookAt(new Vector3(0f, 0f, 0.8f));
            s.spotAngle = 80f;
            s.shadows = LightShadows.Hard;
            AddLight(LightType.Point, new Vector3(0.8f, 1.1f, -0.6f), Quaternion.identity, new Color(0.4f, 1f, 0.5f), 2f, 5f).shadows = LightShadows.Hard;
        }, () =>
        {
            removeSpheres();
            clearLights();
            additionalShadows.SetValue(asset, false);
            sun.shadows = LightShadows.None;
            sun.transform.rotation = sunRotation;
        });
        // A Lit transparent sphere (Deferred draws transparent objects forward).
        Material glass = null;
        GameObject pane = null;
        void addGlass(Vector3 position, float size)
        {
            pane = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(pane.GetComponent<Collider>());
            pane.name = "Glass";
            pane.transform.position = position;
            pane.transform.localScale = Vector3.one * size;
            glass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            glass.SetFloat("_Surface", 1f);
            glass.SetFloat("_Blend", 0f);
            glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite", 0f);
            glass.SetFloat("_Smoothness", 0.9f);
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            glass.renderQueue = (int)RenderQueue.Transparent;
            glass.SetColor("_BaseColor", new Color(0.2f, 0.9f, 0.6f, 0.4f));
            pane.GetComponent<MeshRenderer>().sharedMaterial = glass;
        }
        void removeGlass()
        {
            DestroyImmediate(pane);
            DestroyImmediate(glass);
        }
        // Twelve point lights over the ground: more than Forward's per-object limit (the
        // asset's 4); the Manual gives Deferred no limit for opaque objects, a limit for
        // transparent ones: a transparent sphere inside the ring, reached by most of the lights.
        Both("many_lights", () =>
        {
            addSpheres();
            addGlass(new Vector3(0.2f, 0.6f, -0.3f), 1.1f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                AddLight(LightType.Point, new Vector3(Mathf.Cos(a) * 2.2f, 0.6f, Mathf.Sin(a) * 1.8f), Quaternion.identity,
                    Color.HSVToRGB(i / 12f, 0.8f, 1f), 1.5f, 2.5f);
            }
        }, () => { removeSpheres(); clearLights(); removeGlass(); });
        // Light layers (the asset's Use Rendering Layers; the project defines Layer 1 and 2):
        // the point light lights Layer 1 only, the cube and the ground are on Default only, the
        // spheres on both; the spot light lights Default.
        Renderer cube = GameObject.Find("Cube").GetComponent<MeshRenderer>();
        uint cubeMask = cube.renderingLayerMask;
        Both("light_layers", () =>
        {
            addSpheres();
            lightLayers.SetValue(asset, true);
            pointAndSpot();
            var point = locals[0];
            var data = point.GetComponent<UniversalAdditionalLightData>() ?? point.AddComponent<UniversalAdditionalLightData>();
            data.renderingLayers = 2u;
            point.GetComponent<Light>().range = 5f;
            cube.renderingLayerMask = 1u;
            foreach (var s in spheres) s.GetComponent<MeshRenderer>().renderingLayerMask = 3u;
        }, () =>
        {
            removeSpheres();
            clearLights();
            lightLayers.SetValue(asset, false);
            cube.renderingLayerMask = cubeMask;
        });
        // The renderer's Accurate G-buffer normals (octahedron encoded); Forward ignores it.
        Both("accurate_normals", () => { addSpheres(); pointAndSpot(); rendererData.accurateGbufferNormals = true; },
            () => { removeSpheres(); clearLights(); rendererData.accurateGbufferNormals = false; });
        // Lit transparent objects (forward after the deferred lighting) in front of the scene.
        Both("transparent", () => { addSpheres(); pointAndSpot(); addGlass(new Vector3(-0.4f, 0.8f, -1.6f), 1.4f); },
            () => { removeSpheres(); clearLights(); removeGlass(); });
        // MSAA: the asset's 4 samples and a 4-sample target (Deferred renders without it).
        Both("msaa", () => { addSpheres(); pointAndSpot(); asset.msaaSampleCount = 4; s_Antialiasing = 4; },
            () => { removeSpheres(); clearLights(); asset.msaaSampleCount = 1; s_Antialiasing = 1; });

        // The renderer features, from the feature scenarios' closer camera.
        var assets = Resources.Load<FeatureAssets>("FeatureAssets");
        var camera = Camera.main.transform;
        var (cameraPosition, cameraRotation) = (camera.position, camera.rotation);
        camera.SetPositionAndRotation(new Vector3(-0.5f, 1.4f, -2.6f), Quaternion.Euler(20f, 0f, 0f));
        var ambientProbe = RenderSettings.ambientProbe;
        var bright = new SphericalHarmonicsL2();
        bright.AddAmbientLight(new Color(0.8f, 0.8f, 0.8f));
        Both("ssao", () => { addSpheres(); RenderSettings.ambientProbe = bright; }, () => { removeSpheres(); RenderSettings.ambientProbe = ambientProbe; },
            name => SsaoScenario(dir, rendererData, name, null));
        Action<Action<DecalProjector>> place = configure =>
        {
            var projector = new GameObject("Decal Projector");
            projector.SetActive(false);
            projector.transform.SetPositionAndRotation(new Vector3(-0.6f, 1.3f, -0.6f), Quaternion.Euler(90f, 0f, 0f));
            var p = projector.AddComponent<DecalProjector>();
            p.material = assets.decal;
            p.size = new Vector3(1.8f, 1.8f, 1.6f);
            p.pivot = new Vector3(0f, 0f, 0.8f);
            configure?.Invoke(p);
            projector.SetActive(true);
        };
        Both("decal_screen_space", () => { }, () => { },
            name => DecalScenario(dir, rendererData, name, s => Set(s, "technique", "ScreenSpace"), () => place(null)));
        Both("decal_dbuffer", () => { }, () => { },
            name => DecalScenario(dir, rendererData, name, s => Set(s, "technique", "DBuffer"), () => place(null)));
        // Decal layers: only the cube shares the projector's rendering layer (Layer 2).
        foreach (var (name, technique) in new[] { ("decal_layers", "ScreenSpace"), ("decal_dbuffer_layers", "DBuffer") })
            Both(name, () => { }, () => { },
                n => DecalScenario(dir, rendererData, n, s => { Set(s, "technique", technique); Set(s, "decalLayers", true); }, () =>
                {
                    cube.renderingLayerMask = 1u | 4u;
                    place(p => p.renderingLayerMask = 4u);
                }, () => cube.renderingLayerMask = cubeMask));
        s_ExtraKeywords = s_PathKeywords;
        camera.SetPositionAndRotation(cameraPosition, cameraRotation);

        // Mixed lighting: the baked scenes (a mixed sun with shadowmask or subtractive lightmaps,
        // light probes, a dynamic cube casting a realtime shadow).
        yield return Load("Shadowmask");
        var shadowmaskMode = QualitySettings.shadowmaskMode;
        Both("lightmap_shadowmask", () => QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask, () => QualitySettings.shadowmaskMode = shadowmaskMode);
        Both("lightmap_distance_shadowmask", () => QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask,
            () => QualitySettings.shadowmaskMode = shadowmaskMode);
        yield return Load("Subtractive");
        Both("lightmap_subtractive", () => { }, () => { });
        s_AfterRecord = null;
        Quit();
    }

    /// <summary>One modes.jsonl block: the device and what the renderer rendered the recording
    /// with.</summary>
    static void AppendModes(string path, string name, UniversalRenderPipelineAsset asset)
    {
        var renderer = asset.GetRenderer(0);
        var actual = renderer.GetType().GetProperty("renderingModeActual", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
        var lines = new StringBuilder();
        lines.Append("{\"key\":\"").Append(name).Append(" device\",\"value\":\"").Append(SystemInfo.graphicsDeviceType).Append("\"}\n");
        lines.Append("{\"key\":\"").Append(name).Append(" renderingModeActual\",\"value\":\"").Append(actual).Append("\"}\n");
        foreach (var keyword in s_PathKeywords)
            lines.Append("{\"key\":\"").Append(name).Append(" keyword ").Append(keyword).Append("\",\"value\":")
                .Append(Shader.IsKeywordEnabled(keyword) ? "true" : "false").Append("}\n");
        File.AppendAllText(path, lines.ToString());
    }
}
