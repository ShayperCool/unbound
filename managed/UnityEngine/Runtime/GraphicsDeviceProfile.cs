using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    internal enum Alpha8ReadbackMode { Unmeasured, Unsupported, Red }

    /// <summary>
    /// The run's graphics device and what the Editor answers on it, as the native side chose it
    /// once at startup (unbound_compat::graphics: `--force-vulkan`/`--force-glcore`, the project's
    /// graphics API list, the Editor's default). Every device-dependent answer reads the selected
    /// profile; nothing compares device types elsewhere. Answers are the Unity 6000.0.71f1 Linux
    /// Editor's on an NVIDIA RTX 4060 (tests/projects/urp CapsProbe, `caps-6000.0.71f1.jsonl`
    /// for OpenGL Core and `unity-6000.0.71f1/caps-vulkan.jsonl` for Vulkan) and the macOS
    /// Editor's on an Apple M2 Pro (`unity-6000.0.71f1/caps-metal.jsonl`, `device-metal.jsonl`
    /// for Metal); where the devices answer alike the answer stays in SystemInfo (tested against
    /// all three: GraphicsDeviceCapsTests). Not followed: the Vulkan Editor's GPU recorder, the
    /// Vulkan and Metal Editors' foveation support and the Metal Editor's HDR display (Unbound's
    /// renderer has none of them; they answer as on OpenGL Core).
    /// </summary>
    internal sealed class GraphicsDeviceProfile
    {
        internal readonly GraphicsDeviceType Type;
        /// <summary>UNITY_UV_STARTS_AT_TOP: render textures are drawn with the projection's y
        /// flipped (GL.GetGPUProjectionMatrix with renderIntoTexture).</summary>
        internal readonly bool UVStartsAtTop;
        /// <summary>UNITY_REVERSED_Z: depth 1 is near.</summary>
        internal readonly bool ReversedZ;
        internal readonly bool UsesLoadStoreActions;
        internal readonly bool FramebufferReadback;
        /// <summary>Alpha8 readback uses readable red storage on Vulkan
        /// (6000.0.71f1 NVIDIA, tests/projects/nativegfx readbackValidation.alpha.*).</summary>
        internal readonly Alpha8ReadbackMode Alpha8Readback;
        static readonly System.Collections.Generic.HashSet<GraphicsFormat> s_NamedFormats =
            new System.Collections.Generic.HashSet<GraphicsFormat>(System.Enum.GetValues<GraphicsFormat>());

        // Named formats have captured capability rows. Additional values reported by texture
        // metadata need their own controls before they can introduce source diagnostics.
        internal bool ReadPixelsSupportIsMeasured(GraphicsFormat format) => s_NamedFormats.Contains(format)
            || (format == GraphicsFormatUtility.Texture2DAlpha8 && Alpha8Readback != Alpha8ReadbackMode.Unmeasured);
        internal readonly bool SupportsMultisampleResolveStencil;
        internal readonly bool SupportsMultisampleAutoResolve;
        internal readonly int ConstantBufferOffsetAlignment;
        internal readonly RenderingThreadingMode ThreadingMode;
        /// <summary>SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil), the depth of render
        /// texture descriptors made with a depth bit count.</summary>
        internal readonly GraphicsFormat DefaultDepthStencil;
        internal readonly BatchBufferTarget BatchBufferTarget;
        /// <summary>The device's format table (GraphicsCaps.Generated.cs).</summary>
        internal readonly GraphicsCapsTable.Device Caps;
        readonly System.Lazy<MachineIdentity.Gpu> m_Identity;

        /// <summary>SystemInfo.GetGraphicsFormat(DefaultFormat.LDR) and the Default render
        /// texture format are B8G8R8A8 (Metal), else R8G8B8A8.</summary>
        internal bool BgraLdr { get; init; }
        /// <summary>Texture2D allocation overhead before and after Apply, measured by textureresizememory.</summary>
        internal (int CpuOnly, int WithGpu)? Texture2DMemoryOverhead { get; init; }
        /// <summary>What a BatchRendererGroup procedural command of MeshTopology.Quads draws its
        /// vertex ids as, the device having no quad primitive: a triangle list on Vulkan, points on
        /// OpenGL Core (measured, Unity 6000.0.71f1 on the NVIDIA driver: tests/projects/brg
        /// <c>procedural.c8</c>); null where unmeasured (such commands are warned about and not
        /// drawn).</summary>
        internal UnityEngine.MeshTopology? ProceduralQuads { get; init; }
        /// <summary>The most samples GetRenderTextureSupportedMSAASampleCount answers.</summary>
        internal int MaxMsaaSamples { get; init; } = 8;
        internal bool DepthFetchInRenderPass { get; init; } = true;
        internal int MaxTexture3DSize { get; init; } = 16384;
        /// <summary>SystemInfo.maxTextureSize and maxCubemapSize: 32768 on the Linux Editor's OpenGL
        /// Core and Vulkan devices (NVIDIA RTX 4060), 16384 on the Metal one (tests/projects/urp
        /// CapsProbe `device-*.jsonl`); the renderer requests the adapter's own limits.</summary>
        internal int MaxTextureSize { get; init; } = 32768;
        internal int MaxComputeWorkGroupSizeZ { get; init; } = 64;
        /// <summary>The target a readable mesh's buffers gain once a visual effect reads the mesh
        /// (its vertex buffer once it is an effect's value, its index buffer once an effect's
        /// programs read it): Structured on OpenGL Core (Unity 6000.0.71f1, tests/projects/vfx
        /// <c>mesh operations/*/after</c>); not measured on Vulkan and Metal, which answer the
        /// same.</summary>
        internal GraphicsBuffer.Target VfxMeshBufferTarget { get; init; } = GraphicsBuffer.Target.Structured;
        /// <summary>What the Editor's device answers for geometry shaders and conservative
        /// rasterization; Unbound's renderer has neither and answers false (warned where the
        /// Editor answers true).</summary>
        internal bool EditorGeometryShaders { get; init; } = true;
        internal bool EditorConservativeRaster { get; init; } = true;
        internal bool HiddenSurfaceRemovalOnGPU { get; init; }
        /// <summary>The Editor's device scales buffers dynamically (Metal and Vulkan: SystemInfo
        /// answers true, a camera keeps allowDynamicResolution, ScalableBufferManager.ResizeBuffers
        /// sets quantized factors and dynamically scaled render textures take the scaled size;
        /// tests/projects/nativegfx dynres.*, tests/projects/corecaps dynres.*). Unbound scales
        /// both dimensions by their respective factors.</summary>
        internal bool EditorDynamicResolution { get; init; }
        /// <summary>A partial screen camera's HDR intermediate, where it keeps the color under it
        /// (Depth only, Don't Clear), starts from the screen's stored bytes read as linear values,
        /// so in Linear color space that color shows encoded twice after the resolve (Metal: the
        /// macOS Editor's Game view, tests/projects/camerastack `metal/Linear` *.part.bH*, two
        /// captures on an Apple M2 Pro; its LDR and full-screen intermediates and every Gamma case
        /// start from the decoded colors, as on the Linux devices). Unmeasured for explicit
        /// targets and for a partial deferred camera without HDR (its light buffer is half float
        /// in Unbound, so it takes this seed too).</summary>
        internal bool PartialScreenSeedUndecoded { get; init; }
        /// <summary>A 5-6-5 or 1-5-5-5 render target keeps what is drawn into it (or cleared, here
        /// as drawn) at its channels' bits: a program sampling it reads each 8-bit value with its
        /// low bits dropped and the kept ones repeated below them (0.5 reads 125/255 in a 6-bit
        /// channel, 0.2 48/255), no alpha as 1 (5-6-5) and alpha as one bit (1-5-5-5); a blended
        /// draw reads them so too. A 4444 target keeps 8-bit values. Measured on the Editor's
        /// OpenGL Core and Vulkan devices (tests/projects/samplerstates Packed.*, NVIDIA; Vulkan's
        /// clear rounds where Unbound truncates). The Metal Editor keeps them by another rule, the
        /// renderer's (`GraphicsDevice::packed_writes_round`): each channel's nearest level, read as
        /// exactly level / (2^bits - 1), which Unbound's 8-bit store keeps as its nearest byte
        /// (tests/projects/samplerstates metal.jsonl Packed.*, residuals-metal.json).</summary>
        internal bool PackedTargetsKeepChannelBits { get; init; }
        /// <summary>ComputeBuffer/GraphicsBuffer.SetData of an Append or Counter buffer sets its
        /// hidden counter to 0 (Metal: a counter appended to 14, then SetData, copies 0,
        /// tests/projects/gpubuffers metal `append.cb.setdata.count`; the OpenGL Core Editor keeps
        /// 14). Measured for a whole ComputeBuffer.SetData; Unbound applies it to every SetData
        /// of the buffer (CommandBuffer.SetBufferData unmeasured, kept).</summary>
        internal bool SetDataResetsCounter { get; init; }
        /// <summary>An indirect dispatch at a byte offset that is not a multiple of 4 reads its
        /// arguments at the offset rounded down to one (Metal: offsets 2 and 14 of
        /// { 2, 1, 1, 3, 2, 2 } dispatch 2 and 12 groups, tests/projects/cmdbuffers metal.jsonl
        /// `metal.dispatch.offset*`, gpubuffers metal `indirect.unaligned`); elsewhere it
        /// dispatches nothing (OpenGL Core, gpubuffers `indirect.unaligned`; Vulkan unmeasured).</summary>
        internal bool IndirectOffsetRoundsDown { get; init; }
        /// <summary>The device capability keywords (`UNITY_DEVICE_SUPPORTS_*`) the device enables
        /// globally from the start: the macOS Editor's Metal device the native 16-bit, 32-wide and
        /// any-size wave ones (tests/projects/shaderkeywords metal.jsonl `global.enabled.initial`,
        /// `.after`, `.restored`); none on OpenGL Core (the Linux reference), Vulkan unmeasured.</summary>
        internal string[] CapabilityKeywords { get; init; } = System.Array.Empty<string>();
        /// <summary>ComputeBuffer.CopyCount into a destination too small at the offset asserts
        /// `dstOffset + size &lt;= m_size` (OpenGL Core); the Metal Editor copies nothing there
        /// silently (tests/projects/gpubuffers metal `append.cb.copybeyond`).</summary>
        internal bool CopyCountAssertsRange { get; init; } = true;
        /// <summary>A 4444 target keeps its channels' 4 bits too (Metal: 0.25 drawn reads 4/15,
        /// tests/projects/samplerstates metal.jsonl Packed.ARGB4444.*); OpenGL Core and Vulkan
        /// keep 8-bit values there. The same mechanism as the renderer's rounding
        /// (`GraphicsDevice::packed_writes_round`): the device stores the packed formats
        /// themselves; both are true on Metal only and change together.</summary>
        internal bool Packs4444Targets { get; init; }
        /// <summary>SystemInfo.supportsTextureWrapMirrorOnce: 1 where the Editor's device answers
        /// it, 0 on Metal.</summary>
        internal int TextureWrapMirrorOnce { get; init; } = 1;
        /// <summary>The compressed formats the device refuses for a Texture3D, by the name its
        /// error gives the format (Metal: PVRTC, ETC and EAC, tests/projects/nativegfx
        /// `metal.jsonl` `volume.make.*`, two macOS Editor captures on an Apple M2 Pro; the names
        /// are those of the linear formats the TextureFormat constructors make, a GraphicsFormat
        /// constructor's sRGB format is unmeasured). Null: none refused (unmeasured on OpenGL Core
        /// and Vulkan).</summary>
        internal System.Collections.Generic.IReadOnlyDictionary<TextureFormat, string> RefusedVolumeFormats { get; init; }
        /// <summary>SystemInfo.supportsCompressed3DTextures where it is measured (Metal: true,
        /// tests/projects/nativegfx `metal.jsonl` volume.compressed3d); null elsewhere (a stub).</summary>
        internal bool? Compressed3DTextures { get; init; }

        /// <summary>SystemInfo.supportsDynamicResolution is measured on the device: true where
        /// <see cref="EditorDynamicResolution"/> (Metal, Vulkan), else false (OpenGL Core:
        /// tests/projects/urp `device-opengl.jsonl`, tests/projects/corecaps).</summary>
        internal bool DynamicResolutionMeasured { get; init; }

        /// <summary>How Texture2D.ReadPixels and AsyncGPUReadback treat a render texture whose format
        /// answers no ReadPixels usage. Null: they refuse it (Vulkan's ReadPixels and every device's
        /// AsyncGPUReadback, tests/projects/textureformats). Metal's rule:
        /// <see cref="TargetReadbackRule"/>.</summary>
        internal TargetReadbackRule TargetReadback { get; init; }

        /// <summary>A device's own rule for reading back targets whose format answers no ReadPixels
        /// usage (Metal: tests/projects/textureformats `metal.jsonl` readback.*, two macOS Editor
        /// captures on an Apple M2 Pro). Formats the rule does not name are unmeasured: refused,
        /// with Unbound's message.</summary>
        internal sealed class TargetReadbackRule
        {
            /// <summary>Formats whose targets ReadPixels reads (converting their texels).</summary>
            internal System.Collections.Generic.HashSet<GraphicsFormat> Converted { get; init; }
            /// <summary>Formats whose targets ReadPixels refuses, by the id its assertion gives: the
            /// format is not supported for image operations (asserted twice; the texels stay).
            /// B5G5R5A1 asserts R5G5B5A1's id.</summary>
            internal System.Collections.Generic.Dictionary<GraphicsFormat, int> NoImageOperations { get; init; }
            /// <summary>AsyncGPUReadback reads a target whose format answers GetPixels usage and
            /// refuses the others, naming the format as these names do.</summary>
            internal System.Collections.Generic.Dictionary<GraphicsFormat, string> RefusedSourceNames { get; init; }
            /// <summary>Packed formats whose targets AsyncGPUReadback reads in their own format as
            /// the packed words (ReadbackFormat.Packed has each layout); null or absent: such a
            /// request fails (unmeasured).</summary>
            internal System.Collections.Generic.HashSet<GraphicsFormat> PackedWords { get; init; }
            /// <summary>ReadPixels of a signed normalized target into 8-bit texels truncates its
            /// values to bytes (Texture2D.ReadPixels; measured for B8G8R8A8_SNorm, taken alike for
            /// the other signed formats); false: unmeasured, they round.</summary>
            internal bool SignedToBytesTruncates { get; init; }
            /// <summary>Reads of a 4444 or 5-6-5 target keep the format's channel bits and reads of
            /// an R10G10B10_XR target give its codes (RenderTexture.QuantizeReadTexels); false:
            /// unmeasured, Unbound's stored values are read.</summary>
            internal bool ReadsStoredChannelBits { get; init; }
        }

        /// <summary>SystemInfo.maxTiledPixelStorageSize (Metal: 64, tests/projects/urp
        /// `device-metal.jsonl`; OpenGL Core: int.MaxValue, tests/projects/srpmembers tiled.max;
        /// Vulkan: 32, `device-vulkan.jsonl`). On OpenGL Core and Vulkan
        /// GetTiledRenderTargetStorageSize is measured too (<see cref="TiledStorageMeasured"/>;
        /// tests/projects/srpmembers tiled.*, tests/projects/corecaps tiled.*).</summary>
        internal int? MaxTiledPixelStorage { get; init; }
        internal bool TiledStorageMeasured { get; init; }

        /// <summary>SystemInfo.maxGraphicsBufferSize where fixed per device (OpenGL Core and
        /// Vulkan: 2 GiB = 2147483648, tests/projects/corecaps bufferLimit.*); null on Metal
        /// where it comes from the device's maxBufferLength.</summary>
        internal long? MaxGraphicsBufferBytes { get; init; }

        /// <summary>SystemInfo answers measured on the device's Editor alone; null where they are
        /// not measured (the members stay stubs there).</summary>
        internal MeasuredLimits Limits { get; init; }

        internal sealed class MeasuredLimits
        {
            /// <summary>maxComputeBufferInputs of the vertex, fragment, hull and domain stages, of
            /// the geometry stage and of compute shaders.</summary>
            internal int ComputeBufferInputs, GeometryComputeBufferInputs, ComputeStageBufferInputs;
            internal bool MipStreaming, ParallelPSOCreation;
        }

        /// <summary>The render texture formats SystemInfo.SupportsRandomWriteOnRenderTextureFormat
        /// answers true for: OpenGL Core's (tests/projects/urp CapsProbe; the Vulkan Editor also
        /// answers BGRA32, whose storage Unbound's renderer does not request of the device),
        /// Metal's own list.</summary>
        internal RenderTextureFormat[] RandomWriteFormats { get; init; } = s_RandomWrite;

        static readonly RenderTextureFormat[] s_RandomWrite =
        {
            RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf, RenderTextureFormat.Default, RenderTextureFormat.ARGB2101010,
            RenderTextureFormat.DefaultHDR, RenderTextureFormat.ARGB64, RenderTextureFormat.ARGBFloat, RenderTextureFormat.RGFloat,
            RenderTextureFormat.RGHalf, RenderTextureFormat.RFloat, RenderTextureFormat.RHalf, RenderTextureFormat.R8,
            RenderTextureFormat.ARGBInt, RenderTextureFormat.RGInt, RenderTextureFormat.RInt, RenderTextureFormat.RGB111110Float,
            RenderTextureFormat.RG32, RenderTextureFormat.RGBAUShort, RenderTextureFormat.RG16, RenderTextureFormat.R16,
        };

        /// <summary>The LDR format (DefaultFormat.LDR, the Default render texture format) in
        /// the color space's form.</summary>
        internal GraphicsFormat Ldr(bool srgb) => BgraLdr
            ? (srgb ? GraphicsFormat.B8G8R8A8_SRGB : GraphicsFormat.B8G8R8A8_UNorm)
            : (srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm);

        /// <summary>The GPU as the device names it (read once, when first asked).</summary>
        internal MachineIdentity.Gpu Identity => m_Identity.Value;

        GraphicsDeviceProfile(GraphicsDeviceType type, bool uvStartsAtTop, bool reversedZ, bool loadStore,
            bool resolveStencil, bool autoResolve, int cbufferAlignment, RenderingThreadingMode threading,
            GraphicsFormat depthStencil, BatchBufferTarget batchTarget, GraphicsCapsTable.Device caps,
            System.Func<MachineIdentity.Gpu> identity, bool framebufferReadback, Alpha8ReadbackMode alpha8Readback = Alpha8ReadbackMode.Unmeasured)
        {
            Type = type;
            FramebufferReadback = framebufferReadback;
            Alpha8Readback = alpha8Readback;
            UVStartsAtTop = uvStartsAtTop;
            ReversedZ = reversedZ;
            UsesLoadStoreActions = loadStore;
            SupportsMultisampleResolveStencil = resolveStencil;
            SupportsMultisampleAutoResolve = autoResolve;
            ConstantBufferOffsetAlignment = cbufferAlignment;
            ThreadingMode = threading;
            DefaultDepthStencil = depthStencil;
            BatchBufferTarget = batchTarget;
            Caps = caps;
            m_Identity = new System.Lazy<MachineIdentity.Gpu>(identity);
        }

        internal static readonly GraphicsDeviceProfile OpenGLCore = new GraphicsDeviceProfile(
            GraphicsDeviceType.OpenGLCore, uvStartsAtTop: false, reversedZ: false, loadStore: false,
            resolveStencil: false, autoResolve: false, cbufferAlignment: 256,
            RenderingThreadingMode.Direct, GraphicsFormat.D24_UNorm_S8_UInt,
            BatchBufferTarget.ConstantBuffer, GraphicsCapsTable.OpenGLCore, MachineIdentity.ReadOpenGl, framebufferReadback: true, alpha8Readback: Alpha8ReadbackMode.Unsupported)
        {
            MaxTiledPixelStorage = int.MaxValue,
            TiledStorageMeasured = true,
            MaxGraphicsBufferBytes = 2L << 30,
            ProceduralQuads = UnityEngine.MeshTopology.Points,
            PackedTargetsKeepChannelBits = true,
            // tests/projects/urp CapsProbe `device-opengl.jsonl` (two identical captures, NVIDIA
            // RTX 4060).
            Compressed3DTextures = false,
            DynamicResolutionMeasured = true,
            Limits = new MeasuredLimits
            {
                ComputeBufferInputs = 16,
                GeometryComputeBufferInputs = 16,
                ComputeStageBufferInputs = 16,
                MipStreaming = true,
                ParallelPSOCreation = false,
            },
        };

        internal static readonly GraphicsDeviceProfile Vulkan = new GraphicsDeviceProfile(
            GraphicsDeviceType.Vulkan, uvStartsAtTop: true, reversedZ: true, loadStore: true,
            resolveStencil: true, autoResolve: true, cbufferAlignment: 64,
            RenderingThreadingMode.SingleThreaded, GraphicsFormat.D32_SFloat_S8_UInt,
            BatchBufferTarget.RawBuffer, GraphicsCapsTable.Vulkan, VulkanIdentity.Read, framebufferReadback: false, alpha8Readback: Alpha8ReadbackMode.Red)
        {
            ProceduralQuads = UnityEngine.MeshTopology.Triangles,
            PackedTargetsKeepChannelBits = true,
            // tests/projects/urp CapsProbe `device-vulkan.jsonl` (two identical captures, NVIDIA
            // RTX 4060): the driver's per-stage storage buffer limit for the graphics stages, 32
            // for compute shaders.
            Compressed3DTextures = true,
            EditorDynamicResolution = true,
            DynamicResolutionMeasured = true,
            MaxTiledPixelStorage = 32,
            TiledStorageMeasured = true,
            MaxGraphicsBufferBytes = 2L << 30,
            Limits = new MeasuredLimits
            {
                ComputeBufferInputs = 1048576,
                GeometryComputeBufferInputs = 1048576,
                ComputeStageBufferInputs = 32,
                MipStreaming = true,
                ParallelPSOCreation = true,
            },
        };

        /// <summary>The macOS Editor's device (tests/projects/urp CapsProbe
        /// `unity-6000.0.71f1/caps-metal.jsonl`, arm64, Apple M2 Pro).</summary>
        internal static readonly GraphicsDeviceProfile Metal = new GraphicsDeviceProfile(
            GraphicsDeviceType.Metal, uvStartsAtTop: true, reversedZ: true, loadStore: true,
            resolveStencil: true, autoResolve: true, cbufferAlignment: 256,
            RenderingThreadingMode.SingleThreaded, GraphicsFormat.D32_SFloat_S8_UInt,
            BatchBufferTarget.RawBuffer, GraphicsCapsTable.Metal, MetalIdentity.Read, framebufferReadback: false)
        {
            BgraLdr = true,
            Texture2DMemoryOverhead = (960, 960),
            MaxMsaaSamples = 4,
            DepthFetchInRenderPass = false,
            MaxTexture3DSize = 2048,
            MaxTextureSize = 16384,
            MaxComputeWorkGroupSizeZ = 1024,
            EditorGeometryShaders = false,
            EditorConservativeRaster = false,
            HiddenSurfaceRemovalOnGPU = true,
            PartialScreenSeedUndecoded = true,
            PackedTargetsKeepChannelBits = true,
            Packs4444Targets = true,
            SetDataResetsCounter = true,
            IndirectOffsetRoundsDown = true,
            CapabilityKeywords = new[] { "UNITY_DEVICE_SUPPORTS_NATIVE_16BIT", "UNITY_DEVICE_SUPPORTS_WAVE_32", "UNITY_DEVICE_SUPPORTS_WAVE_ANY" },
            CopyCountAssertsRange = false,
            TextureWrapMirrorOnce = 0,
            EditorDynamicResolution = true,
            DynamicResolutionMeasured = true,
            Compressed3DTextures = true,
            TargetReadback = new TargetReadbackRule
            {
                Converted = new System.Collections.Generic.HashSet<GraphicsFormat>
                {
                    GraphicsFormat.B8G8R8A8_UNorm, GraphicsFormat.B8G8R8A8_SRGB, GraphicsFormat.B8G8R8A8_SNorm,
                    GraphicsFormat.B4G4R4A4_UNormPack16, GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormat.R5G6B5_UNormPack16,
                    GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.E5B9G9R9_UFloatPack32, GraphicsFormat.R10G10B10_XRUNormPack32,
                },
                NoImageOperations = new System.Collections.Generic.Dictionary<GraphicsFormat, int>
                {
                    [GraphicsFormat.A1R5G5B5_UNormPack16] = (int)GraphicsFormat.A1R5G5B5_UNormPack16,
                    [GraphicsFormat.R5G5B5A1_UNormPack16] = (int)GraphicsFormat.R5G5B5A1_UNormPack16,
                    [GraphicsFormat.B5G5R5A1_UNormPack16] = (int)GraphicsFormat.R5G5B5A1_UNormPack16,
                    [GraphicsFormat.A2R10G10B10_UNormPack32] = (int)GraphicsFormat.A2R10G10B10_UNormPack32,
                    [GraphicsFormat.A10R10G10B10_XRUNormPack32] = (int)GraphicsFormat.A10R10G10B10_XRUNormPack32,
                },
                // tests/projects/textureformats metal.jsonl readback.*.async: the packed words of a
                // target cleared to (0.25, 0.5, 0.75, 0.6).
                PackedWords = new System.Collections.Generic.HashSet<GraphicsFormat>
                {
                    GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormat.B4G4R4A4_UNormPack16, GraphicsFormat.B5G6R5_UNormPack16,
                    GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormat.E5B9G9R9_UFloatPack32,
                    GraphicsFormat.R10G10B10_XRUNormPack32,
                },
                // tests/projects/textureformats metal.jsonl readback.B8G8R8A8_SNorm.read.RGBA32 and
                // readback.{R4G4B4A4,B4G4R4A4,R5G6B5,B5G6R5,R10G10B10_XR}*.read.*, .asyncfloat.
                SignedToBytesTruncates = true,
                ReadsStoredChannelBits = true,
                RefusedSourceNames = new System.Collections.Generic.Dictionary<GraphicsFormat, string>
                {
                    [GraphicsFormat.A1R5G5B5_UNormPack16] = "A1RGB5 UNorm",
                    [GraphicsFormat.R5G5B5A1_UNormPack16] = "RGB5A1 UNorm",
                    [GraphicsFormat.B5G5R5A1_UNormPack16] = "BGR5A1 UNorm",
                    [GraphicsFormat.R5G6B5_UNormPack16] = "R5G6B5 UNorm",
                    [GraphicsFormat.B8G8R8A8_SNorm] = "BGRA8 SNorm",
                    [GraphicsFormat.A2R10G10B10_UNormPack32] = "BGR10A2 UNorm",
                    [GraphicsFormat.A10R10G10B10_XRUNormPack32] = "BGRA10XR UNorm",
                },
            },
            RefusedVolumeFormats = new System.Collections.Generic.Dictionary<TextureFormat, string>
            {
                [TextureFormat.PVRTC_RGB2] = "RGB Compressed PVRTC 2BPP UNorm",
                [TextureFormat.PVRTC_RGBA2] = "RGBA Compressed PVRTC 2BPP UNorm",
                [TextureFormat.PVRTC_RGB4] = "RGB Compressed PVRTC 4BPP UNorm",
                [TextureFormat.PVRTC_RGBA4] = "RGBA Compressed PVRTC 4BPP UNorm",
                [TextureFormat.ETC_RGB4] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.RGB_ETC_UNorm),
                [TextureFormat.EAC_R] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.R_EAC_UNorm),
                [TextureFormat.EAC_R_SIGNED] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.R_EAC_SNorm),
                [TextureFormat.EAC_RG] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.RG_EAC_UNorm),
                [TextureFormat.EAC_RG_SIGNED] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.RG_EAC_SNorm),
                [TextureFormat.ETC2_RGB] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.RGB_ETC2_UNorm),
                [TextureFormat.ETC2_RGBA1] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.RGB_A1_ETC2_UNorm),
                [TextureFormat.ETC2_RGBA8] = GraphicsFormatUtility.CompressedDisplayName(GraphicsFormat.RGBA_ETC2_UNorm),
            },
            MaxTiledPixelStorage = 64,
            // tests/projects/urp CapsProbe `device-metal.jsonl`. (The renderer's Metal device
            // binds 29 storage buffers a stage: 2 of Metal's 31 buffer slots are its own.)
            Limits = new MeasuredLimits
            {
                ComputeBufferInputs = 31,
                GeometryComputeBufferInputs = 0,
                ComputeStageBufferInputs = 31,
                MipStreaming = true,
                ParallelPSOCreation = true,
            },
            RandomWriteFormats = new[]
            {
                RenderTextureFormat.ARGB32, RenderTextureFormat.ARGBHalf, RenderTextureFormat.DefaultHDR, RenderTextureFormat.ARGBFloat,
                RenderTextureFormat.RFloat, RenderTextureFormat.RHalf, RenderTextureFormat.R8, RenderTextureFormat.ARGBInt,
                RenderTextureFormat.RInt, RenderTextureFormat.RGBAUShort,
            },
        };

        /// <summary>The run's device: OpenGL Core until the host says (tests, tools).</summary>
        internal static GraphicsDeviceProfile Current { get; private set; } = OpenGLCore;

        /// <summary>The run's <see cref="RenderingThreadingMode"/>: the device's own
        /// (<see cref="ThreadingMode"/>), or the one the host chose for <c>-force-gfx-mt</c>.</summary>
        internal static RenderingThreadingMode CurrentThreadingMode { get; private set; } = RenderingThreadingMode.Direct;

        /// <summary>Selects the device the host chose (GraphicsDeviceType's name) with its own
        /// threading mode.</summary>
        internal static void Select(string name) => Select(name, null);

        /// <summary>Selects the device and the threading mode the host chose (a
        /// RenderingThreadingMode name; none: the device's own). The host decides the mode
        /// (unbound_compat::forced_multithreading); a name it does not give is a bug.</summary>
        internal static void Select(string name, string threading)
        {
            var profile = Named(name);
            var mode = threading switch
            {
                null or "" => profile.ThreadingMode,
                "Direct" => RenderingThreadingMode.Direct,
                "SingleThreaded" => RenderingThreadingMode.SingleThreaded,
                "MultiThreaded" => RenderingThreadingMode.MultiThreaded,
                _ => throw new System.ArgumentException($"no rendering threading mode is named '{threading}'", nameof(threading)),
            };
            var previous = Current;
            Current = profile;
            CurrentThreadingMode = mode;
            UnityEngine.Shader.SetDeviceKeywords(previous.CapabilityKeywords, Current.CapabilityKeywords);
        }

        /// <summary>The profile of a GraphicsDeviceType name (none: OpenGL Core).</summary>
        internal static GraphicsDeviceProfile Named(string name) => name switch
        {
            null or "" or "OpenGLCore" => OpenGLCore,
            "Vulkan" => Vulkan,
            "Metal" => Metal,
            _ => throw new System.ArgumentException($"Unbound has no {name} graphics device"),
        };

        /// <summary>The screen's rows are stored top-down where UV starts at the top (it is drawn
        /// with the projection unflipped): a raw copy of it holds its image upside down.</summary>
        internal bool ScreenTopDown => UVStartsAtTop;

        /// <summary>A shadow map's depth of clip-space z, as its projection stores it: z * s + 0.5,
        /// s 0.5 ([-1, 1] to [0, 1]), -0.5 where depth is reversed (1 near).</summary>
        internal float ShadowDepthScale => ReversedZ ? -0.5f : 0.5f;

        /// <summary>Clip-space depth per unit of OpenGL Core's: 1 there ([-1, 1]), -0.5 on devices
        /// with reversed depth, which clip it to [0, 1] (Vulkan). Shadow casters' clip-space bias
        /// is scaled by it (measured: tests/projects/lighting shadow_globals_* on the Vulkan
        /// Editor).</summary>
        internal float ClipDepthSlope => ReversedZ ? -0.5f : 1f;

        /// <summary>GL.GetGPUProjectionMatrix: depth from [-1, 1] to [0, 1] (reversed: 1 near) on
        /// devices that clip there, y negated into render textures where UV starts at the top
        /// (measured to the bit on the Vulkan Editor; OpenGL Core keeps the matrix). The renderer
        /// applies the same mapping (`render::device::DeviceConventions::gpu_projection`); both
        /// are tested against the same Editor matrices and must stay identical.</summary>
        internal Matrix4x4 GpuProjection(Matrix4x4 proj, bool renderIntoTexture)
        {
            if (!ReversedZ) return proj;
            for (int c = 0; c < 4; c++) proj[2, c] = proj[2, c] * -0.5f + proj[3, c] * 0.5f;
            if (renderIntoTexture && UVStartsAtTop)
                for (int c = 0; c < 4; c++) proj[1, c] = -proj[1, c];
            return proj;
        }
    }
}
