using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Texture2D : Instantiation.ICloneable
    {
        Object Instantiation.ICloneable.CloneObject()
        {
            EnsurePixels();
            if (m_Pixels == null && m_Hdr == null && m_AssetGuid != null)
                FetchImportedPixels();

            var copy = new Texture2D
            {
                m_Width = m_Width,
                m_Height = m_Height,
                m_Format = m_Format,
                m_MipCount = m_MipCount,
                m_GpuMipCount = m_GpuMipCount,
                m_IsReadable = m_IsReadable,
                m_Linear = m_Linear,
                m_SourceFormat = m_SourceFormat,
                m_SourceMips = m_SourceMips,
                m_SourceRaw = Copy(m_SourceRaw),
                m_Pixels = Copy(m_Pixels),
                m_Hdr = Copy(m_Hdr),
                m_MipPixels = CopyLevels(m_MipPixels),
                m_MipHdr = CopyLevels(m_MipHdr),
                m_Raw = Copy(m_Raw ?? m_UnreadableRaw),
                m_UnreadableRaw = Copy(m_UnreadableRaw),
                alphaIsTransparency = alphaIsTransparency,
                ignoreMipmapLimit = ignoreMipmapLimit,
                requestedMipmapLevel = requestedMipmapLevel,
                minimumMipmapLevel = minimumMipmapLevel,
                m_PixelsDirty = true,
            };
            copy.SetSamplerQuietly(filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel, mipMapBias);
            // A readable clone uploads its own CPU snapshot, including unapplied edits. No
            // asset GUID is copied: later sampler/pixel writes cannot reload or alias the source.
            copy.EnsureUploaded();
            if (!m_IsReadable && m_NativeHandle != 0)
            {
                // Non-readable textures can have GPU-only writes. Preserve their actual
                // storage instead of reconstructing it from an older retained CPU snapshot.
                if (copy.m_NativeHandle == 0)
                {
                    copy.m_NativeHandle = RenderHandles.Next();
                    copy.UploadSampler();
                }
                for (int mip = 0; mip < m_GpuMipCount; mip++)
                    GfxStream.CopyTexture(m_NativeHandle, 0, mip, new RectInt(-1, -1, -1, -1), copy.m_NativeHandle, 0, mip, 0, 0);
            }
            DropIfNotReadable();
            return copy;
        }

        static T[] Copy<T>(T[] source) => source == null ? null : (T[])source.Clone();

        static T[][] CopyLevels<T>(T[][] source)
        {
            if (source == null) return null;
            var copy = new T[source.Length][];
            for (int mip = 0; mip < source.Length; mip++) copy[mip] = Copy(source[mip]);
            return copy;
        }
    }
}
