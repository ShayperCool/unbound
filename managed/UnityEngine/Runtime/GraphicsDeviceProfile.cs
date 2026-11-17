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
        /// <summary>The most samples GetRenderTextureSupportedMSAASampleCount answers.</summary>
        internal int MaxMsaaSamples { get; init; } = 8;
        internal bool DepthFetchInRenderPass { get; init; } = true;
        internal int MaxTexture3DSize { get; init; } = 16384;
        internal int MaxComputeWorkGroupSizeZ { get; init; } = 64;
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

        /// <summary>Whether Texture2D.ReadPixels and AsyncGPUReadback read a render texture whose
        /// format answers no ReadPixels usage. Metal reads B8G8R8A8 (its LDR targets), B4G4R4A4,
        /// E5B9G9R9 and A2B10G10R10 targets, whose usages leave ReadPixels out, and refuses only
        /// <see cref="UnreadableTargets"/> and A2B10G10R10 into float texels
        /// (tests/projects/textureformats `metal.jsonl` readback.*, two macOS Editor captures on an
        /// Apple M2 Pro). False: such a target is refused (Vulkan's ReadPixels and every device's
        /// AsyncGPUReadback, tests/projects/textureformats).</summary>
        internal bool ReadsTargetsWithoutReadPixelsUsage { get; init; }
        /// <summary>The render texture formats the device reads back from no target, by the name its
        /// AsyncGPUReadback error gives the format (Metal: A1R5G5B5, measured with
        /// <see cref="ReadsTargetsWithoutReadPixelsUsage"/>).</summary>
        internal System.Collections.Generic.IReadOnlyDictionary<GraphicsFormat, string> UnreadableTargets { get; init; }
            = new System.Collections.Generic.Dictionary<GraphicsFormat, string>();

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
        };

        internal static readonly GraphicsDeviceProfile Vulkan = new GraphicsDeviceProfile(
            GraphicsDeviceType.Vulkan, uvStartsAtTop: true, reversedZ: true, loadStore: true,
            resolveStencil: true, autoResolve: true, cbufferAlignment: 64,
            RenderingThreadingMode.SingleThreaded, GraphicsFormat.D32_SFloat_S8_UInt,
            BatchBufferTarget.RawBuffer, GraphicsCapsTable.Vulkan, VulkanIdentity.Read, framebufferReadback: false, alpha8Readback: Alpha8ReadbackMode.Red);

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
            TextureWrapMirrorOnce = 0,
            EditorDynamicResolution = true,
            Compressed3DTextures = true,
            ReadsTargetsWithoutReadPixelsUsage = true,
            UnreadableTargets = new System.Collections.Generic.Dictionary<GraphicsFormat, string>
            {
                [GraphicsFormat.A1R5G5B5_UNormPack16] = "A1RGB5 UNorm",
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
