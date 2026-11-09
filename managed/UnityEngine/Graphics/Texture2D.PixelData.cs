using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    public sealed partial class Texture2D
    {
        /// <summary>Once a script asks for <see cref="GetPixelData{T}"/>: the whole mip chain in the
        /// texture's format, one level after another as Unity stores it. It is then the texture's
        /// pixels: every reader and writer goes through it, so a view sees later writes and writes
        /// through a view are what the texture holds, until the texture is recreated, loses its
        /// CPU copy or an engine path takes the cells back.</summary>
        byte[] m_Store;
        int[] m_StoreOffsets;
        GCHandle m_StorePin;
        AtomicSafetyHandle m_StoreSafety;

        /// <summary>The stored bytes of a mip level (no copy, as in Unity).</summary>
        public unsafe NativeArray<T> GetPixelData<T>(int mipLevel) where T : struct
        {
            RequireReadable();
            CheckMip(mipLevel);
            EnterStore();
            int size = UnsafeUtility.SizeOf<T>();
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                (byte*)m_StorePin.AddrOfPinnedObject() + m_StoreOffsets[mipLevel], (m_StoreOffsets[mipLevel + 1] - m_StoreOffsets[mipLevel]) / size, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, m_StoreSafety);
            return array;
        }

        int TexelBytes => Texture2DTexels.TexelBytes(m_Format);

        void CheckStoreRead() { if (m_Store != null) AtomicSafetyHandle.CheckReadAndThrow(m_StoreSafety); }
        void CheckStoreWrite() { if (m_Store != null) AtomicSafetyHandle.CheckWriteAndThrow(m_StoreSafety); }

        Span<byte> StoreTexel(int mip, int i) => new Span<byte>(m_Store, m_StoreOffsets[mip] + i * TexelBytes, TexelBytes);

        void EnterStore()
        {
            if (m_Store != null) return;
            if (SourceBlocks)
            {
                // The views of a compressed texture are its blocks.
                var raw = (byte[])SourceRawData().Clone();
                var levels = new int[m_SourceMips + 1];
                for (int mip = 0; mip < m_SourceMips; mip++) levels[mip + 1] = checked(levels[mip] + SourceLevelSize(mip));
                m_Store = raw;
                m_StoreOffsets = levels;
                m_StorePin = GCHandle.Alloc(raw, GCHandleType.Pinned);
                m_StoreSafety = AtomicSafetyHandle.Create();
                return;
            }
            if (m_SourceFormat != null || !Texture2DTexels.Coded(m_Format))
                throw new UnityException($"Unbound: Texture2D.GetPixelData of a {format} texture is not supported.");
            var offsets = new int[m_MipCount + 1];
            for (int mip = 0; mip < m_MipCount; mip++) offsets[mip + 1] = checked(offsets[mip] + RawMipSize(mip));
            var data = new byte[offsets[^1]];
            for (int mip = 0; mip < m_MipCount; mip++) EncodeLevel(mip).CopyTo(data, offsets[mip]);
            m_Store = data;
            m_StoreOffsets = offsets;
            m_StorePin = GCHandle.Alloc(data, GCHandleType.Pinned);
            m_StoreSafety = AtomicSafetyHandle.Create();
        }

        /// <summary>The cells take the store's texels; the store stays the texture's pixels.</summary>
        void SyncFromStore()
        {
            // Blocks are not cells: the pixels stay what the import's blocks decode to.
            if (m_Store == null || SourceBlocks) return;
            CheckStoreRead();
            var store = m_Store;
            m_Store = null;
            try
            {
                for (int mip = 0; mip < m_MipCount; mip++)
                    DecodeLevel(new ReadOnlySpan<byte>(store, m_StoreOffsets[mip], m_StoreOffsets[mip + 1] - m_StoreOffsets[mip]), mip);
            }
            finally
            {
                m_Store = store;
            }
        }

        /// <summary>Back to the cells: views of the store end.</summary>
        void LeaveStore()
        {
            if (m_Store == null) return;
            TexturePixelStore.Complete(m_StoreSafety);
            SyncFromStore();
            ReleaseStore();
        }

        void ReleaseStore()
        {
            TexturePixelStore.Release(ref m_StoreSafety, ref m_StorePin);
            m_Store = null;
            m_StoreOffsets = null;
        }
    }
}

namespace Unbound.Runtime
{
    internal static class TexturePixelStore
    {
        internal static void Complete(AtomicSafetyHandle safety)
        {
            if (safety.versionNode == IntPtr.Zero) return;
            if (Jobs.JobSystem.IsExecutingJob)
                throw new InvalidOperationException("Texture pixel storage cannot be reclaimed from a job.");
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(safety);
        }

        internal static void Release(ref AtomicSafetyHandle safety, ref GCHandle pin)
        {
            Complete(safety);
            if (safety.versionNode != IntPtr.Zero) AtomicSafetyHandle.Release(safety);
            safety = default;
            if (pin.IsAllocated) pin.Free();
        }
    }
}
