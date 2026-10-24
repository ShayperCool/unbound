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
        // URP_PROBE_DIR: every scenario into <dir>/<name>.png (and its constants, .jsonl).
        var dir = Environment.GetEnvironmentVariable("URP_PROBE_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            var light = FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            ShadowMatrixProbe.Record(Path.Combine(dir, "shadow-matrices.jsonl"));
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
            Quit();
            return;
        }
        Record(Environment.GetEnvironmentVariable("URP_PROBE_OUT"), Environment.GetEnvironmentVariable("URP_PROBE_JSON"));
        Quit();
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
            Debug.Log("urp probe written to " + output);
        }
    }
}
