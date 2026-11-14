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
