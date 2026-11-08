using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// With <c>LIGHTING_ASSETS_OUT</c> set: what the LightingSettings and ShaderVariantCollection APIs
/// return in Play mode, and the frames the lighting settings could change. Plays Plain (no
/// LightingSettings), then Lit (settings changed after its bake), then Realtime (Enlighten realtime
/// GI). Writes <c>values.jsonl</c> and PNGs into the folder, then quits.
/// </summary>
public partial class LightingAssetsProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;
    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LIGHTING_ASSETS_OUT"))) return;
        if (Environment.GetEnvironmentVariable("SHADER_WARMUP_MODE") == "startup") PrepareStartupWarmup();
        var go = new GameObject("LightingAssetsProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<LightingAssetsProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static string Error(Exception e) => "{\"exception\":" + S(e.GetType().FullName) + ",\"message\":" + S(e.Message) + "}";

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return Error(e); }
    }

#pragma warning disable 618
    static readonly (string Name, Func<LightingSettings, string> Get)[] s_Properties =
    {
        ("bakedGI", s => B(s.bakedGI)),
        ("realtimeGI", s => B(s.realtimeGI)),
        ("realtimeEnvironmentLighting", s => B(s.realtimeEnvironmentLighting)),
        ("autoGenerate", s => B(s.autoGenerate)),
        ("mixedBakeMode", s => E(s.mixedBakeMode)),
        ("albedoBoost", s => F(s.albedoBoost)),
        ("indirectScale", s => F(s.indirectScale)),
        ("lightmapper", s => E(s.lightmapper)),
        ("lightmapMaxSize", s => s.lightmapMaxSize.ToString()),
        ("lightmapResolution", s => F(s.lightmapResolution)),
        ("lightmapPadding", s => s.lightmapPadding.ToString()),
        ("lightmapCompression", s => E(s.lightmapCompression)),
        ("compressLightmaps", s => B(s.compressLightmaps)),
        ("ao", s => B(s.ao)),
        ("aoMaxDistance", s => F(s.aoMaxDistance)),
        ("aoExponentIndirect", s => F(s.aoExponentIndirect)),
        ("aoExponentDirect", s => F(s.aoExponentDirect)),
        ("extractAO", s => B(s.extractAO)),
        ("directionalityMode", s => E(s.directionalityMode)),
        ("exportTrainingData", s => B(s.exportTrainingData)),
        ("trainingDataDestination", s => S(s.trainingDataDestination)),
        ("indirectResolution", s => F(s.indirectResolution)),
        ("sampling", s => E(s.sampling)),
        ("directSampleCount", s => s.directSampleCount.ToString()),
        ("indirectSampleCount", s => s.indirectSampleCount.ToString()),
        ("bounces", s => s.bounces.ToString()),
        ("maxBounces", s => s.maxBounces.ToString()),
        ("russianRouletteStartBounce", s => s.russianRouletteStartBounce.ToString()),
        ("minBounces", s => s.minBounces.ToString()),
        ("prioritizeView", s => B(s.prioritizeView)),
        ("filteringMode", s => E(s.filteringMode)),
        ("denoiserTypeDirect", s => E(s.denoiserTypeDirect)),
        ("denoiserTypeIndirect", s => E(s.denoiserTypeIndirect)),
        ("denoiserTypeAO", s => E(s.denoiserTypeAO)),
        ("filterTypeDirect", s => E(s.filterTypeDirect)),
        ("filterTypeIndirect", s => E(s.filterTypeIndirect)),
        ("filterTypeAO", s => E(s.filterTypeAO)),
        ("filteringGaussianRadiusDirect", s => F(s.filteringGaussianRadiusDirect)),
        ("filteringGaussianRadiusIndirect", s => F(s.filteringGaussianRadiusIndirect)),
        ("filteringGaussianRadiusAO", s => F(s.filteringGaussianRadiusAO)),
        ("filteringGaussRadiusDirect", s => s.filteringGaussRadiusDirect.ToString()),
        ("filteringGaussRadiusIndirect", s => s.filteringGaussRadiusIndirect.ToString()),
        ("filteringGaussRadiusAO", s => s.filteringGaussRadiusAO.ToString()),
        ("filteringAtrousPositionSigmaDirect", s => F(s.filteringAtrousPositionSigmaDirect)),
        ("filteringAtrousPositionSigmaIndirect", s => F(s.filteringAtrousPositionSigmaIndirect)),
        ("filteringAtrousPositionSigmaAO", s => F(s.filteringAtrousPositionSigmaAO)),
        ("environmentImportanceSampling", s => B(s.environmentImportanceSampling)),
        ("environmentSampleCount", s => s.environmentSampleCount.ToString()),
        ("lightProbeSampleCountMultiplier", s => F(s.lightProbeSampleCountMultiplier)),
        ("respectSceneVisibilityWhenBakingGI", s => B(s.respectSceneVisibilityWhenBakingGI)),
    };
#pragma warning restore 618

    static string B(bool b) => b ? "true" : "false";
    static string E(System.Enum e) => System.Convert.ToInt32(e).ToString();

    static string Dump(LightingSettings s)
    {
        if (ReferenceEquals(s, null)) return "null";
        if (s == null) return "\"destroyed\"";
        var parts = new List<string> { "\"name\":" + S(s.name) };
        foreach (var (name, get) in s_Properties) parts.Add(S(name) + ":" + Try(() => get(s)));
        return "{" + string.Join(",", parts) + "}";
    }

    static string Name(UnityEngine.Object o) => ReferenceEquals(o, null) ? "null" : o == null ? "\"destroyed\"" : S(o.name);

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("LIGHTING_ASSETS_OUT");
        Directory.CreateDirectory(m_Dir);
        yield return null;
        yield return null;
        var coldMode = Environment.GetEnvironmentVariable("SHADER_WARMUP_MODE");
        if (coldMode == "state")
        {
            RecordShaderWarmup();
            Finish();
            yield break;
        }
        if (!string.IsNullOrEmpty(coldMode))
        {
            var cold = RecordColdShaderWarmup(coldMode);
            while (cold.MoveNext()) yield return cold.Current;
            Finish();
            yield break;
        }
        var holder = FindAnyObjectByType<LightingAssetsHolder>();

        RecordSceneApi("plain");
#if UNITY_EDITOR
        var defaults = Lightmapping.lightingSettingsDefaults;
        Record("defaults", "\"same\":" + B(ReferenceEquals(defaults, Lightmapping.lightingSettingsDefaults)) + ",\"settings\":" + Dump(defaults));
#endif
        Record("new", "\"settings\":" + Dump(new LightingSettings()));
        Record("custom", "\"settings\":" + Dump(holder.custom));
        Record("custom-clone", "\"settings\":" + Try(() => Dump(Instantiate(holder.custom))));
        RecordSetters();
        RecordLightmaps("plain");
#if UNITY_EDITOR
        Record("plain-set-baked", "\"result\":" + Try(() => { Lightmapping.bakedGI = false; return B(Lightmapping.bakedGI); }));
        RecordSceneApi("plain-after-set-baked");
        Record("plain-set-for-scene", "\"result\":" + Try(() => { Lightmapping.SetLightingSettingsForScene(SceneManager.GetActiveScene(), holder.custom); return "\"ok\""; }));
        RecordSceneApi("plain-after-set-for-scene");
        Record("plain-set-for-scene-null", "\"result\":" + Try(() => { Lightmapping.SetLightingSettingsForScene(SceneManager.GetActiveScene(), null); return "\"ok\""; }));
        RecordSceneApi("plain-after-set-for-scene-null");
#endif

        RecordCollections(holder);
        RecordVariantSets();

        SceneManager.LoadScene("Lit");
        yield return null;
        yield return null;
        yield return null;
        RecordSceneApi("lit");
        RecordLightmaps("lit");
        Capture("lit-authored");
#if UNITY_EDITOR
        var lit = Lightmapping.lightingSettings;
        lit.bakedGI = true;
        lit.realtimeGI = false;
        lit.realtimeEnvironmentLighting = true;
        lit.mixedBakeMode = MixedLightingMode.Shadowmask;
        lit.directionalityMode = LightmapsMode.NonDirectional;
        lit.lightmapCompression = LightmapCompression.None;
        lit.indirectScale = 0.25f;
        lit.albedoBoost = 1f;
        yield return null;
        yield return null;
        RecordLightmaps("lit-edited");
        Capture("lit-edited");
        Lightmapping.lightingSettings = holder.custom;
        yield return null;
        yield return null;
        RecordSceneApi("lit-assigned");
        RecordLightmaps("lit-assigned");
        Capture("lit-assigned");
        Record("lit-assign-null", "\"result\":" + Try(() => { Lightmapping.lightingSettings = null; return "\"ok\""; }));
        RecordSceneApi("lit-after-null");
#endif

        SceneManager.LoadScene("Realtime");
        yield return null;
        RecordSceneApi("realtime");
        RecordLightmaps("realtime");
        for (int i = 0; i < 60; i++) yield return null;
        Capture("realtime-red");
        var point = GameObject.Find("Point Light").GetComponent<Light>();
        point.color = new Color(0.1f, 0.3f, 1f);
        for (int i = 0; i < 60; i++) yield return null;
        Capture("realtime-blue");
        Record("realtime-gi", "\"converged\":" + B(DynamicGI.isConverged));

        SceneManager.LoadScene("RealtimeOff");
        yield return null;
        RecordSceneApi("realtimeoff");
        RecordLightmaps("realtimeoff");
        for (int i = 0; i < 60; i++) yield return null;
        Capture("realtimeoff-red");

        RecordShaderWarmup();
        Finish();
    }

    void Finish()
    {
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString() + "{\"case\":\"end\"}\n");
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void RecordSceneApi(string kind)
    {
        var parts = new List<string>();
#if UNITY_EDITOR
        bool found = Lightmapping.TryGetLightingSettings(out var tried);
        parts.Add("\"try\":" + B(found));
        parts.Add("\"tried\":" + Name(tried));
        parts.Add("\"get\":" + Try(() => Name(Lightmapping.lightingSettings)));
        parts.Add("\"forScene\":" + Try(() => Name(Lightmapping.GetLightingSettingsForScene(SceneManager.GetActiveScene()))));
        parts.Add("\"same\":" + Try(() => B(ReferenceEquals(Lightmapping.lightingSettings, Lightmapping.GetLightingSettingsForScene(SceneManager.GetActiveScene())))));
        parts.Add("\"bakedGI\":" + Try(() => B(Lightmapping.bakedGI)));
        parts.Add("\"realtimeGI\":" + Try(() => B(Lightmapping.realtimeGI)));
        parts.Add("\"settings\":" + (found ? Dump(tried) : "null"));
#endif
        Record(kind + "-api", string.Join(",", parts));
    }

    void RecordLightmaps(string kind)
    {
        var parts = new List<string>
        {
            "\"lightmaps\":" + LightmapSettings.lightmaps.Length,
            "\"mode\":" + E(LightmapSettings.lightmapsMode),
            "\"dir\":" + B(LightmapSettings.lightmaps.Any(l => l.lightmapDir != null)),
        };
        var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).OrderBy(r => r.name).ThenBy(r => r.transform.position.x);
        parts.Add("\"renderers\":[" + string.Join(",", renderers.Select(r =>
            "[" + S(r.name) + "," + r.lightmapIndex + "," + r.realtimeLightmapIndex + "]")) + "]");
        var lights = FindObjectsByType<Light>(FindObjectsSortMode.None).OrderBy(l => l.name);
        parts.Add("\"lights\":[" + string.Join(",", lights.Select(l =>
            "[" + S(l.name) + "," + E(l.bakingOutput.lightmapBakeType) + "," + E(l.bakingOutput.mixedLightingMode) + "," + B(l.bakingOutput.isBaked) + "]")) + "]");
        Record(kind + "-lightmaps", string.Join(",", parts));
    }

    void RecordSetters()
    {
        void Set(string name, Action<LightingSettings> set, Func<LightingSettings, string> get)
        {
            var s = new LightingSettings();
            Record("set", "\"name\":" + S(name) + ",\"result\":" + Try(() => { set(s); return S(get(s)); }));
        }
        Set("lightmapMaxSize 100", s => s.lightmapMaxSize = 100, s => s.lightmapMaxSize.ToString());
        Set("lightmapMaxSize 8192", s => s.lightmapMaxSize = 8192, s => s.lightmapMaxSize.ToString());
        Set("lightmapMaxSize 0", s => s.lightmapMaxSize = 0, s => s.lightmapMaxSize.ToString());
        Set("lightmapMaxSize 80", s => s.lightmapMaxSize = 80, s => s.lightmapMaxSize.ToString());
        Set("lightmapMaxSize 96", s => s.lightmapMaxSize = 96, s => s.lightmapMaxSize.ToString());
        Set("lightmapMaxSize 3000", s => s.lightmapMaxSize = 3000, s => s.lightmapMaxSize.ToString());
        Set("lightmapMaxSize -5", s => s.lightmapMaxSize = -5, s => s.lightmapMaxSize.ToString());
        Set("lightmapPadding 1", s => s.lightmapPadding = 1, s => s.lightmapPadding.ToString());
        Set("lightmapPadding 0", s => s.lightmapPadding = 0, s => s.lightmapPadding.ToString());
        Set("lightmapPadding 300", s => s.lightmapPadding = 300, s => s.lightmapPadding.ToString());
        Set("lightmapResolution 50000", s => s.lightmapResolution = 50000f, s => F(s.lightmapResolution));
        Set("indirectResolution -1", s => s.indirectResolution = -1f, s => F(s.indirectResolution));
        Set("indirectScale 100", s => s.indirectScale = 100f, s => F(s.indirectScale));
        Set("minBounces 9 under max 20", s => { s.maxBounces = 20; s.minBounces = 9; }, s => s.minBounces + " " + s.maxBounces);
        Set("maxBounces 1 under min 2", s => s.maxBounces = 1, s => s.minBounces + " " + s.maxBounces);
        Set("minBounces -4", s => s.minBounces = -4, s => s.minBounces.ToString());
        Set("directSampleCount 48", s => s.directSampleCount = 48, s => s.directSampleCount.ToString());
        Set("directSampleCount 47", s => s.directSampleCount = 47, s => s.directSampleCount.ToString());
        Set("directSampleCount 100000", s => s.directSampleCount = 100000, s => s.directSampleCount.ToString());
        Set("indirectSampleCount 0", s => s.indirectSampleCount = 0, s => s.indirectSampleCount.ToString());
        Set("indirectSampleCount 300", s => s.indirectSampleCount = 300, s => s.indirectSampleCount.ToString());
        Set("indirectSampleCount 100000", s => s.indirectSampleCount = 100000, s => s.indirectSampleCount.ToString());
        Set("environmentSampleCount 0", s => s.environmentSampleCount = 0, s => s.environmentSampleCount.ToString());
        Set("environmentSampleCount 200", s => s.environmentSampleCount = 200, s => s.environmentSampleCount.ToString());
        Set("environmentSampleCount 100000", s => s.environmentSampleCount = 100000, s => s.environmentSampleCount.ToString());
        Set("aoExponentIndirect 20", s => s.aoExponentIndirect = 20f, s => F(s.aoExponentIndirect));
        Set("aoExponentIndirect -1", s => s.aoExponentIndirect = -1f, s => F(s.aoExponentIndirect));
        Set("aoExponentDirect -1", s => s.aoExponentDirect = -1f, s => F(s.aoExponentDirect));
        Set("lightProbeSampleCountMultiplier 100", s => s.lightProbeSampleCountMultiplier = 100f, s => F(s.lightProbeSampleCountMultiplier));
        Set("mixedBakeMode -1", s => s.mixedBakeMode = (MixedLightingMode)(-1), s => E(s.mixedBakeMode));
        Set("mixedBakeMode 3", s => s.mixedBakeMode = (MixedLightingMode)3, s => E(s.mixedBakeMode));
        Set("mixedBakeMode Subtractive", s => s.mixedBakeMode = MixedLightingMode.Subtractive, s => E(s.mixedBakeMode));
        Set("directionalityMode 2", s => s.directionalityMode = (LightmapsMode)2, s => E(s.directionalityMode));
        Set("directionalityMode -1", s => s.directionalityMode = (LightmapsMode)(-1), s => E(s.directionalityMode));
        Set("lightmapCompression 9", s => s.lightmapCompression = (LightmapCompression)9, s => E(s.lightmapCompression));
        Set("sampling 4", s => s.sampling = (LightingSettings.Sampling)4, s => E(s.sampling));
        Set("filteringMode 7", s => s.filteringMode = (LightingSettings.FilterMode)7, s => E(s.filteringMode));
        Set("denoiserTypeDirect 9", s => s.denoiserTypeDirect = (LightingSettings.DenoiserType)9, s => E(s.denoiserTypeDirect));
        Set("filterTypeDirect 9", s => s.filterTypeDirect = (LightingSettings.FilterType)9, s => E(s.filterTypeDirect));
        Set("filteringGaussianRadiusDirect 9", s => s.filteringGaussianRadiusDirect = 9f, s => F(s.filteringGaussianRadiusDirect));
        Set("filteringGaussianRadiusAO -1", s => s.filteringGaussianRadiusAO = -1f, s => F(s.filteringGaussianRadiusAO));
        Set("filteringAtrousPositionSigmaDirect 9", s => s.filteringAtrousPositionSigmaDirect = 9f, s => F(s.filteringAtrousPositionSigmaDirect));
        Set("filteringAtrousPositionSigmaAO -1", s => s.filteringAtrousPositionSigmaAO = -1f, s => F(s.filteringAtrousPositionSigmaAO));
        Set("aoMaxDistance 5000", s => s.aoMaxDistance = 5000f, s => F(s.aoMaxDistance));
        Set("albedoBoost NaN", s => s.albedoBoost = float.NaN, s => F(s.albedoBoost));
        Set("lightmapResolution NaN", s => s.lightmapResolution = float.NaN, s => F(s.lightmapResolution));
        foreach (bool baked in new[] { false, true })
            foreach (bool realtime in new[] { false, true })
                foreach (bool environment in new[] { false, true })
                    Set($"environment baked {baked} realtime {realtime} set {environment}",
                        s => { s.bakedGI = baked; s.realtimeGI = realtime; s.realtimeEnvironmentLighting = environment; },
                        s => B(s.realtimeEnvironmentLighting));
        Set("environment set then realtime off", s => { s.bakedGI = true; s.realtimeGI = true; s.realtimeEnvironmentLighting = true; s.realtimeGI = false; },
            s => B(s.realtimeEnvironmentLighting));
        Set("environment set then realtime on again", s => { s.bakedGI = true; s.realtimeGI = true; s.realtimeEnvironmentLighting = true; s.realtimeGI = false; s.realtimeGI = true; },
            s => B(s.realtimeEnvironmentLighting));
        Set("lightmapResolution -1", s => s.lightmapResolution = -1f, s => F(s.lightmapResolution));
        Set("lightmapPadding -3", s => s.lightmapPadding = -3, s => s.lightmapPadding.ToString());
        Set("indirectScale -2", s => s.indirectScale = -2f, s => F(s.indirectScale));
        Set("albedoBoost 0.25", s => s.albedoBoost = 0.25f, s => F(s.albedoBoost));
        Set("albedoBoost 50", s => s.albedoBoost = 50f, s => F(s.albedoBoost));
        Set("maxBounces -1", s => s.maxBounces = -1, s => s.maxBounces.ToString());
        Set("maxBounces 1000", s => s.maxBounces = 1000, s => s.maxBounces.ToString());
        Set("minBounces 9 over max 2", s => { s.maxBounces = 2; s.minBounces = 9; }, s => s.minBounces + " " + s.maxBounces);
        Set("directSampleCount 0", s => s.directSampleCount = 0, s => s.directSampleCount.ToString());
        Set("aoMaxDistance -1", s => s.aoMaxDistance = -1f, s => F(s.aoMaxDistance));
        Set("aoExponentDirect 20", s => s.aoExponentDirect = 20f, s => F(s.aoExponentDirect));
        Set("lightProbeSampleCountMultiplier 0", s => s.lightProbeSampleCountMultiplier = 0f, s => F(s.lightProbeSampleCountMultiplier));
        Set("mixedBakeMode 7", s => s.mixedBakeMode = (MixedLightingMode)7, s => ((int)s.mixedBakeMode).ToString());
        Set("lightmapper Enlighten", s => s.lightmapper = (LightingSettings.Lightmapper)0, s => E(s.lightmapper));
        Set("lightmapper 5", s => s.lightmapper = (LightingSettings.Lightmapper)5, s => E(s.lightmapper));
        Set("directionalityMode 5", s => s.directionalityMode = (LightmapsMode)5, s => ((int)s.directionalityMode).ToString());
#pragma warning disable 618
        Set("compressLightmaps false", s => s.compressLightmaps = false, s => E(s.lightmapCompression));
        Set("compressLightmaps true", s => { s.lightmapCompression = LightmapCompression.None; s.compressLightmaps = true; }, s => E(s.lightmapCompression));
        Set("bounces 3", s => s.bounces = 3, s => s.maxBounces.ToString());
        Set("autoGenerate true", s => s.autoGenerate = true, s => B(s.autoGenerate));
        Set("filteringGaussRadiusDirect 3", s => s.filteringGaussRadiusDirect = 3, s => F(s.filteringGaussianRadiusDirect));
#pragma warning restore 618
        Set("filteringGaussianRadiusDirect 2.7", s => s.filteringGaussianRadiusDirect = 2.7f, s => F(s.filteringGaussianRadiusDirect) + " " + Try(() => {
#pragma warning disable 618
            return s.filteringGaussRadiusDirect.ToString();
#pragma warning restore 618
        }));
        Set("trainingDataDestination null", s => {
#pragma warning disable 618
            s.trainingDataDestination = null;
        }, s => S(s.trainingDataDestination));
#pragma warning restore 618
    }

    // ---------------------------------------------------------------- ShaderVariantCollection

    static string Describe(ShaderVariantCollection c) => ReferenceEquals(c, null) ? "null" :
        "{\"name\":" + S(c.name) + ",\"shaders\":" + c.shaderCount + ",\"variants\":" + c.variantCount +
        ",\"warmed\":" + c.warmedUpVariantCount + ",\"isWarmedUp\":" + B(c.isWarmedUp) + "}";

    static string Variant(ShaderVariantCollection.ShaderVariant v) =>
        "{\"shader\":" + Name(v.shader) + ",\"pass\":" + E(v.passType) + ",\"keywords\":" +
        (v.keywords == null ? "null" : "[" + string.Join(",", v.keywords.Select(S)) + "]") + "}";

    void RecordCollections(LightingAssetsHolder holder)
    {
        var variants = holder.variants;
        var standard = Shader.Find("Standard");
        var unlit = Shader.Find("Unlit/Color");

        // The constructor's checks.
        void Make(string name, Func<ShaderVariantCollection.ShaderVariant> make) =>
            Record("variant", "\"name\":" + S(name) + ",\"result\":" + Try(() => Variant(make())));
        Make("plain none", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal));
        Make("plain PLAIN_ON RED_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "PLAIN_ON", "RED_ON"));
        Make("plain RED_ON PLAIN_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "RED_ON", "PLAIN_ON"));
        Make("plain one string", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "RED_ON PLAIN_ON"));
        Make("plain duplicate", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "RED_ON", "RED_ON"));
        Make("plain undeclared", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "NOPE"));
        Make("plain other pass keyword", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "CASTER_ON"));
        Make("plain empty keyword", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, ""));
        Make("plain null keywords", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, null));
        Make("forward none", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase));
        Make("forward DIRECTIONAL", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "DIRECTIONAL"));
        Make("forward RED_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "RED_ON"));
        Make("forward DIRECTIONAL BLUE_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "DIRECTIONAL", "BLUE_ON"));
        Make("forward DIRECTIONAL LIGHTMAP_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "DIRECTIONAL", "LIGHTMAP_ON"));
        Make("forward PLAIN_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "PLAIN_ON"));
        Make("caster SHADOWS_DEPTH", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ShadowCaster, "SHADOWS_DEPTH"));
        Make("caster none", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ShadowCaster));
        Make("deferred", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Deferred));
        Make("forwardadd", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardAdd, "POINT"));
        Make("meta", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Meta));
        Make("null shader", () => new ShaderVariantCollection.ShaderVariant(null, PassType.Normal));
        Make("standard forward DIRECTIONAL", () => new ShaderVariantCollection.ShaderVariant(standard, PassType.ForwardBase, "DIRECTIONAL"));
        Make("standard forward none", () => new ShaderVariantCollection.ShaderVariant(standard, PassType.ForwardBase));
        Make("standard deferred", () => new ShaderVariantCollection.ShaderVariant(standard, PassType.Deferred));
        Make("standard meta", () => new ShaderVariantCollection.ShaderVariant(standard, PassType.Meta));
        Make("standard forward _NORMALMAP", () => new ShaderVariantCollection.ShaderVariant(standard, PassType.ForwardBase, "DIRECTIONAL", "_NORMALMAP"));
        Make("plain ONE_A ONE_B", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "ONE_A", "ONE_B"));
        Make("plain ONE_A RED_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "ONE_A", "RED_ON"));
        Make("plain ONE_B ONE_B", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "ONE_B", "ONE_B"));
        Make("forward DIRECTIONAL RED_ON BLUE_ON", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "DIRECTIONAL", "RED_ON", "BLUE_ON"));
        Make("forward DIRECTIONAL SHADOWS_SCREEN LIGHTPROBE_SH", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "DIRECTIONAL", "SHADOWS_SCREEN", "LIGHTPROBE_SH"));
        Make("forward LIGHTMAP_ON LIGHTPROBE_SH", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "LIGHTMAP_ON", "LIGHTPROBE_SH"));
        Make("forward POINT", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "POINT"));
        Make("caster SHADOWS_DEPTH SHADOWS_CUBE", () => new ShaderVariantCollection.ShaderVariant(variants, PassType.ShadowCaster, "SHADOWS_DEPTH", "SHADOWS_CUBE"));
        Make("diffuse caster", () => new ShaderVariantCollection.ShaderVariant(Shader.Find("Legacy Shaders/Diffuse"), PassType.ShadowCaster, "SHADOWS_DEPTH"));
        Make("diffuse vertex", () => new ShaderVariantCollection.ShaderVariant(Shader.Find("Legacy Shaders/Diffuse"), PassType.Vertex));
        Make("diffuse forward", () => new ShaderVariantCollection.ShaderVariant(Shader.Find("Legacy Shaders/Diffuse"), PassType.ForwardBase, "DIRECTIONAL"));
        Make("unlit normal", () => new ShaderVariantCollection.ShaderVariant(unlit, PassType.Normal));
        Make("unlit forward", () => new ShaderVariantCollection.ShaderVariant(unlit, PassType.ForwardBase));
        Make("unlit srp", () => new ShaderVariantCollection.ShaderVariant(unlit, PassType.ScriptableRenderPipeline));
        Make("unlit srp default", () => new ShaderVariantCollection.ShaderVariant(unlit, PassType.ScriptableRenderPipelineDefaultUnlit));

        var v = (Func<Shader, PassType, string[], ShaderVariantCollection.ShaderVariant>)((sh, p, k) =>
            new ShaderVariantCollection.ShaderVariant { shader = sh, passType = p, keywords = k });

        // The authored assets.
        foreach (var asset in new[] { holder.warmup, holder.authored, holder.dupes, holder.missing })
        {
            Record("asset", "\"collection\":" + Describe(asset));
            var probes = new (string, ShaderVariantCollection.ShaderVariant)[]
            {
                ("plain none", v(variants, PassType.Normal, new string[0])),
                ("plain null keywords", v(variants, PassType.Normal, null)),
                ("plain PLAIN_ON", v(variants, PassType.Normal, new[] { "PLAIN_ON" })),
                ("plain RED_ON PLAIN_ON", v(variants, PassType.Normal, new[] { "RED_ON", "PLAIN_ON" })),
                ("plain PLAIN_ON RED_ON", v(variants, PassType.Normal, new[] { "PLAIN_ON", "RED_ON" })),
                ("plain one string", v(variants, PassType.Normal, new[] { "RED_ON PLAIN_ON" })),
                ("plain RED_ON", v(variants, PassType.Normal, new[] { "RED_ON" })),
                ("forward DIRECTIONAL RED_ON", v(variants, PassType.ForwardBase, new[] { "DIRECTIONAL", "RED_ON" })),
                ("forward DIRECTIONAL BLUE_ON", v(variants, PassType.ForwardBase, new[] { "DIRECTIONAL", "BLUE_ON" })),
                ("caster SHADOWS_DEPTH", v(variants, PassType.ShadowCaster, new[] { "SHADOWS_DEPTH" })),
                ("standard DIRECTIONAL", v(standard, PassType.ForwardBase, new[] { "DIRECTIONAL" })),
                ("standard none", v(standard, PassType.Normal, new string[0])),
                ("unlit", v(unlit, PassType.Normal, new string[0])),
                ("null shader", v(null, PassType.Normal, new string[0])),
                ("undeclared", v(variants, PassType.Normal, new[] { "NOPE" })),
                ("plain other pass keyword", v(variants, PassType.Normal, new[] { "CASTER_ON" })),
                ("plain PLAIN_ON NOPE", v(variants, PassType.Normal, new[] { "PLAIN_ON", "NOPE" })),
                ("plain PLAIN_ON PLAIN_ON", v(variants, PassType.Normal, new[] { "PLAIN_ON", "PLAIN_ON" })),
                ("plain ONE_A", v(variants, PassType.Normal, new[] { "ONE_A" })),
                ("forward PLAIN_ON", v(variants, PassType.ForwardBase, new[] { "PLAIN_ON" })),
                ("forward BLUE_ON DIRECTIONAL", v(variants, PassType.ForwardBase, new[] { "BLUE_ON", "DIRECTIONAL" })),
                ("forward RED_ON DIRECTIONAL", v(variants, PassType.ForwardBase, new[] { "RED_ON", "DIRECTIONAL" })),
                ("deferred none", v(variants, PassType.Deferred, new string[0])),
                ("unlit none", v(unlit, PassType.Normal, new string[0])),
            };
            foreach (var (name, probe) in probes)
                Record("contains", "\"collection\":" + S(asset.name) + ",\"name\":" + S(name) + ",\"result\":" + Try(() => B(asset.Contains(probe))));
        }
        var warmup = holder.warmup;
        Record("progressive", "\"count\":2,\"result\":" + Try(() => B(warmup.WarmUpProgressively(2))) + ",\"collection\":" + Describe(warmup));
        Record("progressive", "\"count\":0,\"result\":" + Try(() => B(warmup.WarmUpProgressively(0))) + ",\"collection\":" + Describe(warmup));
        Record("progressive", "\"count\":-1,\"result\":" + Try(() => B(warmup.WarmUpProgressively(-1))) + ",\"collection\":" + Describe(warmup));
        Record("progressive", "\"count\":3,\"result\":" + Try(() => B(warmup.WarmUpProgressively(3))) + ",\"collection\":" + Describe(warmup));
        Record("progressive", "\"count\":100,\"result\":" + Try(() => B(warmup.WarmUpProgressively(100))) + ",\"collection\":" + Describe(warmup));
        Record("progressive", "\"count\":1,\"result\":" + Try(() => B(warmup.WarmUpProgressively(1))) + ",\"collection\":" + Describe(warmup));
        Record("warmup", "\"result\":" + Try(() => { holder.authored.WarmUp(); return "\"ok\""; }) + ",\"collection\":" + Describe(holder.authored));
        Record("warmup", "\"result\":" + Try(() => { holder.missing.WarmUp(); return "\"ok\""; }) + ",\"collection\":" + Describe(holder.missing));
        Record("dupes-remove", "\"result\":" + Try(() => B(holder.dupes.Remove(v(variants, PassType.Normal, new[] { "PLAIN_ON" })))) + ",\"collection\":" + Describe(holder.dupes));
        Record("dupes-remove", "\"result\":" + Try(() => B(holder.dupes.Remove(v(variants, PassType.Normal, new[] { "PLAIN_ON" })))) + ",\"collection\":" + Describe(holder.dupes));
        Record("dupes-add", "\"result\":" + Try(() => B(holder.dupes.Add(v(variants, PassType.Normal, new[] { "RED_ON", "PLAIN_ON" })))) + ",\"collection\":" + Describe(holder.dupes));
        Record("dupes-contains", "\"result\":" + Try(() => B(holder.dupes.Contains(v(variants, PassType.Normal, new[] { "PLAIN_ON", "RED_ON" })))) + ",\"collection\":" + Describe(holder.dupes));
        Record("missing-add", "\"result\":" + Try(() => B(holder.missing.Add(v(unlit, PassType.Normal, new[] { "NOPE" })))) + ",\"collection\":" + Describe(holder.missing));

        // Runtime collections.
        var c = new ShaderVariantCollection();
        Record("runtime", "\"step\":\"new\",\"collection\":" + Describe(c));
        void Op(string step, Func<string> op) => Record("runtime", "\"step\":" + S(step) + ",\"result\":" + Try(op) + ",\"collection\":" + Describe(c));
        Op("add plain PLAIN_ON RED_ON", () => B(c.Add(v(variants, PassType.Normal, new[] { "PLAIN_ON", "RED_ON" }))));
        Op("add again", () => B(c.Add(v(variants, PassType.Normal, new[] { "PLAIN_ON", "RED_ON" }))));
        Op("add reordered", () => B(c.Add(v(variants, PassType.Normal, new[] { "RED_ON", "PLAIN_ON" }))));
        Op("add duplicate keyword", () => B(c.Add(v(variants, PassType.Normal, new[] { "RED_ON", "RED_ON" }))));
        Op("contains RED_ON", () => B(c.Contains(v(variants, PassType.Normal, new[] { "RED_ON" }))));
        Op("add forward", () => B(c.Add(v(variants, PassType.ForwardBase, new[] { "DIRECTIONAL" }))));
        Op("add undeclared", () => B(c.Add(v(variants, PassType.Normal, new[] { "NOPE" }))));
        Op("add missing pass", () => B(c.Add(v(variants, PassType.Deferred, new string[0]))));
        Op("add null keywords", () => B(c.Add(v(unlit, PassType.Normal, null))));
        Op("add null shader", () => B(c.Add(v(null, PassType.Normal, new string[0]))));
        Op("add default", () => B(c.Add(default(ShaderVariantCollection.ShaderVariant))));
        Op("add standard", () => B(c.Add(v(standard, PassType.ForwardBase, new[] { "DIRECTIONAL" }))));
        Op("remove absent", () => B(c.Remove(v(standard, PassType.ForwardBase, new[] { "DIRECTIONAL", "SHADOWS_SCREEN" }))));
        Op("remove reordered", () => B(c.Remove(v(variants, PassType.Normal, new[] { "RED_ON", "PLAIN_ON" }))));
        Op("remove null shader", () => B(c.Remove(v(null, PassType.Normal, new string[0]))));
        Op("warmup", () => { c.WarmUp(); return "\"ok\""; });
        Op("add ONE_A ONE_B", () => B(c.Add(v(variants, PassType.Normal, new[] { "ONE_A", "ONE_B" }))));
        Op("contains ONE_B ONE_A", () => B(c.Contains(v(variants, PassType.Normal, new[] { "ONE_B", "ONE_A" }))));
        Op("add caster keyword on plain", () => B(c.Add(v(variants, PassType.Normal, new[] { "CASTER_ON" }))));
        Op("add after warmup", () => B(c.Add(v(variants, PassType.Normal, new[] { "PLAIN_ON" }))));
        Op("progressive 1", () => B(c.WarmUpProgressively(1)));
        Op("remove standard", () => B(c.Remove(v(standard, PassType.ForwardBase, new[] { "DIRECTIONAL" }))));
        Op("remove last of shader", () => B(c.Remove(v(unlit, PassType.Normal, null))));
        Op("clear", () => { c.Clear(); return "\"ok\""; });
        Op("progressive empty", () => B(c.WarmUpProgressively(1)));
        Record("clone", "\"collection\":" + Try(() => Describe(Instantiate(holder.warmup))));
        Record("clone", "\"collection\":" + Try(() => Describe(Instantiate(holder.authored))));
    }

    void Capture(string name)
    {
        var camera = Camera.main;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), texture.EncodeToPNG());
        Destroy(texture);
        target.Release();
    }
}
