using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections
{
    /// <summary>A strided view of native memory (NativeArray elements or one field of them).</summary>
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    [DebuggerDisplay("Length = {Length}")]
    [DebuggerTypeProxy(typeof(NativeSliceDebugView<>))]
    public unsafe partial struct NativeSlice<T> : IEnumerable<T>, IEquatable<NativeSlice<T>> where T : struct
    {
        [NativeDisableUnsafePtrRestriction]
        internal byte* m_Buffer;
        internal int m_Stride;
        internal int m_Length;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        internal AtomicSafetyHandle m_Safety;

        public NativeSlice(NativeSlice<T> slice, int start) : this(slice, start, slice.Length - start) { }

        public NativeSlice(NativeSlice<T> slice, int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), $"Slice start {start} < 0.");
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), $"Slice length {length} < 0.");
            if (start + length > slice.Length)
                throw new ArgumentException($"Slice start + length ({start + length}) range must be <= slice.Length ({slice.Length})");
            if ((slice.m_MinIndex != 0 || slice.m_MaxIndex != slice.m_Length - 1) &&
                (start < slice.m_MinIndex || slice.m_MaxIndex < start || slice.m_MaxIndex < start + length - 1))
                throw new ArgumentException("Slice may not be used on a restricted range slice", nameof(slice));
            m_MinIndex = 0;
            m_MaxIndex = length - 1;
            m_Safety = slice.m_Safety;
            m_Stride = slice.m_Stride;
            m_Buffer = slice.m_Buffer + m_Stride * start;
            m_Length = length;
        }

        public NativeSlice(NativeArray<T> array) : this(array, 0, array.Length) { }

        public NativeSlice(NativeArray<T> array, int start) : this(array, start, array.Length - start) { }

        public NativeSlice(NativeArray<T> array, int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), $"Slice start {start} < 0.");
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), $"Slice length {length} < 0.");
            if (start + length > array.Length)
                throw new ArgumentException($"Slice start + length ({start + length}) range must be <= array.Length ({array.Length})");
            if ((array.m_MinIndex != 0 || array.m_MaxIndex != array.m_Length - 1) &&
                (start < array.m_MinIndex || array.m_MaxIndex < start || array.m_MaxIndex < start + length - 1))
                throw new ArgumentException("Slice may not be used on a restricted range array", nameof(array));
            if (start + length < 0) throw new ArgumentException("Slice start + length ({start + length}) causes an integer overflow");
            m_MinIndex = 0;
            m_MaxIndex = length - 1;
            m_Safety = array.m_Safety;
            m_Stride = UnsafeUtility.SizeOf<T>();
            m_Buffer = (byte*)array.m_Buffer + m_Stride * start;
            m_Length = length;
        }

        public static implicit operator NativeSlice<T>(NativeArray<T> array) => new NativeSlice<T>(array);

        public int Stride => m_Stride;

        public int Length => m_Length;

        public T this[int index]
        {
            get
            {
                CheckReadIndex(index);
                return UnsafeUtility.ReadArrayElementWithStride<T>(m_Buffer, index, m_Stride);
            }
            [WriteAccessRequired]
            set
            {
                CheckWriteIndex(index);
                UnsafeUtility.WriteArrayElementWithStride(m_Buffer, index, m_Stride, value);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckReadIndex(int index)
        {
            if (index < m_MinIndex || index > m_MaxIndex) FailOutOfRangeError(index);
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckWriteIndex(int index)
        {
            if (index < m_MinIndex || index > m_MaxIndex) FailOutOfRangeError(index);
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void FailOutOfRangeError(int index)
        {
            if (index < Length && (m_MinIndex != 0 || m_MaxIndex != Length - 1))
                throw new IndexOutOfRangeException($"Index {index} is out of restricted IJobParallelFor range [{m_MinIndex}...{m_MaxIndex}] in ReadWriteBuffer.\n" +
                    "ReadWriteBuffers are restricted to only read & write the element at the job index. You can use double buffering strategies to avoid race conditions due to reading & writing in parallel to the same elements from a job.");
            throw new IndexOutOfRangeException($"Index {index} is out of range of '{Length}' Length.");
        }

        public NativeSlice<U> SliceConvert<U>() where U : struct
        {
            int size = UnsafeUtility.SizeOf<U>();
            NativeSlice<U> result = default;
            result.m_Buffer = m_Buffer;
            result.m_Stride = size;
            result.m_Length = m_Length * m_Stride / size;
            if (m_Stride != UnsafeUtility.SizeOf<T>())
                throw new InvalidOperationException("SliceConvert requires that stride matches the size of the source type");
            if (m_MinIndex != 0 || m_MaxIndex != m_Length - 1)
                throw new InvalidOperationException("SliceConvert may not be used on a restricted range array");
            if (m_Stride * m_Length % size != 0)
                throw new InvalidOperationException("SliceConvert requires that Length * sizeof(T) is a multiple of sizeof(U).");
            result.m_MinIndex = 0;
            result.m_MaxIndex = result.m_Length - 1;
            result.m_Safety = m_Safety;
            return result;
        }

        public NativeSlice<U> SliceWithStride<U>(int offset) where U : struct
        {
            NativeSlice<U> result = default;
            result.m_Buffer = m_Buffer + offset;
            result.m_Stride = m_Stride;
            result.m_Length = m_Length;
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "SliceWithStride offset must be >= 0");
            if (offset + UnsafeUtility.SizeOf<U>() > UnsafeUtility.SizeOf<T>())
                throw new ArgumentException("SliceWithStride sizeof(U) + offset must be <= sizeof(T)", nameof(offset));
            result.m_MinIndex = m_MinIndex;
            result.m_MaxIndex = m_MaxIndex;
            result.m_Safety = m_Safety;
            return result;
        }

        public NativeSlice<U> SliceWithStride<U>() where U : struct => SliceWithStride<U>(0);

        [WriteAccessRequired]
        public void CopyFrom(NativeSlice<T> slice)
        {
            if (Length != slice.Length)
                throw new ArgumentException($"slice.Length ({slice.Length}) does not match the Length of this instance ({Length}).", nameof(slice));
            UnsafeUtility.MemCpyStride(this.GetUnsafePtr(), Stride, slice.GetUnsafeReadOnlyPtr(), slice.Stride, UnsafeUtility.SizeOf<T>(), m_Length);
        }

        [WriteAccessRequired]
        public void CopyFrom(T[] array)
        {
            if (Length != array.Length)
                throw new ArgumentException($"array.Length ({array.Length}) does not match the Length of this instance ({Length}).", nameof(array));
            var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            int size = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemCpyStride(this.GetUnsafePtr(), Stride, (void*)handle.AddrOfPinnedObject(), size, size, m_Length);
            handle.Free();
        }

        public void CopyTo(NativeArray<T> array)
        {
            if (Length != array.Length)
                throw new ArgumentException($"array.Length ({array.Length}) does not match the Length of this instance ({Length}).", nameof(array));
            int size = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemCpyStride(array.GetUnsafePtr(), size, this.GetUnsafeReadOnlyPtr(), Stride, size, m_Length);
        }

        public void CopyTo(T[] array)
        {
            if (Length != array.Length)
                throw new ArgumentException($"array.Length ({array.Length}) does not match the Length of this instance ({Length}).", nameof(array));
            var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            int size = UnsafeUtility.SizeOf<T>();
            UnsafeUtility.MemCpyStride((void*)handle.AddrOfPinnedObject(), size, this.GetUnsafeReadOnlyPtr(), Stride, size, m_Length);
            handle.Free();
        }

        public T[] ToArray()
        {
            var array = new T[Length];
            CopyTo(array);
            return array;
        }

        public Enumerator GetEnumerator() => new Enumerator(ref this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(ref this);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(NativeSlice<T> other) => m_Buffer == other.m_Buffer && m_Stride == other.m_Stride && m_Length == other.m_Length;

        public override bool Equals(object obj) => obj is NativeSlice<T> other && Equals(other);

        public override int GetHashCode()
        {
            int hash = (int)(long)m_Buffer;
            hash = (hash * 397) ^ m_Stride;
            return (hash * 397) ^ m_Length;
        }

        public static bool operator ==(NativeSlice<T> left, NativeSlice<T> right) => left.Equals(right);

        public static bool operator !=(NativeSlice<T> left, NativeSlice<T> right) => !left.Equals(right);

        public partial struct Enumerator : IEnumerator<T>
        {
            NativeSlice<T> m_Array;
            int m_Index;

            public Enumerator(ref NativeSlice<T> array)
            {
                m_Array = array;
                m_Index = -1;
            }

            public void Dispose() { }

            public bool MoveNext() => ++m_Index < m_Array.Length;

            public void Reset() => m_Index = -1;

            public T Current => m_Array[m_Index];

            object IEnumerator.Current => Current;
        }
    }

    internal sealed unsafe partial class NativeSliceDebugView<T> where T : struct
    {
        NativeSlice<T> m_Array;

        public NativeSliceDebugView(NativeSlice<T> array)
        {
            m_Array = array;
        }

        public T[] Items
        {
            get
            {
                var items = new T[m_Array.Length];
                for (int i = 0; i < items.Length; i++)
                    items[i] = UnsafeUtility.ReadArrayElementWithStride<T>(UnsafeAccess(m_Array), i, m_Array.Stride);
                return items;
            }
        }

        static unsafe void* UnsafeAccess(NativeSlice<T> slice) => slice.m_Buffer;
    }

    public static partial class NativeSliceExtensions
    {
        public static NativeSlice<T> Slice<T>(this NativeArray<T> thisArray) where T : struct => new NativeSlice<T>(thisArray);

        public static NativeSlice<T> Slice<T>(this NativeArray<T> thisArray, int start) where T : struct => new NativeSlice<T>(thisArray, start);

        public static NativeSlice<T> Slice<T>(this NativeArray<T> thisArray, int start, int length) where T : struct => new NativeSlice<T>(thisArray, start, length);

        public static NativeSlice<T> Slice<T>(this NativeSlice<T> thisSlice) where T : struct => thisSlice;

        public static NativeSlice<T> Slice<T>(this NativeSlice<T> thisSlice, int start) where T : struct => new NativeSlice<T>(thisSlice, start);

        public static NativeSlice<T> Slice<T>(this NativeSlice<T> thisSlice, int start, int length) where T : struct => new NativeSlice<T>(thisSlice, start, length);
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static unsafe partial class NativeSliceUnsafeUtility
    {
        public static AtomicSafetyHandle GetAtomicSafetyHandle<T>(NativeSlice<T> slice) where T : struct => slice.m_Safety;

        public static void SetAtomicSafetyHandle<T>(ref NativeSlice<T> slice, AtomicSafetyHandle safety) where T : struct => slice.m_Safety = safety;

        public static NativeSlice<T> ConvertExistingDataToNativeSlice<T>(void* dataPointer, int stride, int length) where T : struct
        {
            if (length < 0) throw new ArgumentException($"Invalid length of '{length}'. It must be greater than 0.", nameof(length));
            if (stride < 0) throw new ArgumentException($"Invalid stride '{stride}'. It must be greater than 0.", nameof(stride));
            return new NativeSlice<T>
            {
                m_Stride = stride,
                m_Buffer = (byte*)dataPointer,
                m_Length = length,
                m_MinIndex = 0,
                m_MaxIndex = length - 1,
            };
        }

        public static void* GetUnsafePtr<T>(this NativeSlice<T> nativeSlice) where T : struct
        {
            AtomicSafetyHandle.CheckWriteAndThrow(nativeSlice.m_Safety);
            return nativeSlice.m_Buffer;
        }

        public static void* GetUnsafeReadOnlyPtr<T>(this NativeSlice<T> nativeSlice) where T : struct
        {
            AtomicSafetyHandle.CheckReadAndThrow(nativeSlice.m_Safety);
            return nativeSlice.m_Buffer;
        }
    }
}
