using System;
using System.Globalization;
using System.Text;

namespace UnityEngine
{
    /// <summary>A 128-bit hash: two 64-bit halves, written as 32 hex digits.</summary>
    [Serializable]
    public partial struct Hash128 : IComparable, IComparable<Hash128>, IEquatable<Hash128>
    {
        ulong u64_0;
        ulong u64_1;

        public Hash128(uint u32_0, uint u32_1, uint u32_2, uint u32_3)
        {
            u64_0 = ((ulong)u32_1 << 32) | u32_0;
            u64_1 = ((ulong)u32_3 << 32) | u32_2;
        }

        public Hash128(ulong u64_0, ulong u64_1)
        {
            this.u64_0 = u64_0;
            this.u64_1 = u64_1;
        }

        public bool isValid => u64_0 != 0 || u64_1 != 0;

        public int CompareTo(Hash128 rhs)
        {
            int c = u64_0.CompareTo(rhs.u64_0);
            return c != 0 ? c : u64_1.CompareTo(rhs.u64_1);
        }

        public int CompareTo(object obj) => obj is Hash128 h ? CompareTo(h) : 1;

        public override string ToString()
        {
            var sb = new StringBuilder(32);
            for (int i = 0; i < 8; i++) sb.Append(((byte)(u64_0 >> (8 * i))).ToString("x2", CultureInfo.InvariantCulture));
            for (int i = 0; i < 8; i++) sb.Append(((byte)(u64_1 >> (8 * i))).ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static Hash128 Parse(string hashString)
        {
            ulong a = 0, b = 0;
            if (hashString == null) return default;
            for (int i = 0; i < 16 && i * 2 + 1 < hashString.Length; i++)
            {
                if (!byte.TryParse(hashString.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return default;
                if (i < 8) a |= (ulong)value << (8 * i);
                else b |= (ulong)value << (8 * (i - 8));
            }
            return new Hash128(a, b);
        }

        public static Hash128 Compute(string data)
        {
            var h = new Hash128();
            h.Append(data);
            return h;
        }

        public static Hash128 Compute(int val)
        {
            var h = new Hash128();
            h.Append(val);
            return h;
        }

        public static Hash128 Compute(float val)
        {
            var h = new Hash128();
            h.Append(val);
            return h;
        }

        public static Hash128 Compute(byte[] data)
        {
            var h = new Hash128();
            h.Append(data);
            return h;
        }

        public void Append(string data) => Append(Encoding.UTF8.GetBytes(data ?? ""));
        public void Append(int val) => Append(BitConverter.GetBytes(val));
        public void Append(float val) => Append(BitConverter.GetBytes(val));

        public void Append(byte[] data)
        {
            // A 128-bit FNV-style mix: stable and well distributed (not SpookyHash; values differ from Unity's).
            ulong a = u64_0 == 0 && u64_1 == 0 ? 14695981039346656037UL : u64_0;
            ulong b = u64_1 == 0 && u64_0 == 0 ? 1099511628211UL : u64_1;
            foreach (var x in data ?? Array.Empty<byte>())
            {
                a = (a ^ x) * 1099511628211UL;
                b = (b ^ (a >> 29)) * 0x9E3779B97F4A7C15UL;
            }
            u64_0 = a;
            u64_1 = b;
        }

        public override bool Equals(object obj) => obj is Hash128 h && h == this;
        public bool Equals(Hash128 obj) => this == obj;
        public override int GetHashCode() => u64_0.GetHashCode() ^ u64_1.GetHashCode();
        public static bool operator ==(Hash128 hash1, Hash128 hash2) => hash1.u64_0 == hash2.u64_0 && hash1.u64_1 == hash2.u64_1;
        public static bool operator !=(Hash128 hash1, Hash128 hash2) => !(hash1 == hash2);
        public static bool operator <(Hash128 x, Hash128 y) => x.CompareTo(y) < 0;
        public static bool operator >(Hash128 x, Hash128 y) => x.CompareTo(y) > 0;
    }
}
