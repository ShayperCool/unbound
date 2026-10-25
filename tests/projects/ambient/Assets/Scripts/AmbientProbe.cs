using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The ambient light and reflection the Editor derives from the skybox when a scene has no
/// lighting data: for each sky (the scene's default procedural sky, the same with a sun light and
/// other settings, a cubemap sky of six plain faces) RenderSettings.ambientProbe after
/// DynamicGI.UpdateEnvironment, and images of a matte and a mirror sphere lit by it alone. Writes
/// <c>ambient.jsonl</c> and PNGs into <c>AMBIENT_PROBE_OUT</c>, then quits.
/// </summary>
public class AmbientProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("AmbientProbe").AddComponent<AmbientProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static string Sh(SphericalHarmonicsL2 sh)
    {
        var values = new List<string>();
        for (int c = 0; c < 3; c++)
            for (int k = 0; k < 9; k++) values.Add(F(sh[c, k]));
        return "[" + string.Join(",", values) + "]";
    }

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("AMBIENT_PROBE_OUT") ?? "/tmp/ambient";
        Directory.CreateDirectory(dir);
        // The scene as loaded: the default sky, no sun.
        yield return null;
        Record("initial", $"\"mode\":\"{RenderSettings.ambientMode}\",\"sky\":\"{(RenderSettings.skybox != null ? RenderSettings.skybox.name : "")}\",\"probe\":{Sh(RenderSettings.ambientProbe)},\"intensity\":{F(RenderSettings.ambientIntensity)},\"reflection\":\"{(ReflectionProbe.defaultTexture != null ? ReflectionProbe.defaultTexture.name + " " + ReflectionProbe.defaultTexture.width : "")}\",\"decode\":[{F(ReflectionProbe.defaultTextureHDRDecodeValues.x)},{F(ReflectionProbe.defaultTextureHDRDecodeValues.y)},{F(ReflectionProbe.defaultTextureHDRDecodeValues.z)},{F(ReflectionProbe.defaultTextureHDRDecodeValues.w)}]");

        var defaultSky = RenderSettings.skybox;
        var sunObject = new GameObject("sun");
        var sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(30f, 60f, 0f);
        sun.color = new Color(1f, 0.95f, 0.8f);
        sun.intensity = 1.2f;
        sunObject.SetActive(false);

        var faces = Resources.Load<Cubemap>("Faces");
        var cubeSky = new Material(Shader.Find("Skybox/Cubemap"));
        cubeSky.SetTexture("_Tex", faces);
        var tinted = new Material(defaultSky);
        tinted.SetColor("_SkyTint", new Color(0.8f, 0.3f, 0.3f));
        tinted.SetFloat("_Exposure", 2f);
        tinted.SetFloat("_AtmosphereThickness", 0.6f);
        tinted.SetColor("_GroundColor", new Color(0.2f, 0.4f, 0.1f));

        var cases = new (string name, Material sky, bool withSun, float intensity)[]
        {
            ("default", defaultSky, false, 1f),
            ("default sun", defaultSky, true, 1f),
            ("tinted", tinted, false, 1f),
            ("tinted sun", tinted, true, 1f),
            ("faces", cubeSky, false, 1f),
            ("faces intensity", cubeSky, false, 1.5f),
        };

        // Spheres: matte white and a mirror, lit by the environment alone.
        var matte = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        matte.transform.position = new Vector3(-0.6f, 0f, 0f);
        matte.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.white };
        matte.GetComponent<Renderer>().sharedMaterial.SetFloat("_Glossiness", 0f);
        var mirror = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mirror.transform.position = new Vector3(0.6f, 0f, 0f);
        var mirrorMaterial = new Material(Shader.Find("Standard")) { color = Color.white };
        mirrorMaterial.SetFloat("_Metallic", 1f);
        mirrorMaterial.SetFloat("_Glossiness", 1f);
        mirror.GetComponent<Renderer>().sharedMaterial = mirrorMaterial;
        var target = new RenderTexture(160, 90, 24, RenderTextureFormat.ARGB32);
        var camera = new GameObject("camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.targetTexture = target;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.transform.position = new Vector3(0f, 0.3f, -2.5f);
        camera.transform.LookAt(Vector3.zero);

        foreach (var c in cases)
        {
            RenderSettings.skybox = c.sky;
            RenderSettings.sun = c.withSun ? sun : null;
            RenderSettings.ambientIntensity = c.intensity;
            sunObject.SetActive(c.withSun);
            DynamicGI.UpdateEnvironment();
            for (int i = 0; i < 5; i++) yield return null;
            // The sun's own light off for the images: the environment alone.
            sun.enabled = false;
            camera.Render();
            Save(target, Path.Combine(dir, $"{c.name.Replace(' ', '_')}.png"));
            sun.enabled = true;
            Record("sky", $"\"name\":\"{c.name}\",\"probe\":{Sh(RenderSettings.ambientProbe)},\"intensity\":{F(RenderSettings.ambientIntensity)},\"reflection\":\"{(ReflectionProbe.defaultTexture != null ? ReflectionProbe.defaultTexture.name + " " + ReflectionProbe.defaultTexture.width + " " + ReflectionProbe.defaultTexture.mipmapCount : "")}\",\"decode\":[{F(ReflectionProbe.defaultTextureHDRDecodeValues.x)},{F(ReflectionProbe.defaultTextureHDRDecodeValues.y)},{F(ReflectionProbe.defaultTextureHDRDecodeValues.z)},{F(ReflectionProbe.defaultTextureHDRDecodeValues.w)}]");
        }

        File.WriteAllText(Path.Combine(dir, "ambient.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Save(RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Destroy(texture);
    }
}
