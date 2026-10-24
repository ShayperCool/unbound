using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Renders the main camera into a 320x180 render texture, reads it back and writes it to
/// <c>SHADERGRAPH_PROBE_OUT</c> (a PNG; with <c>SHADERGRAPH_PROBE_JSON</c> also sample pixels), then quits.</summary>
public class ShaderGraphProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ShaderGraphProbe").AddComponent<ShaderGraphProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        Record(Environment.GetEnvironmentVariable("SHADERGRAPH_PROBE_OUT"), Environment.GetEnvironmentVariable("SHADERGRAPH_PROBE_JSON"));
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

    /// <summary>Renders the main camera (twice: the first render may still compile) and writes
    /// the image and the constants URP set.</summary>
    static void Record(string output, string json)
    {
        var camera = Camera.main;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
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
                "_MainLightShadowmapSize", "_MainLightShadowOffset0", "_AdditionalLightsPosition", "_AdditionalLightsColor", "_AdditionalLightsAttenuation", "_AdditionalLightsSpotDir" })
            {
                var v = Shader.GetGlobalVector(name);
                lines.Append("{\"key\":\"").Append(name).Append("\",\"value\":[")
                    .Append(string.Join(",", new[] { v.x, v.y, v.z, v.w }.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))
                    .Append("]}\n");
            }
            foreach (var name in new[] { "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN", "_ADDITIONAL_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX", "_SHADOWS_SOFT", "LIGHTMAP_SHADOW_MIXING", "_FORWARD_PLUS", "FOG_LINEAR" })
                lines.Append("{\"key\":\"keyword ").Append(name).Append("\",\"value\":").Append(Shader.IsKeywordEnabled(name) ? "true" : "false").Append("}\n");
            File.WriteAllText(json, lines.ToString());
        }
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllBytes(output, texture.EncodeToPNG());
            Debug.Log("shader graph probe written to " + output);
        }
    }
}
