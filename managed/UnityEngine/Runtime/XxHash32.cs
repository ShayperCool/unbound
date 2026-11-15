using System;
using System.Buffers.Binary;

namespace Unbound.Runtime
{
    /// <summary>The 32-bit xxHash of a byte span with a seed (the reference algorithm by Yann
    /// Collet). Chaining a call's result into the next call's seed hashes several spans in turn.</summary>
    internal static class XxHash32
    {
        const uint Prime1 = 2654435761u;
        const uint Prime2 = 2246822519u;
        const uint Prime3 = 3266489917u;
        const uint Prime4 = 668265263u;
        const uint Prime5 = 374761393u;

        static uint Rotl(uint x, int r) => (x << r) | (x >> (32 - r));

        static uint Round(uint lane, uint input) => Rotl(lane + input * Prime2, 13) * Prime1;

        public static uint Hash(ReadOnlySpan<byte> data, uint seed)
        {
            int length = data.Length, i = 0;
            uint hash;
            if (length >= 16)
            {
                uint v1 = seed + Prime1 + Prime2, v2 = seed + Prime2, v3 = seed, v4 = seed - Prime1;
                for (; i <= length - 16; i += 16)
                {
                    v1 = Round(v1, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i)));
                    v2 = Round(v2, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 4)));
                    v3 = Round(v3, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 8)));
                    v4 = Round(v4, BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 12)));
                }
                hash = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
            }
            else
            {
                hash = seed + Prime5;
            }
            hash += (uint)length;
            for (; i + 4 <= length; i += 4)
                hash = Rotl(hash + BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i)) * Prime3, 17) * Prime4;
            for (; i < length; i++)
                hash = Rotl(hash + data[i] * Prime5, 11) * Prime1;
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
