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
            // A clone uploads its own retained CPU snapshot, including unapplied edits. No
            // asset GUID is copied: later sampler/pixel writes cannot reload or alias the source.
            copy.EnsureUploaded();
            if (copy.m_NativeHandle == 0)
            {
                Object.DestroyImmediate(copy);
                throw new ArgumentException($"Instantiating texture '{name}' is not supported because its retained pixel data is unavailable.");
            }
            if (m_Pixels != null || m_Hdr != null || m_Raw != null) DropIfNotReadable();
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
