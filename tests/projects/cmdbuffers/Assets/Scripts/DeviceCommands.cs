using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    void DeviceCommands()
    {
        Value("device.rayTracing", B(SystemInfo.supportsRayTracing));
        Value("device.rayTracingShaders", B(SystemInfo.supportsRayTracingShaders));
        Value("device.inlineRayTracing", B(SystemInfo.supportsInlineRayTracing));
        Value("device.foveation", ((int)SystemInfo.foveatedRenderingCaps).ToString());
        var image = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        image.Create();
        var material = Keep(new Material(Resources.Load<Shader>("ProceduralGrid")));
        material.SetFloat("_Tag", 17);
        void Case(string key, Action<CommandBuffer> record, int instances = 1)
        {
            using var cmd = new CommandBuffer { name = "probe.device" };
            cmd.SetRenderTarget(image);
            cmd.ClearRenderTarget(false, true, Color.clear);
            Try(key + ".record", () => { record(cmd); Value(key + ".record.ok", "true"); });
            cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 6, instances);
            Try(key + ".execute", () => { Graphics.ExecuteCommandBuffer(cmd); Value(key + ".execute.ok", "true"); });
            Value(key + ".pixels", Read(image));
            using var reset = new CommandBuffer();
            reset.SetInstanceMultiplier(1);
            reset.SetSinglePassStereo(SinglePassStereoMode.None);
            Graphics.ExecuteCommandBuffer(reset);
        }
        foreach (int mode in new[] { 0, 1, 2, 3, -1 })
        {
            Case("device.foveation." + mode, cmd => cmd.SetFoveatedRenderingMode((FoveatedRenderingMode)mode));
            Case("device.stereo." + mode, cmd => cmd.SetSinglePassStereo((SinglePassStereoMode)mode));
            Case("device.late.mark." + mode, cmd => cmd.MarkLateLatchMatrixShaderPropertyID((CameraLateLatchMatrixType)mode, Shader.PropertyToID("_LateMatrix")));
            Case("device.late.unmark." + mode, cmd => cmd.UnmarkLateLatchMatrix((CameraLateLatchMatrixType)mode));
        }
        Case("device.configure", cmd => cmd.ConfigureFoveatedRendering(IntPtr.Zero));
        foreach (uint n in new uint[] { 0, 1, 2, 3 })
        {
            Case("device.multiplier." + n, cmd => cmd.SetInstanceMultiplier(n));
            Case("device.multiplier.twoInstances." + n, cmd => cmd.SetInstanceMultiplier(n), 2);
        }
        foreach (int n in new[] { -1, 0, 1, 2, 3 })
            Case("device.late.projection." + n, cmd => cmd.SetLateLatchProjectionMatrices(n < 0 ? null : Enumerable.Repeat(Matrix4x4.identity, n).ToArray()));
        Case("device.occlusion", cmd => cmd.DrawOcclusionMesh(new RectInt(0, 0, 8, 8)));
        Case("device.occlusion.empty", cmd => cmd.DrawOcclusionMesh(new RectInt()));

        RayTracingAccelerationStructure acceleration = null;
        Try("device.ray.create", () => { acceleration = new RayTracingAccelerationStructure(); Value("device.ray.created", B(acceleration != null)); });
        var rayShader = Resources.Load<RayTracingShader>("AbsentDevice");
        Value("device.ray.shaderLoaded", B(rayShader != null));
#if UNITY_EDITOR
        if (rayShader != null && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rayShader, out string ignoredGuid, out long fileId))
            Value("device.ray.shaderFileId", fileId.ToString());
#endif
#if UNITY_EDITOR
        var secondRayShader = Resources.Load<RayTracingShader>("SecondDevice");
        if (secondRayShader != null && UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(secondRayShader, out string ignoredGuid2, out long fileId2))
            Value("device.ray.secondShaderFileId", fileId2.ToString());
#endif
        var compute = Resources.Load<ComputeShader>("IndexedFill");
        var computeBuffer = new ComputeBuffer(4, 4);
        var graphicsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, 4);
        disposables.Add(computeBuffer);
        disposables.Add(graphicsBuffer);
        // Every ray tracing overload is observed even though no ray tracing device is available.
        var names = new[] { "BuildRayTracingAccelerationStructure", "DispatchRays", "SetRayTracing", "SetGlobalRayTracingAccelerationStructure" };
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (var method in typeof(CommandBuffer).GetMethods().Where(m => names.Any(n => m.Name.StartsWith(n, StringComparison.Ordinal)))
            .OrderBy(m => m.Name + string.Join(".", m.GetParameters().Select(p => p.ParameterType.Name)), StringComparer.Ordinal))
        {
            bool valid = mode > 0;
            bool missingValue = mode == 2;
            string key = "device.ray." + (mode == 0 ? "null." : mode == 1 ? "objects." : "missing.") + method.Name + "." + string.Join("-", method.GetParameters().Select(p => p.ParameterType.Name));
            if (missingValue && method.Name == "SetRayTracingConstantBufferParam") continue;
            object Arg(ParameterInfo p)
            {
                Type t = p.ParameterType;
                if (valid)
                {
                    if (t == typeof(RayTracingShader)) return rayShader;
                    if (t == typeof(RayTracingAccelerationStructure)) return missingValue ? null : acceleration;
                    if (t == typeof(ComputeShader)) return compute;
                    if (t == typeof(ComputeBuffer)) return missingValue ? null : computeBuffer;
                    if (t == typeof(GraphicsBuffer)) return missingValue ? null : graphicsBuffer;
                    if (t == typeof(GraphicsBufferHandle)) return graphicsBuffer.bufferHandle;
                }
                if (missingValue && !t.IsValueType) return null;
                if (t == typeof(string)) return p.Name == "rayGenName" ? "RayGen" : "_RayParam";
                if (t == typeof(float[])) return new float[] { 1, 2 };
                if (t == typeof(int[])) return new int[] { 1, 2 };
                if (t == typeof(Vector4[])) return new[] { Vector4.one };
                if (t == typeof(Matrix4x4[])) return new[] { Matrix4x4.identity };
                if (t == typeof(Matrix4x4)) return Matrix4x4.identity;
                if (t == typeof(uint)) return 1u;
                return t.IsValueType ? Activator.CreateInstance(t) : null;
            }
            object[] args = method.GetParameters().Select(Arg).ToArray();
            Case(key, cmd =>
            {
                try { method.Invoke(cmd, args); }
                catch (TargetInvocationException e) { throw e.InnerException; }
            });
        }
        // Multipliers persist across command buffers and ignore indirect draws.
        using (var set = new CommandBuffer()) { set.SetInstanceMultiplier(3); Graphics.ExecuteCommandBuffer(set); }
        using (var next = new CommandBuffer())
        {
            next.SetRenderTarget(image); next.ClearRenderTarget(false, true, Color.clear);
            next.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 6, 1);
            Graphics.ExecuteCommandBuffer(next);
            Value("device.multiplier.crossBuffer", Read(image));
        }
        var indirect = new ComputeBuffer(4, 4, ComputeBufferType.IndirectArguments);
        indirect.SetData(new uint[] { 6, 1, 0, 0 }); disposables.Add(indirect);
        using (var next = new CommandBuffer())
        {
            next.SetRenderTarget(image); next.ClearRenderTarget(false, true, Color.clear);
            next.DrawProceduralIndirect(Matrix4x4.identity, material, 0, MeshTopology.Triangles, indirect);
            next.SetInstanceMultiplier(1);
            Graphics.ExecuteCommandBuffer(next);
            Value("device.multiplier.indirect", Read(image));
        }
        acceleration?.Dispose();
    }
}
