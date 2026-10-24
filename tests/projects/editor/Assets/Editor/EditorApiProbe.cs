using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.MPE;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records what the Editor API answers in Play mode, frame by frame, into the JSON file
/// <c>EDITOR_PROBE_OUT</c> names, then exits. The Editor enters Play mode from
/// <see cref="ProbeRunner.Record"/>; Unbound plays the scene. <c>compare.py</c> lists differences.
/// </summary>
[InitializeOnLoad]
public static class EditorApiProbe
{
    static readonly List<(string Key, string Json)> s_Records = new List<(string, string)>();
    static int s_Frame;
    static int s_SelectionChanged;
    static int s_UndoRedoPerformed;
    static readonly List<string> s_UndoEvents = new List<string>();
    static GameObject s_Probe;
    static Vector3 s_Start;
    static GameObject s_Created;
    static int s_ProgressId;
    static AnimFloat s_Anim;
    static int s_AnimChanged;

    static string Out => Environment.GetEnvironmentVariable("EDITOR_PROBE_OUT");

    static EditorApiProbe()
    {
        if (string.IsNullOrEmpty(Out)) return;
        EditorApplication.playModeStateChanged += state =>
        {
            Record("playModeStateChanged." + state, "true");
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            s_Frame = 0;
            EditorApplication.update += Step;
        };
    }

    static void Step()
    {
        try
        {
            switch (s_Frame++)
            {
                case 0: First(); break;
                case 1: Second(); break;
                case 2: Third(); break;
                case 3: Fourth(); break;
                case 4: Finish(); break;
            }
        }
        catch (Exception e)
        {
            Record("exception.frame" + (s_Frame - 1), Str(e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace));
            Finish();
        }
    }

    // ------------------------------------------------------------------ frame 0

    static void First()
    {
        Record("env.isPlaying", B(EditorApplication.isPlaying));
        Record("env.inBatchMode", B(InternalEditorUtility.inBatchMode));
        Record("env.isHumanControllingUs", B(InternalEditorUtility.isHumanControllingUs));
        Record("env.currentThreadIsMainThread", B(InternalEditorUtility.CurrentThreadIsMainThread()));
        Record("env.isProSkin", B(EditorGUIUtility.isProSkin));
        Record("env.pixelsPerPoint", F(EditorGUIUtility.pixelsPerPoint));
        Record("env.trHello", Str(L10n.Tr("Hello")));
        Record("env.trTextContent", Str(EditorGUIUtility.TrTextContent("Text", "Tip").text + "|" + EditorGUIUtility.TrTextContent("Text", "Tip").tooltip));
        Record("env.analyticsEnabled", B(EditorAnalytics.enabled));
        Record("env.processLevel", Str(ProcessService.level.ToString()));
        Record("env.importWorker", B(AssetDatabase.IsAssetImportWorkerProcess()));
        Record("env.isCreateFromTemplate", B(EditorApplication.isCreateFromTemplate));
        Record("env.scriptCompilationFailed", B(EditorUtility.scriptCompilationFailed));
        Record("env.preferencesFolderSuffix", Str(Suffix(InternalEditorUtility.unityPreferencesFolder)));
        Record("cloud.userId.empty", B(string.IsNullOrEmpty(CloudProjectSettings.userId)));
        Record("cloud.userName", Str(CloudProjectSettings.userName));
        Record("cloud.accessToken.empty", B(string.IsNullOrEmpty(CloudProjectSettings.accessToken)));
        Record("cloud.projectId", Str(CloudProjectSettings.projectId));
        Record("cloud.projectName", Str(CloudProjectSettings.projectName));
        Record("cloud.organizationId", Str(CloudProjectSettings.organizationId));
        Record("cloud.organizationName", Str(CloudProjectSettings.organizationName));
        Record("cloud.projectBound", B(CloudProjectSettings.projectBound));
        Record("cloud.coppa", Str(CloudProjectSettings.coppaCompliance.ToString()));
        Record("sceneView.count", I(SceneView.sceneViews.Count));
        Record("sceneView.gizmos", B(Handles.ShouldRenderGizmos()));
        Record("fileUtil.physical", Str(FileUtil.GetPhysicalPath("Assets/Probe/Probe.mat")));
        Record("fileUtil.logical", Str(FileUtil.GetLogicalPath("Assets/Probe/Probe.mat")));
        foreach (var bytes in new long[] { 0, 1, 511, 512, 1000, 1023, 1024, 1536, 10240, 524287, 524288, 1048576, 1572864, 536870912, 1073741824, 5000000000 })
            Record("formatBytes." + bytes, Str(EditorUtility.FormatBytes(bytes)));

        Json();

        s_Probe = GameObject.Find("Probe");
        var asset = AssetDatabase.LoadAssetAtPath<ProbeAsset>("Assets/Probe/ProbeAsset.asset");

        // Selection.
        Selection.selectionChanged += () => s_SelectionChanged++;
        Record("selection.initial.active", Name(Selection.activeObject));
        Record("selection.initial.count", I(Selection.count));
        Selection.activeGameObject = s_Probe;
        Record("selection.set.active", Name(Selection.activeObject));
        Record("selection.set.transform", Name(Selection.activeTransform));
        Record("selection.set.count", I(Selection.count));
        Record("selection.set.gameObjects", I(Selection.gameObjects.Length));
        Record("selection.set.changedSameFrame", I(s_SelectionChanged));
        Selection.objects = new Object[] { asset, s_Probe };
        Record("selection.objects.active", Name(Selection.activeObject));
        Record("selection.objects.transforms", I(Selection.transforms.Length));
        Record("selection.objects.assetGUIDs", I(Selection.assetGUIDs.Length));
        Record("selection.objects.filteredBehaviour", I(Selection.GetFiltered<ProbeBehaviour>(SelectionMode.Unfiltered).Length));
        Record("selection.objects.filteredAsset", I(Selection.GetFiltered(typeof(ProbeAsset), SelectionMode.Assets).Length));

        // Undo.
        Undo.undoRedoPerformed += () => s_UndoRedoPerformed++;
        Undo.undoRedoEvent += (in UndoRedoInfo info) => s_UndoEvents.Add(info.undoName + ":" + info.isRedo);
        Record("undo.f0.group", I(Undo.GetCurrentGroup()));
        Record("undo.f0.groupName", Str(Undo.GetCurrentGroupName()));
        Undo.IncrementCurrentGroup();
        Record("undo.f0.afterIncrement", I(Undo.GetCurrentGroup()));
        Undo.SetCurrentGroupName("Probe Move");
        s_Start = s_Probe.transform.position;
        Undo.RecordObject(s_Probe.transform, "Move Probe");
        s_Probe.transform.position = new Vector3(5, 6, 7);
        Record("undo.f0.groupNameAfterRecord", Str(Undo.GetCurrentGroupName()));
        Record("undo.f0.groupAfterRecord", I(Undo.GetCurrentGroup()));

        // Dirtiness.
        Record("dirty.asset.initial", B(EditorUtility.IsDirty(asset)));
        EditorUtility.SetDirty(asset);
        Record("dirty.asset.afterSet", B(EditorUtility.IsDirty(asset)));
        Record("dirty.asset.count", I(EditorUtility.GetDirtyCount(asset)));
        EditorUtility.SetDirty(asset);
        Record("dirty.asset.count2", I(EditorUtility.GetDirtyCount(asset)));
        EditorUtility.ClearDirty(asset);
        Record("dirty.asset.afterClear", B(EditorUtility.IsDirty(asset)));
        Record("dirty.asset.countAfterClear", I(EditorUtility.GetDirtyCount(asset)));
        var behaviour = s_Probe.GetComponent<ProbeBehaviour>();
        EditorUtility.SetDirty(behaviour);
        Record("dirty.scene.afterSet", B(EditorUtility.IsDirty(behaviour)));
        Record("dirty.scene.count", I(EditorUtility.GetDirtyCount(behaviour)));

        // Global object ids.
        GlobalIds(asset, behaviour);

        // Progress.
        s_ProgressId = Progress.Start("Probe task", "first");
        Record("progress.idPositive", B(s_ProgressId > 0));
        Record("progress.exists", B(Progress.Exists(s_ProgressId)));
        Record("progress.name", Str(Progress.GetName(s_ProgressId)));
        Record("progress.description", Str(Progress.GetDescription(s_ProgressId)));
        Record("progress.status", Str(Progress.GetStatus(s_ProgressId).ToString()));
        Record("progress.options", Str(Progress.GetOptions(s_ProgressId).ToString()));
        Record("progress.initial", F(Progress.GetProgress(s_ProgressId)));
        Progress.Report(s_ProgressId, 0.25f, "second");
        Record("progress.reported", F(Progress.GetProgress(s_ProgressId)));
        Record("progress.reportedDescription", Str(Progress.GetDescription(s_ProgressId)));
        Progress.Report(s_ProgressId, 3, 4);
        Record("progress.steps", Str($"{Progress.GetCurrentStep(s_ProgressId)}/{Progress.GetTotalSteps(s_ProgressId)} {F(Progress.GetProgress(s_ProgressId))}"));
        Progress.Finish(s_ProgressId);
        Record("progress.finished.status", Str(Progress.GetStatus(s_ProgressId).ToString()));
        Record("progress.finished.exists", B(Progress.Exists(s_ProgressId)));
        Record("progress.finished.progress", F(Progress.GetProgress(s_ProgressId)));

        // Animated values.
        s_Anim = new AnimFloat(0f, () => s_AnimChanged++);
        Record("anim.initial", F(s_Anim.value));
        s_Anim.target = 1f;
        Record("anim.afterTarget.isAnimating", B(s_Anim.isAnimating));
        Record("anim.afterTarget.value", F(s_Anim.value));
        var b = new AnimBool(true);
        Record("anim.bool.faded", F(b.faded));
        b.value = false;
        Record("anim.bool.fadedAfterSet", F(b.faded));

        // Serialized files.
        SerializedFile(asset);

        // Compute shaders from source.
        Compute();

        // Shader messages.
        Record("shader.messages.standard", I(ShaderUtil.GetShaderMessages(Shader.Find("Standard")).Length));
        Record("shader.hasError.standard", B(ShaderUtil.ShaderHasError(Shader.Find("Standard"))));
    }

    static void Json()
    {
        var asset = AssetDatabase.LoadAssetAtPath<ProbeAsset>("Assets/Probe/ProbeAsset.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Probe/Probe.mat");
        var probe = GameObject.Find("Probe");
        var data = new ProbeData { asset = asset, material = material, sceneObject = probe };
        Record("json.plain", Str(Ids(JsonUtility.ToJson(data))));
        Record("json.plain.pretty", Str(Ids(JsonUtility.ToJson(data, true))));
        Record("editorJson.plain", Str(Ids(EditorJsonUtility.ToJson(data))));
        Record("editorJson.plain.pretty", Str(Ids(EditorJsonUtility.ToJson(data, true))));
        Record("editorJson.asset", Str(Ids(EditorJsonUtility.ToJson(asset))));
        Record("editorJson.behaviour", Str(Ids(EditorJsonUtility.ToJson(probe.GetComponent<ProbeBehaviour>()))));
        Record("editorJson.transform", Str(Ids(EditorJsonUtility.ToJson(probe.transform))));
        Record("editorJson.gameObject", Str(Ids(EditorJsonUtility.ToJson(probe))));
        Record("json.asset", Str(Ids(JsonUtility.ToJson(asset))));

        // References back.
        var copy = new ProbeData();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(data), copy);
        Record("editorJson.roundTrip.asset", Name(copy.asset));
        Record("editorJson.roundTrip.material", Name(copy.material));
        Record("editorJson.roundTrip.sceneObject", Name(copy.sceneObject));
        var copy2 = new ProbeData();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(data), copy2);
        Record("json.roundTrip.asset", Name(copy2.asset));
        Record("json.roundTrip.sceneObject", Name(copy2.sceneObject));
        var guid = AssetDatabase.AssetPathToGUID("Assets/Probe/ProbeAsset.asset");
        var byGuid = new ProbeData();
        EditorJsonUtility.FromJsonOverwrite("{\"asset\":{\"fileID\":11400000,\"guid\":\"" + guid + "\",\"type\":2}}", byGuid);
        Record("editorJson.byGuid.asset", Name(byGuid.asset));
        var byGuidPlain = new ProbeData();
        JsonUtility.FromJsonOverwrite("{\"asset\":{\"fileID\":11400000,\"guid\":\"" + guid + "\",\"type\":2}}", byGuidPlain);
        Record("json.byGuid.asset", Name(byGuidPlain.asset));
        var overwritten = ScriptableObject.CreateInstance<ProbeAsset>();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(asset), overwritten);
        Record("editorJson.overwriteAsset", Str($"{overwritten.value} {overwritten.label} {overwritten.numbers.Count} {Name(overwritten.material)} name={overwritten.name}"));

        // Missing fields.
        var empty = JsonUtility.FromJson<ProbeEmpty>("{}");
        Record("json.fromJson.empty", Str(Describe(empty)));
        var emptyOverwrite = new ProbeEmpty();
        JsonUtility.FromJsonOverwrite("{\"number\":3}", emptyOverwrite);
        Record("json.overwrite.empty", Str(Describe(emptyOverwrite)));
        var editorEmpty = new ProbeEmpty();
        EditorJsonUtility.FromJsonOverwrite("{\"number\":3}", editorEmpty);
        Record("editorJson.overwrite.empty", Str(Describe(editorEmpty)));
        var holder = JsonUtility.FromJson<ProbeCallbackHolder>("{\"single\":{\"id\":\"a\"},\"many\":[{\"id\":\"b\"}]}");
        Record("json.structCallbacks", Str(holder.single.resolved + " " + (holder.many.Count > 0 ? holder.many[0].resolved : "none")));
        var editorHolder = new ProbeCallbackHolder();
        EditorJsonUtility.FromJsonOverwrite("{\"single\":{\"id\":\"a\"},\"many\":[{\"id\":\"b\"}]}", editorHolder);
        Record("editorJson.structCallbacks", Str(editorHolder.single.resolved + " " + (editorHolder.many.Count > 0 ? editorHolder.many[0].resolved : "none")));
    }

    static string Describe(ProbeEmpty e) =>
        $"list={(e.list == null ? "null" : e.list.Count.ToString())} array={(e.array == null ? "null" : e.array.Length.ToString())} " +
        $"text={(e.text == null ? "null" : "'" + e.text + "'")} nested={(e.nested == null ? "null" : "'" + e.nested.name + "'/" + (e.nested.tags == null ? "null" : e.nested.tags.Count.ToString()))} number={e.number}";

    static void GlobalIds(ProbeAsset asset, ProbeBehaviour behaviour)
    {
        void Id(string key, Object o)
        {
            var id = GlobalObjectId.GetGlobalObjectIdSlow(o);
            var text = id.ToString();
            Record("globalId." + key, Str(Guids(text)));
            Record("globalId." + key + ".type", I(id.identifierType));
            Record("globalId." + key + ".back", B(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) == o));
            Record("globalId." + key + ".parse", B(GlobalObjectId.TryParse(text, out var parsed) && parsed.Equals(id)));
        }
        Id("asset", asset);
        Id("material", AssetDatabase.LoadAssetAtPath<Material>("Assets/Probe/Probe.mat"));
        Id("gameObject", behaviour.gameObject);
        Id("component", behaviour);
        Id("transform", behaviour.transform);
        Id("memory", ScriptableObject.CreateInstance<ProbeAsset>());
        Id("null", null);
        Record("globalId.parse.bad", B(GlobalObjectId.TryParse("nonsense", out _)));
    }

    static void SerializedFile(ProbeAsset asset)
    {
        var saved = ScriptableObject.CreateInstance<ProbeAsset>();
        saved.value = 9;
        saved.label = "saved: yes";
        saved.numbers = new List<float> { 0.1f, 2f, 1e-7f };
        saved.nested.tags.Add("t");
        saved.material = asset.material;
        saved.name = "Saved";
        Directory.CreateDirectory("Temp/EditorProbe");
        InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { saved }, "Temp/EditorProbe/Saved.asset", true);
        var text = File.Exists("Temp/EditorProbe/Saved.asset") ? File.ReadAllText("Temp/EditorProbe/Saved.asset") : "";
        // Unbound writes its copy elsewhere; the content is what counts.
        var unbound = "Library/Unbound/EditorFiles/Temp/EditorProbe/Saved.asset";
        if (text.Length == 0 && File.Exists(unbound)) text = File.ReadAllText(unbound);
        Record("serializedFile.text", Str(Guids(text)));
        var loaded = InternalEditorUtility.LoadSerializedFileAndForget("Temp/EditorProbe/Saved.asset");
        Record("serializedFile.loaded.count", I(loaded.Length));
        if (loaded.Length > 0 && loaded[0] is ProbeAsset back)
            Record("serializedFile.loaded", Str($"{back.value} {back.label} {string.Join(",", back.numbers.Select(F))} {back.nested.tags.Count} {Name(back.material)} {back.name} persistent={EditorUtility.IsPersistent(back)} hide={back.hideFlags}"));
        Record("serializedFile.missing", I(InternalEditorUtility.LoadSerializedFileAndForget("Temp/EditorProbe/None.asset").Length));
    }

    static void Compute()
    {
        var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Probe/Doubler.probecompute");
        Record("compute.loaded", B(shader != null));
        if (shader == null) return;
        Record("compute.name", Str(shader.name));
        Record("compute.hasKernel", B(shader.HasKernel("Double")));
        var buffer = new ComputeBuffer(8, 4);
        buffer.SetData(new float[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        int kernel = shader.FindKernel("Double");
        shader.SetBuffer(kernel, "values", buffer);
        shader.Dispatch(kernel, 2, 1, 1);
        var result = new float[8];
        buffer.GetData(result);
        buffer.Release();
        Record("compute.result", Str(string.Join(",", result.Select(F))));
        Record("compute.messages", I(ShaderUtil.GetComputeShaderMessages(shader).Length));
    }

    // ------------------------------------------------------------------ frames 1-4

    static void Second()
    {
        Record("selection.f1.changed", I(s_SelectionChanged));
        Record("undo.f1.group", I(Undo.GetCurrentGroup()));
        Record("undo.f1.groupName", Str(Undo.GetCurrentGroupName()));
        Undo.PerformUndo();
        Record("undo.f1.positionAfterUndo", V(s_Probe.transform.position));
        Record("undo.f1.restoredStart", B(s_Probe.transform.position == s_Start));
        Record("undo.f1.performedCount", I(s_UndoRedoPerformed));
        Record("undo.f1.events", Str(string.Join(";", s_UndoEvents)));
        Undo.PerformRedo();
        Record("undo.f1.positionAfterRedo", V(s_Probe.transform.position));
        Record("undo.f1.performedCountAfterRedo", I(s_UndoRedoPerformed));
        Record("undo.f1.eventsAfterRedo", Str(string.Join(";", s_UndoEvents)));
        Record("undo.f1.groupAfterRedo", I(Undo.GetCurrentGroup()));

        s_Created = new GameObject("Created");
        Undo.RegisterCreatedObjectUndo(s_Created, "Create");
        var collider = Undo.AddComponent<BoxCollider>(s_Probe);
        Record("undo.f1.addComponent", B(collider != null && s_Probe.GetComponent<BoxCollider>() != null));
        Record("progress.f1.exists", B(Progress.Exists(s_ProgressId)));
        Record("anim.f1.isAnimating", B(s_Anim.isAnimating));
        Record("anim.f1.changedCalled", B(s_AnimChanged > 0));
    }

    static void Third()
    {
        Record("undo.f2.group", I(Undo.GetCurrentGroup()));
        Undo.PerformUndo();
        Record("undo.f2.colliderAfterUndo", B(s_Probe.GetComponent<BoxCollider>() != null));
        Record("undo.f2.createdAfterUndo", B(s_Created != null));
        Record("undo.f2.events", Str(string.Join(";", s_UndoEvents)));
        // Groups collapse.
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(s_Probe.transform, "A");
        s_Probe.transform.position = Vector3.one;
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(s_Probe.transform, "B");
        s_Probe.transform.position = Vector3.one * 2;
        Undo.CollapseUndoOperations(group);
        Record("undo.f2.collapsedGroup", I(Undo.GetCurrentGroup() - group));
        Undo.PerformUndo();
        Record("undo.f2.positionAfterCollapsedUndo", V(s_Probe.transform.position));
        Record("undo.f2.eventsAfterCollapse", Str(string.Join(";", s_UndoEvents)));
        // RevertAllInCurrentGroup.
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(s_Probe.transform, "C");
        s_Probe.transform.position = Vector3.one * 3;
        Undo.RevertAllInCurrentGroup();
        Record("undo.f2.positionAfterRevert", V(s_Probe.transform.position));
    }

    static void Fourth()
    {
        Record("undo.f3.group", I(Undo.GetCurrentGroup()));
        Record("progress.f3.exists", B(Progress.Exists(s_ProgressId)));
    }

    static void Finish()
    {
        EditorApplication.update -= Step;
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < s_Records.Count; i++)
        {
            sb.Append("  ").Append(Str(s_Records[i].Key)).Append(": ").Append(s_Records[i].Json);
            sb.Append(i + 1 < s_Records.Count ? ",\n" : "\n");
        }
        sb.Append("}\n");
        File.WriteAllText(Out, sb.ToString());
        EditorApplication.Exit(0);
    }

    // ------------------------------------------------------------------ helpers

    static void Record(string key, string json) => s_Records.Add((key, json));

    static string B(bool b) => b ? "true" : "false";
    static string I(long i) => i.ToString(CultureInfo.InvariantCulture);
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => Str($"{F(v.x)},{F(v.y)},{F(v.z)}");
    static string Name(Object o) => o == null ? "null" : Str(o.name + ":" + o.GetType().Name);

    static string Suffix(string path)
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? "";
        return path.StartsWith(home) ? "~" + path.Substring(home.Length) : path;
    }

    /// <summary>Instance ids differ between runs: replaced by a marker.</summary>
    static string Ids(string json) => System.Text.RegularExpressions.Regex.Replace(json, "\"instanceID\":( ?)-?[0-9]+", "\"instanceID\":$1<id>");

    static string Guids(string text) => text;

    static string Str(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
