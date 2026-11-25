using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in a game's OnGUI in Play mode, what EditorGUI's and EditorGUILayout's colour, object,
/// curve, gradient, mask, layer, tag and enum-flags fields answer, and the SerializedProperty forms
/// (PropertyField of each property type, ObjectField/CurveField/MultiPropertyField of a property):
/// outside OnGUI; in Layout and Repaint (value, control ids, GUI.changed, layout rects, property
/// heights); the menus' choices sent back as their commands; and scripted clicks, keys and drops,
/// one event per frame, with the windows a click opens (closed again at once).
/// </summary>
public sealed class EditorObjectFieldsProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    bool m_Done, m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1, m_Frames;
    HashSet<EditorWindow> m_BaseWindows;

    Material m_MatA, m_MatB;
    Texture2D m_Tex;
    GameObject m_Go;
    ProbeFieldsAsset m_Asset;
    SerializedObject m_So;

    Object m_Obj, m_ObjNoScene;
    Transform m_Tr;
    Color m_Color = new Color(0.25f, 0.5f, 0.75f, 0.5f), m_Color2 = new Color(1, 0, 0, 0.25f);
    AnimationCurve m_Curve = AnimationCurve.Linear(0, 0, 1, 1);
    Gradient m_Gradient = new Gradient();
    int m_Mask = 5;

    static readonly string[] MaskNames = { "One", "Two", "Three" };
    static readonly Rect ObjRect = new Rect(10, 300, 300, 18);
    static readonly Rect NoSceneRect = new Rect(10, 322, 300, 18);
    static readonly Rect TrRect = new Rect(10, 344, 300, 18);
    static readonly Rect ColorRect = new Rect(10, 366, 300, 18);
    static readonly Rect Color2Rect = new Rect(10, 388, 300, 18);
    static readonly Rect CurveRect = new Rect(10, 410, 300, 18);
    static readonly Rect GradientRect = new Rect(10, 432, 300, 18);
    static readonly Rect MaskRect = new Rect(10, 454, 300, 18);
    static readonly Rect SpV3Rect = new Rect(10, 476, 300, 18);
    static readonly Rect SpObjRect = new Rect(10, 498, 300, 18);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("EDITOR_OBJECT_FIELDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("EditorObjectFieldsProbe");
        var probe = go.AddComponent<EditorObjectFieldsProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.Setup();
        probe.RecordOutsideGui();
    }

    void Setup()
    {
        var shader = Shader.Find("Unlit/Color");
        m_MatA = new Material(shader) { name = "Mat A" };
        m_MatB = new Material(shader) { name = "Mat B" };
        m_Tex = new Texture2D(4, 4) { name = "Tex" };
        m_Go = new GameObject("Probe Target");
        m_Obj = m_MatA;
        m_Asset = ScriptableObject.CreateInstance<ProbeFieldsAsset>();
        m_Asset.name = "Probe Asset";
        m_Asset.material = m_MatA;
        m_Asset.texture = m_Tex;
        m_Asset.target = m_Go.transform;
        m_So = new SerializedObject(m_Asset);
        m_BaseWindows = new HashSet<EditorWindow>(Resources.FindObjectsOfTypeAll<EditorWindow>());
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    string S(object v) => v switch
    {
        null => "null",
        Color c => F(c.r) + " " + F(c.g) + " " + F(c.b) + " " + F(c.a),
        Object o => o == null ? "null" : o.name + " (" + o.GetType().Name + ")",
        AnimationCurve a => "curve " + a.length + (ReferenceEquals(a, m_Curve) || ReferenceEquals(a, m_Asset.curve) ? " same" : " other"),
        Gradient g => "gradient " + g.colorKeys.Length + "/" + g.alphaKeys.Length + (ReferenceEquals(g, m_Gradient) ? " same" : " other"),
        Enum e => e + " " + Convert.ToInt32(e),
        Vector3 a => F(a.x) + " " + F(a.y) + " " + F(a.z),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture),
    };
    static object[] RectArr(Rect r) => new object[] { r.x, r.y, r.width, r.height };

    object Try(Func<object> read)
    {
        try { return S(read()); }
        catch (Exception ex) { return "throws " + ex.GetType().Name; }
    }

    void RecordOutsideGui()
    {
        var r = new Rect(0, 0, 300, 18);
        Put("outside/editorGUI",
            Try(() => EditorGUI.ColorField(r, Color.red)), Try(() => EditorGUI.ColorField(r, "L", Color.red)),
            Try(() => EditorGUI.ObjectField(r, m_MatA, typeof(Material), true)), Try(() => EditorGUI.ObjectField(r, "L", m_MatA, typeof(Material), true)),
            Try(() => EditorGUI.CurveField(r, m_Curve)), Try(() => EditorGUI.GradientField(r, m_Gradient)),
            Try(() => EditorGUI.MaskField(r, 5, MaskNames)), Try(() => EditorGUI.LayerField(r, 8)),
            Try(() => EditorGUI.TagField(r, "Untagged")), Try(() => EditorGUI.EnumFlagsField(r, ProbeBits.A)));
        Put("outside/editorGUILayout",
            Try(() => EditorGUILayout.ColorField(Color.red)), Try(() => EditorGUILayout.ObjectField(m_MatA, typeof(Material), true)),
            Try(() => EditorGUILayout.CurveField(m_Curve)), Try(() => EditorGUILayout.GradientField(m_Gradient)),
            Try(() => EditorGUILayout.MaskField(5, MaskNames)), Try(() => EditorGUILayout.LayerField(8)),
            Try(() => EditorGUILayout.TagField("Untagged")), Try(() => EditorGUILayout.EnumFlagsField(ProbeBits.A)));
        Put("outside/property", Try(() => { EditorGUI.PropertyField(r, m_So.FindProperty("color")); return m_Asset.color; }),
            Try(() => { EditorGUI.ObjectField(r, m_So.FindProperty("material")); return m_Asset.material; }));
    }

    object[] Field(Func<object> call)
    {
        bool previous = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object value = Try(call);
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool changed = GUI.changed;
        GUI.changed = previous;
        return new object[] { value, after - before - 1, changed };
    }

    void RecordRectFields(string phase)
    {
        var r = new Rect(10, 10, 300, 18);
        var tall = new Rect(10, 10, 300, 64);
        Put($"rect/{phase}/color",
            Field(() => EditorGUI.ColorField(r, Color.red)), Field(() => EditorGUI.ColorField(r, "Label", Color.red)),
            Field(() => EditorGUI.ColorField(r, new GUIContent("Label"), Color.red, false, false, true)),
            Field(() => EditorGUI.ColorField(r, GUIContent.none, new Color(2, 1, 0.5f, 1), true, true, true)));
        Put($"rect/{phase}/object",
            Field(() => EditorGUI.ObjectField(r, m_MatA, typeof(Material), true)), Field(() => EditorGUI.ObjectField(r, "Label", m_MatA, typeof(Material), false)),
            Field(() => EditorGUI.ObjectField(tall, new GUIContent("Label"), m_Tex, typeof(Texture2D), false)),
            Field(() => EditorGUI.ObjectField(r, null, typeof(GameObject), true)), Field(() => EditorGUI.ObjectField(r, m_MatA, typeof(Material), m_Go)),
            Field(() => EditorGUI.ObjectField(r, "Label", m_Tex, typeof(Texture2D), m_Go)));
        Put($"rect/{phase}/curveGradient",
            Field(() => EditorGUI.CurveField(r, m_Curve)), Field(() => EditorGUI.CurveField(r, "Label", m_Curve)),
            Field(() => EditorGUI.CurveField(r, "Label", m_Curve, Color.green, new Rect(0, 0, 1, 1))), Field(() => EditorGUI.CurveField(r, (AnimationCurve)null)),
            Field(() => EditorGUI.GradientField(r, m_Gradient)), Field(() => EditorGUI.GradientField(r, "Label", m_Gradient)),
            Field(() => EditorGUI.GradientField(r, new GUIContent("Label"), m_Gradient, true)), Field(() => EditorGUI.GradientField(r, (Gradient)null)));
        Put($"rect/{phase}/masks",
            Field(() => EditorGUI.MaskField(r, "Label", 5, MaskNames)), Field(() => EditorGUI.MaskField(r, 0, MaskNames)),
            Field(() => EditorGUI.MaskField(r, -1, MaskNames)), Field(() => EditorGUI.MaskField(r, new GUIContent("Label"), 9, MaskNames)),
            Field(() => EditorGUI.EnumFlagsField(r, ProbeBits.A | ProbeBits.C)), Field(() => EditorGUI.EnumFlagsField(r, "Label", ProbeBits.None)),
            Field(() => EditorGUI.EnumFlagsField(r, new GUIContent("Label"), (ProbeBits)7, true)));
        Put($"rect/{phase}/layerTag",
            Field(() => EditorGUI.LayerField(r, 4)), Field(() => EditorGUI.LayerField(r, "Label", 8)), Field(() => EditorGUI.LayerField(r, 3)),
            Field(() => EditorGUI.TagField(r, "Untagged")), Field(() => EditorGUI.TagField(r, "Label", "Alpha")), Field(() => EditorGUI.TagField(r, "Missing")));
        EditorGUI.indentLevel = 1;
        EditorGUI.showMixedValue = true;
        Put($"rect/{phase}/indentedMixed",
            Field(() => EditorGUI.ColorField(r, Color.red)), Field(() => EditorGUI.ObjectField(r, m_MatA, typeof(Material), true)),
            Field(() => EditorGUI.CurveField(r, m_Curve)), Field(() => EditorGUI.MaskField(r, 5, MaskNames)),
            Field(() => EditorGUI.TagField(r, "Alpha")), Field(() => EditorGUI.LayerField(r, "Label", 8)));
        EditorGUI.showMixedValue = false;
        EditorGUI.indentLevel = 0;
        Put($"rect/{phase}/errors",
            Field(() => EditorGUI.EnumFlagsField(r, (Enum)null)), Field(() => EditorGUI.MaskField(r, 1, null)),
            Field(() => EditorGUI.ObjectField(r, m_MatA, null, true)));
    }

    void RecordLayoutFields(string phase)
    {
        var rects = new List<object>();
        object[] L(Func<object> call)
        {
            var answer = Field(call);
            rects.Add(RectArr(GUILayoutUtility.GetLastRect()));
            return answer;
        }
        GUILayout.BeginArea(new Rect(320, 0, 300, 640));
        Put($"layout/{phase}/fields",
            L(() => EditorGUILayout.ColorField(Color.red)), L(() => EditorGUILayout.ColorField("Label", Color.red)),
            L(() => EditorGUILayout.ColorField(new GUIContent("Label"), Color.red, true, false, false)),
            L(() => EditorGUILayout.ObjectField(m_MatA, typeof(Material), true)), L(() => EditorGUILayout.ObjectField("Label", m_Tex, typeof(Texture2D), false)),
            L(() => EditorGUILayout.ObjectField(new GUIContent("Label"), m_Go, typeof(GameObject), true)), L(() => EditorGUILayout.ObjectField(m_Tex, typeof(Texture2D), false)),
            L(() => EditorGUILayout.CurveField(m_Curve)), L(() => EditorGUILayout.CurveField("Label", m_Curve)),
            L(() => EditorGUILayout.CurveField("Label", m_Curve, Color.green, new Rect(0, 0, 1, 1))),
            L(() => EditorGUILayout.GradientField(m_Gradient)), L(() => EditorGUILayout.GradientField("Label", m_Gradient)),
            L(() => EditorGUILayout.MaskField(5, MaskNames)), L(() => EditorGUILayout.MaskField("Label", 5, MaskNames)),
            L(() => EditorGUILayout.LayerField(4)), L(() => EditorGUILayout.LayerField("Label", 8)),
            L(() => EditorGUILayout.TagField("Untagged")), L(() => EditorGUILayout.TagField("Label", "Alpha")),
            L(() => EditorGUILayout.EnumFlagsField(ProbeBits.A)), L(() => EditorGUILayout.EnumFlagsField("Label", ProbeBits.B)));
        GUILayout.EndArea();
        if (phase == "repaint") Put("layout/repaint/rects", rects.ToArray());
    }

    static readonly string[] Properties =
    {
        "color", "material", "texture", "target", "curve", "gradient", "layers", "kind", "bits",
        "v2", "v3", "v4", "rect", "bounds", "v2i", "v3i", "recti", "boundsi", "letter",
    };

    void RecordProperties(string phase)
    {
        m_So.Update();
        var r = new Rect(10, 10, 300, 18);
        var answers = new List<object>();
        var heights = new List<object>();
        foreach (var name in Properties)
        {
            var p = m_So.FindProperty(name);
            heights.Add(EditorGUI.GetPropertyHeight(p));
            answers.Add(Field(() => { EditorGUI.PropertyField(new Rect(10, 10, 300, EditorGUI.GetPropertyHeight(p)), p); return PropertyValue(p); }));
        }
        Put($"sp/{phase}/propertyField", answers.ToArray());
        if (phase == "repaint") Put("sp/repaint/heights", heights.ToArray());
        bool wide = EditorGUIUtility.wideMode;
        EditorGUIUtility.wideMode = true;
        Put($"sp/{phase}/wideHeights", Properties.Select(n => (object)EditorGUI.GetPropertyHeight(m_So.FindProperty(n))).ToArray());
        EditorGUIUtility.wideMode = wide;
        var labels = new[] { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };
        Put($"sp/{phase}/overloads",
            Field(() => { EditorGUI.ObjectField(r, m_So.FindProperty("material")); return m_Asset.material; }),
            Field(() => { EditorGUI.ObjectField(r, m_So.FindProperty("material"), new GUIContent("Label")); return m_Asset.material; }),
            Field(() => { EditorGUI.ObjectField(r, m_So.FindProperty("texture"), typeof(Texture)); return m_Asset.texture; }),
            Field(() => { EditorGUI.ObjectField(r, m_So.FindProperty("target"), typeof(Transform), new GUIContent("Label")); return m_Asset.target; }),
            Field(() => { EditorGUI.CurveField(r, m_So.FindProperty("curve"), Color.red, new Rect()); return m_Asset.curve; }),
            Field(() => { EditorGUI.CurveField(r, m_So.FindProperty("curve"), Color.red, new Rect(0, 0, 1, 1), new GUIContent("Label")); return m_Asset.curve; }),
            Field(() => { EditorGUI.MultiPropertyField(new Rect(10, 10, 300, 18), labels, m_So.FindProperty("v3.x")); return m_Asset.v3; }),
            Field(() => { EditorGUI.MultiPropertyField(new Rect(10, 10, 300, 40), labels, m_So.FindProperty("v3.x"), new GUIContent("Label")); return m_Asset.v3; }));
        var rects = new List<object>();
        GUILayout.BeginArea(new Rect(320, 0, 300, 640));
        var layout = new List<object>();
        foreach (var name in Properties)
        {
            var p = m_So.FindProperty(name);
            layout.Add(Field(() => { EditorGUILayout.PropertyField(p); return PropertyValue(p); }));
            rects.Add(RectArr(GUILayoutUtility.GetLastRect()));
        }
        layout.Add(Field(() => { EditorGUILayout.ObjectField(m_So.FindProperty("material")); return m_Asset.material; }));
        rects.Add(RectArr(GUILayoutUtility.GetLastRect()));
        layout.Add(Field(() => { EditorGUILayout.CurveField(m_So.FindProperty("curve"), Color.red, new Rect(), new GUIContent("Label")); return m_Asset.curve; }));
        rects.Add(RectArr(GUILayoutUtility.GetLastRect()));
        GUILayout.EndArea();
        Put($"sp/{phase}/layout", layout.ToArray());
        if (phase == "repaint") Put("sp/repaint/layoutRects", rects.ToArray());
        m_So.ApplyModifiedProperties();
    }

    object PropertyValue(SerializedProperty p) => p.propertyType switch
    {
        SerializedPropertyType.Color => S(p.colorValue),
        SerializedPropertyType.ObjectReference => S(p.objectReferenceValue),
        SerializedPropertyType.LayerMask or SerializedPropertyType.Enum or SerializedPropertyType.Character => p.intValue,
        SerializedPropertyType.AnimationCurve => "curve " + p.animationCurveValue.length,
        SerializedPropertyType.Gradient => "gradient",
        _ => p.propertyType.ToString(),
    };

    static object Offset(RectOffset o) => o == null ? "null" : $"{o.left} {o.right} {o.top} {o.bottom}";
    static object State(GUIStyleState s) => new object[]
    {
        "#" + ColorUtility.ToHtmlStringRGBA(s.textColor), s.background != null ? s.background.name : "null",
        string.Join(",", (s.scaledBackgrounds ?? new Texture2D[0]).Select(t => t != null ? t.name : "null")),
    };

    void RecordStyles(string phase)
    {
        foreach (var (name, read) in new (string, Func<GUIStyle>)[]
        {
            ("objectField", () => EditorStyles.objectField), ("objectFieldThumb", () => EditorStyles.objectFieldThumb),
            ("objectFieldMiniThumb", () => EditorStyles.objectFieldMiniThumb), ("colorField", () => EditorStyles.colorField),
            ("layerMaskField", () => EditorStyles.layerMaskField),
            ("objectFieldButton", () => (GUIStyle)typeof(EditorStyles).GetProperty("objectFieldButton", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)),
            ("colorPickerBox", () => (GUIStyle)typeof(EditorStyles).GetProperty("colorPickerBox", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)),
        })
        {
            object v;
            try
            {
                var st = read();
                v = new object[]
                {
                    st.name, Offset(st.margin), Offset(st.padding), Offset(st.border), Offset(st.overflow), st.fixedWidth, st.fixedHeight,
                    st.fontSize, st.fontStyle.ToString(), st.alignment.ToString(), st.imagePosition.ToString(), st.clipping.ToString(),
                    st.stretchWidth, st.stretchHeight, st.wordWrap, st.richText, st.contentOffset.x, st.contentOffset.y,
                    st.font != null ? st.font.name : "null", ReferenceEquals(st, read()),
                    State(st.normal), State(st.hover), State(st.active), State(st.focused),
                    State(st.onNormal), State(st.onHover), State(st.onActive), State(st.onFocused),
                };
            }
            catch (Exception ex) { v = "throws " + ex.GetType().Name; }
            Put($"style/{phase}/{name}", v);
        }
    }

    // ------------------------------------------------------------ menus' choices as their commands

    static readonly Type PopupInfo = typeof(EditorGUI).GetNestedType("PopupCallbackInfo", BindingFlags.NonPublic);
    static readonly Type MaskInfo = typeof(EditorGUI).Assembly.GetType("UnityEditor.MaskFieldGUI")?.GetNestedType("MaskCallbackInfo", BindingFlags.NonPublic);

    object Command(string commandName, Func<int, object> arm, Func<object> call, bool mixed = false)
    {
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object armed;
        try { armed = arm(before + 1); }
        catch (Exception ex) { return "arm throws " + ex.GetType().Name; }
        var original = Event.current;
        var command = new Event { type = EventType.ExecuteCommand, commandName = commandName };
        bool changed = GUI.changed;
        object value;
        bool reported;
        try
        {
            Event.current = command;
            GUI.changed = false;
            EditorGUI.showMixedValue = mixed;
            value = Try(call);
            reported = GUI.changed;
        }
        finally
        {
            EditorGUI.showMixedValue = false;
            Event.current = original;
            GUI.changed = changed;
        }
        return new object[] { value, reported, command.type.ToString(), armed };
    }

    object ArmPopup(int id, int index)
    {
        var instance = Activator.CreateInstance(PopupInfo, id);
        PopupInfo.GetField("m_SelectedIndex", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, index);
        PopupInfo.GetField("instance", BindingFlags.Public | BindingFlags.Static).SetValue(null, instance);
        return "armed";
    }

    object ArmMask(int id, int mask)
    {
        var instance = Activator.CreateInstance(MaskInfo, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { id }, null);
        MaskInfo.GetField("m_NewMask", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(instance, mask);
        MaskInfo.GetField("m_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, instance);
        return "armed";
    }

    void RecordCommands()
    {
        var r = new Rect(10, 10, 300, 18);
        Put("command/popup",
            Command("PopupMenuChanged", id => ArmPopup(id, 7), () => EditorGUI.TagField(r, "Untagged")),
            Command("PopupMenuChanged", id => ArmPopup(id, 0), () => EditorGUI.TagField(r, "Label", "Alpha")),
            Command("PopupMenuChanged", id => ArmPopup(id, 0), () => EditorGUI.TagField(r, "Untagged")),
            Command("PopupMenuChanged", id => ArmPopup(id, 5), () => EditorGUI.LayerField(r, 0)),
            Command("PopupMenuChanged", id => ArmPopup(id, 6), () => EditorGUI.LayerField(r, "Label", 0)),
            Command("PopupMenuChanged", id => ArmPopup(id, 1), () => EditorGUI.LayerField(r, 1)),
            Command("PopupMenuChanged", id => ArmPopup(id, 1), () => EditorGUI.LayerField(r, 1), mixed: true));
        Put("command/mask",
            Command("MaskMenuChanged", id => ArmMask(id, 3), () => EditorGUI.MaskField(r, 5, MaskNames)),
            Command("MaskMenuChanged", id => ArmMask(id, 5), () => EditorGUI.MaskField(r, "Label", 5, MaskNames)),
            Command("MaskMenuChanged", id => ArmMask(id, 5), () => EditorGUI.MaskField(r, 5, MaskNames), mixed: true),
            Command("MaskMenuChanged", id => ArmMask(id, -1), () => EditorGUI.MaskField(r, 0, MaskNames)),
            Command("MaskMenuChanged", id => ArmMask(id, 6), () => EditorGUI.EnumFlagsField(r, ProbeBits.A)),
            Command("MaskMenuChanged", id => ArmMask(id, -1), () => EditorGUI.EnumFlagsField(r, "Label", ProbeBits.A)),
            Command("MaskMenuChanged", id => ArmMask(id + 1, 6), () => EditorGUI.MaskField(r, 5, MaskNames)));
        m_So.Update();
        Put("command/property",
            Command("MaskMenuChanged", id => ArmMask(id, 1 << 11), () => { EditorGUI.PropertyField(r, m_So.FindProperty("layers")); m_So.ApplyModifiedProperties(); return m_Asset.layers.value; }),
            Command("MaskMenuChanged", id => ArmMask(id, 2), () => { EditorGUI.PropertyField(r, m_So.FindProperty("bits")); m_So.ApplyModifiedProperties(); return m_Asset.bits; }));
        // Which of DragAndDrop's calls resets the drag's visual mode and active control.
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        DragAndDrop.activeControlID = 5;
        DragAndDrop.objectReferences = new Object[] { m_MatA };
        var afterReferences = new object[] { DragAndDrop.visualMode.ToString(), DragAndDrop.activeControlID };
        DragAndDrop.paths = new[] { "Assets/x.mat" };
        var afterPaths = new object[] { DragAndDrop.visualMode.ToString(), DragAndDrop.activeControlID };
        DragAndDrop.PrepareStartDrag();
        var afterPrepare = new object[] { DragAndDrop.visualMode.ToString(), DragAndDrop.activeControlID, DragAndDrop.objectReferences.Length, DragAndDrop.paths.Length };
        DragAndDrop.visualMode = DragAndDropVisualMode.None;
        DragAndDrop.activeControlID = 0;
        Put("dragAndDrop/reset", afterReferences, afterPaths, afterPrepare);
        var maskLeft = MaskInfo?.GetField("m_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        var popupLeft = PopupInfo?.GetField("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        Put("command/left", maskLeft != null, popupLeft != null);
    }

    // ------------------------------------------------------------ scripted events

    static Event Mouse(EventType type, Vector2 position, int button = 0, int clicks = 1) =>
        new Event { type = type, mousePosition = position, button = button, clickCount = clicks };
    static Event Key(string key) => Event.KeyboardEvent(key);
    static Event Char(char c) => new Event { type = EventType.KeyDown, character = c };
    static Event Drag(EventType type, Vector2 position) => new Event { type = type, mousePosition = position };

    static void Refs(params Object[] objects)
    {
        DragAndDrop.PrepareStartDrag();
        DragAndDrop.objectReferences = objects;
    }

    (string Name, Func<Event> Make, bool Window)[] Script() => new (string, Func<Event>, bool)[]
    {
        ("obj.body.down", () => Mouse(EventType.MouseDown, new Vector2(100, 309)), false),
        ("obj.body.up", () => Mouse(EventType.MouseUp, new Vector2(100, 309)), false),
        ("obj.delete", () => Key("delete"), false),
        ("obj.dragUpdated", () => { Refs(m_MatB); return Drag(EventType.DragUpdated, new Vector2(100, 309)); }, false),
        ("obj.dragPerform", () => Drag(EventType.DragPerform, new Vector2(100, 309)), false),
        ("obj.dragWrongType", () => { Refs(m_Tex); return Drag(EventType.DragPerform, new Vector2(100, 309)); }, false),
        ("obj.dragOutside", () => { Refs(m_MatA); return Drag(EventType.DragPerform, new Vector2(100, 290)); }, false),
        ("noScene.dragPerform", () => { Refs(m_MatB); return Drag(EventType.DragPerform, new Vector2(200, 331)); }, false),
        ("tr.dragPerform", () => { Refs(m_Go); return Drag(EventType.DragPerform, new Vector2(100, 353)); }, false),
        ("obj.body2.down", () => { Refs(); return Mouse(EventType.MouseDown, new Vector2(100, 309)); }, false),
        ("obj.backspace", () => Key("backspace"), false),
        ("obj.deleteCommand", () => { m_Obj = m_MatA; return new Event { type = EventType.ExecuteCommand, commandName = "Delete" }; }, false),
        ("obj.doubleClick", () => { m_Obj = m_MatA; return Mouse(EventType.MouseDown, new Vector2(100, 309), clicks: 2); }, false),
        ("color.label.down", () => Mouse(EventType.MouseDown, new Vector2(30, 375)), false),
        ("color.ctrlC", () => Key("^c"), false),
        ("color2.label.down", () => Mouse(EventType.MouseDown, new Vector2(30, 397)), false),
        ("color2.ctrlV", () => Key("^v"), false),
        ("color2.validateCopy", () => new Event { type = EventType.ValidateCommand, commandName = "Copy" }, false),
        ("sp.v3.y.down", () => Mouse(EventType.MouseDown, new Vector2(10 + 150, 485)), false),
        ("sp.v3.y.up", () => Mouse(EventType.MouseUp, new Vector2(10 + 150, 485)), false),
        ("sp.v3.y.type", () => Char('7'), false),
        ("sp.v3.y.return", () => Key("return"), false),
        ("sp.obj.down", () => Mouse(EventType.MouseDown, new Vector2(100, 507)), false),
        ("sp.obj.delete", () => Key("delete"), false),
        ("sp.obj.dragPerform", () => { Refs(m_MatB); return Drag(EventType.DragPerform, new Vector2(100, 507)); }, false),
        ("color.body.down", () => { Refs(); return Mouse(EventType.MouseDown, new Vector2(200, 375)); }, true),
        ("color.eyedropper.down", () => Mouse(EventType.MouseDown, new Vector2(10 + 300 - 8, 375)), true),
        ("curve.down", () => Mouse(EventType.MouseDown, new Vector2(200, 419)), true),
        ("gradient.down", () => Mouse(EventType.MouseDown, new Vector2(200, 441)), true),
        ("mask.down", () => Mouse(EventType.MouseDown, new Vector2(200, 463)), true),
        ("obj.button.down", () => Mouse(EventType.MouseDown, new Vector2(10 + 300 - 8, 309)), true),
    };

    (string Name, Func<Event> Make, bool Window)[] m_Script;

    object[] ScriptedFields()
    {
        m_So.Update();
        var a = Field(() => m_Obj = EditorGUI.ObjectField(ObjRect, m_Obj, typeof(Material), true));
        var b = Field(() => m_ObjNoScene = EditorGUI.ObjectField(NoSceneRect, "No scene", m_ObjNoScene, typeof(Material), false));
        var c = Field(() => m_Tr = (Transform)EditorGUI.ObjectField(TrRect, m_Tr, typeof(Transform), true));
        var d = Field(() => m_Color = EditorGUI.ColorField(ColorRect, "Copy", m_Color));
        var e = Field(() => m_Color2 = EditorGUI.ColorField(Color2Rect, new GUIContent("Paste"), m_Color2, true, false, false));
        var f = Field(() => m_Curve = EditorGUI.CurveField(CurveRect, m_Curve));
        var g = Field(() => m_Gradient = EditorGUI.GradientField(GradientRect, m_Gradient));
        var h = Field(() => m_Mask = EditorGUI.MaskField(MaskRect, m_Mask, MaskNames));
        var i = Field(() => { EditorGUI.PropertyField(SpV3Rect, m_So.FindProperty("v3"), GUIContent.none); return m_So.FindProperty("v3").vector3Value; });
        var j = Field(() => { EditorGUI.PropertyField(SpObjRect, m_So.FindProperty("material")); return m_So.FindProperty("material").objectReferenceValue; });
        m_So.ApplyModifiedProperties();
        return new object[]
        {
            a[0], b[0], c[0], d[0], e[0], f[0], g[0], h[0], i[0], j[0],
            new object[] { a[2], b[2], c[2], d[2], e[2], f[2], g[2], h[2], i[2], j[2] },
            GUIUtility.keyboardControl != 0, GUIUtility.hotControl != 0, EditorGUIUtility.editingTextField,
            DragAndDrop.activeControlID != 0, DragAndDrop.visualMode.ToString(),
        };
    }

    List<string> CloseNewWindows()
    {
        var opened = new List<string>();
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
        {
            if (window == null || m_BaseWindows.Contains(window)) continue;
            opened.Add(window.GetType().Name);
            // (Closing a picker at once can throw in its OnDestroy; the window is gone either way.)
            try { window.Close(); }
            catch (Exception) { }
        }
        opened.Sort(StringComparer.Ordinal);
        return opened;
    }

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type != EventType.Layout && type != EventType.Repaint) return;
        if (m_Step < 0)
        {
            string phase = type == EventType.Layout ? "layout" : "repaint";
            if (type == EventType.Layout && m_RecordedLayout) return;
            if (type == EventType.Repaint && (!m_RecordedLayout || m_RecordedRepaint)) return;
            ScriptedFields();
            RecordRectFields(phase);
            RecordLayoutFields(phase);
            RecordProperties(phase);
            RecordStyles(phase);
            if (type == EventType.Layout) m_RecordedLayout = true;
            else
            {
                m_RecordedRepaint = true;
                RecordCommands();
                m_Script = Script();
                m_Step = 0;
                Put("script/start", ScriptedFields());
            }
            return;
        }
        if (type != EventType.Repaint) { ScriptedFields(); return; }
        if (++m_Frames % 2 != 0) { ScriptedFields(); return; }
        var real = Event.current;
        var step = m_Script[m_Step];
        // Only the windows the step opens count (and are closed).
        m_BaseWindows = new HashSet<EditorWindow>(Resources.FindObjectsOfTypeAll<EditorWindow>());
        var scripted = step.Make();
        Event.current = scripted;
        object[] answer;
        try { answer = ScriptedFields(); }
        finally { Event.current = real; }
        m_Step++;
        // A window a step opens may start editing a text field of its own: that answer goes with
        // the window's key, and editing is ended for the next steps.
        object editing = null;
        if (step.Window)
        {
            editing = answer[13];
            answer[13] = null;
        }
        Put("script/" + step.Name, answer, scripted.type.ToString());
        if (step.Window)
        {
            Put("window/" + step.Name, CloseNewWindows().ToArray(), editing);
            EditorGUIUtility.editingTextField = false;
        }
        if (step.Name == "color.ctrlC") Put("clipboard/afterCopy", EditorGUIUtility.systemCopyBuffer);
        if (m_Step == m_Script.Length)
        {
            m_Done = true;
            DragAndDrop.PrepareStartDrag();
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
        }
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        float f => F(f),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
