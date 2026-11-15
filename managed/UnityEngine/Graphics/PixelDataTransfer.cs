using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    /// <summary>SetPixelData's arguments, shared by Texture2D, Texture2DArray, Texture3D and
    /// CubemapArray (matches the Unity 6000.0.71f1 Editor, tests/projects/pixeltransfer). The texture
    /// checks, in order: a negative start index, readability, missing data, then (after the element
    /// size) its element, face and mip level, and that (length − start) × element size covers the
    /// level. A managed array's element size is the marshaled size of its first element's type (a
    /// bool counts four bytes, a char one; a null first reference has no type), a NativeArray's that
    /// of its element type. The level's bytes are the array's own storage from start × element size;
    /// element types that hold object references are not supported.
    /// </summary>
    internal static class PixelDataTransfer
    {
        internal static void CheckStart(int start)
        {
            if (start < 0) throw new UnityException("SetPixelData: sourceDataStartIndex cannot be less than 0.");
        }

        static UnityException NoData() => new UnityException("No texture data provided to SetPixelData.");

        internal static int ElementSize<T>(T[] data)
        {
            if (data == null || data.Length == 0) throw NoData();
            int size;
            if (ValueSize<T>.Cached) size = ValueSize<T>.Size;
            else
            {
                object first = data[0];
                // The first element's runtime type decides: a null reference or empty Nullable has none.
                if (first == null) throw new NullReferenceException();
                size = Marshal.SizeOf(first);
            }
            // The reference then copies the array's memory as its runtime lays it out, object
            // addresses included, with fields in declaration order; this runtime orders a struct's
            // references first.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new UnityException($"Unbound: SetPixelData of {typeof(T).Name} elements, which hold object references, is not supported: " +
                                         "the texture would receive the array's memory, object addresses included.");
            return size;
        }

        /// <summary>The marshaled size of a value type other than Nullable, which has a single
        /// runtime type (generic structs included), computed once per type.</summary>
        static class ValueSize<T>
        {
            internal static readonly bool Cached;
            internal static readonly int Size;

            static ValueSize()
            {
                if (!typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null) return;
                try
                {
                    Size = Marshal.SizeOf((object)default(T));
                    Cached = true;
                }
                catch (ArgumentException)
                {
                    // Not marshalable: each call sizes its first element and raises the runtime's error.
                }
            }
        }

        internal static int ElementSize<T>(NativeArray<T> data) where T : struct
        {
            if (!data.IsCreated || data.Length == 0) throw NoData();
            return UnsafeUtility.SizeOf<T>();
        }

        internal static ArgumentException InvalidMip(string api, string name) =>
            new ArgumentException($"{api}: invalid mipmap level. (Texture '{name}')");

        internal static ArgumentException InvalidArgument(string api, string name) =>
            new ArgumentException($"{api}: one of the arguments is not valid. (Texture '{name}')");

        /// <summary>A level's bytes in a managed array (no copy; any element type).</summary>
        internal static ReadOnlySpan<byte> Level<T>(T[] data, int start, int elementSize, int levelBytes, string api, string name)
        {
            var storage = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(data)),
                checked(data.Length * Unsafe.SizeOf<T>()));
            return Level(storage, data.Length, start, elementSize, levelBytes, api, name, typeof(T));
        }

        internal static unsafe ReadOnlySpan<byte> Level<T>(NativeArray<T> data, int start, int elementSize, int levelBytes, string api, string name) where T : struct
        {
            var storage = new ReadOnlySpan<byte>(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), checked(data.Length * elementSize));
            return Level(storage, data.Length, start, elementSize, levelBytes, api, name, typeof(T));
        }

        static ReadOnlySpan<byte> Level(ReadOnlySpan<byte> storage, int length, int start, int elementSize, int levelBytes, string api, string name, Type element)
        {
            long offset = (long)start * elementSize;
            long provided = ((long)length - start) * elementSize;
            if (provided >= 0 && provided < levelBytes)
                throw new ArgumentException($"{api}: size of data to be filled was larger than the size of data available in the source array. (Texture '{name}')");
            // A start index past the end, or a marshaled element size larger than the array's own
            // stride, makes the reference read memory after the array, whose contents are undefined.
            if (offset + levelBytes > storage.Length)
                throw new UnityException($"Unbound: {api} would read past the end of the {element.Name} array (start index {start}, " +
                                         $"element size {elementSize}, {storage.Length} bytes in the array, {levelBytes} needed); memory outside the array is not read.");
            return storage.Slice((int)offset, levelBytes);
        }
    }
}
