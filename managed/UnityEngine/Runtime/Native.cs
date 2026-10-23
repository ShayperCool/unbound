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
        internal const uint CurrentVersion = 5;

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
        /// <summary>(render frame): hands the frame's draw lists to the renderer.</summary>
        internal delegate* unmanaged<void*, void> SubmitFrame;
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

        internal static void SubmitFrame(FramePacket* frame)
        {
            if (s_Ready && s_Api.SubmitFrame != null) s_Api.SubmitFrame(frame);
        }

        internal static void UploadMesh(ulong handle, MeshPacket* mesh)
        {
            if (s_Ready) s_Api.UploadMesh(handle, mesh);
        }

        internal static void ReleaseMesh(ulong handle)
        {
            if (s_Ready && handle != 0) s_Api.ReleaseMesh(handle);
        }

        internal static void UploadTexture(ulong handle, TexturePacket* texture)
        {
            if (s_Ready) s_Api.UploadTexture(handle, texture);
        }

        internal static bool ImportTexture(ulong handle, string guid, out ImportedTextureInfo info)
        {
            info = default;
            if (!s_Ready) return false;
            var bytes = Encoding.UTF8.GetBytes(guid);
            fixed (byte* p = bytes)
            fixed (ImportedTextureInfo* i = &info)
                return s_Api.ImportTexture(handle, p, bytes.Length, i) == 0;
        }

        internal static void ReleaseTexture(ulong handle)
        {
            if (s_Ready && handle != 0) s_Api.ReleaseTexture(handle);
        }

        internal static void UpdateMaterial(ulong handle, MaterialPacket* material)
        {
            if (s_Ready) s_Api.UpdateMaterial(handle, material);
        }

        internal static void ReleaseMaterial(ulong handle)
        {
            if (s_Ready && handle != 0) s_Api.ReleaseMaterial(handle);
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
