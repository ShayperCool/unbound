#pragma warning disable CS0618 // the deprecated UxmlFactory/UxmlTraits path is measured too
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
using UnityEngine.UIElements;

namespace ControlsProbe
{
    // Game elements created through the deprecated factories over the built-in traits.
    public class OldHelp : HelpBox
    {
        public new class UxmlFactory : UxmlFactory<OldHelp, HelpBox.UxmlTraits> { }
    }

    public class OldPopup : UnityEngine.UIElements.PopupWindow
    {
        public new class UxmlFactory : UxmlFactory<OldPopup, UnityEngine.UIElements.PopupWindow.UxmlTraits> { }
    }

    public class OldGroup : ToggleButtonGroup
    {
        public new class UxmlFactory : UxmlFactory<OldGroup, ToggleButtonGroup.UxmlTraits> { }
    }

    public class OldSplit : TwoPaneSplitView
    {
        public new class UxmlFactory : UxmlFactory<OldSplit, TwoPaneSplitView.UxmlTraits> { }
    }

    [Flags] public enum ByteFlags : byte { A = 1, B = 2, High = 128 }
    [Flags] public enum ShortFlags : short { A = 1, Top = unchecked((short)0x8000) }
    [Flags] public enum IntFlags { A = 1, B = 2, C = 4, D = 8 }
    [Flags] public enum LongFlags : long { A = 1, Big = 1L << 40 }
    public enum Plain { A, B }

    /// <summary>
    /// Records HelpBox, PopupWindow, ToggleButtonGroup (and its state type) and TwoPaneSplitView on a
    /// runtime panel drawing into an 800x600 target: hierarchy and USS classes, properties, the
    /// group's selection rules under submitted clicks, the split view's pane sizes after layout,
    /// resizing, collapsing and dragging, and the controls created from UXML through the UXML
    /// serialization path and through the deprecated factories.
    /// </summary>
    public class ControlsProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly PropertyInfo s_PseudoStates = typeof(VisualElement).GetProperty("pseudoStates", BindingFlags.Instance | BindingFlags.NonPublic);
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Logs = new List<string>();
        VisualElement m_Root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_CONTROLS_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<ControlsProbeBehaviour>();
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("UITK_CONTROLS_OUT");
            Application.logMessageReceived += OnLog;
            var document = GetComponent<UIDocument>();
            var settings = document.panelSettings;
            var target = new RenderTexture(800, 600, 24, RenderTextureFormat.ARGB32);
            target.Create();
            settings.targetTexture = target;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            m_Root = document.rootVisualElement;
            bool failed = false;
            var steps = new List<Func<IEnumerator>> { HelpBoxes, Popups, States, Groups, Splits, Uxml };
            foreach (var step in steps)
            {
                var routine = step();
                while (true)
                {
                    object current;
                    try
                    {
                        if (!routine.MoveNext()) break;
                        current = routine.Current;
                    }
                    catch (Exception e)
                    {
                        Put("error." + step.Method.Name, e.GetType().Name + ": " + FirstLine(e.Message));
                        failed = true;
                        break;
                    }
                    yield return current;
                }
            }
            Application.logMessageReceived -= OnLog;
            if (!failed) Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Log) m_Logs.Add(type + ": " + FirstLine(message));
        }

        /// <summary>The logs since the last call, as one value.</summary>
        string TakeLogs()
        {
            var text = string.Join(" | ", m_Logs);
            m_Logs.Clear();
            return text;
        }

        static string FirstLine(string text)
        {
            if (text == null) return "null";
            int newline = text.IndexOf('\n');
            return (newline < 0 ? text : text.Substring(0, newline)).TrimEnd('\r');
        }

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(Convert.ToString(value, Inv))).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string F(float value) => value.ToString("0.###", Inv);

        /// <summary>An exception's type, parameter and message (without the runtime's own parameter
        /// suffix), or "none".</summary>
        static string Throws(Action action)
        {
            try
            {
                action();
                return "none";
            }
            catch (Exception e)
            {
                string message = FirstLine(e.Message);
                int suffix = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
                if (suffix >= 0) message = message.Substring(0, suffix);
                return e.GetType().Name + " param " + ((e as ArgumentException)?.ParamName ?? "-") + ": " + message;
            }
        }

        /// <summary>An element's hierarchy: name, classes and hierarchy children.</summary>
        static string Tree(VisualElement e)
        {
            var sb = new StringBuilder();
            sb.Append(e.GetType().Name).Append('#').Append(e.name ?? "null").Append('[').Append(string.Join(" ", e.GetClasses())).Append(']');
            if (e is TextElement t && !(e is ToggleButtonGroup) && t.text.Length > 0) sb.Append('"').Append(t.text).Append('"');
            if (e.hierarchy.childCount > 0)
            {
                sb.Append('{');
                for (int i = 0; i < e.hierarchy.childCount; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Tree(e.hierarchy[i]));
                }
                sb.Append('}');
            }
            return sb.ToString();
        }

        // The checked pseudo state (internal: read by reflection).
        static bool Checked(VisualElement e) => (Convert.ToInt32(s_PseudoStates.GetValue(e)) & 8) != 0;

        // ---- HelpBox -------------------------------------------------------------------------

        IEnumerator HelpBoxes()
        {
            var plain = new HelpBox();
            Put("help.default", Tree(plain) + " type " + plain.messageType + " text '" + plain.text + "'");
            var box = new HelpBox("Saved", HelpBoxMessageType.Warning);
            Put("help.warning", Tree(box));
            box.messageType = HelpBoxMessageType.Error;
            Put("help.toError", Tree(box));
            box.messageType = HelpBoxMessageType.None;
            Put("help.toNone", Tree(box));
            box.messageType = HelpBoxMessageType.Info;
            Put("help.toInfo", Tree(box));
            box.messageType = (HelpBoxMessageType)7;
            Put("help.toUndefined", Tree(box) + " type " + (int)box.messageType);
            box.text = null;
            Put("help.textNull", "'" + (box.text ?? "null") + "'");
            // A child added by a game goes after the label.
            box.messageType = HelpBoxMessageType.Info;
            box.Add(new VisualElement { name = "extra" });
            box.messageType = HelpBoxMessageType.None;
            box.messageType = HelpBoxMessageType.Warning;
            Put("help.withChild", Tree(box));
            yield break;
        }

        // ---- PopupWindow ---------------------------------------------------------------------

        IEnumerator Popups()
        {
            var popup = new UnityEngine.UIElements.PopupWindow();
            Put("popup.default", Tree(popup) + " content " + popup.contentContainer.name + " children " + popup.childCount);
            popup.text = "Title";
            popup.Add(new Label("inside") { name = "inside" });
            Put("popup.withChild", Tree(popup) + " children " + popup.childCount);
            yield break;
        }

        // ---- ToggleButtonGroupState ----------------------------------------------------------

        static string S(ToggleButtonGroupState state) => state.ToString() + "/" + state.length;

        IEnumerator States()
        {
            var three = new ToggleButtonGroupState(0b1011, 3);
            Put("state.ctorMasked", S(three) + " " + three[0] + three[1] + three[2]);
            Put("state.full64", S(new ToggleButtonGroupState(ulong.MaxValue, 64)));
            Put("state.zeroLength", S(new ToggleButtonGroupState(5, 0)));
            Put("state.default", S(default));
            Put("state.ctor65", Throws(() => new ToggleButtonGroupState(0, 65)));
            Put("state.ctorNegative", Throws(() => new ToggleButtonGroupState(0, -1)));
            Put("state.getPastEnd", Throws(() => { bool _ = three[3]; }));
            Put("state.setNegative", Throws(() => { var s = three; s[-1] = true; }));
            Put("state.spanShort", Throws(() => three.GetActiveOptions(new int[2])));
            Span<int> buffer = stackalloc int[8];
            Put("state.active", string.Join(",", three.GetActiveOptions(buffer).ToArray()));
            Put("state.inactive", string.Join(",", three.GetInactiveOptions(buffer).ToArray()));
            var five = new ToggleButtonGroupState(0b00100, 5);
            five.SetAllOptions();
            Put("state.setAll", S(five));
            five[1] = false;
            five.ToggleAllOptions();
            Put("state.toggleAll", S(five));
            five.ResetAllOptions();
            Put("state.resetAll", S(five));
            Put("state.fromOptions", S(ToggleButtonGroupState.CreateFromOptions(new[] { true, false, true, true })));
            Put("state.fromOptionsEmpty", S(ToggleButtonGroupState.CreateFromOptions(new List<bool>())));
            Put("state.fromByteFlags", S(ToggleButtonGroupState.FromEnumFlags(ByteFlags.A | ByteFlags.High)));
            Put("state.fromIntFlags", S(ToggleButtonGroupState.FromEnumFlags(IntFlags.B | IntFlags.D, 5)));
            Put("state.fromLongFlags", S(ToggleButtonGroupState.FromEnumFlags(LongFlags.A | LongFlags.Big)));
            Put("state.fromNegativeInt", S(ToggleButtonGroupState.FromEnumFlags((IntFlags)(-2))));
            // Enums narrower than 32 bits: at the full length and in Compare.
            Put("state.fromByte64", S(ToggleButtonGroupState.FromEnumFlags(ByteFlags.A | ByteFlags.High, 64)));
            Put("state.fromShort64", S(ToggleButtonGroupState.FromEnumFlags(ShortFlags.A | ShortFlags.Top, 64)));
            Put("state.compareNarrow", ToggleButtonGroupState.Compare(new ToggleButtonGroupState(0x81, 8), ByteFlags.A | ByteFlags.High) + " "
                + ToggleButtonGroupState.Compare(new ToggleButtonGroupState(0x8001, 16), ShortFlags.A | ShortFlags.Top) + " "
                + ToggleButtonGroupState.Compare(new ToggleButtonGroupState(0x8001, 64), ShortFlags.A | ShortFlags.Top));
            Put("state.fromNotFlags", Throws(() => ToggleButtonGroupState.FromEnumFlags(Plain.B)));
            Put("state.fromTooLong", Throws(() => ToggleButtonGroupState.FromEnumFlags(IntFlags.A, 70)));
            var options = new ToggleButtonGroupState(0b0101, 4);
            Put("state.toIntFlags", ToggleButtonGroupState.ToEnumFlags<IntFlags>(options).ToString());
            Put("state.toIntFlagsStrict", Throws(() => ToggleButtonGroupState.ToEnumFlags<IntFlags>(options, false)));
            Put("state.toByteFlagsStrict", ToggleButtonGroupState.ToEnumFlags<ByteFlags>(new ToggleButtonGroupState(0b11, 8), false).ToString());
            Put("state.toNotFlags", Throws(() => ToggleButtonGroupState.ToEnumFlags<Plain>(options)));
            Put("state.compare", ToggleButtonGroupState.Compare(options, IntFlags.A | IntFlags.C) + " " + ToggleButtonGroupState.Compare(options, IntFlags.A));
            var same = new ToggleButtonGroupState(0b0101, 4);
            var longer = new ToggleButtonGroupState(0b0101, 5);
            Put("state.equality", (options == same) + " " + (options != longer) + " " + options.Equals((object)same) + " " + options.Equals((object)"x")
                + " hash " + (options.GetHashCode() == same.GetHashCode()));
            Put("state.compareTo", options.CompareTo(same) + " " + options.CompareTo(longer));
            Put("state.json", JsonUtility.ToJson(new Holder { state = new ToggleButtonGroupState(ulong.MaxValue, 64), small = options }));
            var read = JsonUtility.FromJson<Holder>("{\"state\":{\"m_Data\":255,\"m_Length\":3}}");
            Put("state.jsonRead", S(read.state) + " " + read.state[2]);
            var attribute = new ToggleButtonGroupStatePropertiesAttribute();
            var custom = new ToggleButtonGroupStatePropertiesAttribute(false, false, 3);
            Put("state.attribute", attribute.allowMultipleSelection + " " + attribute.allowEmptySelection + " " + attribute.length
                + " / " + custom.allowMultipleSelection + " " + custom.allowEmptySelection + " " + custom.length);
            yield break;
        }

        [Serializable]
        class Holder
        {
            public ToggleButtonGroupState state;
            public ToggleButtonGroupState small;
        }

        // ---- ToggleButtonGroup ---------------------------------------------------------------

        int m_Changes;
        string m_LastChange;

        string Group(ToggleButtonGroup group)
        {
            var buttons = group.Query<Button>().ToList();
            var sb = new StringBuilder(S(group.value));
            sb.Append(" checked ");
            foreach (var b in buttons) sb.Append(Checked(b) ? '1' : '0');
            sb.Append(" changes ").Append(m_Changes);
            if (m_LastChange != null) sb.Append(' ').Append(m_LastChange);
            m_LastChange = null;
            m_Changes = 0;
            return sb.ToString();
        }

        void Watch(ToggleButtonGroup group) =>
            group.RegisterValueChangedCallback(e => { m_Changes++; m_LastChange = S(e.previousValue) + "->" + S(e.newValue); });

        static void Click(Button button)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        IEnumerator Groups()
        {
            var plain = new ToggleButtonGroup();
            Put("group.default", Tree(plain) + " " + S(plain.value) + " multiple " + plain.isMultipleSelection + " empty " + plain.allowEmptySelection
                + " content " + plain.contentContainer.name + " focusable " + plain.focusable);
            Put("group.labelled", Tree(new ToggleButtonGroup("Label")));
            var none = new ToggleButtonGroup(new ToggleButtonGroupState(0, 0));
            Put("group.noOptions", Tree(none) + " " + S(none.value));
            none.SetValueWithoutNotify(new ToggleButtonGroupState(1, 2));
            Put("group.noOptionsThenTwo", Tree(none) + " " + S(none.value));

            var group = new ToggleButtonGroup("Pick");
            m_Root.Add(group);
            Watch(group);
            var buttons = new List<Button>();
            for (int i = 0; i < 3; i++)
            {
                var b = new Button { name = "b" + i, text = "B" + i };
                buttons.Add(b);
                group.Add(b);
                Put("group.add" + i, Group(group) + " " + string.Join(" ", buttons.Select(x => string.Join(".", x.GetClasses()))));
            }
            group.Add(new Label("note") { name = "note" });
            Put("group.addLabel", Tree(group));
            yield return null;

            // Single selection, no empty selection (the defaults).
            Click(buttons[2]);
            yield return null;
            Put("group.single.click2", Group(group));
            Click(buttons[2]);
            yield return null;
            Put("group.single.click2Again", Group(group));
            group.allowEmptySelection = true;
            Click(buttons[2]);
            yield return null;
            Put("group.singleEmpty.click2", Group(group));
            Click(buttons[1]);
            yield return null;
            Put("group.singleEmpty.click1", Group(group));
            Click(buttons[0]);
            yield return null;
            Put("group.singleEmpty.click0", Group(group));

            group.isMultipleSelection = true;
            Click(buttons[2]);
            yield return null;
            Put("group.multiple.click2", Group(group));
            Click(buttons[1]);
            yield return null;
            Put("group.multiple.click1", Group(group));
            Click(buttons[0]);
            yield return null;
            Put("group.multiple.click0", Group(group));
            group.allowEmptySelection = false;
            Click(buttons[1]);
            Click(buttons[2]);
            yield return null;
            Put("group.multipleNoEmpty.clear12", Group(group));
            Click(buttons[0]);
            yield return null;
            Put("group.multipleNoEmpty.clickLast", Group(group));
            Click(buttons[1]);
            Click(buttons[2]);
            yield return null;
            group.isMultipleSelection = false;
            Put("group.toSingle", Group(group) + " " + group.isMultipleSelection);

            group.allowEmptySelection = true;
            group.SetValueWithoutNotify(new ToggleButtonGroupState(0, 3));
            Put("group.cleared", Group(group));
            group.allowEmptySelection = false;
            Put("group.disallowEmpty", Group(group));

            group.value = new ToggleButtonGroupState(0b1111, 4);
            Put("group.valueLonger", Group(group));
            group.value = new ToggleButtonGroupState(0b10, 3);

            group.showMixedValue = true;
            Put("group.mixed", Group(group));
            Click(buttons[0]);
            yield return null;
            Put("group.mixed.click0", Group(group) + " mixed " + group.showMixedValue);
            group.showMixedValue = false;
            Put("group.unmixed", Group(group));

            // Removing buttons.
            group.isMultipleSelection = true;
            group.allowEmptySelection = true;
            group.value = new ToggleButtonGroupState(0b110, 3);
            Group(group);
            group.Remove(buttons[1]);
            Put("group.removeActiveMiddle", Group(group) + " " + string.Join(" ", group.Query<Button>().ToList().Select(x => string.Join(".", x.GetClasses()))) + " " + TakeLogs());
            group.Remove(buttons[0]);
            Put("group.removeInactive", Group(group) + " " + TakeLogs());
            Put("group.removeLastActive", Throws(() => group.Remove(buttons[2])) + " " + S(group.value) + " " + TakeLogs());
            Put("group.afterRemoveAll", Tree(group) + " " + S(group.value));

            // A group holds at most 64 options.
            var big = new ToggleButtonGroup();
            for (int i = 0; i < 66; i++) big.Add(new Button { name = "x" + i });
            Put("group.overflow", S(big.value) + " children " + big.contentContainer.childCount + " classed "
                + big.Query<Button>().ToList().Count(b => b.ClassListContains(ToggleButtonGroup.buttonClassName)) + " logs " + m_Logs.Count + " " + (m_Logs.Count > 0 ? m_Logs[0] : ""));
            m_Logs.Clear();
            m_Root.Remove(group);
        }

        // ---- TwoPaneSplitView ----------------------------------------------------------------

        static string Pane(VisualElement e) =>
            e.name + " " + F(e.layout.x) + "," + F(e.layout.y) + " " + F(e.layout.width) + "x" + F(e.layout.height)
            + " display " + e.resolvedStyle.display;

        static string Split(TwoPaneSplitView view)
        {
            var anchor = view.Q("unity-dragline-anchor");
            var content = view.Q("unity-content-container");
            return "fixed " + (view.fixedPane?.name ?? "null") + " flexed " + (view.flexedPane?.name ?? "null")
                + " | " + Pane(content[0]) + " | " + Pane(content[1])
                + " | anchor " + F(anchor.layout.x) + "," + F(anchor.layout.y) + " " + F(anchor.layout.width) + "x" + F(anchor.layout.height)
                + " | content padding " + F(content.resolvedStyle.paddingRight) + "," + F(content.resolvedStyle.paddingBottom);
        }

        static TwoPaneSplitView MakeSplit(string name, TwoPaneSplitView view, float minFixed = 0, float minFlexed = 0)
        {
            view.name = name;
            view.style.width = 400;
            view.style.height = 300;
            view.style.flexShrink = 0;
            var first = new VisualElement { name = name + "A" };
            var second = new VisualElement { name = name + "B" };
            view.Add(first);
            view.Add(second);
            var fixedPane = view.fixedPaneIndex == 0 ? first : second;
            var flexedPane = view.fixedPaneIndex == 0 ? second : first;
            if (minFixed > 0)
            {
                fixedPane.style.minWidth = minFixed;
                fixedPane.style.minHeight = minFixed;
            }
            if (minFlexed > 0)
            {
                flexedPane.style.minWidth = minFlexed;
                flexedPane.style.minHeight = minFlexed;
            }
            return view;
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        static void Pointer(VisualElement target, EventType type, Vector2 position)
        {
            var imgui = new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui)
                : type == EventType.MouseUp ? PointerUpEvent.GetPooled(imgui)
                : (EventBase)PointerMoveEvent.GetPooled(imgui);
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
        }

        IEnumerator Drag(TwoPaneSplitView view, Vector2 delta)
        {
            var anchor = view.Q("unity-dragline-anchor");
            var start = anchor.worldBound.center;
            Pointer(anchor, EventType.MouseDown, start);
            yield return null;
            Pointer(anchor, EventType.MouseDrag, start + delta * 0.5f);
            yield return null;
            Pointer(anchor, EventType.MouseDrag, start + delta);
            yield return null;
            Pointer(anchor, EventType.MouseUp, start + delta);
            yield return Frames(2);
        }

        IEnumerator Splits()
        {
            var detached = new TwoPaneSplitView();
            Put("split.default", Tree(detached) + " index " + detached.fixedPaneIndex + " dimension " + F(detached.fixedPaneInitialDimension)
                + " orientation " + detached.orientation + " fixed " + (detached.fixedPane == null) + " content " + detached.contentContainer.name);
            var vertical = new TwoPaneSplitView(1, 50, TwoPaneSplitViewOrientation.Vertical);
            Put("split.verticalCtor", Tree(vertical));
            vertical.orientation = TwoPaneSplitViewOrientation.Horizontal;
            Put("split.toHorizontal", Tree(vertical));

            var row = new VisualElement { name = "row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            m_Root.Add(row);
            var h0 = MakeSplit("h0", new TwoPaneSplitView(0, 120, TwoPaneSplitViewOrientation.Horizontal));
            var h1 = MakeSplit("h1", new TwoPaneSplitView(1, 120, TwoPaneSplitViewOrientation.Horizontal), minFlexed: 50);
            var v0 = MakeSplit("v0", new TwoPaneSplitView(0, 80, TwoPaneSplitViewOrientation.Vertical), minFixed: 30);
            var pending = MakeSplit("pending", new TwoPaneSplitView(0, 100, TwoPaneSplitViewOrientation.Horizontal));
            // With a view-data key the view keeps a dragged size when it is set up again.
            var keyed = MakeSplit("keyed", new TwoPaneSplitView(0, 100, TwoPaneSplitViewOrientation.Horizontal));
            keyed.viewDataKey = "keyed";
            pending.CollapseChild(1);
            row.Add(h0);
            row.Add(h1);
            row.Add(v0);
            row.Add(pending);
            row.Add(keyed);
            yield return Frames(3);
            foreach (var view in new[] { h0, h1, v0, pending, keyed }) Put("split.laid." + view.name, Split(view));
            Put("split.laid.tree", Tree(h0));
            Put("split.laid.logs", TakeLogs());

            // The view shrinks: the flexed pane keeps its minimum, then the fixed pane its own.
            h1.style.width = 140;
            v0.style.height = 70;
            yield return Frames(3);
            Put("split.shrunk.h1", Split(h1));
            Put("split.shrunk.v0", Split(v0));
            h1.style.width = 60;
            v0.style.height = 20;
            yield return Frames(3);
            Put("split.tiny.h1", Split(h1));
            Put("split.tiny.v0", Split(v0));
            h1.style.width = 400;
            v0.style.height = 300;
            yield return Frames(3);
            Put("split.regrown.h1", Split(h1));
            Put("split.regrown.v0", Split(v0));

            // Dragging the line.
            yield return Drag(h0, new Vector2(40, 0));
            Put("split.drag.h0", Split(h0));
            yield return Drag(h0, new Vector2(-500, 0));
            Put("split.dragPastMin.h0", Split(h0));
            yield return Drag(h1, new Vector2(-60, 0));
            Put("split.drag.h1", Split(h1));
            yield return Drag(v0, new Vector2(0, 500));
            Put("split.dragPastMax.v0", Split(v0));
            yield return Drag(keyed, new Vector2(50, 0));
            Put("split.drag.keyed", Split(keyed));
            Put("split.dragLogs", TakeLogs());
            keyed.orientation = TwoPaneSplitViewOrientation.Vertical;
            h1.orientation = TwoPaneSplitViewOrientation.Vertical;
            yield return Frames(3);
            Put("split.keyedSetUpAgain", Split(keyed));
            Put("split.unkeyedSetUpAgain", Split(h1));

            // Collapsing and restoring.
            h0.CollapseChild(0);
            yield return Frames(2);
            Put("split.collapse0.h0", Split(h0));
            h0.UnCollapse();
            yield return Frames(3);
            Put("split.uncollapse.h0", Split(h0));
            h0.CollapseChild(2);
            Put("split.collapseInvalid", TakeLogs());
            h0.UnCollapse();
            yield return Frames(2);
            Put("split.uncollapseNotCollapsed.h0", Split(h0));

            // Changing a property sets the view up again.
            h0.fixedPaneIndex = 1;
            yield return Frames(3);
            Put("split.toIndex1.h0", Split(h0));
            h0.orientation = TwoPaneSplitViewOrientation.Vertical;
            yield return Frames(3);
            Put("split.toVertical.h0", Split(h0));
            h0.fixedPaneInitialDimension = 60;
            yield return Frames(3);
            Put("split.toDimension60.h0", Split(h0));

            // One child only.
            var single = new TwoPaneSplitView { name = "single" };
            single.Add(new VisualElement());
            row.Add(single);
            yield return Frames(3);
            Put("split.oneChild", TakeLogs() + " fixed " + (single.fixedPane == null));
            m_Root.Remove(row);
        }

        // ---- UXML ----------------------------------------------------------------------------

        IEnumerator Uxml()
        {
            var holder = new VisualElement { name = "uxml" };
            m_Root.Add(holder);
            Resources.Load<VisualTreeAsset>("Controls").CloneTree(holder);
            Put("uxml.cloneLogs", TakeLogs());
            yield return Frames(3);
            foreach (var name in new[] { "help", "helpPlain", "oldHelp", "oldHelpEmpty" })
            {
                var box = holder.Q<HelpBox>(name);
                Put("uxml." + name, Tree(box) + " type " + box.messageType + " text '" + box.text + "'");
            }
            foreach (var name in new[] { "popup", "oldPopup" })
                Put("uxml." + name, Tree(holder.Q(name)));
            foreach (var name in new[] { "group", "groupDefault", "oldGroup" })
            {
                var group = holder.Q<ToggleButtonGroup>(name);
                Put("uxml." + name, Tree(group) + " " + S(group.value) + " multiple " + group.isMultipleSelection + " empty " + group.allowEmptySelection
                    + " label '" + group.label + "'");
            }
            foreach (var name in new[] { "split", "splitDefault", "oldSplit", "oldSplitInt" })
            {
                var view = holder.Q<TwoPaneSplitView>(name);
                Put("uxml." + name, "index " + view.fixedPaneIndex + " dimension " + F(view.fixedPaneInitialDimension) + " orientation " + view.orientation
                    + " | " + Split(view));
            }
            m_Root.Remove(holder);
        }
    }
}
