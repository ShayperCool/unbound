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
        /// <summary>The Editor's device scales buffers dynamically (Metal: SystemInfo answers true,
        /// a camera keeps allowDynamicResolution, ScalableBufferManager.ResizeBuffers sets the
        /// factors and dynamically scaled render textures take the scaled size;
        /// tests/projects/nativegfx dynres.*). Unbound scales them as it does.</summary>
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
        /// <summary>SystemInfo.supportsTextureWrapMirrorOnce: 1 where the Editor's device answers
        /// it (Unbound samples MirrorOnce as Mirror there, warned), 0 on Metal.</summary>
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
        }

        /// <summary>SystemInfo.maxTiledPixelStorageSize where it is measured (Metal: 64, tests/projects/urp
        /// `device-metal.jsonl`; OpenGL Core: int.MaxValue, tests/projects/srpmembers tiled.max);
        /// null on Vulkan, where it stays a stub. On OpenGL Core GetTiledRenderTargetStorageSize is
        /// measured too (<see cref="TiledStorageMeasured"/>).</summary>
        internal int? MaxTiledPixelStorage { get; init; }
        internal bool TiledStorageMeasured { get; init; }

        /// <summary>SystemInfo answers measured on the device's Editor alone; null where they are
        /// not measured (the members stay stubs there).</summary>
        internal MeasuredLimits Limits { get; init; }

        internal sealed class MeasuredLimits
        {
            /// <summary>maxComputeBufferInputs of the vertex, fragment, hull, domain and compute
            /// stages, and of the geometry stage.</summary>
            internal int ComputeBufferInputs, GeometryComputeBufferInputs;
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
            ProceduralQuads = UnityEngine.MeshTopology.Points,
        };

        internal static readonly GraphicsDeviceProfile Vulkan = new GraphicsDeviceProfile(
            GraphicsDeviceType.Vulkan, uvStartsAtTop: true, reversedZ: true, loadStore: true,
            resolveStencil: true, autoResolve: true, cbufferAlignment: 64,
            RenderingThreadingMode.SingleThreaded, GraphicsFormat.D32_SFloat_S8_UInt,
            BatchBufferTarget.RawBuffer, GraphicsCapsTable.Vulkan, VulkanIdentity.Read, framebufferReadback: false, alpha8Readback: Alpha8ReadbackMode.Red)
        {
            ProceduralQuads = UnityEngine.MeshTopology.Triangles,
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
            MaxMsaaSamples = 4,
            DepthFetchInRenderPass = false,
            MaxTexture3DSize = 2048,
            MaxComputeWorkGroupSizeZ = 1024,
            EditorGeometryShaders = false,
            EditorConservativeRaster = false,
            HiddenSurfaceRemovalOnGPU = true,
            PartialScreenSeedUndecoded = true,
            TextureWrapMirrorOnce = 0,
            EditorDynamicResolution = true,
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
                // target cleared to (0.25, 0.5, 0.75, 0.6); R10G10B10_XR's are not decoded yet.
                PackedWords = new System.Collections.Generic.HashSet<GraphicsFormat>
                {
                    GraphicsFormat.R4G4B4A4_UNormPack16, GraphicsFormat.B4G4R4A4_UNormPack16, GraphicsFormat.B5G6R5_UNormPack16,
                    GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormat.E5B9G9R9_UFloatPack32,
                },
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
                [TextureFormat.ETC_RGB4] = "RGB Compressed ETC UNorm",
                [TextureFormat.EAC_R] = "R Compressed EAC UNorm",
                [TextureFormat.EAC_R_SIGNED] = "R Compressed EAC SNorm",
                [TextureFormat.EAC_RG] = "RG Compressed EAC UNorm",
                [TextureFormat.EAC_RG_SIGNED] = "RG Compressed EAC SNorm",
                [TextureFormat.ETC2_RGB] = "RGB Compressed ETC2 UNorm",
                [TextureFormat.ETC2_RGBA1] = "RGBA Compressed ETC2 punchthrough UNorm",
                [TextureFormat.ETC2_RGBA8] = "RGBA Compressed ETC2 UNorm",
            },
            MaxTiledPixelStorage = 64,
            // tests/projects/urp CapsProbe `device-metal.jsonl`. (The renderer's Metal device
            // binds 29 storage buffers a stage: 2 of Metal's 31 buffer slots are its own.)
            Limits = new MeasuredLimits
            {
                ComputeBufferInputs = 31,
                GeometryComputeBufferInputs = 0,
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

        /// <summary>Selects the device the host chose (GraphicsDeviceType's name).</summary>
        internal static void Select(string name) => Current = Named(name);

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
