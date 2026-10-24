using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Unity.Collections;

namespace Unbound.Runtime
{
    /// <summary>The GPU side of a GraphicsBuffer or ComputeBuffer: created, written and read in
    /// order with the rendering commands.</summary>
    internal sealed class GpuBufferStorage
    {
        internal readonly ulong Handle;
        internal readonly int Count;
        internal readonly int Stride;
        internal bool Released;

        internal GpuBufferStorage(int count, int stride, uint usage)
        {
            if (count <= 0) throw new ArgumentException("Attempting to create a zero length compute buffer", nameof(count));
            if (stride <= 0) throw new ArgumentException("Attempting to create a compute buffer with a negative or null stride", nameof(stride));
            Count = count;
            Stride = stride;
            Handle = RenderHandles.Next();
            GfxStream.CreateBuffer(Handle, (long)count * stride, usage);
        }

        internal void Release()
        {
            if (Released) return;
            Released = true;
            GfxStream.ReleaseBuffer(Handle);
        }

        static int ElementSize(Type type) => type == typeof(bool) ? 1 : type == typeof(char) ? 2 : Marshal.SizeOf(type);

        /// <summary>Writes elements [start, start + count) of an array at buffer element
        /// <paramref name="bufferStart"/> (the element size is the array's, as Unity's).</summary>
        internal void SetData(Array data, int start, int bufferStart, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var element = data.GetType().GetElementType();
            if (element == null || (!element.IsPrimitive && !element.IsValueType))
                throw new ArgumentException($"Array passed to SetData must be blittable. {element} is not blittable.");
            int size = ElementSize(element);
            if (start < 0 || bufferStart < 0 || count < 0 || start + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count), $"Bad indices/count arguments (managedBufferStartIndex:{start} computeBufferStartIndex:{bufferStart} count:{count})");
            long offset = (long)bufferStart * size, length = (long)count * size;
            if (offset + length > (long)Count * Stride)
                throw new ArgumentException($"SetData() would write past the end of the buffer ({offset + length} bytes into {(long)Count * Stride}).");
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                unsafe
                {
                    var bytes = new ReadOnlySpan<byte>((byte*)pin.AddrOfPinnedObject() + (long)start * size, (int)length);
                    Write(offset, bytes);
                }
            }
            finally
            {
                pin.Free();
            }
        }

        internal void Write(long offset, ReadOnlySpan<byte> bytes)
        {
            // A buffer change comes after what was recorded before it.
            GfxStream.BufferData(Handle, offset, bytes);
        }

        internal void GetData(Array data, int start, int bufferStart, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var element = data.GetType().GetElementType();
            int size = ElementSize(element);
            if (start < 0 || bufferStart < 0 || count < 0 || start + count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count), $"Bad indices/count arguments (managedBufferStartIndex:{start} computeBufferStartIndex:{bufferStart} count:{count})");
            var bytes = Native.ReadBuffer(Handle, (long)bufferStart * size, (long)count * size);
            if (bytes == null)
            {
                UnityEngine.Debug.LogWarning("Unbound: reading a buffer needs the GPU (a headless run has none); the array is left unchanged.");
                return;
            }
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                Marshal.Copy(bytes, 0, pin.AddrOfPinnedObject() + start * size, Math.Min(bytes.Length, count * size));
            }
            finally
            {
                pin.Free();
            }
        }
    }
}

namespace UnityEngine
{
    /// <summary>A GPU buffer: structured, raw, vertex, index, indirect arguments, constant.</summary>
    public sealed partial class GraphicsBuffer : IDisposable
    {
        [Flags]
        public enum Target
        {
            Vertex = 1,
            Index = 2,
            CopySource = 4,
            CopyDestination = 8,
            Structured = 16,
            Raw = 32,
            Append = 64,
            Counter = 128,
            IndirectArguments = 256,
            Constant = 512,
        }

        [Flags]
        public enum UsageFlags
        {
            None = 0,
            LockBufferForWrite = 1,
        }

        internal IntPtr m_Ptr;
        readonly GpuBufferStorage m_Storage;
        readonly Target m_Target;
        readonly UsageFlags m_UsageFlags;

        public GraphicsBuffer(Target target, int count, int stride) : this(target, UsageFlags.None, count, stride) { }

        public GraphicsBuffer(Target target, UsageFlags usageFlags, int count, int stride)
        {
            m_Target = target;
            m_UsageFlags = usageFlags;
            m_Storage = new GpuBufferStorage(count, stride, (uint)target);
            m_Ptr = (IntPtr)(long)m_Storage.Handle;
        }

        ~GraphicsBuffer() { }

        internal ulong Handle => m_Storage.Handle;

        public void Dispose()
        {
            m_Storage.Release();
            m_Ptr = IntPtr.Zero;
            GC.SuppressFinalize(this);
        }

        public void Release() => Dispose();

        public bool IsValid() => m_Ptr != IntPtr.Zero;

        public int count => m_Storage.Count;
        public int stride => m_Storage.Stride;
        public Target target => m_Target;
        public UsageFlags usageFlags => m_UsageFlags;
        public string name { set { } }

        public IntPtr GetNativeBufferPtr() => m_Ptr;

        public void SetData(Array data) => m_Storage.SetData(data, 0, 0, data?.Length ?? 0);
        public void SetData(Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) =>
            m_Storage.SetData(data, managedBufferStartIndex, graphicsBufferStartIndex, count);
        public void SetData<T>(List<T> data) where T : struct => m_Storage.SetData(data.ToArray(), 0, 0, data.Count);
        public void SetData<T>(List<T> data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct =>
            m_Storage.SetData(data.ToArray(), managedBufferStartIndex, graphicsBufferStartIndex, count);
        public void SetData<T>(NativeArray<T> data) where T : struct => m_Storage.SetData(data.ToArray(), 0, 0, data.Length);
        public void SetData<T>(NativeArray<T> data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct =>
            m_Storage.SetData(data.ToArray(), nativeBufferStartIndex, graphicsBufferStartIndex, count);

        public void GetData(Array data) => m_Storage.GetData(data, 0, 0, data?.Length ?? 0);
        public void GetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) =>
            m_Storage.GetData(data, managedBufferStartIndex, computeBufferStartIndex, count);
    }

    /// <summary>A GPU buffer for compute shaders (the older API of GraphicsBuffer).</summary>
    public sealed partial class ComputeBuffer : IDisposable
    {
        internal IntPtr m_Ptr;
        readonly GpuBufferStorage m_Storage;

        public ComputeBuffer(int count, int stride) : this(count, stride, ComputeBufferType.Default, ComputeBufferMode.Immutable) { }

        public ComputeBuffer(int count, int stride, ComputeBufferType type) : this(count, stride, type, ComputeBufferMode.Immutable) { }

        public ComputeBuffer(int count, int stride, ComputeBufferType type, ComputeBufferMode usage)
        {
            m_Storage = new GpuBufferStorage(count, stride, Usage(type));
            m_Ptr = (IntPtr)(long)m_Storage.Handle;
        }

        ~ComputeBuffer() { }

        /// <summary>The GraphicsBuffer.Target flags of a compute buffer type.</summary>
        static uint Usage(ComputeBufferType type)
        {
            uint usage = (uint)GraphicsBuffer.Target.Structured;
            if ((type & ComputeBufferType.Raw) != 0) usage |= (uint)GraphicsBuffer.Target.Raw;
            if ((type & ComputeBufferType.Append) != 0) usage |= (uint)GraphicsBuffer.Target.Append;
            if ((type & ComputeBufferType.Counter) != 0) usage |= (uint)GraphicsBuffer.Target.Counter;
            if ((type & ComputeBufferType.Constant) != 0) usage |= (uint)GraphicsBuffer.Target.Constant;
            if ((type & ComputeBufferType.IndirectArguments) != 0) usage |= (uint)GraphicsBuffer.Target.IndirectArguments;
            return usage;
        }

        internal ulong Handle => m_Storage.Handle;

        public void Dispose()
        {
            m_Storage.Release();
            m_Ptr = IntPtr.Zero;
            GC.SuppressFinalize(this);
        }

        public void Release() => Dispose();

        public bool IsValid() => m_Ptr != IntPtr.Zero;

        public int count => m_Storage.Count;
        public int stride => m_Storage.Stride;
        public string name { set { } }

        public IntPtr GetNativeBufferPtr() => m_Ptr;

        public void SetData(Array data) => m_Storage.SetData(data, 0, 0, data?.Length ?? 0);
        public void SetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) =>
            m_Storage.SetData(data, managedBufferStartIndex, computeBufferStartIndex, count);
        public void SetData<T>(List<T> data) where T : struct => m_Storage.SetData(data.ToArray(), 0, 0, data.Count);
        public void SetData<T>(List<T> data, int managedBufferStartIndex, int computeBufferStartIndex, int count) where T : struct =>
            m_Storage.SetData(data.ToArray(), managedBufferStartIndex, computeBufferStartIndex, count);
        public void SetData<T>(NativeArray<T> data) where T : struct => m_Storage.SetData(data.ToArray(), 0, 0, data.Length);
        public void SetData<T>(NativeArray<T> data, int nativeBufferStartIndex, int computeBufferStartIndex, int count) where T : struct =>
            m_Storage.SetData(data.ToArray(), nativeBufferStartIndex, computeBufferStartIndex, count);

        public void GetData(Array data) => m_Storage.GetData(data, 0, 0, data?.Length ?? 0);
        public void GetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) =>
            m_Storage.GetData(data, managedBufferStartIndex, computeBufferStartIndex, count);
    }
}
