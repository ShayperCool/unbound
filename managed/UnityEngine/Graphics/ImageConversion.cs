using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        [Flags]
        public enum EXRFlags
        {
            None = 0,
            OutputAsFloat = 1,
            CompressZIP = 2,
            CompressRLE = 4,
            CompressPIZ = 8,
        }
    }

    /// <summary>
    /// Image files in and out of textures (the native side decodes and encodes). LoadImage replaces a
    /// texture's size and pixels: JPGs load as RGB24, other images as RGBA32; the encoders read the
    /// texture's pixels, which must be readable.
    /// </summary>
    public static partial class ImageConversion
    {
        public static bool EnableLegacyPngGammaRuntimeLoadBehavior { get; set; }

        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable)
        {
            if (tex == null) throw new NullReferenceException("The texture is null");
            if (data == null) throw new ArgumentNullException(nameof(data));
            return LoadImage(tex, (ReadOnlySpan<byte>)data, markNonReadable);
        }

        public static bool LoadImage(this Texture2D tex, byte[] data) => LoadImage(tex, data, false);

        public static bool LoadImage(this Texture2D tex, ReadOnlySpan<byte> data) => LoadImage(tex, data, false);

        public static bool LoadImage(this Texture2D tex, ReadOnlySpan<byte> data, bool markNonReadable)
        {
            if (tex == null) throw new NullReferenceException("The texture is null");
            if (!Native.DecodeImage(data, out int width, out int height, out bool jpeg, out var pixels))
            {
                // Unity shows a red-and-white "?" texture for data it cannot read; a blank 8x8 here.
                tex.Reinitialize(8, 8, TextureFormat.RGBA32, tex.mipmapCount > 1);
                return false;
            }
            tex.Reinitialize(width, height, jpeg ? TextureFormat.RGB24 : TextureFormat.RGBA32, tex.mipmapCount > 1);
            tex.SetPixels32(pixels);
            tex.Apply(true, markNonReadable);
            return true;
        }

        static Color32[] PixelsOf(Texture2D tex)
        {
            if (tex == null) throw new NullReferenceException("The texture is null");
            if (!tex.isReadable) throw new ArgumentException($"Texture '{tex.name}' is not readable, the texture memory can not be accessed from scripts. You can make the texture readable in the Texture Import Settings.");
            return tex.GetPixels32();
        }

        public static byte[] EncodeToPNG(this Texture2D tex) => Native.EncodeImage<Color32>(0, 0, PixelsOf(tex), tex.width, tex.height);

        public static byte[] EncodeToJPG(this Texture2D tex, int quality) => Native.EncodeImage<Color32>(1, Mathf.Clamp(quality, 1, 100), PixelsOf(tex), tex.width, tex.height);

        public static byte[] EncodeToJPG(this Texture2D tex) => EncodeToJPG(tex, 75);

        public static byte[] EncodeToTGA(this Texture2D tex) => Native.EncodeImage<Color32>(2, 0, PixelsOf(tex), tex.width, tex.height);

        public static byte[] EncodeToEXR(this Texture2D tex, Texture2D.EXRFlags flags)
        {
            if (tex == null) throw new NullReferenceException("The texture is null");
            if (!tex.isReadable) throw new ArgumentException($"Texture '{tex.name}' is not readable, the texture memory can not be accessed from scripts.");
            return Native.EncodeImage<Color>(3, 0, tex.GetPixels(), tex.width, tex.height);
        }

        public static byte[] EncodeToEXR(this Texture2D tex) => EncodeToEXR(tex, Texture2D.EXRFlags.None);
    }
}
