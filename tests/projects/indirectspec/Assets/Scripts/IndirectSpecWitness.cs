using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The unity_IndirectSpecColor witness on the baked Main scene (Trilight ambient, default
/// skybox), as JSON lines {"k": key, "v": value} in <c>INDIRECT_SPEC_OUT</c>, then quits.
/// The last line, and only it, is {"k":"complete","v":true}.
///
/// Two consumers per capture:
/// * "dump": Hidden/IndirectSpecDump writes unity_IndirectSpecColor (and the other lighting
///   globals named in <see cref="Slots"/>) into an RGBAFloat target, read back as raw floats;
/// * "standard": a Standard sphere (white albedo, metallic 0, smoothness 0,
///   _SPECULARHIGHLIGHTS_OFF, _GLOSSYREFLECTIONS_OFF) whose light probe is a BLACK
///   CustomProvided SH, all lights disabled, rendered into an ARGB32 target (bytes) and an
///   ARGBFloat target (floats).
///
/// The settings: ambientIntensity {1, 0.5} x reflectionIntensity {1, 0.25}, set through
/// RenderSettings in this order: (1, 1) (the scene's own), (1, 0.25), (0.5, 1), (0.5, 0.25).
/// Each is captured in the frame it is set ("immediate") and again two frames later
/// ("settled"). Before that, the dump alone is captured once with the scene's lights enabled.
/// Each setting records the RenderSettings, RenderSettings.ambientProbe, the default
/// reflection's HDR decode values and the color space; the context records the lighting data's
/// serialized ambient probe (Editor only), the default reflection texture and the BRDF tier.
/// Nothing machine-specific (paths, frame numbers, instance ids, times) is written.
/// </summary>
public class IndirectSpecWitness : MonoBehaviour
{
    const int SphereLayer = 8, DumpLayer = 9, SphereSize = 64, DumpWidth = 32;

    /// <summary>Column i of the dump target (Hidden/IndirectSpecDump's slot i).</summary>
    static readonly string[] Slots =
    {
        "unity_IndirectSpecColor", "unity_SpecCube0_HDR",
        "specCube0 lastMip decoded +X", "specCube0 lastMip decoded -X", "specCube0 lastMip decoded +Y",
        "specCube0 lastMip decoded -Y", "specCube0 lastMip decoded +Z", "specCube0 lastMip decoded -Z",
        "unity_SHAr", "unity_SHAg", "unity_SHAb", "unity_SHBr", "unity_SHBg", "unity_SHBb", "unity_SHC",
        "unity_AmbientSky", "unity_AmbientEquator", "unity_AmbientGround", "glstate_lightmodel_ambient",
        "unity_ColorSpaceDielectricSpec",
        "specCube0 lastMip raw +X", "specCube0 lastMip raw -X", "specCube0 lastMip raw +Y",
        "specCube0 lastMip raw -Y", "specCube0 lastMip raw +Z", "specCube0 lastMip raw -Z",
    };

    static readonly (string Name, float Ambient, float Reflection)[] Settings =
    {
        ("ambient 1 reflection 1", 1f, 1f),
        ("ambient 1 reflection 0.25", 1f, 0.25f),
        ("ambient 0.5 reflection 1", 0.5f, 1f),
        ("ambient 0.5 reflection 0.25", 0.5f, 0.25f),
    };

    static StreamWriter s_Out;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("IndirectSpecWitness").AddComponent<IndirectSpecWitness>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector4 v) => $"{F(v.x)} {F(v.y)} {F(v.z)} {F(v.w)}";
    static string C(Color c) => $"{F(c.r)} {F(c.g)} {F(c.b)} {F(c.a)}";
    static string Sh(SphericalHarmonicsL2 sh) => string.Join(" ", Enumerable.Range(0, 27).Select(i => F(sh[i / 9, i % 9])));

    static void Put(string key, string value)
    {
        s_Out.Write("{\"k\":\"" + Escape(key) + "\",\"v\":\"" + Escape(value) + "\"}\n");
        s_Out.Flush();
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    static void Try(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { Put(key + " error", e.GetType().Name + ": " + e.Message); }
    }

    Camera m_DumpCamera, m_SphereCamera;
    RenderTexture m_Dump, m_Bytes, m_Floats;

    IEnumerator Start()
    {
        for (int i = 0; i < 3; i++) yield return null;
        var output = Environment.GetEnvironmentVariable("INDIRECT_SPEC_OUT");
        if (string.IsNullOrEmpty(output)) { Quit(); yield break; }
        s_Out = new StreamWriter(output, false, new UTF8Encoding(false));
        Put("format", "indirectspec 1");
        Try("context", Context);
        Setup();

        // Control: the dump with the scene's lights enabled, at the scene's own settings.
        Try("lights enabled", () => RecordSettings("lights enabled"));
        Try("lights enabled", () => Dump("lights enabled"));

        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) l.enabled = false;
        yield return null;
        yield return null;

        foreach (var (name, ambient, reflection) in Settings)
        {
            RenderSettings.ambientIntensity = ambient;
            RenderSettings.reflectionIntensity = reflection;
            Try(name + " immediate", () => Capture(name + " immediate"));
            yield return null;
            yield return null;
            Try(name + " settled", () => Capture(name + " settled"));
        }

        Put("lights disabled", string.Join(" ", FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(l => l.name + "=" + l.enabled).OrderBy(s => s, StringComparer.Ordinal)));
        s_Out.Write("{\"k\":\"complete\",\"v\":true}\n");
        s_Out.Flush();
        s_Out.Close();
        Debug.Log("indirectspec witness written");
        Quit();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>What does not change with the settings.</summary>
    static void Context()
    {
        Put("activeColorSpace", QualitySettings.activeColorSpace.ToString());
        Put("graphicsDeviceType", SystemInfo.graphicsDeviceType.ToString());
        Put("graphics activeTier", Graphics.activeTier.ToString());
#if UNITY_EDITOR
        var group = UnityEditor.EditorUserBuildSettings.selectedBuildTargetGroup;
        Put("activeBuildTarget", UnityEditor.EditorUserBuildSettings.activeBuildTarget + " " + group);
        var tier = UnityEditor.Rendering.EditorGraphicsSettings.GetTierSettings(group, Graphics.activeTier);
        Put("tier standardShaderQuality", tier.standardShaderQuality.ToString());
        var data = UnityEditor.Lightmapping.lightingDataAsset;
        Put("lightingDataAsset", data == null ? "null" : data.name);
        if (data != null)
        {
            // Every top-level serialized property (hidden ones too); the ambient probes' floats.
            var it = new UnityEditor.SerializedObject(data).GetIterator();
            var names = new List<string>();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = false;
                names.Add(it.name);
                if (it.name.IndexOf("AmbientProbe", StringComparison.Ordinal) >= 0) Put("lightingData " + it.name, Floats(it));
            }
            Put("lightingData properties", string.Join(" ", names));
        }
#else
        Put("lightingDataAsset", "player: not recorded");
#endif
        var reflection = ReflectionProbe.defaultTexture;
        Put("defaultTexture", reflection == null ? "null"
            : $"{reflection.name} {reflection.GetType().Name} {reflection.dimension} {reflection.width}x{reflection.height} mips {reflection.mipmapCount} {reflection.graphicsFormat}");
        var skybox = RenderSettings.skybox;
        Put("skybox", skybox == null ? "null" : $"{skybox.name} shader {skybox.shader.name}");
        Put("customReflectionTexture", RenderSettings.customReflectionTexture == null ? "null" : RenderSettings.customReflectionTexture.name);
        Put("sun", RenderSettings.sun == null ? "null" : RenderSettings.sun.name);
    }

#if UNITY_EDITOR
    static string Floats(UnityEditor.SerializedProperty property)
    {
        var values = new List<string>();
        var end = property.GetEndProperty();
        var child = property.Copy();
        if (child.propertyType == UnityEditor.SerializedPropertyType.Float) values.Add(F(child.floatValue));
        while (child.Next(true) && !UnityEditor.SerializedProperty.EqualContents(child, end))
            if (child.propertyType == UnityEditor.SerializedPropertyType.Float) values.Add(F(child.floatValue));
        return values.Count + ": " + string.Join(" ", values);
    }
#endif

    static void RecordSettings(string key)
    {
        Put(key + " RenderSettings",
            $"ambientMode {RenderSettings.ambientMode} ambientIntensity {F(RenderSettings.ambientIntensity)} " +
            $"reflectionIntensity {F(RenderSettings.reflectionIntensity)} defaultReflectionMode {RenderSettings.defaultReflectionMode} " +
            $"reflectionBounces {RenderSettings.reflectionBounces} defaultReflectionResolution {RenderSettings.defaultReflectionResolution} " +
            $"ambientSkyColor {C(RenderSettings.ambientSkyColor)} ambientEquatorColor {C(RenderSettings.ambientEquatorColor)} " +
            $"ambientGroundColor {C(RenderSettings.ambientGroundColor)} ambientLight {C(RenderSettings.ambientLight)} fog {RenderSettings.fog}");
        Put(key + " ambientProbe", Sh(RenderSettings.ambientProbe));
        Put(key + " defaultTextureHDRDecodeValues", V(ReflectionProbe.defaultTextureHDRDecodeValues));
        Put(key + " activeColorSpace", QualitySettings.activeColorSpace.ToString());
    }

    void Capture(string key)
    {
        RecordSettings(key);
        Dump(key);
        Sphere(key);
    }

    static Camera MakeCamera(string name, int layer, Vector3 position)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.SetPositionAndRotation(position, Quaternion.identity);
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.cullingMask = 1 << layer;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        camera.renderingPath = RenderingPath.Forward;
        return camera;
    }

    void Setup()
    {
        var dumpAt = new Vector3(200f, 100f, 100f);
        var sphereAt = new Vector3(100f, 100f, 100f);
        m_DumpCamera = MakeCamera("Dump Camera", DumpLayer, dumpAt);
        m_SphereCamera = MakeCamera("Sphere Camera", SphereLayer, sphereAt);
        m_Dump = new RenderTexture(DumpWidth, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        m_Bytes = new RenderTexture(SphereSize, SphereSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        m_Floats = new RenderTexture(SphereSize, SphereSize, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Dump Quad";
        quad.layer = DumpLayer;
        quad.transform.SetPositionAndRotation(dumpAt + new Vector3(0f, 0f, 3f), Quaternion.identity);
        quad.transform.localScale = new Vector3(100f, 10f, 1f);
        var quadRenderer = quad.GetComponent<MeshRenderer>();
        quadRenderer.sharedMaterial = new Material(Resources.Load<Shader>("IndirectSpecDump"));
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.lightProbeUsage = LightProbeUsage.Off;

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Standard Sphere";
        sphere.layer = SphereLayer;
        sphere.transform.SetPositionAndRotation(sphereAt + new Vector3(0f, 0f, 3f), Quaternion.identity);
        sphere.transform.localScale = Vector3.one * 1.6f;
        var material = new Material(Shader.Find("Standard")) { color = Color.white };
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetFloat("_GlossyReflections", 0f);
        material.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
        var block = new MaterialPropertyBlock();
        block.CopySHCoefficientArraysFrom(new[] { new SphericalHarmonicsL2() });
        renderer.SetPropertyBlock(block);
        Put("standard material", $"{material.shader.name} keywords {string.Join(" ", material.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal))} " +
            $"color {C(material.color)} metallic {F(material.GetFloat("_Metallic"))} glossiness {F(material.GetFloat("_Glossiness"))}");
        Put("custom SH", Sh(new SphericalHarmonicsL2()));
    }

    static Color[] Read(RenderTexture target, int width, int height, TextureFormat format)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(width, height, format, false, true);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        var pixels = texture.GetPixels();
        Destroy(texture);
        return pixels;
    }

    void Dump(string key)
    {
        m_DumpCamera.targetTexture = m_Dump;
        m_DumpCamera.Render();
        m_DumpCamera.targetTexture = null;
        var pixels = Read(m_Dump, DumpWidth, 1, TextureFormat.RGBAFloat);
        for (int i = 0; i < Slots.Length; i++) Put($"{key} dump {Slots[i]}", C(pixels[i]));
    }

    void Sphere(string key)
    {
        m_SphereCamera.targetTexture = m_Bytes;
        m_SphereCamera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = m_Bytes;
        var bytes = new Texture2D(SphereSize, SphereSize, TextureFormat.RGBA32, false, true);
        bytes.ReadPixels(new Rect(0, 0, SphereSize, SphereSize), 0, 0);
        bytes.Apply();
        RenderTexture.active = previous;
        var pixels32 = bytes.GetPixels32();
        Destroy(bytes);
        var centre = pixels32[SphereSize / 2 * SphereSize + SphereSize / 2];
        Put($"{key} standard bytes centre", $"{centre.r} {centre.g} {centre.b} {centre.a}");
        Put($"{key} standard bytes histogram", Histogram(pixels32.Select(p => $"{p.r} {p.g} {p.b} {p.a}")));

        m_SphereCamera.targetTexture = m_Floats;
        m_SphereCamera.Render();
        m_SphereCamera.targetTexture = null;
        var floats = Read(m_Floats, SphereSize, SphereSize, TextureFormat.RGBAFloat);
        Put($"{key} standard floats centre", C(floats[SphereSize / 2 * SphereSize + SphereSize / 2]));
        Put($"{key} standard floats histogram", Histogram(floats.Select(C)));
    }

    /// <summary>Every distinct value with its pixel count, most frequent first (ties by value).</summary>
    static string Histogram(IEnumerable<string> values) =>
        string.Join("; ", values.GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()}x {g.Key}"));
}
