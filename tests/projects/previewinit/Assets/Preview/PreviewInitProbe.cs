using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public sealed class PreviewProbeEvents : MonoBehaviour
{
    internal static bool Watch;
    internal static readonly List<string> Calls = new();
    void OnEnable() { if (Watch) Calls.Add(name + ":enable"); }
    void OnDisable() { if (Watch) Calls.Add(name + ":disable"); }
}

sealed class PreviewProbePipelineAsset : RenderPipelineAsset
{
    protected override RenderPipeline CreatePipeline() => null;
}

sealed class PreviewMaterialPipelineAsset : RenderPipelineAsset
{
    internal Material ProbeMaterial;
    internal bool ReadName;

    protected override RenderPipeline CreatePipeline() => null;

    public override Material defaultMaterial
    {
        get
        {
            if (ReadName) _ = name;
            return ProbeMaterial;
        }
    }
}

public static class PreviewInitProbe
{
    [Serializable] struct Row { public string k; public string v; }
    static readonly BindingFlags StaticInternal = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly MethodInfo Initialize = typeof(EditorUtility).GetMethod("InitInstantiatedPreviewRecursive", StaticInternal);
    static readonly MethodInfo Configure = typeof(EditorUtility).GetMethod("ConfigurePreviewObjectSRP", StaticInternal);

    public static void RecordEdit() => EditorApplication.delayCall += () => Record(exitEditor: true);
    public static void RunEditTest() => Record(exitEditor: false);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RecordPlay() => Record(exitEditor: true);

    static void Record(bool exitEditor)
    {
        var directory = Environment.GetEnvironmentVariable("PREVIEW_INIT_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var rows = new List<string>();
        void Add(string key, object value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value.ToString() }));
        Add("editor.isPlaying", EditorApplication.isPlaying);
        var saved = GraphicsSettings.defaultRenderPipeline;
        var quality = QualitySettings.renderPipeline;
        QualitySettings.renderPipeline = null;
        var pipeline = ScriptableObject.CreateInstance<PreviewProbePipelineAsset>();
        try
        {
            foreach (bool useSrp in new[] { false, true })
            {
                GraphicsSettings.defaultRenderPipeline = useSrp ? pipeline : null;
                var key = useSrp ? "srp." : "builtin.";
                Add(key + "activePipeline", GraphicsSettings.isScriptableRenderPipelineEnabled);
                var root = new GameObject("PreviewRoot", typeof(MeshRenderer), typeof(PreviewProbeEvents));
                var child = new GameObject("PreviewChild", typeof(MeshRenderer), typeof(PreviewProbeEvents));
                var grandchild = new GameObject("PreviewGrandchild", typeof(PreviewProbeEvents));
                var sibling = new GameObject("PreviewSibling", typeof(MeshRenderer));
                child.transform.SetParent(root.transform, false);
                grandchild.transform.SetParent(child.transform, false);
                grandchild.SetActive(false);
                root.hideFlags = HideFlags.NotEditable | HideFlags.HideInInspector;
                child.hideFlags = HideFlags.DontUnloadUnusedAsset;
                var rootRenderer = root.GetComponent<MeshRenderer>();
                var childRenderer = child.GetComponent<MeshRenderer>();
                rootRenderer.lightProbeUsage = LightProbeUsage.UseProxyVolume;
                childRenderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                PreviewProbeEvents.Calls.Clear();
                PreviewProbeEvents.Watch = true;
                Add(key + "call", Invoke(Initialize, root));
                PreviewProbeEvents.Watch = false;
                Add(key + "flags", $"{(int)root.hideFlags},{(int)child.hideFlags},{(int)grandchild.hideFlags},{(int)sibling.hideFlags}");
                Add(key + "componentFlags", $"{(int)root.transform.hideFlags},{(int)childRenderer.hideFlags},{(int)grandchild.GetComponent<PreviewProbeEvents>().hideFlags}");
                Add(key + "probes", $"{rootRenderer.lightProbeUsage},{childRenderer.lightProbeUsage},{sibling.GetComponent<MeshRenderer>().lightProbeUsage}");
                Add(key + "active", $"{root.activeInHierarchy},{child.activeInHierarchy},{grandchild.activeInHierarchy}");
                Add(key + "parenting", $"{child.transform.parent == root.transform},{grandchild.transform.parent == child.transform}");
                Add(key + "callbacks", string.Join(",", PreviewProbeEvents.Calls));
                var plainRoot = new GameObject("PlainRoot", typeof(PreviewProbeEvents));
                var plainChild = new GameObject("PlainChild", typeof(PreviewProbeEvents));
                plainChild.transform.SetParent(plainRoot.transform, false);
                PreviewProbeEvents.Calls.Clear();
                PreviewProbeEvents.Watch = true;
                plainRoot.hideFlags = HideFlags.HideAndDontSave;
                PreviewProbeEvents.Watch = false;
                Add(key + "directFlagsCallbacks", string.Join(",", PreviewProbeEvents.Calls));
                UnityEngine.Object.DestroyImmediate(plainRoot);
                PreviewProbeEvents.Calls.Clear();
                PreviewProbeEvents.Watch = true;
                Add(key + "repeat", Invoke(Initialize, root));
                PreviewProbeEvents.Watch = false;
                Add(key + "repeatCallbacks", string.Join(",", PreviewProbeEvents.Calls));
                rootRenderer.lightProbeUsage = LightProbeUsage.UseProxyVolume;
                childRenderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                Add(key + "configure", Invoke(Configure, root));
                Add(key + "configureSelfUsage", rootRenderer.lightProbeUsage);
                Add(key + "configureChildPreservesOwnUsage", childRenderer.lightProbeUsage);
                Add(key + "nullRecursive", Invoke(Initialize, null));
                Add(key + "nullConfigure", Invoke(Configure, null));
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(sibling);
                Add(key + "destroyedRecursive", Invoke(Initialize, root));
                Add(key + "destroyedConfigure", Invoke(Configure, root));
            }
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = pipeline;
            var overridden = new GameObject("Quality override", typeof(MeshRenderer));
            try
            {
                var renderer = overridden.GetComponent<MeshRenderer>();
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                Add("quality.activePipeline", GraphicsSettings.isScriptableRenderPipelineEnabled);
                Add("quality.configure", Invoke(Configure, overridden));
                Add("quality.usage", renderer.lightProbeUsage);
                Add("quality.flags", (int)overridden.hideFlags);
            }
            finally { UnityEngine.Object.DestroyImmediate(overridden); }

            var temporaryOverride = ScriptableObject.CreateInstance<PreviewProbePipelineAsset>();
            var fallbackRoot = new GameObject("Destroyed quality override", typeof(MeshRenderer));
            try
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = temporaryOverride;
                Add("quality.liveOverrideSelected", ReferenceEquals(GraphicsSettings.currentRenderPipeline, temporaryOverride));
                UnityEngine.Object.DestroyImmediate(temporaryOverride);
                Add("quality.destroyedOverrideIsNull", QualitySettings.renderPipeline == null);
                Add("quality.defaultSelectedAfterDestroy", ReferenceEquals(GraphicsSettings.currentRenderPipeline, pipeline));
                Add("quality.activeAfterDestroy", GraphicsSettings.isScriptableRenderPipelineEnabled);
                var renderer = fallbackRoot.GetComponent<MeshRenderer>();
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                Add("quality.configureAfterDestroy", Invoke(Configure, fallbackRoot));
                Add("quality.usageAfterDestroy", renderer.lightProbeUsage);
                RecordDestroyedDefault(Add);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fallbackRoot);
                if (temporaryOverride != null) UnityEngine.Object.DestroyImmediate(temporaryOverride);
            }
        }
        finally
        {
            PreviewProbeEvents.Watch = false;
            GraphicsSettings.defaultRenderPipeline = saved;
            QualitySettings.renderPipeline = quality;
            UnityEngine.Object.DestroyImmediate(pipeline);
        }
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(directory, "values.jsonl"), rows);
        if (exitEditor) EditorApplication.Exit(0);
    }

    static void RecordDestroyedDefault(Action<string, object> add)
    {
        var previous = GraphicsSettings.defaultRenderPipeline;
        var pipeline = ScriptableObject.CreateInstance<PreviewMaterialPipelineAsset>();
        var material = new Material(Shader.Find("Standard")) { name = "Preview pipeline default" };
        try
        {
            pipeline.ProbeMaterial = material;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            add("quality.liveDefaultPrimitive", PrimitiveMaterial());
            UnityEngine.Object.DestroyImmediate(pipeline);
            add("quality.bothDestroyedPipelineClrNull", ReferenceEquals(GraphicsSettings.currentRenderPipeline, null));
            add("quality.bothDestroyedPipelineTypeNull", GraphicsSettings.currentRenderPipelineAssetType == null);
            add("quality.bothDestroyedPipelineActive", GraphicsSettings.isScriptableRenderPipelineEnabled);
            add("quality.bothDestroyedPrimitive", PrimitiveMaterial());
            pipeline.ReadName = true;
            add("quality.bothDestroyedPrimitiveVirtualGetter", PrimitiveMaterial());
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = previous;
            if (pipeline != null) UnityEngine.Object.DestroyImmediate(pipeline);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static string PrimitiveMaterial()
    {
        GameObject primitive = null;
        try
        {
            primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = primitive.GetComponent<MeshRenderer>().sharedMaterial;
            return material != null ? material.name : "null";
        }
        catch (Exception error) { return error.GetType().Name; }
        finally { if (primitive != null) UnityEngine.Object.DestroyImmediate(primitive); }
    }

    static string Invoke(MethodInfo method, GameObject target)
    {
        try { method.Invoke(null, new object[] { target }); return "ok"; }
        catch (TargetInvocationException ex) { return ex.InnerException.GetType().Name; }
    }
}
