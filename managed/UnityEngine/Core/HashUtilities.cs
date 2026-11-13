using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>SpookyHash V2 over raw memory; both halves are seeds on input (see Hash128).</summary>
    public static unsafe partial class HashUnsafeUtilities
    {
        public static void ComputeHash128(void* data, ulong dataSize, ulong* hash1, ulong* hash2) =>
            SpookyHash.Hash(new ReadOnlySpan<byte>(data, checked((int)dataSize)), ref *hash1, ref *hash2);

        public static void ComputeHash128(void* data, ulong dataSize, Hash128* hash) =>
            SpookyHash.Hash(new ReadOnlySpan<byte>(data, checked((int)dataSize)), ref hash->u64_0, ref hash->u64_1);
    }

    /// <summary>Hashes of values continuing from an existing Hash128.</summary>
    public static unsafe partial class HashUtilities
    {
        /// <summary>Vectors and matrices hash each component as (int)(value * 1000 + 0.5).</summary>
        const float QuantisationScale = 1000f;

        public static void AppendHash(ref Hash128 inHash, ref Hash128 outHash)
        {
            var value = inHash;
            outHash.AppendBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<Hash128>(&value, 1)));
        }

        public static void QuantisedMatrixHash(ref Matrix4x4 value, ref Hash128 hash)
        {
            int* quantised = stackalloc int[16];
            for (int i = 0; i < 16; i++) quantised[i] = Quantise(value[i]);
            hash.AppendBytes(new ReadOnlySpan<byte>(quantised, 16 * sizeof(int)));
        }

        public static void QuantisedVectorHash(ref Vector3 value, ref Hash128 hash)
        {
            int* quantised = stackalloc int[3];
            for (int i = 0; i < 3; i++) quantised[i] = Quantise(value[i]);
            hash.AppendBytes(new ReadOnlySpan<byte>(quantised, 3 * sizeof(int)));
        }

        public static void ComputeHash128<T>(ref T value, ref Hash128 hash) where T : struct =>
            hash.AppendBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)));

        public static void ComputeHash128(byte[] value, ref Hash128 hash)
        {
            // Like Unity, an empty array fails on its first element.
            _ = value[0];
            hash.AppendBytes(value);
        }

        static int Quantise(float component) => (int)(component * QuantisationScale + 0.5f);
    }
}
