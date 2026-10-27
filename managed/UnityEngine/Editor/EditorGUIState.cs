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

        public static int IntField(Rect position, int value) { RequireGUI(); return EditorGUIControls.IntField(position, null, value); }
        public static int IntField(Rect position, string label, int value) { RequireGUI(); return EditorGUIControls.IntField(position, new GUIContent(label), value); }
        public static int IntField(Rect position, GUIContent label, int value) { RequireGUI(); return EditorGUIControls.IntField(position, label, value); }
        public static string TextField(Rect position, string text) { RequireGUI(); return EditorGUIControls.TextField(position, null, text); }
        public static string TextField(Rect position, string label, string text) { RequireGUI(); return EditorGUIControls.TextField(position, new GUIContent(label), text); }
        public static string TextField(Rect position, GUIContent label, string text) { RequireGUI(); return EditorGUIControls.TextField(position, label, text); }
        public static bool Toggle(Rect position, bool value) { RequireGUI(); return EditorGUIControls.Toggle(position, null, value); }
        public static bool Toggle(Rect position, string label, bool value) { RequireGUI(); return EditorGUIControls.Toggle(position, new GUIContent(label), value); }
        public static bool Toggle(Rect position, GUIContent label, bool value) { RequireGUI(); return EditorGUIControls.Toggle(position, label, value); }
        public static bool Foldout(Rect position, bool foldout, string content) { RequireGUI(); return EditorGUIControls.Foldout(position, foldout, new GUIContent(content)); }
        public static bool Foldout(Rect position, bool foldout, GUIContent content) { RequireGUI(); return EditorGUIControls.Foldout(position, foldout, content); }
        public static void LabelField(Rect position, string label) { RequireGUI(); EditorGUIControls.Label(position, new GUIContent(label)); }
        public static void LabelField(Rect position, GUIContent label) { RequireGUI(); EditorGUIControls.Label(position, label); }
        public static void LabelField(Rect position, string label, string label2) { RequireGUI(); EditorGUIControls.Label(position, new GUIContent(label + " " + label2)); }
        public static bool PropertyField(Rect position, SerializedProperty property) { RequireGUI(); return EditorGUIControls.Property(position, property, null); }
        public static bool PropertyField(Rect position, SerializedProperty property, bool includeChildren) { RequireGUI(); return EditorGUIControls.Property(position, property, null); }
        public static bool PropertyField(Rect position, SerializedProperty property, GUIContent label) { RequireGUI(); return EditorGUIControls.Property(position, property, label); }
        public static bool PropertyField(Rect position, SerializedProperty property, GUIContent label, bool includeChildren) { RequireGUI(); return EditorGUIControls.Property(position, property, label); }
        public static Rect PrefixLabel(Rect totalPosition, GUIContent label) { RequireGUI(); return EditorGUIControls.PrefixLabel(totalPosition, label); }
        public static void DrawRect(Rect rect, Color color) { RequireGUI(); }

        public static GUIContent BeginProperty(Rect totalPosition, GUIContent label, SerializedProperty property)
        {
            RequireGUI();
            s_PropertyStack.Push(property);
            return label;
        }

        public static void EndProperty()
        {
            if (s_PropertyStack.Count > 0) s_PropertyStack.Pop();
        }

        static readonly Stack<SerializedProperty> s_PropertyStack = new Stack<SerializedProperty>();
    }

    /// <summary>The auto-layout forms of the editor controls (see <see cref="EditorGUI"/>).</summary>
    public sealed partial class EditorGUILayout
    {
        static Rect Line(GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            return GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight, options);
        }

        public static int IntField(int value, params GUILayoutOption[] options) => EditorGUIControls.IntField(Line(options), null, value);
        public static int IntField(string label, int value, params GUILayoutOption[] options) => EditorGUIControls.IntField(Line(options), new GUIContent(label), value);
        public static int IntField(GUIContent label, int value, params GUILayoutOption[] options) => EditorGUIControls.IntField(Line(options), label, value);
        public static float FloatField(float value, params GUILayoutOption[] options) => EditorGUIControls.FloatField(Line(options), null, value);
        public static float FloatField(string label, float value, params GUILayoutOption[] options) => EditorGUIControls.FloatField(Line(options), new GUIContent(label), value);
        public static float FloatField(GUIContent label, float value, params GUILayoutOption[] options) => EditorGUIControls.FloatField(Line(options), label, value);
        public static string TextField(string text, params GUILayoutOption[] options) => EditorGUIControls.TextField(Line(options), null, text);
        public static string TextField(string label, string text, params GUILayoutOption[] options) => EditorGUIControls.TextField(Line(options), new GUIContent(label), text);
        public static string TextField(GUIContent label, string text, params GUILayoutOption[] options) => EditorGUIControls.TextField(Line(options), label, text);
        public static bool Toggle(bool value, params GUILayoutOption[] options) => EditorGUIControls.Toggle(Line(options), null, value);
        public static bool Toggle(string label, bool value, params GUILayoutOption[] options) => EditorGUIControls.Toggle(Line(options), new GUIContent(label), value);
        public static bool Toggle(GUIContent label, bool value, params GUILayoutOption[] options) => EditorGUIControls.Toggle(Line(options), label, value);
        public static int Popup(int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) => EditorGUIControls.Popup(Line(options), null, selectedIndex, displayedOptions);
        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) => EditorGUIControls.Popup(Line(options), new GUIContent(label), selectedIndex, displayedOptions);
        public static Enum EnumPopup(Enum selected, params GUILayoutOption[] options) => EditorGUIControls.EnumPopup(Line(options), null, selected);
        public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) => EditorGUIControls.EnumPopup(Line(options), new GUIContent(label), selected);
        public static Enum EnumPopup(GUIContent label, Enum selected, params GUILayoutOption[] options) => EditorGUIControls.EnumPopup(Line(options), label, selected);
        public static void LabelField(string label, params GUILayoutOption[] options) => EditorGUIControls.Label(Line(options), new GUIContent(label));
        public static void LabelField(GUIContent label, params GUILayoutOption[] options) => EditorGUIControls.Label(Line(options), label);
        public static void LabelField(string label, string label2, params GUILayoutOption[] options) => EditorGUIControls.Label(Line(options), new GUIContent(label + " " + label2));
        public static bool Foldout(bool foldout, string content) => EditorGUIControls.Foldout(Line(null), foldout, new GUIContent(content));
        public static bool Foldout(bool foldout, GUIContent content) => EditorGUIControls.Foldout(Line(null), foldout, content);
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) => EditorGUIControls.Foldout(Line(null), foldout, new GUIContent(content));
        public static bool Foldout(bool foldout, GUIContent content, bool toggleOnLabelClick) => EditorGUIControls.Foldout(Line(null), foldout, content);
        public static bool PropertyField(SerializedProperty property, params GUILayoutOption[] options) => EditorGUIControls.Property(Line(options), property, null);
        public static bool PropertyField(SerializedProperty property, GUIContent label, params GUILayoutOption[] options) => EditorGUIControls.Property(Line(options), property, label);
        public static bool PropertyField(SerializedProperty property, bool includeChildren, params GUILayoutOption[] options) => EditorGUIControls.Property(Line(options), property, null);
        public static bool PropertyField(SerializedProperty property, GUIContent label, bool includeChildren, params GUILayoutOption[] options) => EditorGUIControls.Property(Line(options), property, label);
        public static Object ObjectField(Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) => EditorGUIControls.ObjectField(Line(options), null, obj, objType);
        public static Object ObjectField(string label, Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) => EditorGUIControls.ObjectField(Line(options), new GUIContent(label), obj, objType);
        public static Object ObjectField(GUIContent label, Object obj, Type objType, bool allowSceneObjects, params GUILayoutOption[] options) => EditorGUIControls.ObjectField(Line(options), label, obj, objType);
        public static Rect GetControlRect(params GUILayoutOption[] options) => Line(options);
        public static Rect GetControlRect(bool hasLabel, params GUILayoutOption[] options) => Line(options);
        public static Rect GetControlRect(bool hasLabel, float height, params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            return GUILayoutUtility.GetRect(0, height, options);
        }

        public static void HelpBox(string message, MessageType type)
        {
            EditorGUI.RequireGUI();
            GUILayout.Label(message);
        }

        public static void HelpBox(string message, MessageType type, bool wide) => HelpBox(message, type);

        public static void Space()
        {
            EditorGUI.RequireGUI();
            GUILayoutUtility.GetRect(6, 6);
        }

        public static void Space(float width)
        {
            EditorGUI.RequireGUI();
            GUILayoutUtility.GetRect(width, width);
        }

        public static Rect BeginHorizontal(params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            GUILayout.BeginHorizontal(options);
            return default;
        }

        public static void EndHorizontal() => GUILayout.EndHorizontal();

        public static Rect BeginVertical(params GUILayoutOption[] options)
        {
            EditorGUI.RequireGUI();
            GUILayout.BeginVertical(options);
            return default;
        }

        public static void EndVertical() => GUILayout.EndVertical();

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
        static Rect Field(Rect position, GUIContent label)
        {
            if (label == null || (string.IsNullOrEmpty(label.text) && label.image == null)) return EditorGUI.IndentedRect(position);
            return PrefixLabel(position, label);
        }

        internal static Rect PrefixLabel(Rect position, GUIContent label)
        {
            var indented = EditorGUI.IndentedRect(position);
            float width = EditorGUIUtility.labelWidth - EditorGUI.indent;
            GUI.Label(new Rect(indented.x, indented.y, width, indented.height), label);
            return new Rect(indented.x + width, indented.y, indented.width - width, indented.height);
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

        internal static float Of(SerializedProperty property, GUIContent label, bool includeChildren)
        {
            var field = PropertyDrawers.FieldOf(property);
            float height = 0f;
            PropertyAttribute drawerAttribute = null;
            if (field != null)
            {
                foreach (var attribute in field.GetCustomAttributes<PropertyAttribute>(true).OrderBy(a => a.order))
                {
                    switch (attribute)
                    {
                        case SpaceAttribute space: height += space.height; break;
                        case HeaderAttribute _:
                            // The header's style is the Editor's: there is none outside an editor window.
                            EditorStyles.Require();
                            height += Line * 1.5f + 6f;
                            break;
                        default:
                            if (PropertyDrawers.DecoratorFor(attribute) is DecoratorDrawer decorator) height += decorator.GetHeight();
                            else drawerAttribute ??= attribute;
                            break;
                    }
                }
            }
            var drawer = PropertyDrawers.DrawerFor(property, field, drawerAttribute);
            if (drawer != null) return height + drawer.GetPropertyHeight(property, label ?? new GUIContent(property.displayName));
            switch (drawerAttribute)
            {
                case MultilineAttribute multiline:
                    return height + (!EditorGUIUtility.wideMode ? Line : 0f) + Line + (multiline.lines - 1) * 13f;
                case TextAreaAttribute _:
                    // Measured from its style: the Editor's text area style.
                    EditorStyles.Require();
                    break;
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

    /// <summary>Property and decorator drawers, by the [CustomPropertyDrawer] classes the loaded
    /// assemblies have.</summary>
    internal static class PropertyDrawers
    {
        static Dictionary<Type, List<(Type Drawer, bool Children)>> s_Drawers;

        static Dictionary<Type, List<(Type Drawer, bool Children)>> Drawers
        {
            get
            {
                if (s_Drawers != null) return s_Drawers;
                var map = new Dictionary<Type, List<(Type, bool)>>();
                foreach (var type in TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>())
                {
                    if (type.IsAbstract || !typeof(GUIDrawer).IsAssignableFrom(type)) continue;
                    foreach (var attribute in type.GetCustomAttributes<CustomPropertyDrawer>(false))
                    {
                        if (attribute.m_Type == null) continue;
                        if (!map.TryGetValue(attribute.m_Type, out var list)) map[attribute.m_Type] = list = new List<(Type, bool)>();
                        list.Add((type, attribute.m_UseForChildren));
                    }
                }
                return s_Drawers = map;
            }
        }

        static Type Find(Type target, bool decorator)
        {
            for (var t = target; t != null; t = t.BaseType)
            {
                if (!Drawers.TryGetValue(t, out var list)) continue;
                foreach (var (drawer, children) in list)
                {
                    if (typeof(DecoratorDrawer).IsAssignableFrom(drawer) != decorator) continue;
                    if (t == target || children) return drawer;
                }
            }
            return null;
        }

        internal static FieldInfo FieldOf(SerializedProperty property) => property.UnboundField;

        internal static DecoratorDrawer DecoratorFor(PropertyAttribute attribute)
        {
            var type = Find(attribute.GetType(), decorator: true);
            if (type == null) return null;
            var drawer = (DecoratorDrawer)Activator.CreateInstance(type, nonPublic: true);
            drawer.m_Attribute = attribute;
            return drawer;
        }

        internal static PropertyDrawer DrawerFor(SerializedProperty property, FieldInfo field, PropertyAttribute attribute)
        {
            Type type = attribute != null ? Find(attribute.GetType(), decorator: false) : null;
            if (type == null && field != null)
            {
                var fieldType = field.FieldType;
                if (property.isArray && property.propertyType != SerializedPropertyType.String) fieldType = null;
                else if (fieldType.IsArray) fieldType = fieldType.GetElementType();
                else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>)) fieldType = fieldType.GetGenericArguments()[0];
                if (fieldType != null) type = Find(fieldType, decorator: false);
            }
            if (type == null || !typeof(PropertyDrawer).IsAssignableFrom(type)) return null;
            var drawer = (PropertyDrawer)Activator.CreateInstance(type, nonPublic: true);
            drawer.m_Attribute = attribute;
            drawer.m_FieldInfo = field;
            return drawer;
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

        public virtual void OnGUI(Rect position) { }

        public virtual float GetHeight() => EditorGUIUtility.singleLineHeight;
    }

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
    }

    /// <summary>The Editor's control styles. They exist once an editor window has drawn; Unbound has
    /// none, and like the batch-mode Editor in Play mode it has no styles to give (measured: each
    /// throws).</summary>
    public sealed partial class EditorStyles
    {
        internal static void Require() => throw new NullReferenceException("Object reference not set to an instance of an object");

        static GUIStyle Style() { Require(); return null; }

        public static GUIStyle label => Style();
        public static GUIStyle miniLabel => Style();
        public static GUIStyle largeLabel => Style();
        public static GUIStyle boldLabel => Style();
        public static GUIStyle miniBoldLabel => Style();
        public static GUIStyle centeredGreyMiniLabel => Style();
        public static GUIStyle wordWrappedMiniLabel => Style();
        public static GUIStyle wordWrappedLabel => Style();
        public static GUIStyle linkLabel => Style();
        public static GUIStyle whiteLabel => Style();
        public static GUIStyle textField => Style();
        public static GUIStyle textArea => Style();
        public static GUIStyle numberField => Style();
        public static GUIStyle popup => Style();
        public static GUIStyle objectField => Style();
        public static GUIStyle colorField => Style();
        public static GUIStyle layerMaskField => Style();
        public static GUIStyle toggle => Style();
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
        public static GUIStyle inspectorDefaultMargins => Style();
        public static GUIStyle inspectorFullWidthMargins => Style();

        /// <summary>The Editor's UI font (its fonts load without a window).</summary>
        public static Font standardFont => EditorFonts.Standard;
    }

    internal static class EditorFonts
    {
        static Font s_Standard;

        internal static Font Standard => s_Standard ??= MakeFont("Inter-Regular");

        static Font MakeFont(string name)
        {
            var font = new Font(name) { name = name };
            return font;
        }
    }

    public sealed partial class EditorGUIUtility : GUIUtility
    {
        public static float singleLineHeight => 18f;

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

        /// <summary>An object's label and icon as object fields show them: "Name (Type)", "None (Type)".</summary>
        public static GUIContent ObjectContent(Object obj, Type type)
        {
            var typeName = ObjectNames.NicifyVariableName((obj != null ? obj.GetType() : type)?.Name ?? "Object");
            var text = obj != null ? $"{obj.name} ({typeName})" : $"None ({typeName})";
            return new GUIContent(text, EditorIcons.Load(((obj != null ? obj.GetType() : type)?.Name ?? "DefaultAsset") + " Icon", logError: false) ?? EditorIcons.Placeholder);
        }

        public static string systemCopyBuffer { get; set; } = "";
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

        internal static Texture2D Load(string name, bool logError)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var candidates = EditorGUIUtility.isProSkin ? new[] { "d_" + name, name } : new[] { name };
            foreach (var c in candidates)
            {
                if (!Names.Contains(c)) continue;
                if (!s_Loaded.TryGetValue(c, out var tex)) s_Loaded[c] = tex = new Texture2D(1, 1) { name = c };
                return tex;
            }
            if (logError) Debug.LogError($"Unable to load the icon: '{name}'.\nNote that either full project path should be used (with extension) or just the icon name if the icon is located in the following location: 'Assets/Editor Default Resources/Icons/' (without extension, since png is assumed)");
            return null;
        }
    }
}
