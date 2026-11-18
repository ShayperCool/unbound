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
        s_MainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        Run("twice", Twice);
        Run("constructors", Constructors);
        yield return Hierarchy();
        Run("reentrant", Reentrant);
        Run("animator", Animators);
        Run("stats", Stats);
        Run("compilation", Compilation);
        Run("timeline", Timelines);
        Run("plugins", Plugins);
        Run("rsp", Rsp);
        yield return AsyncParent();
        yield return RuntimeController();
        Run("editor_controller", EditorController);
        yield return ObjectChanges();
        Run("annotation", Annotations);
        Run("builddefines", BuildDefinesRows);
        Run("mat", Materials);
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
    static int s_MainThread;
    static int s_Constructions;

    sealed class SameObject : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }

    /// <summary>The counted scripts' constructions since the previous call: type; "live" for the very
    /// object the scene holds or the call returned, "twin" for another object with that one's
    /// instance ID, "alive"/"dead" for any other (whether it compares unequal to null); "@worker" off
    /// the main thread.</summary>
    static string Constructions(params UnityEngine.Object[] returned)
    {
        List<(object Made, int Thread)> made;
        lock (ConstructionLog.Made) made = ConstructionLog.Made.Skip(s_Constructions).ToList();
        s_Constructions += made.Count;
        var live = new List<UnityEngine.Object>(FindObjectsByType<CountedBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        foreach (var r in returned) if (!ReferenceEquals(r, null)) live.Add(r);
        var same = new HashSet<object>(live, new SameObject());
        return made.Count + ": " + string.Join(" ", made.Select(m =>
        {
            var o = (UnityEngine.Object)m.Made;
            var state = same.Contains(o) ? "live" : live.Any(l => l != null && o.GetInstanceID() == l.GetInstanceID()) ? "twin" : o != null ? "alive" : "dead";
            return o.GetType().Name + ":" + state + (m.Thread == s_MainThread ? "" : "@worker");
        }));
    }

    static void Twice()
    {
        Row("twice.scene", Constructions());
        Row("twice.scene_objects", FindObjectsByType<CountedBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        var host = new GameObject("CountedHost");
        LateCounted late = null;
        Try("twice.add", () => late = host.AddComponent<LateCounted>());
        Row("twice.add.made", Constructions(late));
        Try("twice.add_again", () => late = new GameObject("CountedHost2").AddComponent<LateCounted>());
        Row("twice.add_again.made", Constructions(late));
        CountedData data = null;
        Try("twice.data", () => data = ScriptableObject.CreateInstance<CountedData>());
        Row("twice.data.made", Constructions(data));
        Try("twice.data_again", () => data = ScriptableObject.CreateInstance<CountedData>());
        Row("twice.data_again.made", Constructions(data));
        GameObject prefab = null;
        Try("twice.prefab_load", () => prefab = Resources.Load<GameObject>("Counted"));
        Row("twice.prefab_load.made", Constructions(prefab == null ? null : prefab.GetComponent<PrefabCounted>()));
        GameObject clone = null;
        Try("twice.prefab_instantiate", () => clone = Instantiate(prefab));
        Row("twice.prefab_instantiate.made", Constructions(clone == null ? null : clone.GetComponent<PrefabCounted>()));
        Destroy(host);
    }

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

    /// <summary>Compiler options; with <paramref name="relative"/>, an optimization equal to the
    /// Editor's current one (a user preference of the machine) reads "editor".</summary>
    static string Options(ScriptCompilerOptions o, bool relative = false) => o == null ? "null" :
        $"unsafe={o.AllowUnsafeCode} api={o.ApiCompatibilityLevel} optimization={(relative && o.CodeOptimization == CompilationPipeline.codeOptimization ? "editor" : o.CodeOptimization.ToString())} language={o.LanguageVersion ?? "null"} " +
        $"responseFiles={Join(o.ResponseFiles?.Select(Rel))} arguments={Join(o.AdditionalCompilerArguments)} analyzers={Join(o.RoslynAnalyzerDllPaths?.Select(System.IO.Path.GetFileName))} " +
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
                Row(key + ".options", Options(a.compilerOptions, relative: type == AssembliesType.Editor));
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
            Try("plugins.extras.circle_create_again", () => Activator.CreateInstance(extras.GetType("ShapeExtras.Circle", true)).GetType().Name);
            Try("plugins.extras.circle_base", () => extras.GetType("ShapeExtras.Circle", true).BaseType.FullName);
            Try("plugins.extras.circle_methods", () => string.Join(",", extras.GetType("ShapeExtras.Circle", true)
                .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name).OrderBy(s => s, StringComparer.Ordinal)));
            Try("plugins.extras.circle_static", () => Call(extras.GetType("ShapeExtras.Circle", true), "Count"));
            Try("plugins.extras.pentagon_create", () => Activator.CreateInstance(extras.GetType("ShapeExtras.Pentagon", true)).GetType().Name);
            Try("plugins.extras.ruler_create", () => Activator.CreateInstance(extras.GetType("ShapeExtras.Ruler", true)).GetType().Name);
            Try("plugins.extras.ruler_interfaces", () => string.Join(",", extras.GetType("ShapeExtras.Ruler", true).GetInterfaces().Select(i => i.FullName)));
            Try("plugins.extras.floatplug_create", () => Activator.CreateInstance(extras.GetType("ShapeExtras.FloatPlug", true)).GetType().Name);
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
    // ------------------------------------------------------------------ response files

    static string Rel(string path)
    {
        if (path == null) return "null";
        path = path.Replace('\\', '/');
        var root = System.IO.Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        return path.StartsWith(root + "/", StringComparison.Ordinal) ? "<project>/" + path.Substring(root.Length + 1) : path;
    }

    /// <summary>Whether the compiler recorded nullable annotations in the type (only in a nullable
    /// context does it emit its Nullable attributes).</summary>
    static bool Nullable(Type type)
    {
        bool Has(IEnumerable<CustomAttributeData> attributes) => attributes.Any(a => a.AttributeType.Name.StartsWith("Nullable", StringComparison.Ordinal));
        var echo = type.GetMethod("Echo");
        return Has(type.CustomAttributes) || echo != null && (Has(echo.CustomAttributes) || echo.GetParameters().Any(p => Has(p.CustomAttributes)) || Has(echo.ReturnParameter.CustomAttributes));
    }

    static readonly (string Assembly, string Type)[] s_RspReports =
    {
        ("Assembly-CSharp", "RspReportMain"), ("Assembly-CSharp-Editor", "RspReportEditor"), ("ProbeRspLocal", "RspReportLocal"),
        ("ProbeRspInherit", "RspReportInherit"), ("ProbeRspConstrained", "RspReportConstrained"), ("ProbeRspLocalOnly", "RspReportLocalOnly"),
        ("ProbeRspPackage", "RspReportPackage"), ("ProbeRspPackageLocal", "RspReportPackageLocal"),
    };

    static void Rsp()
    {
        foreach (var (assembly, typeName) in s_RspReports)
        {
            var key = "rsp." + assembly;
            var type = Type.GetType(typeName + ", " + assembly);
            Row(key + ".present", type != null);
            if (type == null) continue;
            Try(key + ".defines", () => Call(type, "Defines"));
            Row(key + ".nullable", Nullable(type));
        }
        Try("rsp.unsafe", () => Call(Type.GetType("RspReportInherit, ProbeRspInherit", true), "Unsafe"));
        Try("rsp.reference", () => RspReportMain.Reference());
        Try("rsp.langversion", () => Type.GetType("RspReportLocal, ProbeRspLocal", true).GetField("Version").GetValue(null));
#if UNITY_EDITOR
        var project = System.IO.Path.GetDirectoryName(Application.dataPath);
        foreach (var (key, file) in new[] { ("rsp.parse.project", "Assets/csc.rsp"), ("rsp.parse.local", "Assets/RspLocal/csc.rsp"), ("rsp.parse.missing", "Assets/None/csc.rsp") })
            Try(key, () =>
            {
                var data = CompilationPipeline.ParseResponseFile(file, project, new string[0]);
                return $"defines={Join(data.Defines)} references={Join(data.FullPathReferences?.Select(Rel))} unsafe={data.Unsafe} errors={Join(data.Errors)} other={Join(data.OtherArguments)}";
            });
        Try("rsp.obsolete_names", () =>
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.Scripting.Compilers.CompilerSpecificResponseFiles");
            var field = type?.GetField("MicrosoftCSharpCompilerObsolete", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field == null ? "none" : Join((string[])field.GetValue(null));
        });
        var names = s_RspReports.Select(r => r.Assembly).ToArray();
        foreach (var a in CompilationPipeline.GetAssemblies(AssembliesType.Editor).Where(a => names.Contains(a.name)).OrderBy(a => a.name, StringComparer.Ordinal))
        {
            Row("rsp.pipeline." + a.name + ".response_files", Join(a.compilerOptions.ResponseFiles?.Select(Rel)));
            Row("rsp.pipeline." + a.name + ".unsafe", a.compilerOptions.AllowUnsafeCode);
            Row("rsp.pipeline." + a.name + ".defines", Join(a.defines.Where(d => d.StartsWith("RSP_") || d.StartsWith("PROBE_")).OrderBy(s => s, StringComparer.Ordinal)));
            Row("rsp.pipeline." + a.name + ".arguments", Join(a.compilerOptions.AdditionalCompilerArguments));
        }
        Row("rsp.pipeline.listed", Join(CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name).Where(names.Contains).OrderBy(s => s, StringComparer.Ordinal)));
        Try("rsp.defines_from_name", () => Join(CompilationPipeline.GetDefinesFromAssemblyName("ProbeRspInherit")?.Where(d => d.StartsWith("RSP_")).OrderBy(s => s, StringComparer.Ordinal)));
#endif
    }

    // ------------------------------------------------------------------ InstantiateAsync with a parent in an asset

    static string Results(AsyncInstantiateOperation<GameObject> op) => op == null ? "no operation" :
        $"done={op.isDone} count={op.Result?.Length ?? -1} " + string.Join(" ", (op.Result ?? new GameObject[0]).Select(g => g == null ? "null" : g.name + ":" + Where(g.transform)));

    static IEnumerator AsyncParent()
    {
        var original = Resources.Load<GameObject>("Hier/Piece");
        var asset = Resources.Load<GameObject>("Hier/EditNone").transform;
        var scene = new GameObject("AsyncSceneParent").transform;
        var cases = new (string Key, Func<AsyncInstantiateOperation<GameObject>> Start)[]
        {
            ("async.asset_parent", () => InstantiateAsync(original, asset)),
            ("async.asset_parent_count", () => InstantiateAsync(original, 2, asset, new Vector3(1, 2, 3), Quaternion.identity)),
            ("async.asset_parent_parameters", () => InstantiateAsync(original, new InstantiateParameters { parent = asset, worldSpace = false })),
            ("async.scene_parent", () => InstantiateAsync(original, scene)),
        };
        foreach (var (key, start) in cases)
        {
            AsyncInstantiateOperation<GameObject> op = null;
            Try(key + ".start", () => { op = start(); return op == null ? "null" : "operation"; });
            for (int i = 0; i < 30 && op != null && !op.isDone; i++) yield return null;
            Row(key + ".result", Results(op));
            ProbeLog.Flush(key + ".after");
            if (op?.Result != null) foreach (var g in op.Result) if (g != null) Destroy(g);
        }
        Row("async.asset_tree", Tree(asset));
        Destroy(scene.gameObject);
    }

    // ------------------------------------------------------------------ invalid controller assigned while running

    static IEnumerator RuntimeController()
    {
        var go = new GameObject("RuntimeAnimator");
        var animator = go.AddComponent<Animator>();
        const string key = "animator.runtime_invalid";
        Do(key + ".assign", () => animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("OverrideEmpty"));
        Row(key + ".controller", animator.runtimeAnimatorController == null ? "none" : animator.runtimeAnimatorController.GetType().Name);
        Try(key + ".initialized", () => animator.isInitialized);
        Do(key + ".set_float", () => animator.SetFloat("speed", 1f));
        Try(key + ".parameter_count", () => animator.parameterCount);
        Try(key + ".layer_count", () => animator.layerCount);
        Do(key + ".play", () => animator.Play("Idle"));
        yield return null;
        yield return null;
        ProbeLog.Flush(key + ".frames");
        Do(key + ".deactivate", () => go.SetActive(false));
        Do(key + ".activate", () => go.SetActive(true));
        Try(key + ".initialized_after_activate", () => animator.isInitialized);
        Do(key + ".reassign_valid", () => animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Anim"));
        Try(key + ".valid.initialized", () => animator.isInitialized);
        Do(key + ".valid.set_float", () => animator.SetFloat("speed", 3f));
        Try(key + ".valid.get_float", () => animator.GetFloat("speed"));
        Try(key + ".valid.parameter_count", () => animator.parameterCount);
        Do(key + ".back_to_invalid", () => animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("OverrideEmpty"));
        Try(key + ".back.initialized", () => animator.isInitialized);
        Do(key + ".back.set_float", () => animator.SetFloat("speed", 4f));
        yield return null;
        ProbeLog.Flush(key + ".back.frames");
        Destroy(go);
    }

    // ------------------------------------------------------------------ the Editor's controller class

    static void EditorController()
    {
#if UNITY_EDITOR
        var loaded = Resources.Load<RuntimeAnimatorController>("Anim");
        Row("editor_controller.type", loaded == null ? "none" : loaded.GetType().FullName);
        Row("editor_controller.override_type", Resources.Load<RuntimeAnimatorController>("Override")?.GetType().FullName ?? "none");
        var controller = loaded as UnityEditor.Animations.AnimatorController;
        Row("editor_controller.is_editor_class", controller != null);
        if (controller == null) return;
        Try("editor_controller.parameters", () => string.Join(" ", controller.parameters.Select(p =>
            $"{p.name}:{p.type}:{p.defaultFloat}/{p.defaultInt}/{p.defaultBool}:{p.nameHash}")));
        Try("editor_controller.layers", () => string.Join(" ", controller.layers.Select(l =>
            $"{l.name}:{l.defaultWeight}:{l.blendingMode}:{l.syncedLayerIndex}:{l.iKPass}:{(l.stateMachine == null ? "null" : l.stateMachine.name)}")));
        Try("editor_controller.states", () => string.Join(" ", controller.layers[0].stateMachine.states.Select(s => s.state.name + ":" + s.state.speed + ":" + s.state.nameHash)));
        Try("editor_controller.default_state", () => controller.layers[0].stateMachine.defaultState?.name ?? "null");
        Try("editor_controller.clips", () => controller.animationClips.Length);
        Try("editor_controller.name", () => controller.name);
#endif
    }

    // ------------------------------------------------------------------ ObjectChangeEvents

#if UNITY_EDITOR
    static readonly List<string> s_Changes = new List<string>();
    static int s_ChangeFrame;

    static string Named(int id)
    {
        if (id == 0) return "0";
        var o = EditorUtility.InstanceIDToObject(id);
        return o == null ? "missing" : o.GetType().Name + "'" + o.name + "'";
    }

    static void OnChanges(ref ObjectChangeEventStream stream)
    {
        var parts = new List<string>();
        for (int i = 0; i < stream.length; i++)
        {
            var kind = stream.GetEventType(i);
            string detail;
            switch (kind)
            {
                case ObjectChangeKind.ChangeScene: stream.GetChangeSceneEvent(i, out var scene); detail = scene.scene.name; break;
                case ObjectChangeKind.CreateGameObjectHierarchy: stream.GetCreateGameObjectHierarchyEvent(i, out var create); detail = Named(create.instanceId) + " " + create.scene.name; break;
                case ObjectChangeKind.DestroyGameObjectHierarchy: stream.GetDestroyGameObjectHierarchyEvent(i, out var destroy); detail = Named(destroy.instanceId) + " parent=" + Named(destroy.parentInstanceId) + " " + destroy.scene.name; break;
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy: stream.GetChangeGameObjectStructureHierarchyEvent(i, out var hierarchy); detail = Named(hierarchy.instanceId) + " " + hierarchy.scene.name; break;
                case ObjectChangeKind.ChangeGameObjectStructure: stream.GetChangeGameObjectStructureEvent(i, out var structure); detail = Named(structure.instanceId) + " " + structure.scene.name; break;
                case ObjectChangeKind.ChangeGameObjectParent: stream.GetChangeGameObjectParentEvent(i, out var parent); detail = Named(parent.instanceId) + " from=" + Named(parent.previousParentInstanceId) + " to=" + Named(parent.newParentInstanceId) + " " + parent.previousScene.name + ">" + parent.newScene.name; break;
                case ObjectChangeKind.ChangeChildrenOrder: stream.GetChangeChildrenOrderEvent(i, out var children); detail = Named(children.instanceId) + " " + children.scene.name; break;
                case ObjectChangeKind.ChangeRootOrder: stream.GetChangeRootOrderEvent(i, out var root); detail = Named(root.instanceId) + " " + root.scene.name; break;
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties: stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties); detail = Named(properties.instanceId) + " " + properties.scene.name; break;
                case ObjectChangeKind.CreateAssetObject: stream.GetCreateAssetObjectEvent(i, out var createAsset); detail = Named(createAsset.instanceId); break;
                case ObjectChangeKind.DestroyAssetObject: stream.GetDestroyAssetObjectEvent(i, out var destroyAsset); detail = Named(destroyAsset.instanceId); break;
                case ObjectChangeKind.ChangeAssetObjectProperties: stream.GetChangeAssetObjectPropertiesEvent(i, out var assetProperties); detail = Named(assetProperties.instanceId); break;
                case ObjectChangeKind.UpdatePrefabInstances: stream.GetUpdatePrefabInstancesEvent(i, out var prefabs); detail = prefabs.instanceIds.Length + " " + prefabs.scene.name; break;
                default: detail = ""; break;
            }
            parts.Add(kind + " " + detail);
        }
        s_Changes.Add("+" + (Time.frameCount - s_ChangeFrame) + " " + string.Join("; ", parts));
    }
#endif

    static IEnumerator ObjectChanges()
    {
#if UNITY_EDITOR
        var target = new GameObject("ChangeTarget");
        var child = new GameObject("ChangeChild");
        var doomed = new GameObject("ChangeDoomed");
        var data = ScriptableObject.CreateInstance<CountedData>();
        data.name = "ChangeData";
        yield return null;
        ObjectChangeEvents.changesPublished += OnChanges;
        var steps = new (string Key, Action Change)[]
        {
            ("record", () => { Undo.RecordObject(target.transform, "move"); target.transform.position = new Vector3(1, 2, 3); }),
            ("plain", () => target.transform.position = new Vector3(4, 5, 6)),
            ("created", () => Undo.RegisterCreatedObjectUndo(new GameObject("UndoMade"), "make")),
            ("add_component", () => Undo.AddComponent<BoxCollider>(target)),
            ("set_parent", () => Undo.SetTransformParent(child.transform, target.transform, "parent")),
            ("destroy", () => Undo.DestroyObjectImmediate(doomed)),
            ("record_data", () => { Undo.RecordObject(data, "data"); data.number = 5; }),
            ("two_in_one_frame", () => { Undo.RecordObject(target, "rename"); target.name = "ChangeTargetRenamed"; Undo.RegisterCreatedObjectUndo(new GameObject("UndoMade2"), "make2"); }),
            ("undo", () => Undo.PerformUndo()),
            ("redo", () => Undo.PerformRedo()),
        };
        foreach (var (key, change) in steps)
        {
            s_Changes.Clear();
            s_ChangeFrame = Time.frameCount;
            Do("objectchange." + key, change);
            for (int i = 0; i < 3; i++) yield return null;
            Row("objectchange." + key + ".published", s_Changes.Count == 0 ? "none" : string.Join(" | ", s_Changes));
        }
        ObjectChangeEvents.changesPublished -= OnChanges;
        Undo.ClearAll();
#else
        yield break;
#endif
    }

    // ------------------------------------------------------------------ gizmo annotations

    static void Annotations()
    {
#if UNITY_EDITOR
        var util = typeof(Editor).Assembly.GetType("UnityEditor.AnnotationUtility");
        var annotationType = typeof(Editor).Assembly.GetType("UnityEditor.Annotation");
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var get = util.GetMethod("GetAnnotations", any);
        string Field(object a, string name) => annotationType.GetField(name).GetValue(a)?.ToString() ?? "null";
        List<object> All() => ((Array)get.Invoke(null, null)).Cast<object>().ToList();
        var all = All();
        Row("annotation.count", all.Count);
        Row("annotation.scripts", string.Join(" ", all.Where(a => Field(a, "scriptClass") != "").Select(a =>
            $"{Field(a, "scriptClass")}:{Field(a, "classID")}:{Field(a, "iconEnabled")}:{Field(a, "gizmoEnabled")}:{Field(a, "flags")}").OrderBy(s => s, StringComparer.Ordinal)));
        Row("annotation.builtin", string.Join(" ", all.Where(a => Field(a, "scriptClass") == "").Select(a =>
            $"{Field(a, "classID")}:{Field(a, "iconEnabled")}:{Field(a, "gizmoEnabled")}:{Field(a, "flags")}").OrderBy(s => int.Parse(s.Split(':')[0]))));
        string Entry(int classID, string script) => string.Join(",", All().Where(a => Field(a, "classID") == classID.ToString() && Field(a, "scriptClass") == script)
            .Select(a => $"icon={Field(a, "iconEnabled")} gizmo={Field(a, "gizmoEnabled")} flags={Field(a, "flags")}"));
        var setIcon = util.GetMethod("SetIconEnabled", any);
        var setGizmo = util.GetMethod("SetGizmoEnabled", any);
        Try("annotation.set_icon.script", () => { setIcon.Invoke(null, new object[] { 114, "GizmoProbe", 0 }); return Entry(114, "GizmoProbe"); });
        Try("annotation.set_icon.builtin", () => { setIcon.Invoke(null, new object[] { 20, "", 0 }); return Entry(20, ""); });
        Try("annotation.set_icon.unlisted", () => { setIcon.Invoke(null, new object[] { 114, "CountedBehaviour", 0 }); return Entry(114, "CountedBehaviour"); });
        Try("annotation.set_gizmo.builtin", () => { setGizmo.Invoke(null, new object[] { 20, "", 0, false }); return Entry(20, ""); });
        Try("annotation.restore", () =>
        {
            setIcon.Invoke(null, new object[] { 114, "GizmoProbe", 1 });
            setIcon.Invoke(null, new object[] { 20, "", 1 });
            setGizmo.Invoke(null, new object[] { 20, "", 1, false });
            return Entry(114, "GizmoProbe") + " " + Entry(20, "");
        });
#endif
    }

    // ------------------------------------------------------------------ BuildDefines delegates

    static void BuildDefinesRows()
    {
#if UNITY_EDITOR
        var hook = Type.GetType("ProbeDefinesHook, Assembly-CSharp-Editor");
        Row("builddefines.registered", hook?.GetField("Registered").GetValue(null)?.ToString() ?? "no hook");
        int Calls() => (int)hook.GetField("Calls").GetValue(null);
        Row("builddefines.calls_before", Calls() > 0);
        Row("builddefines.compiled_main", RspReportMain.Defines().Contains("PROBE_DELEGATE_DEFINE"));
        var buildDefines = typeof(Editor).Assembly.GetType("UnityEditor.Build.BuildDefines");
        var get = buildDefines?.GetMethod("GetScriptCompilationDefines", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var before = Calls();
        Try("builddefines.get", () =>
        {
            var result = (string[])get.Invoke(null, new object[] { EditorUserBuildSettings.activeBuildTarget, new[] { "INPUT_A" } });
            return Join(result.Where(d => d == "INPUT_A" || d == "PROBE_DELEGATE_DEFINE" || d == "UNITY_EDITOR"));
        });
        Row("builddefines.get_calls", Calls() - before);
        Try("builddefines.active", () => EditorUserBuildSettings.activeScriptCompilationDefines.Contains("PROBE_DELEGATE_DEFINE"));
        Try("builddefines.pipeline", () => CompilationPipeline.GetDefinesFromAssemblyName("Assembly-CSharp").Contains("PROBE_DELEGATE_DEFINE"));
#endif
    }

    // ------------------------------------------------------------------ material serialized objects

#if UNITY_EDITOR
    static SerializedProperty Saved(SerializedObject so, string list, string name)
    {
        var array = so.FindProperty("m_SavedProperties." + list);
        for (int i = 0; i < array.arraySize; i++)
        {
            var entry = array.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("first").stringValue == name) return entry.FindPropertyRelative("second");
        }
        return null;
    }

    static string Keywords(Material m) => "[" + string.Join(",", m.shaderKeywords.OrderBy(s => s, StringComparer.Ordinal)) + "]";

    static string State(Material m) => m == null ? "none" :
        $"queue={m.renderQueue} keywords={Keywords(m)} gloss={m.GetFloat("_Glossiness")} metal={m.GetFloat("_Metallic")} color={m.GetColor("_Color")}";
#endif

    static void Materials()
    {
#if UNITY_EDITOR
        var variant = Resources.Load<Material>("MatVariant");
        Row("mat.variant.load", $"parent={(variant.parent == null ? "none" : variant.parent.name)} variant={variant.isVariant} " + State(variant));
        Try("mat.variant.overrides", () => $"color={variant.IsPropertyOverriden("_Color")} gloss={variant.IsPropertyOverriden("_Glossiness")} queue={variant.IsPropertyOverriden("m_CustomRenderQueue")}");
        var vso = new SerializedObject(variant);
        Try("mat.variant.so_before", () => $"queue={vso.FindProperty("m_CustomRenderQueue").intValue} valid={vso.FindProperty("m_ValidKeywords")?.arraySize}");
        Try("mat.variant.apply", () =>
        {
            vso.FindProperty("m_CustomRenderQueue").intValue = 2300;
            var valid = vso.FindProperty("m_ValidKeywords");
            valid.arraySize++;
            valid.GetArrayElementAtIndex(valid.arraySize - 1).stringValue = "_ALPHATEST_ON";
            return vso.ApplyModifiedPropertiesWithoutUndo();
        });
        Row("mat.variant.applied", State(variant));
        Try("mat.variant.applied_overrides", () => $"color={variant.IsPropertyOverriden("_Color")} queue={variant.IsPropertyOverriden("m_CustomRenderQueue")}");
        Try("mat.variant.touch", () => { variant.SetFloat("_Metallic", 0.75f); return State(variant); });
        Try("mat.variant.so_after", () => { var again = new SerializedObject(variant); return $"queue={again.FindProperty("m_CustomRenderQueue").intValue} keywords={Join(Enumerable.Range(0, again.FindProperty("m_ValidKeywords").arraySize).Select(i => again.FindProperty("m_ValidKeywords").GetArrayElementAtIndex(i).stringValue))}"; });
        Row("mat.variant.parent_after", State(Resources.Load<Material>("MatA")));
        var a = Resources.Load<Material>("MatA");
        var b = Resources.Load<Material>("MatB");
        Row("mat.multi.before", "a: " + State(a) + " b: " + State(b));
        var so = new SerializedObject(new UnityEngine.Object[] { a, b });
        Row("mat.multi.targets", so.targetObjects.Length + " multiple=" + so.isEditingMultipleObjects);
        Try("mat.multi.gloss", () => { var p = Saved(so, "m_Floats", "_Glossiness"); return p.floatValue + " mixed=" + p.hasMultipleDifferentValues; });
        Try("mat.multi.queue", () => { var p = so.FindProperty("m_CustomRenderQueue"); return p.intValue + " mixed=" + p.hasMultipleDifferentValues; });
        Try("mat.multi.apply", () =>
        {
            Saved(so, "m_Floats", "_Glossiness").floatValue = 0.25f;
            so.FindProperty("m_CustomRenderQueue").intValue = 2400;
            return so.ApplyModifiedPropertiesWithoutUndo();
        });
        Row("mat.multi.after", "a: " + State(a) + " b: " + State(b));

#endif
    }
}
