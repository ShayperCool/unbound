using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Records which light <c>RenderSettings.sun</c> returns, and which light a ForwardBase
/// pass and the skybox are drawn with, for scenes with and without a named sun. Each case
/// creates its lights, records, and destroys them again; the scene's own two lights are recorded
/// first and then deactivated. Writes values.jsonl into RENDER_SETTINGS_SUN_OUT and exits.</summary>
public sealed class RenderSettingsSunProbe : MonoBehaviour
{
    const int DumpLayer = 30;
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<Light> m_Made = new List<Light>();
    Camera m_Camera;
    RenderTexture m_Target;
    Texture2D m_Read;
    Material m_Sky;
    int m_Turn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("RenderSettingsSunProbe").AddComponent<RenderSettingsSunProbe>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    void RecFloats(string key, params float[] values) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":[").Append(string.Join(",", values.Select(F))).Append("]}\n");

    static string Name(Light light) => light == null ? "null" : light.name;

    /// <summary>A directional light of its own direction (each light turns a little further,
    /// so the direction a pass is given names the light).</summary>
    Light Directional(string name, float intensity, Color color, LightShadows shadows = LightShadows.None)
    {
        var go = new GameObject(name);
        go.transform.rotation = Quaternion.Euler(30f + 7f * m_Turn, -40f + 11f * m_Turn, 0f);
        m_Turn++;
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = shadows;
        m_Made.Add(light);
        return light;
    }

    Light Directional(string name, float intensity, LightShadows shadows = LightShadows.None) =>
        Directional(name, intensity, Color.white, shadows);

    Light Other(string name, LightType type, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(new Vector3(0f, 0f, -2f), Quaternion.Euler(10f, 20f, 0f));
        var light = go.AddComponent<Light>();
        light.type = type;
        light.intensity = intensity;
        light.range = 20f;
        m_Made.Add(light);
        return light;
    }

    /// <summary>The light whose direction a pass was given.</summary>
    string Which(Vector4 position, IEnumerable<Light> lights)
    {
        if (Mathf.Abs(position.x) + Mathf.Abs(position.y) + Mathf.Abs(position.z) < 1e-6f) return "none";
        foreach (var light in lights)
        {
            if (light == null || light.type != LightType.Directional) continue;
            var toLight = -light.transform.forward;
            if (Mathf.Abs(toLight.x - position.x) + Mathf.Abs(toLight.y - position.y) + Mathf.Abs(toLight.z - position.z) < 1e-3f)
                return light.name;
        }
        return "other";
    }

    void Read(string key, IEnumerable<Light> lights)
    {
        m_Camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = m_Target;
        m_Read.ReadPixels(new Rect(0, 0, 2, 1), 0, 0);
        m_Read.Apply();
        RenderTexture.active = previous;
        Color color = m_Read.GetPixel(0, 0), position = m_Read.GetPixel(1, 0);
        Rec(key + " light", Which(position, lights));
        RecFloats(key + " color", color.r, color.g, color.b);
        RecFloats(key + " position", position.r, position.g, position.b, position.a);
    }

    /// <summary>The getter, the ForwardBase pass's main light (not between equal lights: the
    /// Editor's choice between them varies between runs) and the skybox's sun.</summary>
    void Snapshot(string name, IEnumerable<Light> lights, bool forward = true)
    {
        var all = lights.ToList();
        Rec(name + " sun", Name(RenderSettings.sun));
        if (forward)
        {
            m_Camera.orthographic = true;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.cullingMask = 1 << DumpLayer;
            Read(name + " forward", all);
        }
        m_Camera.orthographic = false;
        m_Camera.clearFlags = CameraClearFlags.Skybox;
        m_Camera.cullingMask = 0;
        Read(name + " sky", all);
        // The getter again: drawing changes nothing.
        Rec(name + " sun after", Name(RenderSettings.sun));
    }

    void Case(string name, Action setup, bool forward = true)
    {
        m_Turn = 0;
        setup();
        Snapshot(name, m_Made, forward);
        foreach (var light in m_Made)
            if (light != null) DestroyImmediate(light.gameObject);
        m_Made.Clear();
        RenderSettings.sun = null;
    }

    void Setup()
    {
        m_Target = new RenderTexture(2, 1, 24, RenderTextureFormat.ARGBFloat);
        m_Read = new Texture2D(2, 1, TextureFormat.RGBAFloat, false);
        var cameraObject = new GameObject("Sun Probe Camera");
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5f), Quaternion.identity);
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.orthographicSize = 1f;
        m_Camera.nearClipPlane = 0.1f;
        m_Camera.farClipPlane = 20f;
        m_Camera.backgroundColor = Color.clear;
        m_Camera.allowHDR = true;
        m_Camera.allowMSAA = false;
        m_Camera.targetTexture = m_Target;

        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-3f, -3f, 0f), new Vector3(3f, -3f, 0f), new Vector3(3f, 3f, 0f), new Vector3(-3f, 3f, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(6f, 6f, 1f));
        var quad = new GameObject("Sun Probe Quad");
        quad.layer = DumpLayer;
        quad.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = quad.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Resources.Load<Shader>("SunProbeForward"));
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        m_Sky = new Material(Resources.Load<Shader>("SunProbeSky"));
        RenderSettings.skybox = m_Sky;
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        var dir = Environment.GetEnvironmentVariable("RENDER_SETTINGS_SUN_OUT");
        try
        {
            Setup();
            var authored = FindObjectsByType<Light>(FindObjectsSortMode.None).OrderBy(l => l.name, StringComparer.Ordinal).ToList();
            Rec("authored lights", string.Join(",", authored.Select(l => l.name)));
            Snapshot("authored", authored);
            foreach (var light in authored) light.gameObject.SetActive(false);

            Case("no lights", () => { });
            Case("point only", () => Other("Point", LightType.Point, 1f));
            Case("one directional", () => Directional("A", 1f));
            // Shadows weigh 16 times: 0.1 with shadows against 1 without.
            Case("shadows outweigh", () => { Directional("Plain", 1f); Directional("Shadowed", 0.1f, LightShadows.Soft); });
            Case("shadows short", () => { Directional("Plain", 1f); Directional("Shadowed", 0.05f, LightShadows.Soft); });
            Case("shadows hard", () => { Directional("Plain", 1.5f); Directional("Hard", 0.1f, LightShadows.Hard); });
            // Red against blue: 0.3 x 1 against 0.11 x 2.7 = 0.297 (Color.grayscale would pick blue).
            Case("channel weights", () => { Directional("Red", 1f, Color.red); Directional("Blue", 2.7f, Color.blue); });
            Case("channel weights green", () => { Directional("Red", 1.9f, Color.red); Directional("Green", 1f, Color.green); });
            Case("hdr color", () => { Directional("Bright", 1f, new Color(2f, 2f, 2f)); Directional("White", 1.9f); });
            Case("force vertex", () =>
            {
                Directional("Vertex", 2f).renderMode = LightRenderMode.ForceVertex;
                Directional("Auto", 1f);
            });
            Case("force pixel", () =>
            {
                Directional("Pixel", 0.5f).renderMode = LightRenderMode.ForcePixel;
                Directional("Auto", 1f);
            });
            Case("disabled and inactive", () =>
            {
                Directional("Disabled", 5f).enabled = false;
                Directional("Inactive", 5f).gameObject.SetActive(false);
                Directional("Enabled", 1f);
            });
            Case("zero intensity", () => Directional("Zero", 0f));
            Case("zero and point", () => { Other("Point", LightType.Point, 3f); Directional("Zero", 0f); });
            Case("culling mask none", () => { Directional("Masked", 2f).cullingMask = 0; Directional("Open", 1f); });
            Case("cookie", () =>
            {
                Directional("Cookie", 2f).cookie = Texture2D.whiteTexture;
                Directional("Plain", 1f);
            });
            Case("baked", () =>
            {
#if UNITY_EDITOR
                Directional("Baked", 2f).lightmapBakeType = LightmapBakeType.Baked;
#else
                Directional("Baked", 2f);
#endif
                Directional("Realtime", 1f);
            });
            Case("color temperature", () =>
            {
                GraphicsSettings.lightsUseColorTemperature = true;
                var warm = Directional("Warm", 1f);
                warm.useColorTemperature = true;
                warm.colorTemperature = 1500f;
                Directional("White", 0.95f);
            });
            GraphicsSettings.lightsUseColorTemperature = false;
            // Equal lights: the first enabled wins; enabling again moves a light behind the other.
            Case("tie", () => { Directional("First", 1f); Directional("Second", 1f); }, forward: false);
            Case("tie reenabled", () =>
            {
                var first = Directional("First", 1f);
                Directional("Second", 1f);
                first.enabled = false;
                first.enabled = true;
            }, forward: false);
            Case("tie reactivated", () =>
            {
                var first = Directional("First", 1f);
                Directional("Second", 1f);
                first.gameObject.SetActive(false);
                first.gameObject.SetActive(true);
            }, forward: false);
            // A named sun.
            Case("named dim", () => { RenderSettings.sun = Directional("Named", 0.2f); Directional("Brighter", 1f); });
            Case("named disabled", () =>
            {
                var named = Directional("Named", 1f);
                named.enabled = false;
                RenderSettings.sun = named;
                Directional("Other", 2f);
            });
            Case("named inactive", () =>
            {
                var named = Directional("Named", 1f);
                named.gameObject.SetActive(false);
                RenderSettings.sun = named;
                Directional("Other", 2f);
            });
            Case("named point", () => { RenderSettings.sun = Other("NamedPoint", LightType.Point, 1f); Directional("Other", 0.5f); });
            Case("named spot alone", () => { RenderSettings.sun = Other("NamedSpot", LightType.Spot, 1f); });
            Case("named destroyed", () =>
            {
                var named = Directional("Named", 3f);
                RenderSettings.sun = named;
                Directional("Remaining", 1f);
                DestroyImmediate(named.gameObject);
            });
            Case("named cleared", () =>
            {
                RenderSettings.sun = Directional("Named", 0.2f);
                Directional("Brighter", 1f);
                Rec("named cleared before", Name(RenderSettings.sun));
                RenderSettings.sun = null;
            });
            // The getter follows changes at once.
            Case("live", () =>
            {
                Directional("A", 1f);
                var b = Directional("B", 0.5f);
                Rec("live before", Name(RenderSettings.sun));
                b.intensity = 2f;
                Rec("live intensity", Name(RenderSettings.sun));
                b.shadows = LightShadows.None;
                b.intensity = 0.9f;
                Rec("live lowered", Name(RenderSettings.sun));
                b.shadows = LightShadows.Soft;
                Rec("live shadows", Name(RenderSettings.sun));
                b.type = LightType.Point;
                Rec("live type", Name(RenderSettings.sun));
                b.type = LightType.Directional;
            });
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
}
