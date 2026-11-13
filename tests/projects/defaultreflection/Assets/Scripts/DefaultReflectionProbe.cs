using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// What smooth and rough surfaces reflect without a reflection probe: in <c>Data</c> (the built-in
/// lighting data a new scene of this release points at) and in <c>NoData</c> (no lighting data).
/// Each scene gets a row of spheres (the Standard shader, metallic at smoothness 0.95 and 0.5; a
/// Standard surface shader, metallic at 0.95 and 0.5 and dielectric at 0.95; a StandardSpecular
/// surface shader), a nearer row of rough ones (the Standard shader, metallic at smoothness 0 and
/// 0.25 and dielectric at 0; the surface shader, metallic at 0 and dielectric at 0.25), and behind
/// them two flat terrains with one metallic, smooth layer, the left one drawn with its splat
/// material, the right one with its base map. No light: what the surfaces show is the
/// environment's. Writes <c>defaultreflection.jsonl</c> (per scene the default reflection, its
/// decode values and the ambient probe's first coefficients) and one PNG per scene into
/// <c>DEFAULT_REFLECTION_PROBE_OUT</c>, then quits.
/// </summary>
public class DefaultReflectionProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;
    static readonly string[] Scenes = { "Data", "NoData" };
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (FindAnyObjectByType<DefaultReflectionProbe>() != null) return;
        var go = new GameObject("DefaultReflectionProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<DefaultReflectionProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("DEFAULT_REFLECTION_PROBE_OUT") ?? "/tmp/defaultreflection";
        Directory.CreateDirectory(dir);
        foreach (var scene in Scenes)
        {
            if (SceneManager.GetActiveScene().name != scene)
            {
                SceneManager.LoadScene(scene);
                yield return null;
            }
            Build();
            for (int i = 0; i < 3; i++) yield return null;
            Record(dir, scene);
        }
        File.WriteAllText(Path.Combine(dir, "defaultreflection.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Build()
    {
        var standard = Shader.Find("Standard");
        var metallic = Shader.Find("Probe/SurfaceMetallic");
        var specular = Shader.Find("Probe/SurfaceSpecular");
        Sphere(0, standard, 1f, 0.95f);
        Sphere(1, standard, 1f, 0.5f);
        Sphere(2, metallic, 1f, 0.95f);
        Sphere(3, metallic, 1f, 0.5f);
        Sphere(4, metallic, 0f, 0.95f);
        Sphere(5, specular, -1f, 0.95f);
        // Rough surfaces, nearer the camera: the reflection's last mips.
        Sphere(6, standard, 1f, 0f);
        Sphere(7, standard, 1f, 0.25f);
        Sphere(8, standard, 0f, 0f);
        Sphere(9, metallic, 1f, 0f);
        Sphere(10, metallic, 0f, 0.25f);
        MakeTerrain(new Vector3(-40f, -0.01f, -15f), 1000f);
        // Base map distance 0: every patch draws the base map.
        MakeTerrain(new Vector3(0f, -0.01f, -15f), 0f);
    }

    static void Sphere(int index, Shader shader, float metallic, float smoothness)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bool near = index >= 6;
        sphere.transform.position = near ? new Vector3(-4f + 2f * (index - 6), 0.5f, -4f) : new Vector3(-6f + 2.4f * index, 0.8f, 0f);
        sphere.transform.localScale = Vector3.one * (near ? 1f : 1.6f);
        var material = new Material(shader);
        if (metallic >= 0f)
        {
            material.color = new Color(0.9f, 0.9f, 0.9f);
            material.SetFloat("_Metallic", metallic);
        }
        material.SetFloat("_Glossiness", smoothness);
        sphere.GetComponent<Renderer>().sharedMaterial = material;
    }

    static void MakeTerrain(Vector3 position, float basemapDistance)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, true);
        var pixels = new Color[16];
        for (int p = 0; p < pixels.Length; p++) pixels[p] = new Color(0.8f, 0.8f, 0.8f, 1f);
        texture.SetPixels(pixels);
        texture.Apply();
        var layer = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(8f, 8f), metallic = 1f, smoothness = 0.9f };
        var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 32, baseMapResolution = 64 };
        data.size = new Vector3(40f, 2f, 40f);
        data.terrainLayers = new[] { layer };
        var terrain = Terrain.CreateTerrainGameObject(data).GetComponent<Terrain>();
        terrain.transform.position = position;
        terrain.basemapDistance = basemapDistance;
    }

    void Record(string dir, string scene)
    {
        var reflection = ReflectionProbe.defaultTexture;
        var decode = ReflectionProbe.defaultTextureHDRDecodeValues;
        var probe = RenderSettings.ambientProbe;
        m_Out.Append($"{{\"scene\":\"{scene}\",\"reflection\":\"{(reflection ? reflection.name : "null")}\",\"size\":{(reflection ? reflection.width : 0)},")
            .Append($"\"decode\":[{F(decode.x)},{F(decode.y)},{F(decode.z)},{F(decode.w)}],")
            .Append($"\"ambient\":[{F(probe[0, 0])},{F(probe[1, 0])},{F(probe[2, 0])}]}}\n");
        var go = new GameObject("camera");
        go.transform.position = new Vector3(0f, 3f, -9f);
        go.transform.LookAt(new Vector3(0f, 0.5f, 0f));
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.4f, 0.5f, 0.7f);
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, scene + ".png"), image.EncodeToPNG());
        DestroyImmediate(target);
        DestroyImmediate(go);
        Destroy(image);
    }
}
