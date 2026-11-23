using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

sealed class RendererMemoryPipeline : RenderPipelineAsset
{
    internal sealed class FrameRequest { }

    sealed class EmptyPipeline : RenderPipeline
    {
        internal bool RenderedCamera;

        protected override bool IsRenderRequestSupported<RequestData>(Camera camera, RequestData request) => request is FrameRequest;

        protected override void ProcessRenderRequests<RequestData>(ScriptableRenderContext context, Camera camera, RequestData request)
        {
            if (request is FrameRequest) Render(context, new[] { camera });
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            context.Submit();
            RenderedCamera |= cameras.Length != 0;
        }
    }

    EmptyPipeline m_Instance;
    internal bool RenderedFrame => m_Instance != null && m_Instance.RenderedCamera
        && ReferenceEquals(RenderPipelineManager.currentPipeline, m_Instance);

    protected override RenderPipeline CreatePipeline() => m_Instance = new EmptyPipeline();
}

public sealed class MeasureMeshRendererMemory : MonoBehaviour
{
    [Serializable] sealed class Row { public string k, v; }

    sealed class LoggerCounter : ILogHandler
    {
        internal int Calls;
        public void LogException(Exception error, Object context) => Calls++;
        public void LogFormat(LogType type, Object context, string format, params object[] args) => Calls++;
    }
    static readonly List<string> Rows = new();
    const string Property = "_ProbeValue";

    static void Put(string key, string value) => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));

    static Material[] Materials(int count, Material material)
    {
        var materials = new Material[count];
        Array.Fill(materials, material);
        return materials;
    }

    static void Read(string stage, MeshRenderer renderer, int index = 0)
    {
        long before = Profiler.GetRuntimeMemorySizeLong(renderer);
        int length = renderer.sharedMaterials.Length;
        var shared = new MaterialPropertyBlock();
        var local = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(shared);
        if (index < length) renderer.GetPropertyBlock(local, index);
        long after = Profiler.GetRuntimeMemorySizeLong(renderer);
        Put(stage, FormattableString.Invariant($"{length}/{before}/{after}/{shared.GetFloat(Property)}/{local.GetFloat(Property)}"));
    }

    static void MeasureMaterialSlots(Material material)
    {
        var renderer = new GameObject("Material slots").AddComponent<MeshRenderer>();
        Read("slots.fresh", renderer);
        foreach (int count in new[] { 0, 1, 2, 3, 5, 9, 17, 32 })
        {
            renderer.sharedMaterials = Materials(count, material);
            Read("slots.grow." + count, renderer);
        }
        renderer.sharedMaterials = Array.Empty<Material>();
        Read("slots.shrunk", renderer);
        var emptyClone = Object.Instantiate(renderer);
        Read("slots.emptyClone", emptyClone);
        Object.DestroyImmediate(emptyClone.gameObject);
        renderer.sharedMaterial = material;
        Read("slots.scalarAfterEmpty", renderer);
        renderer.SetSharedMaterials(new List<Material> { material, null, material, null });
        Read("slots.sharedList", renderer);
        var instance = renderer.material;
        Put("slots.instancedMaterial", (!ReferenceEquals(material, instance)).ToString());
        Read("slots.instanceMaterial", renderer);
        renderer.SetMaterials(new List<Material> { instance, material, null });
        Read("slots.instanceList", renderer);
        var clone = Object.Instantiate(renderer);
        Read("slots.shrunkClone", clone);
        Object.DestroyImmediate(clone.gameObject);

        var triangles = new[] { 0, 1, 2 };
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, subMeshCount = 3 };
        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) mesh.SetTriangles(triangles, submesh);
        renderer.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -3f);
        camera.orthographic = true;
        var target = new RenderTexture(64, 64, 24);
        camera.targetTexture = target;
        camera.Render();
        Read("slots.afterDraw", renderer);
        renderer.enabled = false;
        Read("slots.disabled", renderer);
        Object.DestroyImmediate(camera.gameObject);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(mesh);
        Object.DestroyImmediate(renderer.gameObject);
        Object.DestroyImmediate(instance);
    }

    static void MeasureAssignAndGetMaterials(Material original)
    {
        var renderer = new GameObject("Material API controls").AddComponent<MeshRenderer>();
        var first = new Material(original);
        var second = new Material(original);
        var list = new List<Material> { first, second, original, first };
        renderer.SetSharedMaterials(list);
        Read("assignment.sharedListGrowth", renderer);
        var shared = renderer.sharedMaterials;
        Put("assignment.sharedListIdentity", (shared[0] == first && shared[1] == second && shared[2] == original && shared[3] == first).ToString());
        list[0] = second;
        Put("assignment.sharedListOwnsArray", (renderer.sharedMaterial == first).ToString());

        renderer.SetMaterials(new List<Material> { second, first, original, second, first, original });
        Read("assignment.instanceListGrowth", renderer);
        shared = renderer.sharedMaterials;
        Put("assignment.instanceListIdentity", (shared[0] == second && shared[1] == first && shared[2] == original).ToString());
        var instances = renderer.materials;
        var owned = new HashSet<Material>(instances);
        Put("assignment.getCreatesInstances", (instances[0] != second && instances[1] != first && instances[2] != original).ToString());
        var instance = instances[0];
        instances[0] = first;
        Put("assignment.getReturnsCopy", (renderer.sharedMaterial == instance).ToString());

        renderer.materials = new[] { first, second, original, first, second, original, first, second, original };
        Read("assignment.arrayGrowth", renderer);
        shared = renderer.sharedMaterials;
        Put("assignment.arrayIdentity", (shared[0] == first && shared[1] == second && shared[2] == original).ToString());
        Object.DestroyImmediate(renderer.gameObject);
        foreach (var item in owned)
            if (item != original && item != first && item != second) Object.DestroyImmediate(item);
        Object.DestroyImmediate(first);
        Object.DestroyImmediate(second);
    }

    static void MeasureIndexedReservationLifetime(Material material)
    {
        var renderer = new GameObject("Indexed reservation lifetime").AddComponent<MeshRenderer>();
        var block = new MaterialPropertyBlock();
        block.SetFloat(Property, 3.75f);
        renderer.sharedMaterials = Materials(3, material);
        renderer.SetPropertyBlock(block, 1);
        Read("indexedLifetime.initial", renderer, 1);
        renderer.SetPropertyBlock(null, 1);
        Read("indexedLifetime.clearLast", renderer, 1);
        renderer.sharedMaterials = Materials(5, material);
        Read("indexedLifetime.growAfterClear", renderer, 4);
        renderer.sharedMaterials = Array.Empty<Material>();
        Read("indexedLifetime.zero", renderer);
        renderer.sharedMaterials = Materials(8, material);
        Read("indexedLifetime.growPastZero", renderer, 7);
        renderer.SetPropertyBlock(block, 7);
        Read("indexedLifetime.reseed", renderer, 7);
        Object.DestroyImmediate(renderer.gameObject);

        renderer = new GameObject("Late indexed reservation").AddComponent<MeshRenderer>();
        renderer.sharedMaterials = Materials(12, material);
        renderer.sharedMaterials = Materials(2, material);
        renderer.SetPropertyBlock(block, 1);
        Read("indexedLate.seedFromShrunkList", renderer, 1);
        renderer.sharedMaterials = Materials(5, material);
        Read("indexedLate.growBelowMaterialReservation", renderer, 4);
        Object.DestroyImmediate(renderer.gameObject);

        renderer = new GameObject("Empty indexed overrides").AddComponent<MeshRenderer>();
        renderer.sharedMaterials = Materials(3, material);
        renderer.SetPropertyBlock(new MaterialPropertyBlock(), 0);
        renderer.sharedMaterials = Materials(5, material);
        Read("indexedEmpty.grow", renderer);
        renderer.SetPropertyBlock(null, 1);
        renderer.sharedMaterials = Materials(8, material);
        Read("indexedEmpty.growAfterClearDifferentSlot", renderer);
        renderer.SetPropertyBlock(null, 0);
        renderer.sharedMaterials = Materials(11, material);
        Read("indexedEmpty.growAfterClearSameSlot", renderer);
        Object.DestroyImmediate(renderer.gameObject);

        renderer = new GameObject("Indexed overrides after trimming").AddComponent<MeshRenderer>();
        renderer.sharedMaterials = Materials(5, material);
        renderer.SetPropertyBlock(block, 4);
        renderer.sharedMaterials = Materials(2, material);
        Read("indexedTrim.lastOverrideOutsideRange", renderer);
        renderer.sharedMaterials = Materials(8, material);
        Read("indexedTrim.growPastLastOverride", renderer, 4);
        renderer.SetPropertyBlock(block, 7);
        renderer.sharedMaterials = Array.Empty<Material>();
        Read("indexedTrim.zeroWhileActive", renderer);
        renderer.sharedMaterials = Materials(10, material);
        Read("indexedTrim.growAfterZero", renderer, 7);
        Object.DestroyImmediate(renderer.gameObject);
    }

    static void MeasureIndexedBlocks(string prefix, int count, Material material)
    {
        var renderer = new GameObject(prefix).AddComponent<MeshRenderer>();
        renderer.sharedMaterials = Materials(count, material);
        Read(prefix + ".initial", renderer);
        var block = new MaterialPropertyBlock();
        block.SetFloat(Property, 3.75f);
        block.SetVectorArray("_Vectors", new Vector4[40]);
        block.SetMatrixArray("_Matrices", new Matrix4x4[20]);
        renderer.SetPropertyBlock(block);
        Read(prefix + ".whole", renderer);
        renderer.SetPropertyBlock(null, 0);
        Read(prefix + ".firstNull", renderer);
        renderer.SetPropertyBlock(new MaterialPropertyBlock(), 0);
        Read(prefix + ".firstEmpty", renderer);
        var local = new MaterialPropertyBlock();
        local.SetFloat(Property, 17.25f);
        renderer.SetPropertyBlock(local, 0);
        renderer.SetPropertyBlock(local, count - 1);
        Read(prefix + ".end", renderer, count - 1);
        var clone = Object.Instantiate(renderer);
        Read(prefix + ".clone", clone, count - 1);
        Object.DestroyImmediate(clone.gameObject);
        renderer.sharedMaterials = Materials(count + 3, material);
        Read(prefix + ".grew", renderer, count - 1);
        renderer.SetPropertyBlock(local, count + 2);
        Read(prefix + ".newLast", renderer, count + 2);
        renderer.sharedMaterials = Materials(1, material);
        Read(prefix + ".shrunk", renderer);
        renderer.SetPropertyBlock(null, 0);
        Read(prefix + ".cleared", renderer);
        renderer.sharedMaterials = Materials(count + 3, material);
        Read(prefix + ".regrew", renderer, count + 2);
        renderer.sharedMaterials = Array.Empty<Material>();
        Read(prefix + ".emptySlots", renderer);
        var materialFromEmpty = renderer.material;
        Put(prefix + ".emptyMaterialIsNull", (materialFromEmpty == null).ToString());
        Read(prefix + ".readDefault", renderer);
        Object.DestroyImmediate(renderer.gameObject);
        Object.DestroyImmediate(materialFromEmpty);
    }

    static void MeasureIndexErrors(Material material)
    {
        var renderer = new GameObject("Invalid material index").AddComponent<MeshRenderer>();
        renderer.sharedMaterials = Materials(2, material);
        var block = new MaterialPropertyBlock();
        block.SetFloat(Property, 3.75f);
        void Measure(string name, MaterialPropertyBlock input, int index)
        {
            var logs = new List<string>();
            var counter = new LoggerCounter();
            var previousHandler = Debug.unityLogger.logHandler;
            bool hasStack = false;
            void OnLog(string message, string trace, LogType type)
            {
                logs.Add(type + "/" + message);
                hasStack |= !string.IsNullOrEmpty(trace);
            }
            Application.logMessageReceived += OnLog;
            Debug.unityLogger.logHandler = counter;
            try
            {
                renderer.SetPropertyBlock(input, index);
                Put(name + ".outcome", "returned");
            }
            catch (Exception error) { Put(name + ".outcome", error.GetType().Name); }
            finally
            {
                Debug.unityLogger.logHandler = previousHandler;
                Application.logMessageReceived -= OnLog;
            }
            Put(name + ".logs", string.Join("|", logs));
            Put(name + ".handlerCalls", counter.Calls.ToString());
            Put(name + ".hasStack", hasStack.ToString());
            Read(name + ".state", renderer);
        }
        var previous = Application.GetStackTraceLogType(LogType.Error);
        try
        {
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
            Measure("invalid.negative", block, -1);
            Measure("invalid.equal", block, 2);
            Measure("invalid.null", null, 2);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            Measure("invalid.scriptTrace", block, 2);
        }
        finally { Application.SetStackTraceLogType(LogType.Error, previous); }
        renderer.sharedMaterials = Materials(3, material);
        Read("invalid.didNotBindAfterGrowth", renderer, 2);
        Object.DestroyImmediate(renderer.gameObject);
    }

    static void MeasureAuthored()
    {
        foreach (int count in new[] { 0, 3 })
        {
            string prefix = "authored." + count;
            var source = GameObject.Find("Authored Renderer " + count).GetComponent<MeshRenderer>();
            Read(prefix, source);
            var clone = Object.Instantiate(source);
            Read(prefix + ".clone", clone);
            Object.DestroyImmediate(clone.gameObject);
            if (count == 0) _ = source.material;
            source.SetPropertyBlock(new MaterialPropertyBlock(), 0);
            Read(prefix + ".emptyIndexed", source);
            source.sharedMaterials = Array.Empty<Material>();
            Read(prefix + ".clearMaterials", source);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MESH_RENDERER_MEMORY_OUT")))
            new GameObject("Measure renderer memory").AddComponent<MeasureMeshRendererMemory>();
    }

    IEnumerator Start()
    {
        string output = Environment.GetEnvironmentVariable("MESH_RENDERER_MEMORY_OUT");
        Put("unity", Application.unityVersion);
        var material = new Material(Shader.Find("Unlit/Color"));
        MeasureMaterialSlots(material);
        MeasureAssignAndGetMaterials(material);
        MeasureIndexedReservationLifetime(material);
        MeasureIndexErrors(material);
        foreach (int count in new[] { 1, 2, 3, 5, 8, 17 })
            MeasureIndexedBlocks("builtin." + count, count, material);

        var pipeline = ScriptableObject.CreateInstance<RendererMemoryPipeline>();
        var srpCamera = new GameObject("SRP measurement camera").AddComponent<Camera>();
        var previousDefault = GraphicsSettings.defaultRenderPipeline;
        var previousQuality = QualitySettings.renderPipeline;
        try
        {
            QualitySettings.renderPipeline = null;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var request = new RendererMemoryPipeline.FrameRequest();
            bool acceptsRequest = RenderPipeline.SupportsRenderRequest(srpCamera, request);
            Put("srp.renderRequestSupported", acceptsRequest.ToString());
            if (acceptsRequest) RenderPipeline.SubmitRenderRequest(srpCamera, request);
            for (int frame = 0; frame < 4 && !pipeline.RenderedFrame; frame++) yield return null;
            Put("srp.active", (GraphicsSettings.currentRenderPipeline != null).ToString());
            Put("srp.renderedFrame", pipeline.RenderedFrame.ToString());
            MeasureIndexedBlocks("srp.3", 3, material);
        }
        finally
        {
            QualitySettings.renderPipeline = previousQuality;
            GraphicsSettings.defaultRenderPipeline = previousDefault;
            Object.DestroyImmediate(srpCamera.gameObject);
            Object.DestroyImmediate(pipeline);
        }
        MeasureAuthored();
        Object.DestroyImmediate(material);
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
