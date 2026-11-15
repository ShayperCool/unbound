using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

// Managed-array SetPixelData: argument order, element stride and the bytes each texture kind
// stores. Values are read back with GetPixelData<byte>, so only CPU copies are compared.
public class PixelTransferProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("PixelTransferProbe").AddComponent<PixelTransferProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    public struct BoolPair { public bool flag; public byte value; }
    public class Boxed { public int value; }
    public struct Pair<T> { public T first; public T second; }
    public struct NamedValue { public string name; public int value; }
    public struct ValueFirst { public int value; public string name; }
    public struct Mixed { public byte a; public string name; public short b; public string other; public int c; }
    public struct Nested { public int x; public NamedValue inner; public int y; }
    public struct Names { public string first; public string second; }
    public struct Labeled { public string label; public Color32 color; public byte tail; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    public struct Packed { public byte a; public int b; }
    public struct WithPacked { public Packed packed; public byte tail; public string name; }
    public struct Optional { public string name; public int? count; public byte tail; }
    public struct Callback { public Action action; public int value; }
    public struct LeadByteColor { public byte lead; public Color32 color; public string name; }
    public struct LeadBytePacked { public byte lead; public Packed packed; public string name; }

    StreamWriter output;
    readonly List<string> logs = new List<string>();

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void OnLog(string message, string stack, LogType type) => logs.Add(type + ": " + message);

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("PIXEL_TRANSFER_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            Application.logMessageReceived += OnLog;
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static string Hex(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) text.Append(b.ToString("x2"));
        return text.ToString();
    }

    // Runs one call; records ok or the exception with its message (exceptions the runtime's own
    // marshaling raises by type alone), the log lines it produced and the level's stored bytes.
    void Call(string key, Action call, Func<byte[]> read, bool runtimeError = false)
    {
        logs.Clear();
        try { call(); Put(key + ".result", "ok"); }
        catch (Exception e) { Put(key + ".result", runtimeError ? e.GetType().Name : e.GetType().Name + ": " + e.Message); }
        Put(key + ".logs", string.Join(" | ", logs));
        if (read != null) Put(key + ".bytes", Hex(read()));
    }

    static byte[] Pattern(int count, int seed)
    {
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = (byte)(i * 7 + seed);
        return bytes;
    }

    static Texture2D Plain(TextureFormat format, int size, bool mips, string name)
    {
        var texture = new Texture2D(size, size, format, mips) { name = name };
        int levels = texture.mipmapCount;
        for (int m = 0; m < levels; m++) texture.SetPixelData(Pattern(texture.GetPixelData<byte>(m).Length, 200 + m), m);
        return texture;
    }

    void Record()
    {
        // Ordinary elements: bytes and Color32 on R8 and RGBA32, with start offsets.
        var r8 = Plain(TextureFormat.R8, 8, false, "r8");
        Func<byte[]> r8Bytes = () => r8.GetPixelData<byte>(0).ToArray();
        Call("r8.bytes.start0", () => r8.SetPixelData(Pattern(64, 1), 0), r8Bytes);
        Call("r8.bytes.start5", () => r8.SetPixelData(Pattern(80, 3), 0, 5), r8Bytes);
        Call("r8.bytes.extra", () => r8.SetPixelData(Pattern(100, 9), 0), r8Bytes);
        var rgba = Plain(TextureFormat.RGBA32, 4, true, "rgba");
        Put("rgba.mips", rgba.mipmapCount);
        var colors = new Color32[16];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color32((byte)i, (byte)(i * 3), (byte)(255 - i), (byte)(i * 11));
        Call("rgba.color32", () => rgba.SetPixelData(colors, 0), () => rgba.GetPixelData<byte>(0).ToArray());
        Call("rgba.color32.mip1", () => rgba.SetPixelData(colors, 1, 9), () => rgba.GetPixelData<byte>(1).ToArray());
        Call("rgba.uint.mip2", () => rgba.SetPixelData(new uint[] { 0x11223344u, 0x55667788u }, 2, 1), () => rgba.GetPixelData<byte>(2).ToArray());

        // Element stride: the marshaled size of the first element, not the array's own stride.
        var bools = new bool[80];
        for (int i = 0; i < bools.Length; i++) bools[i] = i % 3 == 0;
        Call("r8.bool.start0", () => r8.SetPixelData(bools, 0), r8Bytes);
        Call("r8.bool.start2", () => r8.SetPixelData(bools, 0, 2), r8Bytes);
        var chars = new char[64];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)(0x2041 + i * 5);
        Call("r8.char.start0", () => r8.SetPixelData(chars, 0), r8Bytes);
        Call("r8.char.start3", () => r8.SetPixelData(chars, 0, 3), r8Bytes);
        var pairs = new BoolPair[40];
        for (int i = 0; i < pairs.Length; i++) pairs[i] = new BoolPair { flag = i % 2 == 1, value = (byte)(i + 100) };
        Call("r8.boolpair.start0", () => r8.SetPixelData(pairs, 0), r8Bytes);
        Call("r8.boolpair.start1", () => r8.SetPixelData(pairs, 0, 1), r8Bytes);
        // Structs holding references: the reference slots hold addresses and are cleared before
        // the bytes are compared.
        Func<byte[]> Masked(params int[] pointerSlots) => () =>
        {
            var bytes = r8.GetPixelData<byte>(0).ToArray();
            foreach (int slot in pointerSlots) Array.Clear(bytes, slot, 8);
            return bytes;
        };
        var named = new NamedValue[8];
        for (int i = 0; i < named.Length; i++) named[i] = new NamedValue { name = "n" + i, value = 0x01020304 * (i + 1) };
        Call("r8.named", () => r8.SetPixelData(named, 0), Masked(0, 16, 32, 48));
        var valueFirst = new ValueFirst[8];
        for (int i = 0; i < valueFirst.Length; i++) valueFirst[i] = new ValueFirst { name = "v", value = 0x11223344 + i };
        Call("r8.valueFirst", () => r8.SetPixelData(valueFirst, 0), Masked(8, 24, 40, 56));
        Call("r8.valueFirst.start1", () => r8.SetPixelData(valueFirst, 0, 1), Masked(8, 24, 40, 56));
        var mixed = new Mixed[4];
        for (int i = 0; i < mixed.Length; i++) mixed[i] = new Mixed { a = (byte)(0xA0 + i), name = "m", b = (short)(0x0B00 + i), other = "o", c = 0x0C0C0C00 + i };
        Call("r8.mixed", () => r8.SetPixelData(mixed, 0), Masked(8, 24, 48));
        Call("r8.mixed.start1", () => r8.SetPixelData(mixed, 0, 1), Masked(8, 24, 48));
        var nested = new Nested[4];
        for (int i = 0; i < nested.Length; i++) nested[i] = new Nested { x = 0x0A0A0A00 + i, inner = new NamedValue { name = "i", value = 0x0C0C0C00 + i }, y = 0x0B0B0B00 + i };
        Call("r8.nested", () => r8.SetPixelData(nested, 0), Masked(8, 40));
        var names = new Names[8];
        for (int i = 0; i < names.Length; i++) names[i] = new Names { first = "a", second = "b" };
        Call("r8.names", () => r8.SetPixelData(names, 0), Masked(0, 8, 16, 24, 32, 40, 48, 56));
        var labeled = new Labeled[8];
        for (int i = 0; i < labeled.Length; i++) labeled[i] = new Labeled { label = "l", color = new Color32(1, 2, 3, (byte)i), tail = 9 };
        Call("r8.labeled", () => r8.SetPixelData(labeled, 0), Masked(0, 16, 32, 48));
        var withPacked = new WithPacked[4];
        for (int i = 0; i < withPacked.Length; i++) withPacked[i] = new WithPacked { packed = new Packed { a = (byte)(7 + i), b = 0x01020304 }, tail = 5, name = "p" };
        Call("r8.withPacked", () => r8.SetPixelData(withPacked, 0), Masked(8, 24, 40, 56));
        var leadColor = new LeadByteColor[4];
        for (int i = 0; i < leadColor.Length; i++) leadColor[i] = new LeadByteColor { lead = (byte)(0xE0 + i), color = new Color32(1, 2, 3, (byte)i), name = "c" };
        Call("r8.leadColor", () => r8.SetPixelData(leadColor, 0), Masked(8, 24, 40, 56));
        var leadPacked = new LeadBytePacked[4];
        for (int i = 0; i < leadPacked.Length; i++) leadPacked[i] = new LeadBytePacked { lead = (byte)(0xE0 + i), packed = new Packed { a = (byte)(7 + i), b = 0x01020304 }, name = "q" };
        Call("r8.leadPacked", () => r8.SetPixelData(leadPacked, 0), Masked(8, 24, 40, 56));
        var optional = new Optional[4];
        optional[0] = new Optional { name = "o", count = 0x0A0B0C0D, tail = 6 };
        optional[1] = new Optional { name = "o", count = null, tail = 7 };
        optional[2] = new Optional { name = "o", count = -2, tail = 8 };
        Call("r8.optional", () => r8.SetPixelData(optional, 0), Masked(0, 24, 48));
        var callbacks = new Callback[4];
        for (int i = 0; i < callbacks.Length; i++) callbacks[i] = new Callback { action = Install, value = 0x33 + i };
        Call("r8.callback", () => r8.SetPixelData(callbacks, 0), Masked(0, 16, 32, 48));
        r8.SetPixelData(Pattern(64, 5), 0);
        var generic = new Pair<byte>[40];
        for (int i = 0; i < generic.Length; i++) generic[i] = new Pair<byte> { first = (byte)i, second = (byte)(i + 50) };
        Call("r8.generic.start3", () => r8.SetPixelData(generic, 0, 3), r8Bytes);
        var nullable = new int?[20];
        for (int i = 0; i < nullable.Length; i += 2) nullable[i] = 0x01020304 * (i + 1);
        Call("r8.nullable", () => r8.SetPixelData(nullable, 0), r8Bytes);
        Call("r8.nullableNullFirst", () => r8.SetPixelData(new int?[20], 0), r8Bytes, true);
        Call("r8.dateTime", () => r8.SetPixelData(new DateTime[20], 0), r8Bytes, true);
        Call("r8.string", () => r8.SetPixelData(new[] { "a", "b" }, 0), r8Bytes, true);
        Call("r8.object", () => r8.SetPixelData(new object[] { new object() }, 0), r8Bytes, true);
        Call("r8.boxed", () => r8.SetPixelData(new[] { new Boxed { value = 5 } }, 0), r8Bytes, true);
        Call("r8.nullFirst", () => r8.SetPixelData(new object[] { null, 1 }, 0), r8Bytes, true);
        Call("r8.nullFirstBoxed", () => r8.SetPixelData(new Boxed[2], 0), r8Bytes, true);

        // Argument order and the native checks.
        Call("r8.null", () => r8.SetPixelData((byte[])null, 0), r8Bytes);
        Call("r8.empty", () => r8.SetPixelData(new byte[0], 0), r8Bytes);
        Call("r8.startNegative", () => r8.SetPixelData(Pattern(64, 11), 0, -1), r8Bytes);
        Call("r8.startNegativeNull", () => r8.SetPixelData((byte[])null, 0, -1), r8Bytes);
        Call("r8.startAtEnd", () => r8.SetPixelData(Pattern(64, 13), 0, 64), r8Bytes);
        Call("r8.short", () => r8.SetPixelData(Pattern(63, 17), 0), r8Bytes);
        Call("r8.shortAfterStart", () => r8.SetPixelData(Pattern(70, 19), 0, 7), r8Bytes);
        Call("r8.mipMissing", () => r8.SetPixelData(Pattern(64, 21), 1), r8Bytes);
        Call("r8.mipNegative", () => r8.SetPixelData(Pattern(64, 23), -1), r8Bytes);
        Call("r8.mipMissingShort", () => r8.SetPixelData(Pattern(3, 25), 4), r8Bytes);
        Call("rgba.mip2.short", () => rgba.SetPixelData(new uint[] { 1u }, 2, 1), () => rgba.GetPixelData<byte>(2).ToArray());

        // NativeArray overload: the same argument checks with the element type's own size.
        using (var native = new NativeArray<byte>(Pattern(64, 31), Allocator.Temp))
        {
            Call("r8.native.ok", () => r8.SetPixelData(native, 0), r8Bytes);
            Call("r8.native.start4", () => r8.SetPixelData(new NativeArray<byte>(Pattern(68, 33), Allocator.Temp), 0, 4), r8Bytes);
            Call("r8.native.startNegative", () => r8.SetPixelData(native, 0, -1), r8Bytes);
        }
        Call("r8.native.default", () => r8.SetPixelData(default(NativeArray<byte>), 0), r8Bytes);
        Call("r8.native.empty", () => r8.SetPixelData(new NativeArray<byte>(0, Allocator.Temp), 0), r8Bytes);
        Call("r8.native.short", () => r8.SetPixelData(new NativeArray<byte>(Pattern(60, 35), Allocator.Temp), 0), r8Bytes);
        Call("r8.native.bool", () => r8.SetPixelData(new NativeArray<bool>(bools, Allocator.Temp), 0, 2), r8Bytes);

        // A texture without a CPU copy: the readability check comes after the start index check.
        var locked = Plain(TextureFormat.R8, 8, false, "locked");
        locked.Apply(false, true);
        Put("locked.readable", locked.isReadable);
        Call("locked.bytes", () => locked.SetPixelData(Pattern(64, 41), 0), null);
        Call("locked.null", () => locked.SetPixelData((byte[])null, 0), null);
        Call("locked.startNegative", () => locked.SetPixelData((byte[])null, 0, -1), null);
        Call("locked.native", () => locked.SetPixelData(default(NativeArray<byte>), 0), null);

        // Layered textures share the transfer rule.
        var array = new Texture2DArray(8, 8, 2, TextureFormat.R8, false) { name = "array" };
        for (int e = 0; e < 2; e++) array.SetPixelData(Pattern(64, 50 + e), 0, e);
        Func<byte[]> arrayBytes = () => Hex2(array.GetPixelData<byte>(0, 0).ToArray(), array.GetPixelData<byte>(0, 1).ToArray());
        Call("array.bool.start2", () => array.SetPixelData(bools, 0, 1, 2), arrayBytes);
        Call("array.char.start3", () => array.SetPixelData(chars, 0, 0, 3), arrayBytes);
        Call("array.boolpair", () => array.SetPixelData(pairs, 0, 1), arrayBytes);
        Call("array.null", () => array.SetPixelData((byte[])null, 0, 0), arrayBytes);
        Call("array.empty", () => array.SetPixelData(new byte[0], 0, 0), arrayBytes);
        Call("array.startNegative", () => array.SetPixelData(Pattern(64, 55), 0, 0, -1), arrayBytes);
        Call("array.short", () => array.SetPixelData(Pattern(10, 57), 0, 0), arrayBytes);
        Call("array.string", () => array.SetPixelData(new[] { "a" }, 0, 0), arrayBytes, true);
        Call("array.generic", () => array.SetPixelData(generic, 0, 1, 1), arrayBytes);
        Call("array.elementMissing", () => array.SetPixelData(Pattern(64, 59), 0, 2), arrayBytes);
        Call("array.elementNegative", () => array.SetPixelData(Pattern(64, 59), 0, -1), arrayBytes);
        Call("array.mipMissing", () => array.SetPixelData(Pattern(64, 59), 1, 0), arrayBytes);
        Call("array.mipMissingShort", () => array.SetPixelData(Pattern(2, 59), 1, 0), arrayBytes);

        var lockedArray = new Texture2DArray(4, 4, 1, TextureFormat.R8, false) { name = "lockedArray" };
        lockedArray.SetPixelData(Pattern(16, 1), 0, 0);
        lockedArray.Apply(false, true);
        Call("lockedArray.null", () => lockedArray.SetPixelData((byte[])null, 0, 0), null);
        Call("lockedArray.startNegative", () => lockedArray.SetPixelData((byte[])null, 0, 0, -1), null);

        var volume = new Texture3D(4, 4, 4, TextureFormat.R8, false) { name = "volume" };
        volume.SetPixelData(Pattern(64, 60), 0);
        Func<byte[]> volumeBytes = () => volume.GetPixelData<byte>(0).ToArray();
        Call("volume.bool.start2", () => volume.SetPixelData(bools, 0, 2), volumeBytes);
        Call("volume.boolpair.start1", () => volume.SetPixelData(pairs, 0, 1), volumeBytes);
        Call("volume.null", () => volume.SetPixelData((byte[])null, 0), volumeBytes);
        Call("volume.startNegative", () => volume.SetPixelData(Pattern(64, 61), 0, -1), volumeBytes);
        Call("volume.short", () => volume.SetPixelData(Pattern(5, 63), 0), volumeBytes);
        Call("volume.mipMissing", () => volume.SetPixelData(Pattern(64, 63), 1), volumeBytes);

        var cubes = new CubemapArray(4, 1, TextureFormat.R8, false) { name = "cubes" };
        for (int f = 0; f < 6; f++) cubes.SetPixelData(Pattern(16, 70 + f), 0, (CubemapFace)f, 0);
        Func<byte[]> cubeBytes = () => cubes.GetPixelData<byte>(0, CubemapFace.NegativeY, 0).ToArray();
        Call("cubes.bool.start2", () => cubes.SetPixelData(bools, 0, CubemapFace.NegativeY, 0, 2), cubeBytes);
        Call("cubes.char.start1", () => cubes.SetPixelData(chars, 0, CubemapFace.NegativeY, 0, 1), cubeBytes);
        Call("cubes.null", () => cubes.SetPixelData((byte[])null, 0, CubemapFace.NegativeY, 0), cubeBytes);
        Call("cubes.startNegative", () => cubes.SetPixelData(Pattern(16, 77), 0, CubemapFace.NegativeY, 0, -1), cubeBytes);
        Call("cubes.short", () => cubes.SetPixelData(Pattern(15, 79), 0, CubemapFace.NegativeY, 0), cubeBytes);
        Call("cubes.elementMissing", () => cubes.SetPixelData(Pattern(16, 81), 0, CubemapFace.NegativeY, 1), cubeBytes);
        Call("cubes.mipMissing", () => cubes.SetPixelData(Pattern(16, 81), 1, CubemapFace.NegativeY, 0), cubeBytes);
        Call("cubes.faceInvalid", () => cubes.SetPixelData(Pattern(16, 81), 0, (CubemapFace)6, 0), cubeBytes);
        Call("cubes.faceUnknown", () => cubes.SetPixelData(Pattern(16, 81), 0, CubemapFace.Unknown, 0), cubeBytes);
        Put("cubes.otherFace", Hex(cubes.GetPixelData<byte>(0, CubemapFace.PositiveZ, 0).ToArray()));
    }

    static byte[] Hex2(byte[] a, byte[] b)
    {
        var all = new byte[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }
}
