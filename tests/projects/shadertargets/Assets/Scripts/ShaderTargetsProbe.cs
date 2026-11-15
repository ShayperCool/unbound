using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Which shader target and GPU features each keyword variant of a program compiles with, under
/// its `#pragma target` and `#pragma require` lines (with and without keywords) and the keywords
/// Unity gives requirements of their own. Each case is a shader `Probe/Targets/&lt;case&gt;`
/// declaring two keywords, drawn in its four variants into one float texel
/// (`TargetsCommon.hlsl`): red is `SHADER_TARGET`, green the `SHADER_AVAILABLE_*` defines (one
/// bit each), blue the variant's keywords, alpha `SHADER_TARGET_AVAILABLE`. Rows are
/// `&lt;case&gt;.&lt;keywords&gt;` (`none`, or the enabled keywords joined by `+`). Writes
/// `values.jsonl` into `SHADER_TARGETS_OUT` and quits.
/// </summary>
public class UitkProbe : MonoBehaviour
{
    public UnityEngine.UIElements.UIDocument document;
    public Texture2D picture;

    static readonly string[] Cases =
    {
        "None", "Plain", "TwoPlain", "Keyed", "KeyedFirst", "KeyedOnly", "KeyedLower", "TwoKeywords",
        "Target20", "Target30", "Target40", "Target46", "Target50",
        "ReqCube", "ReqKeyed", "ReqCompute", "ReqInts", "Target46Cube", "ReqMsaaKeyed",
        "BuiltinKeywords", "BuiltinCompute", "Aliases",
        "BuiltinShadows", "ReqMrt4Array", "ReqInstSparse", "ReqStages", "ReqComputeTess",
        "PlainGeometry", "PlainTessellation", "SparseFallback", "SparseLater", "SparseFallbackControl",
    };

    /// <summary>The keywords the cases declare (a keyword space also holds built-in ones).</summary>
    static readonly string[] ProbeKeywords =
    {
        "KW_A", "KW_B", "INSTANCING_ON", "SHADOWS_SCREEN", "PROCEDURAL_INSTANCING_ON", "DYNAMICLIGHTMAP_ON",
        "SHADOWS_SOFT", "DIRLIGHTMAP_COMBINED",
    };

    void Start()
    {
        var output = Environment.GetEnvironmentVariable("SHADER_TARGETS_OUT");
        Directory.CreateDirectory(output);
        var rows = new StringBuilder();
        try
        {
            rows.Append("{\"k\":\"device\",\"v\":\"").Append(SystemInfo.graphicsDeviceType).Append("\"}\n");
            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
            target.Create();
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            foreach (var name in Cases)
            {
                var shader = Shader.Find("Probe/Targets/" + name);
                if (shader == null)
                {
                    rows.Append("{\"k\":\"").Append(name).Append("\",\"v\":\"missing\"}\n");
                    continue;
                }
                var declared = new List<string>(shader.keywordSpace.keywordNames);
                declared.RemoveAll(k => Array.IndexOf(ProbeKeywords, k) < 0);
                declared.Sort(StringComparer.Ordinal);
                var variants = new List<string[]> { new string[0] };
                foreach (var k in declared) variants.Add(new[] { k });
                if (declared.Count == 2) variants.Add(declared.ToArray());
                foreach (var keywords in variants)
                {
                    var material = new Material(shader);
                    foreach (var k in keywords) material.EnableKeyword(k);
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    GL.Clear(true, true, new Color(-1, -1, -1, -1));
                    RenderTexture.active = previous;
                    Graphics.Blit(null, target, material);
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    readback.Apply();
                    RenderTexture.active = previous;
                    Row(rows, name + "." + (keywords.Length == 0 ? "none" : string.Join("+", keywords)), readback.GetPixel(0, 0));
                    DestroyImmediate(material);
                }
            }
            SurfaceIndices(rows, target, readback);
            InstancedTarget(rows, target, readback);
            // Framebuffer fetch: Metal's rows only (the other devices' references stay as they are).
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal)
                Fetch(rows, target, readback);
            rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e)
        {
            rows.Append("{\"k\":\"exception\",\"v\":\"").Append(e.GetType().Name).Append(": ").Append(e.Message.Replace("\"", "'")).Append("\"}\n");
        }
        File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void InstancedTarget(StringBuilder rows, RenderTexture target, Texture2D readback)
    {
        var material = new Material(Shader.Find("Probe/Targets/InstancingTarget"));
        material.enableInstancing = true;
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0) };
        mesh.triangles = new[] { 0,1,2,0,2,3 };
        foreach (bool instanced in new[] { false, true })
        {
            var command = new UnityEngine.Rendering.CommandBuffer();
            command.SetRenderTarget(target);
            command.SetViewport(new Rect(0,0,1,1));
            command.ClearRenderTarget(false, true, new Color(-1,-1,-1,-1));
            if (instanced)
                command.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(0.1f,0,0)) }, 2);
            else
                command.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(command);
            command.Release();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0,0,1,1),0,0);
            readback.Apply();
            RenderTexture.active = previous;
            Row(rows, instanced ? "draw.instanced" : "draw.plain", readback.GetPixel(0,0));
        }
        DestroyImmediate(mesh);
        DestroyImmediate(material);
    }

    /// <summary>The fetch shaders drawn once and twice over a texel cleared to (0.5, 0.25, 0.75, 1):
    /// what the fetch path reads is the color before each draw.</summary>
    static void Fetch(StringBuilder rows, RenderTexture target, Texture2D readback)
    {
        foreach (var name in new[] { "Plain", "Required" })
        {
            var material = new Material(Shader.Find("Probe/Fetch/" + name));
            foreach (int draws in new[] { 1, 2 })
            {
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(0.5f, 0.25f, 0.75f, 1f));
                RenderTexture.active = previous;
                for (int i = 0; i < draws; i++)
                    Graphics.Blit(null, target, material);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;
                Row(rows, "fetch." + name + "." + draws, readback.GetPixel(0, 0));
            }
            DestroyImmediate(material);
        }
        var flags = new Material(Shader.Find("Probe/Fetch/Flags"));
        Graphics.Blit(null, target, flags);
        var active = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        readback.Apply();
        RenderTexture.active = active;
        Row(rows, "fetch.Flags", readback.GetPixel(0, 0));
        DestroyImmediate(flags);
        FetchLimits(rows, target, readback);
    }

    static void ReadInto(StringBuilder rows, string key, RenderTexture source, Texture2D readback)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = source;
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        readback.Apply();
        RenderTexture.active = previous;
        Row(rows, key, readback.GetPixel(0, 0));
    }

    static void ClearTo(RenderTexture texture, Color color)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, color);
        RenderTexture.active = previous;
    }

    /// <summary>Framebuffer fetch past one draw of one triangle per texel: <c>fetch.Overlap</c>, one
    /// draw whose two copies of a quad each cover the texel (Probe/Fetch/Plain over (0.5, 0.25,
    /// 0.75, 1)); <c>fetch.Second.*</c>, a fetch of color target 1 (Probe/Fetch/Second, the targets
    /// cleared to (0.5, 0.25, 0.75, 1) and (0.25, 0.75, 0.5, 1)); <c>fetch.Msaa.*</c>, a 4x
    /// multisampled texel half covered by a white triangle over black, resolved, then squared by
    /// Probe/Fetch/Square and resolved (each sample's square, or the square of their average).</summary>
    static void FetchLimits(StringBuilder rows, RenderTexture target, Texture2D readback)
    {
        var quad = new Mesh { vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) } };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var twice = new Mesh { vertices = quad.vertices, triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 1, 2, 0, 2, 3 } };
        var half = new Mesh { vertices = quad.vertices, triangles = new[] { 0, 1, 2 } };
        void Draw(UnityEngine.Rendering.RenderTargetIdentifier[] colors, UnityEngine.Rendering.RenderTargetIdentifier depth, Mesh mesh, Material material)
        {
            var command = new UnityEngine.Rendering.CommandBuffer();
            command.SetRenderTarget(colors, depth);
            command.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            command.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
            Graphics.ExecuteCommandBuffer(command);
            command.Release();
        }
        var plain = new Material(Shader.Find("Probe/Fetch/Plain"));
        ClearTo(target, new Color(0.5f, 0.25f, 0.75f, 1f));
        Draw(new UnityEngine.Rendering.RenderTargetIdentifier[] { target }, target, twice, plain);
        ReadInto(rows, "fetch.Overlap", target, readback);

        var second = new Material(Shader.Find("Probe/Fetch/Second"));
        var first = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
        var other = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
        first.Create();
        other.Create();
        ClearTo(first, new Color(0.5f, 0.25f, 0.75f, 1f));
        ClearTo(other, new Color(0.25f, 0.75f, 0.5f, 1f));
        Draw(new UnityEngine.Rendering.RenderTargetIdentifier[] { first, other }, first, quad, second);
        ReadInto(rows, "fetch.Second.0", first, readback);
        ReadInto(rows, "fetch.Second.1", other, readback);

        var square = new Material(Shader.Find("Probe/Fetch/Square"));
        var white = new Material(Shader.Find("Hidden/Internal-Colored")) { color = Color.white };
        white.SetInt("_Cull", 0);
        white.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        var msaa = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat) { antiAliasing = 4 };
        msaa.Create();
        ClearTo(msaa, Color.black);
        Draw(new UnityEngine.Rendering.RenderTargetIdentifier[] { msaa }, msaa, half, white);
        Graphics.Blit(msaa, target);
        ReadInto(rows, "fetch.Msaa.covered", target, readback);
        Draw(new UnityEngine.Rendering.RenderTargetIdentifier[] { msaa }, msaa, quad, square);
        Graphics.Blit(msaa, target);
        ReadInto(rows, "fetch.Msaa.squared", target, readback);

        foreach (var o in new UnityEngine.Object[] { quad, twice, half, plain, second, first, other, square, white, msaa })
            DestroyImmediate(o);
    }

    static void SurfaceIndices(StringBuilder rows, RenderTexture target, Texture2D readback)
    {
        foreach (var name in new[] { "Plain", "AddShadow", "NoAdd", "ForwardOnly", "DeferredOnly" })
        {
            var shader = Shader.Find("Probe/SurfaceIndices/" + name);
            var material = new Material(shader);
            var metadata = new StringBuilder();
            for (int i = 0; i < material.passCount; i++)
            {
                var passName = material.GetPassName(i);
                metadata.Append(i).Append('/').Append(passName).Append('/')
                    .Append(material.FindPass(passName)).Append('/')
                    .Append(shader.FindPassTagValue(i, new UnityEngine.Rendering.ShaderTagId("LightMode")).name)
                    .Append(';');
            }
            rows.Append("{\"k\":\"surface.").Append(name).Append(".passes\",\"v\":\"")
                .Append(metadata).Append("\"}\n");
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(-1, -1, -1, -1));
            RenderTexture.active = previous;
            Graphics.Blit(null, target, material, material.FindPass("TAIL"));
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            Row(rows, "surface." + name + ".tail", readback.GetPixel(0, 0));
            DestroyImmediate(material);
        }
    }

    static void Row(StringBuilder rows, string key, Color v)
    {
        rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[")
            .Append(F(v.r)).Append(',').Append(F(v.g)).Append(',').Append(F(v.b)).Append(',').Append(F(v.a))
            .Append("]}\n");
    }

    static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
}
