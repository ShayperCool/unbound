using System;
using Unbound.Runtime;
using System.IO;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>The device as the Editor reports it on this machine, within what Unbound's
    /// renderer does: the run's graphics device (Unbound.Runtime.GraphicsDeviceProfile, chosen at
    /// startup as the Editor chooses it; the Linux Editor's default is OpenGL Core). Answers the
    /// devices share are measured on both (tests/projects/urp CapsProbe).</summary>
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

        static Unbound.Runtime.MachineIdentity.Gpu Gpu => Unbound.Runtime.MachineIdentity.Graphics;
        static Unbound.Runtime.GraphicsDeviceProfile Device => Unbound.Runtime.GraphicsDeviceProfile.Current;

        /// <summary>The Linux Editor on a machine without a battery answers -1 and Full; a
        /// battery's own state (Linux laptops, macOS) is not read yet.</summary>
        public static float batteryLevel { get { WarnBattery(); return -1f; } }
        public static BatteryStatus batteryStatus { get { WarnBattery(); return BatteryStatus.Full; } }

        static readonly Lazy<bool> s_HasBattery = new Lazy<bool>(() =>
        {
            if (OperatingSystem.IsMacOS()) return true;
            try
            {
                foreach (var supply in Directory.GetDirectories("/sys/class/power_supply"))
                    if (File.Exists(Path.Combine(supply, "type")) && File.ReadAllText(Path.Combine(supply, "type")).Trim() == "Battery") return true;
            }
            catch (Exception) { }
            return false;
        });

        static void WarnBattery()
        {
            if (s_HasBattery.Value) Unbound.Runtime.Stubs.Ignored("SystemInfo.batteryLevel/batteryStatus", "do not read the battery yet: they answer -1 and Full, as for a machine without one");
        }

        /// <summary>As the Editor words it: <c>Mac OS X 15.3.1</c>, <c>Linux 7.0 Ubuntu 26.04 64bit</c>
        /// (measured: tests/projects/appfacts).</summary>
        public static string operatingSystem => OperatingSystem.IsMacOS()
            ? $"Mac OS X {Environment.OSVersion.Version.ToString(3)}"
            : Unbound.Runtime.MachineIdentity.LinuxOperatingSystem();
        public static OperatingSystemFamily operatingSystemFamily =>
            OperatingSystem.IsMacOS() ? OperatingSystemFamily.MacOSX :
            OperatingSystem.IsWindows() ? OperatingSystemFamily.Windows : OperatingSystemFamily.Linux;
        public static string processorType => s_Processor.Value;
        public static string processorModel => s_Processor.Value;
        /// <summary>/proc/cpuinfo's vendor_id (<c>AuthenticAMD</c>, <c>GenuineIntel</c>).</summary>
        public static string processorManufacturer
        {
            get
            {
                var vendor = OperatingSystem.IsLinux() ? Unbound.Runtime.MachineIdentity.CpuField("vendor_id") : null;
                if (vendor == null) Unbound.Runtime.Stubs.Ignored("SystemInfo.processorManufacturer", "is not read on this OS: it answers \"\"");
                return vendor ?? "";
            }
        }

        /// <summary>The first processor's clock when first asked, in whole MHz (/proc/cpuinfo's
        /// "cpu MHz", which follows frequency scaling).</summary>
        public static int processorFrequency
        {
            get
            {
                var mhz = OperatingSystem.IsLinux() ? Unbound.Runtime.MachineIdentity.CpuField("cpu MHz") : null;
                if (mhz != null && double.TryParse(mhz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                    return (int)value;
                Unbound.Runtime.Stubs.Ignored("SystemInfo.processorFrequency", "is not read on this machine: it answers 0");
                return 0;
            }
        }
        public static int processorCount { get { MainThreadCheck.Check("GetProcessorCount"); return Environment.ProcessorCount; } }
        public static int systemMemorySize { get { MainThreadCheck.Check("GetPhysicalMemoryMB"); return s_MemoryMb.Value; } }
        public static string deviceUniqueIdentifier => s_DeviceId.Value;
        public static string deviceName => Environment.MachineName;
        /// <summary>"PC" on Linux (measured: tests/projects/appfacts); the macOS Editor's hardware
        /// model is not read yet.</summary>
        public static string deviceModel { get { MainThreadCheck.Check("GetDeviceModel"); if (OperatingSystem.IsMacOS()) Unbound.Runtime.Stubs.Ignored("SystemInfo.deviceModel", "is not read on macOS yet: it answers \"PC\""); return "PC"; } }
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
        // The GPU as the Editor's device names it (Unbound.Runtime.MachineIdentity).
        public static int graphicsMemorySize => Gpu?.MemoryMb ?? 0;
        public static string graphicsDeviceName => Gpu?.Name ?? "Unbound wgpu";
        public static string graphicsDeviceVendor => Gpu?.Vendor ?? "Unbound";
        public static int graphicsDeviceID => Gpu?.DeviceId ?? 0;
        public static int graphicsDeviceVendorID => Gpu?.VendorId ?? 0;
        public static GraphicsDeviceType graphicsDeviceType { get { MainThreadCheck.Check("GetGraphicsDeviceType"); return Device.Type; } }
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static bool graphicsUVStartsAtTop => Device.UVStartsAtTop;
        public static string graphicsDeviceVersion => Gpu?.Version ?? (Device.Type == GraphicsDeviceType.Vulkan ? "Vulkan 1.1.0 (Unbound)" : "OpenGL 4.5.0 (Unbound)");
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
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer draws camera and object motion vectors (tests/projects/builtinrp)")]
        public static bool supportsMotionVectors => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsRenderToCubemap => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsImageEffects => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supports3DTextures => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supports2DArrayTextures => true;
        [Unbound.EditorFaithful("measured on the OpenGL Core device: tests/projects/renderapi volume.supported")]
        public static bool supports3DRenderTextures => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsCubemapArrayTextures => true;
        public static CopyTextureSupport copyTextureSupport => CopyTextureSupport.Basic | CopyTextureSupport.Copy3D | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT | CopyTextureSupport.RTToTexture;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsComputeShaders => true;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers this, and the renderer does it")]
        public static bool supportsInstancing => true;
        [Unbound.EditorFaithful("The renderer exposes no ray tracing device, matching the measured OpenGL Core device.")]
        public static bool supportsRayTracing => false;
        [Unbound.EditorFaithful("The renderer exposes no ray tracing device, matching the measured OpenGL Core device.")]
        public static bool supportsRayTracingShaders => false;
        [Unbound.EditorFaithful("The renderer exposes no ray tracing device, matching the measured OpenGL Core device.")]
        public static bool supportsInlineRayTracing => false;
        [Unbound.EditorFaithful("No XR device or foveated rendering is exposed, matching cmdbuffers device.* (the Vulkan Editor answers FoveationImage, which Unbound's renderer does not do)")]
        public static UnityEngine.Rendering.FoveatedRenderingCaps foveatedRenderingCaps => UnityEngine.Rendering.FoveatedRenderingCaps.None;
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
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static bool usesReversedZBuffer => Device.ReversedZ;
        [Unbound.EditorFaithful("the Editor caps texture sizes at 16384")]
        public static int maxTextureSize => 16384;
        [Unbound.EditorFaithful("the Editor caps texture sizes at 16384")]
        public static int maxCubemapSize => 16384;
        public static int maxRenderTextureSize => 16384;
        public static NPOTSupport npotSupport => NPOTSupport.Full;
        [Unbound.EditorFaithful("readbacks complete (see AsyncReadbacks), as on the Editor's OpenGLCore device")]
        public static bool supportsAsyncGPUReadback => true;
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static bool usesLoadStoreActions => Device.UsesLoadStoreActions;

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
            // Uncompressed integer and normalized formats count as supported where the device
            // samples them after a CPU conversion; float and compressed formats need the device's
            // own (the Unity 6000.0.71f1 Editor answers true for ARGB4444, EAC and the signed
            // 8- and 16-bit formats, false for ASTC, PVRTC and YUY2: tests/projects/texturecompression
            // sys.tf.*).
            var graphics = GraphicsFormatUtility.GetGraphicsFormat(format, false);
            if (graphics != GraphicsFormat.None && !GraphicsFormatUtility.IsIEEE754Format(graphics)
                && !GraphicsFormatUtility.IsCompressedFormat(graphics))
                return true;
            return IsFormatSupported(graphics, GraphicsFormatUsage.Sample);
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
            var usages = Device.Caps.Usages;
            if (i <= 0 || i >= usages.Length) return false;
            return (usages[i] & (long)usage) == (long)usage;
        }

        [Obsolete("Use overload with a GraphicsFormatUsage parameter instead")]
        public static bool IsFormatSupported(GraphicsFormat format, FormatUsage usage) => IsFormatSupported(format, (GraphicsFormatUsage)(1 << (int)usage));

        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, GraphicsFormatUsage usage)
        {
            if (IsFormatSupported(format, usage)) return format;
            int i = (int)format;
            var caps = Device.Caps;
            int[] table = usage switch
            {
                GraphicsFormatUsage.Render => caps.CompatibleRender,
                GraphicsFormatUsage.Blend => caps.CompatibleBlend,
                GraphicsFormatUsage.LoadStore => caps.CompatibleLoadStore,
                _ => caps.CompatibleSample,
            };
            return i > 0 && i < table.Length ? (GraphicsFormat)table[i] : GraphicsFormat.None;
        }

        [Obsolete("Use overload with a GraphicsFormatUsage parameter instead")]
        public static GraphicsFormat GetCompatibleFormat(GraphicsFormat format, FormatUsage usage) => GetCompatibleFormat(format, (GraphicsFormatUsage)(1 << (int)usage));

        public static GraphicsFormat GetGraphicsFormat(DefaultFormat format) => format switch
        {
            DefaultFormat.LDR => QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
            DefaultFormat.HDR => GraphicsFormat.R16G16B16A16_SFloat,
            DefaultFormat.DepthStencil => Device.DefaultDepthStencil,
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
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static bool supportsMultisampleResolveStencil => Device.SupportsMultisampleResolveStencil;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsStoreAndResolveAction => true;
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static bool supportsMultisampleAutoResolve => Device.SupportsMultisampleAutoResolve;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultisampledBackBuffer => false;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static HDRDisplaySupportFlags hdrDisplaySupportFlags => HDRDisplaySupportFlags.None;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsDepthFetchInRenderPass => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsSetConstantBuffer => true;
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static int constantBufferOffsetAlignment => Device.ConstantBufferOffsetAlignment;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int maxConstantBufferSize => 65536;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int supportedRandomWriteTargetCount => 8;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsAsyncCompute => false;
        [Unbound.EditorFaithful("measured on both devices: tests/projects/urp CapsProbe")]
        public static RenderingThreadingMode renderingThreadingMode => Device.ThreadingMode;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool hasMipMaxLevel => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsAnisotropicFilter => true;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int maxAnisotropyLevel => 16;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static int computeSubGroupSize => 32;
        [Unbound.EditorFaithful("measured on the OpenGL Core device: tests/projects/renderapi volumeCompute.limits and wideGroup; the GPU backend requests adapter limits")]
        public static int maxComputeWorkGroupSize => 1024;
        [Unbound.EditorFaithful("measured: tests/projects/renderapi volumeCompute.limits")]
        public static int maxComputeWorkGroupSizeX => 1024;
        [Unbound.EditorFaithful("measured: tests/projects/renderapi volumeCompute.limits")]
        public static int maxComputeWorkGroupSizeY => 1024;
        [Unbound.EditorFaithful("the Editor's OpenGLCore device answers 64 too, and the renderer does it")]
        public static int maxComputeWorkGroupSizeZ => 64;
        [Unbound.EditorFaithful("measured on the OpenGL Core device: tests/projects/urp caps; the GPU backend requests the adapter's limits")]
        public static int maxTexture3DSize => 16384;
        [Unbound.EditorFaithful("measured: tests/projects/renderapi volumeCompute.limits; the GPU backend requests adapter limits")]
        public static int maxTextureArraySlices => 2048;
        public static bool supportsGeometryShaders { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsGeometryShaders", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsTessellationShaders { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsTessellationShaders", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsConservativeRaster { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsConservativeRaster", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe (the Editor's OpenGLCore device)")]
        public static bool supportsMultiview => false;
        [Unbound.EditorFaithful("measured: tests/projects/urp CapsProbe; indirect draws run (Graphics.DrawMeshInstancedIndirect, DrawProceduralIndirect)")]
        public static bool supportsIndirectArgumentsBuffer => true;
        /// <summary>The Editor's OpenGL Core answer (measured: cmdbuffers fence.supported); its async
        /// queue fences cannot be asked whether they passed (supportsAsyncCompute is false).</summary>
        [Unbound.EditorFaithful("measured: tests/projects/cmdbuffers fence.supported")]
        public static bool supportsGraphicsFence => true;
        public static bool supportsRenderTargetArrayIndexFromVertexShader { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsRenderTargetArrayIndexFromVertexShader", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
        public static bool supportsMultisampled2DArrayTextures { get { Unbound.Runtime.Stubs.Ignored("SystemInfo.supportsMultisampled2DArrayTextures", "answers false, what Unbound's renderer does (the Editor answers true): games take their fallback path"); return false; } }
    }
}
