using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// The run's graphics device and what the Editor answers on it, as the native side chose it
    /// once at startup (unbound_compat::graphics: `--force-vulkan`/`--force-glcore`, the project's
    /// graphics API list, the Editor's default). Every device-dependent answer reads the selected
    /// profile; nothing compares device types elsewhere. Answers are the Unity 6000.0.71f1 Linux
    /// Editor's on an NVIDIA RTX 4060 (tests/projects/urp CapsProbe, `caps-6000.0.71f1.jsonl`
    /// for OpenGL Core and `unity-6000.0.71f1/caps-vulkan.jsonl` for Vulkan); where the devices
    /// answer alike the answer stays in SystemInfo. Not followed: the Vulkan Editor's GPU recorder
    /// and foveation image support (Unbound's renderer has neither; both answer as on OpenGL Core).
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

        /// <summary>The GPU as the device names it (read once, when first asked).</summary>
        internal MachineIdentity.Gpu Identity => m_Identity.Value;

        GraphicsDeviceProfile(GraphicsDeviceType type, bool uvStartsAtTop, bool reversedZ, bool loadStore,
            bool resolveStencil, bool autoResolve, int cbufferAlignment, RenderingThreadingMode threading,
            GraphicsFormat depthStencil, BatchBufferTarget batchTarget, GraphicsCapsTable.Device caps,
            System.Func<MachineIdentity.Gpu> identity)
        {
            Type = type;
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
            BatchBufferTarget.ConstantBuffer, GraphicsCapsTable.OpenGLCore, MachineIdentity.ReadOpenGl);

        internal static readonly GraphicsDeviceProfile Vulkan = new GraphicsDeviceProfile(
            GraphicsDeviceType.Vulkan, uvStartsAtTop: true, reversedZ: true, loadStore: true,
            resolveStencil: true, autoResolve: true, cbufferAlignment: 64,
            RenderingThreadingMode.SingleThreaded, GraphicsFormat.D32_SFloat_S8_UInt,
            BatchBufferTarget.RawBuffer, GraphicsCapsTable.Vulkan, VulkanIdentity.Read);

        /// <summary>The run's device: OpenGL Core until the host says (tests, tools).</summary>
        internal static GraphicsDeviceProfile Current { get; private set; } = OpenGLCore;

        /// <summary>Selects the device the host chose (GraphicsDeviceType's name).</summary>
        internal static void Select(string name) => Current = Named(name);

        /// <summary>The profile of a GraphicsDeviceType name (none: OpenGL Core).</summary>
        internal static GraphicsDeviceProfile Named(string name) => name switch
        {
            null or "" or "OpenGLCore" => OpenGLCore,
            "Vulkan" => Vulkan,
            _ => throw new System.ArgumentException($"Unbound has no {name} graphics device"),
        };

        /// <summary>GL.GetGPUProjectionMatrix: depth from [-1, 1] to [0, 1] (reversed: 1 near) on
        /// devices that clip there, y negated into render textures where UV starts at the top
        /// (measured to the bit on the Vulkan Editor; OpenGL Core keeps the matrix).</summary>
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
