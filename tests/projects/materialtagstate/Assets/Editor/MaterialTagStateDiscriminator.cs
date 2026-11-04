using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// <c>-executeMethod MaterialTagStateDiscriminator.Run</c> (MATERIAL_TAG_STATE_OUT and
/// MATERIAL_TAG_STATE_DIAG set) records seven material subjects before and after one observed
/// URP camera render: the complete serialized stringTagMap, GetTag with and without fallbacks,
/// shader and asset identity, raw/effective queue and texture slots, next to the configured
/// pipeline asset, the actual RenderPipelineManager.currentPipeline and Shader.globalRenderPipeline.
/// Run resets the scene first, then waits for the startup settle predicate (below) before it creates
/// or loads any subject; the before-render baseline is taken right after it, before the camera exists.
///
/// Startup settle predicate (public Editor API only): one complete EditorApplication.update
/// cycle, from this fixture's previous update callback to the current one, so that every other update
/// subscriber was invoked once inside it, during which no AssetPostprocessor.OnPostprocessAllAssets
/// batch reported an imported, deleted or moved asset, and neither EditorApplication.isCompiling nor
/// EditorApplication.isUpdating holds. Without it within MaxSettleTicks callbacks the run fails (exit 3)
/// before any subject exists. The predicate cannot see deferred work that an update subscriber has not
/// started yet, so it is not a proof of import quiescence: the measurement interval, from subject
/// creation to the last after-render digest, is observed by the same callback and any import batch or
/// compilation inside it fails the run (exit 3) with every recorded path in the diagnostics.
///
/// The observer never writes a subject: no SetOverrideTag on existing assets, no ShaderGUI
/// validation, no import, reimport or ApplyModifiedProperties. Its only AssetPostprocessor
/// (MaterialTagStateImportObserver) implements nothing but the static OnPostprocessAllAssets
/// notification and records the reported paths; it has no per-asset import callback.
/// The render is either the normal render loop drawing the fixture camera or the documented
/// UniversalRenderPipeline.SingleCameraRequest (SupportsRenderRequest checked first); completion
/// is the endCameraRendering event for the fixture camera. Camera.Render is never used. Without an
/// observed completion the run fails (exit 3) and writes no after-render rows. A failure writes the
/// render rows and fixture/status once and keeps every row recorded before it.
///
/// Canonical rows (one JSON object per line, {"k":..,"v":..}) hold only stable values. Instance
/// IDs, process data, paths outside the project, clocks, frame/tick counts and device details go
/// to the diagnostics file, which a comparison only hashes.
/// </summary>
public static class MaterialTagStateDiscriminator
{
    const string Sentinel = "<unset-sentinel>";
    const string LitShaderPath = "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";
    const string LitShaderGuid = "933532a4fcc9baf4fa0491de14d08ed7";
    const string LitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";
    const int MaxTicks = 600;
    const int MaxSettleTicks = 600;
    static readonly string[] TextureSlots = { "_BaseMap", "_EmissionMap", "_BumpMap" };

    sealed class Subject
    {
        public string Label;
        public Material Material;
        public MeshRenderer Renderer;
        public string BeforeDigest;
    }

    static readonly List<string> rows = new List<string>();
    static readonly Dictionary<string, object> diag = new Dictionary<string, object>();
    static readonly List<object> events = new List<object>();
    static List<Subject> subjects;
    static Camera fixtureCamera;
    static RenderTexture target;
    static string outPath, diagPath;
    static int ticks, endCameraForFixture, endContextForFixture, pipelinesCreated;
    static string pipelineAtEndCamera;
    static bool submitted, supportedAtSubmit, finished, renderRowsWritten, statusWritten;
    static string renderPath = "none";

    // Import observation. Statics restart with every domain load, so "startup" covers only the batches
    // since the last domain load before Run.
    static string phase = "startup";
    static int settleTicks, settleWindowBatches, pipelinesCreatedBeforeBaseline, emptyImportBatches;
    static readonly Dictionary<string, object> importBatches = new Dictionary<string, object>
    {
        { "startup", new List<object>() }, { "settle", new List<object>() }, { "measurement", new List<object>() },
        { "afterEnd", new List<object>() },
    };
    static readonly List<object> settleTrace = new List<object>();

    public static void Run()
    {
        outPath = Environment.GetEnvironmentVariable("MATERIAL_TAG_STATE_OUT");
        diagPath = Environment.GetEnvironmentVariable("MATERIAL_TAG_STATE_DIAG");
        if (string.IsNullOrEmpty(outPath) || string.IsNullOrEmpty(diagPath))
        {
            Debug.LogError("MaterialTagStateDiscriminator: MATERIAL_TAG_STATE_OUT and MATERIAL_TAG_STATE_DIAG are required");
            EditorApplication.Exit(2);
            return;
        }
        try
        {
            diag["unityVersion"] = Application.unityVersion;
            diag["isBatchMode"] = Application.isBatchMode;
            diag["isPlaying"] = EditorApplication.isPlaying;
            diag["processId"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            diag["dataPath"] = Application.dataPath;
            diag["startUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            diag["graphicsDeviceType"] = SystemInfo.graphicsDeviceType.ToString();
            diag["graphicsDeviceName"] = SystemInfo.graphicsDeviceName;
            diag["graphicsDeviceVersion"] = SystemInfo.graphicsDeviceVersion;
            diag["frameCountAtRun"] = Time.frameCount;
            diag["renderedFrameCountAtRun"] = Time.renderedFrameCount;
            diag["isCompilingAtRun"] = EditorApplication.isCompiling;
            diag["isUpdatingAtRun"] = EditorApplication.isUpdating;

            // Reset scenes before any fixture object exists, so the scene transition cannot unload a
            // subject; the settle cycle below then also covers whatever the transition starts.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            phase = "settle";
            settleWindowBatches = 0;
            RenderPipelineManager.activeRenderPipelineCreated += OnPipelineCreatedBeforeBaseline;
            Debug.Log("MaterialTagStateDiscriminator: phase settle");
            EditorApplication.update += Settle;
        }
        catch (Exception e)
        {
            Fail("exception: " + e.GetType().Name + ": " + e.Message, e.ToString());
        }
    }

    /// <summary>Called by MaterialTagStateImportObserver for every OnPostprocessAllAssets batch.</summary>
    internal static void RecordImportBatch(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        int count = PathCount(imported) + PathCount(deleted) + PathCount(moved) + PathCount(movedFrom);
        if (count == 0)
        {
            emptyImportBatches++;
            return;
        }
        var batch = new Dictionary<string, object>
        {
            { "settleTick", settleTicks }, { "tick", ticks }, { "frameCount", Time.frameCount },
            { "imported", Paths(imported) }, { "deleted", Paths(deleted) }, { "moved", Paths(moved) },
            { "movedFrom", Paths(movedFrom) },
        };
        ((List<object>)importBatches[phase == "done" ? "afterEnd" : phase]).Add(batch);
        if (phase == "settle") settleWindowBatches++;
    }

    static int PathCount(string[] paths) => paths == null ? 0 : paths.Length;

    static List<object> Paths(string[] paths) => paths == null ? new List<object>() : paths.Select(p => (object)p).ToList();

    static int MeasurementImportBatches() => ((List<object>)importBatches["measurement"]).Count;

    static void Settle()
    {
        if (finished) return;
        settleTicks++;
        try
        {
            bool compiling = EditorApplication.isCompiling;
            bool updating = EditorApplication.isUpdating;
            int window = settleWindowBatches;
            settleWindowBatches = 0;
            settleTrace.Add(new Dictionary<string, object>
            {
                { "settleTick", settleTicks }, { "importBatchesInCycle", window }, { "isCompiling", compiling },
                { "isUpdating", updating }, { "frameCount", Time.frameCount }, { "renderedFrameCount", Time.renderedFrameCount },
            });
            // The first callback's window starts in Run, not at a previous callback of this delegate,
            // so it is not a complete update cycle.
            if (settleTicks >= 2 && window == 0 && !compiling && !updating)
            {
                BeginMeasurement();
                return;
            }
            if (settleTicks >= MaxSettleTicks)
            {
                Fail("startup-import-not-quiescent", null);
                return;
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception e)
        {
            Fail("exception: " + e.GetType().Name + ": " + e.Message, e.ToString());
        }
    }

    static void BeginMeasurement()
    {
        EditorApplication.update -= Settle;
        RenderPipelineManager.activeRenderPipelineCreated -= OnPipelineCreatedBeforeBaseline;
        phase = "measurement";
        diag["settleTicks"] = settleTicks;
        diag["frameCountAtBaseline"] = Time.frameCount;
        diag["renderedFrameCountAtBaseline"] = Time.renderedFrameCount;
        diag["pipelinesCreatedBeforeBaseline"] = pipelinesCreatedBeforeBaseline;
        Debug.Log("MaterialTagStateDiscriminator: phase measurement after " + settleTicks + " settle callbacks");

        subjects = LoadSubjects();
        Row("fixture/subjects", subjects.Select(s => (object)s.Label).ToList());
        Row("fixture/stages", new List<object> { "before-render", "after-render" });

        // before-render: nothing about the camera exists yet.
        Sample("before-render");
        foreach (var s in subjects) s.BeforeDigest = SerializedDigest(s.Material);
        diag["beforeDigests"] = subjects.ToDictionary(s => s.Label, s => (object)s.BeforeDigest);
        ImportDiagnostics();
        Flush();

        RenderPipelineManager.endCameraRendering += OnEndCamera;
        RenderPipelineManager.endContextRendering += OnEndContext;
        RenderPipelineManager.activeRenderPipelineCreated += OnPipelineCreated;
        BuildScene();
        EditorApplication.update += Tick;
    }

    static void ImportDiagnostics()
    {
        diag["imports.batches"] = importBatches;
        diag["imports.emptyBatches"] = emptyImportBatches;
        diag["settle.trace"] = settleTrace;
    }

    static List<Subject> LoadSubjects()
    {
        var lit = AssetDatabase.LoadAssetAtPath<Shader>(LitShaderPath);
        if (lit == null) throw new InvalidOperationException("URP Lit shader not found at " + LitShaderPath);
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(lit, out string litGuid, out long _) || litGuid != LitShaderGuid)
            throw new InvalidOperationException("URP Lit shader GUID is " + litGuid + ", expected " + LitShaderGuid);

        var opaque = new Material(lit) { name = "Fresh_Opaque" };
        opaque.SetOverrideTag("RenderType", "Opaque");
        var cleared = new Material(lit) { name = "Fresh_Cleared" };
        cleared.SetOverrideTag("RenderType", "Opaque");
        cleared.SetOverrideTag("RenderType", "");
        var transparent = new Material(lit) { name = "Fresh_Transparent" };
        transparent.SetOverrideTag("RenderType", "Transparent");

        var list = new List<Subject>
        {
            new Subject { Label = "fresh-opaque", Material = opaque },
            new Subject { Label = "fresh-cleared", Material = cleared },
            new Subject { Label = "fresh-transparent", Material = transparent },
            new Subject { Label = "legacy-M_Tex", Material = Imported("Assets/Models/MaterialsLegacy.fbx", "M_Tex") },
            new Subject { Label = "legacy-M_Red", Material = Imported("Assets/Models/MaterialsLegacy.fbx", "M_Red") },
            new Subject { Label = "description-M_Tex", Material = Imported("Assets/Models/Materials.fbx", "M_Tex") },
            new Subject { Label = "package-Lit.mat", Material = AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath) },
        };
        foreach (var s in list)
            if (s.Material == null) throw new InvalidOperationException("subject " + s.Label + " not found");
        return list;
    }

    /// <summary>The one Material sub-asset with that name, loaded as already imported.</summary>
    static Material Imported(string path, string name)
    {
        var found = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Where(m => m.name == name).ToList();
        if (found.Count != 1)
            throw new InvalidOperationException(path + " has " + found.Count + " materials named " + name + ", expected 1");
        return found[0];
    }

    static void BuildScene()
    {
        target = new RenderTexture(16, 16, 24) { name = "MaterialTagStateTarget" };
        target.Create();
        var cameraObject = new GameObject("MaterialTagStateCamera");
        cameraObject.transform.position = new Vector3(0f, 0f, -8f);
        fixtureCamera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<UniversalAdditionalCameraData>();
        fixtureCamera.clearFlags = CameraClearFlags.SolidColor;
        fixtureCamera.backgroundColor = Color.black;
        fixtureCamera.targetTexture = target;
        fixtureCamera.enabled = true;

        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        for (int i = 0; i < subjects.Count; i++)
        {
            var go = new GameObject("Subject_" + subjects[i].Label);
            go.transform.position = new Vector3((i - 3) * 1.1f, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = subjects[i].Material;
            subjects[i].Renderer = renderer;
        }
    }

    static void Tick()
    {
        if (finished) return;
        ticks++;
        try
        {
            if (MeasurementImportBatches() > 0)
            {
                Fail("import-during-measurement", null);
                return;
            }
            if (EditorApplication.isCompiling)
            {
                Fail("compilation-during-measurement", null);
                return;
            }
            if (endCameraForFixture > 0)
            {
                if (renderPath == "none") renderPath = submitted ? "SingleCameraRequest-deferred" : "render-loop";
                Complete();
                return;
            }
            var pipeline = RenderPipelineManager.currentPipeline;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            bool supported = RenderPipeline.SupportsRenderRequest(fixtureCamera, request);
            if (ticks == 1)
            {
                diag["firstTick.currentPipelineNull"] = pipeline == null;
                diag["firstTick.supportsSingleCameraRequest"] = supported;
            }
            if (!submitted && supported)
            {
                submitted = true;
                supportedAtSubmit = true;
                diag["submitTick"] = ticks;
                RenderPipeline.SubmitRenderRequest(fixtureCamera, request);
                if (endCameraForFixture > 0)
                {
                    renderPath = "SingleCameraRequest";
                    Complete();
                    return;
                }
            }
            if (ticks >= MaxTicks)
            {
                Fail(submitted ? "render-request-submitted-without-completion" : "no-render-completion-and-request-unsupported", null);
                return;
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception e)
        {
            Fail("exception: " + e.GetType().Name + ": " + e.Message, e.ToString());
        }
    }

    static void OnEndCamera(ScriptableRenderContext context, Camera camera)
    {
        events.Add("endCameraRendering:" + (camera == fixtureCamera ? "fixture" : "other:" + camera.name));
        if (camera != fixtureCamera) return;
        endCameraForFixture++;
        var p = RenderPipelineManager.currentPipeline;
        pipelineAtEndCamera = p == null ? null : p.GetType().FullName;
    }

    static void OnEndContext(ScriptableRenderContext context, List<Camera> cameras)
    {
        bool has = cameras != null && cameras.Contains(fixtureCamera);
        events.Add("endContextRendering:" + (has ? "fixture" : "other"));
        if (has) endContextForFixture++;
    }

    static void OnPipelineCreatedBeforeBaseline() => pipelinesCreatedBeforeBaseline++;

    static void OnPipelineCreated()
    {
        pipelinesCreated++;
        events.Add("activeRenderPipelineCreated");
    }

    static void RenderRows(bool completed)
    {
        // Written once: a later sampling failure keeps the observed render rows and every partial
        // sample; fixture/status records the failure independently of camera-render completion.
        if (renderRowsWritten) return;
        renderRowsWritten = true;
        Row("render/supportsSingleCameraRequestAtSubmit", supportedAtSubmit);
        Row("render/path", renderPath);
        Row("render/endCameraRendering.fixtureCamera", endCameraForFixture > 0);
        Row("render/endContextRendering.fixtureCamera", endContextForFixture > 0);
        Row("render/pipelineAtEndCamera.type", pipelineAtEndCamera);
        Row("render/pipelineCreatedDuringRun", pipelinesCreated > 0);
        Row("render/completed", completed);
        diag["ticks"] = ticks;
        diag["endCameraForFixture"] = endCameraForFixture;
        diag["endContextForFixture"] = endContextForFixture;
        diag["pipelinesCreated"] = pipelinesCreated;
        diag["events"] = events;
        diag["frameCountAtEnd"] = Time.frameCount;
    }

    static void Complete()
    {
        finished = true;
        Unsubscribe();
        RenderRows(true);
        Sample("after-render");
        foreach (var s in subjects)
        {
            string after = SerializedDigest(s.Material);
            Row("after-render/" + s.Label + "/serializedUnchanged", after == s.BeforeDigest);
            Row("after-render/" + s.Label + "/rendererBound", s.Renderer != null && s.Renderer.sharedMaterial == s.Material);
            diag["afterDigest." + s.Label] = after;
        }
        // The measurement interval ends here; an import or compilation anywhere inside it fails the run.
        if (MeasurementImportBatches() > 0)
        {
            Fail("import-during-measurement", null);
            return;
        }
        if (EditorApplication.isCompiling)
        {
            Fail("compilation-during-measurement", null);
            return;
        }
        phase = "done";
        statusWritten = true;
        Row("fixture/status", "complete");
        ImportDiagnostics();
        Flush();
        EditorApplication.Exit(0);
    }

    static void Fail(string reason, string detail)
    {
        finished = true;
        diag["failurePhase"] = phase;
        phase = "done";
        Unsubscribe();
        Debug.LogError("MaterialTagStateDiscriminator failed: " + reason);
        if (statusWritten)
        {
            // fixture/status was already written once; record the late failure in diagnostics only.
            diag["failureAfterStatus"] = reason;
            if (detail != null) diag["failureAfterStatusDetail"] = detail;
            Flush();
            EditorApplication.Exit(3);
            return;
        }
        RenderRows(false);
        statusWritten = true;
        Row("fixture/status", "failed: " + reason);
        if (detail != null) diag["failureDetail"] = detail;
        ImportDiagnostics();
        Flush();
        EditorApplication.Exit(3);
    }

    static void Unsubscribe()
    {
        EditorApplication.update -= Settle;
        EditorApplication.update -= Tick;
        RenderPipelineManager.activeRenderPipelineCreated -= OnPipelineCreatedBeforeBaseline;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        RenderPipelineManager.endContextRendering -= OnEndContext;
        RenderPipelineManager.activeRenderPipelineCreated -= OnPipelineCreated;
    }

    static void Sample(string stage)
    {
        Row(stage + "/pipeline/configured.current", AssetIdentity(GraphicsSettings.currentRenderPipeline));
        Row(stage + "/pipeline/configured.default", AssetIdentity(GraphicsSettings.defaultRenderPipeline));
        Row(stage + "/pipeline/configured.quality", AssetIdentity(QualitySettings.renderPipeline));
        var actual = RenderPipelineManager.currentPipeline;
        Row(stage + "/pipeline/actual.isNull", actual == null);
        Row(stage + "/pipeline/actual.type", actual == null ? null : actual.GetType().FullName);
        Row(stage + "/pipeline/actual.disposed", actual == null ? (object)null : actual.disposed);
        Row(stage + "/pipeline/globalRenderPipeline", Shader.globalRenderPipeline);
        diag[stage + ".actualPipelineHash"] = actual == null ? 0 : actual.GetHashCode();
        foreach (var s in subjects) SampleSubject(stage + "/" + s.Label + "/", s.Material);
    }

    static void SampleSubject(string key, Material m)
    {
        Row(key + "name", m.name);
        Row(key + "asset", AssetIdentity(m));
        var shader = m.shader;
        Row(key + "shader", shader == null ? null : shader.name);
        Row(key + "shader.asset", AssetIdentity(shader));
        Row(key + "shader.subshaderTags", SubshaderTags(shader));
        Row(key + "stringTagMap", StringTagMap(m));
        Row(key + "GetTag.false.sentinel", m.GetTag("RenderType", false, Sentinel));
        Row(key + "GetTag.true.sentinel", m.GetTag("RenderType", true, Sentinel));
        Row(key + "GetTag.false", m.GetTag("RenderType", false));
        Row(key + "renderQueue", m.renderQueue);
        Row(key + "rawRenderQueue", m.rawRenderQueue);
        var textures = new Dictionary<string, object>();
        foreach (var slot in TextureSlots)
        {
            if (!m.HasProperty(slot)) { textures[slot] = "absent-property"; continue; }
            var t = m.GetTexture(slot);
            textures[slot] = t == null ? null : (object)new Dictionary<string, object>
            {
                { "name", t.name }, { "width", t.width }, { "height", t.height }, { "asset", AssetIdentity(t) },
            };
        }
        Row(key + "textures", textures);
        diag[key + "instanceId"] = m.GetInstanceID();
    }

    /// <summary>The serialized stringTagMap, read through SerializedObject without applying anything.</summary>
    static object StringTagMap(Material m)
    {
        using (var so = new SerializedObject(m))
        {
            var p = so.FindProperty("stringTagMap");
            if (p == null) return new Dictionary<string, object> { { "__error__", "stringTagMap property missing" } };
            if (!p.isArray) return new Dictionary<string, object> { { "__error__", "stringTagMap is not an array" } };
            var map = new Dictionary<string, object>();
            for (int i = 0; i < p.arraySize; i++)
            {
                var e = p.GetArrayElementAtIndex(i);
                var first = e.FindPropertyRelative("first");
                var second = e.FindPropertyRelative("second");
                if (first == null || second == null || first.propertyType != SerializedPropertyType.String
                    || second.propertyType != SerializedPropertyType.String)
                    return new Dictionary<string, object> { { "__error__", "stringTagMap element " + i + " is not a string pair" } };
                if (map.ContainsKey(first.stringValue))
                    return new Dictionary<string, object> { { "__error__", "stringTagMap duplicate key " + first.stringValue } };
                map[first.stringValue] = second.stringValue;
            }
            return map;
        }
    }

    static object SubshaderTags(Shader shader)
    {
        if (shader == null) return null;
        var list = new List<object>();
        for (int i = 0; i < shader.subshaderCount; i++)
        {
            var renderType = shader.FindSubshaderTagValue(i, new ShaderTagId("RenderType")).name;
            var pipeline = shader.FindSubshaderTagValue(i, new ShaderTagId("RenderPipeline")).name;
            list.Add(new Dictionary<string, object> { { "RenderType", renderType }, { "RenderPipeline", pipeline } });
        }
        return list;
    }

    static object AssetIdentity(UnityEngine.Object o)
    {
        if (o == null) return null;
        var d = new Dictionary<string, object> { { "name", o.name }, { "type", o.GetType().FullName } };
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long fileId) && !string.IsNullOrEmpty(guid))
        {
            d["path"] = AssetDatabase.GetAssetPath(o);
            d["guid"] = guid;
            d["fileID"] = fileId;
        }
        else d["path"] = "not-an-asset";
        return d;
    }

    /// <summary>A digest of every serialized value, object references by asset identity, not instance ID.</summary>
    static string SerializedDigest(Material m)
    {
        var b = new StringBuilder();
        using (var so = new SerializedObject(m))
        {
            var p = so.GetIterator();
            while (p.Next(true))
            {
                b.Append(p.propertyPath).Append('|').Append(p.propertyType).Append('=');
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer: b.Append(p.longValue); break;
                    case SerializedPropertyType.Boolean: b.Append(p.boolValue); break;
                    case SerializedPropertyType.Float: b.Append(p.doubleValue.ToString("R", CultureInfo.InvariantCulture)); break;
                    case SerializedPropertyType.String: b.Append(p.stringValue); break;
                    case SerializedPropertyType.Color: b.Append(Vec(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a)); break;
                    case SerializedPropertyType.Vector2: b.Append(Vec(p.vector2Value.x, p.vector2Value.y)); break;
                    case SerializedPropertyType.Vector3: b.Append(Vec(p.vector3Value.x, p.vector3Value.y, p.vector3Value.z)); break;
                    case SerializedPropertyType.Vector4: b.Append(Vec(p.vector4Value.x, p.vector4Value.y, p.vector4Value.z, p.vector4Value.w)); break;
                    case SerializedPropertyType.ArraySize: b.Append(p.intValue); break;
                    case SerializedPropertyType.Enum: b.Append(p.intValue); break;
                    case SerializedPropertyType.ObjectReference:
                        b.Append(Json(AssetIdentity(p.objectReferenceValue))); break;
                    default: break;
                }
                b.Append('\n');
            }
        }
        using (var sha = SHA256.Create())
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString())).Select(x => x.ToString("x2")));
    }

    static string Vec(params float[] v) => string.Join(",", v.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

    static void Row(string key, object value) => rows.Add("{\"k\":" + Json(key) + ",\"v\":" + Json(value) + "}");

    static void Flush()
    {
        File.WriteAllText(outPath, string.Join("\n", rows) + "\n");
        File.WriteAllText(diagPath, Json(diag) + "\n");
    }

    static string Json(object v)
    {
        switch (v)
        {
            case null: return "null";
            case bool x: return x ? "true" : "false";
            case int x: return x.ToString(CultureInfo.InvariantCulture);
            case long x: return x.ToString(CultureInfo.InvariantCulture);
            case float x: return x.ToString("R", CultureInfo.InvariantCulture);
            case double x: return x.ToString("R", CultureInfo.InvariantCulture);
            case string s:
            {
                var b = new StringBuilder("\"");
                foreach (char c in s)
                {
                    if (c == '"' || c == '\\') b.Append('\\').Append(c);
                    else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                }
                return b.Append('"').ToString();
            }
            case Dictionary<string, object> d:
                return "{" + string.Join(",", d.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => Json(k) + ":" + Json(d[k]))) + "}";
            case List<object> l:
                return "[" + string.Join(",", l.Select(Json)) + "]";
            default:
                return Json(v.ToString());
        }
    }
}

/// <summary>
/// Records every batch the static AssetPostprocessor.OnPostprocessAllAssets notification
/// reports, for MaterialTagStateDiscriminator's settle predicate and measurement check. It implements
/// no per-asset callback, writes nothing and changes no import result.
/// </summary>
class MaterialTagStateImportObserver : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (AssetDatabase.IsAssetImportWorkerProcess()) return;
        MaterialTagStateDiscriminator.RecordImportBatch(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
    }
}
