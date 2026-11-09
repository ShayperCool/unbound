using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    /// <summary>The graphics device profiles against the Unity 6000.0.71f1 Editor's answers on
    /// OpenGL Core and Vulkan (tests/projects/urp CapsProbe; GL.GetGPUProjectionMatrix of
    /// Matrix4x4.Perspective(60, 16/9, 0.3, 1000) and Ortho(-5, 5, -3, 3, 1, 11)). The profiles
    /// are read directly: the run's selected one is process state other tests read.</summary>
    public class GraphicsDeviceProfileTests
    {
        static readonly Matrix4x4 s_Perspective = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 1000f);
        static readonly Matrix4x4 s_Ortho = Matrix4x4.Ortho(-5f, 5f, -3f, 3f, 1f, 11f);

        [Fact]
        public void OpenGLCoreKeepsTheProjection()
        {
            var gl = GraphicsDeviceProfile.OpenGLCore;
            Assert.Equal(s_Perspective, gl.GpuProjection(s_Perspective, true));
            Assert.Equal(GraphicsDeviceType.OpenGLCore, gl.Type);
            Assert.False(gl.ReversedZ || gl.UVStartsAtTop);
            Assert.Equal(GraphicsFormat.D24_UNorm_S8_UInt, gl.DefaultDepthStencil);
            Assert.Equal(BatchBufferTarget.ConstantBuffer, gl.BatchBufferTarget);
        }

        [Fact]
        public void VulkanReversesDepthAndFlipsRenderTextures()
        {
            var vulkan = GraphicsDeviceProfile.Vulkan;
            // Shadow casters' clip-space bias (lighting shadow_globals_*: half, negated).
            Assert.Equal(-0.5f, vulkan.ClipDepthSlope);
            Assert.Equal(1f, GraphicsDeviceProfile.OpenGLCore.ClipDepthSlope);
            var p = vulkan.GpuProjection(s_Perspective, false);
            Assert.Equal(0.000300049782f, p.m22);
            Assert.Equal(0.300090015f, p.m23);
            Assert.Equal(1.73205078f, p.m11);
            Assert.Equal(-1.73205078f, vulkan.GpuProjection(s_Perspective, true).m11);
            var o = vulkan.GpuProjection(s_Ortho, true);
            Assert.Equal(0.1f, o.m22);
            Assert.Equal(1.1f, o.m23);
            Assert.Equal(-0.333333343f, o.m11);
            Assert.True(vulkan.ReversedZ && vulkan.UVStartsAtTop && vulkan.UsesLoadStoreActions);
            Assert.Equal(GraphicsFormat.D32_SFloat_S8_UInt, vulkan.DefaultDepthStencil);
            Assert.Equal(BatchBufferTarget.RawBuffer, vulkan.BatchBufferTarget);
            Assert.Equal(64, vulkan.ConstantBufferOffsetAlignment);
            // The devices' format tables differ (D24S8: 14352 on Vulkan, 14384 on OpenGL Core).
            Assert.Equal(14352, vulkan.Caps.Usages[(int)GraphicsFormat.D24_UNorm_S8_UInt]);
            Assert.NotEqual(vulkan.Caps.Usages[(int)GraphicsFormat.D24_UNorm_S8_UInt], GraphicsDeviceProfile.OpenGLCore.Caps.Usages[(int)GraphicsFormat.D24_UNorm_S8_UInt]);
        }

        [Fact]
        public void HostNamesNameTheProfiles()
        {
            Assert.Same(GraphicsDeviceProfile.Vulkan, GraphicsDeviceProfile.Named("Vulkan"));
            Assert.Same(GraphicsDeviceProfile.OpenGLCore, GraphicsDeviceProfile.Named(""));
            Assert.Same(GraphicsDeviceProfile.OpenGLCore, GraphicsDeviceProfile.Named("OpenGLCore"));
            Assert.Throws<System.ArgumentException>(() => GraphicsDeviceProfile.Named("Direct3D11"));
        }
    }
}
