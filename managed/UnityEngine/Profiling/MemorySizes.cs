using System;
using System.Threading;
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
        // The first script-created clip before an audio reset carries this extra memory.
        // Unity 6000.0.71f1, tests/projects/clipstartupmemory.
        const long AudioClipStartupBytes = 88;
        // Created streams: two 400 ms buffers, each frame count truncated independently
        // (tests/projects/clipmemory, Unity 6000.0.71f1).
        const int CreatedStreamBuffers = 2, CreatedStreamBufferMilliseconds = 400;
        const long GameObjectHeader = 240, GameObjectPerComponent = 24;
        // The attached component itself, measured by tests/projects/componentmemory on Unity 6000.0.71f1.
        const long BoxColliderBytes = 288, MeshFilterBytes = 120, MonoBehaviourBytes = 456;
        const long LightBytes = 1176, LightCommandBufferSlotBytes = 12;
        const long AudioSourceBytes = 1128, AudioSourceCurveKeyBytes = 32;
        const long MeshRendererBytes = 656, MeshRendererMaterialSlotBytes = 4, MeshRendererIndexedBlockSlotBytes = 8;
        const long TransformBytes = 296, RectTransformBytes = 368, TransformChildSlotBytes = 16;
        const long PackedHierarchyHeader = 176, PackedHierarchySlotBytes = 104, PackedHierarchyExtraBlockBytes = 16;
        const long HierarchyPairUnits = 5, ExtraBlockUnits = 8;
        // Unity 6000.0.71f1, tests/projects/textassetmemory: byte storage changes past 31 bytes.
        const long TextAssetHeader = 160, TextAssetInlineBytes = 31;
        static readonly object s_AstcFallbackLock = new();
        static uint s_AstcFallbackFormats;

        internal static bool MeasuresAstcFallback(Texture texture, TextureFormat format) =>
            BlockFormats.IsAstc(format) && Compat.AstcFallbackMemoryMeasured
            && GraphicsDeviceProfile.Current.AstcFallbackMemoryOverhead != null
            && !texture.IsExternalTexture
            && !SystemInfo.IsFormatSupported(texture.GraphicsFormatOf(), GraphicsFormatUsage.Sample);

        /// <summary>Reserve the software ASTC workspace for a new format. A 2D or cube texture
        /// includes the setup bytes in its own total, while an array reports its own storage.
        /// The format stays initialized after its first texture is destroyed.</summary>
        internal static void RecordAstcFallback(Texture texture, TextureFormat format)
        {
            if (!MeasuresAstcFallback(texture, format)
                || GraphicsDeviceProfile.Current.AstcFallbackMemoryOverhead is not { } overhead)
                return;
            // Both ASTC families fit in this span of TextureFormat values. The linear and sRGB
            // graphics formats share a bit, while LDR and HDR have separate bits.
            uint bit = 1u << ((int)format - (int)TextureFormat.ASTC_4x4);
            if ((Volatile.Read(ref s_AstcFallbackFormats) & bit) != 0) return;
            lock (s_AstcFallbackLock)
            {
                uint formats = s_AstcFallbackFormats;
                if ((formats & bit) != 0) return;
                if (texture is not Texture2DArray)
                    texture.m_AstcFallbackBytes += formats == 0 ? overhead.First : overhead.Additional;
                Volatile.Write(ref s_AstcFallbackFormats, formats | bit);
            }
        }

        static long AstcFallbackMemory(Texture texture) =>
            Compat.AstcFallbackMemoryMeasured ? texture.m_AstcFallbackBytes : 0;

        /// <summary>The size, or null for a kind of object whose size was not measured.</summary>
        internal static long? RuntimeSize(UnityEngine.Object o)
        {
            switch (o)
            {
                case Texture2D t: return Texture2DSize(t) + AstcFallbackMemory(t);
                case Texture2DArray t: return Texture2DArrayHeader + Both(t.width, t.height, t.depth, false, t.mipmapCount, t.format, 1, !t.m_AstcMemoryPendingUpload);
                case Texture3D t: return Texture3DHeader + Both(t.width, t.height, t.depth, true, t.mipmapCount, t.format, 1);
                case Cubemap t: return CubemapHeader + Both(t.width, t.height, 1, false, t.mipmapCount, t.format, 6, !t.m_AstcMemoryPendingUpload)
                    + AstcFallbackMemory(t);
                case RenderTexture rt: return RenderTextureSize(rt);
                case Mesh m: return MeshSize(m);
                case AudioClip clip: return clip.m_IsAsset ? null : AudioClipSize(clip);
                case TextAsset text when Compat.TextAssetMemoryMeasured: return TextAssetSize(text);
                case GameObject go: return GameObjectHeader + GameObjectPerComponent * go.ProfilerComponentSlots;
                case Component component when !component.IsPersistentAsset
                    && component.m_GameObject is { IsPersistentAsset: false, m_IsPrefabAsset: false } owner && owner != null:
                    return component switch
                    {
                        BoxCollider when Compat.FixedComponentMemoryMeasured => BoxColliderBytes,
                        MeshFilter when Compat.FixedComponentMemoryMeasured => MeshFilterBytes,
                        Transform transform when transform.MeasuresHierarchyStorage => TransformSize(transform),
                        MonoBehaviour when Compat.FixedComponentMemoryMeasured => MonoBehaviourBytes,
                        Light light when Compat.LightCommandBufferMemoryMeasured && GraphicsSettings.currentRenderPipeline == null
                            => LightBytes + light.ProfilerCommandBufferSlots * LightCommandBufferSlotBytes,
                        AudioSource source when Compat.AudioSourceCurveStorageMeasured
                            => AudioSourceBytes + source.ProfilerReservedCurveKeys * AudioSourceCurveKeyBytes,
                        MeshRenderer renderer when Compat.MeshRendererMaterialMemoryMeasured
                            => MeshRendererBytes + renderer.ProfilerMaterialSlots * MeshRendererMaterialSlotBytes
                                + renderer.ProfilerIndexedBlockSlots * MeshRendererIndexedBlockSlotBytes,
                        _ => null,
                    };
                default: return null;
            }
        }

        static long TransformSize(Transform transform)
        {
            long bytes = transform is RectTransform ? RectTransformBytes : TransformBytes;
            bytes += transform.ProfilerReservedChildSlots * TransformChildSlotBytes;
            long capacity = transform.ProfilerHierarchyCapacity;
            if (capacity == 0) return bytes;
            // The measured packed allocation adds one extra block per completed group of
            // eight units strictly below the five units reserved for each pair of slots.
            long pairedSlots = (capacity + 1) / 2;
            long blocks = (HierarchyPairUnits * pairedSlots - 1) / ExtraBlockUnits;
            return bytes + PackedHierarchyHeader + PackedHierarchySlotBytes * capacity + PackedHierarchyExtraBlockBytes * blocks;
        }

        static long Texture2DSize(Texture2D texture)
        {
            if (!Compat.TextureResizeMemoryMeasured || texture.IsPersistentAsset || texture.IsExternalTexture || texture.width <= 0
                || GraphicsDeviceProfile.Current.Texture2DMemoryOverhead is not { } overhead)
                return Texture2DHeader + Both(texture.width, texture.height, 1, false, texture.mipmapCount, texture.format, 1);

            if (texture.m_ResizeMemoryPendingApply || texture.m_AstcMemoryPendingUpload)
            {
                int header = texture.m_ResizeMemoryPendingApply ? overhead.CpuOnly : overhead.WithGpu;
                return header + Chain(texture.width, texture.height, 1, false, texture.mipmapCount, texture.format, gpu: false, faces: 1);
            }
            long decompressedBytesPerPixel =
                texture.m_ScriptTextureStorage && BlockFormats.IsAstcHdr(texture.format) && MeasuresAstcFallback(texture, texture.format)
                    ? 8 : 4;
            return overhead.WithGpu + Both(texture.width, texture.height, 1, false, texture.mipmapCount, texture.format, 1,
                decompressedBytesPerPixel: decompressedBytesPerPixel);
        }

        static long TextAssetSize(TextAsset text)
        {
            long bytes = text.dataSize;
            return TextAssetHeader + (bytes > TextAssetInlineBytes ? bytes + 1 : 0);
        }

        static long Both(int w, int h, int d, bool volume, int mips, TextureFormat format, int faces, bool hasGpu = true,
            long decompressedBytesPerPixel = 4) =>
            Chain(w, h, d, volume, mips, format, gpu: false, faces)
            + (hasGpu ? Chain(w, h, d, volume, mips, format, gpu: true, faces, decompressedBytesPerPixel) : 0);

        /// <summary>A texture's mip chain in <paramref name="format"/>; the GPU copy of a format the
        /// device does not sample uses the caller's decompressed texel size.</summary>
        static long Chain(int w, int h, int d, bool volume, int mips, TextureFormat format, bool gpu, int faces,
            long decompressedBytesPerPixel = 4)
        {
            var graphics = GraphicsFormatUtility.GetGraphicsFormat(format, false);
            if (graphics == GraphicsFormat.None) return 0;
            bool decompressed = gpu && !SystemInfo.IsFormatSupported(graphics, GraphicsFormatUsage.Sample);
            long size = 0;
            for (int i = 0; i < Math.Max(1, mips); i++)
            {
                int mw = Math.Max(1, w >> i), mh = Math.Max(1, h >> i), md = volume ? Math.Max(1, d >> i) : d;
                size += decompressed ? decompressedBytesPerPixel * mw * mh * md : Surface(mw, mh, graphics) * md;
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
            int n = m.vertexCount;
            if (n == 0) return 0;
            long stride = 12;
            if (m.HasVertexAttribute(VertexAttribute.Normal)) stride += 12;
            if (m.HasVertexAttribute(VertexAttribute.Tangent)) stride += 16;
            if (m.HasVertexAttribute(VertexAttribute.Color)) stride += 4;
            for (int i = 0; i < m.m_UVs.Length; i++)
                if (m.HasVertexAttribute(VertexAttribute.TexCoord0 + i)) stride += 4 * Math.Max(1, m.m_UVDimensions[i]);
            return stride * n;
        }

        internal static long MeshIndexBytes(Mesh m)
        {
            long count = 0;
            foreach (var sub in m.m_SubMeshes) count += sub.IndexCount;
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
            if (Compat.CreatedClipStartupMemoryMeasured && clip.m_OwnsStartupMemory) header += AudioClipStartupBytes;
            bool measured = Compat.CreatedClipStreamMemoryMeasured;
            bool stream = clip.m_Stream || (measured && clip.m_CreateStreamRequested);
            long streamFrames = measured
                ? CreatedStreamBuffers * (clip.frequency * (long)CreatedStreamBufferMilliseconds / 1000)
                : clip.frequency * 4L / 5;
            long samples = stream ? streamFrames : clip.samples;
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
