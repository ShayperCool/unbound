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
    static string Gpu(Texture2D t, int mip = 0)
    {
        int width = Math.Max(1, t.width >> mip), height = Math.Max(1, t.height >> mip);
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        Graphics.Blit(t, rt);
        RenderTexture.active = rt;
        var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
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
            ("new.ASTC4", TextureFormat.ASTC_4x4, 12, 12, true),
            ("new.ASTC5", TextureFormat.ASTC_5x5, 12, 12, true),
            ("new.ASTC6", TextureFormat.ASTC_6x6, 12, 12, true),
            ("new.ASTC8", TextureFormat.ASTC_8x8, 12, 12, true),
            ("new.ASTC4Odd", TextureFormat.ASTC_4x4, 7, 5, false),
            ("new.ASTC5Odd", TextureFormat.ASTC_5x5, 7, 5, false),
            ("new.ASTC6Odd", TextureFormat.ASTC_6x6, 7, 5, false),
            ("new.ASTC8Odd", TextureFormat.ASTC_8x8, 7, 5, false),
            ("new.ASTC10", TextureFormat.ASTC_10x10, 13, 7, true),
            ("new.ASTC12", TextureFormat.ASTC_12x12, 13, 7, true),
            ("new.ASTCHDR4", TextureFormat.ASTC_HDR_4x4, 13, 7, true),
            ("new.ASTCHDR5", TextureFormat.ASTC_HDR_5x5, 13, 7, true),
            ("new.ASTCHDR6", TextureFormat.ASTC_HDR_6x6, 13, 7, true),
            ("new.ASTCHDR8", TextureFormat.ASTC_HDR_8x8, 13, 7, true),
            ("new.ASTCHDR10", TextureFormat.ASTC_HDR_10x10, 13, 7, true),
            ("new.ASTCHDR12", TextureFormat.ASTC_HDR_12x12, 13, 7, true),
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

        // Half-float constant-colour blocks retain radiance and alpha through writes and uploads.
        Section("write.astchdr", () =>
        {
            var t = new Texture2D(13, 7, TextureFormat.ASTC_HDR_6x6, true, true);
            var block = new byte[] { 252, 255, 255, 255, 255, 255, 255, 255, 0, 62, 128, 64, 0, 67, 0, 56 };
            var raw = t.GetRawTextureData();
            for (int at = 0; at < raw.Length; at += 16) block.CopyTo(raw, at);
            t.LoadRawTextureData(raw);
            t.Apply(false);
            Describe("write.astchdr.loaded", t);
            var view = t.GetPixelData<byte>(0);
            view[8] = 0; view[9] = 68;
            Add("write.astchdr.view.pixel", Floats(t.GetPixel(0, 0)));
            Add("write.astchdr.view.pixels", Floats(t.GetPixels(0)[0]));
            var mip = t.GetPixelData<byte>(1);
            mip[8] = 0; mip[9] = 70;
            Add("write.astchdr.view1.pixel", Floats(t.GetPixel(0, 0, 1)));
            t.Apply(false);
            Add("write.astchdr.gpu", Gpu(t));
            Destroy(t);
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
        AdditionalAstcCopies();
        AdditionalAstcRegions();
        AdditionalCrunchReads();
        AdditionalEacReads();
        AdditionalEacValues();
        AdditionalCommandMipCopies();
    }

    void AdditionalCompressControls()
    {
        // Independent channel extrema and rectangular, non-power-of-two mip chains.
        foreach (var item in new[]
        {
            ("seed.r8", TextureFormat.R8, 8, 8, false),
            ("seed.rg16", TextureFormat.RG16, 8, 8, false),
            ("rect.nomips.r8", TextureFormat.R8, 12, 8, false),
            ("rect.short.rg16", TextureFormat.RG16, 12, 4, true),
            ("rect.r8", TextureFormat.R8, 12, 8, true),
            ("rect.rg16", TextureFormat.RG16, 12, 8, true),
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
    void AdditionalAstcCopies()
    {
        foreach (var format in new[] { TextureFormat.ASTC_5x5, TextureFormat.ASTC_6x6 })
        {
            string key = "copy.astc." + format;
            Section(key, () =>
            {
                var src = new Texture2D(12, 12, TextureFormat.ASTC_5x5, false, true);
                var dst = new Texture2D(12, 12, format, false, true);
                try
                {
                    Run(key + ".setup", () => {
                        src.LoadRawTextureData(Enumerable.Repeat((byte)167, src.GetRawTextureData().Length).ToArray());
                        src.Apply(false);
                    });
                    Add(key + ".source", Bytes(src.GetRawTextureData()));
                    Add(key + ".before", Bytes(dst.GetRawTextureData()));
                    Run(key, () => Graphics.CopyTexture(src, dst));
                    Add(key + ".after", Bytes(dst.GetRawTextureData()));
                }
                finally { Destroy(src); Destroy(dst); }
            });
        }
    }

    static byte[] AstcSolid(Texture2D texture, Color32 colour)
    {
        var raw = new byte[texture.GetRawTextureData().Length];
        var rgba = new[] { colour.r, colour.g, colour.b, colour.a };
        for (int at = 0; at < raw.Length; at += 16)
        {
            // A LDR constant-colour block, with unrestricted extent and UNorm16 channels.
            raw[at] = 252; raw[at + 1] = 253;
            for (int i = 2; i < 8; i++) raw[at + i] = 255;
            for (int c = 0; c < 4; c++) raw[at + 8 + c * 2] = raw[at + 9 + c * 2] = rgba[c];
        }
        return raw;
    }

    void AdditionalAstcRegions()
    {
        foreach (int alignment in new[] { 5, 4 })
        {
            string key = "copy.astc.region" + alignment;
            Section(key, () =>
            {
                var src = new Texture2D(20, 20, TextureFormat.ASTC_5x5, false, true);
                var dst = new Texture2D(20, 20, TextureFormat.ASTC_5x5, false, true);
                try
                {
                    src.filterMode = dst.filterMode = FilterMode.Point;
                    Run(key + ".setup", () => {
                        src.LoadRawTextureData(AstcSolid(src, new Color32(80, 20, 30, 255)));
                        src.Apply(false);
                        dst.LoadRawTextureData(AstcSolid(dst, new Color32(10, 40, 90, 255)));
                        dst.Apply(false);
                        // CPU green/yellow; retained GPU red/blue before the copy.
                        src.SetPixelData(AstcSolid(src, new Color32(20, 100, 40, 255)), 0);
                        dst.SetPixelData(AstcSolid(dst, new Color32(110, 90, 10, 255)), 0);
                    });
                    Add(key + ".source.raw", Bytes(src.GetRawTextureData()));
                    Add(key + ".source.gpu", Gpu(src));
                    Add(key + ".before.raw", Bytes(dst.GetRawTextureData()));
                    Add(key + ".before.gpu", Gpu(dst));
                    Run(key, () => Graphics.CopyTexture(src, 0, 0, alignment, alignment, alignment, alignment,
                        dst, 0, 0, alignment, alignment));
                    Add(key + ".after.raw", Bytes(dst.GetRawTextureData()));
                    Add(key + ".after.gpu", Gpu(dst));
                }
                finally { Destroy(src); Destroy(dst); }
            });
        }
    }

    void AdditionalCrunchReads()
    {
        foreach (var name in new[] { "SmoothDXT1Crunched", "NoiseDXT5Crunched" })
        {
            string key = "crunch.read." + name;
            Section(key, () =>
            {
                var t = Find(name);
                Run(key + ".float", () => {
                    var pixels = t.GetPixels();
                    Add(key + ".float", pixels.Length + " / " + Floats(pixels[0]));
                });
                Run(key + ".generic", () => {
                    var data = t.GetRawTextureData<byte>();
                    Add(key + ".generic.length", data.Length.ToString());
                    Add(key + ".generic.prefix", string.Join(" ", Enumerable.Range(0, 16).Select(i => data[i])));
                    byte saved = data[20];
                    try {
                        data[20] = (byte)(saved ^ 1);
                        Add(key + ".generic.alias", (t.GetRawTextureData()[20] == (byte)(saved ^ 1)).ToString());
                    }
                    finally { data[20] = saved; }
                });
                Run(key + ".level", () => {
                    var data = t.GetPixelData<byte>(0);
                    Add(key + ".level.length", data.Length.ToString());
                    Add(key + ".level.prefix", string.Join(" ", Enumerable.Range(0, 16).Select(i => data[i])));
                    byte saved = data[20];
                    try {
                        data[20] = (byte)(saved ^ 1);
                        Add(key + ".level.alias", (t.GetRawTextureData()[20] == (byte)(saved ^ 1)).ToString());
                    }
                    finally { data[20] = saved; }
                });
                Add(key + ".restored.raw", Bytes(t.GetRawTextureData()));
                Run(key + ".after", () => Add(key + ".after.pixel", Floats(t.GetPixel(5, 7))));
                Run(key + ".load", () => {
                    var loaded = new Texture2D(t.width, t.height, t.format, t.mipmapCount > 1, true);
                    loaded.name = "LoadedCrunch";
                    try {
                        loaded.filterMode = FilterMode.Point;
                        loaded.LoadRawTextureData(t.GetRawTextureData());
                        loaded.Apply(false, false);
                        Describe(key + ".loaded", loaded);
                    }
                    finally { Destroy(loaded); }
                });
            });
        }
        foreach (var format in new[] { TextureFormat.DXT1Crunched, TextureFormat.DXT5Crunched,
            TextureFormat.ETC_RGB4Crunched, TextureFormat.ETC2_RGBA8Crunched })
        {
            string key = "crunch.new." + format;
            Run(key, () => {
                var t = new Texture2D(8, 8, format, true, true);
                try { Add(key + ".info", t.format + " / " + t.GetRawTextureData().Length); }
                finally { Destroy(t); }
            });
        }
    }

    void AdditionalEacReads()
    {
        foreach (var format in new[] { TextureFormat.EAC_R, TextureFormat.EAC_RG })
        {
            string key = "eac.manual." + format;
            Section(key, () =>
            {
                var t = new Texture2D(8, 8, format, true, true);
                try
                {
                    int stride = format == TextureFormat.EAC_R ? 8 : 16;
                    var raw = new byte[t.GetRawTextureData().Length];
                    for (int at = 0; at < raw.Length; at += stride)
                    {
                        // Red uses a zero multiplier with nonzero modifiers. Green uses
                        // a nonzero multiplier, so CPU byte reads and GPU 11-bit reads diverge.
                        raw[at] = 128; raw[at + 1] = 13;
                        for (int i = 2; i < 8; i++) raw[at + i] = 255;
                        if (stride == 16) { raw[at + 8] = 64; raw[at + 9] = 48; }
                    }
                    Run(key + ".setup", () => { t.LoadRawTextureData(raw); t.Apply(false); });
                    Describe(key, t);
                }
                finally { Destroy(t); }
            });
        }
    }

    void AdditionalEacValues()
    {
        Section("eac.values", () =>
        {
            var t = new Texture2D(256, 128, TextureFormat.EAC_R, false, true);
            try
            {
                var raw = new byte[t.GetRawTextureData().Length];
                // Two modifier tables from the EAC specification cover every low-three-bit
                // value with multiplier zero, so each block records one of all 2048 codes.
                int[][] modifiers = { new[] { -2, -4, -8, -10, 1, 3, 7, 9 },
                    new[] { -1, -2, -3, -10, 0, 1, 2, 9 } };
                for (int value = 0; value < 2048; value++)
                {
                    bool found = false;
                    for (int table = 0; table < 2 && !found; table++)
                    for (int index = 0; index < 8 && !found; index++)
                    {
                        int numerator = value - 4 - modifiers[table][index];
                        if (numerator % 8 != 0 || numerator / 8 < 0 || numerator / 8 > 255) continue;
                        int at = value * 8;
                        raw[at] = (byte)(numerator / 8);
                        raw[at + 1] = (byte)(table == 0 ? 10 : 13);
                        ulong selectors = 0;
                        for (int i = 0; i < 16; i++) selectors = (selectors << 3) | (uint)index;
                        for (int i = 0; i < 6; i++) raw[at + 2 + i] = (byte)(selectors >> (40 - i * 8));
                        found = true;
                    }
                    if (!found) throw new Exception("EAC control cannot represent " + value);
                }
                Run("eac.values.setup", () => { t.LoadRawTextureData(raw); t.Apply(false); });
                Describe("eac.values", t);
            }
            finally { Destroy(t); }
        });
    }

    void AdditionalCommandMipCopies()
    {
        foreach (var format in new[] { TextureFormat.ASTC_6x6, TextureFormat.DXT1 })
        {
            string key = "copy.command.mip." + format;
            Section(key, () => {
                var src = new Texture2D(12, 12, format, true, true);
                var dst = new Texture2D(12, 12, format, true, true);
                byte[] Solid(Texture2D t, bool red)
                {
                    if (format == TextureFormat.ASTC_6x6)
                        return AstcSolid(t, red ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255));
                    var raw = new byte[t.GetRawTextureData().Length];
                    for (int at = 0; at < raw.Length; at += 8)
                    {
                        raw[at] = red ? (byte)0 : (byte)31;
                        raw[at + 1] = red ? (byte)248 : (byte)0;
                    }
                    return raw;
                }
                try
                {
                    src.filterMode = dst.filterMode = FilterMode.Point;
                    Run(key + ".setup", () => {
                        src.LoadRawTextureData(Solid(src, true)); src.Apply(false);
                        dst.LoadRawTextureData(Solid(dst, false)); dst.Apply(false);
                    });
                    Add(key + ".source.raw", Bytes(src.GetRawTextureData()));
                    Add(key + ".source.gpu2", Gpu(src, 2));
                    Add(key + ".before.raw", Bytes(dst.GetRawTextureData()));
                    Add(key + ".before.gpu2", Gpu(dst, 2));
                    Run(key, () => {
                        var commands = new UnityEngine.Rendering.CommandBuffer();
                        try { commands.CopyTexture(src, 0, 2, dst, 0, 2); Graphics.ExecuteCommandBuffer(commands); }
                        finally { commands.Release(); }
                    });
                    Add(key + ".after.gpu2", Gpu(dst, 2));
                    Add(key + ".after.raw", Bytes(dst.GetRawTextureData()));
                }
                finally { Destroy(src); Destroy(dst); }
            });
        }
    }

}
