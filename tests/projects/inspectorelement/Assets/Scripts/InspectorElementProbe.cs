using System;
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
using UnityEditor.UIElements;
#endif

public class InspectorElementProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("InspectorElementProbe");
        var doc = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        doc.panelSettings = settings;
        go.AddComponent<InspectorElementProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("INSPECTOR_ELEMENT_OUT");
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        m_Logs.Sort(StringComparer.Ordinal);
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

    static Type RuntimeUtil => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Frames(int count = 4)
    {
        var update = RuntimeUtil.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var repaint = RuntimeUtil.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < count; i++)
        {
            update.Invoke(null, null);
            repaint.Invoke(null, new object[] { false });
            System.Threading.Thread.Sleep(20);
        }
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
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    // An element and its hierarchy children: type, name, classes, '!' when disabled, the text of
    // labels, inline position and overflow. A PropertyField's own content is left out (other
    // probes measure it).
    static string Tree(VisualElement e)
    {
        var sb = new StringBuilder();
        sb.Append(e.GetType().Name).Append('#').Append(e.name).Append('{').Append(string.Join(" ", e.GetClasses())).Append('}');
        if (!e.enabledSelf) sb.Append('!');
        if (e is Label l) sb.Append("'").Append(l.text).Append("'");
        if (e.style.position.keyword != StyleKeyword.Null) sb.Append("[pos=").Append(e.style.position.value).Append(']');
        if (e.style.overflow.keyword != StyleKeyword.Null) sb.Append("[ov=").Append(e.style.overflow.value).Append(']');
#if UNITY_EDITOR
        if (e is PropertyField) return sb.ToString();
#endif
        if (e.hierarchy.childCount > 0)
        {
            sb.Append('(');
            for (int i = 0; i < e.hierarchy.childCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Tree(e.hierarchy[i]));
            }
            sb.Append(')');
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    static object Internal(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(target);

    static string EditorOf(InspectorElement element)
    {
        var editor = Internal(element, "editor") as Editor;
        return editor == null ? "null" : editor.GetType().Name;
    }

    // The editors' counts (EditorCounts is in the Editor assembly).
    static int Count(string field) =>
        (int)AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("EditorCounts")).First(t => t != null)
            .GetField(field, BindingFlags.Static | BindingFlags.Public).GetValue(null);

    static string Counts() => $"enabled={Count("enabled")}|disabled={Count("disabled")}|guis={Count("guis")}";

    InspectorElement Show(InspectorElement element)
    {
        m_Root.Add(element);
        Frames();
        return element;
    }

    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 640f;
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };

        // 1. The class names and the Editor's own answers.
        Put("uss", string.Join("|", InspectorElement.ussClassName, InspectorElement.customInspectorUssClassName, InspectorElement.iMGUIContainerUssClassName,
            InspectorElement.iMGUIInspectorVariantUssClassName, InspectorElement.uIEInspectorVariantUssClassName, InspectorElement.noInspectorFoundVariantUssClassName,
            InspectorElement.uIECustomVariantUssClassName, InspectorElement.iMGUICustomVariantUssClassName, InspectorElement.iMGUIDefaultVariantUssClassName,
            InspectorElement.uIEDefaultVariantUssClassName, InspectorElement.debugVariantUssClassName, InspectorElement.debugInternalVariantUssClassName));
        var defaults = ScriptableObject.CreateInstance<InspectorDefaultTarget>();
        var generic = Editor.CreateEditor(defaults);
        var genericGui = generic.CreateInspectorGUI();
        Put("editor.generic", $"{generic.GetType().Name}|margins={generic.UseDefaultMargins()}|gui={(genericGui == null ? "null" : Tree(genericGui))}");
        var imguiTarget = ScriptableObject.CreateInstance<InspectorImguiTarget>();
        var imguiEditor = Editor.CreateEditor(imguiTarget);
        Put("editor.base", $"{imguiEditor.GetType().Name}|margins={imguiEditor.UseDefaultMargins()}|gui={(imguiEditor.CreateInspectorGUI() == null ? "null" : "element")}");
        UnityEngine.Object.DestroyImmediate(generic);
        UnityEngine.Object.DestroyImmediate(imguiEditor);

        // 2. The default inspector: of an object, a SerializedObject, an Editor, a component and
        // several objects.
        var byObject = Show(new InspectorElement(defaults));
        Put("default.object", Tree(byObject) + "|editor=" + EditorOf(byObject));
        var bySo = Show(new InspectorElement(new SerializedObject(defaults)));
        Put("default.serializedObject", Tree(bySo) + "|editor=" + EditorOf(bySo));
        var go = new GameObject("Inspected");
        var behaviour = go.AddComponent<InspectorBehaviour>();
        var byComponent = Show(new InspectorElement(behaviour));
        Put("default.component", Tree(byComponent) + "|editor=" + EditorOf(byComponent));
        var second = ScriptableObject.CreateInstance<InspectorDefaultTarget>();
        second.count = 8;
        var multi = Show(new InspectorElement(new SerializedObject(new UnityEngine.Object[] { defaults, second })));
        Put("default.multi", Tree(multi) + "|editor=" + EditorOf(multi));

        // 3. Custom editors: UI Toolkit, IMGUI only, a null UI Toolkit GUI, no default margins.
        var uieTarget = ScriptableObject.CreateInstance<InspectorUieTarget>();
        string before = Counts();
        var uie = new InspectorElement(uieTarget);
        string created = Counts();
        Show(uie);
        Put("custom.uie", Tree(uie) + "|editor=" + EditorOf(uie) + $"|before={before}|created={created}|shown={Counts()}");
        uie.RemoveFromHierarchy();
        Frames();
        Put("custom.uieRemoved", Counts() + "|editor=" + EditorOf(uie));
        Show(uie);
        Put("custom.uieReattached", Tree(uie) + "|editor=" + EditorOf(uie) + "|" + Counts());
        var imgui = Show(new InspectorElement(imguiTarget));
        Put("custom.imgui", Tree(imgui) + "|editor=" + EditorOf(imgui));
        var nullGui = Show(new InspectorElement(ScriptableObject.CreateInstance<InspectorNullGuiTarget>()));
        Put("custom.nullGui", Tree(nullGui) + "|editor=" + EditorOf(nullGui));
        var noMargins = Show(new InspectorElement(ScriptableObject.CreateInstance<InspectorNoMarginsTarget>()));
        Put("custom.noMargins", Tree(noMargins));

        // 4. An editor given to the element is not the element's to destroy.
        before = Counts();
        var given = Editor.CreateEditor(uieTarget);
        var byEditor = Show(new InspectorElement(given));
        Put("given.shown", Tree(byEditor) + $"|before={before}|now={Counts()}|same={ReferenceEquals(Internal(byEditor, "editor"), given)}");
        byEditor.RemoveFromHierarchy();
        Frames();
        Put("given.removed", Counts() + $"|alive={given != null}");
        UnityEngine.Object.DestroyImmediate(given);

        // 5. An element without an object, bound later, and bound again to another type.
        var empty = new InspectorElement();
        Put("empty", Tree(empty) + "|editor=" + EditorOf(empty));
        Show(empty);
        empty.Bind(new SerializedObject(defaults));
        Frames();
        Put("empty.bound", Tree(empty) + "|editor=" + EditorOf(empty));
        var firstGui = empty.hierarchy[2];
        var firstEditor = Internal(empty, "editor");
        empty.Bind(new SerializedObject(second));
        Frames();
        Put("empty.sameType", Tree(empty) + $"|sameGui={ReferenceEquals(empty.hierarchy[2], firstGui)}|sameEditor={ReferenceEquals(Internal(empty, "editor"), firstEditor)}|count={empty.Q<IntegerField>()?.value}");
        empty.Bind(new SerializedObject(uieTarget));
        Frames();
        Put("empty.rebound", Tree(empty) + "|editor=" + EditorOf(empty) + "|" + Counts());

        // An element bound to its own object inside a container bound to another one.
        var holder = new VisualElement { name = "holder" };
        var own = new InspectorElement(new SerializedObject(defaults));
        holder.Add(own);
        m_Root.Add(holder);
        Frames();
        holder.Bind(new SerializedObject(behaviour));
        Frames();
        Put("nested.own", Tree(own) + "|editor=" + EditorOf(own));
        holder.RemoveFromHierarchy();

        // 6. FillDefaultInspector: no object, an object without an editor, with one.
        var none = new VisualElement();
        InspectorElement.FillDefaultInspector(none, null, null);
        Put("fill.null", Tree(none));
        var plain = new VisualElement();
        InspectorElement.FillDefaultInspector(plain, new SerializedObject(behaviour), null);
        Put("fill.noEditor", Tree(plain));
        var withEditor = new VisualElement();
        var behaviourEditor = Editor.CreateEditor(behaviour);
        InspectorElement.FillDefaultInspector(withEditor, behaviourEditor.serializedObject, behaviourEditor);
        Put("fill.editor", Tree(withEditor));
        UnityEngine.Object.DestroyImmediate(behaviourEditor);

        // 7. The panel's visual tree updater, one phase at a time.
        VisualTreeUpdater(defaults);
        UnityEngine.Object.DestroyImmediate(go);
    }

    void VisualTreeUpdater(InspectorDefaultTarget defaults)
    {
        var panelType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.Panel");
        var field = panelType.GetField("m_VisualTreeUpdater", BindingFlags.Instance | BindingFlags.NonPublic);
        Put("vtu.field", field == null ? "null" : field.FieldType.FullName + "|" + field.Attributes);
        if (field == null) return;
        var updater = field.GetValue(m_Root.panel);
        var phaseType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.VisualTreeUpdatePhase");
        var phase = updater.GetType().GetMethod("UpdateVisualTreePhase");
        var all = updater.GetType().GetMethod("UpdateVisualTree");
        Put("vtu.phases", string.Join("|", Enum.GetNames(phaseType).Select(n => n + "=" + Convert.ToInt32(Enum.Parse(phaseType, n)))));

        var box = new VisualElement { name = "vtu-box" };
        int geometry = 0;
        box.RegisterCallback<GeometryChangedEvent>(_ => geometry++);
        // Out of the root's flow: its size is its own, whatever the inspectors above it take.
        box.style.position = Position.Absolute;
        box.style.width = 123f;
        box.style.height = 45f;
        m_Root.Add(box);
        string State() => $"w={F(box.layout.width)}|h={F(box.layout.height)}|rw={F(box.resolvedStyle.width)}|geometry={geometry}";
        Put("vtu.added", State());
        foreach (var name in new[] { "ViewData", "Bindings", "DataBinding", "Animation", "Styles", "Layout", "TransformClip" })
        {
            phase.Invoke(updater, new[] { Enum.Parse(phaseType, name) });
            Put("vtu.after" + name, State());
        }
        box.style.width = 200f;
        Put("vtu.changed", State());
        all.Invoke(updater, null);
        Put("vtu.all", State());

        // The style phase alone, then the layout phase alone.
        box.style.width = 77f;
        phase.Invoke(updater, new[] { Enum.Parse(phaseType, "Layout") });
        Put("vtu.layoutOnly", State());
        phase.Invoke(updater, new[] { Enum.Parse(phaseType, "Styles") });
        Put("vtu.stylesThen", State());
        phase.Invoke(updater, new[] { Enum.Parse(phaseType, "Layout") });
        Put("vtu.layoutThen", State());

        // A field bound off its panel: its binding request, then its value.
        defaults.count = 21;
        var bound = new IntegerField { bindingPath = "count" };
        bound.Bind(new SerializedObject(defaults));
        m_Root.Add(bound);
        Put("vtu.bindAdded", $"value={bound.value}");
        phase.Invoke(updater, new[] { Enum.Parse(phaseType, "Bindings") });
        Put("vtu.bindBindings", $"value={bound.value}");
        phase.Invoke(updater, new[] { Enum.Parse(phaseType, "DataBinding") });
        Put("vtu.bindDataBinding", $"value={bound.value}");
    }
#else
    void Run() { }
#endif
}
