using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renders the main camera into a 320x180 render texture, reads it back and writes it to
/// <c>URP_PROBE_OUT</c> (a PNG; with <c>URP_PROBE_JSON</c> also sample pixels), then quits.</summary>
public class UrpProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;
    /// <summary>The target's MSAA samples (the msaa scenario's 4).</summary>
    static int s_Antialiasing = 1;
    /// <summary>More keywords to record (the lighting scenarios').</summary>
    static string[] s_ExtraKeywords = Array.Empty<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("UrpProbe").AddComponent<UrpProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        // URP_PROBE_DIR: every scenario into <dir>/<name>.png (and its constants, .jsonl).
        var dir = Environment.GetEnvironmentVariable("URP_PROBE_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            var light = FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            ShadowMatrixProbe.Record(Path.Combine(dir, "shadow-matrices.jsonl"), Path.Combine(dir, "conservative-shadow-matrices.jsonl"));
            LocalShadowMatrixProbe.Record(Path.Combine(dir, "local-shadow-matrices.jsonl"));
            SkyMeshProbe.Record(Path.Combine(dir, "sky-mesh.jsonl"));
            Scenario(dir, "base", () => { }, () => { });
            var sun = light.transform.rotation;
            Scenario(dir, "shadows", () => { light.shadows = LightShadows.Hard; light.shadowStrength = 1f; light.transform.rotation = Quaternion.Euler(40f, 160f, 0f); },
                () => { light.shadows = LightShadows.None; light.transform.rotation = sun; });
            GameObject point = null, spot = null;
            Scenario(dir, "lights", () =>
            {
                point = new GameObject("Point Light");
                point.transform.position = new Vector3(0f, 1.2f, -1f);
                var p = point.AddComponent<Light>();
                p.type = LightType.Point;
                p.color = new Color(1f, 0.3f, 0.2f);
                p.intensity = 2f;
                p.range = 4f;
                spot = new GameObject("Spot Light");
                spot.transform.position = new Vector3(1.2f, 3f, 0f);
                spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var s = spot.AddComponent<Light>();
                s.type = LightType.Spot;
                s.color = new Color(0.2f, 0.4f, 1f);
                s.intensity = 3f;
                s.range = 6f;
                s.spotAngle = 40f;
            }, () => { DestroyImmediate(point); DestroyImmediate(spot); });
            Scenario(dir, "lights_shadows", () =>
            {
                light.shadows = LightShadows.Hard;
                light.transform.rotation = Quaternion.Euler(55f, -130f, 0f);
                point = new GameObject("Point Light");
                point.transform.position = new Vector3(-0.4f, 1.6f, -1.2f);
                var p = point.AddComponent<Light>();
                p.type = LightType.Point;
                p.color = new Color(1f, 0.85f, 0.4f);
                p.intensity = 1.5f;
                p.range = 5f;
            }, () => { light.shadows = LightShadows.None; light.transform.rotation = sun; DestroyImmediate(point); });
            var cameraData = Camera.main.GetComponent<UniversalAdditionalCameraData>() ?? Camera.main.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            // supportsAdditionalLightShadows has an internal setter.
            var additionalShadows = typeof(UniversalRenderPipelineAsset).GetField("m_AdditionalLightShadowsSupported",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Scenario(dir, "spot_shadows", () =>
            {
                additionalShadows.SetValue(asset, true);
                spot = new GameObject("Spot Light");
                spot.transform.position = new Vector3(-0.2f, 2.8f, -2f);
                spot.transform.LookAt(new Vector3(0f, 0f, 0.8f));
                var s = spot.AddComponent<Light>();
                s.type = LightType.Spot;
                s.color = new Color(1f, 0.9f, 0.7f);
                s.intensity = 6f;
                s.range = 10f;
                s.spotAngle = 80f;
                s.shadows = LightShadows.Hard;
            }, () => { additionalShadows.SetValue(asset, false); DestroyImmediate(spot); });
            Scenario(dir, "point_shadows", () =>
            {
                additionalShadows.SetValue(asset, true);
                point = new GameObject("Point Light");
                point.transform.position = new Vector3(0f, 1f, -1.2f);
                var p = point.AddComponent<Light>();
                p.type = LightType.Point;
                p.color = new Color(1f, 0.8f, 0.5f);
                p.intensity = 3f;
                p.range = 6f;
                p.shadows = LightShadows.Hard;
            }, () => { additionalShadows.SetValue(asset, false); DestroyImmediate(point); });
            Scenario(dir, "skybox", () => Camera.main.clearFlags = CameraClearFlags.Skybox, () => Camera.main.clearFlags = CameraClearFlags.SolidColor);
            GameObject volume = null;
            VolumeProfile profile = null;
            Scenario(dir, "postfx", () =>
            {
                cameraData.renderPostProcessing = true;
                volume = new GameObject("Volume");
                var v = volume.AddComponent<Volume>();
                v.isGlobal = true;
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.Override(1.5f);
                bloom.threshold.Override(0.8f);
                var tonemapping = profile.Add<Tonemapping>(true);
                tonemapping.mode.Override(TonemappingMode.ACES);
                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.Override(0.35f);
                var adjustments = profile.Add<ColorAdjustments>(true);
                adjustments.saturation.Override(20f);
                adjustments.contrast.Override(10f);
                v.sharedProfile = profile;
            }, () => { cameraData.renderPostProcessing = false; DestroyImmediate(volume); DestroyImmediate(profile); });
            foreach (var effect in new[] { "bloom", "tonemap", "vignette", "color" })
            {
                Scenario(dir, "postfx_" + effect, () =>
                {
                    cameraData.renderPostProcessing = true;
                    volume = new GameObject("Volume");
                    var v = volume.AddComponent<Volume>();
                    v.isGlobal = true;
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    switch (effect)
                    {
                        case "bloom": { var b = profile.Add<Bloom>(true); b.intensity.Override(1.5f); b.threshold.Override(0.8f); break; }
                        case "tonemap": profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES); break;
                        case "vignette": profile.Add<Vignette>(true).intensity.Override(0.35f); break;
                        default: { var c = profile.Add<ColorAdjustments>(true); c.saturation.Override(20f); c.contrast.Override(10f); break; }
                    }
                    v.sharedProfile = profile;
                }, () => { cameraData.renderPostProcessing = false; DestroyImmediate(volume); DestroyImmediate(profile); });
            }
            GameObject glass = null;
            Material glassMaterial = null;
            Scenario(dir, "transparent", () =>
            {
                glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(glass.GetComponent<Collider>());
                glass.transform.position = new Vector3(0f, 0.8f, -1.5f);
                glass.transform.localScale = new Vector3(2.5f, 1.2f, 1f);
                glassMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                glassMaterial.SetFloat("_Surface", 1f);
                glassMaterial.SetFloat("_Blend", 0f);
                glassMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                glassMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                glassMaterial.SetFloat("_ZWrite", 0f);
                glassMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glassMaterial.renderQueue = (int)RenderQueue.Transparent;
                glassMaterial.SetColor("_BaseColor", new Color(0.2f, 0.6f, 1f, 0.45f));
                glass.GetComponent<MeshRenderer>().sharedMaterial = glassMaterial;
            }, () => { DestroyImmediate(glass); DestroyImmediate(glassMaterial); });
            Scenario(dir, "fog", () =>
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = new Color(0.7f, 0.75f, 0.8f);
                RenderSettings.fogStartDistance = 2f;
                RenderSettings.fogEndDistance = 15f;
            }, () => RenderSettings.fog = false);
            s_Antialiasing = 4;
            Scenario(dir, "msaa", () => asset.msaaSampleCount = 4, () => asset.msaaSampleCount = 1);
            s_Antialiasing = 1;
            Scenario(dir, "render_scale", () => asset.renderScale = 0.5f, () => asset.renderScale = 1f);
            GameObject overlay = null, marker = null;
            int mask = Camera.main.cullingMask;
            Scenario(dir, "camera_stack", () =>
            {
                // An overlay camera drawing one quad (layer 5) over the base camera's image.
                Camera.main.cullingMask = mask & ~(1 << 5);
                marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(marker.GetComponent<Collider>());
                marker.layer = 5;
                marker.transform.position = new Vector3(0.6f, 0.3f, 4f);
                marker.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
                overlay = new GameObject("Overlay Camera");
                overlay.transform.position = new Vector3(0f, 0f, 0f);
                var o = overlay.AddComponent<Camera>();
                o.cullingMask = 1 << 5;
                o.fieldOfView = 40f;
                var od = overlay.AddComponent<UniversalAdditionalCameraData>();
                od.renderType = CameraRenderType.Overlay;
                cameraData.cameraStack.Add(o);
            }, () =>
            {
                cameraData.cameraStack.Clear();
                Camera.main.cullingMask = mask;
                DestroyImmediate(overlay);
                DestroyImmediate(marker);
            });
            var rendererData = ((ScriptableRendererData[])typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(asset))[0];
            var feature = ScriptableObject.CreateInstance<StateBlockProbeFeature>();
            rendererData.rendererFeatures.Add(feature);
            rendererData.SetDirty();
            Scenario(dir, "state_block", () =>
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", new Color(1f, 0.9f, 0.1f, 0.6f));
                StateBlockProbeFeature.Material = m;
            }, () => { DestroyImmediate(StateBlockProbeFeature.Material); StateBlockProbeFeature.Material = null; });
            foreach (int pass in new[] { 0, 1, -1 })
                Scenario(dir, "override_shader_" + (pass < 0 ? "default" : pass.ToString()), () =>
                {
                    StateBlockProbeFeature.OverrideShader = Shader.Find("Probe/Override");
                    StateBlockProbeFeature.ShaderPass = pass;
                }, () => StateBlockProbeFeature.OverrideShader = null);
            Scenario(dir, "override_shader_and_material", () =>
            {
                StateBlockProbeFeature.Material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                StateBlockProbeFeature.Material.SetColor("_BaseColor", new Color(0.8f, 0.1f, 0.9f, 0.6f));
                StateBlockProbeFeature.OverrideShader = Shader.Find("Probe/Override");
                StateBlockProbeFeature.ShaderPass = 1;
            }, () =>
            {
                DestroyImmediate(StateBlockProbeFeature.Material);
                StateBlockProbeFeature.Material = null;
                StateBlockProbeFeature.OverrideShader = null;
            });
            rendererData.rendererFeatures.Remove(feature);
            rendererData.SetDirty();
            DestroyImmediate(feature);
            // The lights scenario under the other rendering paths.
            var universalData = (UniversalRendererData)rendererData;
            foreach (var (name, mode) in new[] { ("forward_plus", RenderingMode.ForwardPlus), ("deferred", RenderingMode.Deferred) })
            {
                Scenario(dir, name, () =>
                {
                    universalData.renderingMode = mode;
                    point = new GameObject("Point Light");
                    point.transform.position = new Vector3(0f, 1.2f, -1f);
                    var p = point.AddComponent<Light>();
                    p.type = LightType.Point;
                    p.color = new Color(1f, 0.3f, 0.2f);
                    p.intensity = 2f;
                    p.range = 4f;
                    spot = new GameObject("Spot Light");
                    spot.transform.position = new Vector3(1.2f, 3f, 0f);
                    spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    var s = spot.AddComponent<Light>();
                    s.type = LightType.Spot;
                    s.color = new Color(0.2f, 0.4f, 1f);
                    s.intensity = 3f;
                    s.range = 6f;
                    s.spotAngle = 40f;
                }, () => { universalData.renderingMode = RenderingMode.Forward; DestroyImmediate(point); DestroyImmediate(spot); });
            }
            GameObject left = null, right = null;
            Material bufferMaterial = null;
            GraphicsBuffer offsets = null, colors = null, otherColors = null;
            Scenario(dir, "structured_buffer", () =>
            {
                // Per-vertex offsets (structured) and colors (raw): the material's for both quads,
                // a property block's colors for the right one.
                offsets = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16);
                offsets.SetData(new[] { new Vector4(-0.2f, -0.1f, 0f, 0f), new Vector4(0.3f, 0f, 0f, 0f), new Vector4(0f, 0.25f, 0f, 0f), new Vector4(0.1f, 0.1f, 0f, 0f) });
                colors = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 16, 4);
                colors.SetData(new[] { 1f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 1f });
                otherColors = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 16, 4);
                otherColors.SetData(new[] { 0f, 1f, 1f, 1f, 1f, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 0.2f, 0.2f, 0.2f, 1f });
                bufferMaterial = new Material(Shader.Find("Probe/BufferProbe"));
                bufferMaterial.SetBuffer("_Offsets", offsets);
                bufferMaterial.SetBuffer("_Colors", colors);
                left = GameObject.CreatePrimitive(PrimitiveType.Quad);
                left.transform.position = new Vector3(-1.4f, 1.5f, -2f);
                left.GetComponent<MeshRenderer>().sharedMaterial = bufferMaterial;
                right = GameObject.CreatePrimitive(PrimitiveType.Quad);
                right.transform.position = new Vector3(1.4f, 1.5f, -2f);
                var r = right.GetComponent<MeshRenderer>();
                r.sharedMaterial = bufferMaterial;
                var block = new MaterialPropertyBlock();
                block.SetBuffer("_Colors", otherColors);
                r.SetPropertyBlock(block);
            }, () =>
            {
                DestroyImmediate(left);
                DestroyImmediate(right);
                DestroyImmediate(bufferMaterial);
                offsets.Release();
                colors.Release();
                otherColors.Release();
            });
            Scenario(dir, "depth_opaque", () => { cameraData.requiresDepthTexture = true; cameraData.requiresColorTexture = true; },
                () => { cameraData.requiresDepthTexture = false; cameraData.requiresColorTexture = false; });
            s_ExtraKeywords = new[] { "_LIGHT_COOKIES", "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION", "SHADOWS_SHADOWMASK", "_MIXED_LIGHTING_SUBTRACTIVE" };
            CookieScenarios(dir, light);
            StartCoroutine(SceneScenarios(dir, asset, universalData));
            return;
        }
        Record(Environment.GetEnvironmentVariable("URP_PROBE_OUT"), Environment.GetEnvironmentVariable("URP_PROBE_JSON"));
        Quit();
    }

    /// <summary>Light cookies: the main light's (tiled by the URP light data's cookie size and
    /// offset), a spot light's colored cookie and a point light's cube cookie.</summary>
    void CookieScenarios(string dir, Light sun)
    {
        var textures = Resources.Load<LightingTextures>("LightingTextures");
        var sunData = sun.GetComponent<UniversalAdditionalLightData>() ?? sun.gameObject.AddComponent<UniversalAdditionalLightData>();
        Scenario(dir, "cookie_directional", () =>
        {
            sun.cookie = textures.cookieGray;
            sunData.lightCookieSize = new Vector2(1.5f, 2f);
            sunData.lightCookieOffset = new Vector2(0.4f, -0.3f);
        }, () => { sun.cookie = null; sunData.lightCookieSize = Vector2.one; sunData.lightCookieOffset = Vector2.zero; });
        // An opaque image imported as a Cookie, its alpha from the input: a white cookie.
        Scenario(dir, "cookie_directional_opaque", () =>
        {
            sun.cookie = textures.cookieOpaque;
            sunData.lightCookieSize = new Vector2(1.5f, 2f);
        }, () => { sun.cookie = null; sunData.lightCookieSize = Vector2.one; });
        GameObject local = null;
        Scenario(dir, "cookie_spot", () =>
        {
            local = new GameObject("Spot Light");
            local.transform.position = new Vector3(0.2f, 3f, -0.8f);
            local.transform.rotation = Quaternion.Euler(80f, 20f, 0f);
            var s = local.AddComponent<Light>();
            s.type = LightType.Spot;
            s.intensity = 3f;
            s.range = 8f;
            s.spotAngle = 70f;
            s.cookie = textures.cookieColor;
        }, () => DestroyImmediate(local));
        Scenario(dir, "cookie_point", () =>
        {
            local = new GameObject("Point Light");
            local.transform.position = new Vector3(0f, 1.3f, -0.6f);
            local.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            var p = local.AddComponent<Light>();
            p.type = LightType.Point;
            p.intensity = 3f;
            p.range = 5f;
            p.cookie = textures.cookieCube;
        }, () => DestroyImmediate(local));
    }

    /// <summary>The baked scenes (Assets/Editor/LightingSetup): reflection probes with the URP
    /// asset's blending and box projection off and on (and on Forward+), lightmaps and light
    /// probes in Shadowmask (both shadowmask modes) and Subtractive mode.</summary>
    IEnumerator SceneScenarios(string dir, UniversalRenderPipelineAsset asset, UniversalRendererData rendererData)
    {
        DontDestroyOnLoad(gameObject);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var blending = typeof(UniversalRenderPipelineAsset).GetField("m_ReflectionProbeBlending", flags);
        var boxProjection = typeof(UniversalRenderPipelineAsset).GetField("m_ReflectionProbeBoxProjection", flags);
        yield return Load("Reflections");
        var constants = Path.Combine(dir, "per-object-constants.jsonl");
        File.WriteAllText(constants, "");
        var reflectionObjects = new[] { "Baked Only", "Overlap", "Custom Only", "Outside", "Off", "Simple", "Floor", "Back Wall" };
        DumpPerObject(constants, "reflections simple", reflectionObjects);
        Scenario(dir, "reflections_simple", () => { }, () => { });
        blending.SetValue(asset, true);
        boxProjection.SetValue(asset, true);
        DumpPerObject(constants, "reflections blending", reflectionObjects);
        blending.SetValue(asset, false);
        boxProjection.SetValue(asset, false);
        Scenario(dir, "reflections", () => { blending.SetValue(asset, true); boxProjection.SetValue(asset, true); },
            () => { blending.SetValue(asset, false); boxProjection.SetValue(asset, false); });
        Scenario(dir, "reflections_forward_plus", () =>
        {
            blending.SetValue(asset, true);
            boxProjection.SetValue(asset, true);
            rendererData.renderingMode = RenderingMode.ForwardPlus;
        }, () =>
        {
            blending.SetValue(asset, false);
            boxProjection.SetValue(asset, false);
            rendererData.renderingMode = RenderingMode.Forward;
        });
        var bakedObjects = new[] { "Ground", "Wall", "Block", "Lit Sphere", "Simple Lit Sphere", "Baked Lit Sphere", "Dynamic Cube", "Proxy Sphere" };
        yield return Load("Shadowmask");
        DumpPerObject(constants, "shadowmask", bakedObjects);
        var shadowmaskMode = QualitySettings.shadowmaskMode;
        Scenario(dir, "lightmap_shadowmask", () => QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask, () => QualitySettings.shadowmaskMode = shadowmaskMode);
        Scenario(dir, "lightmap_distance_shadowmask", () => QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask, () => QualitySettings.shadowmaskMode = shadowmaskMode);
        yield return Load("Subtractive");
        DumpPerObject(constants, "subtractive", bakedObjects);
        Scenario(dir, "lightmap_subtractive", () => { }, () => { });
        Quit();
    }

    const int DumpLayer = 30;
    static readonly string[] s_DumpConstants = { "unity_SpecCube0_BoxMin", "unity_SpecCube0_BoxMax", "unity_SpecCube0_ProbePosition",
        "unity_SpecCube0_HDR", "unity_SpecCube1_BoxMin", "unity_SpecCube1_BoxMax", "unity_SpecCube1_ProbePosition", "unity_SpecCube1_HDR",
        "unity_SpecCube0 sample", "unity_SpecCube1 sample", "unity_LightData", "unity_LightmapST", "unity_ProbesOcclusion",
        "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC" };

    /// <summary>The per-object constants the named renderers of the loaded scene draw with
    /// (Probe/PerObjectDump): a copy of each (same mesh, transform, lightmap, light and
    /// reflection probe usage, so the same bounds and baked lighting) rendered alone into a 20x1
    /// float target.</summary>
    static void DumpPerObject(string path, string setting, string[] names)
    {
        var main = Camera.main;
        var target = new RenderTexture(s_DumpConstants.Length, 1, 0, RenderTextureFormat.ARGBFloat);
        var cameraObject = new GameObject("Dump Camera");
        cameraObject.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = main.fieldOfView;
        camera.cullingMask = 1 << DumpLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.targetTexture = target;
        var material = new Material(Shader.Find("Probe/PerObjectDump"));
        var readback = new Texture2D(s_DumpConstants.Length, 1, TextureFormat.RGBAFloat, false);
        var lines = new System.Text.StringBuilder();
        foreach (var name in names)
        {
            var source = GameObject.Find(name).GetComponent<MeshRenderer>();
            var copy = new GameObject("Dump " + name) { layer = DumpLayer };
            copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            copy.transform.localScale = source.transform.lossyScale;
            copy.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var renderer = copy.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.reflectionProbeUsage = source.reflectionProbeUsage;
            renderer.lightProbeUsage = source.lightProbeUsage;
            renderer.lightProbeProxyVolumeOverride = source.gameObject;
            renderer.lightmapIndex = source.lightmapIndex;
            renderer.lightmapScaleOffset = source.lightmapScaleOffset;
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, s_DumpConstants.Length, 1), 0, 0);
            readback.Apply();
            RenderTexture.active = null;
            DestroyImmediate(copy);
            for (int i = 0; i < s_DumpConstants.Length; i++)
            {
                var c = readback.GetPixel(i, 0);
                lines.Append("{\"key\":\"").Append(setting).Append(' ').Append(name).Append(' ').Append(s_DumpConstants[i]).Append("\",\"value\":[")
                    .Append(string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))
                    .Append("]}\n");
            }
        }
        File.AppendAllText(path, lines.ToString());
        camera.targetTexture = null;
        target.Release();
        DestroyImmediate(cameraObject);
        DestroyImmediate(material);
        DestroyImmediate(readback);
    }

    /// <summary>Loads a scene and waits until its lighting data and reflection probes are set up.</summary>
    static IEnumerator Load(string scene)
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        for (int i = 0; i < 3; i++) yield return null;
    }

    void Scenario(string dir, string name, Action setup, Action teardown)
    {
        setup();
        Record(Path.Combine(dir, name + ".png"), Path.Combine(dir, name + ".jsonl"));
        teardown();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Renders the main camera (twice: the first render may still compile) and writes
    /// the image and the constants URP set.</summary>
    static void Record(string output, string json)
    {
        var camera = Camera.main;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = s_Antialiasing };
        camera.targetTexture = target;
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        target.Release();
        if (!string.IsNullOrEmpty(json))
        {
            // The lighting constants URP set for the frame.
            var lines = new System.Text.StringBuilder();
            foreach (var name in new[] { "_MainLightPosition", "_MainLightColor", "_AdditionalLightsCount", "_GlossyEnvironmentColor",
                "_GlossyEnvironmentCubeMap_HDR", "_AmbientOcclusionParam", "_ScaledScreenParams", "_WorldSpaceCameraPos", "_ProjectionParams",
                "_ZBufferParams", "unity_OrthoParams", "_MainLightShadowParams", "_ShadowBias", "_TimeParameters", "unity_FogColor", "unity_FogParams",
                "_MainLightShadowmapSize", "_MainLightShadowOffset0", "_AdditionalLightsPosition", "_AdditionalLightsColor", "_AdditionalLightsAttenuation", "_AdditionalLightsSpotDir", "_AdditionalShadowParams", "_AdditionalShadowmapSize", "_AdditionalShadowFadeParams" })
            {
                var v = Shader.GetGlobalVector(name);
                lines.Append("{\"key\":\"").Append(name).Append("\",\"value\":[")
                    .Append(string.Join(",", new[] { v.x, v.y, v.z, v.w }.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))
                    .Append("]}\n");
            }
            var stack = VolumeManager.instance.stack;
            var bloom = stack?.GetComponent<Bloom>();
            var cameraData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
            lines.Append("{\"key\":\"volumes\",\"value\":\"").Append(VolumeManager.instance.GetVolumes(1).Length)
                .Append(" bloom ").Append(bloom != null && bloom.IsActive()).Append(" ").Append(bloom != null ? bloom.intensity.value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")
                .Append(" post ").Append(cameraData != null && cameraData.renderPostProcessing).Append("\"}\n");
            foreach (var name in new[] { "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN", "_ADDITIONAL_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX", "_SHADOWS_SOFT", "LIGHTMAP_SHADOW_MIXING", "_FORWARD_PLUS", "FOG_LINEAR" }.Concat(s_ExtraKeywords))
                lines.Append("{\"key\":\"keyword ").Append(name).Append("\",\"value\":").Append(Shader.IsKeywordEnabled(name) ? "true" : "false").Append("}\n");
            File.WriteAllText(json, lines.ToString());
        }
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllBytes(output, texture.EncodeToPNG());
            Debug.Log("urp probe written to " + output);
        }
    }
}
