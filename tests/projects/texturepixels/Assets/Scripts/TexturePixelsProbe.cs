using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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

    void RecordLayered(string name, Texture t)
    {
        int depth = t is Texture2DArray a ? a.depth : t is Texture3D v ? v.depth : 1;
        TextureFormat format = t is Texture2DArray fa ? fa.format : ((Texture3D)t).format;
        Add(name + ".info", string.Format(CultureInfo.InvariantCulture, "{0} {1}x{2}x{3} mips {4} {5} {6} readable {7}",
            t.GetType().Name, t.width, t.height, depth, t.mipmapCount, format, t.graphicsFormat, t.isReadable));
        string size = t.width + "x" + t.height;
        if (t is Texture2DArray array)
            for (int e = 0; e < depth; e++)
            {
                int el = e;
                string key = name + ".e" + e + ".mip0";
                Value(key + ".px32", size, () => Bytes(array.GetPixels32(el, 0)));
                Value(key + ".px", size, () => Floats(array.GetPixels(el, 0)));
                Value(key + ".raw", size, () => Convert.ToBase64String(array.GetPixelData<byte>(0, el).ToArray()));
            }
        else
        {
            var volume = (Texture3D)t;
            string key = name + ".mip0";
            size += "x" + depth;
            Value(key + ".px32", size, () => Bytes(volume.GetPixels32(0)));
            Value(key + ".px", size, () => Floats(volume.GetPixels(0)));
            Value(key + ".raw", size, () => Convert.ToBase64String(volume.GetPixelData<byte>(0).ToArray()));
            Value(key + ".pixel", size, () =>
            {
                var colors = new Color[volume.width * volume.height * depth];
                for (int z = 0; z < depth; z++)
                    for (int y = 0; y < volume.height; y++)
                        for (int x = 0; x < volume.width; x++) colors[(z * volume.height + y) * volume.width + x] = volume.GetPixel(x, y, z);
                return Floats(colors);
            });
        }
    }

    /// <summary>Script-made volumes: GetPixel against GetPixels (byte and float formats), and
    /// the bytes SetPixel stores for values half way between two bytes.</summary>
    void Volumes()
    {
        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.BGRA32, TextureFormat.R8, TextureFormat.RHalf, TextureFormat.RGFloat })
        {
            var volume = new Texture3D(Size, Size, 2, format, false);
            if (format == TextureFormat.RHalf || format == TextureFormat.RGFloat)
            {
                var colors = new Color[Size * Size * 2];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color(i / 37f - 2f, 1.5f - i / 91f, 0.25f, 0.75f);
                volume.SetPixels(colors);
            }
            else
            {
                volume.SetPixelData(Pattern(format, Size * Size * 2, 0), 0);
            }
            volume.Apply(false);
            RecordLayered("Volume" + format, volume);
        }
        var set = new Texture3D(Size, Size, 1, TextureFormat.RGBA32, false);
        for (int i = 0; i < Size * Size; i++)
        {
            float v = (i + 0.5f) / 255f;
            set.SetPixel(i % Size, i / Size, 0, new Color(v, i / 255f, 1 - v, (255 - i + 0.5f) / 255f));
        }
        Value("VolumeSetPixel.mip0.raw", Size + "x" + Size, () => Convert.ToBase64String(set.GetPixelData<byte>(0).ToArray()));
    }

    string SampleCube(Cubemap cube, int face)
    {
        var material = new Material(Shader.Find("Probe/SampleCube"));
        material.SetTexture("_Cube", cube);
        material.SetFloat("_Face", face);
        material.SetFloat("_Lod", 0);
        var rt = RenderTexture.GetTemporary(cube.width, cube.width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(null, rt, material);
            RenderTexture.active = rt;
            var read = new Texture2D(cube.width, cube.width, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, cube.width, cube.width), 0, 0, false);
            read.Apply(false);
            var result = Bytes(read.GetPixels32());
            Destroy(read);
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(material);
        }
    }

    /// <summary>An imported readable cubemap: its faces drawn by the GPU before any CPU access,
    /// after Apply(false) as the first access, and after GetPixels then Apply(false).</summary>
    void ImportedCube(string name)
    {
        var cube = Array.Find(textures, t => t != null && t.name == name) as Cubemap;
        if (cube == null)
        {
            Add(name + ".missing", "True");
            return;
        }
        string size = cube.width + "x" + cube.width;
        for (int f = 0; f < 6; f++) { int face = f; Value(name + ".before.f" + f + ".sample", size, () => SampleCube(cube, face)); }
        Value(name + ".apply", "first", () => { cube.Apply(false); return "ok"; });
        for (int f = 0; f < 6; f++) { int face = f; Value(name + ".applied.f" + f + ".sample", size, () => SampleCube(cube, face)); }
        for (int f = 0; f < 6; f++) { int face = f; Value(name + ".f" + f + ".mip0.px", size, () => Floats(cube.GetPixels((CubemapFace)face, 0))); }
        Value(name + ".reapply", "after GetPixels", () => { cube.Apply(false); return "ok"; });
        for (int f = 0; f < 6; f++) { int face = f; Value(name + ".reapplied.f" + f + ".sample", size, () => SampleCube(cube, face)); }
    }

    /// <summary>The probe's own assets: BGRA32 array and volume files, and an imported readable
    /// PNG encoded to EXR before any other CPU read.</summary>
    void Assets()
    {
        foreach (var suffix in new[] { "", "Linear" })
        {
            var array = Resources.Load<Texture2DArray>("PixelsBgraArray" + suffix);
            if (array == null) Add("PixelsBgraArray" + suffix + ".missing", "True"); else RecordLayered("PixelsBgraArray" + suffix, array);
            var volume = Resources.Load<Texture3D>("PixelsBgraVolume" + suffix);
            if (volume == null) Add("PixelsBgraVolume" + suffix + ".missing", "True"); else RecordLayered("PixelsBgraVolume" + suffix, volume);
        }
        Add("Utility.B8G8R8A8_SRGB", GraphicsFormatUtility.GetTextureFormat(GraphicsFormat.B8G8R8A8_SRGB).ToString());
        Add("Utility.B8G8R8A8_UNorm", GraphicsFormatUtility.GetTextureFormat(GraphicsFormat.B8G8R8A8_UNorm).ToString());
        var imported = Resources.Load<Texture2D>("PixelsImported");
        if (imported == null)
        {
            Add("PixelsImported.missing", "True");
            return;
        }
        Value("PixelsImported.exr", "first", () => imported.EncodeToEXR().Length > 0 ? "ok" : "empty");
        Record("PixelsImported", imported);
    }

    string SampleVolume(Texture3D volume, float z)
    {
        volumeMaterial.SetFloat("_Z", z);
        volumeMaterial.SetFloat("_Lod", 0);
        var rt = RenderTexture.GetTemporary(volume.width, volume.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        try
        {
            Graphics.Blit(null, rt, volumeMaterial);
            RenderTexture.active = rt;
            var read = new Texture2D(volume.width, volume.height, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, volume.width, volume.height), 0, 0, false);
            read.Apply(false);
            var result = Bytes(read.GetPixels32());
            Destroy(read);
            return result;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>One bound Texture3D whose wrapModeW changes Repeat, Clamp, Repeat, then Clamp with an
    /// Apply and Repeat again: each state samples depths beyond the volume (slices 5 and -2 of 4).</summary>
    void LiveWrapW()
    {
        var volume = new Texture3D(4, 4, 4, TextureFormat.RGBA32, false);
        var data = new byte[4 * 4 * 4 * 4];
        for (int i = 0; i < 64; i++)
        {
            int z = i / 16;
            data[i * 4] = (byte)(40 + 60 * z);
            data[i * 4 + 1] = (byte)(i * 13 & 255);
            data[i * 4 + 2] = (byte)(255 - 50 * z);
            data[i * 4 + 3] = 255;
        }
        volume.SetPixelData(data, 0);
        volume.filterMode = FilterMode.Point;
        volume.wrapModeU = TextureWrapMode.Clamp;
        volume.wrapModeV = TextureWrapMode.Clamp;
        volume.wrapModeW = TextureWrapMode.Repeat;
        volume.Apply(false);
        volumeMaterial.SetTexture("_Vol", volume);
        string size = volume.width + "x" + volume.height;
        void Sample(string state)
        {
            foreach (int slice in new[] { 5, -2 })
                Value("LiveWrapW." + state + ".sample" + slice, size, () => SampleVolume(volume, (slice + 0.5f) / 4f));
        }
        Sample("repeat");
        volume.wrapModeW = TextureWrapMode.Clamp;
        Sample("clamp");
        volume.wrapModeW = TextureWrapMode.Repeat;
        Sample("repeatAgain");
        volume.wrapModeW = TextureWrapMode.Clamp;
        volume.Apply(false);
        Sample("clampApplied");
        volume.wrapModeW = TextureWrapMode.Repeat;
        Sample("repeatAfterApply");
        Add("LiveWrapW.wrapModeW", volume.wrapModeW.ToString());
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
            Volumes();
            Assets();
            ImportedCube("CubeStrip");
            ImportedCube("CubeStripLinear");
            LiveWrapW();
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
