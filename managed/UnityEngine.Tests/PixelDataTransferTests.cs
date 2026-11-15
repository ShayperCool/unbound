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
        struct ValueFirst { public int value; public string name; }
        struct Mixed { public byte a; public string name; public short b; public string other; public int c; }
        struct Nested { public int x; public NamedValue inner; public int y; }
        struct Names { public string first; public string second; }
        struct Labeled { public string label; public Color32 color; public byte tail; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        struct Packed { public byte a; public int b; }
        struct WithPacked { public Packed packed; public byte tail; public string name; }
        struct Optional { public string name; public int? count; public byte tail; }
        struct Callback { public Action action; public int value; }
        struct LeadByteColor { public byte lead; public Color32 color; public string name; }
        struct LeadBytePacked { public byte lead; public Packed packed; public string name; }
        struct WithDecimal { public string name; public int count; public decimal amount; }
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct Overlaid { [System.Runtime.InteropServices.FieldOffset(0)] public string name; [System.Runtime.InteropServices.FieldOffset(8)] public int value; }
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

            // Structs holding references are copied in declaration order with their own alignment
            // (references eight bytes), whatever this runtime's field order; reference slots hold
            // addresses, so only the other bytes are compared.
            static byte[] Without(byte[] bytes, params int[] pointerSlots)
            {
                var copy = (byte[])bytes.Clone();
                foreach (int slot in pointerSlots) Array.Clear(copy, slot, 8);
                return copy;
            }
            var named = new NamedValue[8];
            for (int i = 0; i < named.Length; i++) named[i] = new NamedValue { name = "n", value = 0x01020304 * (i + 1) };
            Fill(texture, named, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 4, 3, 2, 1, 0, 0, 0, 0 }, Without(texture.GetPixelData<byte>(0).ToArray(), 0)[..16]);
            var valueFirst = new ValueFirst[8];
            for (int i = 0; i < valueFirst.Length; i++) valueFirst[i] = new ValueFirst { name = "n", value = 0x11223344 + i };
            Fill(texture, valueFirst, 0);
            Assert.Equal(new byte[] { 0x44, 0x33, 0x22, 0x11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x45, 0x33, 0x22, 0x11 },
                Without(texture.GetPixelData<byte>(0).ToArray(), 8)[..20]);
            var mixed = new Mixed[4];
            for (int i = 0; i < mixed.Length; i++) mixed[i] = new Mixed { a = 0xAA, name = "n", b = 0x0BBB, other = "o", c = 0x0CCCCCCC };
            // Byte, address, short, address, int: 40 bytes in declaration order, 24 in this runtime.
            Fill(texture, mixed, 0);
            var expected = new byte[64];
            expected[0] = 0xAA; expected[16] = 0xBB; expected[17] = 0x0B;
            expected[32] = 0xCC; expected[33] = 0xCC; expected[34] = 0xCC; expected[35] = 0x0C;
            expected[40] = 0xAA; expected[56] = 0xBB; expected[57] = 0x0B;
            Assert.Equal(expected, Without(texture.GetPixelData<byte>(0).ToArray(), 8, 24, 48));
            var nested = new Nested[2];
            for (int i = 0; i < nested.Length; i++) nested[i] = new Nested { x = 0x0A0A0A0A, inner = new NamedValue { name = "n", value = 0x0C0C0C0C }, y = 0x0B0B0B0B };
            Fill(texture, nested, 0);
            Assert.Equal(new byte[] { 10, 10, 10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 12, 12, 12, 12, 0, 0, 0, 0, 11, 11, 11, 11, 0, 0, 0, 0 },
                Without(texture.GetPixelData<byte>(0).ToArray(), 8)[..32]);
            // References in every slot: both addresses are written, whatever their low bytes.
            var names = new Names[8];
            for (int i = 0; i < names.Length; i++) names[i] = new Names { first = "a", second = "b" };
            Fill(texture, names, 0);
            var written = texture.GetPixelData<byte>(0).ToArray();
            Assert.Equal(new byte[64], Without(written, 0, 8, 16, 24, 32, 40, 48, 56));
            Assert.All(new[] { 0, 8 }, slot => Assert.NotEqual(0L, BitConverter.ToInt64(written, slot)));
            // A struct without references is a block of its own bytes at its fields' alignment
            // (Color32 four, a Pack = 1 struct one); a Nullable is its flag then its value.
            var labeled = new Labeled[8];
            for (int i = 0; i < labeled.Length; i++) labeled[i] = new Labeled { label = "l", color = new Color32(1, 2, 3, (byte)i), tail = 9 };
            Fill(texture, labeled, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 0, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 1, 9, 0, 0, 0 },
                Without(texture.GetPixelData<byte>(0).ToArray(), 0, 16)[..32]);
            var withPacked = new WithPacked[4];
            for (int i = 0; i < withPacked.Length; i++) withPacked[i] = new WithPacked { packed = new Packed { a = 7, b = 0x01020304 }, tail = 5, name = "p" };
            Fill(texture, withPacked, 0);
            Assert.Equal(new byte[] { 7, 4, 3, 2, 1, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, Without(texture.GetPixelData<byte>(0).ToArray(), 8)[..16]);
            // After a smaller field a block starts at its own alignment: Color32 at 4, Pack = 1 at 1.
            var leadColor = new LeadByteColor[4];
            for (int i = 0; i < leadColor.Length; i++) leadColor[i] = new LeadByteColor { lead = 0xEE, color = new Color32(1, 2, 3, 4), name = "c" };
            Fill(texture, leadColor, 0);
            Assert.Equal(new byte[] { 0xEE, 0, 0, 0, 1, 2, 3, 4, 0, 0, 0, 0, 0, 0, 0, 0 }, Without(texture.GetPixelData<byte>(0).ToArray(), 8)[..16]);
            var leadPacked = new LeadBytePacked[4];
            for (int i = 0; i < leadPacked.Length; i++) leadPacked[i] = new LeadBytePacked { lead = 0xEE, packed = new Packed { a = 7, b = 0x01020304 }, name = "q" };
            Fill(texture, leadPacked, 0);
            Assert.Equal(new byte[] { 0xEE, 7, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xEE }, Without(texture.GetPixelData<byte>(0).ToArray(), 8)[..17]);
            Assert.StartsWith("Unbound: Texture2D.SetPixelData of WithDecimal elements is not supported: the reference runtime's layout of its Decimal field is unknown",
                Assert.IsType<UnityException>(Thrown(() => Fill(texture, new WithDecimal[8], 0))).Message);
            var optional = new Optional[4];
            optional[0] = new Optional { name = "o", count = 0x0A0B0C0D, tail = 6 };
            optional[1] = new Optional { name = "o", count = null, tail = 7 };
            Fill(texture, optional, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0x0D, 0x0C, 0x0B, 0x0A, 6, 0, 0, 0, 0, 0, 0, 0,
                                      0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0, 0, 0, 0, 0 },
                Without(texture.GetPixelData<byte>(0).ToArray(), 0, 24)[..48]);
            var callbacks = new Callback[4];
            for (int i = 0; i < callbacks.Length; i++) callbacks[i] = new Callback { action = () => { }, value = 0x33 + i };
            Fill(texture, callbacks, 0);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0x33, 0, 0, 0, 0, 0, 0, 0 }, Without(texture.GetPixelData<byte>(0).ToArray(), 0)[..16]);
            // Boxed values in an object[] are the array's own memory, addresses.
            Fill(texture, new object[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, 0);
            // An explicit layout holding references is unsupported, after the size checks.
            var before = texture.GetPixelData<byte>(0).ToArray();
            Assert.IsType<ArgumentException>(Thrown(() => Fill(texture, new Overlaid[3], 0)));
            Assert.StartsWith("Unbound: Texture2D.SetPixelData of Overlaid elements is not supported: Overlaid holds references with LayoutKind.Explicit",
                Assert.IsType<UnityException>(Thrown(() => Fill(texture, new Overlaid[8], 0))).Message);
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
