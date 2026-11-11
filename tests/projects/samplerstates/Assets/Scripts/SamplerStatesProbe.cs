using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Which sampler state a graphics program samples a texture through: its own, another texture's,
/// in the vertex stage, beside loads and legacy samplers, for cubes and volumes, and with
/// comparisons. Each case is a shader `Probe/SamplerStates/&lt;case&gt;` drawn into one float
/// texel; `_A` and `_C` are point filtered, `_B` is bilinear, so a sample between two texels
/// tells the state. The comparison and depth cases are drawn again with depth targets of two
/// texels, 0.25 and 0.75, of each depth format and filter (`&lt;case&gt;.&lt;target&gt;`): there a
/// comparison, a filter and a depth read give different values, recorded in twentieths (5 is
/// the first texel, 8 both filtered, 6 and 14 a filtered comparison) so that the bits a depth
/// buffer stores do not tell; `DepthChannels.split.raw` is the value as sampled. Writes
/// `values.jsonl` into `SAMPLER_STATES_OUT` and quits.
/// </summary>
public class UitkProbe : MonoBehaviour
{
    public UnityEngine.UIElements.UIDocument document;
    public Texture2D picture;

    static readonly string[] Cases =
    {
        "One", "Two", "Foreign", "Main", "Load", "Legacy", "Shapes", "Vertex",
        "CmpOnly", "CmpTwo", "CmpOwn", "CmpInline", "CmpOther",
        "Inline", "DepthPlain", "DepthChannels",
    };

    /// <summary>The cases that sample `_Shadow`, drawn again with each depth target.</summary>
    static readonly string[] DepthCases =
    {
        "CmpOnly", "CmpTwo", "CmpOwn", "CmpInline", "CmpOther", "DepthPlain", "DepthChannels",
    };

    /// <summary>A depth target of two texels, 0.25 and 0.75.</summary>
    static RenderTexture Split(RenderTextureFormat format, FilterMode filter, Texture source)
    {
        var texture = new RenderTexture(2, 1, 16, format);
        texture.filterMode = filter;
        texture.Create();
        Graphics.Blit(source, texture, new Material(Shader.Find("Probe/SamplerStates/Fill")));
        return texture;
    }

    static Texture2D Pair(float first, float second, FilterMode filter)
    {
        var texture = new Texture2D(2, 1, TextureFormat.RGBAFloat, false, true);
        texture.filterMode = filter;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(new[] { new Color(first, first, first, 1), new Color(second, second, second, 1) });
        texture.Apply(false, false);
        return texture;
    }

    void Start()
    {
        var output = Environment.GetEnvironmentVariable("SAMPLER_STATES_OUT");
        Directory.CreateDirectory(output);
        var rows = new StringBuilder();
        try
        {
            var a = Pair(0.25f, 0.75f, FilterMode.Point);
            var b = Pair(0.5f, 1f, FilterMode.Bilinear);
            var c = Pair(0.125f, 0.625f, FilterMode.Point);
            var shadow = new RenderTexture(2, 1, 16, RenderTextureFormat.Depth);
            shadow.filterMode = FilterMode.Point;
            shadow.Create();
            var previous = RenderTexture.active;
            RenderTexture.active = shadow;
            GL.Clear(true, true, Color.black, 1f);
            RenderTexture.active = previous;
            var texels = new[] { new Color(.25f, 0, 0, 1), new Color(.25f, 0, 0, 1), new Color(.75f, 0, 0, 1), new Color(.75f, 0, 0, 1) };
            var cube = new Cubemap(2, TextureFormat.RGBAFloat, false);
            cube.filterMode = FilterMode.Point;
            for (int face = 0; face < 6; face++) cube.SetPixels(texels, (CubemapFace)face);
            cube.Apply(false);
            var volume = new Texture3D(2, 1, 1, TextureFormat.RGBAFloat, false);
            volume.filterMode = FilterMode.Point;
            volume.wrapMode = TextureWrapMode.Clamp;
            volume.SetPixels(new[] { new Color(.25f, 0, 0, 1), new Color(.75f, 0, 0, 1) });
            volume.Apply(false);

            var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.Create();
            var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            var depths = new (string, RenderTexture)[]
            {
                ("split", Split(RenderTextureFormat.Depth, FilterMode.Point, a)),
                ("splitLinear", Split(RenderTextureFormat.Depth, FilterMode.Bilinear, a)),
                ("map", Split(RenderTextureFormat.Shadowmap, FilterMode.Point, a)),
                ("mapLinear", Split(RenderTextureFormat.Shadowmap, FilterMode.Bilinear, a)),
            };
            var twentieths = new Material(Shader.Find("Probe/SamplerStates/Twentieths"));
            var rounded = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            rounded.Create();
            // The depth targets' cases in twentieths, and one of them as sampled.
            var draws = new List<(string name, string key, RenderTexture depth, bool exact)>();
            foreach (var name in Cases) draws.Add((name, name, shadow, true));
            foreach (var (suffix, depth) in depths)
            {
                foreach (var name in DepthCases) draws.Add((name, name + "." + suffix, depth, false));
                if (suffix == "split") draws.Add(("DepthChannels", "DepthChannels.split.raw", depth, true));
            }
            foreach (var (name, key, depth, exact) in draws)
            {
                var shader = Shader.Find("Probe/SamplerStates/" + name);
                if (shader == null)
                {
                    rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"missing\"}\n");
                    continue;
                }
                var material = new Material(shader);
                material.SetTexture("_A", a);
                material.SetTexture("_B", b);
                material.SetTexture("_C", c);
                material.SetTexture("_Shadow", depth);
                material.SetTexture("_Cube", cube);
                material.SetTexture("_Vol", volume);
                Graphics.Blit(a, target, material);
                if (!exact) Graphics.Blit(target, rounded, twentieths);
                previous = RenderTexture.active;
                RenderTexture.active = exact ? target : rounded;
                readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;
                var v = readback.GetPixel(0, 0);
                rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[")
                    .Append(F(v.r)).Append(',').Append(F(v.g)).Append(',').Append(F(v.b)).Append(',').Append(F(v.a))
                    .Append("]}\n");
            }
            rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e)
        {
            rows.Append("{\"k\":\"fatal\",\"v\":\"").Append(e.GetType().Name).Append("\"}\n");
            Debug.LogException(e);
        }
        File.WriteAllText(Path.Combine(output, "values.jsonl"), rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
}
