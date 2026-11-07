using System;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Cubemap : Instantiation.ICloneable
    {
        /// <summary>The CPU copy an unreadable cubemap retains (made unreadable by Apply, or
        /// imported without Read/Write and read from the importer on its first clone): scripts
        /// cannot read it, clones copy it, as Texture2D's retained storage.</summary>
        internal Color[][][] m_UnreadablePixels;

        /// <summary>
        /// A clone copies the size, format, color space, mip count, readability and sampler, and
        /// uploads its own copy of every face's mip chain from the retained CPU data, including
        /// unapplied edits (as Texture2D clones do). An unreadable source still clones, into an
        /// unreadable copy. A cubemap held only on the GPU (an AssetBundle's or a built-in one)
        /// has no retained data and is rejected, as such textures are.
        /// </summary>
        Object Instantiation.ICloneable.CloneObject()
        {
            if (!m_Readable && Compat.AssetCloneResourcesMeasured)
                Debug.LogError($"Instantiating a non-readable '{name}' texture is not allowed! Please mark the texture readable in the inspector or don't instantiate it.");
            var faces = RetainedFaces();
            if (faces == null)
                throw new ArgumentException($"Instantiating cubemap '{name}' is not supported because its retained pixel data is unavailable.");
            var copy = new Cubemap
            {
                m_Width = m_Width,
                m_Height = m_Height,
                m_Format = m_Format,
                m_MipCount = m_MipCount,
                m_Readable = m_Readable,
                m_Linear = m_Linear,
                m_Rgbm = m_Rgbm,
                m_Pixels = faces,
                requestedMipmapLevel = requestedMipmapLevel,
                loadAllMips = loadAllMips,
            };
            copy.SetSamplerQuietly(filterMode, wrapModeU, wrapModeV, wrapModeW, anisoLevel, mipMapBias);
            copy.Upload();
            if (!copy.m_Readable)
            {
                copy.m_UnreadablePixels = copy.m_Pixels;
                copy.m_Pixels = null;
            }
            return copy;
        }

        /// <summary>A copy of every face's mip chain from the CPU data, or null when there is none.</summary>
        Color[][][] RetainedFaces()
        {
            if (m_Readable)
            {
                CheckStoreRead();
                if (m_Pixels == null && m_AssetGuid != null) FetchImported();
                if (m_Pixels == null) return null;
                var faces = new Color[6][][];
                for (int f = 0; f < 6; f++)
                {
                    faces[f] = new Color[m_MipCount][];
                    // Level is current with a GetPixelData store; the store itself is not copied.
                    for (int m = 0; m < m_MipCount; m++) faces[f][m] = (Color[])Level((CubemapFace)f, m).Clone();
                }
                return faces;
            }
            if (m_UnreadablePixels == null && m_AssetGuid != null)
            {
                FetchImported();
                m_UnreadablePixels = m_Pixels;
                m_Pixels = null;
            }
            if (m_UnreadablePixels == null) return null;
            var copy = new Color[6][][];
            for (int f = 0; f < 6; f++)
            {
                copy[f] = new Color[m_MipCount][];
                for (int m = 0; m < m_MipCount; m++) copy[f][m] = (Color[])m_UnreadablePixels[f][m].Clone();
            }
            return copy;
        }
    }
}
