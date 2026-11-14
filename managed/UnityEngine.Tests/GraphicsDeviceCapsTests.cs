using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Unbound.Tests
{
    /// <summary>SystemInfo's device answers on each graphics device against the Unity 6000.0.71f1
    /// Editor's (tests/projects/urp CapsProbe: OpenGL Core and Vulkan on the Linux Editor, Metal on
    /// the macOS Editor): the records CapsProbe.Record writes for SystemInfo, default formats,
    /// render texture descriptors, format support and BatchRendererGroup, made the same way here.
    /// The scene-dependent records (ambient, lights, spherical harmonics) and the built-in shader
    /// defines (the host passes the target's) are tested elsewhere. Tests do not run in parallel,
    /// so selecting a device for one is safe; it is put back after.</summary>
    public class GraphicsDeviceCapsTests
    {
        readonly ITestOutputHelper m_Output;
        public GraphicsDeviceCapsTests(ITestOutputHelper output) => m_Output = output;

        static string Reference(string name, [CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "..", "tests", "projects", "urp", "Reference", name));

        static Dictionary<string, string> Read(string path)
        {
            var values = new Dictionary<string, string>();
            foreach (var line in File.ReadLines(path))
            {
                using var row = JsonDocument.Parse(line);
                values[row.RootElement.GetProperty("key").GetString()] = row.RootElement.GetProperty("value").GetString();
            }
            return values;
        }

        /// <summary>What CapsProbe.Record writes before its built-in shader defines, as Unbound
        /// answers it.</summary>
        static Dictionary<string, string> Record()
        {
            var r = new Dictionary<string, string>();
            void Line(string key, object value) => r[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) switch
            {
                "True" => "True",
                "False" => "False",
                var text => text,
            };
            Line("graphicsDeviceType", SystemInfo.graphicsDeviceType);
            Line("usesReversedZBuffer", SystemInfo.usesReversedZBuffer);
            Line("graphicsUVStartsAtTop", SystemInfo.graphicsUVStartsAtTop);
            Line("supportsComputeShaders", SystemInfo.supportsComputeShaders);
            Line("usesLoadStoreActions", SystemInfo.usesLoadStoreActions);
            Line("supportsMultisampleResolveDepth", SystemInfo.supportsMultisampleResolveDepth);
            Line("supportsMultisampleResolveStencil", SystemInfo.supportsMultisampleResolveStencil);
            Line("supportsStoreAndResolveAction", SystemInfo.supportsStoreAndResolveAction);
            Line("supportsMultisampleAutoResolve", SystemInfo.supportsMultisampleAutoResolve);
            Line("supportsMultisampledBackBuffer", SystemInfo.supportsMultisampledBackBuffer);
            Line("hdrDisplaySupportFlags", SystemInfo.hdrDisplaySupportFlags);
            Line("supportsDepthFetchInRenderPass", SystemInfo.supportsDepthFetchInRenderPass);
            Line("supportsRenderTargetArrayIndexFromVertexShader", SystemInfo.supportsRenderTargetArrayIndexFromVertexShader);
            Line("supportsSetConstantBuffer", SystemInfo.supportsSetConstantBuffer);
            Line("constantBufferOffsetAlignment", SystemInfo.constantBufferOffsetAlignment);
            Line("maxConstantBufferSize", SystemInfo.maxConstantBufferSize);
            Line("supportedRandomWriteTargetCount", SystemInfo.supportedRandomWriteTargetCount);
            Line("supportsGraphicsFence", SystemInfo.supportsGraphicsFence);
            Line("supportsAsyncCompute", SystemInfo.supportsAsyncCompute);
            Line("supportsGeometryShaders", SystemInfo.supportsGeometryShaders);
            Line("supportsTessellationShaders", SystemInfo.supportsTessellationShaders);
            Line("supportsConservativeRaster", SystemInfo.supportsConservativeRaster);
            Line("supportsMultiview", SystemInfo.supportsMultiview);
            Line("supportsIndirectArgumentsBuffer", SystemInfo.supportsIndirectArgumentsBuffer);
            Line("supportsMultisampled2DArrayTextures", SystemInfo.supportsMultisampled2DArrayTextures);
            Line("maxTexture3DSize", SystemInfo.maxTexture3DSize);
            Line("maxTextureArraySlices", SystemInfo.maxTextureArraySlices);
            Line("maxComputeWorkGroupSize", SystemInfo.maxComputeWorkGroupSize);
            Line("computeSubGroupSize", SystemInfo.computeSubGroupSize);
            Line("renderingThreadingMode", SystemInfo.renderingThreadingMode);
            Line("hasMipMaxLevel", SystemInfo.hasMipMaxLevel);
            Line("supportsAnisotropicFilter", SystemInfo.supportsAnisotropicFilter);
            Line("maxAnisotropyLevel", SystemInfo.maxAnisotropyLevel);
            foreach (DefaultFormat f in Enum.GetValues(typeof(DefaultFormat)))
                Line("GetGraphicsFormat." + f, SystemInfo.GetGraphicsFormat(f));
            foreach (RenderTextureFormat f in new[] { RenderTextureFormat.Default, RenderTextureFormat.DefaultHDR, RenderTextureFormat.ARGB32, RenderTextureFormat.Depth, RenderTextureFormat.Shadowmap })
            {
                var d = new RenderTextureDescriptor(64, 64, f, 24);
                Line("RenderTextureDescriptor.graphicsFormat." + f, d.graphicsFormat);
                Line("RenderTextureDescriptor.depthStencilFormat." + f, d.depthStencilFormat);
                d.msaaSamples = 8;
                Line("GetRenderTextureSupportedMSAASampleCount.8." + f, SystemInfo.GetRenderTextureSupportedMSAASampleCount(d));
            }
            var allUsages = (GraphicsFormatUsage[])Enum.GetValues(typeof(GraphicsFormatUsage));
            foreach (GraphicsFormat f in Enum.GetValues(typeof(GraphicsFormat)))
            {
                if (f == GraphicsFormat.None) continue;
                long mask = 0;
                foreach (var u in allUsages)
                    if (u != GraphicsFormatUsage.None && SystemInfo.IsFormatSupported(f, u)) mask |= (long)u;
                Line("usages." + (int)f, mask);
                Line("compatible.render." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Render));
                Line("compatible.sample." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Sample));
                Line("compatible.blend." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Blend));
                Line("compatible.loadstore." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.LoadStore));
            }
            foreach (RenderTextureFormat f in Enum.GetValues(typeof(RenderTextureFormat)))
            {
                try { Line("SupportsRenderTextureFormat." + f, SystemInfo.SupportsRenderTextureFormat(f)); } catch (Exception) { }
                try { Line("SupportsRandomWriteOnRenderTextureFormat." + f, SystemInfo.SupportsRandomWriteOnRenderTextureFormat(f)); } catch (Exception) { }
            }
            Line("BatchRendererGroup.BufferTarget", BatchRendererGroup.BufferTarget);
            Line("BatchRendererGroup.GetConstantBufferMaxWindowSize", BatchRendererGroup.GetConstantBufferMaxWindowSize());
            Line("BatchRendererGroup.GetConstantBufferOffsetAlignment", BatchRendererGroup.GetConstantBufferOffsetAlignment());
            return r;
        }

        /// <summary>The records of `reference` Unbound answers differently on `device`, as
        /// "key: Editor -> Unbound".</summary>
        List<string> Differences(string device, string reference, ColorSpace space)
        {
            var editor = Read(Reference(reference));
            var previous = GraphicsDeviceProfile.Current;
            var previousSpace = QualitySettings.activeColorSpace;
            Dictionary<string, string> ours;
            try
            {
                GraphicsDeviceProfile.Select(device);
                QualitySettings.s_ColorSpace = space;
                ours = Record();
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
                QualitySettings.s_ColorSpace = previousSpace;
            }
            var differences = new List<string>();
            foreach (var (key, value) in ours)
                if (!editor.TryGetValue(key, out var expected)) differences.Add($"{key}: (not recorded) -> {value}");
                else if (expected != value) differences.Add($"{key}: {expected} -> {value}");
            foreach (var d in differences) m_Output.WriteLine(d);
            differences.Sort(StringComparer.Ordinal);
            return differences;
        }

        static List<string> Sorted(List<string> values)
        {
            values.Sort(StringComparer.Ordinal);
            return values;
        }

        /// <summary>What Unbound's renderer does not do, on every device: it answers false where
        /// the Editors answer true (SystemInfo warns of each).</summary>
        static readonly string[] s_NotDrawn =
        {
            "supportsRenderTargetArrayIndexFromVertexShader: True -> False",
            "supportsTessellationShaders: True -> False",
            "supportsMultisampled2DArrayTextures: True -> False",
        };

        /// <summary>The Linux Editor's devices also have geometry shaders and conservative
        /// rasterization, which Unbound's renderer does not.</summary>
        static readonly string[] s_LinuxNotDrawn =
        {
            "supportsGeometryShaders: True -> False",
            "supportsConservativeRaster: True -> False",
        };

        [Fact]
        public void LinuxDevicesAnswerAsTheLinuxEditor()
        {
            var expected = new List<string>(s_NotDrawn);
            expected.AddRange(s_LinuxNotDrawn);
            Assert.Equal(Sorted(expected), Differences("OpenGLCore", "caps-6000.0.71f1.jsonl", ColorSpace.Gamma));
            // BGRA8 storage is a device feature the renderer does not request.
            expected.Add("SupportsRandomWriteOnRenderTextureFormat.BGRA32: True -> False");
            Assert.Equal(Sorted(expected), Differences("Vulkan", "unity-6000.0.71f1/caps-vulkan.jsonl", ColorSpace.Gamma));
        }

        /// <summary>Every SystemInfo property the macOS Editor recorded (`device-metal.jsonl`: the
        /// ones the machine decides are not recorded) against Unbound's on the Metal device. The
        /// GPU's own strings and ids are read from Metal and the OS family is the host's, so they
        /// are compared on macOS only.</summary>
        [Fact]
        public void MetalSystemInfoAsTheMacEditor()
        {
            var editor = Read(Reference("unity-6000.0.71f1/device-metal.jsonl"));
            // Read from Metal or decided by the OS: compared on macOS only.
            var identity = new[] { "graphicsDeviceVendor", "graphicsDeviceVersion", "graphicsDeviceVendorID", "graphicsDeviceID", "operatingSystemFamily" };
            var previous = GraphicsDeviceProfile.Current;
            var differences = new List<string>();
            try
            {
                GraphicsDeviceProfile.Select("Metal");
                foreach (var (key, recorded) in editor)
                {
                    if (!key.StartsWith("SystemInfo.", StringComparison.Ordinal)) continue;
                    var name = key.Substring("SystemInfo.".Length);
                    if (!OperatingSystem.IsMacOS() && Array.IndexOf(identity, name) >= 0) continue;
                    var property = typeof(SystemInfo).GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    string value;
                    try { value = Convert.ToString(property.GetValue(null), System.Globalization.CultureInfo.InvariantCulture); }
                    catch (Exception e) { value = "throws " + (e.InnerException ?? e).GetType().Name; }
                    if (value != recorded) differences.Add($"{name}: {recorded} -> {value}");
                }
            }
            finally
            {
                GraphicsDeviceProfile.Select(previous.Type.ToString());
            }
            foreach (var d in differences) m_Output.WriteLine(d);
            var expected = new List<string>
            {
                // Not done by Unbound's renderer (no foveation, HDR output or sparse textures).
                "foveatedRenderingCaps: NonUniformRaster -> None",
                "hdrDisplaySupportFlags: Supported, RuntimeSwitchable, AutomaticTonemapping -> None",
                "supportsSparseTextures: True -> False",
                // Stubs on every device (not implemented yet).
                "maxComputeBufferInputsCompute: 31 -> 0",
                "maxComputeBufferInputsDomain: 31 -> 0",
                "maxComputeBufferInputsFragment: 31 -> 0",
                "maxComputeBufferInputsHull: 31 -> 0",
                "maxComputeBufferInputsVertex: 31 -> 0",
                "maxTiledPixelStorageSize: 64 -> 0",
                "supportsCompressed3DTextures: True -> False",
                "supportsDynamicResolution: True -> False",
                "supportsMipStreaming: True -> False",
                "supportsParallelPSOCreation: True -> False",
            };
            expected.AddRange(s_NotDrawn);
            Assert.Equal(Sorted(expected), Sorted(differences));
        }

        /// <summary>GL.GetGPUProjectionMatrix on Metal, to the bit (`device-metal.jsonl`: the
        /// same matrices as the Vulkan Editor's).</summary>
        [Fact]
        public void MetalGpuProjectionAsTheMacEditor()
        {
            var editor = Read(Reference("unity-6000.0.71f1/device-metal.jsonl"));
            var projections = new (string, Matrix4x4)[]
            {
                ("perspective", Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 1000f)),
                ("ortho", Matrix4x4.Ortho(-5f, 5f, -3f, 3f, 1f, 11f)),
            };
            foreach (var (name, projection) in projections)
                foreach (var texture in new[] { false, true })
                {
                    var m = GraphicsDeviceProfile.Metal.GpuProjection(projection, texture);
                    // The Editor writes Mono's 9 significant digits: the same floats when parsed.
                    // The rows the device changes (y and depth) are compared; the others are
                    // Matrix4x4.Perspective's own.
                    var recorded = Array.ConvertAll(editor[$"GL.GetGPUProjectionMatrix.{name}.{texture}"].Split(' '),
                        v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                    for (int column = 0; column < 4; column++)
                        for (int row = 1; row <= 2; row++)
                            Assert.Equal(recorded[column * 4 + row], m[row, column]);
                }
        }

        /// <summary>The framebuffer fetch defines on Metal with the defines the host passes
        /// (unbound_project's shader_defines): depth fetch is answered where the fallback is not
        /// compiled. The macOS Editor with the standalone target (`caps-metal.jsonl`), with a
        /// list of OpenGL Core and Vulkan (`caps-metal-list.jsonl`) and with Android active
        /// (`caps-metal-android.jsonl`, whose other mobile defines are tested elsewhere).</summary>
        [Fact]
        public void MetalFetchDefinesAsTheMacEditor()
        {
            var desktop = new[] { "SHADER_API_DESKTOP", "UNITY_PBS_USE_BRDF1", "UNITY_SPECCUBE_BOX_PROJECTION", "UNITY_SPECCUBE_BLENDING",
                "UNITY_ENABLE_DETAIL_NORMALMAP", "UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS", "UNITY_HARDWARE_TIER3" };
            var mobile = new[] { "SHADER_API_MOBILE", "UNITY_NO_DXT5nm", "UNITY_PBS_USE_BRDF2", "UNITY_NO_SCREENSPACE_SHADOWS", "UNITY_HARDWARE_TIER3" };
            var cases = new (string, string[])[]
            {
                ("caps-metal.jsonl", desktop.Concat(new[] { "UNITY_FRAMEBUFFER_FETCH_AVAILABLE", "UNITY_NEEDS_RENDERPASS_FBFETCH_FALLBACK" }).ToArray()),
                ("caps-metal-list.jsonl", desktop.Concat(new[] { "UNITY_FRAMEBUFFER_FETCH_AVAILABLE" }).ToArray()),
                ("caps-metal-android.jsonl", mobile.Concat(new[] { "UNITY_FRAMEBUFFER_FETCH_AVAILABLE" }).ToArray()),
            };
            var fetch = new[] { BuiltinShaderDefine.UNITY_FRAMEBUFFER_FETCH_AVAILABLE, BuiltinShaderDefine.UNITY_NEEDS_RENDERPASS_FBFETCH_FALLBACK,
                BuiltinShaderDefine.UNITY_PLATFORM_SUPPORTS_DEPTH_FETCH };
            try
            {
                foreach (var (reference, defines) in cases)
                {
                    var editor = Read(Reference("unity-6000.0.71f1/" + reference));
                    GraphicsSettings.SetPlatformShaderDefines(defines);
                    foreach (var define in fetch)
                        Assert.Equal(editor["HasShaderDefine." + define], GraphicsSettings.HasShaderDefine(define) ? "True" : "False");
                }
            }
            finally
            {
                GraphicsSettings.SetPlatformShaderDefines(desktop.Append("UNITY_LIGHT_PROBE_PROXY_VOLUME").Where(d => d != "UNITY_HARDWARE_TIER3"));
            }
        }

        [Fact]
        public void MetalAnswersAsTheMacEditor()
        {
            // The macOS Editor's HDR display support is the display's (an HDR one here); Unbound
            // presents no HDR output.
            var expected = new List<string> { "hdrDisplaySupportFlags: Supported, RuntimeSwitchable, AutomaticTonemapping -> None" };
            expected.AddRange(s_NotDrawn);
            Assert.Equal(Sorted(expected), Differences("Metal", "unity-6000.0.71f1/caps-metal.jsonl", ColorSpace.Gamma));
            Assert.Equal(Sorted(expected), Differences("Metal", "unity-6000.0.71f1/caps-metal-linear.jsonl", ColorSpace.Linear));
        }
    }
}
