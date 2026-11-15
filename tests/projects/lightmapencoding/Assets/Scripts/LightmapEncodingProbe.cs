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
/// formats of a compressed HDR lightmap and cubemap; a material's <c>_Cube_HDR</c> and
/// <c>_Flat_HDR</c> and its cube decoded with them; level 1 of mipmapped float and 8-bit
/// lightmaps; a float 2D texture's material decode values; the cube's through a property block
/// and as a global texture; unity_Lightmap_HDR with a plain first lightmap; which of a texture's
/// and an explicit <c>_HDR</c> value wins through materials, blocks and globals. Writes <c>lightmapencoding.jsonl</c> into
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

        // Material textures (as a Skybox/Cubemap's): <name>_HDR, the cube decoded with it, and a
        // 2D texture's (the float lightmap's) decode values.
        diagnostic.SetTexture("_Cube", cube);
        diagnostic.SetTexture("_Flat", lightmap);
        string[] material = { "material/cubeDecode", "material/cubeDecoded", "material/flatDecode" };
        for (int mode = 6; mode < 9; mode++)
        {
            diagnostic.SetFloat("_Mode", mode);
            Capture(camera, target, material[mode - 6]);
        }
        // Level 1 of mipmapped float and 8-bit lightmaps.
        diagnostic.SetFloat("_Mode", 9);
        LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = Resources.Load<Texture2D>("LightmapMips") } };
        Capture(camera, target, "mips/lightmapLevel1");
        LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = Resources.Load<Texture2D>("LightmapLdrMips") } };
        Capture(camera, target, "mips/ldrLevel1");
        // A float 2D colour texture's decode values on the material.
        diagnostic.SetTexture("_Flat", Resources.Load<Texture2D>("Hdr2d"));
        diagnostic.SetFloat("_Mode", 8);
        Capture(camera, target, "material/hdr2dDecode");
        // The cube through a property block on a material without one, and as a global texture.
        var bare = new Material(diagnostic.shader);
        bare.SetFloat("_Mode", 6);
        renderer.sharedMaterial = bare;
        var block = new MaterialPropertyBlock();
        block.SetTexture("_Cube", cube);
        renderer.SetPropertyBlock(block);
        Capture(camera, target, "block/cubeDecode");
        renderer.SetPropertyBlock(null);
        Shader.SetGlobalTexture("_GlobalCube", cube);
        bare.SetFloat("_Mode", 10);
        Capture(camera, target, "global/cubeDecode");
        // Two lightmaps, the first a plain texture: the second's draw decodes as the first says.
        renderer.sharedMaterial = diagnostic;
        diagnostic.SetFloat("_Mode", 1);
        LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = Texture2D.whiteTexture }, new LightmapData { lightmapColor = lightmap } };
        renderer.lightmapIndex = 1;
        Capture(camera, target, "mixed/lightmapDecode");

        // Which values win: a plain cube (a cube render texture) through a block over the
        // material's encoded one; explicit _Cube_HDR values set before or after the texture on a
        // material, a block and as globals; an encoded global cube over a material's plain one.
        var plainCube = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32) { dimension = TextureDimension.Cube };
        plainCube.Create();
        var explicitValues = new Vector4(7f, 8f, 9f, 10f);
        diagnostic.SetFloat("_Mode", 6);
        block.Clear();
        block.SetTexture("_Cube", plainCube);
        renderer.SetPropertyBlock(block);
        Capture(camera, target, "precedence/blockPlainOverMaterial");
        block.Clear();
        block.SetTexture("_Cube", cube);
        block.SetVector("_Cube_HDR", explicitValues);
        renderer.SetPropertyBlock(block);
        renderer.sharedMaterial = bare;
        bare.SetFloat("_Mode", 6);
        Capture(camera, target, "precedence/blockVectorAfterTexture");
        block.Clear();
        block.SetVector("_Cube_HDR", explicitValues);
        block.SetTexture("_Cube", cube);
        renderer.SetPropertyBlock(block);
        Capture(camera, target, "precedence/blockTextureAfterVector");
        renderer.SetPropertyBlock(null);
        var vectorAfter = new Material(diagnostic.shader);
        vectorAfter.SetFloat("_Mode", 6);
        vectorAfter.SetTexture("_Cube", cube);
        vectorAfter.SetVector("_Cube_HDR", explicitValues);
        renderer.sharedMaterial = vectorAfter;
        Capture(camera, target, "precedence/materialVectorAfterTexture");
        var textureAfter = new Material(diagnostic.shader);
        textureAfter.SetFloat("_Mode", 6);
        textureAfter.SetVector("_Cube_HDR", explicitValues);
        textureAfter.SetTexture("_Cube", cube);
        renderer.sharedMaterial = textureAfter;
        Capture(camera, target, "precedence/materialTextureAfterVector");
        renderer.sharedMaterial = bare;
        bare.SetFloat("_Mode", 10);
        Shader.SetGlobalTexture("_GlobalCube", cube);
        Shader.SetGlobalVector("_GlobalCube_HDR", explicitValues);
        Capture(camera, target, "precedence/globalVectorAfterTexture");
        Shader.SetGlobalVector("_GlobalCube_HDR", explicitValues);
        Shader.SetGlobalTexture("_GlobalCube", cube);
        Capture(camera, target, "precedence/globalTextureAfterVector");
        var plainMaterial = new Material(diagnostic.shader);
        plainMaterial.SetFloat("_Mode", 6);
        plainMaterial.SetTexture("_Cube", plainCube);
        Shader.SetGlobalTexture("_Cube", cube);
        renderer.sharedMaterial = plainMaterial;
        Capture(camera, target, "precedence/globalOverMaterialPlain");

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
