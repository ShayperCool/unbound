using System;
using UnityEngine;

public partial class ResProbe
{
    string MipPixels(Texture2D t, int level)
    {
        var colors = t.GetPixels(level);
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < colors.Length; i++) sb.Append(i == 0 ? "" : ",").Append(C(colors[i]));
        return sb.Append("]").ToString();
    }

    void MipChain(string prefix, Texture2D t)
    {
        Value(prefix + ".count", I(t.mipmapCount));
        for (int mip = 0; mip < t.mipmapCount; mip++) Value(prefix + ".mip" + mip, MipPixels(t, mip));
    }

    void Mipmaps()
    {
        var material = Add(new Material(Shader.Find("Probe/MipLevels")));
        var target = Add(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        foreach (var format in new[] { TextureFormat.RGBA32, TextureFormat.RGB24, TextureFormat.Alpha8, TextureFormat.RGBAHalf, TextureFormat.RGBAFloat, TextureFormat.RFloat })
        {
            var t = Add(new Texture2D(8, 4, format, true, true));
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            string name = "mips." + format;
            for (int mip = 0; mip < t.mipmapCount; mip++)
            {
                int width = Math.Max(1, t.width >> mip), height = Math.Max(1, t.height >> mip);
                var pixels = new Color[width * height];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color((mip + 1) * 0.2f, i * 0.03f, 0.8f - mip * 0.2f, 0.3f + mip * 0.1f);
                t.SetPixels(pixels, mip);
            }
            t.Apply(false);
            MipChain(name + ".manual", t);
            Value(name + ".raw_length", I(t.GetRawTextureData().Length));
            for (int mip = 0; mip < t.mipmapCount; mip++)
            {
                material.SetFloat("_Level", mip);
                Graphics.Blit(t, target, material);
                Value(name + ".gpu" + mip, C(Pixel(target)));
            }
            t.SetPixel(0, 0, Color.magenta, 1);
            Value(name + ".setpixel", C(t.GetPixel(0, 0, 1)));
            Value(name + ".get_base", C(t.GetPixel(0, 0)));
            Value(name + ".bilinear", C(t.GetPixelBilinear(0.5f, 0.5f, 1)));
            t.Apply(true);
            MipChain(name + ".generated", t);
            Try(name + ".get_invalid_mip", () => C(t.GetPixel(0, 0, t.mipmapCount)));
            Try(name + ".pixels_invalid_mip", () => I(t.GetPixels(-1).Length));
            Try(name + ".set_invalid_mip", () => { t.SetPixel(0, 0, Color.white, 10); return "true"; });
            Try(name + ".block_invalid", () => I(t.GetPixels(-1, 0, 2, 2, 1).Length));
        }
        foreach (int count in new[] { -2, -1, 0, 1, 2, 10 })
            Try("mips.constructor." + count, () => I(Add(new Texture2D(8, 4, TextureFormat.RGBA32, count, true)).mipmapCount));
        foreach (var asset in new[] { "MipImport", "MipAsset" })
        {
            var t = Resources.Load<Texture2D>(asset);
            for (int mip = 0; mip < t.mipmapCount; mip++)
            {
                material.SetFloat("_Level", mip);
                Graphics.Blit(t, target, material);
                Value("mips.asset." + asset + ".gpu" + mip, C(Pixel(target)));
            }
            MipChain("mips.asset." + asset, t);
        }
        var raw = Add(new Texture2D(4, 2, TextureFormat.RGBA32, true, true));
        raw.SetPixels(new Color[8]);
        raw.SetPixelData(new byte[] { 0, 0, 0, 0, 255, 64, 128, 255, 32, 128, 192, 255 }, 1, 4);
        raw.SetPixelData(new byte[] { 25, 50, 75, 100 }, 2);
        Value("mips.pixeldata.raw", "[" + string.Join(",", raw.GetRawTextureData()) + "]");
        var copy = Add(new Texture2D(4, 2, TextureFormat.RGBA32, true, true));
        copy.LoadRawTextureData(raw.GetRawTextureData());
        copy.Apply(false);
        MipChain("mips.rawcopy", copy);
        Try("mips.raw_short", () => { copy.LoadRawTextureData(new byte[2]); return "true"; });
        Try("mips.pixeldata_short", () => { copy.SetPixelData(new byte[1], 2); return "true"; });
        copy.Apply(false, true);
        Try("mips.unreadable", () => I(copy.GetPixels(1).Length));
        Try("mips.unreadable.pixel", () => C(copy.GetPixel(0, 0, 1)));
        Try("mips.unreadable.bilinear", () => C(copy.GetPixelBilinear(0.5f, 0.5f, 1)));
        Try("mips.unreadable.pixels32", () => I(copy.GetPixels32(1).Length));
        Try("mips.unreadable.raw", () => I(copy.GetRawTextureData().Length));
        Try("mips.unreadable.raw_bytes", () => "[" + string.Join(",", copy.GetRawTextureData()) + "]");
        Try("mips.unreadable.setpixel", () => { copy.SetPixel(0, 0, Color.white, 1); return "true"; });
        Try("mips.unreadable.setpixels", () => { copy.SetPixels(new Color[2], 1); return "true"; });
        Try("mips.unreadable.setpixels32", () => { copy.SetPixels32(new Color32[2], 1); return "true"; });
        Try("mips.unreadable.apply", () => { copy.Apply(); return "true"; });
        copy.filterMode = FilterMode.Point;
        material.SetFloat("_Level", 2);
        Graphics.Blit(copy, target, material);
        Value("mips.sampler_after_discard", C(Pixel(target)));
        foreach (bool linear in new[] { false, true })
        {
            var t = Add(new Texture2D(5, 3, TextureFormat.RGBA32, true, linear));
            var p = new Color[15];
            for (int i = 0; i < p.Length; i++) p[i] = new Color(i % 2, (i / 5) * 0.5f, 0.25f, (i % 3) * 0.5f);
            t.SetPixels(p);
            t.Apply(true);
            MipChain("mips.npot." + B(linear), t);
        }
        Clear();
    }
}
