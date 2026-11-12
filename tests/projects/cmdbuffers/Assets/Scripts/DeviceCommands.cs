using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    void MeshMultiplier(RenderTexture image)
    {
        var quad = Keep(new Mesh());
        quad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
        quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var mat = Keep(new Material(Resources.Load<Shader>("Instanced")) { enableInstancing = true });
        var ids = Keep(new Material(Resources.Load<Shader>("InstancedIds")) { enableInstancing = true });
        var matrices = Enumerable.Range(0, 4).Select(i => Matrix4x4.TRS(new Vector3(-1 + 0.5f * i, -1, 0), Quaternion.identity, new Vector3(0.5f, 2, 1))).ToArray();
        var colors = new[] { Rgb(200, 20, 40), Rgb(30, 180, 50), Rgb(40, 60, 210), Rgb(220, 200, 70) };
        var block = new MaterialPropertyBlock();
        block.SetVectorArray("_Color", colors);
        foreach (uint multiplier in new uint[] { 0, 1, 2, 3 })
        foreach (bool raw in new[] { false, true })
        {
            string key = "device.multiplier.mesh." + multiplier + (raw ? ".ids" : ".colors");
            using var cmd = new CommandBuffer();
            cmd.SetRenderTarget(image);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.SetInstanceMultiplier(multiplier);
            cmd.DrawMeshInstanced(quad, 0, raw ? ids : mat, 0, matrices, 2, raw ? null : block);
            Try(key, () => { Graphics.ExecuteCommandBuffer(cmd); Value(key + ".ok", "true"); });
            Value(key + ".pixels", Read(image));
            using var reset = new CommandBuffer();
            reset.SetInstanceMultiplier(1);
            Graphics.ExecuteCommandBuffer(reset);
        }
        MeshMultiplierPlaced(image, quad, matrices);
    }

    /// <summary>Mesh instances placed by their raw instance IDs under a multiplier: which IDs a
    /// call of two instances (and of one) draws, and what the added instances read of the call's
    /// instanced colors (no earlier draw of this section wrote past two instances). Through a
    /// command buffer and through a camera's queued draws of the persistent multiplier.</summary>
    void MeshMultiplierPlaced(RenderTexture image, Mesh quad, Matrix4x4[] matrices)
    {
        Material Placed(float mode)
        {
            var m = Keep(new Material(Resources.Load<Shader>("MeshInstancePlace")) { enableInstancing = true });
            m.SetFloat("_Mode", mode);
            return m;
        }
        var byColor = Placed(0);
        var byId = Placed(1);
        var two = new MaterialPropertyBlock();
        two.SetVectorArray("_Color", new[] { Rgb(10, 100, 200), Rgb(150, 50, 90) });
        void Execute(string key, uint multiplier, Material mat, MaterialPropertyBlock block, int count)
        {
            using var cmd = new CommandBuffer();
            cmd.SetRenderTarget(image);
            cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetInstanceMultiplier(multiplier);
            cmd.DrawMeshInstanced(quad, 0, mat, 0, matrices, count, block);
            Try(key, () => { Graphics.ExecuteCommandBuffer(cmd); Value(key + ".ok", "true"); });
            Value(key + ".pixels", Read(image));
            using var reset = new CommandBuffer();
            reset.SetInstanceMultiplier(1);
            Graphics.ExecuteCommandBuffer(reset);
        }
        foreach (uint multiplier in new uint[] { 1, 2, 3 })
            Execute("device.multiplier.placed.ids." + multiplier, multiplier, byId, null, 2);
        Execute("device.multiplier.placed.ids.one.3", 3, byId, null, 1);
        foreach (uint multiplier in new uint[] { 2, 3 })
            Execute("device.multiplier.placed.colors." + multiplier, multiplier, byColor, two, 2);
        var go = new GameObject("MultiplierCamera");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.targetTexture = image;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.transform.position = new Vector3(0, 0, -5);
        var bounds = new Bounds(Vector3.zero, Vector3.one * 100);
        foreach (uint multiplier in new uint[] { 1, 2 })
        {
            using (var set = new CommandBuffer()) { set.SetInstanceMultiplier(multiplier); Graphics.ExecuteCommandBuffer(set); }
            Graphics.RenderMeshInstanced(new RenderParams(byId) { camera = camera, worldBounds = bounds }, quad, 0, matrices, 2);
            camera.Render();
            Value("device.multiplier.camera.ids." + multiplier + ".pixels", Read(image));
            Graphics.RenderMeshInstanced(new RenderParams(byColor) { camera = camera, worldBounds = bounds, matProps = two }, quad, 0, matrices, 2);
            camera.Render();
            Value("device.multiplier.camera.colors." + multiplier + ".pixels", Read(image));
        }
        using (var set = new CommandBuffer()) { set.SetInstanceMultiplier(1); Graphics.ExecuteCommandBuffer(set); }
        UnityEngine.Object.DestroyImmediate(go);
    }

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
        MeshMultiplier(image);
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
