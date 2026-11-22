using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The editor's IMGUI state and controls. Unbound has no editor windows, so editor GUI runs as in
    /// the batch-mode Editor: the state (indent, change checks, disabled and mixed-value scopes) and
    /// the measurements (property heights) work anywhere; drawing outside OnGUI throws as the
    /// Editor's does (measured, tests/projects/editorui).
    /// </summary>
    public sealed partial class EditorGUI
    {
        static int s_IndentLevel;
        static readonly Stack<bool> s_ChangedStack = new Stack<bool>();
        static readonly Stack<bool> s_EnabledStack = new Stack<bool>();

        /// <summary>The indentation of controls, 15 pixels a level.</summary>
        public static int indentLevel { get => s_IndentLevel; set => s_IndentLevel = value; }

        internal static float indent => s_IndentLevel * 15f;

        public static bool showMixedValue { get; set; }

        public static bool actionKey => Event.current != null && (Application.platform == RuntimePlatform.OSXEditor ? Event.current.command : Event.current.control);

        /// <summary>Starts a block whose controls' changes EndChangeCheck reports.</summary>
        public static void BeginChangeCheck()
        {
            s_ChangedStack.Push(GUI.changed);
            GUI.changed = false;
        }

        /// <summary>Whether a control changed since BeginChangeCheck (GUI.changed); the outer block
        /// sees it changed too. Unbalanced, it reports a change.</summary>
        public static bool EndChangeCheck()
        {
            if (s_ChangedStack.Count == 0)
            {
                GUI.changed = true;
                return true;
            }
            bool changed = GUI.changed;
            GUI.changed |= s_ChangedStack.Pop();
            return changed;
        }

        public static void BeginDisabledGroup(bool disabled)
        {
            s_EnabledStack.Push(GUI.enabled);
            GUI.enabled &= !disabled;
        }

        public static void EndDisabledGroup()
        {
            if (s_EnabledStack.Count > 0) GUI.enabled = s_EnabledStack.Pop();
        }

        public static Rect IndentedRect(Rect source)
        {
            float x = indent;
            return new Rect(source.x + x, source.y, source.width - x, source.height);
        }

        public partial struct DisabledScope : IDisposable
        {
            bool m_Disposed;

            public DisabledScope(bool disabled)
            {
                m_Disposed = false;
                BeginDisabledGroup(disabled);
            }

            public void Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                EndDisabledGroup();
            }
        }

        public partial class DisabledGroupScope : GUI.Scope
        {
            public DisabledGroupScope(bool disabled) => BeginDisabledGroup(disabled);

            protected override void CloseScope() => EndDisabledGroup();
        }

        public partial class ChangeCheckScope : GUI.Scope
        {
            bool m_ChangeChecked;
            bool m_Changed;

            public ChangeCheckScope() => BeginChangeCheck();

            public bool changed
            {
                get
                {
                    if (!m_ChangeChecked)
                    {
                        m_ChangeChecked = true;
                        m_Changed = EndChangeCheck();
                    }
                    return m_Changed;
                }
            }

            protected override void CloseScope()
            {
                if (!m_ChangeChecked)
                {
                    m_ChangeChecked = true;
                    m_Changed = EndChangeCheck();
                }
            }
        }

        public partial class IndentLevelScope : GUI.Scope
        {
            readonly int m_IndentOffset;

            public IndentLevelScope() : this(1) { }

            public IndentLevelScope(int increment)
            {
                m_IndentOffset = increment;
                indentLevel += increment;
            }

            protected override void CloseScope() => indentLevel -= m_IndentOffset;
        }

        public partial struct MixedValueScope : IDisposable
        {
            bool m_Disposed;
            readonly bool m_Previous;

            public MixedValueScope(bool newShowMixedValue)
            {
                m_Disposed = false;
                m_Previous = showMixedValue;
                showMixedValue = newShowMixedValue;
            }

            void IDisposable.Dispose()
            {
                if (m_Disposed) return;
                m_Disposed = true;
                showMixedValue = m_Previous;
            }
        }

        public partial class PropertyScope : GUI.Scope
        {
            public GUIContent content { get; protected set; }

            public PropertyScope(Rect totalPosition, GUIContent label, SerializedProperty property) =>
                content = BeginProperty(totalPosition, label, property);

            protected override void CloseScope() => EndProperty();
        }

        // ---------------------------------------------------------------- property heights

        public static float GetPropertyHeight(SerializedProperty property) => GetPropertyHeight(property, null, true);

        public static float GetPropertyHeight(SerializedProperty property, bool includeChildren) => GetPropertyHeight(property, null, includeChildren);

        public static float GetPropertyHeight(SerializedProperty property, GUIContent label) => GetPropertyHeight(property, label, true);

        /// <summary>A property's height as the inspector lays it out: its decorators', then its drawer's,
        /// else its own line(s) and, expanded, its children's.</summary>
        public static float GetPropertyHeight(SerializedProperty property, GUIContent label, bool includeChildren) =>
            PropertyHeights.Of(property, label, includeChildren);

        /// <summary>The height a property of a type takes with a label: vectors and rects take one
        /// more line for a label when the view is not wide.</summary>
        public static float GetPropertyHeight(SerializedPropertyType type, GUIContent label) => PropertyHeights.OfType(type, label);

        // ---------------------------------------------------------------- drawing

        /// <summary>Editor controls draw only in an editor window's OnGUI; outside one the Editor's
        /// throw (its GUI state is not there).</summary>
        internal static void RequireGUI()
        {
            if (Event.current == null) throw new NullReferenceException("Object reference not set to an instance of an object");
        }

        public static int IntField(Rect position, int value) { RequireGUI(); return EditorGUIControls.IntField(position, GUIContent.none, value); }
        public static int IntField(Rect position, string label, int value) { RequireGUI(); return EditorGUIControls.IntField(position, new GUIContent(label), value); }
        public static int IntField(Rect position, GUIContent label, int value) { RequireGUI(); return EditorGUIControls.IntField(position, label, value); }
        public static string TextField(Rect position, string text) { RequireGUI(); return EditorGUIControls.TextField(position, GUIContent.none, text); }
        public static string TextField(Rect position, string label, string text) { RequireGUI(); return EditorGUIControls.TextField(position, new GUIContent(label), text); }
        public static string TextField(Rect position, GUIContent label, string text) { RequireGUI(); return EditorGUIControls.TextField(position, label, text); }
        public static bool Toggle(Rect position, bool value) { RequireGUI(); return EditorGUIControls.Toggle(position, GUIContent.none, value); }
        public static bool Toggle(Rect position, string label, bool value) { RequireGUI(); return EditorGUIControls.Toggle(position, new GUIContent(label), value); }
        public static bool Toggle(Rect position, GUIContent label, bool value) { RequireGUI(); return EditorGUIControls.Toggle(position, label, value); }
        public static bool ToggleLeft(Rect position, string label, bool value) =>
            ToggleLeft(position, GUIContent.Temp(label), value, null);

        public static bool ToggleLeft(Rect position, string label, bool value, GUIStyle labelStyle) =>
            ToggleLeft(position, GUIContent.Temp(label), value, labelStyle);

        public static bool ToggleLeft(Rect position, GUIContent label, bool value) =>
            ToggleLeft(position, label, value, null);

        public static bool ToggleLeft(Rect position, GUIContent label, bool value, GUIStyle labelStyle)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUI.ToggleLeft");
                return default;
            }
            RequireGUI();
            return EditorGUIControls.LeftToggle(position, label, value, labelStyle ?? EditorStyles.label);
        }

        public static bool Foldout(Rect position, bool foldout, string content) { RequireGUI(); return EditorGUIControls.Foldout(position, foldout, new GUIContent(content)); }
        public static bool Foldout(Rect position, bool foldout, GUIContent content) { RequireGUI(); return EditorGUIControls.Foldout(position, foldout, content); }
        public static bool PropertyField(Rect position, SerializedProperty property) => PropertyField(position, property, null, false);
        public static bool PropertyField(Rect position, SerializedProperty property, bool includeChildren) => PropertyField(position, property, null, includeChildren);
        public static bool PropertyField(Rect position, SerializedProperty property, GUIContent label) => PropertyField(position, property, label, false);

        /// <summary>A property's decorators take the top of its rect (unless a drawer of the property
        /// or a PropertyField that shows them itself is drawing it); below them its own drawer draws
        /// it when it has one (the game's), else the Editor's control for its type.</summary>
        public static bool PropertyField(Rect position, SerializedProperty property, GUIContent label, bool includeChildren)
        {
            RequireGUI();
            var drawers = DrawerDispatch.For(property);
            int level = DrawerNesting.LevelFor(property, out bool decorated);
            if (decorated && drawers.Decorators != null)
            {
                float bottom = position.yMax;
                foreach (var decorator in drawers.Decorators)
                {
                    position.height = PropertyHeights.OfDecorator(decorator);
                    // The engine's decorators (a space, a header) have no IMGUI of their own here.
                    if (DrawerNesting.IsGameDrawer(decorator))
                    {
                        float labelWidth = EditorGUIUtility.labelWidth, fieldWidth = EditorGUIUtility.fieldWidth;
                        decorator.OnGUI(position);
                        EditorGUIUtility.labelWidth = labelWidth;
                        EditorGUIUtility.fieldWidth = fieldWidth;
                    }
                    position.y += position.height;
                }
                position.height = bottom - position.y;
            }
            var drawer = drawers.DrawerAt(level);
            if (!DrawerNesting.IsGameDrawer(drawer)) return EditorGUIControls.Property(position, property, label);
            float oldLabelWidth = EditorGUIUtility.labelWidth, oldFieldWidth = EditorGUIUtility.fieldWidth;
            using (DrawerNesting.Enter(property, level))
                drawer.OnGUI(position, property.Copy(), label ?? new GUIContent(property.displayName));
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUIUtility.fieldWidth = oldFieldWidth;
            return false;
        }
        public static Rect PrefixLabel(Rect totalPosition, GUIContent label)
        {
            if (Compat.InspectorGuiStylesMeasured) return PrefixLabel(totalPosition, 0, label);
            RequireGUI();
            return EditorGUIControls.PrefixLabel(totalPosition, label);
        }

        public static Rect PrefixLabel(Rect totalPosition, GUIContent label, GUIStyle style) => PrefixLabel(totalPosition, 0, label, style);

        internal static GUIStyle DefaultPrefixLabelStyle =>
            Compat.InspectorGuiStylesMeasured && Event.current != null ? EditorStyles.label : null;

        public static Rect PrefixLabel(Rect totalPosition, int id, GUIContent label) =>
            PrefixLabel(totalPosition, id, label, DefaultPrefixLabelStyle);

        public static Rect PrefixLabel(Rect totalPosition, int id, GUIContent label, GUIStyle style)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUI.PrefixLabel");
                return default;
            }
            RequireGUI();
            return EditorGUIControls.PrefixLabel(totalPosition, id, label, style);
        }

        public static void HandlePrefixLabel(Rect totalPosition, Rect labelPosition, GUIContent label) =>
            HandlePrefixLabel(totalPosition, labelPosition, label, 0, DefaultPrefixLabelStyle);

        public static void HandlePrefixLabel(Rect totalPosition, Rect labelPosition, GUIContent label, int id) =>
            HandlePrefixLabel(totalPosition, labelPosition, label, id, DefaultPrefixLabelStyle);

        public static void HandlePrefixLabel(Rect totalPosition, Rect labelPosition, GUIContent label, int id, GUIStyle style)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUI.HandlePrefixLabel");
                return;
            }
            RequireGUI();
            EditorGUIControls.HandlePrefixLabel(labelPosition, label, id, style);
        }

        public static void DrawRect(Rect rect, Color color)
        {
            RequireGUI();
            if (Event.current.type != EventType.Repaint) return;
            GuiRender.FillRect(rect, GUI.color * color);
        }

        public static GUIContent BeginProperty(Rect totalPosition, GUIContent label, SerializedProperty property)
        {
            RequireGUI();
            if (!Compat.EditorPropertyScopeMeasured)
            {
                s_PropertyStates.Push(default);
                return label;
            }
            if (property == null)
                throw new NullReferenceException((label == null ? "" : label.text + ": ") + "SerializedProperty is null");

            string tooltip = label?.tooltip;
            s_PropertyContent.text = label == null ? property.displayName : label.text;
            s_PropertyContent.tooltip = string.IsNullOrEmpty(tooltip) ? property.tooltip : tooltip;
            s_PropertyContent.image = label?.image;

            var state = (Enabled: GUI.enabled, Background: GUI.backgroundColor);
            bool previousMixed = showMixedValue;
            int previousDepth = s_PropertyStates.Count;
            s_PropertyStates.Push(state);
            try
            {
                EditorGUIUtility.NotifyBeginProperty(totalPosition, property);
                showMixedValue = property.hasMultipleDifferentValues;
                GUI.enabled = GUI.enabled && property.editable;
                return s_PropertyContent;
            }
            catch
            {
                while (s_PropertyStates.Count > previousDepth) s_PropertyStates.Pop();
                showMixedValue = previousMixed;
                GUI.enabled = state.Enabled;
                GUI.backgroundColor = state.Background;
                throw;
            }
        }

        public static void EndProperty()
        {
            if (!s_PropertyStates.TryPop(out var state) || !Compat.EditorPropertyScopeMeasured) return;
            showMixedValue = false;
            GUI.enabled = state.Enabled;
            GUI.backgroundColor = state.Background;
        }

        static readonly Stack<(bool Enabled, Color Background)> s_PropertyStates = new Stack<(bool, Color)>();
        static readonly GUIContent s_PropertyContent = new GUIContent();
    }

    /// <summary>The auto-layout forms of the editor controls (see <see cref="EditorGUI"/>).</summary>
    public sealed partial class EditorGUILayout
    {
        static readonly GUILayoutOption[] s_PrefixLayoutOptions = { GUILayout.ExpandWidth(false) };

        static Rect Line(GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            return GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight, options);
        }

        public static int IntField(int value, params GUILayoutOption[] options) => EditorGUIControls.IntField(Line(options), GUIContent.none, value);
        public static int IntField(string label, int value, params GUILayoutOption[] options) => EditorGUIControls.IntField(Line(options), new GUIContent(label), value);
        public static int IntField(GUIContent label, int value, params GUILayoutOption[] options) => EditorGUIControls.IntField(Line(options), label, value);
        public static float FloatField(float value, params GUILayoutOption[] options) => EditorGUIControls.FloatField(Line(options), GUIContent.none, value);
        public static float FloatField(string label, float value, params GUILayoutOption[] options) => EditorGUIControls.FloatField(Line(options), new GUIContent(label), value);
        public static float FloatField(GUIContent label, float value, params GUILayoutOption[] options) => EditorGUIControls.FloatField(Line(options), label, value);
        public static string TextField(string text, params GUILayoutOption[] options) => EditorGUIControls.TextField(Line(options), GUIContent.none, text);
        public static string TextField(string label, string text, params GUILayoutOption[] options) => EditorGUIControls.TextField(Line(options), new GUIContent(label), text);
        public static string TextField(GUIContent label, string text, params GUILayoutOption[] options) => EditorGUIControls.TextField(Line(options), label, text);
        public static bool Toggle(bool value, params GUILayoutOption[] options) => EditorGUIControls.Toggle(Line(options), GUIContent.none, value);
        public static bool Toggle(string label, bool value, params GUILayoutOption[] options) => EditorGUIControls.Toggle(Line(options), new GUIContent(label), value);
        public static bool Toggle(GUIContent label, bool value, params GUILayoutOption[] options) => EditorGUIControls.Toggle(Line(options), label, value);
        public static int Popup(int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) => EditorGUIControls.Popup(Line(options), GUIContent.none, selectedIndex, displayedOptions);
        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) => EditorGUIControls.Popup(Line(options), new GUIContent(label), selectedIndex, displayedOptions);
        public static Enum EnumPopup(Enum selected, params GUILayoutOption[] options) => EditorGUIControls.EnumPopup(Line(options), GUIContent.none, selected);
        public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) => EditorGUIControls.EnumPopup(Line(options), new GUIContent(label), selected);
        public static Enum EnumPopup(GUIContent label, Enum selected, params GUILayoutOption[] options) => EditorGUIControls.EnumPopup(Line(options), label, selected);
        public static bool Foldout(bool foldout, string content) => EditorGUIControls.Foldout(Line(null), foldout, new GUIContent(content));
        public static bool Foldout(bool foldout, GUIContent content) => EditorGUIControls.Foldout(Line(null), foldout, content);
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) => EditorGUIControls.Foldout(Line(null), foldout, new GUIContent(content));
        public static bool Foldout(bool foldout, GUIContent content, bool toggleOnLabelClick) => EditorGUIControls.Foldout(Line(null), foldout, content);
        public static bool PropertyField(SerializedProperty property, params GUILayoutOption[] options) => PropertyField(property, null, false, options);
        public static bool PropertyField(SerializedProperty property, GUIContent label, params GUILayoutOption[] options) => PropertyField(property, label, false, options);
        public static bool PropertyField(SerializedProperty property, bool includeChildren, params GUILayoutOption[] options) => PropertyField(property, null, includeChildren, options);

        /// <summary>A property takes a control rect of its height (its drawer's, when it has one)
        /// and is drawn in it. A checkbox without drawers keeps a plain line: the Editor's toggle
        /// rect is not measured.</summary>
        public static bool PropertyField(SerializedProperty property, GUIContent label, bool includeChildren, params GUILayoutOption[] options)
        {
            Rect rect;
            if (property.propertyType == SerializedPropertyType.Boolean && DrawerDispatch.For(property) is { Decorators: null } drawers && drawers.DrawerAt(0) == null)
                rect = Line(options);
            else
                rect = GetControlRect(label == null || label.text != string.Empty || label.image != null, EditorGUI.GetPropertyHeight(property, label, includeChildren), options);
            return EditorGUI.PropertyField(rect, property, label, includeChildren);
        }
        public static Object ObjectField(Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) => EditorGUIControls.ObjectField(Line(options), GUIContent.none, obj, objType);
        public static Object ObjectField(string label, Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) => EditorGUIControls.ObjectField(Line(options), new GUIContent(label), obj, objType);
        public static Object ObjectField(GUIContent label, Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) => EditorGUIControls.ObjectField(Line(options), label, obj, objType);
        public static Rect GetControlRect(params GUILayoutOption[] options) => GetControlRect(true, options);
        public static Rect GetControlRect(bool hasLabel, params GUILayoutOption[] options) =>
            GetControlRect(hasLabel, EditorGUIUtility.singleLineHeight, options);

        public static Rect GetControlRect(bool hasLabel, float height, params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            return Compat.EditorControlRectMeasured
                ? ReserveControlRect(hasLabel, height, Compat.EditorControlRectStyle, options)
                : GUILayoutUtility.GetRect(0, height, options);
        }

        public static Rect GetControlRect(bool hasLabel, float height, GUIStyle style, params GUILayoutOption[] options)
        {
            if (!Compat.EditorControlRectMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.GetControlRect");
                return default;
            }
            EditorGUI.RequireGUI();
            if (style == null && Event.current.type == EventType.Layout) throw new NullReferenceException();
            return ReserveControlRect(hasLabel, height, style, options);
        }

        static Rect ReserveControlRect(bool hasLabel, float height, GUIStyle style, GUILayoutOption[] options)
        {
            float field = EditorGUIUtility.fieldWidth;
            float preferred = EditorGUIUtility.labelWidth + field + Compat.EditorControlLabelGap;
            return GUILayoutUtility.GetRect(hasLabel ? preferred : field, preferred, height, height, style, options);
        }

        public static void HelpBox(string message, MessageType type)
        {
            EditorGUI.RequireGUI();
            GUILayout.Label(message);
        }

        public static void HelpBox(string message, MessageType type, bool wide) => HelpBox(message, type);

        static readonly GUILayoutOption[] s_ExpandWidthTrue = { GUILayout.ExpandWidth(true) };
        static readonly GUILayoutOption[] s_ExpandWidthFalse = { GUILayout.ExpandWidth(false) };

        public static void Separator() => Space();

        public static void Space() => Space(6f, true);

        public static void Space(float width) => Space(width, true);

        public static void Space(float width, bool expand)
        {
            EditorGUI.RequireGUI();
            GUILayoutUtility.GetRect(width, width, expand ? s_ExpandWidthTrue : s_ExpandWidthFalse);
        }

        public static Rect BeginHorizontal(params GUILayoutOption[] options) => BeginHorizontal(GUIStyle.none, options);
        public static Rect BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) => BeginHorizontal(GUIContent.none, style, options);
        internal static Rect BeginHorizontal(GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
            BeginLayoutGroup(content, style, options, vertical: false);

        public static void EndHorizontal() => GUILayout.EndHorizontal();

        public static Rect BeginVertical(params GUILayoutOption[] options) => BeginVertical(GUIStyle.none, options);
        public static Rect BeginVertical(GUIStyle style, params GUILayoutOption[] options) => BeginVertical(GUIContent.none, style, options);
        internal static Rect BeginVertical(GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
            BeginLayoutGroup(content, style, options, vertical: true);

        static Rect BeginLayoutGroup(GUIContent content, GUIStyle style, GUILayoutOption[] options, bool vertical)
        {
            EditorGUI.RequireGUI();
            return GUILayout.BeginLayoutGroup(content, style, options, vertical, drawContent: false);
        }

        public static void EndVertical() => GUILayout.EndVertical();

        public static void PrefixLabel(string label) => PrefixLabel(GUIContent.Temp(label));

        public static void PrefixLabel(string label, GUIStyle followingStyle) =>
            PrefixLabel(GUIContent.Temp(label), followingStyle);

        public static void PrefixLabel(string label, GUIStyle followingStyle, GUIStyle labelStyle) =>
            PrefixLabel(GUIContent.Temp(label), followingStyle, labelStyle);

        public static void PrefixLabel(GUIContent label) =>
            PrefixLabel(label, Compat.InspectorGuiStylesMeasured && Event.current != null ? (GUIStyle)"Button" : null);

        public static void PrefixLabel(GUIContent label, GUIStyle followingStyle) =>
            PrefixLabel(label, followingStyle, EditorGUI.DefaultPrefixLabelStyle);

        public static void PrefixLabel(GUIContent label, GUIStyle followingStyle, GUIStyle labelStyle)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.PrefixLabel");
                return;
            }
            EditorGUI.RequireGUI();
            bool hasLabel = EditorGUIControls.HasLabelContent(label);
            float prefixWidth = (hasLabel ? EditorGUIUtility.labelWidth : EditorGUI.indent) - followingStyle.margin.left;
            var labelPosition = GUILayoutUtility.GetRect(prefixWidth, EditorGUIUtility.singleLineHeight, followingStyle, s_PrefixLayoutOptions);
            if (!hasLabel) return;
            labelPosition.xMin += EditorGUI.indent;
            EditorGUIControls.HandlePrefixLabel(labelPosition, label, 0, labelStyle);
        }

        public static bool ToggleLeft(string label, bool value, params GUILayoutOption[] options) =>
            ToggleLeft(GUIContent.Temp(label), value, null, options);

        public static bool ToggleLeft(GUIContent label, bool value, params GUILayoutOption[] options) =>
            ToggleLeft(label, value, null, options);

        public static bool ToggleLeft(string label, bool value, GUIStyle labelStyle, params GUILayoutOption[] options) =>
            ToggleLeft(GUIContent.Temp(label), value, labelStyle, options);

        public static bool ToggleLeft(GUIContent label, bool value, GUIStyle labelStyle, params GUILayoutOption[] options)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.ToggleLeft");
                return default;
            }
            return EditorGUI.ToggleLeft(GetControlRect(options), label, value, labelStyle);
        }

        public static bool BeginToggleGroup(string label, bool toggle) => BeginToggleGroup(GUIContent.Temp(label), toggle);

        public static bool BeginToggleGroup(GUIContent label, bool toggle)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.BeginToggleGroup");
                return default;
            }

            var labelStyle = EditorStyles.boldLabel;
            var rect = GetControlRect();
            toggle = EditorGUIControls.LeftToggle(rect, label, toggle, labelStyle);
            EditorGUI.BeginDisabledGroup(!toggle);
            GUILayout.BeginVertical();
            return toggle;
        }

        public static void EndToggleGroup()
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.EndToggleGroup");
                return;
            }

            GUILayout.EndVertical();
            EditorGUI.EndDisabledGroup();
        }

        public partial class ToggleGroupScope : GUI.Scope
        {
            public bool enabled { get; protected set; }

            public ToggleGroupScope(string label, bool toggle) => enabled = BeginToggleGroup(label, toggle);
            public ToggleGroupScope(GUIContent label, bool toggle) => enabled = BeginToggleGroup(label, toggle);

            protected override void CloseScope() => EndToggleGroup();
        }

        public partial class HorizontalScope : GUI.Scope
        {
            public Rect rect { get; protected set; }
            public HorizontalScope(params GUILayoutOption[] options) => rect = BeginHorizontal(options);
            public HorizontalScope(GUIStyle style, params GUILayoutOption[] options) => rect = BeginHorizontal(style, options);
            internal HorizontalScope(GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
                rect = BeginHorizontal(content, style, options);
            protected override void CloseScope() => EndHorizontal();
        }

        public partial class VerticalScope : GUI.Scope
        {
            public Rect rect { get; protected set; }
            public VerticalScope(params GUILayoutOption[] options) => rect = BeginVertical(options);
            public VerticalScope(GUIStyle style, params GUILayoutOption[] options) => rect = BeginVertical(style, options);
            internal VerticalScope(GUIContent content, GUIStyle style, params GUILayoutOption[] options) =>
                rect = BeginVertical(content, style, options);
            protected override void CloseScope() => EndVertical();
        }

        /// <summary>A slider: an IMGUI control, outside OnGUI refused as GUI's are.</summary>
        public static float Slider(float value, float leftValue, float rightValue, params GUILayoutOption[] options)
        {
            if (Event.current == null) throw new ArgumentException("You can only call GUI functions from inside OnGUI.");
            return GUILayout.HorizontalSlider(value, leftValue, rightValue, options);
        }

        public static float Slider(string label, float value, float leftValue, float rightValue, params GUILayoutOption[] options) => Slider(value, leftValue, rightValue, options);

        public static float Slider(GUIContent label, float value, float leftValue, float rightValue, params GUILayoutOption[] options) => Slider(value, leftValue, rightValue, options);
    }

    /// <summary>What the editor controls do inside an OnGUI (a game's, under UNITY_EDITOR): IMGUI's
    /// own controls with the editor's label column.</summary>
    internal static class EditorGUIControls
    {
        // Space between a prefix label and its field in tests/projects/editortogglegroup.
        const float PrefixLabelGap = 2f;
        static readonly int s_ToggleHash = "s_ToggleHash".GetHashCode();
        static GUIStyle s_MixedLeftToggle;
        static GUIContent s_PendingPrefixContent;
        static GUIStyle s_PendingPrefixStyle;
        static Rect s_PendingPrefixRect;
        static Color s_PendingPrefixColor;
        static bool s_HasPendingPrefix;

        static Rect Field(Rect position, GUIContent label)
        {
            if (!HasLabelContent(label)) return EditorGUI.IndentedRect(position);
            return PrefixLabel(position, label);
        }

        internal static bool HasLabelContent(GUIContent label) =>
            label == null || label.text != string.Empty || label.image != null;

        internal static Rect PrefixLabel(Rect position, GUIContent label)
        {
            var indented = EditorGUI.IndentedRect(position);
            float width = EditorGUIUtility.labelWidth - EditorGUI.indent;
            GUI.Label(new Rect(indented.x, indented.y, width, indented.height), label);
            return new Rect(indented.x + width, indented.y, indented.width - width, indented.height);
        }

        internal static Rect PrefixLabel(Rect position, int id, GUIContent label, GUIStyle style)
        {
            var indented = EditorGUI.IndentedRect(position);
            if (!HasLabelContent(label)) return indented;

            float labelWidth = EditorGUIUtility.labelWidth;
            var labelPosition = new Rect(indented.x, position.y, labelWidth - EditorGUI.indent, EditorGUIUtility.singleLineHeight);
            HandlePrefixLabel(labelPosition, label, id, style);
            float fieldOffset = labelWidth + PrefixLabelGap;
            return new Rect(position.x + fieldOffset, position.y, position.width - fieldOffset, position.height);
        }

        internal static void HandlePrefixLabel(Rect labelPosition, GUIContent label, int id, GUIStyle style)
        {
            if (id != 0 || label == null)
            {
                DrawPrefixLabel(labelPosition, label, id, style);
                return;
            }
            s_PendingPrefixContent ??= new GUIContent();
            s_PendingPrefixContent.text = label.text;
            s_PendingPrefixContent.image = label.image;
            s_PendingPrefixContent.tooltip = label.tooltip;
            s_PendingPrefixStyle = style;
            s_PendingPrefixRect = labelPosition;
            s_PendingPrefixColor = GUI.color;
            s_HasPendingPrefix = true;
        }

        internal static void CompletePendingPrefixLabel(int id)
        {
            if (!s_HasPendingPrefix || Event.current == null) return;
            var style = s_PendingPrefixStyle;
            s_HasPendingPrefix = false;
            s_PendingPrefixStyle = null;
            var label = s_PendingPrefixContent;
            var previous = GUI.color;
            try
            {
                if (!string.IsNullOrEmpty(label.text))
                {
                    GUI.color = s_PendingPrefixColor;
                    DrawPrefixLabel(s_PendingPrefixRect, label, id, style);
                }
            }
            finally
            {
                GUI.color = previous;
                label.text = null;
                label.image = null;
                label.tooltip = null;
            }
        }

        internal static void Label(Rect position, GUIContent label) => GUI.Label(EditorGUI.IndentedRect(position), label);

        internal static int IntField(Rect position, GUIContent label, int value)
        {
            var text = GUI.TextField(Field(position, label), value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : value;
        }

        internal static float FloatField(Rect position, GUIContent label, float value)
        {
            var text = GUI.TextField(Field(position, label), value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : value;
        }

        internal static string TextField(Rect position, GUIContent label, string text) => GUI.TextField(Field(position, label), text ?? "");

        internal static bool Toggle(Rect position, GUIContent label, bool value) => GUI.Toggle(Field(position, label), value, GUIContent.none);

        internal static bool LeftToggle(Rect position, GUIContent label, bool value, GUIStyle labelStyle)
        {
            var current = Event.current;
            var type = current.type;
            int id = GUIUtility.GetControlID(s_ToggleHash, FocusType.Keyboard, position);
            var toggleStyle = EditorStyles.toggle;
            var togglePosition = EditorGUI.IndentedRect(position);
            var labelPosition = togglePosition;
            labelPosition.xMin += toggleStyle.padding.left;
            labelPosition.y -= (toggleStyle.margin.top - toggleStyle.margin.bottom) / 2;

            if (label != null) DrawPrefixLabel(labelPosition, label, id, labelStyle);

            if (EditorGUI.showMixedValue)
            {
                value = false;
                toggleStyle = s_MixedLeftToggle ??= new GUIStyle { name = InspectorTogglePaint.MixedStyleName };
            }
            if (type == EventType.MouseDown && current.button != 0) return value;
            bool selected = GUI.DoToggle(togglePosition, id, value, GUIContent.none, toggleStyle);
            if (current.type != type) GUIUtility.keyboardControl = id;
            return selected;
        }

        static void DrawPrefixLabel(Rect position, GUIContent label, int id, GUIStyle style)
        {
            var current = Event.current;
            if (current.type == EventType.Repaint)
            {
                position.width = Mathf.Max(1f, position.width);
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = Color.white;
                try { style.DrawPrefixLabel(position, label, id); }
                finally { GUI.backgroundColor = previous; }
            }
            else if (current.type == EventType.MouseDown && current.button == 0 && GUI.enabled &&
                position.Contains(current.mousePosition))
            {
                if (GUIUtility.CanHaveKeyboardFocus(id)) GUIUtility.keyboardControl = id;
                EditorGUIUtility.editingTextField = false;
            }
        }

        internal static bool Foldout(Rect position, bool foldout, GUIContent content) => GUI.Toggle(EditorGUI.IndentedRect(position), foldout, content);

        internal static int Popup(Rect position, GUIContent label, int selected, string[] options)
        {
            var field = Field(position, label);
            GUI.Label(field, selected >= 0 && options != null && selected < options.Length ? options[selected] : "");
            return selected;
        }

        internal static Enum EnumPopup(Rect position, GUIContent label, Enum selected)
        {
            GUI.Label(Field(position, label), selected?.ToString() ?? "");
            return selected;
        }

        internal static Object ObjectField(Rect position, GUIContent label, Object obj, Type type)
        {
            GUI.Label(Field(position, label), EditorGUIUtility.ObjectContent(obj, type));
            return obj;
        }

        internal static bool Property(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            try
            {
                label ??= new GUIContent(property.displayName);
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer: property.intValue = IntField(position, label, property.intValue); break;
                    case SerializedPropertyType.Float: property.floatValue = FloatField(position, label, property.floatValue); break;
                    case SerializedPropertyType.Boolean: property.boolValue = Toggle(position, label, property.boolValue); break;
                    case SerializedPropertyType.String: property.stringValue = TextField(position, label, property.stringValue); break;
                    default:
                        if (property.hasVisibleChildren) property.isExpanded = Foldout(position, property.isExpanded, label);
                        else Label(position, label);
                        break;
                }
                return property.isExpanded;
            }
            finally { EditorGUI.EndProperty(); }
        }
    }

    /// <summary>Property heights as the inspector computes them (measured in tests/projects/editorui).</summary>
    internal static class PropertyHeights
    {
        const float Line = 18f;
        const float Spacing = 2f;

        static bool HasContent(GUIContent label) => label == null || !string.IsNullOrEmpty(label.text) || label.image != null;

        internal static float OfType(SerializedPropertyType type, GUIContent label)
        {
            float labelLine = HasContent(label) && !EditorGUIUtility.wideMode ? Line + Spacing : 0f;
            switch (type)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Quaternion:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                    return labelLine + Line;
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                    return labelLine + Line * 2 + Spacing;
                default:
                    return Line;
            }
        }

        // A property's drawers are the ones PropertyField finds (DrawerDispatch: the declaring field,
        // a collection's attributes on its elements, the built-in attributes); the engine's own
        // drawers and decorators are measured here, a game's by its GetHeight/GetPropertyHeight.
        const float TextAreaLine = 15f;

        // The label's line, the first text line and 15 points for each further one: the lines are
        // the height the text has in the Editor's text area style at the context's width, in
        // steps of 15 points, within the attribute's limits (the style's padding makes four
        // lines of text count five: tests/projects/imguicontainer builtin.heights longNote).
        // The style is the Editor's: there is none before a GUI pass.
        static float OfTextArea(SerializedProperty property, TextAreaAttribute area)
        {
            if (!EditorStyles.Available) EditorStyles.Require();
            string text = property.propertyType == SerializedPropertyType.String ? property.stringValue : "";
            float textHeight = EditorStyles.TextAreaStyle.CalcHeight(GUIContent.Temp(text ?? ""), EditorGUIUtility.contextWidth);
            int lines = Mathf.Clamp(Mathf.CeilToInt(textHeight / TextAreaLine), area.minLines, area.maxLines);
            return Line + Line + (lines - 1) * TextAreaLine;
        }

        internal static float OfDecorator(DecoratorDrawer decorator)
        {
            switch (decorator)
            {
                case SpaceDrawer _:
                    return ((SpaceAttribute)decorator.attribute).height;
                case HeaderDrawer _:
                    // A line and a half, and the bold label's line height for each further line of
                    // the header. The style is the Editor's: there is none before a GUI pass.
                    if (!EditorStyles.Available) EditorStyles.Require();
                    string header = ((HeaderAttribute)decorator.attribute).header;
                    int lines = 1;
                    if (header != null)
                        foreach (char c in header)
                            if (c == '\n') lines++;
                    return lines == 1 ? Line * 1.5f : Line * 1.5f + EditorStyles.boldLabel.CalcHeight(new GUIContent(header), 1f) / lines * (lines - 1);
                default:
                    return decorator.GetHeight();
            }
        }

        internal static float Of(SerializedProperty property, GUIContent label, bool includeChildren)
        {
            var drawers = DrawerDispatch.For(property);
            float height = 0f;
            // A drawer measuring its own property again gets the next drawer, without the
            // decorators; so does a PropertyField that shows the decorators itself.
            int level = DrawerNesting.LevelFor(property, out bool decorated);
            if (drawers.Decorators != null && decorated)
            {
                foreach (var decorator in drawers.Decorators) height += OfDecorator(decorator);
            }
            var drawer = drawers.DrawerAt(level);
            if (DrawerNesting.IsGameDrawer(drawer))
            {
                using (DrawerNesting.Enter(property, level))
                    return height + drawer.GetPropertyHeight(property.Copy(), label ?? new GUIContent(property.displayName));
            }
            switch (drawer?.attribute)
            {
                case MultilineAttribute multiline:
                    return height + (!EditorGUIUtility.wideMode ? Line : 0f) + Line + (multiline.lines - 1) * 13f;
                case TextAreaAttribute area:
                    return height + OfTextArea(property, area);
            }
            return height + OfProperty(property, label, includeChildren);
        }

        static float OfProperty(SerializedProperty property, GUIContent label, bool includeChildren)
        {
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                if (!includeChildren || !property.isExpanded) return Line;
                // A reorderable list: header, elements (or the empty line), footer.
                int count = property.arraySize;
                if (count == 0) return 48f + 21f;
                float h = 48f;
                for (int i = 0; i < count; i++) h += EditorGUI.GetPropertyHeight(property.GetArrayElementAtIndex(i)) + Spacing;
                return h;
            }
            float single = OfType(property.propertyType, label);
            if (!includeChildren || !property.isExpanded || property.propertyType != SerializedPropertyType.Generic || !property.hasVisibleChildren) return single;
            float total = single;
            var child = property.Copy();
            var end = property.GetEndProperty();
            if (child.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(child, end)) break;
                    total += EditorGUI.GetPropertyHeight(child, null, true) + Spacing;
                }
                while (child.NextVisible(false));
            }
            return total;
        }
    }

    public abstract partial class GUIDrawer
    {
        protected GUIDrawer() { }
    }

    public abstract partial class PropertyDrawer : GUIDrawer
    {
        internal PropertyAttribute m_Attribute;
        internal FieldInfo m_FieldInfo;
        internal string m_PreferredLabel;

        protected PropertyDrawer() { }

        public PropertyAttribute attribute => m_Attribute;

        public FieldInfo fieldInfo => m_FieldInfo;

        public string preferredLabel => m_PreferredLabel;

        public virtual void OnGUI(Rect position, SerializedProperty property, GUIContent label) => EditorGUI.PropertyField(position, property, label);

        public virtual float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUI.GetPropertyHeight(property.propertyType, label);
    }

    public abstract partial class DecoratorDrawer : GUIDrawer
    {
        internal PropertyAttribute m_Attribute;

        protected DecoratorDrawer() { }

        public PropertyAttribute attribute => m_Attribute;

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual void OnGUI(Rect position) { }

        public virtual float GetHeight() => EditorGUIUtility.singleLineHeight;
    }

    [global::System.@AttributeUsageAttribute(global::System.AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed partial class CustomPropertyDrawer : Attribute
    {
        internal Type m_Type;
        internal bool m_UseForChildren;

        public CustomPropertyDrawer(Type type) => m_Type = type;

        public CustomPropertyDrawer(Type type, bool useForChildren)
        {
            m_Type = type;
            m_UseForChildren = useForChildren;
        }
    }

    public partial class SerializedProperty
    {
        /// <summary>The script field the property's node was read from (drawers look at its
        /// attributes and type).</summary>
        internal FieldInfo UnboundField => SerializedTree.FieldOf(N);

        /// <summary>The script field that declares the property; for an array or list element, the
        /// collection's field.</summary>
        internal FieldInfo UnboundDeclaringField => SerializedTree.DeclaringFieldOf(N);
    }

    /// <summary>The Editor's control styles. Supported Inspector styles are available during
    /// Game view OnGUI. The other window styles retain the batch-mode exception measured in
    /// tests/projects/editorui.</summary>
    public sealed partial class EditorStyles
    {
        static GUIStyle s_LightLabel;
        static GUIStyle s_DarkLabel;
        static GUIStyle s_LightBoldLabel;
        static GUIStyle s_DarkBoldLabel;
        static GUIStyle s_LightToggle;
        static GUIStyle s_DarkToggle;

        internal static bool HasInspectorStyles => Compat.InspectorGuiStylesMeasured && Event.current != null;

        /// <summary>An Editor GUI pass has run (an Editor-context IMGUIContainer's): from then on
        /// what the styles decide without their fonts is known outside a GUI pass too
        /// (tests/projects/imguicontainer builtin.outsideGUI).</summary>
        internal static bool Loaded;

        /// <summary>Whether the styles are there: inside a GUI pass, or after an Editor one ran.</summary>
        internal static bool Available => Compat.InspectorGuiStylesMeasured && (Event.current != null || Loaded);

        static GUIStyle s_InspectorHorizontalDefaultMargins;
        static GUIStyle s_InspectorDefaultMargins;

        /// <summary>The inspector's horizontal margins: 18 points left, 4 right.</summary>
        internal static GUIStyle inspectorHorizontalDefaultMargins => s_InspectorHorizontalDefaultMargins ??= new GUIStyle { padding = new RectOffset(18, 4, 0, 0) };

        internal static void Require() => throw new NullReferenceException("Object reference not set to an instance of an object");

        static GUIStyle Style() { Require(); return null; }

        public static GUIStyle label
        {
            get
            {
                if (!HasInspectorStyles) return Style();
                return EditorGUIUtility.isProSkin
                    ? s_DarkLabel ??= CreateInspectorLabel(dark: true, bold: false)
                    : s_LightLabel ??= CreateInspectorLabel(dark: false, bold: false);
            }
        }
        public static GUIStyle miniLabel => Style();
        public static GUIStyle largeLabel => Style();
        public static GUIStyle boldLabel
        {
            get
            {
                if (!HasInspectorStyles) return Style();
                return EditorGUIUtility.isProSkin
                    ? s_DarkBoldLabel ??= CreateInspectorLabel(dark: true, bold: true)
                    : s_LightBoldLabel ??= CreateInspectorLabel(dark: false, bold: true);
            }
        }
        public static GUIStyle miniBoldLabel => Style();
        public static GUIStyle centeredGreyMiniLabel => Style();
        public static GUIStyle wordWrappedMiniLabel => Style();
        public static GUIStyle wordWrappedLabel => Style();
        public static GUIStyle linkLabel => Style();
        public static GUIStyle whiteLabel => Style();
        public static GUIStyle textField => HasInspectorStyles ? TextFieldStyle : Style();
        public static GUIStyle textArea => HasInspectorStyles ? TextAreaStyle : Style();
        public static GUIStyle numberField => HasInspectorStyles ? TextFieldStyle : Style();

        static GUIStyle s_TextArea;
        static GUIStyle s_TextAreaSource;

        /// <summary>The text field style of the Editor's skin (number fields have the same one).</summary>
        internal static GUIStyle TextFieldStyle => Unbound.Runtime.EditorGuiSkins.Current.textField;

        /// <summary>The text field style with word wrap.</summary>
        internal static GUIStyle TextAreaStyle
        {
            get
            {
                var source = TextFieldStyle;
                if (s_TextArea == null || s_TextAreaSource != source)
                {
                    s_TextAreaSource = source;
                    s_TextArea = new GUIStyle(source) { wordWrap = true };
                }
                return s_TextArea;
            }
        }
        public static GUIStyle popup => Style();
        public static GUIStyle objectField => Style();
        public static GUIStyle colorField => Style();
        public static GUIStyle layerMaskField => Style();
        public static GUIStyle toggle
        {
            get
            {
                if (!HasInspectorStyles) return Style();
                return EditorGUIUtility.isProSkin
                    ? s_DarkToggle ??= CreateToggle(dark: true)
                    : s_LightToggle ??= CreateToggle(dark: false);
            }
        }
        public static GUIStyle radioButton => Style();
        public static GUIStyle foldout => Style();
        public static GUIStyle foldoutHeader => Style();
        public static GUIStyle helpBox => Style();
        public static GUIStyle toolbar => Style();
        public static GUIStyle toolbarButton => Style();
        public static GUIStyle toolbarDropDown => Style();
        public static GUIStyle toolbarSearchField => Style();
        public static GUIStyle miniButton => Style();
        public static GUIStyle miniButtonLeft => Style();
        public static GUIStyle selectionRect => Style();
        /// <summary>The inspector's margins: 18 points left, 4 right, 4 at the top.</summary>
        public static GUIStyle inspectorDefaultMargins =>
            HasInspectorStyles ? s_InspectorDefaultMargins ??= new GUIStyle { padding = new RectOffset(18, 4, 4, 0) } : Style();
        public static GUIStyle inspectorFullWidthMargins => Style();

        /// <summary>The Editor's UI font (its fonts load without a window).</summary>
        public static Font standardFont => EditorFonts.Standard;

        static GUIStyle CreateInspectorLabel(bool dark, bool bold)
        {
            // Resolved Inspector theme values from tests/projects/editorstyles. The style uses
            // the current GUI skin font unless the caller assigns a font explicitly.
            var text = dark ? new Color32(196, 196, 196, 255) : new Color32(9, 9, 9, 255);
            var focus = dark ? new Color32(129, 180, 255, 255) : new Color32(0, 60, 136, 255);
            var style = new GUIStyle
            {
                name = bold ? "BoldLabel" : "ControlLabel",
                fontSize = 12,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                imagePosition = ImagePosition.ImageLeft,
                padding = new RectOffset(1, 1, 0, 0),
                margin = new RectOffset(3, 3, 2, 2),
                richText = false,
            };
            style.normal.textColor = style.hover.textColor = style.onHover.textColor = text;
            style.focused.textColor = focus;
            style.onFocused.textColor = Color.white;
            if (bold)
            {
                style.active.textColor = style.onActive.textColor = text;
                style.onNormal.textColor = dark ? new Color32(210, 210, 210, 255) : new Color32(0, 0, 0, 255);
            }
            else
            {
                style.active.textColor = style.onActive.textColor =
                    dark ? new Color32(76, 126, 255, 255) : new Color32(0, 50, 230, 255);
                style.onNormal.textColor = text;
            }
            return style;
        }

        static GUIStyle CreateToggle(bool dark)
        {
            // The Inspector override keeps the scaled skin images. Its background property is
            // null in each state, as captured by tests/projects/editorstyles.
            Color text = dark ? new Color32(210, 210, 210, 255) : new Color32(2, 2, 2, 255);
            string skin = dark ? "darkskin" : "lightskin";
            var style = new GUIStyle
            {
                name = "toggle",
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                imagePosition = ImagePosition.ImageLeft,
                border = dark ? new RectOffset(16, 0, 15, 0) : new RectOffset(15, 0, 13, 0),
                overflow = dark ? new RectOffset(0, 0, -3, 1) : new RectOffset(0, 0, -2, 2),
                padding = new RectOffset(17, 0, 0, 0),
                margin = new RectOffset(3, 3, 2, 2),
                contentOffset = new Vector2(0, 1),
                richText = false,
            };
            SetToggleState(style.normal, skin, "", text);
            SetToggleState(style.active, skin, " act", text);
            SetToggleState(style.focused, skin, " focus", text);
            SetToggleState(style.onNormal, skin, " on", text);
            SetToggleState(style.onActive, skin, " on act", dark ? text : Color.black);
            SetToggleState(style.onFocused, skin, " on focus", text);
            style.hover.textColor = style.onHover.textColor = text;
            return style;
        }

        static void SetToggleState(GUIStyleState state, string skin, string suffix, Color text)
        {
            string path = $"builtin skins/{skin}/images/toggle{suffix}@2x.png";
            var texture = Unbound.UIElements.UiResources.EditorLoad(path) as Texture2D;
            if (texture == null) throw new InvalidOperationException($"Unbound: Inspector toggle image '{path}' is unavailable.");
            state.textColor = text;
            state.scaledBackgrounds = new[] { texture };
        }
    }

    internal static class EditorFonts
    {
        static Font s_Standard;

        internal static Font Standard => s_Standard ??= MakeFont("Inter-Regular");

        static Font MakeFont(string name)
        {
            // A dynamic font of the OS font of that name (new Font(name) is an empty custom font).
            var font = Font.CreateDynamicFontFromOSFont(name, 16);
            font.name = name;
            return font;
        }
    }

    public sealed partial class EditorGUIUtility : GUIUtility
    {
        internal static event Action<Rect, SerializedProperty> beginProperty;

        internal static void NotifyBeginProperty(Rect position, SerializedProperty property) => beginProperty?.Invoke(position, property);

        internal static bool HasCurrentWindowKeyFocus()
        {
            if (Compat.EditorKeyFocusRequiresGUI) CheckOnGUI(requireCurrentEvent: true);
            return Event.current != null && Application.isFocused;
        }

        internal static new bool CanHaveKeyboardFocus(int id) => GUIUtility.CanHaveKeyboardFocus(id);

        internal static new int GetSearchIndexOfControlIDList() => GUIUtility.GetSearchIndexOfControlIDList();

        internal static new void SetSearchIndexOfControlIDList(int index) => GUIUtility.SetSearchIndexOfControlIDList(index);

        [Unbound.EditorFaithful("measured: tests/projects/editorui EditorGUIUtility.singleLineHeight")]
        public static float singleLineHeight => 18f;

        [Unbound.EditorFaithful("measured: tests/projects/editorui EditorGUIUtility.standardVerticalSpacing")]
        public static float standardVerticalSpacing => 2f;

        public static bool wideMode { get; set; }

        public static bool editingTextField { get; set; }

        /// <summary>The width of the editor view being drawn: only an OnGUI has one.</summary>
        public static float currentViewWidth
        {
            get
            {
                if (Event.current == null) throw new ArgumentException("You can only call GUI functions from inside OnGUI.");
                return s_CurrentViewWidth > 0f ? s_CurrentViewWidth : contextWidth;
            }
            internal set => s_CurrentViewWidth = value;
        }

        static float s_CurrentViewWidth;

        public static GUIContent IconContent(string name) => IconContent(name, (string)null);

        /// <summary>A built-in editor icon (the Editor's icons by name, the pro skin's "d_" ones first;
        /// a name none has logs an error); <paramref name="text"/> is the tooltip.</summary>
        public static GUIContent IconContent(string name, string text)
        {
            var icon = EditorIcons.Load(name, logError: true);
            return new GUIContent { image = icon };
        }

        internal static GUIContent IconContent(string name, bool logError) => new GUIContent { image = EditorIcons.Load(name, logError) };

        public static Texture2D FindTexture(string name) => EditorIcons.Load(name, logError: false);

        public static Vector2 GetIconSize() => s_IconSize;

        public static void SetIconSize(Vector2 size) => s_IconSize = size;

        static Vector2 s_IconSize;

        public static Texture2D whiteTexture => Texture2D.whiteTexture;

        static readonly GUIContent s_ObjectContent = new GUIContent();

        /// <summary>An object's label and icon as object fields show them: "Name (Type)" for
        /// Components and ScriptableObjects, "Name" for other live objects, "None (Type)" or
        /// "Missing (Type)" for null/destroyed references, and "&lt;no type&gt;" when untyped.
        /// The tooltip is left as it is (empty). Matches the Unity 6000.0.71f1 Editor
        /// (tests/projects/objectbinding, tests/projects/editorui).</summary>
        public static GUIContent ObjectContent(Object obj, Type type) =>
            ObjectContent(obj, type, obj is null ? 0 : obj.GetInstanceID(), showNullIcon: true);

        internal static GUIContent ObjectContent(Object obj, Type type, bool showNullIcon) =>
            ObjectContent(obj, type, obj is null ? 0 : obj.GetInstanceID(), showNullIcon);

        internal static GUIContent ObjectContent(Object obj, Type type, int instanceID, bool showNullIcon = true)
        {
            if (obj != null)
            {
                var objType = obj.GetType();
                s_ObjectContent.text = (obj is Component || obj is ScriptableObject)
                    ? $"{obj.name} ({ObjectNames.NicifyVariableName(objType.Name)})"
                    : obj.name;
                s_ObjectContent.image = EditorIcons.Load(objType.Name + " Icon", logError: false) ?? EditorIcons.Placeholder;
            }
            else if (type != null)
            {
                var prefix = instanceID != 0 ? "Missing (" : "None (";
                s_ObjectContent.text = prefix + ObjectNames.NicifyVariableName(type.Name) + ")";
                s_ObjectContent.image = showNullIcon
                    ? (EditorIcons.Load(type.Name + " Icon", logError: false) ?? EditorIcons.Placeholder)
                    : null;
            }
            else
            {
                s_ObjectContent.text = "<no type>";
                s_ObjectContent.image = null;
            }
            return s_ObjectContent;
        }

        internal static GUIContent ObjectContentForProperty(Object obj, Type type, SerializedProperty property)
        {
            if (obj == null && type == null && property != null && property.isValid)
            {
                s_ObjectContent.text = property.objectReferenceStringValue;
                s_ObjectContent.image = null;
                return s_ObjectContent;
            }
            if (property != null && property.isValid)
                return ObjectContent(obj, type, property.objectReferenceInstanceIDValue, showNullIcon: false);
            return ObjectContent(obj, type, showNullIcon: false);
        }

        /// <summary>The system copy buffer: the same system-wide clipboard, and the same in-process
        /// copy, as <see cref="GUIUtility.systemCopyBuffer"/>.</summary>
        public static string systemCopyBuffer
        {
            get => GUIUtility.systemCopyBuffer;
            set => GUIUtility.systemCopyBuffer = value;
        }
    }

    /// <summary>The Editor's built-in icons, by the names its resources have
    /// (Editor/Data/editor-icons-&lt;family&gt;.txt, from `unity editor resources`); each a small
    /// placeholder texture, as Unbound draws no editor UI.</summary>
    internal static class EditorIcons
    {
        static HashSet<string> s_Names;
        static readonly Dictionary<string, Texture2D> s_Loaded = new Dictionary<string, Texture2D>();
        static Texture2D s_Placeholder;

        internal static Texture2D Placeholder => s_Placeholder ??= new Texture2D(1, 1) { name = "Unbound Icon" };

        static HashSet<string> Names
        {
            get
            {
                if (s_Names != null) return s_Names;
                var names = new HashSet<string>(StringComparer.Ordinal);
                using var stream = typeof(EditorIcons).Assembly.GetManifestResourceStream("Unbound.editor-icons-6000.0.txt");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.Length > 0) names.Add(line);
                }
                return s_Names = names;
            }
        }

        /// <summary>The icon of exactly this name (a skin's prefix included), or null.</summary>
        internal static Texture2D LoadNamed(string name)
        {
            if (string.IsNullOrEmpty(name) || !Names.Contains(name)) return null;
            if (!s_Loaded.TryGetValue(name, out var tex)) s_Loaded[name] = tex = Make(name);
            return tex;
        }

        /// <summary>A placeholder: one transparent texel (Unbound has no icon pixels to draw).</summary>
        static Texture2D Make(string name)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = name };
            tex.SetPixel(0, 0, Color.clear);
            tex.Apply();
            return tex;
        }

        internal static Texture2D Load(string name, bool logError)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var candidates = EditorGUIUtility.isProSkin ? new[] { "d_" + name, name } : new[] { name };
            foreach (var c in candidates)
            {
                if (!Names.Contains(c)) continue;
                if (!s_Loaded.TryGetValue(c, out var tex)) s_Loaded[c] = tex = Make(c);
                return tex;
            }
            if (logError) Debug.LogError($"Unable to load the icon: '{name}'.\nNote that either full project path should be used (with extension) or just the icon name if the icon is located in the following location: 'Assets/Editor Default Resources/Icons/' (without extension, since png is assumed)");
            return null;
        }
    }
}
