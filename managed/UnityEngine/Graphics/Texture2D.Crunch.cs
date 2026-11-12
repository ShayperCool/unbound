using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using Unbound.Runtime;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        bool SourceCrunch => m_SourceFormat is TextureFormat f && BlockFormats.IsCrunch(f);
        byte[] m_CrunchBlocks;
        ulong m_CrunchHash;
        int m_CrunchArchiveLength = -1;

        ReadOnlySpan<byte> CrunchArchive()
        {
            var raw = m_Store ?? SourceRawData() ?? Array.Empty<byte>();
            return new ReadOnlySpan<byte>(raw, 0, m_CrunchArchiveLength >= 0 ? m_CrunchArchiveLength : raw.Length);
        }

        void ResetCrunch()
        {
            m_CrunchBlocks = null;
            m_CrunchHash = 0;
            m_CrunchArchiveLength = -1;
        }

        byte[] CrunchBlocks()
        {
            var archive = CrunchArchive();
            ulong hash = Fnv1a(archive);
            if (m_CrunchBlocks != null && hash == m_CrunchHash) return m_CrunchBlocks;
            if (archive.IsEmpty) throw new UnityException("Texture has no Crunch archive. Load a complete archive before reading or uploading pixels.");
            m_CrunchBlocks = DecodeCrunchArchive(archive);
            m_CrunchHash = hash;
            return m_CrunchBlocks;
        }

        byte[] DecodeCrunchArchive(ReadOnlySpan<byte> archive)
        {
            var json = Native.Call("texture-crunch-blocks", JsonSerializer.Serialize(new { data = Convert.ToBase64String(archive) }));
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.GetProperty("format").GetInt32() != (int)m_SourceFormat.Value
                || r.GetProperty("width").GetInt32() != m_Width || r.GetProperty("height").GetInt32() != m_Height
                || r.GetProperty("mips").GetInt32() != m_SourceMips)
                throw new UnityException("Crunch archive dimensions, format or mip count differ from the texture.");
            return Convert.FromBase64String(r.GetProperty("blocks").GetString());
        }

        void EnterCrunchStore()
        {
            var raw = CrunchArchive().ToArray();
            var offsets = new int[m_SourceMips + 1];
            for (int mip = 0; mip < m_SourceMips; mip++) offsets[mip + 1] = checked(offsets[mip] + SourceLevelSize(mip));
            m_CrunchArchiveLength = raw.Length;
            // Measured views use the archive pointer with decoded-block lengths. Padding keeps
            // those lengths memory-safe; bytes beyond the archive are unmeasured and zero here.
            var store = new byte[Math.Max(raw.Length, offsets[^1])];
            raw.CopyTo(store, 0);
            m_SourceRaw = store;
            m_Store = store;
            m_StoreOffsets = offsets;
            m_StorePin = GCHandle.Alloc(store, GCHandleType.Pinned);
            m_StoreSafety = AtomicSafetyHandle.Create();
        }

        void RefreshCrunchCells()
        {
            var old = m_CrunchBlocks;
            var blocks = CrunchBlocks();
            if (ReferenceEquals(old, blocks)) return;
            EnsureBlockCells();
            int at = 0;
            for (int mip = 0; mip < m_SourceMips; mip++)
            {
                int size = SourceLevelSize(mip);
                DecodeBlockLevel(new ReadOnlySpan<byte>(blocks, at, size), mip);
                at += size;
            }
        }

        void LoadCrunch(ReadOnlySpan<byte> data)
        {
            var blocks = DecodeCrunchArchive(data);
            LeaveStore();
            m_SourceRaw = data.ToArray();
            m_CrunchArchiveLength = data.Length;
            m_CrunchBlocks = blocks;
            m_CrunchHash = Fnv1a(data);
            DecodeAllBlocks();
        }

        void ThrowIfCrunchPixels()
        {
            if (SourceCrunch) throw new ArgumentException($"Texture2D.GetPixels: texture is crunch compressed while this function does not support crunched textures. You can disable crunch compression in the Texture Import Settings. (Texture '{name}')");
        }
    }
}
