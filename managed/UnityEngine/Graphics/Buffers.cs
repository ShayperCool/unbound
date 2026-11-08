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
        /// <summary>GraphicsBuffer.Target flags.</summary>
        internal readonly uint Usage;
        internal bool Released;
        internal readonly bool MeshOwned;
        int m_Views;
        bool m_OwnerReleased;
        bool m_DestroyedOwner;
        bool m_GpuReleased;

        internal void AddView() => m_Views++;

        internal void ReleaseView()
        {
            if (--m_Views == 0 && m_OwnerReleased) Release();
        }

        internal void ReleaseMeshOwner()
        {
            m_OwnerReleased = true;
            m_DestroyedOwner = true;
            ReleaseGpu();
            if (m_Views == 0) Release();
        }
        internal void InvalidateMeshBuffer()
        {
            Released = true;
            m_OwnerReleased = true;
            if (m_Views == 0) ReleaseGpu();
        }

        const uint AppendOrCounter = (uint)(UnityEngine.GraphicsBuffer.Target.Append | UnityEngine.GraphicsBuffer.Target.Counter);

        /// <summary>The size in bytes.</summary>
        internal long Size => (long)Count * Stride;

        /// <summary>An Append or Counter buffer: it has a hidden counter.</summary>
        internal bool HasCounter => (Usage & AppendOrCounter) != 0;

        internal bool Has(UnityEngine.GraphicsBuffer.Target target) => (Usage & (uint)target) != 0;

        internal GpuBufferStorage(int count, int stride, uint usage, bool meshOwned = false)
        {
            if (count <= 0) throw new ArgumentException("Attempting to create a zero length compute buffer", nameof(count));
            if (stride <= 0) throw new ArgumentException("Attempting to create a compute buffer with a negative or null stride", nameof(stride));
            Count = count;
            Stride = stride;
            Usage = usage;
            MeshOwned = meshOwned;
            Handle = RenderHandles.Next();
            GfxStream.CreateBuffer(Handle, (long)count * stride, usage);
        }

        internal void Release()
        {
            Released = true;
            ReleaseGpu();
        }

        void ReleaseGpu()
        {
            if (m_GpuReleased) return;
            m_GpuReleased = true;
            GfxStream.ReleaseBuffer(Handle);
        }

        static int ElementSize(Type type) => type == typeof(bool) ? 1 : type == typeof(char) ? 2 : Marshal.SizeOf(type);

        /// <summary>The names a write's messages use: the buffer index argument and the API.</summary>
        internal readonly struct Names
        {
            internal readonly string Index, Api;
            internal Names(string index, string api) { Index = index; Api = api; }
        }

        internal static readonly Names Compute = new Names("computeBufferStartIndex", "SetData()");
        internal static readonly Names Graphics = new Names("graphicsBufferStartIndex", "SetData()");
        internal static readonly Names Commands = new Names("graphicsBufferStartIndex", "GraphicsBuffer.SetData()");

        /// <summary>Checks a write of elements [start, start + count) of an array at buffer element
        /// <paramref name="bufferStart"/> (counted in the array's element size, as Unity's) and
        /// copies its bytes: the Editor's exceptions for bad ranges and writes past the end.</summary>
        internal (long Offset, byte[] Bytes) Prepare(Array data, int start, int bufferStart, int count, Names names)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var element = data.GetType().GetElementType();
            if (element == null || (!element.IsPrimitive && !element.IsValueType))
                throw new ArgumentException($"Array passed to SetData must be blittable. {element} is not blittable.");
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                unsafe { return Prepare((void*)pin.AddrOfPinnedObject(), data.Length, ElementSize(element), start, bufferStart, count, names); }
            }
            finally
            {
                pin.Free();
            }
        }

        internal unsafe (long Offset, byte[] Bytes) Prepare(void* data, int length, int elementSize, int start, int bufferStart, int count, Names names)
        {
            if (start < 0 || bufferStart < 0 || count < 0 || start + count > length)
                throw new ArgumentOutOfRangeException($"Bad indices/count arguments (managedBufferStartIndex:{start} {names.Index}:{bufferStart} count:{count})");
            long offset = (long)bufferStart * elementSize, size = (long)count * elementSize;
            if (offset + size > Size)
                throw new ArgumentException($"{names.Api}: Accessing {size} bytes at offset {offset} for Buffer of size {Size} bytes is not possible.");
            var bytes = new byte[size];
            new ReadOnlySpan<byte>((byte*)data + (long)start * elementSize, (int)size).CopyTo(bytes);
            return (offset, bytes);
        }

        internal void SetData(Array data, int start, int bufferStart, int count, Names names)
        {
            var (offset, bytes) = Prepare(data, start, bufferStart, count, names);
            Write(offset, bytes);
        }

        internal unsafe void SetData<T>(NativeArray<T> data, int start, int bufferStart, int count, Names names) where T : struct
        {
            var (offset, bytes) = Prepare(Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), data.Length,
                Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>(), start, bufferStart, count, names);
            Write(offset, bytes);
        }

        internal void Write(long offset, ReadOnlySpan<byte> bytes)
        {
            if (Released) return;
            // A buffer change comes after what was recorded before it.
            GfxStream.BufferData(Handle, offset, bytes);
        }

        internal void GetData(Array data, int start, int bufferStart, int count)
        {
            if (Released && !MeshOwned) throw new NullReferenceException("Object reference not set to an instance of an object.");
            if (data == null) throw new ArgumentNullException(nameof(data));
            var element = data.GetType().GetElementType();
            int size = ElementSize(element);
            if (start < 0 || bufferStart < 0 || count < 0 || start + count > data.Length)
                throw new ArgumentOutOfRangeException($"Bad indices/count arguments (managedBufferStartIndex:{start} computeBufferStartIndex:{bufferStart} count:{count})");
            long offset = (long)bufferStart * size, length = (long)count * size;
            if (offset % Stride != 0)
                throw new ArgumentException($"GetData(): Buffer offset ({offset} bytes) is not integer multiple of Buffer stride ({Stride} bytes).");
            if (offset + length > Size)
                throw new ArgumentException($"GetData(): Accessing {length} bytes at offset {offset} for Buffer of size {Size} bytes is not possible.");
            if (length % Stride != 0)
                throw new ArgumentException($"GetData(): Data size ({length} bytes) is not integer multiple of Buffer stride ({Stride} bytes).");
            // A destroyed mesh leaves its view metadata valid, but there is no GPU buffer to
            // read. The measured GetData leaves the destination untouched (renderapi).
            if (m_DestroyedOwner) return;
            var bytes = Native.ReadBuffer(Handle, offset, length);
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

        internal GraphicsBuffer(GpuBufferStorage storage)
        {
            m_Storage = storage;
            storage.AddView();
            m_Target = (Target)storage.Usage;
            m_Ptr = (IntPtr)(long)storage.Handle;
        }

        public GraphicsBuffer(Target target, int count, int stride) : this(target, UsageFlags.None, count, stride) { }

        public GraphicsBuffer(Target target, UsageFlags usageFlags, int count, int stride)
        {
            m_Target = target;
            m_UsageFlags = usageFlags;
            m_Storage = new GpuBufferStorage(count, stride, (uint)target);
            m_Ptr = (IntPtr)(long)m_Storage.Handle;
        }

        ~GraphicsBuffer()
        {
            if (m_Storage?.MeshOwned == true && m_Ptr != IntPtr.Zero)
                MainThreadContext.Instance.Post(static state => ((GpuBufferStorage)state).ReleaseView(), m_Storage);
        }

        internal ulong Handle => m_Storage.Handle;
        internal GpuBufferStorage Storage => m_Ptr != IntPtr.Zero ? m_Storage : null;
        GpuBufferStorage CheckedStorage => Storage ?? throw new NullReferenceException("Object reference not set to an instance of an object.");

        public void Dispose()
        {
            if (m_Ptr == IntPtr.Zero) return;
            if (m_Storage.MeshOwned) m_Storage.ReleaseView();
            else m_Storage.Release();
            m_Ptr = IntPtr.Zero;
            GC.SuppressFinalize(this);
        }

        public void Release() => Dispose();

        public bool IsValid() => m_Ptr != IntPtr.Zero && !m_Storage.Released;

        public int count => CheckedStorage.Count;
        public int stride => CheckedStorage.Stride;
        public Target target { get { _ = CheckedStorage; return m_Target; } }
        public UsageFlags usageFlags { get { _ = CheckedStorage; return m_UsageFlags; } }
        [Unbound.EditorFaithful("write-only label for GPU debuggers: nothing a game can observe")]
        public string name { set { } }

        public IntPtr GetNativeBufferPtr() { _ = CheckedStorage; return m_Ptr; }

        public void SetData(Array data) => CheckedStorage.SetData(data, 0, 0, data?.Length ?? 0, GpuBufferStorage.Graphics);
        public void SetData(Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) =>
            CheckedStorage.SetData(data, managedBufferStartIndex, graphicsBufferStartIndex, count, GpuBufferStorage.Graphics);
        public void SetData<T>(List<T> data) where T : struct => CheckedStorage.SetData(data.ToArray(), 0, 0, data.Count, GpuBufferStorage.Graphics);
        public void SetData<T>(List<T> data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct =>
            CheckedStorage.SetData(data.ToArray(), managedBufferStartIndex, graphicsBufferStartIndex, count, GpuBufferStorage.Graphics);
        public void SetData<T>(NativeArray<T> data) where T : struct => CheckedStorage.SetData(data, 0, 0, data.Length, GpuBufferStorage.Graphics);
        public void SetData<T>(NativeArray<T> data, int nativeBufferStartIndex, int graphicsBufferStartIndex, int count) where T : struct =>
            CheckedStorage.SetData(data, nativeBufferStartIndex, graphicsBufferStartIndex, count, GpuBufferStorage.Graphics);

        public void GetData(Array data) => CheckedStorage.GetData(data, 0, 0, data?.Length ?? 0);
        public void GetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) =>
            CheckedStorage.GetData(data, managedBufferStartIndex, computeBufferStartIndex, count);

        /// <summary>Sets the hidden counter of an Append or Counter buffer.</summary>
        public void SetCounterValue(uint counterValue) => GpuBufferOps.SetCounter(m_Storage, counterValue);

        public static void CopyCount(ComputeBuffer src, ComputeBuffer dst, int dstOffsetBytes) => GpuBufferOps.CopyCount(src?.Storage, dst?.Storage, dstOffsetBytes);
        public static void CopyCount(GraphicsBuffer src, ComputeBuffer dst, int dstOffsetBytes) => GpuBufferOps.CopyCount(src?.Storage, dst?.Storage, dstOffsetBytes);
        public static void CopyCount(ComputeBuffer src, GraphicsBuffer dst, int dstOffsetBytes) => GpuBufferOps.CopyCount(src?.Storage, dst?.Storage, dstOffsetBytes);
        public static void CopyCount(GraphicsBuffer src, GraphicsBuffer dst, int dstOffsetBytes) => GpuBufferOps.CopyCount(src?.Storage, dst?.Storage, dstOffsetBytes);
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
        internal GpuBufferStorage Storage => m_Storage;

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
        [Unbound.EditorFaithful("write-only label for GPU debuggers: nothing a game can observe")]
        public string name { set { } }

        public IntPtr GetNativeBufferPtr() => m_Ptr;

        public void SetData(Array data) => m_Storage.SetData(data, 0, 0, data?.Length ?? 0, GpuBufferStorage.Compute);
        public void SetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) =>
            m_Storage.SetData(data, managedBufferStartIndex, computeBufferStartIndex, count, GpuBufferStorage.Compute);
        public void SetData<T>(List<T> data) where T : struct => m_Storage.SetData(data.ToArray(), 0, 0, data.Count, GpuBufferStorage.Compute);
        public void SetData<T>(List<T> data, int managedBufferStartIndex, int computeBufferStartIndex, int count) where T : struct =>
            m_Storage.SetData(data.ToArray(), managedBufferStartIndex, computeBufferStartIndex, count, GpuBufferStorage.Compute);
        public void SetData<T>(NativeArray<T> data) where T : struct => m_Storage.SetData(data, 0, 0, data.Length, GpuBufferStorage.Compute);
        public void SetData<T>(NativeArray<T> data, int nativeBufferStartIndex, int computeBufferStartIndex, int count) where T : struct =>
            m_Storage.SetData(data, nativeBufferStartIndex, computeBufferStartIndex, count, GpuBufferStorage.Compute);

        public void GetData(Array data) => m_Storage.GetData(data, 0, 0, data?.Length ?? 0);
        public void GetData(Array data, int managedBufferStartIndex, int computeBufferStartIndex, int count) =>
            m_Storage.GetData(data, managedBufferStartIndex, computeBufferStartIndex, count);

        /// <summary>Sets the hidden counter of an Append or Counter buffer.</summary>
        public void SetCounterValue(uint counterValue) => GpuBufferOps.SetCounter(m_Storage, counterValue);

        public static void CopyCount(ComputeBuffer src, ComputeBuffer dst, int dstOffsetBytes) => GpuBufferOps.CopyCount(src?.Storage, dst?.Storage, dstOffsetBytes);
    }

    public partial class Graphics
    {
        /// <summary>Copies a buffer's contents into another, on the GPU in order with rendering.</summary>
        public static void CopyBuffer(GraphicsBuffer source, GraphicsBuffer dest) => GpuBufferOps.Copy(source, dest);
    }
}

namespace Unbound.Runtime
{
    /// <summary>Counters, copies and indirect arguments of GPU buffers, in order with the
    /// rendering commands (render protocol 7), with the Editor's checks (tests/projects/gpubuffers).</summary>
    internal static class GpuBufferOps
    {
        /// <summary>Sets an Append or Counter buffer's hidden counter; other buffers have none.</summary>
        internal static void SetCounter(GpuBufferStorage buffer, uint value)
        {
            if (buffer == null || buffer.Released || !buffer.HasCounter) return;
            GfxStream.SetCounter(buffer.Handle, value);
        }

        /// <summary>Writes a hidden counter into a buffer: nothing for buffers without one, nor (the
        /// renderer decides) for one whose counter no dispatch has bound yet; an offset that is not a multiple of 4 warns and still
        /// copies there; one past the end asserts and copies nothing.</summary>
        internal static void CopyCount(GpuBufferStorage source, GpuBufferStorage destination, long offset)
        {
            if (source == null) throw new ArgumentNullException("src");
            if (destination == null) throw new ArgumentNullException("dst");
            if (offset % 4 != 0) UnityEngine.Debug.LogWarning("dstOffsetBytes must be a multiple of 4");
            if (offset < 0 || offset + 4 > destination.Size)
            {
                UnityEngine.Debug.LogAssertion("Assertion failed on expression: 'dstOffset + size <= m_size'");
                return;
            }
            if (source.Released || destination.Released || !source.HasCounter) return;
            GfxStream.CopyCounter(source.Handle, destination.Handle, (uint)offset);
        }

        /// <summary>Graphics.CopyBuffer's checks: both buffers, the same size, CopySource and
        /// CopyDestination targets.</summary>
        internal static void ValidateCopy(UnityEngine.GraphicsBuffer source, UnityEngine.GraphicsBuffer dest)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (dest == null) throw new ArgumentNullException(nameof(dest));
            var s = source.Storage;
            var d = dest.Storage;
            if (s.Released || d.Released) throw new NullReferenceException("Object reference not set to an instance of an object.");
            if (s.Size != d.Size)
                throw new ArgumentException($"CopyBuffer source and destination buffers must be the same size, source was {s.Size} bytes, dest was {d.Size} bytes");
            if (!s.Has(UnityEngine.GraphicsBuffer.Target.CopySource))
                throw new ArgumentException("CopyBuffer source must have CopySource target", nameof(source));
            if (!d.Has(UnityEngine.GraphicsBuffer.Target.CopyDestination))
                throw new ArgumentException("CopyBuffer destination must have CopyDestination target", nameof(dest));
        }

        internal static void Copy(UnityEngine.GraphicsBuffer source, UnityEngine.GraphicsBuffer dest)
        {
            ValidateCopy(source, dest);
            CopyChecked(source.Storage, dest.Storage);
        }

        /// <summary>The copy of checked buffers (a command buffer checks when it records).</summary>
        internal static void CopyChecked(GpuBufferStorage source, GpuBufferStorage destination)
        {
            if (source.Released || destination.Released) return;
            GfxStream.CopyBuffer(source.Handle, 0, destination.Handle, 0, source.Size);
        }

        /// <summary>Whether <paramref name="args"/> holds a dispatch's arguments at the offset: any
        /// buffer can; an offset that is not a multiple of 4 or arguments past the end dispatch
        /// nothing, silently, as in the Editor.</summary>
        internal static bool IndirectArguments(GpuBufferStorage args, uint offset)
        {
            if (args == null) throw new ArgumentNullException("argsBuffer");
            return !args.Released && offset % 4 == 0 && offset + 12L <= args.Size;
        }
    }
}
