using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Records Mathf.CorrelatedColorTemperatureToRGB over a sweep of temperatures (and the values
/// outside its range), and the final colour a scriptable pipeline's visible lights carry for
/// lights of several colours, intensities and colour temperatures, under each combination of
/// GraphicsSettings.lightsUseLinearIntensity and lightsUseColorTemperature. Writes values.jsonl
/// (floats as the hex of their bits) into LIGHT_COLOR_OUT and exits.
/// </summary>
public sealed class LightColorProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("LightColorProbe").AddComponent<LightColorProbe>();

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string F(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
    static string C(Color c) => F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a);

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("LIGHT_COLOR_OUT");
        try
        {
            Temperatures();
            Lights();
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("light color probe failed: " + e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    void Temperatures()
    {
        var kelvins = new List<float>
        {
            float.NegativeInfinity, -100f, 0f, 500f, 999.9f, 1000f, 1000.1f, 1234.5f, 1500f, 1900f,
            2700f, 3000f, 3500f, 4000f, 4500f, 5000f, 5500f, 6000f, 6500f, 6569.99f, 6570f, 6570.01f,
            6600f, 7000f, 8000f, 9500f, 10000f, 12000f, 15000f, 20000f, 30000f, 39999f, 40000f, 40000.5f,
            50000f, float.PositiveInfinity, float.NaN,
        };
        for (float k = 1000f; k <= 40000f; k += 250f) kelvins.Add(k + 17.25f);
        foreach (float k in kelvins)
            Rec("cct." + F(k), C(Mathf.CorrelatedColorTemperatureToRGB(k)));
    }

    struct Spec
    {
        public string name;
        public Color color;
        public float intensity;
        public bool useTemperature;
        public float temperature;
        public LightType type;
    }

    void Lights()
    {
        var specs = new[]
        {
            new Spec { name = "white4000", color = Color.white, intensity = 35f, useTemperature = true, temperature = 4000f, type = LightType.Directional },
            new Spec { name = "warm4000", color = new Color(0.8f, 0.5f, 0.2f, 0.75f), intensity = 2.5f, useTemperature = true, temperature = 4000f, type = LightType.Point },
            new Spec { name = "warmOff", color = new Color(0.8f, 0.5f, 0.2f, 0.75f), intensity = 2.5f, useTemperature = false, temperature = 4000f, type = LightType.Point },
            new Spec { name = "cool12000", color = new Color(0.3f, 0.9f, 0.03f, 1f), intensity = 1.25f, useTemperature = true, temperature = 12000f, type = LightType.Spot },
            new Spec { name = "dark2000", color = new Color(0.02f, 0.04045f, 0.0405f, 1f), intensity = 7f, useTemperature = true, temperature = 2000f, type = LightType.Point },
            new Spec { name = "hdr6570", color = new Color(1.5f, 0.3f, 2f, 1f), intensity = 0.6f, useTemperature = true, temperature = 6570f, type = LightType.Point },
            new Spec { name = "zero5000", color = new Color(0.4f, 0.6f, 0.8f, 1f), intensity = 0f, useTemperature = true, temperature = 5000f, type = LightType.Point },
        };
        var lights = new List<Light>();
        foreach (var spec in specs)
        {
            var go = new GameObject(spec.name);
            go.transform.SetPositionAndRotation(new Vector3(0f, 1f, 3f), Quaternion.Euler(60f, 0f, 0f));
            var light = go.AddComponent<Light>();
            light.type = spec.type;
            light.range = 50f;
            light.spotAngle = 120f;
            light.color = spec.color;
            light.intensity = spec.intensity;
            light.useColorTemperature = spec.useTemperature;
            light.colorTemperature = spec.temperature;
            lights.Add(light);
        }

        var target = new RenderTexture(32, 32, 24);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.targetTexture = target;
        camera.transform.position = Vector3.zero;
        camera.farClipPlane = 100f;
        var previous = GraphicsSettings.defaultRenderPipeline;
        GraphicsSettings.defaultRenderPipeline = ScriptableObject.CreateInstance<ProbePipelineAsset>();
        bool linearIntensity = GraphicsSettings.lightsUseLinearIntensity;
        bool useTemperature = GraphicsSettings.lightsUseColorTemperature;
        Rec("settings.default", (linearIntensity ? "linear" : "gamma") + " " + (useTemperature ? "temperature" : "plain"));
        Rec("settings.colorSpace", QualitySettings.activeColorSpace.ToString());
        try
        {
            foreach (bool linear in new[] { false, true })
            foreach (bool temperature in new[] { false, true })
            {
                GraphicsSettings.lightsUseLinearIntensity = linear;
                GraphicsSettings.lightsUseColorTemperature = temperature;
                string mode = (linear ? "linear" : "gamma") + "." + (temperature ? "temperature" : "plain");
                int culls = 0;
                ProbePipelineAsset.OnCull = (cam, results) =>
                {
                    if (cam != camera) return;
                    culls++;
                    var visible = results.visibleLights;
                    var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 0; i < visible.Length; i++)
                    {
                        var light = visible[i].light;
                        if (light != null) rows[light.name] = C(visible[i].finalColor);
                    }
                    foreach (var row in rows) Rec("light." + mode + "." + row.Key, row.Value);
                    Rec("light." + mode + ".count", rows.Count.ToString());
                };
                camera.Render();
                ProbePipelineAsset.OnCull = null;
                Rec("light." + mode + ".culls", culls.ToString());
            }
        }
        finally
        {
            GraphicsSettings.lightsUseLinearIntensity = linearIntensity;
            GraphicsSettings.lightsUseColorTemperature = useTemperature;
            GraphicsSettings.defaultRenderPipeline = previous;
            camera.targetTexture = null;
            target.Release();
        }
    }
}
