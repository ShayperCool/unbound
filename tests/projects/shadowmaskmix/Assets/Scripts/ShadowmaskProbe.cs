using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The built-in pipeline mixing baked shadow masks with realtime shadows (see README.md): renders
/// the main camera into a 320x180 render texture per case and writes it as a PNG, with what the
/// lighting data gives the scene (each light's baking output, each renderer's lightmap) as keyed
/// values, into SHADOWMASK_OUT (tests/value_probe.py).
/// </summary>
public sealed class ShadowmaskProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHADOWMASK_OUT"))) return;
        new GameObject("ShadowmaskProbe").AddComponent<ShadowmaskProbe>();
    }

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("SHADOWMASK_OUT");
        var camera = Camera.main;
        var target = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        var sun = GameObject.Find("Sun").GetComponent<Light>();
        var point = GameObject.Find("Point Light").GetComponent<Light>();

        QualitySettings.shadows = ShadowQuality.HardOnly;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowCascades = 1;
        QualitySettings.shadowDistance = 12f;
        QualitySettings.shadowProjection = ShadowProjection.StableFit;

        foreach (var light in new[] { sun, point })
        {
            var o = light.bakingOutput;
            Rec("light/" + light.name, "bake=" + o.lightmapBakeType + " mixed=" + o.mixedLightingMode + " channel=" + o.occlusionMaskChannel
                + " probeChannel=" + o.probeOcclusionLightIndex + " baked=" + o.isBaked + " caster=" + light.lightShadowCasterMode);
        }
        foreach (var name in new[] { "Ground", "Near Block", "Near Column", "Far Block", "Far Column", "Near Sphere", "Far Sphere", "Wall", "Shadowed Sphere", "Far Shadowed Sphere" })
        {
            var r = GameObject.Find(name).GetComponent<Renderer>();
            Rec("renderer/" + name, "lightmap=" + r.lightmapIndex + " st=" + F(r.lightmapScaleOffset.x) + "," + F(r.lightmapScaleOffset.y) + "," + F(r.lightmapScaleOffset.z) + "," + F(r.lightmapScaleOffset.w));
        }
        // The probe occlusion the shadowed spheres take (their probe's, interpolated at their center).
        foreach (var name in new[] { "Shadowed Sphere", "Far Shadowed Sphere" })
        {
            var at = new[] { GameObject.Find(name).transform.position };
            var sh = new UnityEngine.Rendering.SphericalHarmonicsL2[1];
            var occlusion = new Vector4[1];
            LightProbes.CalculateInterpolatedLightAndOcclusionProbes(at, sh, occlusion);
            Rec("occlusion/" + name, F(occlusion[0].x) + "," + F(occlusion[0].y) + "," + F(occlusion[0].z) + "," + F(occlusion[0].w));
        }
        var maps = LightmapSettings.lightmaps;
        Rec("lightmaps", maps.Length + " shadowmask=" + (maps.Length > 0 && maps[0].shadowMask != null));

        // Forward, then deferred: each Shadowmask Mode, then the sun's shadow caster modes.
        foreach (var path in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
        {
            camera.renderingPath = path;
            string p = path == RenderingPath.Forward ? "forward" : "deferred";
            foreach (var mode in new[] { ShadowmaskMode.Shadowmask, ShadowmaskMode.DistanceShadowmask })
            {
                QualitySettings.shadowmaskMode = mode;
                string m = mode == ShadowmaskMode.Shadowmask ? "shadowmask" : "distance";
                foreach (var caster in new[] { LightShadowCasterMode.Default, LightShadowCasterMode.NonLightmappedOnly, LightShadowCasterMode.Everything })
                {
                    sun.lightShadowCasterMode = caster;
                    point.lightShadowCasterMode = caster;
                    yield return Shot(p + "_" + m + "_" + caster.ToString().ToLowerInvariant());
                }
            }
        }
        sun.lightShadowCasterMode = LightShadowCasterMode.Default;
        point.lightShadowCasterMode = LightShadowCasterMode.Default;
        camera.renderingPath = RenderingPath.Forward;
        QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
        // Realtime shadows off: the baked masks alone.
        QualitySettings.shadows = ShadowQuality.Disable;
        yield return Shot("forward_shadowmask_noshadows");
        QualitySettings.shadows = ShadowQuality.HardOnly;

        m_Out.Append("{\"k\": \"complete\", \"v\": true}\n");
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    IEnumerator Shot(string name)
    {
        // Three frames for the settings to take, then one render of the camera's own (a batch-mode
        // Editor never reaches the end of a frame).
        for (int i = 0; i < 3; i++) yield return null;
        var camera = Camera.main;
        camera.Render();
        var target = camera.targetTexture;
        var active = RenderTexture.active;
        RenderTexture.active = target;
        var shot = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        shot.Apply();
        RenderTexture.active = active;
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), shot.EncodeToPNG());
        Destroy(shot);
        Rec("shot/" + name, name + ".png");
    }
}
