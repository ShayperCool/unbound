using System;
using Unbound.Runtime;

namespace UnityEngine.Rendering
{
    public enum CameraLateLatchMatrixType { View, InverseView, ViewProjection, InverseViewProjection }
    [Flags]
    public enum FoveatedRenderingCaps { None = 0, FoveationImage = 1, NonUniformRaster = 2, ModeChangeOnlyBeforeRenderTargetSet = 4 }
    [Flags]
    public enum CustomMarkerCallbackFlags { CustomMarkerCallbackDefault = 0, CustomMarkerCallbackForceInvalidateStateTracking = 4 }

    public partial class CommandBuffer
    {
        // No XR or ray tracing device is exposed by the renderer. The argument checks and
        // implemented ignored modes match Unity 6000.0.71f1 on OpenGL Core (cmdbuffers device.*).
        [Unbound.EditorFaithful("No XR device: measured by cmdbuffers device.* on Unity 6000.0.71f1.")]
        public void SetFoveatedRenderingMode(FoveatedRenderingMode foveatedRenderingMode) { }
        [Unbound.EditorFaithful("No XR device: measured by cmdbuffers device.* on Unity 6000.0.71f1.")]
        public void ConfigureFoveatedRendering(IntPtr platformData) { }
        public void SetSinglePassStereo(SinglePassStereoMode mode)
        {
            if (mode == SinglePassStereoMode.SideBySide)
                throw new NotSupportedException("Unbound: side-by-side stereo clipping without an XR device is not supported.");
        }
        [Unbound.EditorFaithful("No XR device: measured by cmdbuffers device.* on Unity 6000.0.71f1.")]
        public void MarkLateLatchMatrixShaderPropertyID(CameraLateLatchMatrixType matrixPropertyType, int shaderPropertyID) { }
        [Unbound.EditorFaithful("No XR device: measured by cmdbuffers device.* on Unity 6000.0.71f1.")]
        public void UnmarkLateLatchMatrix(CameraLateLatchMatrixType matrixPropertyType) { }
        [Unbound.EditorFaithful("No XR device: measured by cmdbuffers device.* on Unity 6000.0.71f1.")]
        public void DrawOcclusionMesh(RectInt normalizedCamViewport) { }
        public void SetLateLatchProjectionMatrices(Matrix4x4[] projectionMat)
        {
            if (projectionMat == null) throw new ArgumentNullException(nameof(projectionMat));
            if (projectionMat.Length != 2) throw new ArgumentException("Stereo Projection Matrix must be of size 2.");
        }
        static void RequireRayShader(RayTracingShader rayTracingShader)
        {
            if (rayTracingShader == null) throw new ArgumentNullException(nameof(rayTracingShader));
        }
        static void RequireAcceleration(RayTracingAccelerationStructure accelerationStructure)
        {
            if (accelerationStructure == null) throw new ArgumentNullException(nameof(accelerationStructure));
        }
        public void SetRayTracingShaderPass(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string passName) { RequireRayShader(rayTracingShader); }
        public void BuildRayTracingAccelerationStructure(global::UnityEngine.Rendering.RayTracingAccelerationStructure accelerationStructure) { RequireAcceleration(accelerationStructure); }
        public void BuildRayTracingAccelerationStructure(global::UnityEngine.Rendering.RayTracingAccelerationStructure accelerationStructure, global::UnityEngine.Vector3 relativeOrigin) { RequireAcceleration(accelerationStructure); }
        public void BuildRayTracingAccelerationStructure(global::UnityEngine.Rendering.RayTracingAccelerationStructure accelerationStructure, global::UnityEngine.Rendering.RayTracingAccelerationStructure.BuildSettings buildSettings) { RequireAcceleration(accelerationStructure); }
        public void SetRayTracingAccelerationStructure(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.Rendering.RayTracingAccelerationStructure rayTracingAccelerationStructure) { RequireRayShader(rayTracingShader); RequireAcceleration(rayTracingAccelerationStructure); }
        public void SetRayTracingAccelerationStructure(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.Rendering.RayTracingAccelerationStructure rayTracingAccelerationStructure) { RequireRayShader(rayTracingShader); RequireAcceleration(rayTracingAccelerationStructure); }
        public void SetRayTracingAccelerationStructure(global::UnityEngine.ComputeShader computeShader, int kernelIndex, string name, global::UnityEngine.Rendering.RayTracingAccelerationStructure rayTracingAccelerationStructure) { if (computeShader == null) throw new ArgumentNullException(nameof(computeShader)); RequireAcceleration(rayTracingAccelerationStructure); }
        public void SetRayTracingAccelerationStructure(global::UnityEngine.ComputeShader computeShader, int kernelIndex, int nameID, global::UnityEngine.Rendering.RayTracingAccelerationStructure rayTracingAccelerationStructure) { if (computeShader == null) throw new ArgumentNullException(nameof(computeShader)); RequireAcceleration(rayTracingAccelerationStructure); }
        public void SetRayTracingBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.ComputeBuffer buffer) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.ComputeBuffer buffer) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.GraphicsBuffer buffer) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.GraphicsBuffer buffer) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.GraphicsBufferHandle bufferHandle) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.GraphicsBufferHandle bufferHandle) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingConstantBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.ComputeBuffer buffer, int offset, int size) { RequireRayShader(rayTracingShader); if (buffer == null) throw new ArgumentNullException(nameof(buffer)); }
        public void SetRayTracingConstantBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.ComputeBuffer buffer, int offset, int size) { RequireRayShader(rayTracingShader); if (buffer == null) throw new ArgumentNullException(nameof(buffer)); }
        public void SetRayTracingConstantBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.GraphicsBuffer buffer, int offset, int size) { RequireRayShader(rayTracingShader); if (buffer == null) throw new ArgumentNullException(nameof(buffer)); }
        public void SetRayTracingConstantBufferParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.GraphicsBuffer buffer, int offset, int size) { RequireRayShader(rayTracingShader); if (buffer == null) throw new ArgumentNullException(nameof(buffer)); }
        public void SetRayTracingTextureParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.Rendering.RenderTargetIdentifier rt) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingTextureParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.Rendering.RenderTargetIdentifier rt) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingFloatParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, float val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingFloatParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, float val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingFloatParams(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, params float[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingFloatParams(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, params float[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingIntParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, int val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingIntParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, int val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingIntParams(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, params int[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingIntParams(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, params int[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingVectorParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.Vector4 val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingVectorParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.Vector4 val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingVectorArrayParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, params global::UnityEngine.Vector4[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingVectorArrayParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, params global::UnityEngine.Vector4[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingMatrixParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, global::UnityEngine.Matrix4x4 val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingMatrixParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, global::UnityEngine.Matrix4x4 val) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingMatrixArrayParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string name, params global::UnityEngine.Matrix4x4[] values) { RequireRayShader(rayTracingShader); }
        public void SetRayTracingMatrixArrayParam(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, int nameID, params global::UnityEngine.Matrix4x4[] values) { RequireRayShader(rayTracingShader); }
        public void DispatchRays(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string rayGenName, uint width, uint height, uint depth, global::UnityEngine.Camera camera = null) { RequireRayShader(rayTracingShader); }
        public void DispatchRays(global::UnityEngine.Rendering.RayTracingShader rayTracingShader, string rayGenName, global::UnityEngine.GraphicsBuffer argsBuffer, uint argsOffset, global::UnityEngine.Camera camera = null) { RequireRayShader(rayTracingShader); if (argsBuffer == null) throw new ArgumentNullException(nameof(argsBuffer)); }
        [Unbound.EditorFaithful("No ray tracing device: measured ignored for null and non-null structures by cmdbuffers device.ray.* on Unity 6000.0.71f1 OpenGL Core.")]
        public void SetGlobalRayTracingAccelerationStructure(string name, global::UnityEngine.Rendering.RayTracingAccelerationStructure accelerationStructure) {  }
        [Unbound.EditorFaithful("No ray tracing device: measured ignored for null and non-null structures by cmdbuffers device.ray.* on Unity 6000.0.71f1 OpenGL Core.")]
        public void SetGlobalRayTracingAccelerationStructure(int nameID, global::UnityEngine.Rendering.RayTracingAccelerationStructure accelerationStructure) {  }
    }

    public sealed partial class RayTracingAccelerationStructure : IDisposable
    {
        public RayTracingAccelerationStructure() => Debug.LogAssertion("Platform doesn't implement RayTracingAccelerationStructure.");
        [Unbound.EditorFaithful("The unsupported device created no resource to release.")]
        public void Dispose() { }
        [Unbound.EditorFaithful("The unsupported device created no resource to release.")]
        public void Release() { }
        public partial struct BuildSettings
        {
            public BuildSettings() { }
        }
    }
}
