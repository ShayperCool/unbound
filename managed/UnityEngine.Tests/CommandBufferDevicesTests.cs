using System;
using System.Linq;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Xunit;

namespace Unbound.Tests
{
    public class CommandBufferDevicesTests
    {
        /// <summary>A command buffer's mesh instances under an instance multiplier record each
        /// instance with the call's count, whatever the multiplier (the renderer adds the
        /// multiplied instances): nothing is refused.</summary>
        [Fact]
        public void MeshExpansionUnderInstanceMultipliersRecordsTheCallsCount()
        {
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset("Shader \"Tests/MultipliedInstances\" { SubShader { Pass {} } }");
            var material = new Material(shader) { enableInstancing = true };
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.triangles = new[] { 0, 1, 2 };
            try
            {
                using var cmd = new CommandBuffer();
                cmd.SetInstanceMultiplier(3);
                cmd.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity, Matrix4x4.identity }, 2);
                cmd.SetInstanceMultiplier(1);
                GfxStream.Flush();
                Graphics.ExecuteCommandBuffer(cmd);
                var bytes = GfxStream.Recorded.ToArray();
                GfxStream.Flush();
                for (uint index = 0; index < 2; index++)
                {
                    var instance = new byte[] { 30 }.Concat(BitConverter.GetBytes(index)).Concat(BitConverter.GetBytes(2u)).ToArray();
                    Assert.Equal(1, Occurrences(bytes, instance));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(shader);
            }
        }

        static int Occurrences(byte[] bytes, byte[] part)
        {
            int n = 0;
            for (int at = 0; at + part.Length <= bytes.Length; at++)
                if (bytes.AsSpan(at, part.Length).SequenceEqual(part)) n++;
            return n;
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
    }
}
