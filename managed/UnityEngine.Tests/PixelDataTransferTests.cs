using System;
using Unity.Collections;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    // tests/projects/pixeltransfer: two identical Unity 6000.0.71f1 captures.
    public class PixelDataTransferTests
    {
        struct BoolPair { public bool flag; public byte value; }
        struct NamedValue { public string name; public int value; }
        struct Pair<T> { public T first; public T second; }

        static Texture2D R8(string name)
        {
            var texture = new Texture2D(8, 8, TextureFormat.R8, false) { name = name, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixelData(new byte[64], 0);
            return texture;
        }

        // A caller without a struct constraint compiles; the element size is the marshaled one.
        static void Fill<T>(Texture2D texture, T[] data, int start) => texture.SetPixelData(data, 0, start);

        static Exception Thrown(Action call) => Record.Exception(call);

        [Fact]
        public void ManagedArraysStrideByTheMarshaledSizeOfTheFirstElement()
        {
            var texture = R8("r8");
            var bools = new bool[80];
            for (int i = 0; i < bools.Length; i++) bools[i] = i % 3 == 0;
            Fill(texture, bools, 2);
            var bytes = texture.GetPixelData<byte>(0).ToArray();
            // Four bytes per bool: the level starts at byte 8, the ninth bool.
            for (int i = 0; i < 64; i++) Assert.Equal(bools[8 + i] ? 1 : 0, bytes[i]);

            var chars = new char[64];
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)(0x2041 + i * 5);
            Fill(texture, chars, 0);
            bytes = texture.GetPixelData<byte>(0).ToArray();
            // One byte per char: the level is the first 32 chars' UTF-16 storage.
            Assert.Equal(new byte[] { 0x41, 0x20, 0x46, 0x20 }, bytes[..4]);
            var error = Thrown(() => Fill(texture, chars, 3));
            Assert.IsType<ArgumentException>(error);
            Assert.Equal("Texture2D.SetPixelData: size of data to be filled was larger than the size of data available in the source array. (Texture 'r8')", error.Message);

            var pairs = new BoolPair[40];
            for (int i = 0; i < pairs.Length; i++) pairs[i] = new BoolPair { flag = i % 2 == 1, value = (byte)(i + 100) };
            Fill(texture, pairs, 1);
            bytes = texture.GetPixelData<byte>(0).ToArray();
            // Eight marshaled bytes per pair over a two-byte stride: the level starts at the fifth pair.
            Assert.Equal(new byte[] { 0, 104, 1, 105 }, bytes[..4]);

            // Elements holding references would copy object addresses: unsupported, loudly, after
            // the marshaled size (which a string field has) and before any texel changes.
            var before = texture.GetPixelData<byte>(0).ToArray();
            var named = new NamedValue[8];
            for (int i = 0; i < named.Length; i++) named[i] = new NamedValue { name = "n" + i, value = 0x01020304 * (i + 1) };
            Assert.StartsWith("Unbound: SetPixelData of NamedValue elements, which hold object references, is not supported",
                Assert.IsType<UnityException>(Thrown(() => Fill(texture, named, 0))).Message);
            Assert.StartsWith("Unbound:", Assert.IsType<UnityException>(Thrown(() => Fill(texture, new object[] { 1, 2 }, 0))).Message);
            Assert.Equal(before, texture.GetPixelData<byte>(0).ToArray());

            // Generic structs and Nullable elements are sized by the first element's runtime type.
            var generic = new Pair<byte>[40];
            for (int i = 0; i < generic.Length; i++) generic[i] = new Pair<byte> { first = (byte)i, second = (byte)(i + 50) };
            Fill(texture, generic, 3);
            bytes = texture.GetPixelData<byte>(0).ToArray();
            Assert.Equal(new byte[] { 3, 53, 4, 54 }, bytes[..4]);
            var nullable = new int?[20];
            nullable[0] = 1;
            nullable[1] = 0x0A0B0C0D;
            Fill(texture, nullable, 0);
            bytes = texture.GetPixelData<byte>(0).ToArray();
            // Four marshaled bytes per element over the eight-byte Nullable storage.
            Assert.Equal(new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A }, bytes[..16]);
            Assert.IsType<NullReferenceException>(Thrown(() => Fill(texture, new int?[20], 0)));
            Assert.IsType<ArgumentException>(Thrown(() => Fill(texture, new DateTime[20], 0)));
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void ArgumentsAreCheckedInTheReleaseOrder()
        {
            var texture = R8("locked");
            texture.Apply(false, true);
            Assert.Equal("SetPixelData: sourceDataStartIndex cannot be less than 0.", Assert.IsType<UnityException>(Thrown(() => Fill<byte>(texture, null, -1))).Message);
            Assert.StartsWith("Texture 'locked' is not readable", Assert.IsType<UnityException>(Thrown(() => Fill<byte>(texture, null, 0))).Message);
            Object.DestroyImmediate(texture);

            texture = R8("r8");
            Assert.Equal("No texture data provided to SetPixelData.", Assert.IsType<UnityException>(Thrown(() => Fill<byte>(texture, null, 0))).Message);
            Assert.Equal("No texture data provided to SetPixelData.", Assert.IsType<UnityException>(Thrown(() => texture.SetPixelData(default(NativeArray<byte>), 0))).Message);
            Assert.IsType<NullReferenceException>(Thrown(() => Fill(texture, new object[] { null, 1 }, 0)));
            Assert.IsType<ArgumentException>(Thrown(() => Fill(texture, new[] { "a", "b" }, 0)));
            // The mip level is checked before the size.
            Assert.Equal("Texture2D.SetPixelData: invalid mipmap level. (Texture 'r8')", Assert.IsType<ArgumentException>(Thrown(() => texture.SetPixelData(new byte[3], 4))).Message);
            Assert.IsType<ArgumentException>(Thrown(() => Fill(texture, new byte[64], 64)));
            // The reference reads past the array here; Unbound does not.
            var past = Assert.IsType<UnityException>(Thrown(() => Fill(texture, new byte[64], 65)));
            Assert.StartsWith("Unbound: Texture2D.SetPixelData would read past the end of the Byte array", past.Message);
            Assert.StartsWith("Unbound:", Assert.IsType<UnityException>(Thrown(() => Fill(texture, new bool[20], 0))).Message);
            Object.DestroyImmediate(texture);
        }

        [Fact]
        public void LayeredTexturesShareTheRule()
        {
            var array = new Texture2DArray(8, 8, 2, TextureFormat.R8, false) { name = "array", hideFlags = HideFlags.HideAndDontSave };
            var bools = new bool[80];
            bools[8] = true;
            array.SetPixelData(bools, 0, 1, 2);
            Assert.Equal(1, array.GetPixelData<byte>(0, 1)[0]);
            Assert.Equal("Texture2DArray.SetPixelData: one of the arguments is not valid. (Texture 'array')",
                Assert.IsType<ArgumentException>(Thrown(() => array.SetPixelData(new byte[64], 0, 2))).Message);
            Assert.Equal("Texture2DArray.SetPixelData: invalid mipmap level. (Texture 'array')",
                Assert.IsType<ArgumentException>(Thrown(() => array.SetPixelData(new byte[2], 1, 0))).Message);

            var cubes = new CubemapArray(4, 1, TextureFormat.R8, false) { name = "cubes", hideFlags = HideFlags.HideAndDontSave };
            Assert.Equal("CubemapArray.SetPixelData: one of the arguments is not valid. (Texture 'cubes')",
                Assert.IsType<ArgumentException>(Thrown(() => cubes.SetPixelData(new byte[16], 0, CubemapFace.Unknown, 0))).Message);
            var chars = new char[17];
            chars[0] = (char)0x2041;
            cubes.SetPixelData(chars, 0, CubemapFace.NegativeY, 0, 1);
            Assert.Equal(0x20, cubes.GetPixelData<byte>(0, CubemapFace.NegativeY, 0)[0]);

            var volume = new Texture3D(4, 4, 4, TextureFormat.R8, false) { name = "volume", hideFlags = HideFlags.HideAndDontSave };
            Assert.Equal("Texture3D.SetPixelData: size of data to be filled was larger than the size of data available in the source array. (Texture 'volume')",
                Assert.IsType<ArgumentException>(Thrown(() => volume.SetPixelData(new byte[5], 0))).Message);
            Assert.Equal("No texture data provided to SetPixelData.", Assert.IsType<UnityException>(Thrown(() => volume.SetPixelData(new byte[0], 0))).Message);

            // Writing a level reads the array in place.
            var data = new byte[64];
            array.SetPixelData(data, 0, 0);
            array.SetPixelData(bools, 0, 1, 2);
            AllocationMeasurement.EmptyAllocationContext();
            long before = GC.GetAllocatedBytesForCurrentThread();
            array.SetPixelData(data, 0, 0);
            array.SetPixelData(bools, 0, 1, 2);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Object.DestroyImmediate(array);
            Object.DestroyImmediate(cubes);
            Object.DestroyImmediate(volume);
        }
    }
}
