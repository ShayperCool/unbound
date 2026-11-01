using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// GPU readbacks as the Editor completes them (tests/projects/gpubuffers): the data is the
    /// source's when the request is made (Unbound reads it then, the renderer running on the
    /// game's thread), but a request is done only at the next frame's
    /// <c>EarlyUpdate.UpdateAsyncReadbackManager</c>, which runs the callbacks in request order;
    /// the Editor completed them one or two frames later, as its GPU finished. The data stays
    /// available until the second readback update that sees the request done (the next frame's
    /// for one completed by the manager, the one after for one a wait completed), after which the
    /// request reads done with an error. A request that fails its checks logs, is done
    /// with an error and calls back before <c>Request</c> returns. <c>WaitForCompletion</c> and
    /// <c>WaitAllRequests</c> complete at once and call back before they return; <c>Update</c>
    /// completes nothing early. A native array a request fills cannot be accessed until it is done.
    /// </summary>
    internal static class AsyncReadbacks
    {
        internal sealed class Result
        {
            internal byte[][] Layers = Array.Empty<byte[]>();
            internal int Width, Height, Depth;
            internal bool HasError, Done;
            /// <summary>The frame it completes on, then the frame it completed on.</summary>
            internal int Frame;
            /// <summary>The frame whose readback update first saw it done (-1: none yet).</summary>
            internal int Seen = -1;
            internal Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> Callback;
            /// <summary>The native array the data goes into on completion, locked until then.</summary>
            internal unsafe void* Output;
            internal long OutputLength;
            internal Safety.ExternalWriteReservation OutputReservation;
            internal bool HasOutput;
        }

        static readonly Dictionary<long, Result> s_Live = new Dictionary<long, Result>();
        static readonly List<long> s_Pending = new List<long>();
        static readonly List<long> s_Completed = new List<long>();
        static long s_Next;
        static bool s_Registered;

        internal static Result Find(IntPtr id) => s_Live.TryGetValue((long)id, out var r) ? r : null;

        /// <summary>A failed request: logged, done with an error, called back now.</summary>
        internal static UnityEngine.Rendering.AsyncGPUReadbackRequest Fail(string message, Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback)
        {
            if (message != null) Debug.LogError(message);
            var request = Make(new Result { HasError = true, Done = true, Frame = Time.frameCount }, pending: false);
            Call(callback, request);
            return request;
        }

        /// <summary>A request whose data was read now; it completes at the next frame's update.</summary>
        internal static UnityEngine.Rendering.AsyncGPUReadbackRequest Start(Result result)
        {
            if (result.HasError) return Fail(null, result.Callback);
            result.Frame = Time.frameCount + 1;
            return Make(result, pending: true);
        }

        static UnityEngine.Rendering.AsyncGPUReadbackRequest Make(Result result, bool pending)
        {
            if (!s_Registered)
            {
                s_Registered = true;
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateAsyncReadbackManager), Update);
            }
            long id = ++s_Next;
            s_Live[id] = result;
            if (pending) s_Pending.Add(id);
            else s_Completed.Add(id);
            return new UnityEngine.Rendering.AsyncGPUReadbackRequest { m_Ptr = (IntPtr)id, m_Version = 1 };
        }

        static void Call(Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback, UnityEngine.Rendering.AsyncGPUReadbackRequest request)
        {
            if (callback == null) return;
            try { callback(request); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static unsafe void Complete(long id, bool update = false)
        {
            if (!s_Live.TryGetValue(id, out var result) || result.Done) return;
            s_Pending.Remove(id);
            result.Done = true;
            result.Frame = Time.frameCount;
            if (update) result.Seen = Time.frameCount;
            if (result.HasOutput)
            {
                try
                {
                    long at = 0;
                    foreach (var layer in result.Layers)
                    {
                        long n = Math.Min(layer.Length, result.OutputLength - at);
                        if (n <= 0) break;
                        fixed (byte* p = layer) UnsafeUtility.MemCpy((byte*)result.Output + at, p, n);
                        at += n;
                    }
                }
                finally
                {
                    result.OutputReservation.Dispose();
                    result.OutputReservation = null;
                    result.HasOutput = false;
                    result.Output = null;
                }
            }
            s_Completed.Add(id);
            Call(result.Callback, new UnityEngine.Rendering.AsyncGPUReadbackRequest { m_Ptr = (IntPtr)id, m_Version = 1 });
        }

        /// <summary>EarlyUpdate.UpdateAsyncReadbackManager: releases what completed in an earlier
        /// frame, then completes the requests due.</summary>
        static void Update()
        {
            int frame = Time.frameCount;
            for (int i = s_Completed.Count - 1; i >= 0; i--)
            {
                long id = s_Completed[i];
                if (s_Live.TryGetValue(id, out var r) && (r.Seen < 0 || r.Seen >= frame))
                {
                    if (r.Seen < 0) r.Seen = frame;
                    continue;
                }
                s_Live.Remove(id);
                s_Completed.RemoveAt(i);
            }
            if (s_Pending.Count == 0) return;
            foreach (var id in s_Pending.ToArray())
                if (s_Live.TryGetValue(id, out var r) && r.Frame <= frame) Complete(id, update: true);
        }

        internal static void Wait(IntPtr id) => Complete((long)id);

        internal static void WaitAll()
        {
            foreach (var id in s_Pending.ToArray()) Complete(id);
        }

        /// <summary>Locks <paramref name="output"/> until the request is done, then copies the data
        /// into it; a smaller array fails the request.</summary>
        internal static unsafe UnityEngine.Rendering.AsyncGPUReadbackRequest Into<T>(ref NativeArray<T> output, Result result) where T : struct
        {
            long length = (long)output.Length * UnsafeUtility.SizeOf<T>();
            if (!result.HasError)
            {
                if (output.m_AllocatorLabel != Allocator.Persistent && output.m_AllocatorLabel != Allocator.TempJob)
                    throw new ArgumentException("Unbound: a GPU readback destination must own Persistent or TempJob memory; frame-local and borrowed storage are not supported.", nameof(output));
                long size = 0;
                foreach (var layer in result.Layers) size += layer.Length;
                if (size > length)
                    return Fail($"Size of source buffer data ({size} bytes) is larger than the destination nativeArray ({length} bytes).", result.Callback);
                result.OutputReservation = Safety.ReserveExternalWrite(output.m_Safety);
                result.Output = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(output);
                result.OutputLength = length;
                result.HasOutput = true;
            }
            return Start(result);
        }

        // ------------------------------------------------------------ sources

        internal static Result Buffer(GpuBufferStorage buffer, int size, int offset, Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback)
        {
            if (buffer == null || buffer.Released) throw new ArgumentNullException("buffer");
            var result = new Result { Callback = callback };
            if (size <= 0 || offset < 0 || (long)offset + size > buffer.Size)
            {
                Debug.LogError($"AsyncGPUReadback - Out of bounds arguments - src offset:{offset} dst size:{size} src size:{buffer.Size}");
                result.HasError = true;
                return result;
            }
            var bytes = Native.ReadBuffer(buffer.Handle, offset, size);
            if (bytes == null) return Unavailable(result);
            result.Layers = new[] { bytes };
            result.Width = size;
            result.Height = result.Depth = 1;
            return result;
        }

        static Result Unavailable(Result result)
        {
            Debug.LogWarning("Unbound: reading back from the GPU needs a renderer (a headless run has none); the request fails.");
            result.HasError = true;
            return result;
        }

        /// <summary>The layers a texture has at a mip level: array slices, cube faces (times
        /// cubes) or a volume's depth at that level.</summary>
        static int Layers(Texture src, int mip)
        {
            switch (src)
            {
                case Texture3D t: return Math.Max(1, t.depth >> mip);
                case Texture2DArray t: return t.depth;
                case Cubemap _: return 6;
                case CubemapArray t: return t.cubemapCount * 6;
                case RenderTexture rt:
                    if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex3D) return Math.Max(1, rt.volumeDepth >> mip);
                    if (rt.dimension == UnityEngine.Rendering.TextureDimension.Cube) return 6;
                    if (rt.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray || rt.dimension == UnityEngine.Rendering.TextureDimension.CubeArray) return rt.volumeDepth;
                    return 1;
                default: return 1;
            }
        }

        internal static int LayerCount(Texture src, int mip) => src == null ? 1 : Layers(src, Math.Max(0, mip));

        /// <summary>Whether the declared format keeps 8 bits per channel: those read as the stored
        /// bytes; every other format reads as floats, which the renderer converts from whatever it
        /// stores the texture in (its storage may differ from the declared format).</summary>
        static bool EightBit(GraphicsFormat format) =>
            !GraphicsFormatUtility.IsHalfFormat(format) && !GraphicsFormatUtility.IsIEEE754Format(format)
            && GraphicsFormatUtility.GetComponentCount(format) > 0
            && GraphicsFormatUtility.GetBlockSize(format) == GraphicsFormatUtility.GetComponentCount(format);

        internal static Result Texture(Texture src, int mip, int x, int width, int y, int height, int z, int depth, ReadbackFormat format,
            Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> callback)
        {
            if (src == null) throw new ArgumentNullException("src");
            var result = new Result { Callback = callback };
            if (mip < 0 || mip >= Math.Max(1, src.mipmapCount))
            {
                Debug.LogError($"AsyncGPUReadback - Invalid mipIndex: {mip}");
                result.HasError = true;
                return result;
            }
            int lw = Math.Max(1, src.width >> mip), lh = Math.Max(1, src.height >> mip), ld = Layers(src, mip);
            if (x < 0 || y < 0 || z < 0 || width <= 0 || height <= 0 || depth <= 0 || x + width > lw || y + height > lh || z + depth > ld)
            {
                Debug.LogError($"AsyncGPUReadback - Out of bounds arguments - src offset:({x},{y},{z}) dst dim:({width},{height},{depth}) src dim:({lw},{lh},{ld})");
                result.HasError = true;
                return result;
            }
            if (format == null || GraphicsFormatUtility.IsCompressedFormat(src.graphicsFormat))
            {
                Debug.LogError("Unbound: AsyncGPUReadback into this format is not supported; the request fails.");
                result.HasError = true;
                return result;
            }
            ulong handle = TextureHandles.Of(src);
            bool floats = !EightBit(src.graphicsFormat);
            bool alpha = src is Texture2D a8 && a8.format == TextureFormat.Alpha8;
            result.Layers = new byte[depth][];
            for (int layer = 0; layer < depth; layer++)
            {
                var texels = handle == 0 ? null : Native.ReadTexture(handle, mip, z + layer, x, y, width, height, floats);
                if (texels == null) return Unavailable(result);
                result.Layers[layer] = format.Encode(texels, width * height, floats, alpha);
            }
            result.Width = width;
            result.Height = height;
            result.Depth = depth;
            return result;
        }
    }

    /// <summary>The layout a texture readback is written in: channels in order, 8-bit, half or
    /// float, from the source's RGBA (bytes of 8-bit textures as they are, as floats b * (1 / 255) in
    /// single precision; floats round to bytes).</summary>
    internal sealed class ReadbackFormat
    {
        /// <summary>Source channel (0 r, 1 g, 2 b, 3 a) of each output channel.</summary>
        readonly int[] m_Channels;
        /// <summary>1 byte, 2 half, 4 float per channel.</summary>
        readonly int m_Size;

        ReadbackFormat(int size, params int[] channels) { m_Size = size; m_Channels = channels; }

        static readonly ReadbackFormat Rgba8 = new ReadbackFormat(1, 0, 1, 2, 3), Bgra8 = new ReadbackFormat(1, 2, 1, 0, 3),
            Argb8 = new ReadbackFormat(1, 3, 0, 1, 2), R8 = new ReadbackFormat(1, 0), Rg8 = new ReadbackFormat(1, 0, 1), Rgb8 = new ReadbackFormat(1, 0, 1, 2),
            A8 = new ReadbackFormat(1, 3), RgbaHalf = new ReadbackFormat(2, 0, 1, 2, 3), RHalf = new ReadbackFormat(2, 0), RgHalf = new ReadbackFormat(2, 0, 1),
            RgbaFloat = new ReadbackFormat(4, 0, 1, 2, 3), RFloat = new ReadbackFormat(4, 0), RgFloat = new ReadbackFormat(4, 0, 1);

        internal static ReadbackFormat Of(TextureFormat format) => format switch
        {
            TextureFormat.RGBA32 => Rgba8,
            TextureFormat.BGRA32 => Bgra8,
            TextureFormat.ARGB32 => Argb8,
            TextureFormat.RGB24 => Rgb8,
            TextureFormat.R8 => R8,
            TextureFormat.RG16 => Rg8,
            TextureFormat.Alpha8 => A8,
            TextureFormat.RGBAHalf => RgbaHalf,
            TextureFormat.RHalf => RHalf,
            TextureFormat.RGHalf => RgHalf,
            TextureFormat.RGBAFloat => RgbaFloat,
            TextureFormat.RFloat => RFloat,
            TextureFormat.RGFloat => RgFloat,
            _ => null,
        };

        internal static ReadbackFormat Of(GraphicsFormat format) => format switch
        {
            GraphicsFormatUtility.Texture2DAlpha8 => A8,
            GraphicsFormat.R8G8B8A8_UNorm or GraphicsFormat.R8G8B8A8_SRGB => Rgba8,
            GraphicsFormat.B8G8R8A8_UNorm or GraphicsFormat.B8G8R8A8_SRGB => Bgra8,
            GraphicsFormat.R8G8B8_UNorm or GraphicsFormat.R8G8B8_SRGB => Rgb8,
            GraphicsFormat.R8_UNorm or GraphicsFormat.R8_SRGB => R8,
            GraphicsFormat.R8G8_UNorm or GraphicsFormat.R8G8_SRGB => Rg8,
            GraphicsFormat.R16G16B16A16_SFloat => RgbaHalf,
            GraphicsFormat.R16_SFloat => RHalf,
            GraphicsFormat.R16G16_SFloat => RgHalf,
            GraphicsFormat.R32G32B32A32_SFloat => RgbaFloat,
            GraphicsFormat.R32_SFloat => RFloat,
            GraphicsFormat.R32G32_SFloat => RgFloat,
            _ => null,
        };

        /// <summary><paramref name="texels"/> as the renderer reads them: 4 bytes per texel of an
        /// 8-bit RGBA texture (1 of a one-channel one), 4 floats when <paramref name="floats"/>.</summary>
        internal byte[] Encode(byte[] texels, int count, bool floats, bool alpha = false)
        {
            int inChannels = floats ? 4 : Math.Max(1, texels.Length / Math.Max(1, count));
            var output = new byte[count * m_Channels.Length * m_Size];
            int o = 0;
            for (int i = 0; i < count; i++)
            {
                foreach (int c in m_Channels)
                {
                    float f;
                    byte b;
                    if (floats)
                    {
                        f = BitConverter.ToSingle(texels, (i * 4 + c) * 4);
                        b = (byte)Math.Round(Math.Clamp(f, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
                    }
                    else
                    {
                        // A one-channel texture reads (r, 0, 0, 1); Alpha8 (0, 0, 0, a).
                        b = inChannels == 4 ? texels[i * 4 + c]
                            : alpha ? (c == 3 ? texels[i] : (byte)0)
                            : c == 0 ? texels[i] : c == 3 ? (byte)255 : (byte)0;
                        f = b * (1f / 255f);
                    }
                    switch (m_Size)
                    {
                        case 1: output[o] = b; break;
                        case 2: BitConverter.TryWriteBytes(new Span<byte>(output, o, 2), (Half)f); break;
                        default: BitConverter.TryWriteBytes(new Span<byte>(output, o, 4), f); break;
                    }
                    o += m_Size;
                }
            }
            return output;
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial struct AsyncGPUReadbackRequest
    {
        internal IntPtr m_Ptr;
        internal int m_Version;

        /// <summary>The request's state; null once its data was released (or for a default request).</summary>
        AsyncReadbacks.Result Result => m_Ptr != IntPtr.Zero ? AsyncReadbacks.Find(m_Ptr) : null;

        [Unbound.EditorFaithful("the Editor completes a request in the readback manager's update, not here")]
        public void Update() { }
        public void WaitForCompletion()
        {
            if (m_Ptr != IntPtr.Zero) AsyncReadbacks.Wait(m_Ptr);
        }

        public bool done => m_Ptr != IntPtr.Zero && (Result?.Done ?? true);
        public bool hasError => Result is not { } r || r.HasError;
        public int layerCount => Available ? Result.Layers.Length : 0;
        public int layerDataSize => Available && Result.Layers.Length > 0 ? Result.Layers[0].Length : 0;
        public int width => Available ? Result.Width : 0;
        public int height => Available ? Result.Height : 0;
        public int depth => Available ? Result.Depth : 0;
        public bool forcePlayerLoopUpdate { get; set; }

        bool Available => Result is { } r && !r.HasError;

        /// <summary>A layer's data as <typeparamref name="T"/>, for the frame the request completed in.</summary>
        public unsafe NativeArray<T> GetData<T>(int layer = 0) where T : struct
        {
            var result = Result;
            if (result == null || !result.Done || result.HasError) throw new InvalidOperationException("Cannot access the data as it is not available");
            if (layer < 0 || layer >= result.Layers.Length) throw new ArgumentException($"Layer index is out of range {layer} / {result.Layers.Length}");
            var bytes = result.Layers[layer];
            int size = UnsafeUtility.SizeOf<T>();
            if (bytes.Length % size != 0) throw new ArgumentException($"Type {typeof(T)} doesn't divide the layer's {bytes.Length} bytes");
            var array = new NativeArray<T>(bytes.Length / size, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            fixed (byte* p = bytes) UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafePtr(array), p, bytes.Length);
            return array;
        }
    }

    public static partial class AsyncGPUReadback
    {
        public static void WaitAllRequests() => AsyncReadbacks.WaitAll();

        static AsyncReadbacks.Result Buffer(GpuBufferStorage src, int size, int offset, Action<AsyncGPUReadbackRequest> callback) =>
            AsyncReadbacks.Buffer(src, size, offset, callback);

        public static AsyncGPUReadbackRequest Request(ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest Request(ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest Request(GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest Request(GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Buffer(src?.Storage, size, offset, callback));

        static AsyncReadbacks.Result Texture(Texture src, int mipIndex, ReadbackFormat format, Action<AsyncGPUReadbackRequest> callback)
        {
            if (src == null) throw new ArgumentNullException("src");
            int w = Math.Max(1, src.width >> Math.Max(0, mipIndex)), h = Math.Max(1, src.height >> Math.Max(0, mipIndex));
            return AsyncReadbacks.Texture(src, mipIndex, 0, w, 0, h, 0, AsyncReadbacks.LayerCount(src, mipIndex), format, callback);
        }

        static ReadbackFormat Own(Texture src) => src == null ? null : ReadbackFormat.Of(src.graphicsFormat);

        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Texture(src, mipIndex, Own(src), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, Own(src), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback));
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            AsyncReadbacks.Start(AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback));

        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, (int)(src?.Storage?.Size ?? 0), 0, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Buffer(src?.Storage, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Texture(src, mipIndex, Own(src), callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, Texture(src, mipIndex, ReadbackFormat.Of(dstFormat), callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            AsyncReadbacks.Into(ref output, AsyncReadbacks.Texture(src, mipIndex, x, width, y, height, z, depth, ReadbackFormat.Of(dstFormat), callback));
    }
}
