using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Records each cubemap's format and every texel of every mip, sampled on the GPU
/// (Hidden/CubeTexels) into a float target and read back. Writes values.jsonl into
/// CUBE_COMPRESSION_OUT and exits.</summary>
public sealed class CubeCompressionProbe : MonoBehaviour
{
    static readonly string[] Cubes = { "HdrPlain", "HdrCompressed", "HdrConvolved", "LdrPlain", "LdrCompressed", "LdrAlpha", "LdrHigh" };
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("CubeCompressionProbe").AddComponent<CubeCompressionProbe>();

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    void Texels(string key, Cubemap cube, Material material, int mip, bool alpha, int faces = 6)
    {
        int size = Mathf.Max(1, cube.width >> mip);
        var target = new RenderTexture(faces * size, size, 0, RenderTextureFormat.ARGBFloat);
        material.SetTexture("_Cube", cube);
        material.SetFloat("_Mip", mip);
        material.SetFloat("_Size", size);
        material.SetFloat("_Faces", faces);
        Graphics.Blit(Texture2D.blackTexture, target, material);
        var read = new Texture2D(faces * size, size, TextureFormat.RGBAFloat, false);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, faces * size, size), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        var values = new StringBuilder();
        foreach (var c in read.GetPixels())
        {
            if (values.Length > 0) values.Append(',');
            values.Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b));
            if (alpha) values.Append(',').Append(F(c.a));
        }
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":[").Append(values).Append("]}\n");
        Destroy(read);
        target.Release();
        Destroy(target);
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        var dir = Environment.GetEnvironmentVariable("CUBE_COMPRESSION_OUT");
        try
        {
            var material = new Material(Resources.Load<Shader>("CubeTexels"));
            foreach (var name in Cubes)
            {
                var cube = Resources.Load<Cubemap>(name);
                if (cube == null)
                {
                    Rec(name, "null");
                    continue;
                }
                Rec(name, $"{cube.format} {cube.graphicsFormat} {cube.width}x{cube.height} mips {cube.mipmapCount} sRGB {cube.isDataSRGB}");
                for (int mip = 0; mip < cube.mipmapCount; mip++)
                    Texels($"{name} mip{mip}", cube, material, mip, name == "LdrAlpha");
            }
            // Graphics.ConvertTexture of a cubemap's first face into a new cubemap's +X face,
            // from uncompressed and compressed imports.
            foreach (var name in new[] { "LdrPlain", "LdrCompressed", "HdrPlain", "HdrCompressed" })
            {
                var cube = Resources.Load<Cubemap>(name);
                var destination = new Cubemap(cube.width, name.StartsWith("Hdr") ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false);
                destination.Apply(false);
                bool converted = Graphics.ConvertTexture(cube, 0, destination, 0);
                Rec($"convert {name}", $"{converted} {destination.format}");
                Texels($"convert {name} +X", destination, material, 0, false, 1);
            }
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
