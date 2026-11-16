using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Dragging a slider's label: the slider is an IValueField whose label is a FieldMouseDragger drag
// zone. A mouse press on the label starts a drag, each move applies the pointer delta through
// ApplyInputDeviceDelta (shift faster, alt slower), release ends it and Escape restores the start
// value. Values and change notifications are recorded after every step in a runtime panel.
public class SliderDragProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("SliderDragProbe").AddComponent<SliderDragProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    int changes;
    string lastChange = "";

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("SLIDER_DRAG_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static void Send(VisualElement target, EventBase evt)
    {
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    static Event Mouse(EventType type, int button, Vector2 delta, EventModifiers modifiers) =>
        new Event { type = type, button = button, mousePosition = new Vector2(5, 5), delta = delta, modifiers = modifiers, clickCount = type == EventType.MouseDown ? 1 : 0 };

    static void Down(VisualElement label, int button = 0, EventModifiers modifiers = EventModifiers.None) =>
        Send(label, PointerDownEvent.GetPooled(Mouse(EventType.MouseDown, button, Vector2.zero, modifiers)));

    static void Move(VisualElement label, float dx, float dy, EventModifiers modifiers = EventModifiers.None) =>
        Send(label, PointerMoveEvent.GetPooled(Mouse(EventType.MouseDrag, 0, new Vector2(dx, dy), modifiers)));

    static void Up(VisualElement label, int button = 0) =>
        Send(label, PointerUpEvent.GetPooled(Mouse(EventType.MouseUp, button, Vector2.zero, EventModifiers.None)));

    static void Escape(VisualElement label) =>
        Send(label, KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None));

    static string V(object v) => v is float f ? F(f) : Convert.ToString(v, CultureInfo.InvariantCulture);

    void Watch<T>(BaseField<T> field) => field.RegisterValueChangedCallback(e => { changes++; lastChange = V(e.previousValue) + ">" + V(e.newValue); });

    void State(string key, Slider slider)
    {
        Put(key + ".value", F(slider.value));
        Put(key + ".changes", changes + " " + lastChange);
        Put(key + ".captured", slider.labelElement.HasMouseCapture());
        changes = 0;
        lastChange = "";
    }

    void State(string key, SliderInt slider)
    {
        Put(key + ".value", slider.value);
        Put(key + ".changes", changes + " " + lastChange);
        Put(key + ".captured", slider.labelElement.HasMouseCapture());
        changes = 0;
        lastChange = "";
    }

    void Record()
    {
        Put("slider.valueField", typeof(IValueField<float>).IsAssignableFrom(typeof(Slider)));
        Put("sliderInt.valueField", typeof(IValueField<int>).IsAssignableFrom(typeof(SliderInt)));
        Put("minMax.valueField", typeof(IValueField<Vector2>).IsAssignableFrom(typeof(MinMaxSlider)));
        Put("baseSlider.declares", Array.IndexOf(typeof(BaseSlider<float>).GetInterfaces(), typeof(IValueField<float>)) >= 0);

        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        var go = new GameObject("Document");
        var document = go.AddComponent<UIDocument>();
        document.panelSettings = settings;
        var root = document.rootVisualElement;

        var slider = new Slider("Amount", 0f, 10f) { value = 5f };
        var sliderInt = new SliderInt("Count", 0, 100) { value = 50 };
        var wide = new Slider("Wide", -1000f, 1000f) { value = 0f };
        var unlabeled = new Slider(0f, 10f) { value = 5f };
        root.Add(slider);
        root.Add(sliderInt);
        root.Add(wide);
        root.Add(unlabeled);
        Watch(slider);
        Watch(sliderInt);
        Watch(wide);
        Put("slider.labelDragClass", slider.labelElement.ClassListContains(BaseSlider<float>.labelDraggerVariantUssClassName));
        Put("unlabeled.labelDragClass", unlabeled.labelElement.ClassListContains(BaseSlider<float>.labelDraggerVariantUssClassName));
        State("start", slider);

        // A drag on the label: horizontal, vertical (up is positive), diagonal, shift and alt.
        var label = slider.labelElement;
        Down(label);
        State("down", slider);
        Move(label, 10f, 0f);
        State("right10", slider);
        Move(label, -4f, 0f);
        State("left4", slider);
        Move(label, 0f, -6f);
        State("up6", slider);
        Move(label, 0f, 3f);
        State("down3", slider);
        Move(label, 3f, 3.1f);
        State("diagonal", slider);
        Move(label, 5f, 0f, EventModifiers.Shift);
        State("shift5", slider);
        Move(label, 8f, 0f, EventModifiers.Alt);
        State("alt8", slider);
        Move(label, 0f, 0f);
        State("zero", slider);
        Move(label, 500f, 0f);
        State("right500", slider);
        Move(label, 3000f, 0f);
        State("past.high", slider);
        Move(label, 10f, 0f);
        State("past.high.more", slider);
        Move(label, -6000f, 0f);
        State("past.low", slider);
        Move(label, 7f, 1f);
        State("near.horizontal", slider);
        // Moves whose axes are within a tenth of each other keep the axis of the move before.
        Move(label, 3f, 3.1f);
        State("axis.kept.x", slider);
        Move(label, 1f, -4f);
        State("axis.to.y", slider);
        Move(label, -3.1f, 3f);
        State("axis.kept.y", slider);
        Move(label, 10f, 10.9f);
        State("axis.ratio.below", slider);
        Move(label, 10f, 0f);
        Move(label, 10f, 11.2f);
        State("axis.ratio.above", slider);
        Up(label);
        State("up", slider);
        Move(label, 10f, 0f);
        State("move.after.up", slider);

        // A touch press does not start a drag in a runtime panel.
        slider.value = 3f;
        changes = 0;
        lastChange = "";
        Send(label, PointerDownEvent.GetPooled(new Touch { fingerId = 0, position = new Vector2(5, 5), phase = TouchPhase.Began, tapCount = 1 }));
        State("touch.down", slider);
        Send(label, PointerMoveEvent.GetPooled(new Touch { fingerId = 0, position = new Vector2(25, 5), deltaPosition = new Vector2(20, 0), phase = TouchPhase.Moved }));
        State("touch.move", slider);
        Send(label, PointerUpEvent.GetPooled(new Touch { fingerId = 0, position = new Vector2(25, 5), phase = TouchPhase.Ended }));
        State("touch.up", slider);

        // Escape during a drag restores the value the drag started from.
        slider.value = 4f;
        State("set4", slider);
        Down(label);
        Move(label, 20f, 0f);
        State("escape.moved", slider);
        Escape(label);
        State("escape", slider);
        Move(label, 20f, 0f);
        State("escape.move", slider);
        Up(label);
        State("escape.up", slider);

        // Only the left button starts a drag.
        Down(label, 1);
        Move(label, 20f, 0f);
        State("right.button", slider);
        Up(label, 1);

        // An integer slider rounds each move's delta to whole steps.
        var intLabel = sliderInt.labelElement;
        Down(intLabel);
        Move(intLabel, 10f, 0f);
        State("int.right10", sliderInt);
        Move(intLabel, 0.4f, 0f);
        State("int.right0.4", sliderInt);
        Move(intLabel, 0.6f, 0f);
        State("int.right0.6", sliderInt);
        Move(intLabel, -3f, 0f, EventModifiers.Shift);
        State("int.shift.left3", sliderInt);
        Move(intLabel, 3f, 0f, EventModifiers.Alt);
        State("int.alt3", sliderInt);
        Move(intLabel, 1f, 0f, EventModifiers.Alt);
        State("int.alt1", sliderInt);
        Move(intLabel, 2f, 0f, EventModifiers.Alt);
        State("int.alt2", sliderInt);
        Move(intLabel, 500f, 0f);
        State("int.past.high", sliderInt);
        Move(intLabel, -500f, 0f);
        Move(intLabel, 30f, 0f);
        State("int.back", sliderInt);
        Up(intLabel);
        // The axis kept is shared by every field: a move on another field keeps it.
        Down(label);
        Move(label, 0f, -5f);
        Up(label);
        Down(intLabel);
        Move(intLabel, 31f, 30f);
        State("int.axis.shared", sliderInt);
        Up(intLabel);
        State("int.up", sliderInt);

        // The sensitivity follows the slider's range.
        var wideLabel = wide.labelElement;
        Down(wideLabel);
        Move(wideLabel, 10f, 0f);
        State("wide.right10", wide);
        Move(wideLabel, 1f, 0f);
        State("wide.right1", wide);
        Up(wideLabel);

        // An integer slider's own state: defaults, classes, whole page sizes and keys.
        var plain = new SliderInt();
        Put("int.default", plain.lowValue + " " + plain.highValue + " " + plain.value + " " + F(plain.pageSize) + " " + plain.range);
        Put("int.classes", string.Join(" ", plain.GetClasses()));
        Put("int.label.classes", string.Join(" ", plain.labelElement.GetClasses()));
        plain.pageSize = 2.6f;
        Put("int.pageSize.2.6", F(plain.pageSize));
        plain.pageSize = 2.5f;
        Put("int.pageSize.2.5", F(plain.pageSize));
        var keyed = new SliderInt("Keys", 0, 1000) { value = 503, pageSize = 7f };
        root.Add(keyed);
        Watch(keyed);
        changes = 0;
        void Key(string name, KeyCode code, EventModifiers modifiers = EventModifiers.None)
        {
            Send(keyed, KeyDownEvent.GetPooled('\0', code, modifiers));
            State("int.key." + name, keyed);
        }
        Key("right", KeyCode.RightArrow);
        Key("right.again", KeyCode.RightArrow);
        Key("left", KeyCode.LeftArrow);
        Key("shift.right", KeyCode.RightArrow, EventModifiers.Shift);
        Key("pageUp", KeyCode.PageUp);
        Key("pageDown", KeyCode.PageDown);
        Key("home", KeyCode.Home);
        Key("end", KeyCode.End);
        var narrow = new SliderInt("Narrow", 0, 20) { value = 7 };
        root.Add(narrow);
        Send(narrow, KeyDownEvent.GetPooled('\0', KeyCode.RightArrow, EventModifiers.None));
        Put("int.narrow.right", narrow.value);
        // Navigation moves the dragger once a submit has made it movable.
        Send(narrow, NavigationSubmitEvent.GetPooled());
        Send(narrow, NavigationMoveEvent.GetPooled(Vector2.right));
        Put("int.narrow.nav.right", narrow.value);
        Send(narrow, NavigationMoveEvent.GetPooled(Vector2.right, EventModifiers.Shift));
        Put("int.narrow.nav.shift.right", narrow.value);
        Send(narrow, NavigationMoveEvent.GetPooled(Vector2.left));
        Put("int.narrow.nav.left", narrow.value);
        keyed.value = 503;
        Send(keyed, NavigationSubmitEvent.GetPooled());
        Send(keyed, NavigationMoveEvent.GetPooled(Vector2.right));
        Put("int.keyed.nav.right", keyed.value);
        Send(keyed, NavigationMoveEvent.GetPooled(Vector2.right, EventModifiers.Shift));
        Put("int.keyed.nav.shift.right", keyed.value);
        var reversed = new SliderInt("Reversed", 10, 0) { value = 4 };
        root.Add(reversed);
        Put("int.reversed.range", reversed.range);
        reversed.value = 15;
        Put("int.reversed.clamp", reversed.value);

        // The interface's own calls: ApplyInputDeviceDelta uses the current value, not the start.
        slider.value = 2f;
        changes = 0;
        IValueField<float> field = slider;
        field.StartDragging();
        field.ApplyInputDeviceDelta(new Vector3(10f, 0f, 0f), DeltaSpeed.Normal, 9f);
        State("direct.normal", slider);
        field.ApplyInputDeviceDelta(new Vector3(10f, 0f, 0f), DeltaSpeed.Fast, 0f);
        State("direct.fast", slider);
        field.ApplyInputDeviceDelta(new Vector3(10f, 0f, 0f), DeltaSpeed.Slow, 0f);
        State("direct.slow", slider);
        field.ApplyInputDeviceDelta(new Vector3(0f, 0f, 50f), DeltaSpeed.Normal, 0f);
        State("direct.z", slider);
        field.StopDragging();
        field.value = 7f;
        State("direct.value", slider);
        IValueField<int> intField = sliderInt;
        intField.ApplyInputDeviceDelta(new Vector3(-12f, 0f, 0f), DeltaSpeed.Normal, 0);
        State("direct.int", sliderInt);
    }
}
