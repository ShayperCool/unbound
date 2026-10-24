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
        Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle m_Safety;

        /// <summary>A view of the asset's bytes (not a copy, as in Unity), under the asset's safety
        /// handle: accessing it after the asset is destroyed throws, as in the Editor.</summary>
        public unsafe Unity.Collections.NativeArray<T> GetData<T>() where T : struct
        {
            long size = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
            if (m_Bytes.Length % size != 0)
                throw new ArgumentException(string.Format("Type passed to {0} can't capture the asset data. Data size is {1} which is not a multiple of type size {2}", "GetData", m_Bytes.LongLength, size));
            if (!m_Pinned.IsAllocated) m_Pinned = System.Runtime.InteropServices.GCHandle.Alloc(m_Bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            if (m_Safety.versionNode == IntPtr.Zero) m_Safety = Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.Create();
            var array = Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (void*)m_Pinned.AddrOfPinnedObject(), (int)(m_Bytes.Length / size), Unity.Collections.Allocator.None);
            Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_Safety);
            return array;
        }

        internal override void OnEngineDestroy()
        {
            if (m_Safety.versionNode != IntPtr.Zero) Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.Release(m_Safety);
            if (m_Pinned.IsAllocated) m_Pinned.Free();
        }

        public override string ToString() => text;
    }
}
