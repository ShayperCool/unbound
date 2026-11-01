using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TextureRawProbe
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ThreeBytes { public byte A, B, C; }
    static readonly List<string> Rows = new List<string>();
    static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static void Value(string key, string value) => Rows.Add("{\"k\":" + Json(key) + ",\"v\":" + Json(value) + "}");
    static void Try(string key, Func<string> get)
    {
        try { Value(key, get()); }
        catch (Exception e) { Value(key, "throws:" + e.GetType().Name); }
    }
    static string Bytes(NativeArray<byte> data) => Convert.ToBase64String(data.ToArray());
    static string Pixels(Texture2D texture, int mip)
    {
        var bytes = new List<byte>();
        foreach (Color c in texture.GetPixels(mip))
            foreach (float f in new[] { c.r, c.g, c.b, c.a }) bytes.AddRange(BitConverter.GetBytes(f));
        return Convert.ToBase64String(bytes.ToArray());
    }
    static int Stride(TextureFormat format)
    {
        switch (format)
        {
            case TextureFormat.R8: return 1;
            case TextureFormat.RGB24: return 3;
            case TextureFormat.RGBA32: return 4;
            case TextureFormat.RGB565:
            case TextureFormat.R16:
            case TextureFormat.RHalf: return 2;
            case TextureFormat.RGFloat: return 8;
            case TextureFormat.RGBAFloat: return 16;
            default: throw new ArgumentException();
        }
    }
    static byte[] Seed(TextureFormat format, int count, int mip)
    {
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = (byte)(i * 37 + mip * 53 + 11);
        if (format == TextureFormat.RHalf)
            for (int i = 0; i < count; i += 2) { bytes[i] = (byte)(i + mip); bytes[i + 1] = 0x38; }
        if (format == TextureFormat.RGFloat || format == TextureFormat.RGBAFloat)
            for (int i = 0; i < count; i += 4) BitConverter.GetBytes((i + mip + 1) * 0.125f).CopyTo(bytes, i);
        return bytes;
    }
    static void Format(TextureFormat format)
    {
        string key = format.ToString();
        var texture = new Texture2D(4, 2, format, true, true);
        try
        {
            int total = 0;
            for (int mip = 0; mip < texture.mipmapCount; mip++)
            {
                int size = Math.Max(1, 4 >> mip) * Math.Max(1, 2 >> mip) * Stride(format);
                texture.SetPixelData(Seed(format, size, mip), mip);
                total += size;
            }
            Value(key + ".mips", I(texture.mipmapCount));
            Value(key + ".bytes", I(total));
            Try(key + ".raw", () => Bytes(texture.GetRawTextureData<byte>()));
            Try(key + ".ushort.length", () => I(texture.GetRawTextureData<ushort>().Length));
            Try(key + ".float.length", () => I(texture.GetRawTextureData<float>().Length));
            Try(key + ".three.length", () => I(texture.GetRawTextureData<ThreeBytes>().Length));
            Try(key + ".nongeneric", () => Convert.ToBase64String(texture.GetRawTextureData()));
            // Keep both views alive while writes cross each mip boundary in both directions.
            Try(key + ".alias", () =>
            {
                var raw = texture.GetRawTextureData<byte>();
                int offset = 0;
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                {
                    var pixels = texture.GetPixelData<byte>(mip);
                    raw[offset] ^= 1;
                    Value(key + ".mip" + mip + ".raw_to_pixel", Bytes(pixels));
                    Value(key + ".mip" + mip + ".raw_to_colors", Pixels(texture, mip));
                    pixels[pixels.Length - 1] = 0x3f;
                    Value(key + ".mip" + mip + ".pixel_to_raw", Bytes(raw));
                    var colors = texture.GetPixels(mip);
                    for (int i = 0; i < colors.Length; i++) colors[i] = new Color(1f, 0.5f, 0.25f, 1f);
                    texture.SetPixels(colors, mip);
                    Value(key + ".mip" + mip + ".colors_to_raw", Bytes(raw));
                    Value(key + ".mip" + mip + ".colors_to_pixel", Bytes(pixels));
                    offset += pixels.Length;
                }
                return Bytes(texture.GetRawTextureData<byte>());
            });
        }
        finally { Object.DestroyImmediate(texture); }
    }
    static string CheckView(NativeArray<byte> view)
    {
        AtomicSafetyHandle.CheckReadAndThrow(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(view));
        return "readable";
    }
    static void Lifetime()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
        try
        {
            // GetPixelData and the non-generic API are controls, with their own readability rules.
            var raw = texture.GetRawTextureData<byte>();
            var pixel = texture.GetPixelData<byte>(0);
            Value("reinitialize.result", texture.Reinitialize(3, 1, TextureFormat.RGB24, true).ToString());
            Try("reinitialize.old_raw", () => I(raw[0]));
            Try("reinitialize.old_pixel", () => I(pixel[0]));
            Try("reinitialize.new_length", () => I(texture.GetRawTextureData<byte>().Length));
            raw = texture.GetRawTextureData<byte>();
            pixel = texture.GetPixelData<byte>(0);
            texture.Apply(false, true);
            Value("unreadable.readable", texture.isReadable.ToString());
            Try("unreadable.old_raw", () => CheckView(raw));
            Try("unreadable.old_pixel", () => CheckView(pixel));
            Try("unreadable.raw_byte", () => I(texture.GetRawTextureData<byte>().Length));
            Try("unreadable.raw_three", () => I(texture.GetRawTextureData<ThreeBytes>().Length));
            Try("unreadable.pixel", () => I(texture.GetPixelData<byte>(0).Length));
            Try("unreadable.nongeneric", () => I(texture.GetRawTextureData().Length));
        }
        finally { Object.DestroyImmediate(texture); }
        var destroyed = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        var view = destroyed.GetRawTextureData<byte>();
        var p = destroyed.GetPixelData<byte>(0);
        Object.DestroyImmediate(destroyed);
        Try("destroy.old_raw", () => I(view[0]));
        Try("destroy.old_pixel", () => I(p[0]));
    }
    public static void Record()
    {
        Rows.Clear();
        foreach (var format in new[] { TextureFormat.R8, TextureFormat.RGB24, TextureFormat.RGBA32,
            TextureFormat.RGB565, TextureFormat.R16, TextureFormat.RHalf, TextureFormat.RGFloat, TextureFormat.RGBAFloat })
            Try(format + ".case", () => { Format(format); return "ok"; });
        Try("lifetime.case", () => { Lifetime(); return "ok"; });
        Value("done", "ok");
        File.WriteAllLines(Environment.GetEnvironmentVariable("TEXTURE_RAW_PROBE_OUT"), Rows);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Start() { Record(); }
}
