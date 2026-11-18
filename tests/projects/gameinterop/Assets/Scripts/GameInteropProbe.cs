using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Timeline;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
#endif

/// <summary>
/// Writes values.jsonl in $GAMEINTEROP_OUT (tests/value_probe.py rows, see README.md): scripts whose
/// constructors all take arguments, hierarchy edits of loaded prefab assets before they are
/// instantiated, nested prefabs, reparenting from inside parent-change messages, Animator warnings,
/// UnityStats.screenRes, the compilation pipeline's assemblies and compiler options, Timeline assets
/// as they load, and precompiled plugins built against other versions of a library.
/// </summary>
public class GameInteropProbe : MonoBehaviour
{
    static void Row(string key, string value) => ProbeLog.Row(key, value);
    static void Row(string key, bool value) => ProbeLog.Row(key, value);
    static void Row(string key, int value) => ProbeLog.Row(key, value);
    static void Try(string key, Func<object> action) => ProbeLog.Try(key, action);
    static void Do(string key, Action action) => ProbeLog.Try(key, () => { action(); return "done"; });

    IEnumerator Start()
    {
        if (!ProbeLog.Open) yield break;
        // What loading the scene logged and ran (Awake, OnEnable of its scripts).
        ProbeLog.Flush("scene_load");
        Run("constructors", Constructors);
        yield return Hierarchy();
        Run("reentrant", Reentrant);
        Run("animator", Animators);
        Run("stats", Stats);
        Run("compilation", Compilation);
        Run("timeline", Timelines);
        Run("plugins", Plugins);
        ProbeLog.Flush("end");
        ProbeLog.Close();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static void Run(string section, Action action)
    {
        try { action(); }
        catch (Exception e) { Row(section + ".aborted", ProbeLog.Thrown(e)); }
        ProbeLog.Flush(section);
    }

    // ------------------------------------------------------------------ constructors

    static string Fields(ArgOnlyBehaviour c) => c == null ? "none" :
        $"number={c.number} text={c.text ?? "null"} list={List(c.list)} array={(c.array == null ? "null" : string.Join(",", c.array))} " +
        $"nested={(c.nested == null ? "null" : c.nested.a + "/" + c.nested.b)} transient={c.transient} transientList={List(c.transientList)} enabled={c.enabled}";

    static string Fields(ArgDerived c) => c == null ? "none" :
        $"baseNumber={c.baseNumber} baseTransient={c.baseTransient} derivedNumber={c.derivedNumber} derivedTransient={c.derivedTransient}";

    static string Fields(PrivateCtorBehaviour c) => c == null ? "none" : $"number={c.number} transient={c.transient}";

    static string Fields(ArgOnlyData d) => d == null ? (ReferenceEquals(d, null) ? "none" : "Unity-null") :
        $"name={d.name} number={d.number} list={List(d.list)} transient={d.transient}";

    static string List(List<int> list) => list == null ? "null" : "[" + string.Join(",", list) + "]";

    static int[] counted = new int[5];

    /// <summary>How many times each script's own constructor ran since the previous count.</summary>
    static string Counts()
    {
        var now = new[] { ArgOnlyBehaviour.Constructed, ArgBase.Constructed, ArgDerived.Constructed, PrivateCtorBehaviour.Constructed, ArgOnlyData.Constructed };
        var text = $"argOnly={now[0] - counted[0]} base={now[1] - counted[1]} derived={now[2] - counted[2]} private={now[3] - counted[3]} data={now[4] - counted[4]}";
        counted = now;
        return text;
    }

    static void Components(string key, GameObject go)
    {
        if (go == null) { Row(key, "no object"); return; }
        Row(key + ".arg_only", Fields(go.GetComponent<ArgOnlyBehaviour>()));
        Row(key + ".derived", Fields(go.GetComponent<ArgDerived>()));
        Row(key + ".private", Fields(go.GetComponent<PrivateCtorBehaviour>()));
        Row(key + ".components", string.Join(",", go.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name)));
        var holder = go.GetComponent<ArgHolder>();
        if (holder != null)
        {
            Row(key + ".holder.arg_only", holder.argOnly != null && holder.argOnly == go.GetComponent<ArgOnlyBehaviour>());
            Row(key + ".holder.derived", holder.derived != null && holder.derived == go.GetComponent<ArgDerived>());
            Row(key + ".holder.private", holder.privateCtor != null);
            Row(key + ".holder.data", Fields(holder.data));
        }
    }

    static void Constructors()
    {
        Row("ctor.after_scene", Counts());
        Components("ctor.scene", GameObject.Find("ArgScene"));

        GameObject asset = null;
        Try("ctor.prefab.load", () => asset = Resources.Load<GameObject>("ArgPrefab"));
        Row("ctor.prefab.counts", Counts());
        Components("ctor.prefab.asset", asset);
        GameObject clone = null;
        Try("ctor.prefab.instantiate", () => clone = asset == null ? null : Instantiate(asset));
        Row("ctor.prefab.instantiate.counts", Counts());
        Components("ctor.prefab.clone", clone);

        GameObject sceneCopy = null;
        Try("ctor.scene.instantiate", () => sceneCopy = Instantiate(GameObject.Find("ArgScene")));
        Components("ctor.scene.copy", sceneCopy);

        var added = new GameObject("Added");
        Try("ctor.add_call.arg_only", () => added.AddComponent<ArgOnlyBehaviour>());
        Try("ctor.add_call.derived", () => added.AddComponent<ArgDerived>());
        Try("ctor.add_call.private", () => added.AddComponent(typeof(PrivateCtorBehaviour)));
        Row("ctor.add.counts", Counts());
        Components("ctor.add", added);

        GameObject made = null;
        Try("ctor.new_game_object", () => made = new GameObject("Made", typeof(ArgOnlyBehaviour)));
        Row("ctor.new_game_object.fields", Fields(made == null ? null : made.GetComponent<ArgOnlyBehaviour>()));

        ArgOnlyData data = null;
        Try("ctor.data.load", () => data = Resources.Load<ArgOnlyData>("ArgData"));
        Row("ctor.data.load.fields", Fields(data));
        ArgOnlyData created = null;
        Try("ctor.data.create_generic", () => created = ScriptableObject.CreateInstance<ArgOnlyData>());
        Row("ctor.data.create_generic.fields", Fields(created));
        Try("ctor.data.create_type", () => Fields((ArgOnlyData)ScriptableObject.CreateInstance(typeof(ArgOnlyData))));
        Try("ctor.data.create_name", () => Fields(ScriptableObject.CreateInstance("ArgOnlyData") as ArgOnlyData));
        Try("ctor.data.instantiate", () => Fields(data == null ? null : Instantiate(data)));
        Row("ctor.data.counts", Counts());
        Try("ctor.data.json", () => { var d = ScriptableObject.CreateInstance<ArgOnlyData>(); JsonUtility.FromJsonOverwrite("{\"number\":21}", d); return Fields(d); });
    }

    // ------------------------------------------------------------------ hierarchy

    /// <summary>The hierarchy under a transform: names with their children in brackets, "!parent" on
    /// a child whose parent is not the one listing it and "!twice" on a transform listed again.</summary>
    static string Tree(Transform root)
    {
        var text = new StringBuilder();
        var seen = new HashSet<Transform>();
        void Walk(Transform t, Transform listedBy)
        {
            text.Append(t.name);
            if (listedBy != null && t.parent != listedBy) text.Append("!parent");
            if (!seen.Add(t)) { text.Append("!twice"); return; }
            if (t.childCount == 0) return;
            text.Append('(');
            for (int i = 0; i < t.childCount; i++)
            {
                if (i > 0) text.Append(',');
                var child = t.GetChild(i);
                if (child == null) text.Append("null");
                else Walk(child, t);
            }
            text.Append(')');
        }
        Walk(root, null);
        return text.ToString();
    }

    static string Where(Transform t) => t == null ? "none" :
        (t.parent == null ? "root" : "under " + t.parent.name) + " scene=" + (t.gameObject.scene.IsValid() ? t.gameObject.scene.name : "none");

    /// <summary>A hierarchy edit of a freshly loaded prefab asset (Rig/Body/{Head,Arm}, Rig/Base/Foot),
    /// the asset afterwards, and what instantiating it then gives.</summary>
    static IEnumerator AssetEdit(string key, string prefab, Func<GameObject, object> edit)
    {
        GameObject asset = null;
        ProbeLog.Try(key + ".load", () => asset = Resources.Load<GameObject>("Hier/" + prefab));
        if (asset == null) yield break;
        Row(key + ".validations", LimbRig.Validations);
        var head = asset.transform.Find("Body/Head");
        if (edit != null) Try(key + ".edit", () => edit(asset));
        yield return null;
        ProbeLog.Flush(key + ".frame");
        Row(key + ".asset", Tree(asset.transform));
        Row(key + ".head", Where(head));
        GameObject clone = null;
        Try(key + ".instantiate", () => clone = Instantiate(asset));
        if (clone != null)
        {
            Row(key + ".clone", Tree(clone.transform));
            Destroy(clone);
        }
    }

    static IEnumerator Hierarchy()
    {
        LimbRig.Mode = "";
        yield return AssetEdit("hier.control", "EditNone", null);
        yield return AssetEdit("hier.set_parent_inside", "EditInside", a => { a.transform.Find("Body/Head").SetParent(a.transform.Find("Base")); return "done"; });
        yield return AssetEdit("hier.set_parent_null", "EditNull", a => { a.transform.Find("Body/Head").SetParent(null); return "done"; });
        yield return AssetEdit("hier.parent_property", "EditProperty", a => { a.transform.Find("Body/Head").parent = a.transform.Find("Base"); return "done"; });
        var sceneParent = new GameObject("SceneParent");
        yield return AssetEdit("hier.set_parent_scene", "EditScene", a => { a.transform.Find("Body/Head").SetParent(sceneParent.transform); return Tree(sceneParent.transform); });
        yield return AssetEdit("hier.scene_under_asset", "EditLoose", a => { var loose = new GameObject("Loose"); loose.transform.SetParent(a.transform.Find("Body")); return Where(loose.transform); });
        yield return AssetEdit("hier.sibling_index", "EditSibling", a => { a.transform.Find("Body/Arm").SetSiblingIndex(0); return a.transform.Find("Body/Arm").GetSiblingIndex(); });
        yield return AssetEdit("hier.instantiate_under", "EditInstantiate", a =>
        {
            var piece = Instantiate(Resources.Load<GameObject>("Hier/Piece"), a.transform.Find("Body"));
            return Where(piece.transform);
        });
        yield return AssetEdit("hier.instantiate_under_world", "EditInstantiateWorld", a =>
        {
            var piece = Instantiate(Resources.Load<GameObject>("Hier/Piece"), Vector3.one, Quaternion.identity, a.transform.Find("Body"));
            return Where(piece.transform);
        });
        yield return AssetEdit("hier.detach_children", "EditDetach", a => { a.transform.Find("Body").DetachChildren(); return "done"; });
        yield return AssetEdit("hier.rect_transform", "EditRect", a => a.transform.Find("Body/Arm").gameObject.AddComponent<RectTransform>());
        yield return AssetEdit("hier.destroy_child", "EditDestroy", a => { Destroy(a.transform.Find("Body/Arm").gameObject); return "done"; });
        yield return AssetEdit("hier.new_child", "EditNewChild", a =>
        {
            var child = new GameObject("Fresh");
            child.transform.parent = a.transform.Find("Body/Head");
            return Where(child.transform);
        });

        // OnValidate on the loaded asset itself.
        LimbRig.Mode = "reparent";
        yield return AssetEdit("hier.validate_reparent", "ValidateReparent", null);
        LimbRig.Mode = "add";
        yield return AssetEdit("hier.validate_add", "ValidateAdd", null);
        LimbRig.Mode = "";
        Row("hier.validations", LimbRig.Validations);

        // Nested prefabs and a variant: the rig nested in a box, an object added inside the instance.
        yield return AssetEdit("hier.nested", "Outer", null);
        yield return AssetEdit("hier.variant", "OuterVariant", null);
        LimbRig.Mode = "reparent";
        yield return AssetEdit("hier.nested_validate", "OuterValidate", null);
        LimbRig.Mode = "";
        Destroy(sceneParent);
    }

    static void Reentrant()
    {
        var p1 = new GameObject("P1").transform;
        var p2 = new GameObject("P2").transform;
        var p3 = new GameObject("P3").transform;
        var x = new GameObject("X").transform;
        x.SetParent(p1);
        x.gameObject.AddComponent<ReparentOnBefore>().target = p2;
        Do("reentrant.before", () => x.SetParent(p3));
        Row("reentrant.before.x", Where(x));
        Row("reentrant.before.trees", Tree(p1) + " " + Tree(p2) + " " + Tree(p3));
        Try("reentrant.before.sibling_index", () => x.GetSiblingIndex());
        GameObject copy = null;
        Try("reentrant.before.instantiate_p2", () => copy = Instantiate(p2.gameObject));
        if (copy != null) { Row("reentrant.before.copy_p2", Tree(copy.transform)); Destroy(copy); }
        copy = null;
        Try("reentrant.before.instantiate_p3", () => copy = Instantiate(p3.gameObject));
        if (copy != null) { Row("reentrant.before.copy_p3", Tree(copy.transform)); Destroy(copy); }

        // The interim parent is inactive: the transform hears it is disabled, then enabled again
        // under the parent it asked for.
        var r1 = new GameObject("R1").transform;
        var r2 = new GameObject("R2").transform;
        var r3 = new GameObject("R3").transform;
        r2.gameObject.SetActive(false);
        var z = new GameObject("Z").transform;
        z.SetParent(r1);
        z.gameObject.AddComponent<ReparentOnBefore>().target = r2;
        ProbeLog.Flush("reentrant.inactive.setup");
        Do("reentrant.inactive", () => z.SetParent(r3));
        Row("reentrant.inactive.z", Where(z) + " activeInHierarchy=" + z.gameObject.activeInHierarchy + " enabled=" + z.GetComponent<ReparentOnBefore>().isActiveAndEnabled);
        Row("reentrant.inactive.trees", Tree(r1) + " " + Tree(r2) + " " + Tree(r3));
        foreach (var t in new[] { r1, r2, r3 }) Destroy(t.gameObject);

        var q1 = new GameObject("Q1").transform;
        var q2 = new GameObject("Q2").transform;
        var q3 = new GameObject("Q3").transform;
        var y = new GameObject("Y").transform;
        y.SetParent(q1);
        y.gameObject.AddComponent<ReparentOnAfter>().target = q2;
        Do("reentrant.after", () => y.SetParent(q3));
        Row("reentrant.after.y", Where(y));
        Row("reentrant.after.trees", Tree(q1) + " " + Tree(q2) + " " + Tree(q3));
        foreach (var t in new[] { p1, p2, p3, q1, q2, q3 }) Destroy(t.gameObject);
    }

    // ------------------------------------------------------------------ animator

    static void Animators()
    {
        var anims = GameObject.Find("Anims").transform;
        foreach (var name in new[] { "Bare", "Inactive", "Disabled", "OverrideEmpty", "Override", "Controller" })
        {
            var animator = anims.Find(name).GetComponent<Animator>();
            var key = "animator." + name;
            Row(key + ".controller", animator.runtimeAnimatorController == null ? "none" : animator.runtimeAnimatorController.GetType().Name);
            Try(key + ".initialized", () => animator.isInitialized);
            Do(key + ".set_float", () => animator.SetFloat("speed", 1f));
            Try(key + ".get_float", () => animator.GetFloat("speed"));
            Do(key + ".set_trigger", () => animator.SetTrigger("go"));
            Do(key + ".set_bool_hash", () => animator.SetBool(Animator.StringToHash("flag"), true));
            Try(key + ".parameter_count", () => animator.parameterCount);
            Try(key + ".layer_count", () => animator.layerCount);
            Try(key + ".layer_name", () => animator.GetLayerName(0));
            Try(key + ".state", () => animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"));
            Do(key + ".play", () => animator.Play("Idle"));
        }
        var assign = anims.Find("Bare").GetComponent<Animator>();
        Do("animator.assigned.assign", () => assign.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Anim"));
        Do("animator.assigned.set_float", () => assign.SetFloat("speed", 2f));
        Try("animator.assigned.get_float", () => assign.GetFloat("speed"));
    }

    // ------------------------------------------------------------------ editor stats

    static void Stats()
    {
        // UnityStats.screenRes is the last rendered Game view's size: a batch-mode Editor has no
        // Game view (it answers "0x0"), so only its form is recorded here.
#if UNITY_EDITOR
        Try("stats.screen_res_form", () => System.Text.RegularExpressions.Regex.IsMatch(UnityStats.screenRes ?? "", "^[0-9]+x[0-9]+$"));
#endif
    }

    // ------------------------------------------------------------------ compilation pipeline

#if UNITY_EDITOR
    static string Join(IEnumerable<string> values) => values == null ? "null" : "[" + string.Join(",", values) + "]";

    static string Options(ScriptCompilerOptions o) => o == null ? "null" :
        $"unsafe={o.AllowUnsafeCode} api={o.ApiCompatibilityLevel} optimization={o.CodeOptimization} language={o.LanguageVersion ?? "null"} " +
        $"responseFiles={Join(o.ResponseFiles)} arguments={Join(o.AdditionalCompilerArguments)} analyzers={Join(o.RoslynAnalyzerDllPaths?.Select(System.IO.Path.GetFileName))} " +
        $"ruleset={o.RoslynAnalyzerRulesetPath ?? "null"} config={o.AnalyzerConfigPath ?? "null"} additional={Join(o.RoslynAdditionalFilePaths)} " +
        $"editorCompatibility={o.EditorAssembliesCompatibilityLevel}";

    static string Describe(UnityEditor.Compilation.Assembly a) => a == null ? "null" :
        $"name={a.name} rootNamespace={a.rootNamespace ?? "null"} flags={a.flags} output={a.outputPath} sources={Join(a.sourceFiles)} " +
        $"defines={Join(a.defines)} references={Join(a.assemblyReferences?.Select(r => r.name))} compiled={Join(a.compiledAssemblyReferences)} options={Options(a.compilerOptions)}";
#endif

    static void Compilation()
    {
#if UNITY_EDITOR
        var probeAssemblies = new[] { "Assembly-CSharp", "Assembly-CSharp-Editor", "ProbeUnsafeLib" };
        foreach (var type in new[] { AssembliesType.Editor, AssembliesType.Player })
            foreach (var a in CompilationPipeline.GetAssemblies(type).Where(a => probeAssemblies.Contains(a.name)).OrderBy(a => a.name, StringComparer.Ordinal))
            {
                var key = "compilation." + type + "." + a.name;
                Row(key + ".root_namespace", a.rootNamespace);
                Row(key + ".flags", a.flags.ToString());
                Row(key + ".output", a.outputPath);
                Row(key + ".sources", Join(a.sourceFiles.OrderBy(s => s, StringComparer.Ordinal)));
                Row(key + ".references", Join(a.assemblyReferences.Select(r => r.name).Where(probeAssemblies.Contains).OrderBy(s => s, StringComparer.Ordinal)));
                Row(key + ".plugins", Join(a.compiledAssemblyReferences.Where(r => r.Replace('\\', '/').Contains("Assets/")).OrderBy(s => s, StringComparer.Ordinal)));
                Row(key + ".options", Options(a.compilerOptions));
            }
        Try("compilation.options_new", () => Options(new ScriptCompilerOptions()));
        Try("compilation.ctor_options", () =>
        {
            var o = new ScriptCompilerOptions { AllowUnsafeCode = true, ApiCompatibilityLevel = ApiCompatibilityLevel.NET_Unity_4_8, CodeOptimization = CodeOptimization.Debug };
            return Describe(new UnityEditor.Compilation.Assembly("Made", "Out/Made.dll", new[] { "Assets/A.cs" }, new[] { "MADE" }, new UnityEditor.Compilation.Assembly[0],
                new[] { "Assets/Plugins/ShapeCore.dll" }, AssemblyFlags.EditorAssembly, o, "MadeNs"));
        });
        Try("compilation.ctor_options_no_namespace", () =>
            Describe(new UnityEditor.Compilation.Assembly("Made", "Out/Made.dll", new[] { "Assets/A.cs" }, new string[0], new UnityEditor.Compilation.Assembly[0],
                new string[0], AssemblyFlags.None, new ScriptCompilerOptions())));
        Try("compilation.ctor_plain", () =>
            Describe(new UnityEditor.Compilation.Assembly("Made", "Out/Made.dll", new[] { "Assets/A.cs" }, null, null, null, AssemblyFlags.None)));
        Try("compilation.ctor_null_options", () =>
            Describe(new UnityEditor.Compilation.Assembly("Made", "Out/Made.dll", new string[0], new string[0], new UnityEditor.Compilation.Assembly[0],
                new string[0], AssemblyFlags.None, null, null)));
#endif
    }

    // ------------------------------------------------------------------ timeline

    static void Timelines()
    {
        foreach (var name in new[] { "Cutscene", "CutsceneMissing" })
        {
            var key = "timeline." + name;
            TimelineAsset timeline = null;
            Try(key + ".load", () => timeline = Resources.Load<TimelineAsset>(name));
            if (timeline == null) continue;
            Row(key + ".duration", timeline.duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Row(key + ".root_tracks", timeline.rootTrackCount);
            Row(key + ".output_tracks", timeline.outputTrackCount);
            foreach (var track in timeline.GetOutputTracks())
            {
                Try(key + ".track." + track.name, () => track.GetType().Name + " clips=" + track.GetClips().Count() + " markers=" + track.GetMarkerCount() +
                    " [" + string.Join(",", track.GetMarkers().Select(m => m == null ? "null" : m.GetType().Name + "@" + m.time.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]" +
                    " parent=" + (track.parent == null ? "null" : track.parent.name));
            }
            var markers = timeline.markerTrack;
            Try(key + ".marker_track", () => markers == null ? "none" : markers.GetMarkerCount() + " [" + string.Join(",", markers.GetMarkers().Select(m => m == null ? "null" : m.GetType().Name)) + "]");
#if UNITY_EDITOR
            if (name == "Cutscene") Try(key + ".load_all", () => string.Join(",", AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/" + name + ".playable")
                .Select(o => o == null ? "null" : o.GetType().Name + ":" + o.name).OrderBy(s => s, StringComparer.Ordinal)));
#endif
        }
    }

    // ------------------------------------------------------------------ plugins

    static object Call(Type type, string method, object target = null, params object[] args) =>
        type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).Invoke(target, args);

    static string Types(System.Reflection.Assembly assembly)
    {
        try
        {
            return "types " + string.Join(",", assembly.GetTypes().Select(t => t.FullName).OrderBy(s => s, StringComparer.Ordinal));
        }
        catch (ReflectionTypeLoadException e)
        {
            return "throws ReflectionTypeLoadException; types " + string.Join(",", e.Types.Where(t => t != null).Select(t => t.FullName).OrderBy(s => s, StringComparer.Ordinal)) +
                "; loader " + string.Join(" | ", e.LoaderExceptions.Where(l => l != null).Select(l => l.GetType().Name + ": " + l.Message).Distinct());
        }
    }

    static void Plugins()
    {
        var core = typeof(ShapeCore.Shape).Assembly;
        Row("plugins.core", core.GetName().Name + " " + core.GetName().Version);
        var info = Type.GetType("ShapeExtras.ExtrasInfo, ShapeExtras");
        Row("plugins.extras.info", info != null);
        if (info != null)
        {
            var extras = info.Assembly;
            Row("plugins.extras.reference", string.Join(",", extras.GetReferencedAssemblies().Where(r => r.Name.StartsWith("Shape")).Select(r => r.Name + " " + r.Version)));
            Try("plugins.extras.answer", () => Call(info, "Answer"));
            Try("plugins.extras.core_name", () => Call(info, "CoreName"));
            Try("plugins.extras.types", () => Types(extras));
            Try("plugins.extras.square", () => { var square = Activator.CreateInstance(extras.GetType("ShapeExtras.Square")); return Call(square.GetType(), "Area", square); });
            Try("plugins.extras.circle_type", () => extras.GetType("ShapeExtras.Circle") == null ? "null" : "found");
            Try("plugins.extras.circle_by_name", () => Type.GetType("ShapeExtras.Circle, ShapeExtras") == null ? "null" : "found");
            Try("plugins.extras.circle_create", () => Activator.CreateInstance(extras.GetType("ShapeExtras.Circle", true)).GetType().Name);
        }
        var later = Type.GetType("ShapeLater.LaterInfo, ShapeLater");
        Row("plugins.later.info", later != null);
        if (later != null)
        {
            Row("plugins.later.reference", string.Join(",", later.Assembly.GetReferencedAssemblies().Where(r => r.Name.StartsWith("Shape")).Select(r => r.Name + " " + r.Version)));
            Try("plugins.later.core_version", () => Call(later, "CoreVersion"));
            Try("plugins.later.types", () => Types(later.Assembly));
            Try("plugins.later.triangle", () => ((ShapeCore.Shape)Activator.CreateInstance(later.Assembly.GetType("ShapeLater.Triangle"))).Area(2f));
        }
        Row("plugins.loaded", string.Join(",", AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Where(n => n.StartsWith("Shape")).Distinct().OrderBy(s => s, StringComparer.Ordinal)));
    }
}
