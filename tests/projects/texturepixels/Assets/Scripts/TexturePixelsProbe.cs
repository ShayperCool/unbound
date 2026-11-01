using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Staged by run.py over the texture array fixture's probe script (same class and GUID, so the
/// fixture's scene runs it). Records how Texture2D reads stored bytes as floats: for 16x16
/// textures of each 8-bit and packed format, filled with SetPixelData so that every channel byte
/// takes every value 0..255, it records per mip level <c>GetPixels32</c> (RGBA bytes),
/// <c>GetPixels</c> (little-endian 32-bit floats), <c>GetPixel</c> and
/// <c>GetPixelBilinear</c> at every texel centre, <c>GetPixelData&lt;byte&gt;</c>, and the
/// <c>GetPixels()</c> and block overloads. Writes <c>TEXTURE_PIXELS_PROBE_OUT</c> (one {"k","v"}
/// JSON line per value) and quits, with exit code 1 when the probe itself fails.
/// </summary>
public class TextureArrayProbe : MonoBehaviour
{
    // The fixture scene's serialized fields; unused here.
    public Texture[] textures;
    public Material arrayMaterial;
    public Material volumeMaterial;
    public string[] samples;

    const int Size = 16;
    readonly StringBuilder m_Out = new StringBuilder();

    void Add(string key, string value)
    {
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
    }

    static string Floats(Color[] colors)
    {
        var bytes = new byte[colors.Length * 16];
        for (int i = 0; i < colors.Length; i++)
            for (int c = 0; c < 4; c++)
                BitConverter.GetBytes(colors[i][c]).CopyTo(bytes, i * 16 + c * 4);
        return Convert.ToBase64String(bytes);
    }

    static string Bytes(Color32[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4] = pixels[i].r;
            bytes[i * 4 + 1] = pixels[i].g;
            bytes[i * 4 + 2] = pixels[i].b;
            bytes[i * 4 + 3] = pixels[i].a;
        }
        return Convert.ToBase64String(bytes);
    }

    void Value(string key, string size, Func<string> read)
    {
        string value;
        try { value = size + ":" + read(); }
        catch (Exception e) { value = "error " + e.GetType().Name + ": " + e.Message; }
        Add(key, value);
    }

    static int BytesPerTexel(TextureFormat format) => format switch
    {
        TextureFormat.Alpha8 or TextureFormat.R8 => 1,
        TextureFormat.RG16 or TextureFormat.R16 or TextureFormat.RGBA4444 or TextureFormat.ARGB4444 or TextureFormat.RGB565 => 2,
        TextureFormat.RGB24 => 3,
        _ => 4,
    };

    // Byte c of texel i at level mip: within a level every byte position runs through 0..255.
    static byte[] Pattern(TextureFormat format, int texels, int mip)
    {
        int bpp = BytesPerTexel(format);
        var data = new byte[texels * bpp];
        for (int i = 0; i < texels; i++)
            for (int c = 0; c < bpp; c++)
                data[i * bpp + c] = (byte)((i * (mip + 1) + 64 * c + 17 * mip) & 255);
        return data;
    }

    void Record(string name, Texture2D t)
    {
        Add(name + ".info", string.Format(CultureInfo.InvariantCulture, "{0}x{1} mips {2} {3} {4} readable {5}",
            t.width, t.height, t.mipmapCount, t.format, t.graphicsFormat, t.isReadable));
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            int m = mip, w = Math.Max(1, t.width >> mip), h = Math.Max(1, t.height >> mip);
            string size = w + "x" + h, key = name + ".mip" + mip;
            Value(key + ".px32", size, () => Bytes(t.GetPixels32(m)));
            Value(key + ".px", size, () => Floats(t.GetPixels(m)));
            Value(key + ".raw", size, () => Convert.ToBase64String(t.GetPixelData<byte>(m).ToArray()));
            Value(key + ".pixel", size, () =>
            {
                var colors = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) colors[y * w + x] = m == 0 ? t.GetPixel(x, y) : t.GetPixel(x, y, m);
                return Floats(colors);
            });
            Value(key + ".bilinear", size, () =>
            {
                var colors = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                        colors[y * w + x] = m == 0 ? t.GetPixelBilinear(u, v) : t.GetPixelBilinear(u, v, m);
                    }
                return Floats(colors);
            });
            if (w > 2 && h > 2)
                Value(key + ".block", (w - 2) + "x" + (h - 2), () => Floats(m == 0 ? t.GetPixels(1, 1, w - 2, h - 2) : t.GetPixels(1, 1, w - 2, h - 2, m)));
        }
        Value(name + ".all.px", t.width + "x" + t.height, () => Floats(t.GetPixels()));
    }

    Texture2D Make(TextureFormat format, bool mips, bool linear)
    {
        var t = new Texture2D(Size, Size, format, mips, linear);
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            int n = Math.Max(1, Size >> mip) * Math.Max(1, Size >> mip);
            t.SetPixelData(Pattern(format, n, mip), mip);
        }
        t.Apply(false);
        return t;
    }

    void Start()
    {
        int code = 0;
        try
        {
            foreach (var format in new[]
                     {
                         TextureFormat.RGBA32, TextureFormat.ARGB32, TextureFormat.BGRA32, TextureFormat.RGB24,
                         TextureFormat.Alpha8, TextureFormat.R8, TextureFormat.RG16, TextureFormat.R16,
                         TextureFormat.RGBA4444, TextureFormat.ARGB4444, TextureFormat.RGB565,
                     })
                Record(format.ToString(), Make(format, false, false));
            Record("RGBA32Linear", Make(TextureFormat.RGBA32, false, true));
            Record("RGBA32Mips", Make(TextureFormat.RGBA32, true, true));
            Record("R8Mips", Make(TextureFormat.R8, true, true));
            var png = Make(TextureFormat.RGBA32, false, false).EncodeToPNG();
            var loaded = new Texture2D(2, 2);
            Add("Loaded.load", loaded.LoadImage(png, false) ? "True" : "False");
            Record("Loaded", loaded);
            var path = Environment.GetEnvironmentVariable("TEXTURE_PIXELS_PROBE_OUT");
            if (!string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, m_Out.ToString());
            }
            else
            {
                Debug.Log(m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("texture pixels probe failed: " + e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
