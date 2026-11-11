using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Which sampler state a graphics program samples a texture through: its own, another texture's,
/// in the vertex stage, beside loads and legacy samplers, for cubes and volumes, and with
/// comparisons. Each case is a shader `Probe/SamplerStates/&lt;case&gt;` drawn into one float
/// texel; `_A` and `_C` are point filtered, `_B` is bilinear, so a sample between two texels
/// tells the state. Writes `values.jsonl` into `SAMPLER_STATES_OUT` and quits.
/// </summary>
public class UitkProbe : MonoBehaviour
{
    public UnityEngine.UIElements.UIDocument document;
    public Texture2D picture;

    static readonly string[] Cases =
    {
        "One", "Two", "Foreign", "Main", "Load", "Legacy", "Shapes", "Vertex",
        "CmpOnly", "CmpTwo", "CmpOwn", "CmpInline", "CmpOther",
    };

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
            foreach (var name in Cases)
            {
                var shader = Shader.Find("Probe/SamplerStates/" + name);
                if (shader == null)
                {
                    rows.Append("{\"k\":\"").Append(name).Append("\",\"v\":\"missing\"}\n");
                    continue;
                }
                var material = new Material(shader);
                material.SetTexture("_A", a);
                material.SetTexture("_B", b);
                material.SetTexture("_C", c);
                material.SetTexture("_Shadow", shadow);
                material.SetTexture("_Cube", cube);
                material.SetTexture("_Vol", volume);
                Graphics.Blit(a, target, material);
                previous = RenderTexture.active;
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;
                var v = readback.GetPixel(0, 0);
                rows.Append("{\"k\":\"").Append(name).Append("\",\"v\":[")
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
