using System;
using System.IO;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>The device as the Editor reports it on this machine: the Linux Editor's OpenGLCore
/// device (shaders compile for SHADER_API_GLCORE), within what Unbound's renderer does.</summary>
    public sealed partial class SystemInfo
    {
        public const string unsupportedIdentifier = "n/a";

        static readonly Lazy<int> s_MemoryMb = new Lazy<int>(() =>
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                    if (line.StartsWith("MemTotal:"))
                        return (int)(long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) / 1024);
            }
            catch (Exception) { }
            return 8192;
        });

        static readonly Lazy<string> s_Processor = new Lazy<string>(() =>
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name")) return line.Substring(line.IndexOf(':') + 1).Trim();
            }
            catch (Exception) { }
            return "Unknown";
        });

        static readonly Lazy<string> s_DeviceId = new Lazy<string>(() =>
        {
            try
            {
                var id = File.ReadAllText("/etc/machine-id").Trim();
                if (id.Length > 0) return id;
            }
            catch (Exception) { }
            return Environment.MachineName;
        });

        /// <summary>The GPU's name, reported by the renderer at start.</summary>
        internal static string s_GraphicsDeviceName = "Unbound wgpu";
        internal static string s_GraphicsDeviceVendor = "Unbound";

        /// <summary>The Linux Editor reads no battery (-1, Unknown); the macOS Editor reads the
        /// laptop's, which Unbound does not yet.</summary>
        public static float batteryLevel { get { if (OperatingSystem.IsMacOS()) Unbound.Runtime.Stubs.Ignored("SystemInfo.batteryLevel", "is not read from the machine yet"); return -1f; } }
        public static BatteryStatus batteryStatus { get { if (OperatingSystem.IsMacOS()) Unbound.Runtime.Stubs.Ignored("SystemInfo.batteryStatus", "is not read from the machine yet"); return BatteryStatus.Unknown; } }
        /// <summary>As the Editor words it: <c>Mac OS X 15.3.1</c>, <c>Linux 6.8.0 64bit</c>.</summary>
        public static string operatingSystem => OperatingSystem.IsMacOS()
            ? $"Mac OS X {Environment.OSVersion.Version.ToString(3)}"
            : $"Linux {Environment.OSVersion.Version} {Environment.Is64BitOperatingSystem switch { true => "64bit", false => "32bit" }}";
        public static OperatingSystemFamily operatingSystemFamily =>
            OperatingSystem.IsMacOS() ? OperatingSystemFamily.MacOSX :
            OperatingSystem.IsWindows() ? OperatingSystemFamily.Windows : OperatingSystemFamily.Linux;
        public static string processorType => s_Processor.Value;
        public static string processorModel => s_Processor.Value;
        public static string processorManufacturer { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.processorManufacturer", "is not read from the machine yet: it answers \"\""); return ""; } }
        public static int processorFrequency { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.processorFrequency", "is not read from the machine yet: it answers 0"); return 0; } }
        public static int processorCount => Environment.ProcessorCount;
        public static int systemMemorySize => s_MemoryMb.Value;
        public static string deviceUniqueIdentifier => s_DeviceId.Value;
        public static string deviceName => Environment.MachineName;
        public static string deviceModel { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.deviceModel", "is not read from the machine yet: it answers \"PC\""); return "PC"; } }
        public static DeviceType deviceType => DeviceType.Desktop;
        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool supportsAccelerometer => false;
        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool supportsGyroscope => false;
        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool supportsLocationService => false;
        [Unbound.EditorFaithful("measured: tests/projects/platform")]
        public static bool supportsVibration => false;
        [Unbound.EditorFaithful("the Editor plays audio")]
        public static bool supportsAudio => true;
        public static int graphicsMemorySize { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.graphicsMemorySize", "is not read from the GPU yet: it answers 4096"); return 4096; } }
        public static string graphicsDeviceName => s_GraphicsDeviceName;
        public static string graphicsDeviceVendor => s_GraphicsDeviceVendor;
        public static int graphicsDeviceID { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.graphicsDeviceID", "is not read from the GPU yet: it answers 0"); return 0; } }
        public static int graphicsDeviceVendorID { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.graphicsDeviceVendorID", "is not read from the GPU yet: it answers 0"); return 0; } }
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.OpenGLCore;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool graphicsUVStartsAtTop => false;
        public static string graphicsDeviceVersion { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.graphicsDeviceVersion", "names no GPU driver yet: it answers \"OpenGL 4.5.0 (Unbound)\""); return "OpenGL 4.5.0 (Unbound)"; } }
        [Unbound.EditorFaithful("the Editor's OpenGLCore 4.5 device is shader level 5.0")]
        public static int graphicsShaderLevel => 50;
        [Unbound.EditorFaithful("measured renderingThreadingMode is Direct (tests/projects/urp CapsProbe): no render thread")]
        public static bool graphicsMultiThreaded => false;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool hasHiddenSurfaceRemovalOnGPU => false;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool hasDynamicUniformArrayIndexingInFragmentShaders => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsShadows => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsRawShadowDepthSampling => true;
        public static bool supportsMotionVectors { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsMotionVectors", "is claimed, but no motion vectors are rendered yet"); return true; } }
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsRenderToCubemap => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsImageEffects => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supports3DTextures => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supports2DArrayTextures => true;
        public static bool supports3DRenderTextures { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supports3DRenderTextures", "is claimed, but 3D render textures are not drawn into yet"); return true; } }
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsCubemapArrayTextures => true;
        public static CopyTextureSupport copyTextureSupport => CopyTextureSupport.Basic | CopyTextureSupport.Copy3D | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT | CopyTextureSupport.RTToTexture;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsComputeShaders => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsInstancing => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsHardwareQuadTopology => false;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supports32bitsIndexBuffer => true;
        public static bool supportsSparseTextures { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsSparseTextures", "answers false, what Unbound's renderer does (the Editor answers true with ARB_sparse_texture): games take their fallback path"); return false; } }
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static int supportedRenderTargetCount => 8;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsSeparatedRenderTargetsBlend => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static int supportsMultisampledTextures => 1;
        public static int supportsTextureWrapMirrorOnce { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsTextureWrapMirrorOnce", "is claimed, but MirrorOnce samples as Mirror"); return 1; } }
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool usesReversedZBuffer => false;
        [Unbound.EditorFaithful("the Editor caps texture sizes at 16384")]
        public static int maxTextureSize => 16384;
        [Unbound.EditorFaithful("the Editor caps texture sizes at 16384")]
        public static int maxCubemapSize => 16384;
        public static int maxRenderTextureSize => 16384;
        public static NPOTSupport npotSupport => NPOTSupport.Full;
        [Unbound.EditorFaithful("readbacks complete (see AsyncReadbacks), as on the Editor's OpenGLCore device")]
        public static bool supportsAsyncGPUReadback => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool usesLoadStoreActions => false;

        /// <summary>From the Editor's format table (sampling the format's linear GraphicsFormat), as
        /// <see cref="IsFormatSupported(GraphicsFormat, GraphicsFormatUsage)"/> answers; crunched
        /// formats as their base format.</summary>
        public static bool SupportsTextureFormat(TextureFormat format)
        {
            format = format switch
            {
                TextureFormat.DXT1Crunched => TextureFormat.DXT1,
                TextureFormat.DXT5Crunched => TextureFormat.DXT5,
                TextureFormat.ETC_RGB4Crunched => TextureFormat.ETC_RGB4,
                TextureFormat.ETC2_RGBA8Crunched => TextureFormat.ETC2_RGBA8,
                _ => format,
            };
            return IsFormatSupported(GraphicsFormatUtility.GetGraphicsFormat(format, false), GraphicsFormatUsage.Sample);
        }
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device renders every RenderTextureFormat)")]
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format) => true;
        /// <summary>From the Editor's format table: blending on the format's linear GraphicsFormat
        /// (integer and depth formats do not blend).</summary>
        public static bool SupportsBlendingOnRenderTextureFormat(RenderTextureFormat format) =>
            IsFormatSupported(GraphicsFormatUtility.GetGraphicsFormat(format, false), GraphicsFormatUsage.Blend);

        public static bool SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat format) => format switch
        {
            RenderTextureFormat.ARGB32 or RenderTextureFormat.ARGBHalf or RenderTextureFormat.Default or RenderTextureFormat.ARGB2101010
                or RenderTextureFormat.DefaultHDR or RenderTextureFormat.ARGB64 or RenderTextureFormat.ARGBFloat or RenderTextureFormat.RGFloat
                or RenderTextureFormat.RGHalf or RenderTextureFormat.RFloat or RenderTextureFormat.RHalf or RenderTextureFormat.R8
                or RenderTextureFormat.ARGBInt or RenderTextureFormat.RGInt or RenderTextureFormat.RInt or RenderTextureFormat.RGB111110Float
                or RenderTextureFormat.RG32 or RenderTextureFormat.RGBAUShort or RenderTextureFormat.RG16 or RenderTextureFormat.R16 => true,
            _ => false,
        };

        // ------------------------------------------------------------ graphics formats (the Editor's table)

        public static bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage)
        {
            int i = (int)format;
            if (i <= 0 || i >= Unbound.Runtime.GraphicsCapsTable.Usages.Length) return false;
            return (Unbound.Runtime.GraphicsCapsTable.Usages[i] & (long)usage) == (long)usage;
        }

        [Obsolete("Use overload with a GraphicsFormatUsage parameter instead")]
        public static bool IsFormatSupported(GraphicsFormat format, FormatUsage usage) => IsFormatSupported(format, (GraphicsFormatUsage)(1 << (int)usage));

        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, GraphicsFormatUsage usage)
        {
            if (IsFormatSupported(format, usage)) return format;
            int i = (int)format;
            int[] table = usage switch
            {
                GraphicsFormatUsage.Render => Unbound.Runtime.GraphicsCapsTable.CompatibleRender,
                GraphicsFormatUsage.Blend => Unbound.Runtime.GraphicsCapsTable.CompatibleBlend,
                GraphicsFormatUsage.LoadStore => Unbound.Runtime.GraphicsCapsTable.CompatibleLoadStore,
                _ => Unbound.Runtime.GraphicsCapsTable.CompatibleSample,
            };
            return i > 0 && i < table.Length ? (GraphicsFormat)table[i] : GraphicsFormat.None;
        }

        [Obsolete("Use overload with a GraphicsFormatUsage parameter instead")]
        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, FormatUsage usage) => GetCompatibleFormat(format, (GraphicsFormatUsage)(1 << (int)usage));

        public static GraphicsFormat GetGraphicsFormat(DefaultFormat format) => format switch
        {
            DefaultFormat.LDR => QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
            DefaultFormat.HDR => GraphicsFormat.R16G16B16A16_SFloat,
            DefaultFormat.DepthStencil => GraphicsFormat.D24_UNorm_S8_UInt,
            DefaultFormat.Shadow => GraphicsFormat.D16_UNorm,
            DefaultFormat.Video => GraphicsFormat.YUV2,
            _ => GraphicsFormat.None,
        };

        // Measured on the reference device in cameramsaa: non-power-of-two requests round
        // down, independently of whether a camera chooses quality or target samples.
        public static int GetRenderTextureSupportedMSAASampleCount(RenderTextureDescriptor desc) =>
            desc.msaaSamples >= 8 ? 8 : desc.msaaSamples >= 4 ? 4 : desc.msaaSamples >= 2 ? 2 : 1;

        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultisampleResolveDepth => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultisampleResolveStencil => false;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsStoreAndResolveAction => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultisampleAutoResolve => false;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultisampledBackBuffer => false;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static HDRDisplaySupportFlags hdrDisplaySupportFlags => HDRDisplaySupportFlags.None;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsDepthFetchInRenderPass => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsSetConstantBuffer => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int constantBufferOffsetAlignment => 256;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int maxConstantBufferSize => 65536;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int supportedRandomWriteTargetCount => 8;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsAsyncCompute => false;
        public static RenderingThreadingMode renderingThreadingMode => RenderingThreadingMode.Direct;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool hasMipMaxLevel => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsAnisotropicFilter => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int maxAnisotropyLevel => 16;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int computeSubGroupSize => 32;
        // What Unbound's renderer (wgpu's default limits) runs; no geometry or tessellation stages,
        // multiview or indirect arguments.
        public static int maxComputeWorkGroupSize { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.maxComputeWorkGroupSize", "answers 256, what Unbound's renderer does (the Editor answers 1024): games take their fallback path"); return 256; } }
        public static int maxComputeWorkGroupSizeX { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.maxComputeWorkGroupSizeX", "answers 256, what Unbound's renderer does (the Editor answers 1024): games take their fallback path"); return 256; } }
        public static int maxComputeWorkGroupSizeY { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.maxComputeWorkGroupSizeY", "answers 256, what Unbound's renderer does (the Editor answers 1024): games take their fallback path"); return 256; } }
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers 64 too, and the renderer does it")]
        public static int maxComputeWorkGroupSizeZ => 64;
        public static int maxTexture3DSize { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.maxTexture3DSize", "answers 2048, what Unbound's renderer does (the Editor answers 16384): games take their fallback path"); return 2048; } }
        public static int maxTextureArraySlices { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.maxTextureArraySlices", "answers 256, what Unbound's renderer does (the Editor answers 2048): games take their fallback path"); return 256; } }
        public static bool supportsGeometryShaders { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsGeometryShaders", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsTessellationShaders { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsTessellationShaders", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsConservativeRaster { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsConservativeRaster", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultiview => false;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe; indirect draws run (Graphics.DrawMeshInstancedIndirect, DrawProceduralIndirect)")]
        public static bool supportsIndirectArgumentsBuffer => true;
        public static bool supportsGraphicsFence { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsGraphicsFence", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsRenderTargetArrayIndexFromVertexShader { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsRenderTargetArrayIndexFromVertexShader", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsMultisampled2DArrayTextures { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsMultisampled2DArrayTextures", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
    }
}
