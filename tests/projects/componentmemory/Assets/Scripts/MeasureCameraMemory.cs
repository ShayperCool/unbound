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

    sealed class CommandLog : ILogHandler
    {
        internal readonly List<string> Entries = new();

        public void LogFormat(LogType type, Object context, string format, params object[] args)
            => Entries.Add($"{type}: {string.Format(format, args)}");

        public void LogException(Exception exception, Object context)
            => Entries.Add(exception.GetType().Name);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("CAMERA_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var rows = new List<string>();
        void Put(string key, string value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));
        void Read(string stage, Camera camera) => Put(stage, FormattableString.Invariant(
            $"{Profiler.GetRuntimeMemorySizeLong(camera)}/{camera.commandBufferCount}"));
        void CheckCall(string stage, Action call)
        {
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var log = new CommandLog();
            logger.logHandler = log;
            string failure = "";
            try { call(); }
            catch (Exception error) { failure = $"{error.GetType().Name}: {error.Message}"; }
            finally { logger.logHandler = previous; }
            Put(stage, $"{string.Join("|", log.Entries)}|{failure}");
        }

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
        var third = new CommandBuffer { name = "Third buffer" };
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
        camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, third);
        Read("commands.reuseReservedEvent", camera);
        camera.RemoveCommandBuffers(CameraEvent.AfterForwardOpaque);
        Read("commands.removeEvent", camera);
        camera.RemoveAllCommandBuffers();
        Read("commands.removeAll", camera);

        const CameraEvent orderedEvent = CameraEvent.BeforeImageEffects;
        camera.AddCommandBuffer(orderedEvent, first);
        camera.AddCommandBuffer(orderedEvent, second);
        camera.AddCommandBuffer(orderedEvent, first);
        camera.AddCommandBuffer(orderedEvent, third);
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
            CheckCall("pipeline.configuredAdd", () => camera.AddCommandBuffer(CameraEvent.AfterEverything, second));
            Read("pipeline.afterConfiguredAdd", camera);
            var request = new RendererMemoryPipeline.FrameRequest();
            bool supported = RenderPipeline.SupportsRenderRequest(camera, request);
            if (supported) RenderPipeline.SubmitRenderRequest(camera, request);
            Put("pipeline.actualFrame", (supported && pipeline.RenderedFrame).ToString());
            Read("pipeline.existing", camera);
            createdUnderPipeline = new GameObject("Camera in a scriptable pipeline").AddComponent<Camera>();
            Read("pipeline.new", createdUnderPipeline);
            CheckCall("pipeline.add", () => createdUnderPipeline.AddCommandBuffer(CameraEvent.AfterEverything, second));
            Read("pipeline.afterRegister", createdUnderPipeline);
            CheckCall("pipeline.remove", () => camera.RemoveCommandBuffer(CameraEvent.AfterEverything, first));
            Read("pipeline.afterRemove", camera);
            CheckCall("pipeline.addAsync", () => camera.AddCommandBufferAsync(CameraEvent.AfterEverything, compute, ComputeQueueType.Default));
            CheckCall("pipeline.get", () =>
            {
                var buffers = camera.GetCommandBuffers(CameraEvent.AfterEverything);
                Put("pipeline.getRetained", string.Join("/", Array.ConvertAll(buffers, buffer => buffer.name)));
            });
            CheckCall("pipeline.removeEvent", () => camera.RemoveCommandBuffers(CameraEvent.AfterEverything));
            CheckCall("pipeline.removeAll", camera.RemoveAllCommandBuffers);
            CheckCall("pipeline.addNull", () => camera.AddCommandBuffer(CameraEvent.AfterEverything, null));
            CheckCall("pipeline.addAsyncNull", () => camera.AddCommandBufferAsync(CameraEvent.AfterEverything, null, ComputeQueueType.Default));
            CheckCall("pipeline.removeNull", () => camera.RemoveCommandBuffer(CameraEvent.AfterEverything, null));
            Read("pipeline.afterAllCalls", camera);
        }
        finally
        {
            QualitySettings.renderPipeline = previousQuality;
            GraphicsSettings.defaultRenderPipeline = previousDefault;
        }
        camera.Render();
        Read("pipeline.restoredOldCamera", camera);
        CheckCall("commands.builtInRemoveNull", () => camera.RemoveCommandBuffer(CameraEvent.AfterEverything, null));
        Read("commands.afterRemoveNull", camera);
        if (createdUnderPipeline != null)
        {
            Read("pipeline.restoredNewCamera", createdUnderPipeline);
            Object.DestroyImmediate(createdUnderPipeline.gameObject);
        }
        Object.DestroyImmediate(pipeline);
        compute.Release();
        first.Release();
        second.Release();
        third.Release();
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
