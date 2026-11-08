using System;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    public class CommandBufferDevicesTests
    {
        [Fact]
        public void MeshExpansionRejectsNontrivialInstanceMultipliers()
        {
            try
            {
                GfxStream.InstanceMultiplier(2);
                Assert.Equal("Unbound: instance multipliers on CPU-expanded mesh instances are not supported.",
                    Assert.Throws<NotSupportedException>(() => GfxStream.MeshInstance(0)).Message);
                GfxStream.InstanceMultiplier(0);
                GfxStream.MeshInstance(1);
            }
            finally { GfxStream.InstanceMultiplier(1); }
        }

        [Fact]
        public void MissingDeviceCapabilitiesStayConsistent()
        {
            Assert.False(SystemInfo.supportsRayTracing);
            Assert.False(SystemInfo.supportsRayTracingShaders);
            Assert.False(SystemInfo.supportsInlineRayTracing);
            Assert.Equal(FoveatedRenderingCaps.None, SystemInfo.foveatedRenderingCaps);
        }

        [Fact]
        public void LateLatchChecksTheProjectionArrayWithoutAnXrDevice()
        {
            using var commands = new CommandBuffer();
            Assert.Equal("projectionMat", Assert.Throws<ArgumentNullException>(() => commands.SetLateLatchProjectionMatrices(null)).ParamName);
            foreach (int count in new[] { 0, 1, 3, 4 })
                Assert.Equal("Stereo Projection Matrix must be of size 2.",
                    Assert.Throws<ArgumentException>(() => commands.SetLateLatchProjectionMatrices(new Matrix4x4[count])).Message);
            commands.SetLateLatchProjectionMatrices(new[] { Matrix4x4.identity, Matrix4x4.identity });
            commands.SetFoveatedRenderingMode(FoveatedRenderingMode.Disabled);
            commands.SetSinglePassStereo(SinglePassStereoMode.None);
            commands.MarkLateLatchMatrixShaderPropertyID(CameraLateLatchMatrixType.View, 0);
            commands.UnmarkLateLatchMatrix(CameraLateLatchMatrixType.View);
            commands.DrawOcclusionMesh(new RectInt(0, 0, 1, 1));
        }

        [Fact]
        public void RayTracingChecksRequiredObjectsBeforeIgnoringDeviceCommands()
        {
            using var commands = new CommandBuffer();
            var shader = RayTracingShader.Create("Tests/NoRayDevice");
            Assert.Equal("rayTracingShader", Assert.Throws<ArgumentNullException>(() => commands.SetRayTracingFloatParam(null, 0, 1)).ParamName);
            Assert.Equal("rayTracingShader", Assert.Throws<ArgumentNullException>(() => commands.DispatchRays(null, null, (GraphicsBuffer)null, 0)).ParamName);
            Assert.Equal("argsBuffer", Assert.Throws<ArgumentNullException>(() => commands.DispatchRays(shader, null, (GraphicsBuffer)null, 0)).ParamName);
            Assert.Equal("accelerationStructure", Assert.Throws<ArgumentNullException>(() => commands.BuildRayTracingAccelerationStructure(null)).ParamName);
            Assert.Equal("computeShader", Assert.Throws<ArgumentNullException>(() => commands.SetRayTracingAccelerationStructure((ComputeShader)null, 0, 0, null)).ParamName);
            Assert.Equal("accelerationStructure", Assert.Throws<ArgumentNullException>(() => commands.SetRayTracingAccelerationStructure(shader, 0, null)).ParamName);
            // Optional values are ignored on the device too, including arrays and buffers.
            commands.SetRayTracingFloatParams(shader, 0, null);
            commands.SetRayTracingBufferParam(shader, 0, (ComputeBuffer)null);
            commands.SetGlobalRayTracingAccelerationStructure(0, null);
            Assert.Equal("buffer", Assert.Throws<ArgumentNullException>(() =>
                commands.SetRayTracingConstantBufferParam(shader, 0, (ComputeBuffer)null, 0, 16)).ParamName);
            UnityEngine.Object.DestroyImmediate(shader);
        }

        [Fact]
        public void RenderingPluginsFailClearlyUntilDeviceInteropExists()
        {
            using var commands = new CommandBuffer();
            foreach (Action call in new Action[] {
                () => commands.IssuePluginCustomBlit(IntPtr.Zero, 0, default, default, 0, 0),
                () => commands.IssuePluginCustomTextureUpdate(IntPtr.Zero, null, 0),
                () => commands.IssuePluginCustomTextureUpdateV1(IntPtr.Zero, null, 0),
                () => commands.IssuePluginCustomTextureUpdateV2(IntPtr.Zero, null, 0),
            }) Assert.Equal("Unbound: native rendering plugin callbacks require graphics-device interop, which is not supported.",
                Assert.Throws<NotSupportedException>(call).Message);
        }
    }
}
