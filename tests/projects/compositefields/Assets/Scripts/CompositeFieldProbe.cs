using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The composite fields of a runtime panel (Vector2/3/4Field, Vector2Int/3IntField, RectField,
// RectIntField, BoundsField, BoundsIntField) and Hash128Field: hierarchy and classes, values set
// with and without notification and how they reach the sub-fields, values typed into a sub-field
// and committed, a drag on a sub-field's label, mixed values, UXML values and a data binding.
public class CompositeFieldProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("CompositeFieldProbe").AddComponent<CompositeFieldProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    class Source
    {
        [CreateProperty] public Vector3 position = new Vector3(1, 2, 3);
    }

    VisualElement root;
    int changes;
    string lastChange = "";

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("COMPOSITE_FIELDS_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        bool failed = false;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var document = new GameObject("Document").AddComponent<UIDocument>();
        document.panelSettings = settings;
        root = document.rootVisualElement;
        root.style.unityFontDefinition = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        root.style.fontSize = 14;
        root.style.width = 600;
        for (int i = 0; i < 3; i++) yield return null;
        var steps = Record();
        while (true)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception ex) { Debug.LogException(ex); failed = true; break; }
            if (!more) break;
            yield return steps.Current;
        }
        if (!failed) Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    static string V(object v)
    {
        switch (v)
        {
            case Vector2 a: return F(a.x) + "," + F(a.y);
            case Vector3 a: return F(a.x) + "," + F(a.y) + "," + F(a.z);
            case Vector4 a: return F(a.x) + "," + F(a.y) + "," + F(a.z) + "," + F(a.w);
            case Rect a: return F(a.x) + "," + F(a.y) + "," + F(a.width) + "," + F(a.height);
            case Bounds a: return V(a.center) + "/" + V(a.extents);
            default: return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    static string Classes(VisualElement e) => e == null ? "<null>" : string.Join(" ", e.GetClasses().OrderBy(c => c, StringComparer.Ordinal));

    // The element tree under an element: type, name and classes, children in brackets.
    static string Tree(VisualElement e)
    {
        var self = e.GetType().Name + (string.IsNullOrEmpty(e.name) ? "" : "#" + e.name) + "(" + string.Join(" ", e.GetClasses().OrderBy(c => c, StringComparer.Ordinal)) + ")";
        if (e is TextElement || e is TextField) return self;
        var children = e.hierarchy.Children().Where(c => !(c is TextElement) || e is Label).ToList();
        return children.Count == 0 ? self : self + "[" + string.Join(" ", children.Select(Tree)) + "]";
    }

    static void Send(VisualElement target, EventBase evt)
    {
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    static IEnumerator Settle() { yield return null; yield return null; }

    string Changes()
    {
        var result = changes + " " + lastChange;
        changes = 0;
        lastChange = "";
        return result;
    }

    void Watch<T>(BaseField<T> field) => field.RegisterValueChangedCallback(e => { changes++; lastChange = V(e.previousValue) + ">" + V(e.newValue); });

    static string Texts(VisualElement field) => string.Join("|", field.Query<VisualElement>().Where(e => e.GetType().Name == "FloatField" || e.GetType().Name == "IntegerField").ToList()
        .Select(e => e is FloatField f ? f.text : ((IntegerField)e).text));

    string State<T>(BaseField<T> field) => "value " + V(field.value) + " texts " + Texts(field) + " changes " + Changes();

    static TextElement Input(VisualElement field) => field.Q<TextElement>(className: TextElement.ussClassName + "--inner-input-field-component");

    // Types into one sub-field (selected first) and presses Return.
    IEnumerator Type<T>(string key, BaseField<T> owner, VisualElement sub, string typed)
    {
        sub.Focus();
        yield return Settle();
        if (sub is FloatField f) f.SelectAll(); else if (sub is IntegerField i) i.SelectAll(); else if (sub is Hash128Field h) h.SelectAll();
        var input = Input(sub);
        foreach (var c in typed) Send(input, KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None));
        Put(key + ".typed", State(owner));
        Send(input, KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None));
        yield return Settle();
        Put(key + ".committed", State(owner));
    }

    static Event Mouse(EventType type, Vector2 delta) =>
        new Event { type = type, button = 0, mousePosition = new Vector2(5, 5), delta = delta, clickCount = type == EventType.MouseDown ? 1 : 0 };

    IEnumerator Exercise<T>(string name, BaseField<T> field, T a, T b, string first, string second)
    {
        root.Add(field);
        Watch(field);
        yield return Settle();
        Put(name + ".tree", Tree(field));
        Put(name + ".default", State(field));
        field.value = a;
        Put(name + ".set", State(field));
        field.value = a;
        Put(name + ".setSame", State(field));
        field.SetValueWithoutNotify(b);
        Put(name + ".withoutNotify", State(field));
        // Typed into the first numeric sub-field and the last one.
        var subs = field.Query<VisualElement>().Where(e => e is FloatField || e is IntegerField).ToList();
        yield return Type(name + ".typeFirst", field, subs[0], first);
        yield return Type(name + ".typeLast", field, subs[subs.Count - 1], second);
        // A drag on the first sub-field's label.
        var label = subs[0] is FloatField ff ? ff.labelElement : ((IntegerField)subs[0]).labelElement;
        Send(label, PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, Vector2.zero)));
        Send(label, PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, new Vector2(8, 0))));
        Send(label, PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, Vector2.zero)));
        yield return Settle();
        Put(name + ".labelDrag", State(field));
        // Mixed values reach the sub-fields.
        field.showMixedValue = true;
        Put(name + ".mixed", State(field) + " subMixed " + string.Join(",", subs.Select(s => s is FloatField f ? f.showMixedValue : ((IntegerField)s).showMixedValue)));
        field.showMixedValue = false;
        Put(name + ".mixed.off", State(field));
        field.RemoveFromHierarchy();
    }

    IEnumerator Record()
    {
        yield return Exercise("v2", new Vector2Field("Offset"), new Vector2(1.5f, -2), new Vector2(1f / 3, 4), "2+3", "-0.25");
        yield return Exercise("v3", new Vector3Field("Position"), new Vector3(1, 2, 3), new Vector3(0.1f, 0.2f, 0.3f), "10/4", "1e39");
        yield return Exercise("v4", new Vector4Field("Color"), new Vector4(1, 2, 3, 4), Vector4.zero, "7", "x*2");
        yield return Exercise("v2i", new Vector2IntField("Cell"), new Vector2Int(1, 2), new Vector2Int(-3, 4), "2*21", "3000000000");
        yield return Exercise("v3i", new Vector3IntField("Voxel"), new Vector3Int(1, 2, 3), Vector3Int.zero, "5", "-7");
        yield return Exercise("rect", new RectField("Area"), new Rect(1, 2, 3, 4), new Rect(0.5f, 0.5f, 10, 20), "12", "+=1");
        yield return Exercise("recti", new RectIntField("Pixels"), new RectInt(1, 2, 3, 4), new RectInt(5, 6, 7, 8), "9", "100");
        yield return Exercise("bounds", new BoundsField("Box"), new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6)), new Bounds(Vector3.one, Vector3.one), "4", "0.5");
        yield return Exercise("boundsi", new BoundsIntField("Cells"), new BoundsInt(1, 2, 3, 4, 5, 6), new BoundsInt(0, 0, 0, 1, 1, 1), "8", "-2");

        // A field without a label.
        var plain = new Vector3Field();
        root.Add(plain);
        yield return Settle();
        Put("plain.tree", Tree(plain));
        plain.RemoveFromHierarchy();

        // Hash128Field.
        var hash = new Hash128Field("Hash");
        Watch(hash);
        root.Add(hash);
        yield return Settle();
        Put("hash.default", "value " + hash.value + " text " + hash.text + " classes " + Classes(hash) + " changes " + Changes());
        hash.value = Hash128.Compute("probe");
        Put("hash.set", "value " + hash.value + " text " + hash.text + " changes " + Changes());
        foreach (var (key, typed) in new[] { ("ab", "ab"), ("one", "1"), ("rejected", "xyz9"), ("long", "0123456789abcdef0123456789abcdef01"), ("upper", "ABCDEF") })
        {
            hash.Focus();
            yield return Settle();
            hash.SelectAll();
            var input = Input(hash);
            foreach (var c in typed) Send(input, KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None));
            Put("hash." + key + ".typed", "value " + hash.value + " text " + hash.text + " changes " + Changes());
            Send(input, KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None));
            yield return Settle();
            Put("hash." + key + ".committed", "value " + hash.value + " text " + hash.text + " changes " + Changes());
        }
        // Emptied then left: the default hash.
        hash.Focus();
        yield return Settle();
        hash.SelectAll();
        Send(Input(hash), KeyDownEvent.GetPooled('\b', KeyCode.Backspace, EventModifiers.None));
        Input(hash).Blur();
        yield return Settle();
        Put("hash.emptied", "value " + hash.value + " text " + hash.text + " changes " + Changes());
        hash.RemoveFromHierarchy();

        // UXML.
        var container = new VisualElement();
        root.Add(container);
        Resources.Load<VisualTreeAsset>("CompositeFields").CloneTree(container);
        yield return Settle();
        foreach (var e in container.Children())
        {
            string value;
            switch (e)
            {
                case Vector2Field f: value = V(f.value); break;
                case Vector3Field f: value = V(f.value); break;
                case Vector4Field f: value = V(f.value); break;
                case Vector2IntField f: value = V(f.value); break;
                case Vector3IntField f: value = V(f.value); break;
                case RectField f: value = V(f.value); break;
                case RectIntField f: value = V(f.value); break;
                case BoundsField f: value = V(f.value); break;
                case BoundsIntField f: value = V(f.value); break;
                case Hash128Field f: value = f.value + " " + f.text; break;
                default: value = "?"; break;
            }
            Put("uxml." + e.name, e.GetType().Name + " " + value + " texts " + Texts(e));
        }
        container.RemoveFromHierarchy();

        // A two-way data binding of a Vector3Field's value.
        var source = new Source();
        var bound = new Vector3Field("Bound") { dataSource = source };
        bound.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("position"), bindingMode = BindingMode.TwoWay, updateTrigger = BindingUpdateTrigger.EveryUpdate });
        Watch(bound);
        root.Add(bound);
        for (int i = 0; i < 4; i++) yield return null;
        Put("binding.initial", State(bound) + " source " + V(source.position));
        source.position = new Vector3(4, 5, 6);
        for (int i = 0; i < 4; i++) yield return null;
        Put("binding.fromSource", State(bound) + " source " + V(source.position));
        yield return Type("binding.typed", bound, bound.Q<FloatField>(), "9");
        for (int i = 0; i < 4; i++) yield return null;
        Put("binding.toSource", State(bound) + " source " + V(source.position));
    }
}
