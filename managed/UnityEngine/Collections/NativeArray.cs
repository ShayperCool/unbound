using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using SysUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace Unity.Collections
{
    /// <summary>
    /// Unity's native array: elements in unmanaged memory (Unity's layout, so code that takes its
    /// pointer through NativeArrayUnsafeUtility sees the same memory). The job safety system is not
    /// simulated: safety handles are inert.
    /// </summary>
    public unsafe partial struct NativeArray<T> : IDisposable, IEnumerable<T>, IEquatable<NativeArray<T>> where T : struct
    {
        internal void* m_Buffer;
        internal int m_Length;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        internal AtomicSafetyHandle m_Safety;
        internal Allocator m_AllocatorLabel;

        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            Allocate(length, allocator, out this);
            if ((options & NativeArrayOptions.ClearMemory) != 0)
                UnsafeUtility.MemClear(m_Buffer, (long)Length * UnsafeUtility.SizeOf<T>());
        }

        public NativeArray(T[] array, Allocator allocator)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            Allocate(array.Length, allocator, out this);
            Copy(array, this);
        }

        public NativeArray(NativeArray<T> array, Allocator allocator)
        {
            Allocate(array.Length, allocator, out this);
            Copy(array, 0, this, 0, array.Length);
        }

        static void Allocate(int length, Allocator allocator, out NativeArray<T> array)
        {
            if (allocator <= Allocator.None) throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0");
            IsUnmanagedAndThrow();
            long size = (long)UnsafeUtility.SizeOf<T>() * length;
            array = default;
            array.m_Buffer = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<T>(), allocator);
            array.m_Length = length;
            array.m_AllocatorLabel = allocator;
            array.m_MinIndex = 0;
            array.m_MaxIndex = length - 1;
        }

        internal static void IsUnmanagedAndThrow()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new InvalidOperationException($"{typeof(T)} used in NativeArray<{typeof(T)}> must be unmanaged (contain no managed types).");
        }

        public int Length => m_Length;

        public bool IsCreated => m_Buffer != null;

        public T this[int index]
        {
            get
            {
                CheckIndex(index);
                return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index);
            }
            set
            {
                CheckIndex(index);
                UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
            }
        }

        void CheckIndex(int index)
        {
            if ((uint)index >= (uint)m_Length)
                throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
        }
        public void Dispose()
        {
            if (m_Buffer == null)
                throw new ObjectDisposedException("The NativeArray is already disposed.");
            if (m_AllocatorLabel == Allocator.Invalid)
                throw new InvalidOperationException("The NativeArray can not be Disposed because it was not allocated with a valid allocator.");
            if (m_AllocatorLabel > Allocator.None) UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = null;
            m_Length = 0;
        }
        public void CopyFrom(T[] array) => Copy(array, this);
        public void CopyFrom(NativeArray<T> array) => Copy(array, this);

        public void CopyTo(T[] array) => Copy(this, array);

        public void CopyTo(NativeArray<T> array) => Copy(this, array);

        public T[] ToArray()
        {
            var array = new T[Length];
            Copy(this, array, Length);
            return array;
        }

        public Enumerator GetEnumerator() => new Enumerator(ref this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(NativeArray<T> other) => m_Buffer == other.m_Buffer && m_Length == other.m_Length;

        public override bool Equals(object obj) => obj is NativeArray<T> other && Equals(other);

        public override int GetHashCode() => ((int)(long)m_Buffer * 397) ^ m_Length;

        public static bool operator ==(NativeArray<T> left, NativeArray<T> right) => left.Equals(right);

        public static bool operator !=(NativeArray<T> left, NativeArray<T> right) => !left.Equals(right);

        // ------------------------------------------------------------------ copies

        public static void Copy(NativeArray<T> src, NativeArray<T> dst) => Copy(src, 0, dst, 0, CheckSameLength(src.Length, dst.Length));
        public static void Copy(ReadOnly src, NativeArray<T> dst) => Copy(src, 0, dst, 0, CheckSameLength(src.Length, dst.Length));
        public static void Copy(T[] src, NativeArray<T> dst) => Copy(src, 0, dst, 0, CheckSameLength(src.Length, dst.Length));
        public static void Copy(NativeArray<T> src, T[] dst) => Copy(src, 0, dst, 0, CheckSameLength(src.Length, dst.Length));
        public static void Copy(ReadOnly src, T[] dst) => Copy(src, 0, dst, 0, CheckSameLength(src.Length, dst.Length));
        public static void Copy(NativeArray<T> src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(ReadOnly src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(T[] src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(NativeArray<T> src, T[] dst, int length) => Copy(src, 0, dst, 0, length);
        public static void Copy(ReadOnly src, T[] dst, int length) => Copy(src, 0, dst, 0, length);

        public static void Copy(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            CheckRange(src.Length, srcIndex, length);
            CheckRange(dst.Length, dstIndex, length);
            int size = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemMove((byte*)dst.m_Buffer + dstIndex * size, (byte*)src.m_Buffer + srcIndex * size, (long)length * size);
        }

        public static void Copy(ReadOnly src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            CheckRange(src.Length, srcIndex, length);
            CheckRange(dst.Length, dstIndex, length);
            int size = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemMove((byte*)dst.m_Buffer + dstIndex * size, (byte*)src.m_Buffer + srcIndex * size, (long)length * size);
        }

        public static void Copy(T[] src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            CheckRange(src.Length, srcIndex, length);
            CheckRange(dst.Length, dstIndex, length);
            for (int i = 0; i < length; i++) UnsafeUtility.WriteArrayElement(dst.m_Buffer, dstIndex + i, src[srcIndex + i]);
        }

        public static void Copy(NativeArray<T> src, int srcIndex, T[] dst, int dstIndex, int length)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            CheckRange(src.Length, srcIndex, length);
            CheckRange(dst.Length, dstIndex, length);
            for (int i = 0; i < length; i++) dst[dstIndex + i] = UnsafeUtility.ReadArrayElement<T>(src.m_Buffer, srcIndex + i);
        }

        public static void Copy(ReadOnly src, int srcIndex, T[] dst, int dstIndex, int length)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            CheckRange(src.Length, srcIndex, length);
            CheckRange(dst.Length, dstIndex, length);
            for (int i = 0; i < length; i++) dst[dstIndex + i] = UnsafeUtility.ReadArrayElement<T>(src.m_Buffer, srcIndex + i);
        }

        static int CheckSameLength(int a, int b)
        {
            if (a != b) throw new ArgumentException("source and destination length must be the same");
            return a;
        }

        static void CheckRange(int length, int index, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "length must be equal or greater than zero.");
            if (index < 0 || index > length) throw new ArgumentOutOfRangeException(nameof(index), "index is outside the range of valid indexes.");
            if (index + count > length) throw new ArgumentException("length is greater than the number of elements from index to the end of the array.");
        }

        // ------------------------------------------------------------------ views

        public NativeArray<T> GetSubArray(int start, int length)
        {
            CheckRange(m_Length, start, length);
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>((byte*)m_Buffer + (long)start * UnsafeUtility.SizeOf<T>(), length, Allocator.None);
            return result;
        }

        public NativeArray<U> Reinterpret<U>() where U : struct
        {
            int tSize = UnsafeUtility.SizeOf<T>(), uSize = UnsafeUtility.SizeOf<U>();
            long bytes = (long)m_Length * tSize;
            if (bytes % uSize != 0)
                throw new InvalidOperationException($"Types {typeof(T)} (array length {m_Length}) and {typeof(U)} cannot be aliased due to size constraints. The size of the types and lengths involved must line up.");
            return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<U>(m_Buffer, (int)(bytes / uSize), Allocator.None);
        }

        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct
        {
            if (UnsafeUtility.SizeOf<T>() != expectedTypeSize)
                throw new InvalidOperationException($"Type {typeof(T)} was expected to be {expectedTypeSize} but is {UnsafeUtility.SizeOf<T>()} bytes");
            if (UnsafeUtility.SizeOf<U>() != expectedTypeSize)
                throw new InvalidOperationException($"Type {typeof(U)} was expected to be {expectedTypeSize} but is {UnsafeUtility.SizeOf<U>()} bytes");
            return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<U>(m_Buffer, m_Length, Allocator.None);
        }

        public U ReinterpretLoad<U>(int sourceIndex) where U : struct
        {
            long offset = (long)sourceIndex * UnsafeUtility.SizeOf<T>();
            if (offset + UnsafeUtility.SizeOf<U>() > (long)m_Length * UnsafeUtility.SizeOf<T>() || sourceIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            return SysUnsafe.ReadUnaligned<U>((byte*)m_Buffer + offset);
        }

        public void ReinterpretStore<U>(int destIndex, U data) where U : struct
        {
            long offset = (long)destIndex * UnsafeUtility.SizeOf<T>();
            if (offset + UnsafeUtility.SizeOf<U>() > (long)m_Length * UnsafeUtility.SizeOf<T>() || destIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(destIndex));
            SysUnsafe.WriteUnaligned((byte*)m_Buffer + offset, data);
        }

        public ReadOnly AsReadOnly() => new ReadOnly(m_Buffer, m_Length, ref m_Safety);

        public readonly Span<T> AsSpan() => new Span<T>(m_Buffer, m_Length);

        public readonly ReadOnlySpan<T> AsReadOnlySpan() => new ReadOnlySpan<T>(m_Buffer, m_Length);

        public static implicit operator Span<T>(in NativeArray<T> source) => source.AsSpan();

        public static implicit operator ReadOnlySpan<T>(in NativeArray<T> source) => source.AsReadOnlySpan();

        public partial struct Enumerator : IEnumerator<T>
        {
            NativeArray<T> m_Array;
            int m_Index;
            T m_Value;

            public Enumerator(ref NativeArray<T> array)
            {
                m_Array = array;
                m_Index = -1;
                m_Value = default;
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                m_Index++;
                if (m_Index < m_Array.m_Length)
                {
                    m_Value = UnsafeUtility.ReadArrayElement<T>(m_Array.m_Buffer, m_Index);
                    return true;
                }
                m_Value = default;
                return false;
            }

            public void Reset() => m_Index = -1;

            public T Current => m_Value;

            object IEnumerator.Current => Current;
        }

        /// <summary>A read-only view of a native array.</summary>
        public partial struct ReadOnly : IEnumerable<T>
        {
            internal void* m_Buffer;
            internal int m_Length;
            internal AtomicSafetyHandle m_Safety;

            internal ReadOnly(void* buffer, int length, ref AtomicSafetyHandle safety)
            {
                m_Buffer = buffer;
                m_Length = length;
                m_Safety = safety;
            }

            public int Length => m_Length;

            public bool IsCreated => m_Buffer != null;

            public T this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
                    return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index);
                }
            }

            public ref readonly T UnsafeElementAt(int index) => ref UnsafeUtility.ArrayElementAsRef<T>(m_Buffer, index);

            public void CopyTo(T[] array) => Copy(this, array);

            public void CopyTo(NativeArray<T> array) => Copy(this, array);

            public T[] ToArray()
            {
                var array = new T[m_Length];
                Copy(this, array, m_Length);
                return array;
            }

            public NativeArray<U>.ReadOnly Reinterpret<U>() where U : struct
            {
                long bytes = (long)m_Length * UnsafeUtility.SizeOf<T>();
                int uSize = UnsafeUtility.SizeOf<U>();
                if (bytes % uSize != 0)
                    throw new InvalidOperationException($"Types {typeof(T)} (array length {m_Length}) and {typeof(U)} cannot be aliased due to size constraints.");
                return new NativeArray<U>.ReadOnly(m_Buffer, (int)(bytes / uSize), ref m_Safety);
            }

            public readonly ReadOnlySpan<T> AsReadOnlySpan() => new ReadOnlySpan<T>(m_Buffer, m_Length);

            public static implicit operator ReadOnlySpan<T>(in ReadOnly source) => source.AsReadOnlySpan();

            public Enumerator GetEnumerator() => new Enumerator(in this);

            IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public partial struct Enumerator : IEnumerator<T>
            {
                readonly ReadOnly m_Array;
                int m_Index;
                T m_Value;

                public Enumerator(in ReadOnly array)
                {
                    m_Array = array;
                    m_Index = -1;
                    m_Value = default;
                }

                public void Dispose() { }

                public bool MoveNext()
                {
                    m_Index++;
                    if (m_Index < m_Array.m_Length)
                    {
                        m_Value = UnsafeUtility.ReadArrayElement<T>(m_Array.m_Buffer, m_Index);
                        return true;
                    }
                    m_Value = default;
                    return false;
                }

                public void Reset() => m_Index = -1;

                public T Current => m_Value;

                object IEnumerator.Current => Current;
            }
        }
    }

    /// <summary>A strided view of native memory (NativeArray elements or one field of them).</summary>
    public unsafe partial struct NativeSlice<T> : IEnumerable<T>, IEquatable<NativeSlice<T>> where T : struct
    {
        internal byte* m_Buffer;
        internal int m_Stride;
        internal int m_Length;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        internal AtomicSafetyHandle m_Safety;

        public NativeSlice(NativeSlice<T> slice, int start) : this(slice, start, slice.Length - start) { }

        public NativeSlice(NativeSlice<T> slice, int start, int length)
        {
            if (start < 0 || length < 0 || start + length > slice.Length) throw new ArgumentOutOfRangeException(nameof(start));
            m_Stride = slice.m_Stride;
            m_Buffer = slice.m_Buffer + m_Stride * start;
            m_Length = length;
            m_MinIndex = 0;
            m_MaxIndex = length - 1;
            m_Safety = slice.m_Safety;
        }

        public NativeSlice(NativeArray<T> array) : this(array, 0, array.Length) { }

        public NativeSlice(NativeArray<T> array, int start) : this(array, start, array.Length - start) { }

        public NativeSlice(NativeArray<T> array, int start, int length)
        {
            if (start < 0 || length < 0 || start + length > array.Length) throw new ArgumentOutOfRangeException(nameof(start));
            m_Stride = UnsafeUtility.SizeOf<T>();
            m_Buffer = (byte*)array.m_Buffer + m_Stride * start;
            m_Length = length;
            m_MinIndex = 0;
            m_MaxIndex = length - 1;
            m_Safety = array.m_Safety;
        }

        public static implicit operator NativeSlice<T>(NativeArray<T> array) => new NativeSlice<T>(array);

        public int Stride => m_Stride;

        public int Length => m_Length;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
                return UnsafeUtility.ReadArrayElementWithStride<T>(m_Buffer, index, m_Stride);
            }
            set
            {
                if ((uint)index >= (uint)m_Length) throw new IndexOutOfRangeException($"Index {index} is out of range of '{m_Length}' Length.");
                UnsafeUtility.WriteArrayElementWithStride(m_Buffer, index, m_Stride, value);
            }
        }

        public NativeSlice<U> SliceConvert<U>() where U : struct
        {
            int size = UnsafeUtility.SizeOf<U>();
            if (m_Stride != UnsafeUtility.SizeOf<T>())
                throw new InvalidOperationException("SliceConvert requires that stride matches the size of the source type");
            long bytes = (long)m_Stride * m_Length;
            if (bytes % size != 0)
                throw new InvalidOperationException("SliceConvert requires that Length * sizeof(T) is a multiple of sizeof(U).");
            return NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<U>(m_Buffer, size, (int)(bytes / size));
        }

        public NativeSlice<U> SliceWithStride<U>(int offset) where U : struct =>
            NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<U>(m_Buffer + offset, m_Stride, m_Length);

        public NativeSlice<U> SliceWithStride<U>() where U : struct => SliceWithStride<U>(0);

        public void CopyFrom(NativeSlice<T> slice)
        {
            if (slice.Length != Length) throw new ArgumentException("Slices must have the same length");
            for (int i = 0; i < m_Length; i++) this[i] = slice[i];
        }

        public void CopyFrom(T[] array)
        {
            if (array.Length != Length) throw new ArgumentException("Array must have the slice's length");
            for (int i = 0; i < m_Length; i++) this[i] = array[i];
        }

        public void CopyTo(NativeArray<T> array)
        {
            if (array.Length != Length) throw new ArgumentException("Array must have the slice's length");
            for (int i = 0; i < m_Length; i++) array[i] = this[i];
        }

        public void CopyTo(T[] array)
        {
            if (array.Length != Length) throw new ArgumentException("Array must have the slice's length");
            for (int i = 0; i < m_Length; i++) array[i] = this[i];
        }

        public T[] ToArray()
        {
            var array = new T[m_Length];
            CopyTo(array);
            return array;
        }

        public Enumerator GetEnumerator() => new Enumerator(ref this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(NativeSlice<T> other) => m_Buffer == other.m_Buffer && m_Stride == other.m_Stride && m_Length == other.m_Length;

        public override bool Equals(object obj) => obj is NativeSlice<T> other && Equals(other);

        public override int GetHashCode() => ((int)(long)m_Buffer * 397) ^ (m_Stride * 31) ^ m_Length;

        public static bool operator ==(NativeSlice<T> left, NativeSlice<T> right) => left.Equals(right);

        public static bool operator !=(NativeSlice<T> left, NativeSlice<T> right) => !left.Equals(right);

        public partial struct Enumerator : IEnumerator<T>
        {
            NativeSlice<T> m_Slice;
            int m_Index;

            public Enumerator(ref NativeSlice<T> array)
            {
                m_Slice = array;
                m_Index = -1;
            }

            public void Dispose() { }

            public bool MoveNext() => ++m_Index < m_Slice.Length;

            public void Reset() => m_Index = -1;

            public T Current => m_Slice[m_Index];

            object IEnumerator.Current => Current;
        }
    }

    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
        AudioKernel = 5,
        Domain = 6,
        FirstUserIndex = 64,
    }

    [Flags]
    public enum NativeArrayOptions
    {
        UninitializedMemory = 0,
        ClearMemory = 1,
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>The job safety handle. Unbound runs no jobs concurrently, so it checks nothing.</summary>
    public partial struct AtomicSafetyHandle
    {
        internal IntPtr versionNode;
        internal int version;
        internal int staticSafetyId;
    }

    /// <summary>Raw memory, as Unity's native allocator gives it (aligned, on the native heap).</summary>
    public static unsafe partial class UnsafeUtility
    {
        public static void* Malloc(long size, int alignment, Allocator allocator)
        {
            if (allocator <= Allocator.None) throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            // AlignedAlloc needs a power of two.
            nuint align = (nuint)Math.Max(alignment, 1);
            if ((align & (align - 1)) != 0) align = 16;
            return NativeMemory.AlignedAlloc((nuint)Math.Max(size, 1), align);
        }

        public static void* MallocTracked(long size, int alignment, Allocator allocator, int callstacksToSkip) => Malloc(size, alignment, allocator);

        public static void Free(void* memory, Allocator allocator)
        {
            if (memory == null || allocator <= Allocator.None) return;
            NativeMemory.AlignedFree(memory);
        }

        public static void FreeTracked(void* memory, Allocator allocator) => Free(memory, allocator);

        public static bool IsValidAllocator(Allocator allocator) => allocator > Allocator.None;

        public static void MemCpy(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);

        public static void MemMove(void* destination, void* source, long size) => Buffer.MemoryCopy(source, destination, size, size);

        public static void MemCpyReplicate(void* destination, void* source, int size, int count)
        {
            for (int i = 0; i < count; i++) MemCpy((byte*)destination + (long)i * size, source, size);
        }

        public static void MemCpyStride(void* destination, int destinationStride, void* source, int sourceStride, int elementSize, int count)
        {
            for (int i = 0; i < count; i++)
                MemCpy((byte*)destination + (long)i * destinationStride, (byte*)source + (long)i * sourceStride, elementSize);
        }

        public static void MemSwap(void* ptr1, void* ptr2, long size)
        {
            var a = (byte*)ptr1;
            var b = (byte*)ptr2;
            for (long i = 0; i < size; i++) (a[i], b[i]) = (b[i], a[i]);
        }

        public static void MemSet(void* destination, byte value, long size) => NativeMemory.Fill(destination, (nuint)size, value);

        public static void MemClear(void* destination, long size) => NativeMemory.Clear(destination, (nuint)size);

        public static int MemCmp(void* ptr1, void* ptr2, long size)
        {
            var a = (byte*)ptr1;
            var b = (byte*)ptr2;
            for (long i = 0; i < size; i++)
                if (a[i] != b[i]) return a[i] - b[i];
            return 0;
        }

        public static int SizeOf<T>() where T : struct => SysUnsafe.SizeOf<T>();

        public static int SizeOf(Type type) => (int)typeof(SysUnsafe).GetMethod(nameof(SysUnsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

        public static int AlignOf<T>() where T : struct
        {
            int size = SysUnsafe.SizeOf<T>();
            return size >= 16 ? 16 : size >= 8 ? 8 : size >= 4 ? 4 : size >= 2 ? 2 : 1;
        }

        public static bool IsUnmanaged<T>() => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        public static bool IsUnmanaged(Type type) =>
            !(bool)typeof(RuntimeHelpers).GetMethod(nameof(RuntimeHelpers.IsReferenceOrContainsReferences))!.MakeGenericMethod(type).Invoke(null, null)!;

        public static bool IsBlittable<T>() where T : struct => IsUnmanaged<T>();

        public static bool IsBlittable(Type type) => type.IsValueType && IsUnmanaged(type);

        public static bool IsValidNativeContainerElementType<T>() => IsUnmanaged<T>();

        public static bool IsValidNativeContainerElementType(Type type) => type.IsValueType && IsUnmanaged(type);

        public static void CopyPtrToStructure<T>(void* ptr, out T output) where T : struct => output = SysUnsafe.ReadUnaligned<T>(ptr);

        public static void CopyStructureToPtr<T>(ref T input, void* ptr) where T : struct => SysUnsafe.WriteUnaligned(ptr, input);

        public static T ReadArrayElement<T>(void* source, int index) => SysUnsafe.ReadUnaligned<T>((byte*)source + (long)index * SysUnsafe.SizeOf<T>());

        public static T ReadArrayElementWithStride<T>(void* source, int index, int stride) => SysUnsafe.ReadUnaligned<T>((byte*)source + (long)index * stride);

        public static void WriteArrayElement<T>(void* destination, int index, T value) => SysUnsafe.WriteUnaligned((byte*)destination + (long)index * SysUnsafe.SizeOf<T>(), value);

        public static void WriteArrayElementWithStride<T>(void* destination, int index, int stride, T value) => SysUnsafe.WriteUnaligned((byte*)destination + (long)index * stride, value);

        public static void* AddressOf<T>(ref T output) where T : struct => SysUnsafe.AsPointer(ref output);

        public static ref T AsRef<T>(void* ptr) where T : struct => ref SysUnsafe.AsRef<T>(ptr);

        public static ref T ArrayElementAsRef<T>(void* ptr, int index) where T : struct => ref SysUnsafe.AsRef<T>((byte*)ptr + (long)index * SysUnsafe.SizeOf<T>());

        public static ref T As<U, T>(ref U from) => ref SysUnsafe.As<U, T>(ref from);

        public static int EnumToInt<T>(T enumValue) where T : struct, IConvertible => enumValue.ToInt32(null);

        public static bool EnumEquals<T>(T lhs, T rhs) where T : struct, IConvertible => lhs.ToInt64(null) == rhs.ToInt64(null);

        public static void* PinGCObjectAndGetAddress(object target, out ulong gcHandle)
        {
            var handle = GCHandle.Alloc(target, GCHandleType.Pinned);
            gcHandle = (ulong)(long)GCHandle.ToIntPtr(handle);
            return (void*)handle.AddrOfPinnedObject();
        }

        public static void* PinGCArrayAndGetDataAddress(Array target, out ulong gcHandle)
        {
            var handle = GCHandle.Alloc(target, GCHandleType.Pinned);
            gcHandle = (ulong)(long)GCHandle.ToIntPtr(handle);
            return (void*)Marshal.UnsafeAddrOfPinnedArrayElement(target, 0);
        }

        public static void ReleaseGCObject(ulong gcHandle) => GCHandle.FromIntPtr((IntPtr)(long)gcHandle).Free();

        public static int GetFieldOffset(System.Reflection.FieldInfo field) => (int)Marshal.OffsetOf(field.DeclaringType, field.Name);
    }

    /// <summary>The pointers of native arrays, and arrays over existing memory.</summary>
    public static unsafe partial class NativeArrayUnsafeUtility
    {
        public static NativeArray<T> ConvertExistingDataToNativeArray<T>(void* dataPointer, int length, Allocator allocator) where T : struct
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0");
            return new NativeArray<T>
            {
                m_Buffer = dataPointer,
                m_Length = length,
                m_AllocatorLabel = allocator,
                m_MinIndex = 0,
                m_MaxIndex = length - 1,
            };
        }

        public static void* GetUnsafePtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;

        public static void* GetUnsafeReadOnlyPtr<T>(this NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;

        public static void* GetUnsafeReadOnlyPtr<T>(this NativeArray<T>.ReadOnly nativeArray) where T : struct => nativeArray.m_Buffer;

        public static void* GetUnsafeBufferPointerWithoutChecks<T>(NativeArray<T> nativeArray) where T : struct => nativeArray.m_Buffer;

        public static AtomicSafetyHandle GetAtomicSafetyHandle<T>(NativeArray<T> array) where T : struct => array.m_Safety;

        public static void SetAtomicSafetyHandle<T>(ref NativeArray<T> array, AtomicSafetyHandle safety) where T : struct => array.m_Safety = safety;
    }

    public static unsafe partial class NativeSliceUnsafeUtility
    {
        public static NativeSlice<T> ConvertExistingDataToNativeSlice<T>(void* dataPointer, int stride, int length) where T : struct
        {
            if (length < 0) throw new ArgumentException("Invalid length of '" + length + "'. It must be greater than 0.", nameof(length));
            if (stride < 0) throw new ArgumentException("Invalid stride '" + stride + "'. It must be greater than 0.", nameof(stride));
            return new NativeSlice<T>
            {
                m_Stride = stride,
                m_Buffer = (byte*)dataPointer,
                m_Length = length,
                m_MinIndex = 0,
                m_MaxIndex = length - 1,
            };
        }

        public static void* GetUnsafePtr<T>(this NativeSlice<T> nativeSlice) where T : struct => nativeSlice.m_Buffer;

        public static void* GetUnsafeReadOnlyPtr<T>(this NativeSlice<T> nativeSlice) where T : struct => nativeSlice.m_Buffer;

        public static AtomicSafetyHandle GetAtomicSafetyHandle<T>(NativeSlice<T> slice) where T : struct => slice.m_Safety;

        public static void SetAtomicSafetyHandle<T>(ref NativeSlice<T> slice, AtomicSafetyHandle safety) where T : struct => slice.m_Safety = safety;
    }
}
