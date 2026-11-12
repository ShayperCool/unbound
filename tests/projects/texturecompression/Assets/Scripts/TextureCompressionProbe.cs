using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Records what block-compressed textures give a game. For every imported texture: size, mip
/// count, format, graphics format, the raw data (<c>GetRawTextureData</c>), each level's
/// <c>GetPixelData</c> length, <c>GetPixels32</c> of every level, two float reads and what the GPU
/// samples (a blit into a float target read back). Then the runtime APIs: which formats
/// <c>SystemInfo</c> supports, compressed textures made by code, <c>LoadRawTextureData</c>,
/// <c>Texture2D.Compress</c> and <c>Graphics.CopyTexture</c> between compressed textures. Errors
/// logged by a control are recorded with it. Writes <c>TEXTURE_COMPRESSION_PROBE_OUT</c> (one
/// {"k","v"} JSON line per value) and quits.
/// </summary>
public class TextureCompressionProbe : MonoBehaviour
{
    public Texture2D[] textures;

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Errors = new List<string>();

    class Capture : ILogHandler
    {
        public ILogHandler Inner;
        public List<string> Errors;
        public void LogFormat(LogType type, Object context, string format, params object[] args)
        {
            var message = args == null || args.Length == 0 ? format : string.Format(format, args);
            if (type != LogType.Log && type != LogType.Warning) Errors.Add(type + ": " + message.Split('\n')[0]);
            Inner.LogFormat(type, context, format, args);
        }
        public void LogException(Exception exception, Object context)
        {
            Errors.Add("Exception: " + exception.GetType().Name + ": " + exception.Message.Split('\n')[0]);
            Inner.LogException(exception, context);
        }
    }

    void Add(string key, string value)
    {
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"")
            .Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
    }

    /// <summary>Runs a control; records what it threw and the errors it logged.</summary>
    void Run(string key, Action action)
    {
        m_Errors.Clear();
        string thrown = "none";
        try { action(); }
        catch (Exception e) { thrown = e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        Add(key + ".throws", thrown);
        Add(key + ".errors", m_Errors.Count == 0 ? "none" : string.Join(" | ", m_Errors));
    }

    /// <summary>Runs a group of controls; an exception that escapes them is recorded and the
    /// next group runs.</summary>
    void Section(string key, Action action)
    {
        try { action(); }
        catch (Exception e) { Add(key + ".aborted", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
    }

    static string Bytes(byte[] bytes) => Convert.ToBase64String(bytes);

    static string Floats(Color c) => string.Join(" ", new[] { c.r, c.g, c.b, c.a }.Select(f => f.ToString("R", CultureInfo.InvariantCulture)));

    static string Pixels32(Color32[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[i * 4] = pixels[i].r;
            bytes[i * 4 + 1] = pixels[i].g;
            bytes[i * 4 + 2] = pixels[i].b;
            bytes[i * 4 + 3] = pixels[i].a;
        }
        return Bytes(bytes);
    }

    /// <summary>What the GPU samples at every texel centre of level 0: a blit into a float
    /// target read back as little-endian floats.</summary>
    static string Gpu(Texture2D t)
    {
        var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        Graphics.Blit(t, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(t.width, t.height, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0, false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var colors = read.GetPixels();
        Destroy(read);
        var bytes = new byte[colors.Length * 16];
        for (int i = 0; i < colors.Length; i++)
            for (int c = 0; c < 4; c++)
                BitConverter.GetBytes(colors[i][c]).CopyTo(bytes, i * 16 + c * 4);
        return Bytes(bytes);
    }

    void Describe(string key, Texture2D t, bool gpu = true)
    {
        Add(key + ".info", string.Format(CultureInfo.InvariantCulture, "{0}x{1} mips {2} {3} {4} readable {5}",
            t.width, t.height, t.mipmapCount, t.format, t.graphicsFormat, t.isReadable));
        Run(key + ".raw", () => Add(key + ".raw", Bytes(t.GetRawTextureData())));
        if (t.isReadable)
        {
            Run(key + ".levels", () => Add(key + ".levels", string.Join(" ", Enumerable.Range(0, t.mipmapCount).Select(m => t.GetPixelData<byte>(m).Length))));
            for (int m = 0; m < t.mipmapCount; m++)
                Run(key + ".pixels" + m, () => Add(key + ".pixels" + m, Pixels32(t.GetPixels32(m))));
            Run(key + ".pixel", () => Add(key + ".pixel", Floats(t.GetPixel(5, 7)) + " / " + Floats(t.GetPixelBilinear(0.3f, 0.6f))));
        }
        else
        {
            Run(key + ".getpixels", () => t.GetPixels());
            Run(key + ".getpixeldata", () => t.GetPixelData<byte>(0));
        }
        if (gpu) Run(key + ".gpu", () => Add(key + ".gpu", Gpu(t)));
    }

    Texture2D Find(string name) => textures.First(t => t.name == name);

    /// <summary>A readable copy of an uncompressed import in its own format, every level written
    /// with SetPixelData.</summary>
    static Texture2D Copy(Texture2D source, bool mips = true)
    {
        var copy = new Texture2D(source.width, source.height, source.format, mips ? source.mipmapCount : 1, !GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat));
        for (int m = 0; m < copy.mipmapCount; m++) copy.SetPixelData(source.GetPixelData<byte>(m), m);
        copy.Apply(false);
        return copy;
    }

    void Start()
    {
        var capture = new Capture { Inner = Debug.unityLogger.logHandler, Errors = m_Errors };
        Debug.unityLogger.logHandler = capture;
        try { Record(); }
        finally { Debug.unityLogger.logHandler = capture.Inner; }
        var path = Environment.GetEnvironmentVariable("TEXTURE_COMPRESSION_PROBE_OUT");
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

    void Record()
    {
        foreach (var t in textures) Describe("tex." + t.name, t);

        // SystemInfo.
        Add("sys.copyTextureSupport", SystemInfo.copyTextureSupport.ToString());
        foreach (var name in Enum.GetNames(typeof(TextureFormat)))
        {
            var field = typeof(TextureFormat).GetField(name);
            if (field.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0) continue;
            var format = (TextureFormat)Enum.Parse(typeof(TextureFormat), name);
            Add("sys.tf." + name, SystemInfo.SupportsTextureFormat(format).ToString());
        }
        foreach (var format in new[]
        {
            GraphicsFormat.RGBA_DXT1_SRGB, GraphicsFormat.RGBA_DXT1_UNorm, GraphicsFormat.RGBA_DXT5_SRGB,
            GraphicsFormat.RGBA_DXT5_UNorm, GraphicsFormat.R_BC4_UNorm, GraphicsFormat.R_BC4_SNorm,
            GraphicsFormat.RG_BC5_UNorm, GraphicsFormat.RG_BC5_SNorm, GraphicsFormat.RGB_BC6H_UFloat,
            GraphicsFormat.RGB_BC6H_SFloat, GraphicsFormat.RGBA_BC7_SRGB, GraphicsFormat.RGBA_BC7_UNorm,
            GraphicsFormat.RGB_ETC_UNorm, GraphicsFormat.RGB_ETC2_SRGB, GraphicsFormat.RGBA_ETC2_UNorm,
            GraphicsFormat.R_EAC_UNorm, GraphicsFormat.RGBA_ASTC4X4_SRGB, GraphicsFormat.RGBA_ASTC4X4_UNorm,
            GraphicsFormat.RGB_PVRTC_4Bpp_UNorm,
        })
        {
            Add("sys.gf." + format, string.Join(" ", new[] { GraphicsFormatUsage.Sample, GraphicsFormatUsage.Linear, GraphicsFormatUsage.Render, GraphicsFormatUsage.SetPixels, GraphicsFormatUsage.GetPixels }
                .Select(u => SystemInfo.IsFormatSupported(format, u) ? "1" : "0")));
        }

        // Compressed textures made by code.
        foreach (var (key, format, w, h, mips) in new[]
        {
            ("new.DXT1", TextureFormat.DXT1, 64, 64, true), ("new.DXT5", TextureFormat.DXT5, 64, 64, true),
            ("new.BC4", TextureFormat.BC4, 64, 64, true), ("new.BC5", TextureFormat.BC5, 64, 64, true),
            ("new.BC6H", TextureFormat.BC6H, 64, 64, true), ("new.BC7", TextureFormat.BC7, 64, 64, true),
            ("new.DXT1Odd", TextureFormat.DXT1, 30, 18, false), ("new.DXT5Npot", TextureFormat.DXT5, 20, 12, true),
        })
        Section(key, () =>
        {
            Texture2D t = null;
            Run(key, () => t = new Texture2D(w, h, format, mips));
            if (t != null)
            {
                Describe(key, t, false);
                Destroy(t);
            }
        });

        // LoadRawTextureData with compressed blocks.
        var smooth = Find("SmoothNormal");
        var noise = Find("NoiseNormal");
        var smoothRaw = smooth.GetRawTextureData();
        var noiseRaw = noise.GetRawTextureData();
        Section("load", () =>
        {
            var t = new Texture2D(64, 64, smooth.format, true);
            Run("load.dxt1", () => { t.LoadRawTextureData(smoothRaw); t.Apply(false); });
            Describe("load.dxt1", t);
            Run("load.short", () => t.LoadRawTextureData(smoothRaw.Take(smoothRaw.Length - 8).ToArray()));
            Run("load.long", () => t.LoadRawTextureData(smoothRaw.Concat(new byte[8]).ToArray()));
            Add("load.after", Bytes(t.GetRawTextureData()));
            var top = new Texture2D(64, 64, smooth.format, false);
            Run("load.toplevel", () => { top.LoadRawTextureData(smoothRaw.Take(64 * 64 / 2).ToArray()); top.Apply(); });
            Describe("load.toplevel", top);
            var t5 = new Texture2D(64, 64, noise.format, true);
            Run("load.dxt5", () => { t5.LoadRawTextureData(noiseRaw); t5.Apply(false); });
            Describe("load.dxt5", t5);
            Destroy(t); Destroy(top); Destroy(t5);
        });

        // Texture2D.Compress.
        foreach (var (key, source, mips) in new[]
        {
            ("compress.rgba32", "NoiseRGBA32", true), ("compress.rgb24", "SmoothRGB24", true),
            ("compress.r8", "HeightR8", true), ("compress.rg16", "NormalsRG16", true),
            ("compress.odd", "OddRGBA32", false), ("compress.rgba32top", "NoiseRGBA32", false),
        })
        {
            foreach (var high in new[] { false, true })
            {
                var k = key + (high ? ".high" : ".fast");
                Section(k, () =>
                {
                    var t = Copy(Find(source), mips);
                    Run(k, () => t.Compress(high));
                    Describe(k, t);
                    Destroy(t);
                });
            }
        }
        Section("compress.again", () =>
        {
            var t = new Texture2D(64, 64, TextureFormat.DXT1, true);
            t.LoadRawTextureData(smoothRaw);
            t.Apply(false);
            Run("compress.again", () => t.Compress(true));
            Add("compress.again.same", t.GetRawTextureData().SequenceEqual(smoothRaw).ToString());
            Add("compress.again.format", t.format.ToString());
            Destroy(t);
        });
        Section("compress.unreadable", () =>
        {
            var locked = Copy(Find("NoiseRGBA32"));
            locked.Apply(false, true);
            Run("compress.unreadable", () => locked.Compress(false));
            Add("compress.unreadable.info", locked.format + " readable " + locked.isReadable);
            Run("compress.unreadable.gpu", () => Add("compress.unreadable.gpu", Gpu(locked)));
            Destroy(locked);
        });

        // Graphics.CopyTexture between compressed textures.
        var noiseDxt5 = Find("NoiseDXT5");
        foreach (var (key, src, dstFormat, region) in new[]
        {
            ("copy.whole", smooth, TextureFormat.DXT1, 0), ("copy.region", smooth, TextureFormat.DXT1, 1),
            ("copy.unaligned", smooth, TextureFormat.DXT1, 2), ("copy.mip", smooth, TextureFormat.DXT1, 3),
            ("copy.unreadable", Find("SmoothUnreadable"), TextureFormat.DXT1, 0),
            ("copy.dxt1to5", smooth, TextureFormat.DXT5, 0), ("copy.dxt5tobc7", noiseDxt5, TextureFormat.BC7, 0),
            ("copy.dxt1torgba32", smooth, TextureFormat.RGBA32, 0), ("copy.dxt5region", noiseDxt5, TextureFormat.DXT5, 1),
            ("copy.dxt1todxt5region", smooth, TextureFormat.DXT5, 1),
        })
        Section(key, () =>
        {
            var dst = new Texture2D(64, 64, dstFormat, true);
            Run(key, () =>
            {
                switch (region)
                {
                    case 0: Graphics.CopyTexture(src, dst); break;
                    case 1: Graphics.CopyTexture(src, 0, 0, 8, 4, 16, 8, dst, 0, 0, 4, 8); break;
                    case 2: Graphics.CopyTexture(src, 0, 0, 3, 3, 5, 5, dst, 0, 0, 0, 0); break;
                    default: Graphics.CopyTexture(src, 0, 2, dst, 0, 2); break;
                }
            });
            Describe(key, dst);
            Destroy(dst);
        });

        // A copy of a compressed import.
        Section("clone", () =>
        {
            var copy = Instantiate(smooth);
            Describe("clone", copy);
            Destroy(copy);
        });

        // Writes to a compressed import: the pixel setters, then its blocks through a view.
        Section("write", () =>
        {
            var t = Find("SmoothWrite");
            Run("write.setpixel", () => t.SetPixel(0, 0, Color.red));
            Add("write.setpixel.raw", Bytes(t.GetRawTextureData().Take(16).ToArray()));
            Add("write.setpixel.pixel", Floats(t.GetPixel(0, 0)));
            Run("write.setpixels", () => t.SetPixels(new Color[64 * 64]));
            Run("write.setpixels32", () => t.SetPixels32(new Color32[64 * 64]));
            Run("write.setpixeldata", () => t.SetPixelData(new byte[2048], 0));
            Add("write.raw", Bytes(t.GetRawTextureData()));
            Add("write.pixel", Floats(t.GetPixel(0, 0)));
            var view = t.GetPixelData<byte>(0);
            // Block 0 becomes black: both endpoints 0, every index 0.
            Run("write.view", () => { for (int i = 0; i < 8; i++) view[i] = 0; });
            Add("write.view.raw", Bytes(t.GetRawTextureData().Take(16).ToArray()));
            Add("write.view.pixel", Floats(t.GetPixel(0, 0)));
            Run("write.view.apply", () => t.Apply(false));
            Add("write.view.applied.pixel", Floats(t.GetPixel(0, 0)));
            Run("write.view.gpu", () => Add("write.view.gpu", Gpu(t)));
            // Level 1's first block (not zeroed) becomes white: colour 0 = 0xFFFF, every index 0.
            var level1 = t.GetPixelData<byte>(1);
            Run("write.view1", () => { level1[0] = 0xFF; level1[1] = 0xFF; for (int i = 4; i < 8; i++) level1[i] = 0; });
            Add("write.view1.pixel", Floats(t.GetPixel(1, 2, 1)));
            Add("write.view1.pixels", Pixels32(t.GetPixels32(1)));
            Add("write.view1.raw", Bytes(t.GetRawTextureData().Skip(2048).Take(16).ToArray()));
            var copy = Instantiate(t);
            Add("write.view1.copy", Bytes(copy.GetRawTextureData().Skip(2048).Take(16).ToArray()));
            Destroy(copy);
        });
        // Copies into a readable import and into an unreadable texture.
        Section("copy.intoimport", () =>
        {
            var dst = Find("FlatCopyDst");
            Run("copy.intoimport", () => Graphics.CopyTexture(smooth, dst));
            Describe("copy.intoimport", dst);
        });
        Section("copy.unreadabledst", () =>
        {
            var dst = new Texture2D(64, 64, TextureFormat.DXT1, true);
            dst.Apply(false, true);
            Run("copy.unreadabledst", () => Graphics.CopyTexture(smooth, dst));
            Run("copy.unreadabledst.raw", () => Add("copy.unreadabledst.raw", Bytes(dst.GetRawTextureData())));
            Run("copy.unreadabledst.gpu", () => Add("copy.unreadabledst.gpu", Gpu(dst)));
            Destroy(dst);
        });
        // Reinitialize of a compressed texture: the same format, then a size that is not
        // block-sized.
        Section("reinit", () =>
        {
            var t = new Texture2D(64, 64, TextureFormat.DXT1, true);
            Run("reinit.same", () => t.Reinitialize(32, 32));
            Add("reinit.same.info", t.width + "x" + t.height + " mips " + t.mipmapCount + " " + t.format);
            Run("reinit.odd", () => Add("reinit.odd.result", t.Reinitialize(30, 18, TextureFormat.DXT1, false).ToString()));
            Add("reinit.odd.info", t.width + "x" + t.height + " mips " + t.mipmapCount + " " + t.format);
            Destroy(t);
        });
        Section("load.rgba32", () =>
        {
            var t = Copy(Find("NoiseRGBA32"), false);
            Run("load.rgba32.short", () => t.LoadRawTextureData(new byte[64 * 64 * 4 - 4]));
            Destroy(t);
        });
        // One- and two-channel textures made by code, linear and sRGB: what the GPU samples, from
        // raw data (with mips) and from SetPixels32 (without).
        foreach (var format in new[] { TextureFormat.R8, TextureFormat.RG16, TextureFormat.Alpha8 })
            foreach (var linear in new[] { true, false })
            {
                var key = "channels." + format + (linear ? ".linear" : ".srgb");
                Section(key, () =>
                {
                    var t = new Texture2D(8, 8, format, true, linear);
                    var raw = new byte[t.GetRawTextureData().Length];
                    for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i * 37 + 11);
                    t.LoadRawTextureData(raw);
                    t.Apply(false);
                    Describe(key, t);
                    Destroy(t);
                    var cells = new Texture2D(8, 8, format, false, linear);
                    var colors = new Color32[64];
                    for (int i = 0; i < colors.Length; i++)
                        colors[i] = new Color32((byte)(i * 29 + 3), (byte)(i * 53 + 7), (byte)(i * 11 + 5), (byte)(i * 17 + 1));
                    cells.SetPixels32(colors);
                    cells.Apply(false);
                    Run(key + ".cells.gpu", () => Add(key + ".cells.gpu", Gpu(cells)));
                    Destroy(cells);
                });
            }
        // Graphics.CopyTexture between linear and sRGB R8 and RG16 textures, and from a render
        // texture of the format into an sRGB texture: raw data and the GPU's samples afterwards.
        foreach (var format in new[] { TextureFormat.R8, TextureFormat.RG16 })
        {
            var key = "channels.copy." + format;
            Section(key, () =>
            {
                Texture2D Made(bool linear, int seed)
                {
                    var t = new Texture2D(8, 8, format, false, linear);
                    var raw = new byte[t.GetRawTextureData().Length];
                    for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i * 41 + seed);
                    t.LoadRawTextureData(raw);
                    t.Apply(false);
                    return t;
                }
                void Copied(string name, Texture source, Texture2D destination)
                {
                    Run(key + "." + name, () => Graphics.CopyTexture(source, destination));
                    Run(key + "." + name + ".raw", () => Add(key + "." + name + ".raw", Bytes(destination.GetRawTextureData())));
                    Run(key + "." + name + ".gpu", () => Add(key + "." + name + ".gpu", Gpu(destination)));
                }
                var linear = Made(true, 3);
                var srgb = Made(false, 90);
                Copied("tosrgb", linear, srgb);
                var linear2 = Made(true, 17);
                var srgb2 = Made(false, 200);
                Copied("tolinear", srgb2, linear2);
                var rt = new RenderTexture(8, 8, 0, format == TextureFormat.R8 ? RenderTextureFormat.R8 : RenderTextureFormat.RG16, RenderTextureReadWrite.Linear);
                rt.Create();
                Graphics.Blit(linear2, rt);
                var srgb3 = Made(false, 55);
                Copied("fromrt", rt, srgb3);
                rt.Release();
                foreach (var t in new[] { linear, srgb, linear2, srgb2, srgb3 }) Destroy(t);
            });
        }
        AdditionalCompressControls();
    }

    void AdditionalCompressControls()
    {
        // Independent channel extrema and rectangular, non-power-of-two mip chains.
        foreach (var item in new[]
        {
            ("seed.r8", TextureFormat.R8, 8, 8, false),
            ("seed.rg16", TextureFormat.RG16, 8, 8, false),
            ("rect.rgba", TextureFormat.RGBA32, 20, 12, true),
            ("rect.rgb", TextureFormat.RGB24, 12, 8, true),
            ("flat.rgba", TextureFormat.RGBA32, 8, 8, false),
        })
        foreach (bool high in new[] { false, true })
        {
            string key = "compress." + item.Item1 + (high ? ".high" : ".fast");
            Section(key, () =>
            {
                var t = new Texture2D(item.Item3, item.Item4, item.Item2, item.Item5, true);
                try
                {
                    int bpp = item.Item2 == TextureFormat.R8 ? 1 : item.Item2 == TextureFormat.RG16 ? 2
                        : item.Item2 == TextureFormat.RGB24 ? 3 : 4;
                    for (int mip = 0; mip < t.mipmapCount; mip++)
                    {
                        int w = Math.Max(1, t.width >> mip), h = Math.Max(1, t.height >> mip);
                        var raw = new byte[w * h * bpp];
                        for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int at = (y * w + x) * bpp;
                            bool first = x % 4 == 0 && y % 4 == 0;
                            if (bpp <= 2)
                            {
                                raw[at] = first ? (byte)0 : (byte)(78 + x / 4 * 27 + y / 4 * 13);
                                if (bpp == 2) raw[at + 1] = first ? (byte)1 : (byte)(130 + x / 4 * 9 + y / 4 * 17);
                            }
                            else
                            {
                                raw[at] = (byte)(x * 19 + y * 7 + mip * 29 + 3);
                                raw[at + 1] = (byte)(x * 11 + y * 31 + mip * 13 + 5);
                                raw[at + 2] = (byte)(x * 37 + y * 5 + mip * 17 + 7);
                                if (item.Item1 == "flat.rgba")
                                {
                                    raw[at] = 127; raw[at + 1] = 65; raw[at + 2] = 33;
                                }
                                if (bpp == 4) raw[at + 3] = (x + y + mip) % 3 == 0 ? (byte)0
                                    : (x + y) % 2 == 0 ? (byte)83 : (byte)255;
                            }
                        }
                        t.SetPixelData(raw, mip);
                    }
                    t.Apply(false);
                    Add(key + ".input", Bytes(t.GetRawTextureData()));
                    Run(key, () => t.Compress(high));
                    Describe(key, t);
                }
                finally { Destroy(t); }
            });
        }
    }
}
