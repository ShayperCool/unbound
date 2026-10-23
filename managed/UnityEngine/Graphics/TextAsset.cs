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

        System.Runtime.InteropServices.GCHandle m_Pinned;

        /// <summary>A view of the asset's bytes (not a copy, as in Unity): valid while the asset lives.</summary>
        public unsafe Unity.Collections.NativeArray<T> GetData<T>() where T : struct
        {
            if (!m_Pinned.IsAllocated) m_Pinned = System.Runtime.InteropServices.GCHandle.Alloc(m_Bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            int size = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
            return Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (void*)m_Pinned.AddrOfPinnedObject(), m_Bytes.Length / size, Unity.Collections.Allocator.None);
        }

        internal override void OnEngineDestroy()
        {
            if (m_Pinned.IsAllocated) m_Pinned.Free();
        }

        public override string ToString() => text;
    }
}
