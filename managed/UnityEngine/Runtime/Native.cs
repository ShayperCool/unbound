using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>A byte buffer owned by the native side; release it with <see cref="Native.Free"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeBuffer
    {
        internal byte* Data;
        internal long Length;
    }

    /// <summary>A rasterized glyph in pixels (FreeType's bitmap_left, bitmap_top and size).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphInfo
    {
        internal float Advance;
        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;
    }

    /// <summary>A glyph rendered by index: the bitmap as in <see cref="GlyphInfo"/> plus the
    /// outline's unrounded control box in pixels (relative to the pen on the baseline).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GlyphOutlineInfo
    {
        internal float Advance;
        internal float XMin;
        internal float YMin;
        internal float XMax;
        internal float YMax;
        internal int Left;
        internal int Top;
        internal int Width;
        internal int Height;
    }

    internal static unsafe class Native
    {
        static NativeApi s_Api;
        static bool s_Ready;

        internal static void Initialize(NativeApi* api)
        {
            if (api->Version != NativeApi.CurrentVersion)
                throw new InvalidOperationException($"native API version {api->Version}, managed expects {NativeApi.CurrentVersion}");
            s_Api = *api;
            s_Ready = true;
        }

        internal static bool IsReady => s_Ready;

        internal static void Log(int level, string text)
        {
            if (!s_Ready || s_Api.Log == null)
            {
                Console.Error.WriteLine(text);
                return;
            }
            // Every Debug.Log line comes here: short ones are encoded on the stack.
            int max = Encoding.UTF8.GetMaxByteCount(text.Length);
            byte[] rented = null;
            Span<byte> buffer = max <= 1024 ? stackalloc byte[1024] : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(max));
            int length = Encoding.UTF8.GetBytes(text, buffer);
            fixed (byte* p = buffer) s_Api.Log(level, p, length);
            if (rented != null) System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }

        /// <summary>A control call into the native side. Throws with the native error message.</summary>
        internal static string Call(string method, string request)
        {
            if (!s_Ready) throw new InvalidOperationException("the native side is not connected");
            long started = Trace.PerfOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { return CallCore(method, request); }
            finally { if (Trace.PerfOn) Trace.Perf("native " + method, started); }
        }

        static string CallCore(string method, string request)
        {
            var m = Encoding.UTF8.GetBytes(method);
            var r = Encoding.UTF8.GetBytes(request ?? "");
            NativeBuffer response;
            int status;
            fixed (byte* mp = m)
            fixed (byte* rp = r)
                status = s_Api.Call(mp, m.Length, rp, r.Length, &response);
            try
            {
                var text = response.Length == 0 ? "" : Encoding.UTF8.GetString(response.Data, (int)response.Length);
                if (status != 0) throw new InvalidOperationException($"native `{method}` failed: {text}");
                return text;
            }
            finally
            {
                Free(response);
            }
        }

        internal static void Free(NativeBuffer buffer)
        {
            if (buffer.Data != null && s_Api.FreeBuffer != null) s_Api.FreeBuffer(buffer);
        }

        /// <summary>The resolved documents of a file (`Assets/...` path or 32-hex guid).</summary>
        internal static System.Collections.Generic.List<YDocument> LoadDocuments(string pathOrGuid)
        {
            if (!s_Ready) throw new InvalidOperationException("the native side is not connected");
            var key = Encoding.UTF8.GetBytes(pathOrGuid);
            NativeBuffer buffer;
            int status;
            fixed (byte* k = key) status = s_Api.LoadDocuments(k, key.Length, &buffer);
            try
            {
                if (status != 0)
                {
                    var error = buffer.Length == 0 ? "unknown error" : Encoding.UTF8.GetString(buffer.Data, (int)buffer.Length);
                    throw new InvalidOperationException($"loading {pathOrGuid}: {error}");
                }
                return YBinary.DecodeDocuments(buffer.Data, buffer.Length);
            }
            finally
            {
                Free(buffer);
            }
        }

        internal static void SubmitCommands(byte* commands, long length)
        {
            if (s_Ready && length > 0) s_Api.SubmitCommands(commands, length);
        }

        /// <summary>The pixels of a rectangle of a texture (0: the screen) after every command so far:
        /// RGBA rows bottom first, 4 bytes per pixel or 4 floats when <paramref name="asFloat"/>;
        /// null without a GPU.</summary>
        internal static byte[] ReadPixels(ulong texture, int x, int y, int width, int height, bool asFloat)
        {
            if (!s_Ready) return null;
            GfxStream.Flush();
            NativeBuffer buffer;
            if (s_Api.ReadPixels(texture, x, y, width, height, asFloat ? 1 : 0, &buffer) == 0) return null;
            try
            {
                var bytes = new byte[buffer.Length];
                new ReadOnlySpan<byte>(buffer.Data, (int)buffer.Length).CopyTo(bytes);
                return bytes;
            }
            finally
            {
                Free(buffer);
            }
        }

        /// <summary>A buffer's bytes after every command so far; null without a GPU.</summary>
        internal static byte[] ReadBuffer(ulong buffer, long offset, long size)
        {
            if (!s_Ready) return null;
            GfxStream.Flush();
            NativeBuffer bytes;
            if (s_Api.ReadBuffer(buffer, offset, size, &bytes) == 0) return null;
            try
            {
                var result = new byte[bytes.Length];
                new ReadOnlySpan<byte>(bytes.Data, (int)bytes.Length).CopyTo(result);
                return result;
            }
            finally
            {
                Free(bytes);
            }
        }

        /// <summary>The frame's gamepad input into <paramref name="output"/> when it fits; returns its
        /// size (0 without SDL). <paramref name="family"/> is the Unity family, UTF-8.</summary>
        internal static int GamepadsState(ReadOnlySpan<byte> family, Span<byte> output)
        {
            if (!s_Ready) return 0;
            fixed (byte* f = family)
            fixed (byte* o = output)
                return s_Api.GamepadsState(f, family.Length, o, output.Length);
        }

        /// <summary>Decodes a video frame into its plane textures; false when it is not decoded (yet).
        /// Throws with the decoder's error.</summary>
        internal static bool VideoFrame(ulong decoder, long frame, ulong y, ulong u, ulong v)
        {
            if (!s_Ready) throw new InvalidOperationException("the native side is not connected");
            ulong* handles = stackalloc ulong[3] { y, u, v };
            NativeBuffer error = default;
            int status = s_Api.VideoFrame(decoder, frame, handles, &error);
            try
            {
                if (status < 0) throw new InvalidOperationException(error.Length == 0 ? "unknown error" : Encoding.UTF8.GetString(error.Data, (int)error.Length));
                return status > 0;
            }
            finally
            {
                Free(error);
            }
        }

        // Resource changes take effect in order with the rendering commands: what was recorded
        // before them executes first.

        internal static void UploadMesh(ulong handle, MeshPacket* mesh)
        {
            if (!s_Ready) return;
            GfxStream.Flush();
            s_Api.UploadMesh(handle, mesh);
        }

        internal static void ReleaseMesh(ulong handle)
        {
            if (!s_Ready || handle == 0) return;
            GfxStream.Flush();
            s_Api.ReleaseMesh(handle);
        }

        internal static void UploadTexture(ulong handle, TexturePacket* texture)
        {
            if (!s_Ready) return;
            GfxStream.Flush();
            s_Api.UploadTexture(handle, texture);
        }

        internal static bool ImportTexture(ulong handle, string guid, out ImportedTextureInfo info)
        {
            info = default;
            if (!s_Ready) return false;
            GfxStream.Flush();
            var bytes = Encoding.UTF8.GetBytes(guid);
            fixed (byte* p = bytes)
            fixed (ImportedTextureInfo* i = &info)
                return s_Api.ImportTexture(handle, p, bytes.Length, i) == 0;
        }

        internal static void ReleaseTexture(ulong handle)
        {
            if (!s_Ready || handle == 0) return;
            GfxStream.Flush();
            s_Api.ReleaseTexture(handle);
        }

        internal static void UpdateMaterial(ulong handle, MaterialPacket* material)
        {
            if (!s_Ready) return;
            GfxStream.Flush();
            s_Api.UpdateMaterial(handle, material);
        }

        internal static void ReleaseMaterial(ulong handle)
        {
            if (!s_Ready || handle == 0) return;
            GfxStream.Flush();
            s_Api.ReleaseMaterial(handle);
        }

        /// <summary>A glyph of a native font face; false when the face lacks it. The coverage
        /// (top row first) is null for an empty glyph.</summary>
        internal static bool FontGlyph(ulong font, uint codepoint, float size, int style, out GlyphInfo info, out byte[] coverage)
        {
            info = default;
            coverage = null;
            if (!s_Ready || font == 0) return false;
            NativeBuffer buffer;
            GlyphInfo glyph;
            int found = s_Api.FontGlyph(font, codepoint, size, style, &glyph, &buffer);
            try
            {
                info = glyph;
                if (buffer.Length > 0) coverage = new ReadOnlySpan<byte>(buffer.Data, (int)buffer.Length).ToArray();
                return found != 0;
            }
            finally
            {
                Free(buffer);
            }
        }
        /// <summary>The glyph index of a code point in a native font face; 0 when it lacks it.</summary>
        internal static uint FontGlyphIndex(ulong font, uint codepoint) =>
            s_Ready && font != 0 ? s_Api.FontGlyphIndex(font, codepoint) : 0;

        /// <summary>A glyph of a native font face by index; false when the face lacks it. The pixels
        /// (top row first) are null for an empty glyph; see <see cref="NativeApi.FontRenderGlyph"/>.</summary>
        internal static bool FontRenderGlyph(ulong font, uint glyph, float size, int sdfPadding, out GlyphOutlineInfo info, out byte[] pixels)
        {
            info = default;
            pixels = null;
            if (!s_Ready || font == 0) return false;
            NativeBuffer buffer;
            GlyphOutlineInfo rendered;
            int found = s_Api.FontRenderGlyph(font, glyph, size, sdfPadding, &rendered, &buffer);
            try
            {
                info = rendered;
                if (buffer.Length > 0) pixels = new ReadOnlySpan<byte>(buffer.Data, (int)buffer.Length).ToArray();
                return found != 0;
            }
            finally
            {
                Free(buffer);
            }
        }

        /// <summary>Decodes image file bytes (PNG, JPG, TGA, ...) to RGBA8, rows bottom first.</summary>
        internal static bool DecodeImage(ReadOnlySpan<byte> bytes, out int width, out int height, out bool jpeg, out Color32[] pixels)
        {
            width = height = 0;
            jpeg = false;
            pixels = null;
            if (!s_Ready || bytes.Length == 0) return false;
            NativeBuffer buffer;
            int w, h, j, ok;
            fixed (byte* data = bytes)
                ok = s_Api.ImageDecode(data, bytes.Length, &w, &h, &j, &buffer);
            try
            {
                if (ok == 0 || buffer.Length != (long)w * h * 4) return false;
                pixels = new Color32[w * h];
                new ReadOnlySpan<byte>(buffer.Data, (int)buffer.Length).CopyTo(MemoryMarshal.AsBytes(pixels.AsSpan()));
                width = w;
                height = h;
                jpeg = j != 0;
                return true;
            }
            finally
            {
                Free(buffer);
            }
        }

        /// <summary>Encodes RGBA8 (or float RGBA) pixels, rows bottom first: 0 PNG, 1 JPG, 2 TGA, 3 EXR.</summary>
        internal static byte[] EncodeImage<T>(int encoding, int quality, ReadOnlySpan<T> pixels, int width, int height) where T : unmanaged
        {
            if (!s_Ready) return null;
            bool isFloat = typeof(T) == typeof(float) || typeof(T) == typeof(Color);
            NativeBuffer buffer;
            int ok;
            fixed (T* data = pixels)
                ok = s_Api.ImageEncode(encoding, quality, (byte*)data, width, height, isFloat ? 1 : 0, &buffer);
            try
            {
                return ok != 0 && buffer.Length > 0 ? new ReadOnlySpan<byte>(buffer.Data, (int)buffer.Length).ToArray() : null;
            }
            finally
            {
                Free(buffer);
            }
        }

        /// <summary>The audio entries (valid once <see cref="IsReady"/>).</summary>
        internal static ref readonly NativeApi Api => ref s_Api;
    }
}
