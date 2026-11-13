using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes Hash128 values into values.jsonl in $HASH128_OUT (tests/value_probe.py): Compute and
/// Append over strings (empty, null, ASCII, non-ASCII, around the 192-byte short limit), ints,
/// floats, arrays, ranges, lists, native arrays, unmanaged values and raw memory, chains that
/// continue from a seeded hash, the HashUtilities and HashUnsafeUtilities helpers, and the
/// argument checks.
/// </summary>
public class Hash128Probe : MonoBehaviour
{
    static StreamWriter output;

    struct Pair { public int a; public float b; }
    struct WithReference { public string s; }

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"}\n");

    static void Row(string key, Hash128 hash) => Row(key, hash.ToString() + " " + hash.isValid);

    static void Try(string key, Func<Hash128> f)
    {
        try { Row(key, f()); }
        catch (Exception e) { Row(key, "throws " + e.GetType().Name + ": " + e.Message); }
    }

    static string Text(int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = (char)('a' + (i * 7) % 26);
        return new string(chars);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var dir = Environment.GetEnvironmentVariable("HASH128_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                Write();
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static unsafe void Write()
    {
        Row("default", new Hash128());
        Row("ctor_u32", new Hash128(1, 2, 3, 4));
        Row("ctor_u64", new Hash128(0x0123456789abcdefUL, 0xfedcba9876543210UL));
        Row("parse", Hash128.Parse("0123456789abcdef0011223344556677"));
        Row("parse_short", Hash128.Parse("abc"));
        Row("parse_upper", Hash128.Parse("0123456789ABCDEF0011223344556677"));

        Try("string_empty", () => Hash128.Compute(""));
        Try("string_null", () => Hash128.Compute((string)null));
        Try("string_abc", () => Hash128.Compute("abc"));
        Try("string_non_ascii", () => Hash128.Compute("héllo 世界 😀"));
        foreach (var n in new[] { 1, 3, 4, 7, 8, 9, 12, 15, 16, 17, 31, 32, 33, 48, 63, 95, 96, 191, 192, 193, 287, 288, 300, 1000 })
        {
            var text = Text(n);
            Try("string_len_" + n, () => Hash128.Compute(text));
        }

        foreach (var v in new[] { 0, 1, 42, -1, int.MinValue, int.MaxValue })
            Try("int_" + v, () => Hash128.Compute(v));
        foreach (var v in new[] { 0f, -0f, 1.5f, -3.25f, float.NaN, float.PositiveInfinity, 1e-30f })
            Try("float_" + BitConverter.SingleToInt32Bits(v).ToString("x8"), () => Hash128.Compute(v));

        var ints = new int[100];
        for (int i = 0; i < ints.Length; i++) ints[i] = i * 31 - 7;
        var bytes = new byte[300];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 13 + 5);
        var vectors = new[] { new Vector3(1, 2, 3), new Vector3(-0.5f, 0.25f, 9) };
        Try("int_array", () => Hash128.Compute(ints));
        Try("int_array_empty", () => Hash128.Compute(new int[0]));
        Try("int_array_range", () => Hash128.Compute(ints, 10, 20));
        Try("int_array_range_empty", () => Hash128.Compute(ints, 5, 0));
        Try("int_array_range_bad", () => Hash128.Compute(ints, 90, 20));
        Try("int_array_range_negative", () => Hash128.Compute(ints, -1, 2));
        Try("byte_array", () => Hash128.Compute(bytes));
        Try("byte_array_191", () => Hash128.Compute(bytes, 0, 191));
        Try("byte_array_192", () => Hash128.Compute(bytes, 0, 192));
        Try("vector_array", () => Hash128.Compute(vectors));
        Try("struct_array", () => Hash128.Compute(new[] { new Pair { a = 3, b = 0.5f } }));
        Try("non_blittable_array", () => Hash128.Compute(new[] { new WithReference { s = "x" } }));
        var list = new List<int>(ints);
        Try("int_list", () => Hash128.Compute(list));
        Try("int_list_range", () => Hash128.Compute(list, 10, 20));
        Try("int_list_range_bad", () => Hash128.Compute(list, 0, 101));
        using (var native = new NativeArray<int>(ints, Allocator.Temp))
        {
            Try("native_array", () => Hash128.Compute(native));
            Try("native_array_range", () => Hash128.Compute(native, 10, 20));
            Try("native_array_range_bad", () => Hash128.Compute(native, 99, 2));
        }
        var pair = new Pair { a = 7, b = -2f };
        Try("ref_struct", () => Hash128.Compute(ref pair));
        var matrix = Matrix4x4.TRS(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), Vector3.one * 2);
        Try("ref_matrix", () => Hash128.Compute(ref matrix));
        fixed (byte* p = bytes)
        {
            var ptr = p;
            Try("pointer_0", () => Hash128.Compute(ptr, 0));
            Try("pointer_5", () => Hash128.Compute(ptr, 5));
            Try("pointer_250", () => Hash128.Compute(ptr, 250));
        }

        // Append continues from the current value, whatever it is.
        Try("append_chain", () =>
        {
            var h = new Hash128();
            h.Append("abc"); h.Append(42); h.Append(1.5f); h.Append(ints); h.Append(ref pair);
            h.Append(ints, 3, 4); h.Append(list); h.Append(list, 1, 2);
            return h;
        });
        Try("append_seeded_string", () => { var h = new Hash128(1, 2, 3, 4); h.Append("abc"); return h; });
        Try("append_seeded_int", () => { var h = new Hash128(1, 2, 3, 4); h.Append(42); return h; });
        Try("append_seeded_long", () => { var h = new Hash128(0x0123456789abcdefUL, 0xfedcba9876543210UL); h.Append(bytes); return h; });
        Try("append_empty_string", () => { var h = new Hash128(5, 6, 7, 8); h.Append(""); return h; });
        Try("append_twice_vs_once", () => { var h = new Hash128(); h.Append("ab"); h.Append("c"); return h; });
        using (var native = new NativeArray<int>(ints, Allocator.Temp))
        {
            Try("append_native", () => { var h = new Hash128(9, 8, 7, 6); h.Append(native); h.Append(native, 2, 3); return h; });
        }
        fixed (byte* p = bytes)
        {
            var ptr = p;
            Try("append_pointer", () => { var h = new Hash128(9, 8, 7, 6); h.Append(ptr, 200); return h; });
        }
        Try("append_non_blittable", () => { var h = new Hash128(); h.Append(new[] { new WithReference() }); return h; });
        Try("append_list_bad", () => { var h = new Hash128(); h.Append(list, 50, 51); return h; });

        Try("utilities_append_hash", () => { var a = new Hash128(1, 2, 3, 4); var b = new Hash128(5, 6, 7, 8); HashUtilities.AppendHash(ref a, ref b); return b; });
        Try("utilities_vector", () => { var v = new Vector3(1.2345f, -0.0004f, 1000.0006f); var h = new Hash128(1, 0, 0, 0); HashUtilities.QuantisedVectorHash(ref v, ref h); return h; });
        Try("utilities_matrix", () => { var h = new Hash128(); HashUtilities.QuantisedMatrixHash(ref matrix, ref h); return h; });
        Try("utilities_struct", () => { var h = new Hash128(2, 0, 0, 0); HashUtilities.ComputeHash128(ref pair, ref h); return h; });
        Try("utilities_bytes", () => { var h = new Hash128(3, 0, 0, 0); HashUtilities.ComputeHash128(bytes, ref h); return h; });
        Try("utilities_bytes_empty", () => { var h = new Hash128(3, 0, 0, 0); HashUtilities.ComputeHash128(new byte[0], ref h); return h; });
        fixed (byte* p = bytes)
        {
            var ptr = p;
            Try("unsafe_hash128", () => { var h = new Hash128(4, 0, 0, 0); HashUnsafeUtilities.ComputeHash128(ptr, 100, &h); return h; });
            Try("unsafe_halves", () => { ulong a = 11, b = 12; HashUnsafeUtilities.ComputeHash128(ptr, 33, &a, &b); return new Hash128(a, b); });
        }

        var hashA = Hash128.Compute("a");
        var hashB = Hash128.Compute("b");
        Row("compare", $"{hashA.CompareTo(hashB)} {hashA < hashB} {hashA > hashB} {hashA == Hash128.Parse(hashA.ToString())} {hashA.GetHashCode()}");
    }
}
