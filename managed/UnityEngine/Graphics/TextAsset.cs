using System;
using System.Text;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The contents of a text or binary file (.txt, .json, .bytes, .csv, ...).</summary>
    [ClassId(49)]
    public partial class TextAsset : Object
    {
        internal byte[] m_Bytes = Array.Empty<byte>();

        public TextAsset() : this("") { }

        public TextAsset(string text)
        {
            m_Bytes = Encoding.UTF8.GetBytes(text ?? "");
        }

        public TextAsset(ReadOnlySpan<byte> bytes)
        {
            m_Bytes = bytes.ToArray();
        }

        internal TextAsset(byte[] bytes, string name)
        {
            m_Bytes = bytes;
            m_Name = name;
        }

        /// <summary>The bytes decoded as UTF-8 (a byte order mark is skipped, as Unity does).</summary>
        public string text
        {
            get
            {
                var b = m_Bytes;
                if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
                return Encoding.UTF8.GetString(b);
            }
        }

        public byte[] bytes => (byte[])m_Bytes.Clone();

        public long dataSize => m_Bytes.Length;

        public Unity.Collections.NativeArray<T> GetData<T>() where T : struct
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            var array = new T[m_Bytes.Length / size];
            Buffer.BlockCopy(m_Bytes, 0, array, 0, array.Length * size);
            return new Unity.Collections.NativeArray<T>(array, Unity.Collections.Allocator.None);
        }

        public override string ToString() => text;
    }
}
