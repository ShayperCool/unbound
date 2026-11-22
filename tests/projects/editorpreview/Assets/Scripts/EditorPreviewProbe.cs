using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;

// A preview that records what DrawPreview asks of it.
public class RecordingPreview : ObjectPreview
{
    public readonly List<string> calls = new List<string>();
    public bool has;
    public string info = "";

    public override bool HasPreviewGUI() => has;
    public override string GetInfoString() => info;
    public override bool MoveNextTarget() { bool more = base.MoveNextTarget(); calls.Add("next:" + m_ReferenceTargetIndex + ":" + more); return more; }
    public override void ResetTarget() { base.ResetTarget(); calls.Add("reset"); }
    public override void OnPreviewGUI(Rect r, GUIStyle background) => calls.Add($"gui:{Event.current.type}:{r.x},{r.y},{r.width},{r.height}:{(background == null ? "null" : background.name)}:{m_ReferenceTargetIndex}");
    public int Index => m_ReferenceTargetIndex;
    public UnityEngine.Object[] Targets => m_Targets;
}

public class EditorPreviewTargetEditor : Editor
{
    public static readonly List<string> calls = new List<string>();
    public override void OnPreviewGUI(Rect r, GUIStyle background)
    {
        calls.Add($"gui:{Event.current.type}:{r.x},{r.y},{r.width},{r.height}:{(background == null ? "null" : background.name)}:{target.name}:{targets.Length}");
        if (readSerializedObject) calls.Add("serialized:" + (serializedObject != null));
    }
    public static bool readSerializedObject;
    public void Excluding(params string[] names) => DrawPropertiesExcluding(serializedObject, names);
}
#endif

// Object previews: ObjectPreview's defaults and target walking, an Editor's preview members,
// DrawPreview's calls and DrawPropertiesExcluding.
public class EditorPreviewProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("EditorPreviewProbe");
        var doc = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        doc.panelSettings = settings;
        go.AddComponent<EditorPreviewProbe>();
    }

    IEnumerator Start()
    {
        yield return null;
        var dir = Environment.GetEnvironmentVariable("EDITOR_PREVIEW_OUT");
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        var run = Run();
        while (true)
        {
            object current;
            try
            {
                if (!run.MoveNext()) break;
                current = run.Current;
            }
            catch (Exception e)
            {
                Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                break;
            }
            yield return current;
        }
        m_Out.Append("{\"k\":\"logs\",\"v\":[").Append(string.Join(",", m_Logs.Select(Json))).Append("]}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

    static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static string Try(Func<object> f)
    {
        try
        {
            var v = f();
            return v == null ? "null" : v is string s ? "'" + s + "'" : v is UnityEngine.Object o ? "obj:" + o.name : v.ToString();
        }
        catch (Exception e) { return e.GetType().Name + (e is InvalidOperationException ? ":" + e.Message : ""); }
    }

    IEnumerator Run()
    {
#if UNITY_EDITOR
        var a = ScriptableObject.CreateInstance<EditorPreviewTarget>(); a.name = "Alpha";
        var b = ScriptableObject.CreateInstance<EditorPreviewTarget>(); b.name = "Beta";
        var c = ScriptableObject.CreateInstance<EditorPreviewTarget>(); c.name = "Gamma";
        var materials = new UnityEngine.Object[] { new Material(Shader.Find("Sprites/Default")) { name = "M1" }, new Material(Shader.Find("Sprites/Default")) { name = "M2" } };

        // 1. A preview before and after Initialize.
        var preview = new RecordingPreview();
        Put("preview.new", $"targets={(preview.Targets == null ? "null" : preview.Targets.Length.ToString())}|index={preview.Index}|target={Try(() => preview.target)}|title={Try(() => preview.GetPreviewTitle().text)}");
        var plain = new ObjectPreview();
        plain.Initialize(new UnityEngine.Object[] { a });
        var title = plain.GetPreviewTitle();
        Put("preview.one", $"has={plain.HasPreviewGUI()}|title='{title.text}'|sameTitle={ReferenceEquals(title, plain.GetPreviewTitle())}|info='{plain.GetInfoString()}'|create={Try(() => plain.CreatePreview(new VisualElement()))}|target={Try(() => plain.target)}");
        plain.Cleanup();
        plain.Initialize(new UnityEngine.Object[] { a, b, c });
        Put("preview.three", $"title='{plain.GetPreviewTitle().text}'|target={Try(() => plain.target)}");
        var walk = new List<string>();
        for (int i = 0; i < 4; i++) walk.Add(Try(() => plain.MoveNextTarget()) + ">" + Try(() => plain.target));
        plain.ResetTarget();
        walk.Add("reset>" + Try(() => plain.target));
        Put("preview.walk", string.Join("|", walk));
        plain.Initialize(materials);
        Put("preview.native", $"title='{plain.GetPreviewTitle().text}'");
        plain.Initialize(new UnityEngine.Object[0]);
        Put("preview.none", $"title={Try(() => plain.GetPreviewTitle().text)}|target={Try(() => plain.target)}|next={Try(() => plain.MoveNextTarget())}");
        plain.Initialize(null);
        Put("preview.null", $"title={Try(() => plain.GetPreviewTitle().text)}|target={Try(() => plain.target)}");
        plain.Cleanup();
        Put("attribute", ((Type)typeof(CustomPreviewAttribute).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(new CustomPreviewAttribute(typeof(Material)))).Name);

        // Titles and class names of objects.
        var go = new GameObject("Go");
        var filter = go.AddComponent<MeshFilter>();
#pragma warning disable 618
        var objects = new (string, UnityEngine.Object)[]
        {
            ("gameObject", go), ("transform", go.transform), ("light", go.AddComponent<Light>()), ("camera", go.AddComponent<Camera>()),
            ("sprite", new GameObject("Sprite").AddComponent<SpriteRenderer>()), ("line", go.AddComponent<LineRenderer>()), ("meshFilter", filter),
            ("behaviour", go.AddComponent<EditorPreviewBehaviour>()), ("menu", go.AddComponent<EditorPreviewMenuBehaviour>()),
            ("derivedMenu", go.AddComponent<EditorPreviewDerivedMenuBehaviour>()), ("hidden", go.AddComponent<EditorPreviewHiddenBehaviour>()), ("trailing", go.AddComponent<EditorPreviewTrailingBehaviour>()),
            ("old", go.AddComponent<EditorPreviewOldBehaviour>()), ("scriptable", a), ("material", materials[0]),
            ("texture", new Texture2D(2, 2) { name = "Tex" }), ("mesh", new Mesh { name = "myMesh" }), ("null", null),
        };
#pragma warning restore 618
        var typeName = typeof(ObjectNames).GetMethod("GetTypeName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var (key, obj) in objects)
            Put("names." + key, $"title={Try(() => ObjectNames.GetInspectorTitle(obj))}|multi={Try(() => ObjectNames.GetInspectorTitle(obj, true))}|class={Try(() => ObjectNames.GetClassName(obj))}|type={Try(() => typeName.Invoke(null, new object[] { obj }))}");
        filter.sharedMesh = (Mesh)objects[16].Item2;
        Put("names.meshFilter.mesh", $"title={Try(() => ObjectNames.GetInspectorTitle(filter))}|multi={Try(() => ObjectNames.GetInspectorTitle(filter, true))}");
        var destroyed = ScriptableObject.CreateInstance<EditorPreviewTarget>();
        DestroyImmediate(destroyed);
        var destroyedMaterial = new Material(Shader.Find("Sprites/Default"));
        DestroyImmediate(destroyedMaterial);
        Put("names.destroyed", $"script={Try(() => ObjectNames.GetInspectorTitle(destroyed))}|material={Try(() => ObjectNames.GetInspectorTitle(destroyedMaterial))}|class={Try(() => ObjectNames.GetClassName(destroyedMaterial))}");
#pragma warning disable 618
        Put("names.obsolete", $"{Try(() => ObjectNames.MangleVariableName("m_someName"))}|{Try(() => ObjectNames.GetPropertyEditorTitle(go.transform))}");
#pragma warning restore 618

        // 2. An editor's preview members.
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var one = Editor.CreateEditor(a, typeof(EditorPreviewTargetEditor));
        var three = Editor.CreateEditor(new UnityEngine.Object[] { a, b, c }, typeof(EditorPreviewTargetEditor));
        var generic = Editor.CreateEditor(a);
        Func<Editor, string, string> internalValue = (e, name) => Try(() => typeof(Editor).GetProperty(name, flags).GetValue(e));
        Put("editor.one", $"has={one.HasPreviewGUI()}|title='{one.GetPreviewTitle().text}'|info='{one.GetInfoString()}'|static={Try(() => one.RenderStaticPreview("Assets/x.asset", null, 16, 16))}|create={Try(() => one.CreatePreview(new VisualElement()))}|targetTitle={internalValue(one, "targetTitle")}|index={internalValue(one, "referenceTargetIndex")}");
        Put("editor.generic", $"type={generic.GetType().Name}|has={generic.HasPreviewGUI()}|title='{generic.GetPreviewTitle().text}'|info='{generic.GetInfoString()}'");
        Put("editor.three", $"title='{three.GetPreviewTitle().text}'|targetTitle={internalValue(three, "targetTitle")}|target={three.target.name}");
        walk.Clear();
        for (int i = 0; i < 4; i++) walk.Add(Try(() => three.MoveNextTarget()) + ">" + Try(() => three.target) + ">" + internalValue(three, "referenceTargetIndex"));
        three.ResetTarget();
        walk.Add("reset>" + Try(() => three.target));
        Put("editor.walk", string.Join("|", walk));
        Put("editor.cleanup", Try(() => { one.Cleanup(); return "ok"; }));
        Put("editor.settings", Try(() => { one.OnPreviewSettings(); one.ReloadPreviewInstances(); return "ok"; }));
        Put("editor.outsideGUI", Try(() => { one.OnInteractivePreviewGUI(new Rect(0, 0, 10, 10), GUIStyle.none); return string.Join(";", EditorPreviewTargetEditor.calls); }));
        EditorPreviewTargetEditor.calls.Clear();

        // 3. DrawPreview in a GUI pass: one target, then several.
        var single = new RecordingPreview { info = "line one\nline two" };
        single.Initialize(new UnityEngine.Object[] { a });
        var many = new RecordingPreview();
        many.Initialize(new UnityEngine.Object[] { a, b, c });
        string drawError = "";
        var area = new Rect(4, 6, 300, 200);
        var container = new IMGUIContainer(() =>
        {
            if (Event.current.type != EventType.KeyDown) return;
            try
            {
                single.DrawPreview(area);
                many.DrawPreview(area);
                one.DrawPreview(area);
                three.DrawPreview(area);
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { drawError = e.GetType().Name + ":" + e.Message; }
        });
        container.style.width = 400f;
        container.style.height = 300f;
        m_Root.Add(container);
        yield return null;
        yield return null;
        using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }))
        {
            evt.target = container;
            container.SendEvent(evt);
        }
        Put("draw.single", string.Join(";", single.calls));
        Put("draw.many", string.Join(";", many.calls));
        Put("draw.editors", string.Join(";", EditorPreviewTargetEditor.calls));
        Put("draw.error", drawError);
        Put("draw.after", $"many={many.Index}|three={three.target.name}");
        // The serialized object read inside a preview of several targets.
        EditorPreviewTargetEditor.calls.Clear();
        EditorPreviewTargetEditor.readSerializedObject = true;
        var again = new IMGUIContainer(() =>
        {
            if (Event.current.type == EventType.KeyDown) three.DrawPreview(area);
        });
        again.style.width = 400f;
        again.style.height = 300f;
        m_Root.Add(again);
        yield return null;
        yield return null;
        using (var evt = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.A }))
        {
            evt.target = again;
            again.SendEvent(evt);
        }
        EditorPreviewTargetEditor.readSerializedObject = false;
        Put("draw.serialized", string.Join(";", EditorPreviewTargetEditor.calls.Where(c => c.StartsWith("serialized"))));
        again.RemoveFromHierarchy();
        container.RemoveFromHierarchy();
        single.Cleanup();
        many.Cleanup();
        preview.Cleanup();

        // 4. DrawPropertiesExcluding: the height of what it draws.
        var excluding = new Dictionary<string, float>();
        var editor = (EditorPreviewTargetEditor)one;
        string[][] cases = { new string[0], new[] { "second" }, new[] { "m_Script" }, new[] { "m_Script", "first", "third" }, new[] { "missing" } };
        var rows = new List<string>();
        for (int i = 0; i < cases.Length; i++)
        {
            var names = cases[i];
            var drawn = new IMGUIContainer(() => editor.Excluding(names));
            drawn.style.width = 400f;
            m_Root.Add(drawn);
            yield return null;
            yield return null;
            yield return null;
            rows.Add(string.Join(",", names) + "=" + F(drawn.layout.height));
            drawn.RemoveFromHierarchy();
        }
        Put("excluding", string.Join("|", rows));
        var nullNames = new IMGUIContainer(() => editor.Excluding(null));
        nullNames.style.width = 400f;
        m_Root.Add(nullNames);
        yield return null;
        yield return null;
        Put("excluding.null", F(nullNames.layout.height));
#endif
        yield return null;
    }
}
