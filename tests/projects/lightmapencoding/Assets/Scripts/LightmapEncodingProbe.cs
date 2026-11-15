using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// How the active build target encodes and decodes baked lighting: every built-in shader define
/// GraphicsSettings.HasShaderDefine answers, the imported lightmap's and reflection cubemap's
/// formats, and 16x8 float readbacks of a quad lightmapped with the 16x8 lightmap (one pixel per
/// texel): the raw lightmap texel, unity_Lightmap_HDR, DecodeLightmap; the raw reflection texel at
/// each of the 96 cube texel centres, unity_SpecCube0_HDR and DecodeHDR, through the scene's
/// custom default reflection and through a Custom reflection probe; and the quad drawn with the
/// built-in Legacy Shaders/Diffuse; the first three again with an 8-bit (PNG) lightmap; the
/// formats of a compressed HDR lightmap and cubemap. Writes <c>lightmapencoding.jsonl</c> into
/// <c>LIGHTMAP_ENCODING_PROBE_OUT</c>, then quits.
/// </summary>
public class LightmapEncodingProbe : MonoBehaviour
{
    const int Width = 16, Height = 8;
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("LightmapEncodingProbe").AddComponent<LightmapEncodingProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector4 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)},{F(v.w)}]";

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("LIGHTMAP_ENCODING_PROBE_OUT") ?? "/tmp/lightmapencoding";
        Directory.CreateDirectory(dir);
        foreach (BuiltinShaderDefine define in Enum.GetValues(typeof(BuiltinShaderDefine)))
            m_Out.Append($"{{\"define\":\"{define}\",\"value\":{(GraphicsSettings.HasShaderDefine(define) ? "true" : "false")}}}\n");
        var lightmap = Resources.Load<Texture2D>("Lightmap");
        var cube = Resources.Load<Cubemap>("Reflection");
        var ldr = Resources.Load<Texture2D>("LightmapLdr");
        Describe("Lightmap", lightmap);
        Describe("Reflection", cube);
        Describe("LightmapLdr", ldr);
        Describe("LightmapCompressed", Resources.Load<Texture2D>("LightmapCompressed"));
        Describe("ReflectionCompressed", Resources.Load<Cubemap>("ReflectionCompressed"));
        m_Out.Append($"{{\"defaultReflectionDecode\":{V(ReflectionProbe.defaultTextureHDRDecodeValues)}}}\n");

        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-1f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(-1f, 0.5f, 0f), new Vector3(1f, 0.5f, 0f) };
        var corners = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.uv = corners;
        mesh.uv2 = corners;
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 1f, 0.1f));
        var quad = new GameObject("Quad", typeof(MeshFilter), typeof(MeshRenderer));
        quad.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = quad.GetComponent<MeshRenderer>();
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = lightmap } };
        renderer.lightmapIndex = 0;
        renderer.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);

        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -1f);
        camera.orthographic = true;
        camera.orthographicSize = 0.5f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        camera.enabled = false;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        camera.targetTexture = target;
        yield return null;
        yield return null;

        var diagnostic = new Material(Shader.Find("LightmapEncoding/Diagnostic"));
        string[] modes = { "lightmapRaw", "lightmapDecode", "lightmapDecoded", "reflectionRaw", "reflectionDecode", "reflectionDecoded" };
        for (int mode = 0; mode < modes.Length; mode++)
        {
            diagnostic.SetFloat("_Mode", mode);
            renderer.sharedMaterial = diagnostic;
            Capture(camera, target, modes[mode]);
        }
        var diffuse = new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = Color.white };
        renderer.sharedMaterial = diffuse;
        Capture(camera, target, "legacyDiffuse");
        // The 8-bit lightmap in its place.
        LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = ldr } };
        renderer.sharedMaterial = diagnostic;
        for (int mode = 0; mode < 3; mode++)
        {
            diagnostic.SetFloat("_Mode", mode);
            Capture(camera, target, "ldr/" + modes[mode]);
        }

        var probe = new GameObject("Probe").AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = cube;
        probe.size = new Vector3(10f, 10f, 10f);
        yield return null;
        m_Out.Append($"{{\"probeDecode\":{V(probe.textureHDRDecodeValues)}}}\n");
        renderer.sharedMaterial = diagnostic;
        for (int mode = 3; mode < modes.Length; mode++)
        {
            diagnostic.SetFloat("_Mode", mode);
            Capture(camera, target, "probe/" + modes[mode]);
        }

        File.WriteAllText(Path.Combine(dir, "lightmapencoding.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Describe(string name, Texture texture)
    {
        m_Out.Append($"{{\"texture\":\"{name}\",\"format\":\"{(texture is Texture2D t ? t.format.ToString() : ((Cubemap)texture).format.ToString())}\",");
        m_Out.Append($"\"graphicsFormat\":\"{texture.graphicsFormat}\",\"isDataSRGB\":{(texture.isDataSRGB ? "true" : "false")},");
        m_Out.Append($"\"width\":{texture.width},\"height\":{texture.height},\"mipmapCount\":{texture.mipmapCount}}}\n");
    }

    void Capture(Camera camera, RenderTexture target, string name)
    {
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        var pixels = read.GetPixels();
        Destroy(read);
        m_Out.Append($"{{\"capture\":\"{name}\",\"pixels\":[");
        for (int i = 0; i < pixels.Length; i++)
            m_Out.Append(i == 0 ? "" : ",").Append(V(pixels[i]));
        m_Out.Append("]}\n");
    }
}
