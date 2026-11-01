using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// Profiler.GetRuntimeMemorySizeLong and GetAllocatedMemoryForGraphicsDriver as the Editor
    /// accounts objects a game creates (measured on 6000.0.71f1: tests/projects/appfacts).
    ///
    /// A texture counts a fixed header, its CPU copy and its GPU copy (the copy stays counted
    /// after Apply(makeNoLongerReadable)); the GPU copy of a format the device does not sample is
    /// decompressed to 4 bytes per pixel. A render texture counts its header, then its color and
    /// depth surfaces while created (MSAA surfaces once per sample plus the resolved one). A mesh
    /// counts its vertex and index data, and the same again once uploaded. An AudioClip counts
    /// its float samples, a streamed one a 0.8 s buffer at its rate. The graphics driver figure
    /// is the sum of the GPU copies of live textures, created render textures and uploaded
    /// meshes; the Editor adds what it draws its own windows with, which Unbound has not.
    /// </summary>
    internal static class MemorySizes
    {
        const long Texture2DHeader = 936, Texture2DArrayHeader = 720, Texture3DHeader = 664, CubemapHeader = 992;
        const long RenderTextureHeader = 1024, ReleasedRenderTextureHeader = 544;
        const long MeshHeader = 1448, MeshVertexDataHeader = 16, MeshExtraSubMesh = 56;
        const long AudioClipHeader = 904, AudioClipPerChannel = 32;
        const long GameObjectHeader = 240, GameObjectPerComponent = 24;

        /// <summary>The size, or null for a kind of object whose size was not measured.</summary>
        internal static long? RuntimeSize(UnityEngine.Object o)
        {
            switch (o)
            {
                case Texture2D t: return Texture2DHeader + Both(t.width, t.height, 1, false, t.mipmapCount, t.format, 1);
                case Texture2DArray t: return Texture2DArrayHeader + Both(t.width, t.height, t.depth, false, t.mipmapCount, t.format, 1);
                case Texture3D t: return Texture3DHeader + Both(t.width, t.height, t.depth, true, t.mipmapCount, t.format, 1);
                case Cubemap t: return CubemapHeader + Both(t.width, t.height, 1, false, t.mipmapCount, t.format, 6);
                case RenderTexture rt: return RenderTextureSize(rt);
                case Mesh m: return MeshSize(m);
                case AudioClip clip: return clip.m_IsAsset ? null : AudioClipSize(clip);
                case GameObject go: return GameObjectHeader + GameObjectPerComponent * go.GetComponentCount();
                default: return null;
            }
        }

        static long Both(int w, int h, int d, bool volume, int mips, TextureFormat format, int faces) =>
            Chain(w, h, d, volume, mips, format, gpu: false, faces) + Chain(w, h, d, volume, mips, format, gpu: true, faces);

        /// <summary>A texture's mip chain in <paramref name="format"/>; the GPU copy of a format the
        /// device does not sample is 4 bytes per pixel.</summary>
        static long Chain(int w, int h, int d, bool volume, int mips, TextureFormat format, bool gpu, int faces)
        {
            var graphics = GraphicsFormatUtility.GetGraphicsFormat(format, false);
            if (graphics == GraphicsFormat.None) return 0;
            bool decompressed = gpu && !SystemInfo.IsFormatSupported(graphics, GraphicsFormatUsage.Sample);
            long size = 0;
            for (int i = 0; i < Math.Max(1, mips); i++)
            {
                int mw = Math.Max(1, w >> i), mh = Math.Max(1, h >> i), md = volume ? Math.Max(1, d >> i) : d;
                size += decompressed ? 4L * mw * mh * md : Surface(mw, mh, graphics) * md;
            }
            return size * faces;
        }

        static long Surface(int w, int h, GraphicsFormat format)
        {
            long bw = Math.Max(1u, GraphicsFormatUtility.GetBlockWidth(format));
            long bh = Math.Max(1u, GraphicsFormatUtility.GetBlockHeight(format));
            return (w + bw - 1) / bw * ((h + bh - 1) / bh) * GraphicsFormatUtility.GetBlockSize(format);
        }

        static long RenderTextureSize(RenderTexture rt)
        {
            if (!rt.IsCreated()) return rt.m_EverReleased ? ReleasedRenderTextureHeader : RenderTextureHeader;
            return RenderTextureHeader + RenderTextureSurfaces(rt);
        }

        /// <summary>A created render texture's color (with its mips) and depth surfaces; MSAA ones
        /// once per sample and once resolved.</summary>
        internal static long RenderTextureSurfaces(RenderTexture rt)
        {
            int w = rt.width, h = rt.height;
            int layers = rt.dimension == TextureDimension.Cube ? 6 : Math.Max(1, rt.volumeDepth);
            int samples = Math.Max(1, rt.antiAliasing);
            long copies = samples > 1 ? samples + 1 : 1;
            long color = 0;
            if (rt.graphicsFormat != GraphicsFormat.None)
            {
                int mips = rt.useMipMap ? rt.mipmapCount : 1;
                for (int i = 0; i < Math.Max(1, mips); i++)
                    color += Surface(Math.Max(1, w >> i), Math.Max(1, h >> i), rt.graphicsFormat);
            }
            long depth = (long)w * h * DepthBytes(rt.depthStencilFormat);
            return (color + depth) * layers * copies;
        }

        /// <summary>Depth surfaces as accounted: 16 bits in 2 bytes, 24 bits (with or without
        /// stencil) in 4, 32-bit float with stencil in 8.</summary>
        static long DepthBytes(GraphicsFormat format) => format switch
        {
            GraphicsFormat.None => 0,
            GraphicsFormat.D16_UNorm => 2,
            GraphicsFormat.D16_UNorm_S8_UInt => 4,
            GraphicsFormat.D24_UNorm or GraphicsFormat.D24_UNorm_S8_UInt or GraphicsFormat.D32_SFloat => 4,
            GraphicsFormat.D32_SFloat_S8_UInt => 8,
            GraphicsFormat.S8_UInt => 1,
            _ => 4,
        };

        internal static long MeshVertexBytes(Mesh m)
        {
            int n = m.m_Vertices.Length;
            if (n == 0) return 0;
            long stride = 12;
            if (m.m_Normals != null) stride += 12;
            if (m.m_Tangents != null) stride += 16;
            if (m.m_Colors != null) stride += 4;
            for (int i = 0; i < m.m_UVs.Length; i++)
                if (m.m_UVs[i] != null) stride += 4 * Math.Max(1, m.m_UVDimensions[i]);
            return stride * n;
        }

        internal static long MeshIndexBytes(Mesh m)
        {
            long count = 0;
            foreach (var sub in m.m_SubMeshes) count += sub.Indices.Length;
            return count * (m.indexFormat == IndexFormat.UInt32 ? 4 : 2);
        }

        static long MeshSize(Mesh m)
        {
            long vertices = MeshVertexBytes(m), indices = MeshIndexBytes(m);
            long size = MeshHeader + MeshExtraSubMesh * Math.Max(0, m.m_SubMeshes.Count - 1) + vertices + indices;
            if (vertices > 0) size += MeshVertexDataHeader;
            if (m.m_SyncVersion > 0) size += vertices + indices;
            return size;
        }

        static long AudioClipSize(AudioClip clip)
        {
            int channels = Math.Max(1, clip.channels);
            long header = AudioClipHeader + AudioClipPerChannel * channels;
            long samples = clip.m_Stream ? clip.frequency * 4L / 5 : clip.samples;
            return header + 4L * samples * channels;
        }

        /// <summary>The GPU copies of live textures, created render textures and uploaded meshes.</summary>
        internal static long GraphicsDriver()
        {
            long total = 0;
            foreach (var o in ObjectRegistry.All(typeof(Texture)))
            {
                switch (o)
                {
                    case Texture2D t: total += Chain(t.width, t.height, 1, false, t.mipmapCount, t.format, gpu: true, faces: 1); break;
                    case Texture2DArray t: total += Chain(t.width, t.height, t.depth, false, t.mipmapCount, t.format, gpu: true, faces: 1); break;
                    case Texture3D t: total += Chain(t.width, t.height, t.depth, true, t.mipmapCount, t.format, gpu: true, faces: 1); break;
                    case Cubemap t: total += Chain(t.width, t.height, 1, false, t.mipmapCount, t.format, gpu: true, faces: 6); break;
                    case RenderTexture rt when rt.IsCreated(): total += RenderTextureSurfaces(rt); break;
                }
            }
            foreach (var o in ObjectRegistry.All(typeof(Mesh)))
                if (o is Mesh m && m.m_SyncVersion > 0) total += MeshVertexBytes(m) + MeshIndexBytes(m);
            return total;
        }
    }
}
