using System;
using System.IO;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>The device as the Editor reports it on this machine.</summary>
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

        public static float batteryLevel => -1f;
        public static BatteryStatus batteryStatus => BatteryStatus.Unknown;
        public static string operatingSystem => $"Linux {Environment.OSVersion.Version} {Environment.Is64BitOperatingSystem switch { true => "64bit", false => "32bit" }}";
        public static OperatingSystemFamily operatingSystemFamily => OperatingSystemFamily.Linux;
        public static string processorType => s_Processor.Value;
        public static string processorModel => s_Processor.Value;
        public static string processorManufacturer => "";
        public static int processorFrequency => 0;
        public static int processorCount => Environment.ProcessorCount;
        public static int systemMemorySize => s_MemoryMb.Value;
        public static string deviceUniqueIdentifier => s_DeviceId.Value;
        public static string deviceName => Environment.MachineName;
        public static string deviceModel => "PC";
        public static DeviceType deviceType => DeviceType.Desktop;
        public static bool supportsAccelerometer => false;
        public static bool supportsGyroscope => false;
        public static bool supportsLocationService => false;
        public static bool supportsVibration => false;
        public static bool supportsAudio => true;
        public static int graphicsMemorySize => 4096;
        public static string graphicsDeviceName => s_GraphicsDeviceName;
        public static string graphicsDeviceVendor => s_GraphicsDeviceVendor;
        public static int graphicsDeviceID => 0;
        public static int graphicsDeviceVendorID => 0;
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.Vulkan;
        public static bool graphicsUVStartsAtTop => false;
        public static string graphicsDeviceVersion => "Vulkan (Unbound)";
        public static int graphicsShaderLevel => 50;
        public static bool graphicsMultiThreaded => false;
        public static bool hasHiddenSurfaceRemovalOnGPU => false;
        public static bool hasDynamicUniformArrayIndexingInFragmentShaders => true;
        public static bool supportsShadows => true;
        public static bool supportsRawShadowDepthSampling => true;
        public static bool supportsMotionVectors => true;
        public static bool supportsRenderToCubemap => true;
        public static bool supportsImageEffects => true;
        public static bool supports3DTextures => true;
        public static bool supports2DArrayTextures => true;
        public static bool supports3DRenderTextures => true;
        public static bool supportsCubemapArrayTextures => true;
        public static CopyTextureSupport copyTextureSupport => CopyTextureSupport.Basic | CopyTextureSupport.Copy3D | CopyTextureSupport.DifferentTypes | CopyTextureSupport.TextureToRT | CopyTextureSupport.RTToTexture;
        public static bool supportsComputeShaders => false;
        public static bool supportsInstancing => true;
        public static bool supportsHardwareQuadTopology => false;
        public static bool supports32bitsIndexBuffer => true;
        public static bool supportsSparseTextures => false;
        public static int supportedRenderTargetCount => 8;
        public static bool supportsSeparatedRenderTargetsBlend => true;
        public static int supportsMultisampledTextures => 1;
        public static int supportsTextureWrapMirrorOnce => 1;
        public static bool usesReversedZBuffer => true;
        public static int maxTextureSize => 16384;
        public static int maxCubemapSize => 16384;
        public static int maxRenderTextureSize => 16384;
        public static NPOTSupport npotSupport => NPOTSupport.Full;
        public static bool supportsAsyncGPUReadback => false;
        public static bool usesLoadStoreActions => false;

        public static bool SupportsTextureFormat(TextureFormat format) => true;
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format) => true;
        public static bool SupportsBlendingOnRenderTextureFormat(RenderTextureFormat format) => true;
    }
}

namespace UnityEngine.Profiling
{
    /// <summary>The profiler: samples are accepted and ignored.</summary>
    public sealed partial class Profiler
    {
        public static bool supported => false;
        public static bool enabled { get; set; }
        public static bool enableBinaryLog { get; set; }
        public static string logFile { get; set; }
        public static int maxUsedMemory { get; set; }
        public static long usedHeapSizeLong => GC.GetTotalMemory(false);
        public static uint usedHeapSize => (uint)Math.Min(uint.MaxValue, GC.GetTotalMemory(false));

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void BeginSample(string name) { }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void BeginSample(string name, Object targetObject) { }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void EndSample() { }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void BeginThreadProfiling(string threadGroupName, string threadName) { }

        public static void EndThreadProfiling() { }

        public static long GetRuntimeMemorySizeLong(Object o) => 0;
        public static long GetMonoHeapSizeLong() => GC.GetTotalMemory(false);
        public static long GetMonoUsedSizeLong() => GC.GetTotalMemory(false);
        public static long GetTotalAllocatedMemoryLong() => GC.GetTotalMemory(false);
        public static long GetTotalReservedMemoryLong() => GC.GetTotalMemory(false);
        public static long GetTotalUnusedReservedMemoryLong() => 0;
        public static long GetAllocatedMemoryForGraphicsDriver() => 0;
        public static long GetTempAllocatorSize() => 0;
        public static bool SetTempAllocatorRequestedSize(uint size) => true;
    }
}
