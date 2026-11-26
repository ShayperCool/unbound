using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Writes values.jsonl in $DEFAULTCONTROLS_OUT (tests/value_probe.py rows): controls for creation
/// defaults recorded by tests/projects/defaultstate, each set apart from the fresh value it
/// explains (a value set after creation, another context, an argument that takes another path).
/// Floats are written with "R"; an exception is written as "throws" with its type and message;
/// log lines a call writes are rows of their own (".log").
/// </summary>
public class DefaultControlsProbe : MonoBehaviour
{
    const string Variable = "DEFAULTCONTROLS_OUT";
    static StreamWriter output;
    static readonly List<string> s_Logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    /// <summary>Records the action's answer (or what it throws) and, as key.log, the log lines it
    /// wrote (only when it wrote any).</summary>
    static void Try(string key, Func<string> action)
    {
        s_Logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (s_Logs.Count > 0) Row(key + ".log", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);

    static string Positions(Vector3[] points)
    {
        if (points == null) return "null";
        var parts = new string[points.Length];
        for (int i = 0; i < points.Length; i++) parts[i] = "(" + V(points[i]) + ")";
        return points.Length + ":" + string.Join(";", parts);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("DefaultControlsProbe").AddComponent<DefaultControlsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        Application.logMessageReceived += OnLog;
        try
        {
            CommandBuffers();
            Formats();
            Descriptors();
            CullingMasks();
            Lights();
            Readbacks();
            Animators();
        }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        Application.logMessageReceived -= OnLog;
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static void OnLog(string condition, string stackTrace, LogType type) => s_Logs.Add(type + ": " + condition);

    /// <summary>CommandBuffer.name: new, then set to a name, to empty and to null.</summary>
    static void CommandBuffers()
    {
        Try("cmd.new.name", () => new CommandBuffer().name);
        Try("cmd.set.name", () => { var c = new CommandBuffer(); c.name = "probe"; return c.name; });
        Try("cmd.setEmpty.name", () => { var c = new CommandBuffer(); c.name = "probe"; c.name = ""; return c.name; });
        Try("cmd.setNull.name", () => { var c = new CommandBuffer(); c.name = "probe"; c.name = null; return c.name == null ? "null" : "string:" + c.name; });
        Try("cmd.cleared.name", () => { var c = new CommandBuffer(); c.Clear(); return c.name; });
    }

    /// <summary>GraphicsFormatUtility.GetRenderTextureFormat of every graphics format.</summary>
    static void Formats()
    {
        // By name: obsolete aliases share their value with another name.
        foreach (var name in Enum.GetNames(typeof(GraphicsFormat)))
        {
            var format = (GraphicsFormat)Enum.Parse(typeof(GraphicsFormat), name);
            Try("rtformat." + (int)format + "." + name, () => ((int)GraphicsFormatUtility.GetRenderTextureFormat(format)).ToString(CultureInfo.InvariantCulture));
        }
    }

    static string Attachment(AttachmentDescriptor a) => (int)a.format + "/" + (GraphicsFormatUtility.IsDepthStencilFormat(a.graphicsFormat) ? "depth" : a.graphicsFormat.ToString());

    /// <summary>The descriptor's color format, graphics format, sampling mode and the kind of its
    /// depth format (none, depth only, depth and stencil: the format itself is the device's).</summary>
    static string Descriptor(RenderTextureDescriptor d) =>
        (int)d.colorFormat + "/" + d.graphicsFormat + "/" + d.shadowSamplingMode + "/"
        + (d.depthStencilFormat == GraphicsFormat.None ? "nodepth" : GraphicsFormatUtility.IsStencilFormat(d.depthStencilFormat) ? "depthstencil" : "depth");

    /// <summary>AttachmentDescriptor.format and RenderTextureDescriptor.colorFormat: default, made
    /// from a format, and with the fields their getters read set apart.</summary>
    static void Descriptors()
    {
        Try("attach.default", () => Attachment(default));
        Try("attach.none", () => Attachment(new AttachmentDescriptor(GraphicsFormat.None)));
        Try("attach.argb32", () => Attachment(new AttachmentDescriptor(RenderTextureFormat.ARGB32)));
        Try("attach.depth", () => Attachment(new AttachmentDescriptor(RenderTextureFormat.Depth)));
        Try("attach.shadowmap", () => Attachment(new AttachmentDescriptor(RenderTextureFormat.Shadowmap)));
        Try("attach.d32", () => Attachment(new AttachmentDescriptor(GraphicsFormat.D32_SFloat)));
        Try("attach.r8g8b8a8", () => Attachment(new AttachmentDescriptor(GraphicsFormat.R8G8B8A8_UNorm)));
        Try("attach.setNone", () => { var a = new AttachmentDescriptor(RenderTextureFormat.ARGB32); a.graphicsFormat = GraphicsFormat.None; return Attachment(a); });

        Try("rtd.default", () => Descriptor(default));
        Try("rtd.default.samplingNone", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.None; return Descriptor(d); });
        Try("rtd.default.samplingRaw", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.RawDepth; return Descriptor(d); });
        Try("rtd.default.samplingCompare", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; return Descriptor(d); });
        Try("rtd.argb32", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)));
        Try("rtd.argb32.depth", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24)));
        Try("rtd.depth", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24)));
        Try("rtd.shadowmap", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.Shadowmap, 24)));
        Try("rtd.depth.samplingRaw", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24); d.shadowSamplingMode = ShadowSamplingMode.RawDepth; return Descriptor(d); });
        Try("rtd.depth.samplingCompare", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24); d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; return Descriptor(d); });
        Try("rtd.argb32.noColor", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.graphicsFormat = GraphicsFormat.None; return Descriptor(d); });
        Try("rtd.argb32.noColor.samplingCompare", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.graphicsFormat = GraphicsFormat.None; d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; return Descriptor(d); });
        Try("rtd.depth.setArgb32", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24); d.colorFormat = RenderTextureFormat.ARGB32; return Descriptor(d); });
        Try("rtd.argb32.setShadowmap", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24); d.colorFormat = RenderTextureFormat.Shadowmap; return Descriptor(d); });
        Try("rtd.argb32.setDepth", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24); d.colorFormat = RenderTextureFormat.Depth; return Descriptor(d); });
        Try("rtd.default.depth24", () => { RenderTextureDescriptor d = default; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.default.samplingNone.depth24", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.None; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.argb32.samplingCompare.depth24", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.argb32.samplingRaw.depth24", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.shadowSamplingMode = ShadowSamplingMode.RawDepth; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.texture.depth", () => { var t = new RenderTexture(4, 4, 24, RenderTextureFormat.Depth); var r = Descriptor(t.descriptor); DestroyImmediate(t); return r; });
        Try("rtd.texture.shadowmap", () => { var t = new RenderTexture(4, 4, 24, RenderTextureFormat.Shadowmap); var r = Descriptor(t.descriptor); DestroyImmediate(t); return r; });

        Try("srf.new.terrainDetailUnsupported", () => new SupportedRenderingFeatures().terrainDetailUnsupported.ToString());
        Try("srf.setFalse.terrainDetailUnsupported", () => { var s = new SupportedRenderingFeatures(); s.terrainDetailUnsupported = false; return s.terrainDetailUnsupported.ToString(); });
    }

    /// <summary>Scene culling masks: batch filter settings default and set; objects in the probe
    /// scene, new, inactive, in DontDestroyOnLoad, in a created scene and in a preview scene.</summary>
    static void CullingMasks()
    {
        Try("bfs.default", () => default(BatchFilterSettings).sceneCullingMask.ToString(CultureInfo.InvariantCulture));
        Try("bfs.set5", () => { BatchFilterSettings s = default; s.sceneCullingMask = 5; return s.sceneCullingMask.ToString(CultureInfo.InvariantCulture); });
        Try("bfs.set0", () => { BatchFilterSettings s = default; s.sceneCullingMask = 0; return s.sceneCullingMask.ToString(CultureInfo.InvariantCulture); });
        Try("bfs.drawRange", () => default(BatchDrawRange).filterSettings.sceneCullingMask.ToString(CultureInfo.InvariantCulture));

        Try("go.authored", () => GameObject.Find("AuthoredEmpty").sceneCullingMask.ToString(CultureInfo.InvariantCulture));
        Try("go.new", () => { var g = new GameObject("MaskNew"); var r = g.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
        Try("go.inactive", () => { var g = new GameObject("MaskInactive"); g.SetActive(false); var r = g.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
        Try("go.ddol", () => { var g = new GameObject("MaskDdol"); DontDestroyOnLoad(g); var r = g.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
        Try("go.created", () =>
        {
            var scene = SceneManager.CreateScene("MaskScene");
            var g = new GameObject("MaskCreated");
            SceneManager.MoveGameObjectToScene(g, scene);
            var r = g.sceneCullingMask;
            DestroyImmediate(g);
            return r.ToString(CultureInfo.InvariantCulture);
        });
        Try("camera.new", () => { var g = new GameObject("MaskCamera"); var c = g.AddComponent<Camera>(); var r = c.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
#if UNITY_EDITOR
        Try("editor.activeScene", () => UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(SceneManager.GetActiveScene()).ToString(CultureInfo.InvariantCulture));
        Try("editor.invalidScene", () => UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(default).ToString(CultureInfo.InvariantCulture));
        Try("editor.defaultConstant", () => UnityEditor.SceneManagement.SceneCullingMasks.DefaultSceneCullingMask.ToString(CultureInfo.InvariantCulture));
#endif
    }

    /// <summary>Light.layerShadowCullDistances of a new light; LightProbeGroup positions added by
    /// AddComponent (active and inactive object), loaded empty and with two points, instantiated.</summary>
    static void Lights()
    {
        Try("light.layerShadowCullDistances", () =>
        {
            var g = new GameObject("CullLight");
            var light = g.AddComponent<Light>();
            var a = light.layerShadowCullDistances;
            var text = a == null ? "null" : a.Length + ":" + string.Join(",", Array.ConvertAll(a, F));
            DestroyImmediate(g);
            return text;
        });
        Try("light.layerShadowCullDistances.copy", () =>
        {
            var g = new GameObject("CullLightCopy");
            var light = g.AddComponent<Light>();
            var a = light.layerShadowCullDistances;
            a[0] = 5f;
            var b = light.layerShadowCullDistances;
            var text = (ReferenceEquals(a, b) ? "same" : "copy") + ":" + F(b[0]);
            DestroyImmediate(g);
            return text;
        });

        Try("lpg.added", () => { var g = new GameObject("ProbesAdded"); var r = Positions(g.AddComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.addedInactive", () => { var g = new GameObject("ProbesInactive"); g.SetActive(false); var r = Positions(g.AddComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.authoredEmpty", () => Positions(GameObject.Find("AuthoredEmpty").GetComponent<LightProbeGroup>().probePositions));
        Try("lpg.authoredTwo", () => Positions(GameObject.Find("AuthoredTwo").GetComponent<LightProbeGroup>().probePositions));
        Try("lpg.instantiatedEmpty", () => { var g = Instantiate(GameObject.Find("AuthoredEmpty")); var r = Positions(g.GetComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.setEmpty", () => { var g = new GameObject("ProbesSet"); var p = g.AddComponent<LightProbeGroup>(); p.probePositions = new Vector3[0]; var r = Positions(p.probePositions); DestroyImmediate(g); return r; });
    }

    /// <summary>A default AsyncGPUReadbackRequest and a default CullingResults.</summary>
    static void Readbacks()
    {
        Try("readback.default.done", () => default(AsyncGPUReadbackRequest).done.ToString());
        Try("readback.default.hasError", () => default(AsyncGPUReadbackRequest).hasError.ToString());
        Try("readback.default.width", () => default(AsyncGPUReadbackRequest).width.ToString(CultureInfo.InvariantCulture));
        Try("readback.default.layerCount", () => default(AsyncGPUReadbackRequest).layerCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.lightIndexCount", () => default(CullingResults).lightIndexCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.reflectionProbeIndexCount", () => default(CullingResults).reflectionProbeIndexCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.lightAndReflectionProbeIndexCount", () => default(CullingResults).lightAndReflectionProbeIndexCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.visibleLights", () => default(CullingResults).visibleLights.Length.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.visibleOffscreenVertexLights", () => default(CullingResults).visibleOffscreenVertexLights.Length.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.visibleReflectionProbes", () => default(CullingResults).visibleReflectionProbes.Length.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.GetLightIndexMap", () => { var m = default(CullingResults).GetLightIndexMap(Unity.Collections.Allocator.Temp); var n = m.Length; m.Dispose(); return n.ToString(CultureInfo.InvariantCulture); });
        Try("culling.default.equals", () => default(CullingResults).Equals(default(CullingResults)).ToString());
    }

    static string AnimatorState(Animator a) =>
        "pivot=" + F(a.feetPivotActive) + ";target=" + Q(a.targetRotation) + "@" + V(a.targetPosition)
        + ";playback=" + F(a.playbackTime) + ";start=" + F(a.recorderStartTime) + ";stop=" + F(a.recorderStopTime)
        + ";mode=" + a.recorderMode;

    /// <summary>A new Animator's pivot, target and recorder values, with and without a controller,
    /// on an inactive object, and after each of them is set.</summary>
    static void Animators()
    {
        Try("animator.new", () => { var g = new GameObject("AnimNew"); var r = AnimatorState(g.AddComponent<Animator>()); DestroyImmediate(g); return r; });
        Try("animator.inactive", () => { var g = new GameObject("AnimInactive"); g.SetActive(false); var r = AnimatorState(g.AddComponent<Animator>()); DestroyImmediate(g); return r; });
#if UNITY_EDITOR
        Try("animator.controller", () =>
        {
            var g = new GameObject("AnimController");
            var a = g.AddComponent<Animator>();
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddLayer("Base");
            controller.layers[0].stateMachine.AddState("Idle");
            a.runtimeAnimatorController = controller;
            var r = AnimatorState(a);
            DestroyImmediate(g);
            return r;
        });
#endif
        foreach (var value in new[] { 0.25f, 0f, 1f, 3f, -1f })
        {
            Try("animator.feetPivotActive." + F(value), () =>
            {
                var g = new GameObject("AnimPivot");
                var a = g.AddComponent<Animator>();
                a.feetPivotActive = value;
                var r = F(a.feetPivotActive);
                DestroyImmediate(g);
                return r;
            });
        }
        Try("animator.feetPivotActive.inactive", () =>
        {
            var g = new GameObject("AnimPivotInactive");
            g.SetActive(false);
            var a = g.AddComponent<Animator>();
            a.feetPivotActive = 0.5f;
            var r = F(a.feetPivotActive);
            DestroyImmediate(g);
            return r;
        });
        Try("animator.recorderStartTime.set", () => { var g = new GameObject("AnimStart"); var a = g.AddComponent<Animator>(); a.recorderStartTime = 2f; var r = AnimatorState(a); DestroyImmediate(g); return r; });
        Try("animator.recorderStopTime.set", () => { var g = new GameObject("AnimStop"); var a = g.AddComponent<Animator>(); a.recorderStopTime = 2f; var r = AnimatorState(a); DestroyImmediate(g); return r; });
        Try("animator.playbackTime.set", () => { var g = new GameObject("AnimPlayback"); var a = g.AddComponent<Animator>(); a.playbackTime = 1f; var r = AnimatorState(a); DestroyImmediate(g); return r; });
    }
}
