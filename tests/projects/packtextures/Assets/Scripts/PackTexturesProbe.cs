using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Texture2D.PackTextures of block-compressed and other textures: which format, mip levels and size
/// the atlas gets, the rects, and each level's bytes (as a hash, and per input whether its own level
/// is in the atlas block for block, and whether the rest is zero). The inputs are made at run time
/// from fixed pseudo-random blocks (LoadRawTextureData), so no encoder takes part. Writes
/// values.jsonl into PACK_TEXTURES_OUT and exits.</summary>
public sealed class PackTexturesProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("PackTexturesProbe").AddComponent<PackTexturesProbe>();

    void Rec(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static int BlockBytes(TextureFormat f) => f switch
    {
        TextureFormat.DXT1 or TextureFormat.ETC2_RGB or TextureFormat.ETC2_RGBA1 => 8,
        _ => 16,
    };

    static bool Compressed(TextureFormat f) => f is TextureFormat.DXT1 or TextureFormat.DXT5 or TextureFormat.BC7
        or TextureFormat.ETC2_RGB or TextureFormat.ETC2_RGBA8 or TextureFormat.ASTC_4x4;

    static uint s_Seed = 12345u;

    static byte Next()
    {
        s_Seed = s_Seed * 1664525u + 1013904223u;
        return (byte)(s_Seed >> 24);
    }

    /// <summary>A texture of fixed pseudo-random content: blocks for a compressed format, texels
    /// for RGBA32.</summary>
    static Texture2D Make(string name, int w, int h, TextureFormat format, bool mips)
    {
        var t = new Texture2D(w, h, format, mips, false) { name = name };
        var data = new byte[t.GetRawTextureData().Length];
        for (int i = 0; i < data.Length; i++) data[i] = Next();
        t.LoadRawTextureData(data);
        t.Apply(false, false);
        return t;
    }

    static ulong Hash(byte[] data, int offset, int length)
    {
        ulong h = 14695981039346656037ul;
        for (int i = 0; i < length; i++) { h ^= data[offset + i]; h *= 1099511628211ul; }
        return h;
    }

    static byte[] Level(Texture2D t, int mip) => t.GetPixelData<byte>(mip).ToArray();

    void Case(string name, int padding, int max, params Texture2D[] inputs)
    {
        var atlas = new Texture2D(4, 4);
        var rects = atlas.PackTextures(inputs, padding, max, false);
        string p = "pack." + name;
        if (rects == null) { Rec(p + ".rects", "null"); return; }
        Rec(p + ".format", atlas.format.ToString());
        Rec(p + ".size", atlas.width + "x" + atlas.height);
        Rec(p + ".mips", atlas.mipmapCount.ToString());
        for (int i = 0; i < inputs.Length; i++)
            Rec(p + ".inputFormat" + i, inputs[i].format + "/" + inputs[i].mipmapCount);
        for (int i = 0; i < rects.Length; i++)
            Rec(p + ".rect" + i, F(rects[i].x) + "," + F(rects[i].y) + "," + F(rects[i].width) + "," + F(rects[i].height));
        for (int mip = 0; mip < atlas.mipmapCount; mip++)
        {
            var level = Level(atlas, mip);
            string q = p + ".level" + mip;
            Rec(q + ".hash", Hash(level, 0, level.Length).ToString("x16"));
            if (!Compressed(atlas.format)) continue;
            int bb = BlockBytes(atlas.format);
            int lw = Math.Max(1, atlas.width >> mip), lh = Math.Max(1, atlas.height >> mip);
            int bw = (lw + 3) / 4, bh = (lh + 3) / 4;
            var covered = new bool[bw * bh];
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                if (input == null) continue;
                // Where the input's own level lies, block for block.
                int x0 = Mathf.RoundToInt(rects[i].x * atlas.width) >> mip, y0 = Mathf.RoundToInt(rects[i].y * atlas.height) >> mip;
                string state;
                if (mip >= input.mipmapCount) state = "nolevel";
                else
                {
                    var own = Level(input, mip);
                    int iw = Math.Max(1, input.width >> mip), ih = Math.Max(1, input.height >> mip);
                    int ibw = (iw + 3) / 4, ibh = (ih + 3) / 4;
                    bool same = input.format == atlas.format && x0 % 4 == 0 && y0 % 4 == 0 && x0 / 4 + ibw <= bw && y0 / 4 + ibh <= bh;
                    if (same)
                        for (int by = 0; by < ibh && same; by++)
                            for (int bx = 0; bx < ibw && same; bx++)
                                for (int k = 0; k < bb && same; k++)
                                    same = level[((y0 / 4 + by) * bw + x0 / 4 + bx) * bb + k] == own[(by * ibw + bx) * bb + k];
                    bool zero = x0 % 4 == 0 && y0 % 4 == 0 && x0 / 4 + ibw <= bw && y0 / 4 + ibh <= bh;
                    if (zero)
                        for (int by = 0; by < ibh && zero; by++)
                            for (int bx = 0; bx < ibw && zero; bx++)
                                for (int k = 0; k < bb && zero; k++)
                                    zero = level[((y0 / 4 + by) * bw + x0 / 4 + bx) * bb + k] == 0;
                    state = same ? "own" : zero ? "zero" : "other";
                    if (x0 % 4 == 0 && y0 % 4 == 0)
                        for (int by = 0; by < ibh; by++)
                            for (int bx = 0; bx < ibw; bx++)
                                if (y0 / 4 + by < bh && x0 / 4 + bx < bw) covered[(y0 / 4 + by) * bw + x0 / 4 + bx] = true;
                }
                Rec(q + ".input" + i, state);
            }
            bool restZero = true;
            for (int b = 0; b < covered.Length && restZero; b++)
                if (!covered[b])
                    for (int k = 0; k < bb && restZero; k++) restZero = level[b * bb + k] == 0;
            Rec(q + ".restZero", restZero ? "true" : "false");
        }
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        var dir = Environment.GetEnvironmentVariable("PACK_TEXTURES_OUT");
        try
        {
            Case("dxt5", 0, 2048, Make("a", 64, 64, TextureFormat.DXT5, true), Make("b", 32, 32, TextureFormat.DXT5, true));
            Case("dxt1", 0, 2048, Make("a", 64, 32, TextureFormat.DXT1, true), Make("b", 16, 16, TextureFormat.DXT1, true));
            Case("dxt1dxt5", 0, 2048, Make("a", 32, 32, TextureFormat.DXT1, true), Make("b", 32, 32, TextureFormat.DXT5, true));
            Case("dxt5dxt1", 0, 2048, Make("a", 32, 32, TextureFormat.DXT5, true), Make("b", 16, 16, TextureFormat.DXT1, true));
            Case("dxt5padding", 2, 2048, Make("a", 32, 32, TextureFormat.DXT5, true), Make("b", 16, 16, TextureFormat.DXT5, true));
            Case("dxt5nomips", 0, 2048, Make("a", 32, 32, TextureFormat.DXT5, false), Make("b", 16, 16, TextureFormat.DXT5, false));
            Case("dxt5nomipspadding", 2, 2048, Make("a", 32, 32, TextureFormat.DXT5, false), Make("b", 16, 16, TextureFormat.DXT5, false));
            Case("dxt5mixedmips", 0, 2048, Make("a", 32, 32, TextureFormat.DXT5, true), Make("b", 16, 16, TextureFormat.DXT5, false));
            Case("dxt5rgba32", 0, 2048, Make("a", 32, 32, TextureFormat.DXT5, true), Make("b", 16, 16, TextureFormat.RGBA32, true));
            Case("rgba32dxt5", 0, 2048, Make("a", 16, 16, TextureFormat.RGBA32, true), Make("b", 32, 32, TextureFormat.DXT5, true));
            // Small and odd places: the lower levels stop where a place is no longer on a block.
            Case("dxt5small", 0, 2048, Make("a", 64, 64, TextureFormat.DXT5, true), Make("b", 8, 8, TextureFormat.DXT5, true),
                Make("c", 4, 4, TextureFormat.DXT5, true), Make("d", 12, 20, TextureFormat.DXT5, true), Make("e", 4, 8, TextureFormat.DXT5, true));
            Case("dxt5wide", 0, 2048, Make("a", 128, 16, TextureFormat.DXT5, true), Make("b", 16, 64, TextureFormat.DXT5, true));
            // Larger than the maximum: shrunk.
            Case("dxt5shrunk", 0, 32, Make("a", 64, 64, TextureFormat.DXT5, true), Make("b", 16, 16, TextureFormat.DXT5, true));
            Case("bc7", 0, 2048, Make("a", 32, 32, TextureFormat.BC7, true), Make("b", 16, 16, TextureFormat.BC7, true));
            Case("bc7dxt5", 0, 2048, Make("a", 32, 32, TextureFormat.BC7, true), Make("b", 16, 16, TextureFormat.DXT5, true));
            Case("etc2rgba8", 0, 2048, Make("a", 32, 32, TextureFormat.ETC2_RGBA8, true), Make("b", 16, 16, TextureFormat.ETC2_RGBA8, true));
            Case("etc2rgbrgba8", 0, 2048, Make("a", 32, 32, TextureFormat.ETC2_RGB, true), Make("b", 16, 16, TextureFormat.ETC2_RGBA8, true));
            Case("etc2rgb", 0, 2048, Make("a", 32, 32, TextureFormat.ETC2_RGB, true), Make("b", 16, 16, TextureFormat.ETC2_RGB, true));
            Case("astc4", 0, 2048, Make("a", 32, 32, TextureFormat.ASTC_4x4, true), Make("b", 16, 16, TextureFormat.ASTC_4x4, true));
            Case("argb4444", 0, 2048, Make("a", 32, 32, TextureFormat.ARGB4444, true), Make("b", 16, 16, TextureFormat.ARGB4444, true));
            Case("rgba4444", 0, 2048, Make("a", 32, 32, TextureFormat.RGBA4444, true), Make("b", 16, 16, TextureFormat.RGBA4444, true));
            Case("dxt5small2", 0, 2048, Make("a", 64, 64, TextureFormat.DXT5, true), Make("b", 32, 32, TextureFormat.DXT5, true), Make("c", 8, 8, TextureFormat.DXT5, true), Make("d", 16, 8, TextureFormat.DXT5, true));
            // The same texture twice.
            var twice = Make("t", 32, 32, TextureFormat.DXT5, true);
            Case("dxt5twice", 0, 2048, twice, twice, Make("u", 16, 16, TextureFormat.DXT5, true));
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
