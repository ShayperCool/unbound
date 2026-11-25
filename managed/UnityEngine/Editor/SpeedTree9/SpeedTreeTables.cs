using System;
using System.Buffers.Binary;
using System.Text;

namespace Unbound.SpeedTree
{
    /// <summary>A three-float vector as SpeedTree files store it.</summary>
    internal struct SpeedTreeVector3 { internal float X, Y, Z; }

    /// <summary>
    /// The table container SpeedTree 8 (<c>.st</c>) and 9 (<c>.st9</c>) files share after their
    /// signature. A table is a uint32 count and that many uint32 offsets, relative to the table's
    /// start, to its entries. An entry is a scalar, a packed struct, a string (uint32 length counting
    /// a terminating zero, then the bytes), an array (uint32 count, then the items) or another table.
    /// Little-endian. A read outside the bytes throws <see cref="FormatException"/> through
    /// <see cref="Guard{T}"/>.
    /// </summary>
    internal readonly struct SpeedTreeTables
    {
        readonly byte[] m_Data;

        internal SpeedTreeTables(byte[] data) => m_Data = data;

        internal int Length => m_Data.Length;

        /// <summary>Runs <paramref name="read"/>, turning a read outside the bytes into a
        /// <see cref="FormatException"/> that names <paramref name="what"/>.</summary>
        internal static T Guard<T>(string what, Func<T> read)
        {
            try
            {
                return read();
            }
            catch (Exception e) when (e is ArgumentOutOfRangeException || e is IndexOutOfRangeException || e is OverflowException)
            {
                throw new FormatException($"the {what} is truncated or damaged", e);
            }
        }

        /// <summary>Whether the bytes start with <paramref name="signature"/>.</summary>
        internal static bool HasSignature(byte[] data, string signature)
        {
            if (data.Length < signature.Length) return false;
            for (int i = 0; i < signature.Length; i++)
                if (data[i] != signature[i]) return false;
            return true;
        }

        /// <summary>The absolute offset of entry <paramref name="index"/> of the table at <paramref name="table"/>.</summary>
        internal int Entry(int table, int index) => table + (int)UInt(table + (index + 1) * 4);

        /// <summary>The number of entries of the table at <paramref name="table"/>.</summary>
        internal int EntryCount(int table) => Count(table);

        /// <summary>The count at <paramref name="at"/> of items of <paramref name="itemSize"/> bytes
        /// (offsets for a table) that follow it, which must fit in the file.</summary>
        internal int Count(int at, int itemSize = 4)
        {
            long count = UInt(at);
            if (at + 4 + count * itemSize > m_Data.Length) throw new OverflowException();
            return (int)count;
        }

        /// <summary>Reads each entry of the table at <paramref name="table"/> with <paramref name="read"/>.</summary>
        internal T[] Tables<T>(int table, Func<int, T> read)
        {
            var items = new T[Count(table)];
            for (int i = 0; i < items.Length; i++) items[i] = read(Entry(table, i));
            return items;
        }

        /// <summary>The float array (count, then the values) at <paramref name="at"/>.</summary>
        internal float[] Floats(int at)
        {
            var values = new float[Count(at)];
            for (int i = 0; i < values.Length; i++) values[i] = Float(at + 4 + i * 4);
            return values;
        }

        internal uint UInt(int at) => BinaryPrimitives.ReadUInt32LittleEndian(m_Data.AsSpan(at, 4));
        internal float Float(int at) => BinaryPrimitives.ReadSingleLittleEndian(m_Data.AsSpan(at, 4));
        internal byte Byte(int at) => m_Data[at];
        internal SpeedTreeVector3 Vec3(int at) => new SpeedTreeVector3 { X = Float(at), Y = Float(at + 4), Z = Float(at + 8) };

        internal string String(int at)
        {
            int length = Count(at, 1);
            return length <= 1 ? "" : Encoding.UTF8.GetString(m_Data, at + 4, length - 1);
        }
    }
}
