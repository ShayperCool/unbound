using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// On the third frame, draws the probe shaders into a 4x4 linear render texture with Graphics.Blit and writes the
/// pixel each case gives (bytes), into values.jsonl in $SHADER_RESOURCES_OUT (tests/value_probe.py):
/// a struct holding a structured buffer and a texture filled from globals (read through a function,
/// a copy and directly, at several indices) and 24 textures sampled with one sampler state in one
/// stage.
/// </summary>
public class ShaderResourcesProbe : MonoBehaviour
{
    static StreamWriter output;
    static string s_Directory;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    static Texture2D Solid(Color32 color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        texture.SetPixels32(new[] { color });
        texture.Apply();
        return texture;
    }

    static string Draw(Material material)
    {
        var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(null, target, material);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
        read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        target.Release();
        var c = read.GetPixel(1, 1);
        Color32 b = c;
        return b.r + " " + b.g + " " + b.b + " " + b.a;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        s_Directory = Environment.GetEnvironmentVariable("SHADER_RESOURCES_OUT");
        if (!string.IsNullOrEmpty(s_Directory)) new GameObject("probe").AddComponent<ShaderResourcesProbe>();
    }

    void Update()
    {
        if (Time.frameCount < 3) return;
        enabled = false;
        string dir = s_Directory;
        try
        {
            Directory.CreateDirectory(dir);
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                var colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 16);
                colors.SetData(new[]
                {
                    new Vector4(1f, 0f, 0f, 1f), new Vector4(0f, 1f, 0f, 1f),
                    new Vector4(0.25f, 0.5f, 0.75f, 1f), new Vector4(1f, 1f, 1f, 0.5f),
                });
                var member = new Material(Shader.Find("Probe/BufferMember"));
                member.SetBuffer("_Colors", colors);
                member.SetTexture("_Tint", Solid(new Color32(255, 128, 64, 255)));
                string[] modes = { "function", "copy", "direct" };
                for (int mode = 0; mode < modes.Length; mode++)
                    for (int index = 0; index < 3; index++)
                    {
                        member.SetInt("_Mode", mode);
                        member.SetInt("_Index", index);
                        Row("member." + modes[mode] + "." + index, Draw(member));
                    }
                colors.Release();
                var gaps = new Material(Shader.Find("Probe/UniformGaps"));
                gaps.SetVector("_Used", new Vector4(0.25f, 0.5f, 0.125f, 0.5f));
                gaps.SetVector("_VertexOnly", Vector4.zero);
                gaps.SetFloat("_Depth", 0f);
                gaps.SetVector("_Tiling", new Vector4(1f, 1f, 0f, 0f));
                gaps.SetFloat("_Scale", 2f);
                gaps.SetVector("_Bias", new Vector4(0.25f, 0f, 0.5f, 0.25f));
                Row("gaps.color", Draw(gaps));

                var many = new Material(Shader.Find("Probe/ManyTextures"));
                for (int k = 0; k < 24; k++) many.SetTexture("_T" + k, Solid(new Color32((byte)(k * 8), 0, 0, 255)));
                Row("many.24", Draw(many));
                // A texture changed after the first draw: its own slot follows.
                many.SetTexture("_T16", Solid(new Color32(200, 0, 0, 255)));
                Row("many.24.changed", Draw(many));
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
