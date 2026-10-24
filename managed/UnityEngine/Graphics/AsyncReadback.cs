using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unbound.Runtime;
using UnityEngine.Experimental.Rendering;

namespace Unbound.Runtime
{
    /// <summary>Readbacks: Unbound reads the GPU at once (the renderer runs on the game's thread), so a
    /// request is done when made; its callback runs at the next frame's
    /// EarlyUpdate.UpdateAsyncReadbackManager, as Unity's completed requests call back.</summary>
    internal static class AsyncReadbacks
    {
        internal sealed class Result
        {
            internal byte[][] Layers = Array.Empty<byte[]>();
            internal int Width = 1, Height = 1, Depth = 1;
            internal bool HasError;
            internal Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> Callback;
            internal readonly List<IntPtr> Arrays = new List<IntPtr>();
        }

        static readonly List<(UnityEngine.Rendering.AsyncGPUReadbackRequest Request, Result Result)> s_Pending =
            new List<(UnityEngine.Rendering.AsyncGPUReadbackRequest, Result)>();
        static bool s_Registered;

        internal static UnityEngine.Rendering.AsyncGPUReadbackRequest Make(Result result)
        {
            var request = new UnityEngine.Rendering.AsyncGPUReadbackRequest { m_Ptr = GCHandle.ToIntPtr(GCHandle.Alloc(result)), m_Version = 1 };
            if (result.Callback != null)
            {
                if (!s_Registered)
                {
                    s_Registered = true;
                    PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateAsyncReadbackManager), Update);
                }
                s_Pending.Add((request, result));
            }
            return request;
        }

        static void Update()
        {
            if (s_Pending.Count == 0) return;
            var due = s_Pending.ToArray();
            s_Pending.Clear();
            foreach (var (request, result) in due)
            {
                try { result.Callback(request); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        internal static void WaitAll() => Update();
    }
}

namespace UnityEngine.Rendering
{
    public partial struct AsyncGPUReadbackRequest
    {
        internal IntPtr m_Ptr;
        internal int m_Version;

        AsyncReadbacks.Result Result =>
            m_Ptr != IntPtr.Zero ? (AsyncReadbacks.Result)GCHandle.FromIntPtr(m_Ptr).Target : throw new InvalidOperationException("The request is not valid.");

        public void Update() { }
        public void WaitForCompletion() { }

        public bool done => m_Ptr != IntPtr.Zero;
        public bool hasError => m_Ptr == IntPtr.Zero || Result.HasError;
        public int layerCount => m_Ptr != IntPtr.Zero ? Result.Layers.Length : 0;
        public int layerDataSize => m_Ptr != IntPtr.Zero && Result.Layers.Length > 0 ? Result.Layers[0].Length : 0;
        public int width => m_Ptr != IntPtr.Zero ? Result.Width : 0;
        public int height => m_Ptr != IntPtr.Zero ? Result.Height : 0;
        public int depth => m_Ptr != IntPtr.Zero ? Result.Depth : 0;
        public bool forcePlayerLoopUpdate { get; set; }

        /// <summary>A layer's data as <typeparamref name="T"/> (valid until the frame ends, as Unity's).</summary>
        public unsafe NativeArray<T> GetData<T>(int layer = 0) where T : struct
        {
            var result = Result;
            if (result.HasError) throw new InvalidOperationException("Cannot access the data as it is not available");
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

        static AsyncGPUReadbackRequest Buffer(ulong handle, long size, long offset, Action<AsyncGPUReadbackRequest> callback)
        {
            var result = new AsyncReadbacks.Result { Width = (int)size, Callback = callback };
            var bytes = Native.ReadBuffer(handle, offset, size);
            if (bytes == null) result.HasError = true;
            else result.Layers = new[] { bytes };
            return AsyncReadbacks.Make(result);
        }

        public static AsyncGPUReadbackRequest Request(ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) =>
            Request(src, src?.count * src?.stride ?? 0, 0, callback);
        public static AsyncGPUReadbackRequest Request(ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            return Buffer(src.Handle, size, offset, callback);
        }

        public static AsyncGPUReadbackRequest Request(GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) =>
            Request(src, src?.count * src?.stride ?? 0, 0, callback);
        public static AsyncGPUReadbackRequest Request(GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            return Buffer(src.Handle, size, offset, callback);
        }

        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) =>
            Request(src, mipIndex, src?.graphicsFormat ?? GraphicsFormat.R8G8B8A8_SRGB, callback);
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            Request(src, mipIndex, GraphicsFormatUtility.GetGraphicsFormat(dstFormat, false), callback);
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            int w = Math.Max(1, src.width >> mipIndex), h = Math.Max(1, src.height >> mipIndex);
            return Request(src, mipIndex, 0, w, 0, h, 0, 1, dstFormat, callback);
        }
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, Action<AsyncGPUReadbackRequest> callback = null) =>
            Request(src, mipIndex, x, width, y, height, z, depth, src?.graphicsFormat ?? GraphicsFormat.R8G8B8A8_SRGB, callback);
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) =>
            Request(src, mipIndex, x, width, y, height, z, depth, GraphicsFormatUtility.GetGraphicsFormat(dstFormat, false), callback);

        /// <summary>The request's data copied into <paramref name="output"/> as well.</summary>
        static unsafe AsyncGPUReadbackRequest Into<T>(void* output, long length, AsyncGPUReadbackRequest request) where T : struct
        {
            if (request.hasError) return request;
            var data = request.GetData<byte>();
            if (data.Length > length) throw new ArgumentException($"The output buffer ({length} bytes) is smaller than the data ({data.Length} bytes)");
            UnsafeUtility.MemCpy(output, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), data.Length);
            return request;
        }

        static unsafe AsyncGPUReadbackRequest IntoArray<T>(ref NativeArray<T> output, AsyncGPUReadbackRequest request) where T : struct =>
            Into<T>(NativeArrayUnsafeUtility.GetUnsafePtr(output), (long)output.Length * UnsafeUtility.SizeOf<T>(), request);

        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, ComputeBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, ComputeBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, GraphicsBuffer src, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, GraphicsBuffer src, int size, int offset, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, size, offset, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex = 0, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, mipIndex, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, mipIndex, dstFormat, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, mipIndex, dstFormat, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, mipIndex, x, width, y, height, z, depth, dstFormat, callback));
        public static AsyncGPUReadbackRequest RequestIntoNativeArray<T>(ref NativeArray<T> output, Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null) where T : struct =>
            IntoArray(ref output, Request(src, mipIndex, x, width, y, height, z, depth, dstFormat, callback));

        /// <summary>A region of a texture's first mip, read as RGBA bytes or, for float formats,
        /// 32-bit floats (16-bit for half formats).</summary>
        public static AsyncGPUReadbackRequest Request(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat, Action<AsyncGPUReadbackRequest> callback = null)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            var result = new AsyncReadbacks.Result { Width = width, Height = height, Depth = depth, Callback = callback };
            bool half = dstFormat is GraphicsFormat.R16G16B16A16_SFloat or GraphicsFormat.R16_SFloat or GraphicsFormat.R16G16_SFloat;
            bool full = dstFormat is GraphicsFormat.R32G32B32A32_SFloat or GraphicsFormat.R32_SFloat or GraphicsFormat.R32G32_SFloat;
            ulong handle = TextureHandles.Of(src);
            if (mipIndex != 0 || depth != 1 || handle == 0) result.HasError = true;
            else
            {
                var bytes = Native.ReadPixels(handle, x, y, width, height, half || full);
                if (bytes == null) result.HasError = true;
                else if (half)
                {
                    var halves = new byte[bytes.Length / 2];
                    for (int i = 0; i < bytes.Length / 4; i++)
                        BitConverter.TryWriteBytes(new Span<byte>(halves, i * 2, 2), (Half)BitConverter.ToSingle(bytes, i * 4));
                    result.Layers = new[] { halves };
                }
                else result.Layers = new[] { bytes };
            }
            return AsyncReadbacks.Make(result);
        }
    }
}
