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
    /// of its element type. The level's bytes are the array's memory from start × element size.
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
            if (ValueSize<T>.Cached) return ValueSize<T>.Size;
            object first = data[0];
            // The first element's runtime type decides: a null reference or empty Nullable has none.
            if (first == null) throw new NullReferenceException();
            return Marshal.SizeOf(first);
        }

        static class Layouts<T>
        {
            internal static readonly DeclaredLayout Value = DeclaredLayout.Of<T>();
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

        /// <summary>A pixel block outside the level (the Unity 6000.0.71f1 Editor's text for reads
        /// and writes alike, tests/projects/apierrors `texture.*_block_*`).</summary>
        internal static ArgumentException OutsideLevel(string api, string name) =>
            new ArgumentException($"{api}: the size of data to be written is outside the target buffer bounds. (Texture '{name}')");

        internal static ArgumentException NotEnoughData(string api, string name) =>
            new ArgumentException($"{api}: size of data to be filled was larger than the size of data available in the source array. (Texture '{name}')");

        /// <summary>Texture2D.GetPixelData's mip check (the release's reference assemblies).</summary>
        internal static ArgumentException InvalidMipLevel(int mip, int mipCount) =>
            new ArgumentException($"The passed in miplevel {mip} is invalid. It needs to be in the range 0 and {mipCount - 1}");

        internal static ArgumentException InvalidArgument(string api, string name) =>
            new ArgumentException($"{api}: one of the arguments is not valid. (Texture '{name}')");

        /// <summary>The pixel APIs' element and face checks of array and cube textures (the Unity
        /// 6000.0.71f1 Editor, tests/projects/apierrors `texture.{array,cube_array,cubemap}.*`).</summary>
        internal static ArgumentException InvalidElement(string api, string name) =>
            new ArgumentException($"{api}: invalid array element. (Texture '{name}')");

        internal static ArgumentException InvalidFace(string api, string name) =>
            new ArgumentException($"{api}: invalid cubemap face. (Texture '{name}')");

        /// <summary>GetPixelData's level, element and face checks of Cubemap, Texture2DArray,
        /// CubemapArray and Texture3D (the release's reference assemblies; Texture3D's mip text has
        /// two spaces before the last level, CubemapArray's face text two after "invalid." and no
        /// full stop).</summary>
        internal static ArgumentException MipRange(int mip, int mipCount, string before = " ") =>
            new ArgumentException($"The passed in miplevel {mip} is invalid. The valid range is 0 through{before}{mipCount - 1}");

        internal static ArgumentException ElementRange(int element, int count) =>
            new ArgumentException($"The passed in element {element} is invalid. The valid range is 0 through {count - 1}");

        internal static ArgumentException FaceRange(CubemapFace face, bool array) => array
            ? new ArgumentException($"The passed in face {face} is invalid.  The valid range is 0 through 5")
            : new ArgumentException($"The passed in face {face} is invalid. The valid range is 0 through 5.");

        internal static bool ValidFace(CubemapFace face) => face >= CubemapFace.PositiveX && face <= CubemapFace.NegativeZ;

        /// <summary>The single-texel accessors' checks (GetPixel, SetPixel, GetPixelBilinear): an
        /// undefined level or image is logged and the access does nothing (the Unity 6000.0.71f1
        /// Editor, tests/projects/apierrors `texture.*_pixel*_bad_*`; the text has no closing
        /// parenthesis).</summary>
        internal static bool TexelAccess(bool valid, string api, string what, int last)
        {
            if (valid) return true;
            UnityEngine.Debug.LogError($"{api} called on an undefined {what} (valid values are 0 - {last}");
            return false;
        }

        /// <summary>SetPixels' and SetPixels32's array of a layered texture's level (the Unity
        /// 6000.0.71f1 Editor, tests/projects/apierrors `texture.{array,volume,cube_array}.set_pixels*`):
        /// none is not a valid argument, a short one has too little data, a longer one is written in
        /// part by SetPixels and refused by SetPixels32.</summary>
        internal static void CheckLayerLength(Array colors, int size, bool refuseLonger, string api, string name)
        {
            if (colors == null) throw InvalidArgument(api, name);
            if (colors.Length < size) throw NotEnoughData(api, name);
            if (refuseLonger && colors.Length > size) throw OutsideLevel(api, name);
        }

        /// <summary>A level's bytes in a managed array: the array itself, or a copy in the declared
        /// layout for structs that hold references (<see cref="DeclaredLayout"/>).</summary>
        internal static ReadOnlySpan<byte> Level<T>(T[] data, int start, int elementSize, int levelBytes, string api, string name)
        {
            var layout = Layouts<T>.Value;
            if (layout == null)
            {
                var storage = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetArrayDataReference(data)),
                    checked(data.Length * Unsafe.SizeOf<T>()));
                return storage.Slice((int)Offset(storage.Length, data.Length, start, elementSize, levelBytes, api, name, typeof(T)), levelBytes);
            }
            if (layout.Unsupported != null)
            {
                Offset((long)data.Length * Unsafe.SizeOf<T>(), data.Length, start, elementSize, levelBytes, api, name, typeof(T));
                throw new UnityException($"Unbound: {api} of {typeof(T).Name} elements is not supported: {layout.Unsupported}.");
            }
            return layout.Copy(data, Offset((long)data.Length * layout.Size, data.Length, start, elementSize, levelBytes, api, name, typeof(T)), levelBytes);
        }

        internal static unsafe ReadOnlySpan<byte> Level<T>(NativeArray<T> data, int start, int elementSize, int levelBytes, string api, string name) where T : struct
        {
            var storage = new ReadOnlySpan<byte>(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), checked(data.Length * elementSize));
            return storage.Slice((int)Offset(storage.Length, data.Length, start, elementSize, levelBytes, api, name, typeof(T)), levelBytes);
        }

        /// <summary>Where the level starts in the array's memory, after the size checks.</summary>
        static long Offset(long storage, int length, int start, int elementSize, int levelBytes, string api, string name, Type element)
        {
            long offset = (long)start * elementSize;
            long provided = ((long)length - start) * elementSize;
            if (provided >= 0 && provided < levelBytes)
                throw NotEnoughData(api, name);
            // A start index past the end, or a marshaled element size larger than the array's own
            // stride, makes the reference read memory after the array, whose contents are undefined.
            if (offset + levelBytes > storage)
                throw new UnityException($"Unbound: {api} would read past the end of the {element.Name} array (start index {start}, " +
                                         $"element size {elementSize}, {storage} bytes in the array, {levelBytes} needed); memory outside the array is not read.");
            return offset;
        }
    }
}
