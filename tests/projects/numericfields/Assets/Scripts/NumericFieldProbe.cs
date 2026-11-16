using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Properties;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The scalar numeric fields of a runtime panel (IntegerField, LongField, FloatField, DoubleField,
// UnsignedIntegerField, UnsignedLongField) and the ExpressionEvaluator they parse with: evaluated
// expressions per value type; each field's default state and classes, values set with and without
// notification, format strings, typed text committed with Return (expressions, relative "+=" forms,
// rejected characters, out-of-range values), delayed and read-only fields, input device deltas and
// a label drag, mixed values, ClearValue, UXML attributes and a two-way data binding.
public class NumericFieldProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("NumericFieldProbe").AddComponent<NumericFieldProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    class Source
    {
        [CreateProperty] public int count = 5;
    }

    VisualElement root;
    int changes;
    string lastChange = "";

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("NUMERIC_FIELDS_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        bool failed = false;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var document = new GameObject("Document").AddComponent<UIDocument>();
        document.panelSettings = settings;
        root = document.rootVisualElement;
        root.style.unityFontDefinition = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        root.style.fontSize = 14;
        root.style.width = 400;
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

    static string V(object v)
    {
        switch (v)
        {
            case float f: return f.ToString("R", CultureInfo.InvariantCulture) + "|" + BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8");
            case double d: return d.ToString("R", CultureInfo.InvariantCulture) + "|" + BitConverter.DoubleToInt64Bits(d).ToString("x16");
            default: return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }

    static string Classes(VisualElement e) => e == null ? "<null>" : string.Join(" ", e.GetClasses().OrderBy(c => c, StringComparer.Ordinal));

    static void Send(VisualElement target, EventBase evt)
    {
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    static TextElement Input<T>(TextValueField<T> field) => field.Q<TextElement>(className: TextElement.ussClassName + "--inner-input-field-component") ?? field.Q(className: "unity-base-text-field__input").Q<TextElement>();

    static IEnumerator Settle() { yield return null; yield return null; }

    string Changes()
    {
        var result = changes + " " + lastChange;
        changes = 0;
        lastChange = "";
        return result;
    }

    string State<T>(TextValueField<T> field) => "value " + V(field.value) + " text " + field.text + " changes " + Changes();

    void Watch<T>(TextValueField<T> field) => field.RegisterValueChangedCallback(e => { changes++; lastChange = V(e.previousValue) + ">" + V(e.newValue); });

    // Focuses the field, selects its text, types the characters, records the text and value, then
    // presses Return.
    IEnumerator Type<T>(string key, TextValueField<T> field, string typed, bool commit = true)
    {
        field.Focus();
        yield return Settle();
        field.SelectAll();
        var input = Input(field);
        foreach (var c in typed) Send(input, KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None));
        Put(key + ".typed", State(field));
        if (commit)
        {
            Send(input, KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None));
            yield return Settle();
            Put(key + ".committed", State(field));
        }
    }

    static Event Mouse(EventType type, Vector2 delta, EventModifiers modifiers) =>
        new Event { type = type, button = 0, mousePosition = new Vector2(5, 5), delta = delta, modifiers = modifiers, clickCount = type == EventType.MouseDown ? 1 : 0 };

    IEnumerator Exercise<T>(string name, TextValueField<T> field, T a, T b, string format, string[] typed)
    {
        root.Add(field);
        Watch(field);
        yield return Settle();
        Put(name + ".default", "value " + V(field.value) + " text " + field.text + " format " + field.formatString + " delayed " + field.isDelayed
            + " maxLength " + field.maxLength + " readOnly " + field.isReadOnly);
        Put(name + ".classes", Classes(field) + " | label " + Classes(field.labelElement) + " | input " + Classes(field.Q(className: "unity-base-text-field__input")));
        field.value = a;
        Put(name + ".set", State(field));
        field.value = a;
        Put(name + ".setSame", State(field));
        field.SetValueWithoutNotify(b);
        Put(name + ".withoutNotify", State(field));
        field.formatString = format;
        Put(name + ".format", State(field));
        field.formatString = null;
        Put(name + ".formatNull", State(field));
        for (int i = 0; i < typed.Length; i++)
        {
            yield return Type(name + ".type" + i, field, typed[i]);
            field.SetValueWithoutNotify(a);
        }
        // Relative to the text when the edit started.
        field.SetValueWithoutNotify(a);
        yield return Type(name + ".plusEquals", field, "+=12");
        yield return Type(name + ".timesEquals", field, "*=2");
        // Delayed: the value follows on Return only.
        field.isDelayed = true;
        field.SetValueWithoutNotify(a);
        yield return Type(name + ".delayed", field, "2*4");
        field.isDelayed = false;
        // Input device deltas from the current value.
        field.SetValueWithoutNotify(a);
        field.ApplyInputDeviceDelta(new Vector3(10, 0, 0), DeltaSpeed.Normal, field.value);
        Put(name + ".delta.normal", State(field));
        field.ApplyInputDeviceDelta(new Vector3(-3, 0, 0), DeltaSpeed.Fast, field.value);
        Put(name + ".delta.fast", State(field));
        field.ApplyInputDeviceDelta(new Vector3(0, 7, 0), DeltaSpeed.Slow, field.value);
        Put(name + ".delta.slow", State(field));
        // A drag on the label.
        field.SetValueWithoutNotify(a);
        Send(field.labelElement, PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, Vector2.zero, EventModifiers.None)));
        Send(field.labelElement, PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, new Vector2(12, 0), EventModifiers.None)));
        Send(field.labelElement, PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, new Vector2(-2, 0), EventModifiers.Shift)));
        Send(field.labelElement, PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, Vector2.zero, EventModifiers.None)));
        yield return Settle();
        Put(name + ".labelDrag", State(field));
        // Read-only: no label dragger, typed characters rejected.
        field.isReadOnly = true;
        Put(name + ".readOnly.label", Classes(field.labelElement));
        yield return Type(name + ".readOnly", field, "5", commit: false);
        field.isReadOnly = false;
        Put(name + ".readOnly.off", Classes(field.labelElement));
        field.Blur();
        yield return Settle();
        Changes();
        // Mixed values.
        field.SetValueWithoutNotify(a);
        field.showMixedValue = true;
        Put(name + ".mixed", State(field) + " classes " + Classes(field));
        field.Focus();
        yield return Settle();
        Put(name + ".mixed.focused", State(field));
        Input(field).Blur();
        yield return Settle();
        Put(name + ".mixed.blurred", State(field) + " mixed " + field.showMixedValue);
        field.showMixedValue = false;
        Put(name + ".mixed.off", State(field));
        // ClearValue.
        field.SetValueWithoutNotify(a);
        field.ClearValue();
        Put(name + ".clear", State(field));
        field.RemoveFromHierarchy();
    }

    static readonly string[] s_Expressions =
    {
        "2+2", "2*3^2", "-(1-2)*-3", "10/4", "7%3", "-7%3", "2^-1", "sqrt(16)", "sqrt(-4)", "floor(2.7)", "ceil(-2.2)", "round(2.5)", "round(3.5)",
        "cos(0)", "sin(0)", "1/3*3", "0.1+0.2", "1/3", "2/3", "1e39", "-1e39", "1e400", "1e-50", "pi", "2*pi", "1,5", "1.5f", "3l", "2.5d", "",
        " ", "  7  ", "abc", "2+", "2*", "(2+3", "2+3)", "((2))", "+=3", "x*2", "L(0,10)", "R(0,1)", "#", "9999999999", "-9999999999",
        "-5", "18446744073709551615", "18446744073709551616", "1.23456789012345678", "123456789.123", "0x10", "1 000", "--2", "3-", "1e5",
        "1E5", "+5", ".5", "5.", "1/0", "-1/0", "0/0", "2^64", "2^63", "inf", "nan", "1e+39", "3.5e38",
    };

    // An integer result is read from the expression's double value; C# leaves the conversion of a
    // double outside the integer type's range unspecified, so those cases are not recorded.
    void Evaluate()
    {
        for (int i = 0; i < s_Expressions.Length; i++)
        {
            var e = s_Expressions[i];
            Put("eval." + i + ".expression", "[" + e + "]");
            bool parsed = ExpressionEvaluator.Evaluate(e, out double d);
            bool fits(double min, double max) => !parsed || (d >= min && d < max);
            Put("eval." + i + ".double", parsed + " " + V(d));
            Put("eval." + i + ".float", ExpressionEvaluator.Evaluate(e, out float f) + " " + V(f));
            Put("eval." + i + ".int", fits(int.MinValue, 2147483648.0) ? ExpressionEvaluator.Evaluate(e, out int n) + " " + V(n) : "<unspecified>");
            Put("eval." + i + ".long", fits(long.MinValue, 9223372036854775808.0) ? ExpressionEvaluator.Evaluate(e, out long l) + " " + V(l) : "<unspecified>");
            Put("eval." + i + ".ulong", fits(double.NegativeInfinity, 18446744073709551616.0) ? ExpressionEvaluator.Evaluate(e, out ulong u) + " " + V(u) : "<unspecified>");
        }
    }

    IEnumerator Record()
    {
        Evaluate();

        yield return Exercise("int", new IntegerField("Count"), 7, -3, "000",
            new[] { "2+3*4", "12bac", "10/4", "-", "3000000000", "-3000000000", "x", "L(0,10)", "1,5", "abc" });
        yield return Exercise("long", new LongField("Big"), 9000000000L, -1L, "N0",
            new[] { "9223372036854775807", "2^62", "-5", "7/2", "-9223372036854775808" });
        yield return Exercise("float", new FloatField("Ratio"), 1.5f, -0.1f, "0.00",
            new[] { "1/3", "2^0.5", "1e39", "-1e39", "1,5", "pi", "inf", "-infinity", "nan", "abc", "0.1+0.2", "123456789" });
        yield return Exercise("double", new DoubleField("Precise"), 0.1, 1e-7, "F3",
            new[] { "1/3", "1e400", "0.1+0.2", "inf", "2/3", "1e-320" });
        yield return Exercise("uint", new UnsignedIntegerField("Mask"), 4000000000u, 0u, "X",
            new[] { "-5", "4294967296", "2*3", "4294967295" });
        yield return Exercise("ulong", new UnsignedLongField("Id"), 18446744073709551615ul, 1ul, "X",
            new[] { "-5", "1e19", "12345678901234567890", "18446744073709551615", "2^60" });

        // A float field whose text shows fewer digits than its value: the edit ends without a change,
        // then the shown text is typed again.
        var third = new FloatField("Third");
        Watch(third);
        root.Add(third);
        third.value = 1f / 3;
        Changes();
        third.Focus();
        yield return Settle();
        Input(third).Blur();
        yield return Settle();
        Put("third.unedited", State(third));
        yield return Type("third.retyped", third, "0.3333333");
        // The random function, read twice from a fresh evaluator.
        yield return Type("third.random1", third, "R(0,100)");
        yield return Type("third.random2", third, "R(0,100)");
        third.RemoveFromHierarchy();

        // The text fields of sliders read expressions.
        var slider = new Slider("S", 0, 100) { showInputField = true, value = 10 };
        var sliderInt = new SliderInt("I", 0, 100) { showInputField = true, value = 10 };
        root.Add(slider);
        root.Add(sliderInt);
        yield return Settle();
        foreach (var (key, field, typed) in new[] { ("slider.expression", (VisualElement)slider, "2*3"), ("slider.relative", slider, "+=5"), ("sliderInt.expression", sliderInt, "2*3"), ("sliderInt.relative", sliderInt, "+=5") })
        {
            var text = field.Q<TextField>();
            text.Focus();
            yield return Settle();
            text.SelectAll();
            var input = text.Q<TextElement>(className: TextElement.ussClassName + "--inner-input-field-component");
            foreach (var c in typed) Send(input, KeyDownEvent.GetPooled(c, KeyCode.None, EventModifiers.None));
            Send(input, KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None));
            yield return Settle();
            Put(key, (field is Slider s ? V(s.value) : V(((SliderInt)field).value)) + " text " + text.value);
        }
        slider.RemoveFromHierarchy();
        sliderInt.RemoveFromHierarchy();

        // A field made without a label and one with a maximum length.
        var plain = new IntegerField();
        var shortField = new FloatField("Short", 3);
        root.Add(plain);
        root.Add(shortField);
        yield return Settle();
        Put("plain", Classes(plain) + " | label " + Classes(plain.labelElement) + " | text " + plain.text + " label [" + plain.label + "]");
        yield return Type("short", shortField, "12345");
        plain.RemoveFromHierarchy();
        shortField.RemoveFromHierarchy();

        // UXML.
        var tree = Resources.Load<VisualTreeAsset>("NumericFields");
        var container = new VisualElement();
        root.Add(container);
        tree.CloneTree(container);
        yield return Settle();
        foreach (var name in new[] { "int", "long", "float", "double", "uint", "ulong" })
        {
            var e = container.Q(name);
            string described = e.GetType().Name;
            switch (e)
            {
                case IntegerField f: described += " " + V(f.value) + " " + f.text + " [" + f.label + "] delayed " + f.isDelayed + " readOnly " + f.isReadOnly + " format " + f.formatString + " max " + f.maxLength; break;
                case LongField f: described += " " + V(f.value) + " " + f.text + " [" + f.label + "] delayed " + f.isDelayed + " readOnly " + f.isReadOnly + " format " + f.formatString + " max " + f.maxLength; break;
                case FloatField f: described += " " + V(f.value) + " " + f.text + " [" + f.label + "] delayed " + f.isDelayed + " readOnly " + f.isReadOnly + " format " + f.formatString + " max " + f.maxLength; break;
                case DoubleField f: described += " " + V(f.value) + " " + f.text + " [" + f.label + "] delayed " + f.isDelayed + " readOnly " + f.isReadOnly + " format " + f.formatString + " max " + f.maxLength; break;
                case UnsignedIntegerField f: described += " " + V(f.value) + " " + f.text + " [" + f.label + "] delayed " + f.isDelayed + " readOnly " + f.isReadOnly + " format " + f.formatString + " max " + f.maxLength; break;
                case UnsignedLongField f: described += " " + V(f.value) + " " + f.text + " [" + f.label + "] delayed " + f.isDelayed + " readOnly " + f.isReadOnly + " format " + f.formatString + " max " + f.maxLength; break;
            }
            Put("uxml." + name, described);
        }
        foreach (var name in new[] { "aliases", "propertyNames", "text", "formerMask", "longMask" })
        {
            var e = container.Q(name);
            switch (e)
            {
                case IntegerField f: Put("uxml." + name, "readOnly " + f.isReadOnly + " password " + f.isPasswordField + " mask " + (int)f.maskChar + " text " + f.text); break;
                case TextField f: Put("uxml." + name, "readOnly " + f.isReadOnly + " password " + f.isPasswordField + " mask " + (int)f.maskChar + " text " + f.text); break;
            }
        }
        container.RemoveFromHierarchy();

        // A two-way data binding of an integer field's value.
        var source = new Source();
        var bound = new IntegerField("Bound") { dataSource = source };
        bound.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("count"), bindingMode = BindingMode.TwoWay, updateTrigger = BindingUpdateTrigger.EveryUpdate });
        Watch(bound);
        root.Add(bound);
        for (int i = 0; i < 4; i++) yield return null;
        Put("binding.initial", State(bound) + " source " + source.count);
        source.count = 9;
        for (int i = 0; i < 4; i++) yield return null;
        Put("binding.fromSource", State(bound) + " source " + source.count);
        bound.value = 11;
        for (int i = 0; i < 4; i++) yield return null;
        Put("binding.toSource", State(bound) + " source " + source.count);
    }
}
