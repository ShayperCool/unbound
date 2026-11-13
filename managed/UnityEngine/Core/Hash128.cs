using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    /// <summary>A 128-bit hash: two 64-bit halves, written as 32 hex digits.</summary>
    [Serializable]
    public partial struct Hash128 : IComparable, IComparable<Hash128>, IEquatable<Hash128>
    {
        internal ulong u64_0;
        internal ulong u64_1;

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

        // Every overload hashes the bytes of its input with SpookyHash V2 (SpookyHash.cs),
        // continuing from the current value; Compute starts from the zero hash. Strings hash as
        // UTF-8, values and blittable elements as their in-memory bytes.

        public static Hash128 Compute(string data) { var h = new Hash128(); h.Append(data); return h; }
        public static Hash128 Compute(int val) { var h = new Hash128(); h.Append(val); return h; }
        public static Hash128 Compute(float val) { var h = new Hash128(); h.Append(val); return h; }
        public static Hash128 Compute<T>(T[] data) where T : struct { var h = new Hash128(); h.AppendArray(data, 0, data.Length, "Compute", checkRange: false); return h; }
        public static Hash128 Compute<T>(T[] data, int start, int count) where T : struct { var h = new Hash128(); h.AppendArray(data, start, count, "Compute", checkRange: true); return h; }
        public static Hash128 Compute<T>(List<T> data) where T : struct { var h = new Hash128(); h.AppendList(data, 0, data.Count, "Compute", checkRange: false); return h; }
        public static Hash128 Compute<T>(List<T> data, int start, int count) where T : struct { var h = new Hash128(); h.AppendList(data, start, count, "Compute", checkRange: true); return h; }
        public static Hash128 Compute<T>(NativeArray<T> data) where T : struct { var h = new Hash128(); h.Append(data); return h; }
        public static Hash128 Compute<T>(NativeArray<T> data, int start, int count) where T : struct { var h = new Hash128(); h.Append(data, start, count); return h; }
        public static Hash128 Compute<T>(ref T val) where T : unmanaged { var h = new Hash128(); h.Append(ref val); return h; }
        public static unsafe Hash128 Compute(void* data, ulong size) { var h = new Hash128(); h.Append(data, size); return h; }

        public void Append(string data)
        {
            data ??= "";
            int max = Encoding.UTF8.GetMaxByteCount(data.Length);
            byte[] rented = max > StackLimit ? ArrayPool<byte>.Shared.Rent(max) : null;
            Span<byte> buffer = rented ?? stackalloc byte[StackLimit];
            int length = Encoding.UTF8.GetBytes(data, buffer);
            AppendBytes(buffer.Slice(0, length));
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }

        public void Append(int val) => Append(ref val);
        public void Append(float val) => Append(ref val);
        public void Append<T>(T[] data) where T : struct => AppendArray(data, 0, data.Length, "Append", checkRange: false);
        public void Append<T>(T[] data, int start, int count) where T : struct => AppendArray(data, start, count, "Append", checkRange: true);
        public void Append<T>(List<T> data) where T : struct => AppendList(data, 0, data.Count, "Append", checkRange: false);
        public void Append<T>(List<T> data, int start, int count) where T : struct => AppendList(data, start, count, "Append", checkRange: true);
        public void Append<T>(NativeArray<T> data) where T : struct => AppendBytes(MemoryMarshal.AsBytes(data.AsReadOnlySpan()));

        public void Append<T>(NativeArray<T> data, int start, int count) where T : struct
        {
            CheckRange(start, count, data.Length);
            AppendBytes(MemoryMarshal.AsBytes(data.AsReadOnlySpan().Slice(start, count)));
        }

        public void Append<T>(ref T val) where T : unmanaged => AppendBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref val, 1)));
        public unsafe void Append(void* data, ulong size) => AppendBytes(SpookyHash.Bytes(data, size));

        /// <summary>Strings up to this many UTF-8 bytes are encoded on the stack.</summary>
        const int StackLimit = 256;

        void AppendArray<T>(T[] data, int start, int count, string method, bool checkRange) where T : struct
        {
            if (!UnsafeUtility.IsArrayBlittable(data))
                throw new ArgumentException($"Array passed to {method} must be blittable.\n" + UnsafeUtility.GetReasonForArrayNonBlittable(data));
            if (checkRange) CheckRange(start, count, data.Length);
            AppendBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(data, start, count)));
        }

        void AppendList<T>(List<T> data, int start, int count, string method, bool checkRange) where T : struct
        {
            if (!UnsafeUtility.IsGenericListBlittable<T>())
                throw new ArgumentException(string.Format("List<{0}> passed to {1} must be blittable.\n{2}", typeof(T), method, UnsafeUtility.GetReasonForGenericListNonBlittable<T>()));
            if (checkRange) CheckRange(start, count, data.Count);
            AppendBytes(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(data).Slice(start, count)));
        }

        static void CheckRange(int start, int count, int length)
        {
            if (start < 0 || count < 0 || start + count > length)
                // Unity passes the text as the parameter name; Mono writes it after the message.
                throw new ArgumentOutOfRangeException(null,
                    $"Specified argument was out of the range of valid values.\nParameter name: Bad start/count arguments (start:{start} count:{count})");
        }

        internal void AppendBytes(ReadOnlySpan<byte> data) => SpookyHash.Hash(data, ref u64_0, ref u64_1);

        public override bool Equals(object obj) => obj is Hash128 h && h == this;
        public bool Equals(Hash128 obj) => this == obj;
        public override int GetHashCode() => u64_0.GetHashCode() ^ u64_1.GetHashCode();
        public static bool operator ==(Hash128 hash1, Hash128 hash2) => hash1.u64_0 == hash2.u64_0 && hash1.u64_1 == hash2.u64_1;
        public static bool operator !=(Hash128 hash1, Hash128 hash2) => !(hash1 == hash2);
        public static bool operator <(Hash128 x, Hash128 y) => x.CompareTo(y) < 0;
        public static bool operator >(Hash128 x, Hash128 y) => x.CompareTo(y) > 0;
    }
}
