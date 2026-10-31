using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what texture import gives a game: each texture's size, format and mip count, and the
/// pixels of every mip level (<c>GetPixels32</c>, RGBA bytes bottom row first, base64). Writes
/// <c>TEXTURE_IMPORT_PROBE_OUT</c> (one {"k","v"} JSON line per value) and quits.
/// </summary>
public class TextureImportProbe : MonoBehaviour
{
    public Texture2D[] textures;

    readonly StringBuilder m_Out = new StringBuilder();

    void Add(string key, string value)
    {
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
    }

    void Start()
    {
        foreach (var t in textures)
        {
            Add(t.name + ".info", string.Format(CultureInfo.InvariantCulture, "{0}x{1} mips {2} {3}",
                t.width, t.height, t.mipmapCount, t.format));
            for (int mip = 0; mip < t.mipmapCount; mip++)
            {
                var pixels = t.GetPixels32(mip);
                var bytes = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                {
                    bytes[i * 4] = pixels[i].r;
                    bytes[i * 4 + 1] = pixels[i].g;
                    bytes[i * 4 + 2] = pixels[i].b;
                    bytes[i * 4 + 3] = pixels[i].a;
                }
                int w = Math.Max(1, t.width >> mip), h = Math.Max(1, t.height >> mip);
                Add(t.name + ".mip" + mip, w + "x" + h + ":" + Convert.ToBase64String(bytes));
            }
        }
        var path = Environment.GetEnvironmentVariable("TEXTURE_IMPORT_PROBE_OUT");
        if (!string.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, m_Out.ToString());
        }
        else
        {
            Debug.Log(m_Out.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
