using System;
using System.Text;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>The contents of a text or binary file (.txt, .json, .bytes, .csv, ...).</summary>
    [ClassId(49)]
    public partial class TextAsset : Object, ISerializedAsset
    {
        /// <summary>A serialized TextAsset (an AssetBundle's): its text, or its bytes when they are
        /// not UTF-8 (`{_bytes: hex}`).</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var script = body.Get("m_Script");
            if (script == null) return;
            var hex = script.Kind == YKind.Map ? script.Str("_bytes") : null;
            m_Bytes = hex != null ? Convert.FromHexString(hex) : Encoding.UTF8.GetBytes(script.AsString());
        }

        byte[] m_Data = Array.Empty<byte>();
        /// <summary>The file an imported asset reads its bytes from on first use (null once read, or
        /// for an asset made in memory): project text and script files stay on disk until asked
        /// for, so scripts loaded only to be listed cost no memory.</summary>
        string m_File;

        internal byte[] m_Bytes
        {
            get
            {
                if (m_File != null)
                {
                    try { m_Data = System.IO.File.ReadAllBytes(m_File); }
                    catch (System.IO.IOException) { m_Data = Array.Empty<byte>(); }
                    m_File = null;
                }
                return m_Data;
            }
            set { m_Data = value ?? Array.Empty<byte>(); m_File = null; }
        }

        /// <summary>An imported asset whose bytes are the file's, read on first use.</summary>
        internal TextAsset(string file, string name, long size)
        {
            m_File = file;
            m_SourceFile = file;
            m_Size = size;
            m_Name = name;
        }

        /// <summary>The file an imported asset was made from (null for other text assets).</summary>
        readonly string m_SourceFile;

        /// <summary>
        /// Resources.UnloadAsset (and UnloadUnusedAssets) of an imported text asset: its loaded
        /// bytes are freed and read from its file again when next used. Views GetData handed out
        /// end with the bytes they show, as they do when the asset is destroyed. The object stays
        /// the same one (as measured for materials; text assets are unmeasured). False for a text
        /// asset no file made.
        /// </summary>
        internal bool UnloadData()
        {
            if (m_SourceFile == null) return false;
            if (m_Safety.versionNode != IntPtr.Zero)
            {
                Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.Release(m_Safety);
                m_Safety = default;
            }
            if (m_Pinned.IsAllocated) m_Pinned.Free();
            m_Data = Array.Empty<byte>();
            m_File = m_SourceFile;
            try { m_Size = new System.IO.FileInfo(m_SourceFile).Length; }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
            return true;
        }

        long m_Size = -1;

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

        /// <summary>The text view of the stored bytes, with Native BOM detection and fallback.</summary>
        public string text => DecodeTextBytes(m_Bytes);

        public byte[] bytes => (byte[])m_Bytes.Clone();

        public long dataSize => m_File != null && m_Size >= 0 ? m_Size : m_Bytes.Length;

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
