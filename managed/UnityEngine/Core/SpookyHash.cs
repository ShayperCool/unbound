using System;
using System.Buffers.Binary;

namespace UnityEngine
{
    /// <summary>Bob Jenkins' SpookyHash V2, 128-bit form: the hash Unity's Hash128 and
    /// HashUnsafeUtilities compute. Both halves are seeds on input and the result on output, so
    /// hashing more data into an existing value continues from it. Little-endian reads; no
    /// allocations.</summary>
    internal static unsafe partial class SpookyHash
    {
        public static void Hash(void* message, ulong length, ulong* hash1, ulong* hash2) =>
            Hash(new ReadOnlySpan<byte>(message, checked((int)length)), ref *hash1, ref *hash2);

        /// <summary>The algorithm's constant: odd, not too regular (0xdeadbeefdeadbeef).</summary>
        const ulong Constant = 0xdeadbeefdeadbeefUL;
        /// <summary>Twelve 64-bit state words make one 96-byte block of the long form.</summary>
        const int BlockSize = 96;
        /// <summary>Messages shorter than two blocks take the short form.</summary>
        const int ShortLimit = 2 * BlockSize;

        internal static void Hash(ReadOnlySpan<byte> message, ref ulong hash1, ref ulong hash2)
        {
            if (message.Length < ShortLimit)
            {
                Short(message, ref hash1, ref hash2);
                return;
            }
            ulong h0 = hash1, h3 = hash1, h6 = hash1, h9 = hash1;
            ulong h1 = hash2, h4 = hash2, h7 = hash2, h10 = hash2;
            ulong h2 = Constant, h5 = Constant, h8 = Constant, h11 = Constant;
            int whole = message.Length / BlockSize * BlockSize;
            for (int offset = 0; offset < whole; offset += BlockSize)
                Mix(message.Slice(offset, BlockSize), ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7, ref h8, ref h9, ref h10, ref h11);

            // The last partial block, zero-padded, with its length in the final byte.
            Span<byte> last = stackalloc byte[BlockSize];
            last.Clear();
            var rest = message.Slice(whole);
            rest.CopyTo(last);
            last[BlockSize - 1] = (byte)rest.Length;
            h0 += Word(last, 0); h1 += Word(last, 1); h2 += Word(last, 2); h3 += Word(last, 3);
            h4 += Word(last, 4); h5 += Word(last, 5); h6 += Word(last, 6); h7 += Word(last, 7);
            h8 += Word(last, 8); h9 += Word(last, 9); h10 += Word(last, 10); h11 += Word(last, 11);
            for (int i = 0; i < 3; i++)
                EndPartial(ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7, ref h8, ref h9, ref h10, ref h11);
            hash1 = h0;
            hash2 = h1;
        }

        static ulong Word(ReadOnlySpan<byte> data, int index) => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(index * 8, 8));

        static ulong Rot(ulong x, int k) => (x << k) | (x >> (64 - k));

        static void Short(ReadOnlySpan<byte> message, ref ulong hash1, ref ulong hash2)
        {
            int length = message.Length;
            int remainder = length % 32;
            ulong a = hash1, b = hash2, c = Constant, d = Constant;
            var p = message;
            if (length > 15)
            {
                for (int n = length / 32; n > 0; n--)
                {
                    c += Word(p, 0);
                    d += Word(p, 1);
                    ShortMix(ref a, ref b, ref c, ref d);
                    a += Word(p, 2);
                    b += Word(p, 3);
                    p = p.Slice(32);
                }
                if (remainder >= 16)
                {
                    c += Word(p, 0);
                    d += Word(p, 1);
                    ShortMix(ref a, ref b, ref c, ref d);
                    p = p.Slice(16);
                    remainder -= 16;
                }
            }

            // The last 0..15 bytes and the total length (in the top byte of d).
            d += (ulong)length << 56;
            if (remainder == 0)
            {
                c += Constant;
                d += Constant;
            }
            else
            {
                if (remainder > 8) d += Tail(p.Slice(8, remainder - 8));
                c += Tail(p.Slice(0, Math.Min(remainder, 8)));
            }
            ShortEnd(ref a, ref b, ref c, ref d);
            hash1 = a;
            hash2 = b;
        }

        /// <summary>Up to 8 bytes as a little-endian word.</summary>
        static ulong Tail(ReadOnlySpan<byte> bytes)
        {
            ulong value = 0;
            for (int i = bytes.Length - 1; i >= 0; i--) value = (value << 8) | bytes[i];
            return value;
        }

        static void Mix(ReadOnlySpan<byte> data, ref ulong s0, ref ulong s1, ref ulong s2, ref ulong s3, ref ulong s4, ref ulong s5,
            ref ulong s6, ref ulong s7, ref ulong s8, ref ulong s9, ref ulong s10, ref ulong s11)
        {
            s0 += Word(data, 0); s2 ^= s10; s11 ^= s0; s0 = Rot(s0, 11); s11 += s1;
            s1 += Word(data, 1); s3 ^= s11; s0 ^= s1; s1 = Rot(s1, 32); s0 += s2;
            s2 += Word(data, 2); s4 ^= s0; s1 ^= s2; s2 = Rot(s2, 43); s1 += s3;
            s3 += Word(data, 3); s5 ^= s1; s2 ^= s3; s3 = Rot(s3, 31); s2 += s4;
            s4 += Word(data, 4); s6 ^= s2; s3 ^= s4; s4 = Rot(s4, 17); s3 += s5;
            s5 += Word(data, 5); s7 ^= s3; s4 ^= s5; s5 = Rot(s5, 28); s4 += s6;
            s6 += Word(data, 6); s8 ^= s4; s5 ^= s6; s6 = Rot(s6, 39); s5 += s7;
            s7 += Word(data, 7); s9 ^= s5; s6 ^= s7; s7 = Rot(s7, 57); s6 += s8;
            s8 += Word(data, 8); s10 ^= s6; s7 ^= s8; s8 = Rot(s8, 55); s7 += s9;
            s9 += Word(data, 9); s11 ^= s7; s8 ^= s9; s9 = Rot(s9, 54); s8 += s10;
            s10 += Word(data, 10); s0 ^= s8; s9 ^= s10; s10 = Rot(s10, 22); s9 += s11;
            s11 += Word(data, 11); s1 ^= s9; s10 ^= s11; s11 = Rot(s11, 46); s10 += s0;
        }

        static void EndPartial(ref ulong h0, ref ulong h1, ref ulong h2, ref ulong h3, ref ulong h4, ref ulong h5,
            ref ulong h6, ref ulong h7, ref ulong h8, ref ulong h9, ref ulong h10, ref ulong h11)
        {
            h11 += h1; h2 ^= h11; h1 = Rot(h1, 44);
            h0 += h2; h3 ^= h0; h2 = Rot(h2, 15);
            h1 += h3; h4 ^= h1; h3 = Rot(h3, 34);
            h2 += h4; h5 ^= h2; h4 = Rot(h4, 21);
            h3 += h5; h6 ^= h3; h5 = Rot(h5, 38);
            h4 += h6; h7 ^= h4; h6 = Rot(h6, 33);
            h5 += h7; h8 ^= h5; h7 = Rot(h7, 10);
            h6 += h8; h9 ^= h6; h8 = Rot(h8, 13);
            h7 += h9; h10 ^= h7; h9 = Rot(h9, 38);
            h8 += h10; h11 ^= h8; h10 = Rot(h10, 53);
            h9 += h11; h0 ^= h9; h11 = Rot(h11, 42);
            h10 += h0; h1 ^= h10; h0 = Rot(h0, 54);
        }

        static void ShortMix(ref ulong h0, ref ulong h1, ref ulong h2, ref ulong h3)
        {
            h2 = Rot(h2, 50); h2 += h3; h0 ^= h2;
            h3 = Rot(h3, 52); h3 += h0; h1 ^= h3;
            h0 = Rot(h0, 30); h0 += h1; h2 ^= h0;
            h1 = Rot(h1, 41); h1 += h2; h3 ^= h1;
            h2 = Rot(h2, 54); h2 += h3; h0 ^= h2;
            h3 = Rot(h3, 48); h3 += h0; h1 ^= h3;
            h0 = Rot(h0, 38); h0 += h1; h2 ^= h0;
            h1 = Rot(h1, 37); h1 += h2; h3 ^= h1;
            h2 = Rot(h2, 62); h2 += h3; h0 ^= h2;
            h3 = Rot(h3, 34); h3 += h0; h1 ^= h3;
            h0 = Rot(h0, 5); h0 += h1; h2 ^= h0;
            h1 = Rot(h1, 36); h1 += h2; h3 ^= h1;
        }

        static void ShortEnd(ref ulong h0, ref ulong h1, ref ulong h2, ref ulong h3)
        {
            h3 ^= h2; h2 = Rot(h2, 15); h3 += h2;
            h0 ^= h3; h3 = Rot(h3, 52); h0 += h3;
            h1 ^= h0; h0 = Rot(h0, 26); h1 += h0;
            h2 ^= h1; h1 = Rot(h1, 51); h2 += h1;
            h3 ^= h2; h2 = Rot(h2, 28); h3 += h2;
            h0 ^= h3; h3 = Rot(h3, 9); h0 += h3;
            h1 ^= h0; h0 = Rot(h0, 47); h1 += h0;
            h2 ^= h1; h1 = Rot(h1, 54); h2 += h1;
            h3 ^= h2; h2 = Rot(h2, 32); h3 += h2;
            h0 ^= h3; h3 = Rot(h3, 25); h0 += h3;
            h1 ^= h0; h0 = Rot(h0, 63); h1 += h0;
        }
    }
}
