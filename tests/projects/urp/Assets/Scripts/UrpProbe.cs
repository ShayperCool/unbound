using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Renders the main camera into a 320x180 render texture, reads it back and writes it to
/// <c>URP_PROBE_OUT</c> (a PNG; with <c>URP_PROBE_JSON</c> also sample pixels), then quits.</summary>
public class UrpProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("UrpProbe").AddComponent<UrpProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        var output = Environment.GetEnvironmentVariable("URP_PROBE_OUT");
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
        var json = Environment.GetEnvironmentVariable("URP_PROBE_JSON");
        if (!string.IsNullOrEmpty(json))
        {
            // The lighting constants URP set for the frame.
            var lines = new System.Text.StringBuilder();
            foreach (var name in new[] { "_MainLightPosition", "_MainLightColor", "_AdditionalLightsCount", "_GlossyEnvironmentColor",
                "_GlossyEnvironmentCubeMap_HDR", "_AmbientOcclusionParam", "_ScaledScreenParams", "_WorldSpaceCameraPos", "_ProjectionParams",
                "_ZBufferParams", "unity_OrthoParams", "_MainLightShadowParams", "_ShadowBias", "_TimeParameters", "unity_FogColor", "unity_FogParams" })
            {
                var v = Shader.GetGlobalVector(name);
                lines.Append("{\"key\":\"").Append(name).Append("\",\"value\":[")
                    .Append(string.Join(",", new[] { v.x, v.y, v.z, v.w }.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))
                    .Append("]}\n");
            }
            foreach (var name in new[] { "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHTS_VERTEX", "_SHADOWS_SOFT", "LIGHTMAP_SHADOW_MIXING", "_FORWARD_PLUS", "FOG_LINEAR" })
                lines.Append("{\"key\":\"keyword ").Append(name).Append("\",\"value\":").Append(Shader.IsKeywordEnabled(name) ? "true" : "false").Append("}\n");
            File.WriteAllText(json, lines.ToString());
        }
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllBytes(output, texture.EncodeToPNG());
            Debug.Log("urp probe written to " + output);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
