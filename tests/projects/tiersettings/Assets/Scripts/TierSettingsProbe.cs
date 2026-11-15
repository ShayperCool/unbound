using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Records EditorGraphicsSettings.GetTierSettings for every graphics tier of several
/// platform groups (automatic but for Standalone Tier2, set by hand) and
/// Lightmapping.lightingDataAsset of a scene naming the built-in lighting data and of one naming
/// none. Writes values.jsonl into TIER_SETTINGS_OUT and exits.</summary>
public sealed class TierSettingsProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("TierSettingsProbe").AddComponent<TierSettingsProbe>();

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

#if UNITY_EDITOR
    static string Describe(UnityEditor.Rendering.TierSettings t) =>
        $"quality {t.standardShaderQuality} hdrMode {t.hdrMode} box {t.reflectionProbeBoxProjection} blending {t.reflectionProbeBlending} hdr {t.hdr} " +
        $"detailNormal {t.detailNormalMap} cascades {t.cascadedShadowMaps} shadow32 {t.prefer32BitShadowMaps} lppv {t.enableLPPV} " +
        $"semitransparent {t.semitransparentShadows} path {t.renderingPath} gi {t.realtimeGICPUUsage}";
#endif

    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        yield return null;
        int code = 0;
        try
        {
#if UNITY_EDITOR
            var groups = new[]
            {
                UnityEditor.BuildTargetGroup.Standalone, UnityEditor.BuildTargetGroup.iOS, UnityEditor.BuildTargetGroup.Android,
                UnityEditor.BuildTargetGroup.WebGL, UnityEditor.BuildTargetGroup.WSA, UnityEditor.BuildTargetGroup.tvOS,
            };
            foreach (var group in groups)
                foreach (var tier in new[] { GraphicsTier.Tier1, GraphicsTier.Tier2, GraphicsTier.Tier3 })
                    Rec($"group {(int)group} {tier}", Describe(UnityEditor.Rendering.EditorGraphicsSettings.GetTierSettings(group, tier)));
            Rec("named Standalone Tier3", Describe(UnityEditor.Rendering.EditorGraphicsSettings.GetTierSettings(UnityEditor.Build.NamedBuildTarget.Standalone, GraphicsTier.Tier3)));
            Rec("named Android Tier1", Describe(UnityEditor.Rendering.EditorGraphicsSettings.GetTierSettings(UnityEditor.Build.NamedBuildTarget.Android, GraphicsTier.Tier1)));
            Rec("lightingDataAsset", LightingData());
#endif
            // A scene whose lighting settings name no lighting data.
            UnityEngine.SceneManagement.SceneManager.LoadScene("NoData");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        yield return null;
        var dir = Environment.GetEnvironmentVariable("TIER_SETTINGS_OUT");
        try
        {
#if UNITY_EDITOR
            Rec("NoData lightingDataAsset", LightingData());
#endif
            // Probe loaded beside it: the active scene's (NoData's) asset still; then Probe's once
            // Probe is the active scene.
            UnityEngine.SceneManagement.SceneManager.LoadScene("Probe", UnityEngine.SceneManagement.LoadSceneMode.Additive);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        yield return null;
        try
        {
#if UNITY_EDITOR
            Rec("additive Probe lightingDataAsset", LightingData());
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(UnityEngine.SceneManagement.SceneManager.GetSceneByName("Probe"));
            Rec("active Probe lightingDataAsset", LightingData());
#endif
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
            else Debug.Log(m_Out.ToString());
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

#if UNITY_EDITOR
    static string LightingData()
    {
        var data = UnityEditor.Lightmapping.lightingDataAsset;
        return data == null ? "null" : data.name;
    }
#endif
}
