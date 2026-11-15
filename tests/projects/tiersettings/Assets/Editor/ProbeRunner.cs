using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authors the probe's explicit tier settings (<see cref="Create"/>) and records it
/// (<see cref="Record"/>).</summary>
public static class ProbeRunner
{
    /// <summary>Standalone Tier2 set by hand: every field away from the automatic value.</summary>
    public static void Create()
    {
        var settings = EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Standalone, GraphicsTier.Tier2);
        settings.standardShaderQuality = ShaderQuality.Low;
        settings.hdrMode = CameraHDRMode.R11G11B10;
        settings.reflectionProbeBoxProjection = false;
        settings.reflectionProbeBlending = false;
        settings.hdr = false;
        settings.detailNormalMap = false;
        settings.cascadedShadowMaps = false;
        settings.prefer32BitShadowMaps = true;
        settings.enableLPPV = false;
        settings.semitransparentShadows = false;
        settings.renderingPath = RenderingPath.VertexLit;
        settings.realtimeGICPUUsage = RealtimeGICPUUsage.High;
        EditorGraphicsSettings.SetTierSettings(BuildTargetGroup.Standalone, GraphicsTier.Tier2, settings);
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
