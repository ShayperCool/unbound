using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

static class MeasureCameraMemory
{
    [Serializable] sealed class Row { public string k, v; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("CAMERA_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var rows = new List<string>();
        void Put(string key, string value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));
        void Read(string stage, Camera camera) => Put(stage, FormattableString.Invariant(
            $"{Profiler.GetRuntimeMemorySizeLong(camera)}/{camera.commandBufferCount}"));

        Put("unity", Application.unityVersion);
        Put("device", SystemInfo.graphicsDeviceType.ToString());
        var camera = new GameObject("Camera allocation control").AddComponent<Camera>();
        Read("camera.initial", camera);
        camera.enabled = false;
        camera.orthographic = true;
        camera.cullingMask = 0;
        camera.layerCullDistances = new float[32];
        float[] distances = camera.layerCullDistances;
        for (int layer = 0; layer < distances.Length; layer++)
            distances[layer] = 10 + layer;
        camera.layerCullDistances = distances;
        Read("camera.cullingAndProjection", camera);
        camera.usePhysicalProperties = true;
        camera.focalLength = 35f;
        camera.SetStereoViewMatrix(Camera.StereoscopicEye.Left, Matrix4x4.TRS(Vector3.left, Quaternion.identity, Vector3.one));
        camera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, Matrix4x4.Ortho(-1f, 1f, -1f, 1f, 0.1f, 10f));
        Read("camera.stereoPhysical", camera);
        camera.ResetStereoProjectionMatrices();
        camera.ResetStereoViewMatrices();
        camera.ResetCullingMatrix();
        camera.ResetProjectionMatrix();
        camera.SetReplacementShader(Shader.Find("Unlit/Color"), "RenderType");
        Read("camera.replacementShader", camera);
        camera.ResetReplacementShader();
        var target = new RenderTexture(32, 16, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        Read("camera.target", camera);
        camera.Render();
        Read("camera.renderedTarget", camera);
        camera.targetTexture = null;
        target.Release();
        Object.DestroyImmediate(target);
        Read("camera.removedTarget", camera);

        var first = new CommandBuffer { name = "First reused buffer" };
        var second = new CommandBuffer { name = "Second buffer" };
        first.SetGlobalFloat("_ProbeValue", 1f);
        second.SetGlobalFloat("_ProbeValue", 2f);
        for (int index = 1; index <= 9; index++)
        {
            camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, first);
            Read("commands.oneEvent." + index, camera);
        }
        camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, second);
        camera.AddCommandBuffer(CameraEvent.AfterEverything, second);
        Read("commands.moreEvents", camera);
        var clone = Object.Instantiate(camera);
        Read("commands.clone", clone);
        Object.DestroyImmediate(clone.gameObject);
        var other = new GameObject("Copy target").AddComponent<Camera>();
        other.CopyFrom(camera);
        Read("commands.copyFrom", other);
        Object.DestroyImmediate(other.gameObject);
        camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, first);
        Read("commands.removeBuffer", camera);
        camera.RemoveCommandBuffers(CameraEvent.AfterForwardOpaque);
        Read("commands.removeEvent", camera);
        camera.RemoveAllCommandBuffers();
        Read("commands.removeAll", camera);

        const CameraEvent orderedEvent = CameraEvent.BeforeImageEffects;
        camera.AddCommandBuffer(orderedEvent, first);
        camera.AddCommandBuffer(orderedEvent, second);
        camera.AddCommandBuffer(orderedEvent, first);
        camera.AddCommandBuffer(orderedEvent, second);
        camera.AddCommandBuffer(orderedEvent, first);
        Read("mixed.ordered", camera);
        camera.RemoveCommandBuffer(orderedEvent, first);
        Read("mixed.removedAllMatches", camera);
        var remaining = camera.GetCommandBuffers(orderedEvent);
        Put("mixed.remainingBuffers", remaining.Length == 2 ? remaining[0].name + "/" + remaining[1].name : "count:" + remaining.Length);

        var warmCopyTarget = new GameObject("Populated copy target").AddComponent<Camera>();
        warmCopyTarget.AddCommandBuffer(CameraEvent.AfterEverything, first);
        Read("commands.warmCopyTarget", warmCopyTarget);
        warmCopyTarget.CopyFrom(camera);
        Read("commands.warmCopyFrom", warmCopyTarget);
        var own = warmCopyTarget.GetCommandBuffers(CameraEvent.AfterEverything);
        Put("commands.warmCopyDestinationBuffer", own.Length == 1 ? own[0].name : "count:" + own.Length);
        Object.DestroyImmediate(warmCopyTarget.gameObject);

        var compute = new CommandBuffer { name = "Async empty buffer" };
        compute.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
        camera.AddCommandBufferAsync(CameraEvent.AfterEverything, compute, ComputeQueueType.Default);
        Read("commands.addAsync", camera);
        camera.RemoveCommandBuffer(CameraEvent.AfterEverything, compute);
        Read("commands.removeAsync", camera);
        camera.RemoveAllCommandBuffers();
        compute.Release();
        camera.AddCommandBuffer(CameraEvent.AfterEverything, first);
        Read("pipeline.beforeSwitch", camera);
        var pipeline = ScriptableObject.CreateInstance<RendererMemoryPipeline>();
        var previousDefault = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        Camera createdUnderPipeline = null;
        try
        {
            QualitySettings.renderPipeline = null;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var request = new RendererMemoryPipeline.FrameRequest();
            bool supported = RenderPipeline.SupportsRenderRequest(camera, request);
            if (supported) RenderPipeline.SubmitRenderRequest(camera, request);
            Put("pipeline.actualFrame", (supported && pipeline.RenderedFrame).ToString());
            Read("pipeline.existing", camera);
            createdUnderPipeline = new GameObject("Camera in a scriptable pipeline").AddComponent<Camera>();
            Read("pipeline.new", createdUnderPipeline);
            try { createdUnderPipeline.AddCommandBuffer(CameraEvent.AfterEverything, second); }
            catch (Exception error) { Put("pipeline.registerException", error.GetType().Name); }
            Read("pipeline.afterRegister", createdUnderPipeline);
            try { camera.RemoveCommandBuffer(CameraEvent.AfterEverything, first); }
            catch (Exception error) { Put("pipeline.removeException", error.GetType().Name); }
            Read("pipeline.afterRemove", camera);
        }
        finally
        {
            QualitySettings.renderPipeline = previousQuality;
            GraphicsSettings.defaultRenderPipeline = previousDefault;
        }
        camera.Render();
        Read("pipeline.restoredOldCamera", camera);
        if (createdUnderPipeline != null)
        {
            Read("pipeline.restoredNewCamera", createdUnderPipeline);
            Object.DestroyImmediate(createdUnderPipeline.gameObject);
        }
        Object.DestroyImmediate(pipeline);
        first.Release();
        second.Release();
        Object.DestroyImmediate(camera.gameObject);

        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
