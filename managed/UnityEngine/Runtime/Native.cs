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

    /// <summary>
    /// The functions the native side (Rust) gives the engine at start. The layout is shared with
    /// `unbound-runtime/src/native_api.rs`; both sides check <see cref="Version"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NativeApi
    {
        internal const uint CurrentVersion = 11;

        internal uint Version;
        /// <summary>(level, utf8, length): writes a console line.</summary>
        internal delegate* unmanaged<int, byte*, int, void> Log;
        /// <summary>(method, length, request, length, out response): a control call with JSON
        /// in and out; returns 0 on success, else the response holds an error message.</summary>
        internal delegate* unmanaged<byte*, int, byte*, int, NativeBuffer*, int> Call;
        /// <summary>Releases a buffer the native side returned.</summary>
        internal delegate* unmanaged<NativeBuffer, void> FreeBuffer;
        /// <summary>(asset path or guid, length, out documents): the YAML documents of a scene,
        /// prefab or asset file with prefab instances expanded, in the binary node encoding.</summary>
        internal delegate* unmanaged<byte*, int, NativeBuffer*, int> LoadDocuments;
        /// <summary>(command stream, length): rendering commands, executed in order
        /// (<see cref="GfxStream"/>).</summary>
        internal delegate* unmanaged<byte*, long, void> SubmitCommands;
        internal delegate* unmanaged<ulong, MeshPacket*, void> UploadMesh;
        internal delegate* unmanaged<ulong, void> ReleaseMesh;
        internal delegate* unmanaged<ulong, TexturePacket*, void> UploadTexture;
        internal delegate* unmanaged<ulong, byte*, int, ImportedTextureInfo*, int> ImportTexture;
        internal delegate* unmanaged<ulong, void> ReleaseTexture;
        internal delegate* unmanaged<ulong, MaterialPacket*, void> UpdateMaterial;
        internal delegate* unmanaged<ulong, void> ReleaseMaterial;
        /// <summary>(font handle, code point, pixel size, FontStyle, out info, out coverage): 1 when
        /// the font has the glyph. Coverage is width * height bytes, top row first.</summary>
        internal delegate* unmanaged<ulong, uint, float, int, GlyphInfo*, NativeBuffer*, int> FontGlyph;
        // Version 4: audio (docs/AUDIO.md, Audio/AudioProtocol.cs).
        internal delegate* unmanaged<ulong, byte*, int, AudioClipInfo*, int> AudioImportClip;
        internal delegate* unmanaged<ulong, int, int, int, int, void> AudioCreateClip;
        internal delegate* unmanaged<ulong, int, float*, int, int, int> AudioClipData;
        internal delegate* unmanaged<ulong, void> AudioReleaseClip;
        internal delegate* unmanaged<ulong, AudioSourcePacket*, void> AudioUpdateSource;
        internal delegate* unmanaged<ulong, AudioSourceCommand*, void> AudioCommand;
        internal delegate* unmanaged<ulong, AudioSourceState*, void> AudioState;
        internal delegate* unmanaged<ulong, void> AudioReleaseSource;
        internal delegate* unmanaged<ulong, float, float, void> AudioUpdateGroup;
        internal delegate* unmanaged<AudioFramePacket*, AudioStatus*, void> AudioFrame;
        internal delegate* unmanaged<double> AudioDspTime;
        internal delegate* unmanaged<ulong, int, float*, int, int, int> AudioOutputData;
        internal delegate* unmanaged<delegate* unmanaged<int, ulong, float*, int, int, long, void>, void> AudioSetCallback;
        // Version 5: image bytes (ImageConversion).
        internal delegate* unmanaged<byte*, int, int*, int*, int*, NativeBuffer*, int> ImageDecode;
        internal delegate* unmanaged<int, int, void*, int, int, int, NativeBuffer*, int> ImageEncode;
        // Version 6: glyphs by index (TextCore's FontEngine).
        /// <summary>(font handle, code point): the glyph index, 0 when the face lacks it.</summary>
        internal delegate* unmanaged<ulong, uint, uint> FontGlyphIndex;
        /// <summary>(font handle, glyph index, pixel size, SDF padding, out info, out pixels): 1 when
        /// the face has the glyph. A negative padding gives coverage, else a signed distance field
        /// grown by the padding on every side.</summary>
        internal delegate* unmanaged<ulong, uint, float, int, GlyphOutlineInfo*, NativeBuffer*, int> FontRenderGlyph;
        // Version 9: the command stream replaces the frame packet.
        /// <summary>(texture handle, 0 for the screen; x, y, width, height; float; out pixels): 1 with
        /// the rectangle's RGBA pixels, rows bottom first, 4 bytes each (16 when float).</summary>
        internal delegate* unmanaged<ulong, int, int, int, int, int, NativeBuffer*, int> ReadPixels;
        // Version 10: buffers and compute.
        /// <summary>(buffer handle, offset, size, out bytes): 1 with the buffer's bytes after every
        /// command so far.</summary>
        internal delegate* unmanaged<ulong, long, long, NativeBuffer*, int> ReadBuffer;
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
            var bytes = Encoding.UTF8.GetBytes(text);
            fixed (byte* p = bytes) s_Api.Log(level, p, bytes.Length);
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
                ok = s_Api.ImageEncode(encoding, quality, data, width, height, isFloat ? 1 : 0, &buffer);
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
