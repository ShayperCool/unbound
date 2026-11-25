using System;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI's mask, enum-flags, layer, tag and enum popup fields as Unity 6000.0's C# does
    /// them: keyboard-focusable popups whose menu (Unbound shows none, said once) sends its choice
    /// back as a command to the control it was opened for; a mask's button names its single flag,
    /// Nothing, Everything or Mixed... (measured: tests/projects/editorobjectfields, values, control
    /// ids, GUI.changed, layout rects and the menus' commands). Other families report the fields
    /// not implemented and return the value they are given.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_TagFieldHash = "s_TagFieldHash".GetHashCode();
        static readonly int s_MaskField = "s_MaskField".GetHashCode();
        static readonly int s_EnumFlagsField = "s_EnumFlagsField".GetHashCode();
        static readonly int s_GenericField = "s_GenericField".GetHashCode();
        static string[] m_FlagNames;
        static int[] m_FlagValues;

        // The default styles, read only on the measured family: elsewhere the fields report
        // themselves not implemented before they would use a style.
        internal static GUIStyle PopupStyle => Compat.EditorValueFieldsMeasured ? EditorStyles.popup : null;

        public enum PropertyVisibility
        {
            All,
            OnlyVisible
        }

        public static string TagField(Rect position, string tag) => TagField(position, tag, PopupStyle);

        // Without a label, the labelled form with an empty label (as the Editor's).
        public static string TagField(Rect position, string tag, GUIStyle style)
        {
            if (!PickerFieldsMeasured("TagField")) return tag;
            return TagFieldInternal(position, EditorGUIUtility.TempContent(string.Empty), tag, style);
        }

        public static string TagField(Rect position, string label, string tag) => TagField(position, EditorGUIUtility.TempContent(label), tag, PopupStyle);

        public static string TagField(Rect position, string label, string tag, GUIStyle style) => TagField(position, EditorGUIUtility.TempContent(label), tag, style);

        public static string TagField(Rect position, GUIContent label, string tag) => TagField(position, label, tag, PopupStyle);

        public static string TagField(Rect position, GUIContent label, string tag, GUIStyle style)
        {
            if (!PickerFieldsMeasured("TagField")) return tag;
            return TagFieldInternal(position, label, tag, style);
        }

        /// <summary>The tags' menu choice: a tag, or "Add Tag..." (which opens the Tag Manager;
        /// Unbound shows none, said once).</summary>
        static string ChooseTag(string tag, int selected)
        {
            string[] tagValues = InternalEditorUtility.tags;
            if (selected >= tagValues.Length)
                Stubs.Ignored("UnityEditor.TagManagerInspector", "the Tag Manager is not shown: Unbound has no editor windows");
            else
                tag = tagValues[selected];
            return tag;
        }

        static GUIContent[] TagOptions(string tag, out int index)
        {
            string[] tagValues = InternalEditorUtility.tags;
            int i;
            for (i = 0; i < tagValues.Length; i++)
                if (tagValues[i] == tag) break;
            index = i;
            var options = new GUIContent[tagValues.Length + 2];
            for (int j = 0; j < tagValues.Length; j++) options[j] = new GUIContent(tagValues[j]);
            options[tagValues.Length] = new GUIContent("");
            options[tagValues.Length + 1] = new GUIContent("Add Tag...");
            return options;
        }

        internal static string TagFieldInternal(Rect position, string tag, GUIStyle style)
        {
            position = IndentedRect(position);
            int id = GUIUtility.GetControlID(s_TagFieldHash, FocusType.Keyboard, position);
            Event evt = Event.current;
            int selected = PopupCallbackInfo.GetSelectedValueForControl(id, -1);
            if (selected != -1) tag = ChooseTag(tag, selected);

            if ((evt.type == EventType.MouseDown && position.Contains(evt.mousePosition)) || MainActionKeyForControl(evt, id))
            {
                var options = TagOptions(tag, out int i);
                DoPopup(position, id, i, options, style);
                return tag;
            }
            if (evt.type == EventType.Repaint)
            {
                BeginHandleMixedValueContentColor();
                style.Draw(position, showMixedValue ? s_MixedValueContent : EditorGUIUtility.TempContent(tag), id, false);
                EndHandleMixedValueContentColor();
            }
            return tag;
        }

        internal static string TagFieldInternal(Rect position, GUIContent label, string tag, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_TagFieldHash, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);
            Event evt = Event.current;
            int selected = PopupCallbackInfo.GetSelectedValueForControl(id, -1);
            if (selected != -1) tag = ChooseTag(tag, selected);

            if ((evt.type == EventType.MouseDown && position.Contains(evt.mousePosition)) || MainActionKeyForControl(evt, id))
            {
                var options = TagOptions(tag, out int i);
                DoPopup(position, id, i, options, style);
                return tag;
            }
            if (evt.type == EventType.Repaint)
                style.Draw(position, showMixedValue ? s_MixedValueContent : EditorGUIUtility.TempContent(tag), id, false, position.Contains(evt.mousePosition));
            return tag;
        }

        public static int LayerField(Rect position, int layer) => LayerField(position, GUIContent.none, layer, PopupStyle);

        public static int LayerField(Rect position, int layer, GUIStyle style) => LayerField(position, GUIContent.none, layer, style);

        public static int LayerField(Rect position, string label, int layer) => LayerField(position, EditorGUIUtility.TempContent(label), layer, PopupStyle);

        public static int LayerField(Rect position, string label, int layer, GUIStyle style) => LayerField(position, EditorGUIUtility.TempContent(label), layer, style);

        public static int LayerField(Rect position, GUIContent label, int layer) => LayerField(position, label, layer, PopupStyle);

        public static int LayerField(Rect position, GUIContent label, int layer, GUIStyle style)
        {
            if (!PickerFieldsMeasured("LayerField")) return layer;
            return LayerFieldInternal(position, label, layer, style);
        }

        /// <summary>A layer popup: the named layers in order ("i: name" in the menu), a separator
        /// and "Add Layer..."; the menu's choice is the n-th named layer.</summary>
        internal static int LayerFieldInternal(Rect position, GUIContent label, int layer, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_TagFieldHash, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);
            Event evt = Event.current;
            bool wasChangedBefore = GUI.changed;
            int selected = PopupCallbackInfo.GetSelectedValueForControl(id, -1);
            if (selected != -1)
            {
                if (selected >= InternalEditorUtility.layers.Length)
                {
                    Stubs.Ignored("UnityEditor.TagManagerInspector", "the Tag Manager is not shown: Unbound has no editor windows");
                    GUI.changed = wasChangedBefore;
                }
                else
                {
                    int count = 0;
                    for (int i = 0; i < 32; i++)
                    {
                        if (InternalEditorUtility.GetLayerName(i).Length != 0)
                        {
                            if (count == selected)
                            {
                                layer = i;
                                break;
                            }
                            count++;
                        }
                    }
                }
            }

            if ((evt.type == EventType.MouseDown && position.Contains(evt.mousePosition)) || MainActionKeyForControl(evt, id))
            {
                int count = 0;
                for (int i = 0; i < 32; i++)
                {
                    if (InternalEditorUtility.GetLayerName(i).Length != 0)
                    {
                        if (i == layer) break;
                        count++;
                    }
                }
                string[] layers = InternalEditorUtility.GetLayersWithId();
                var options = new GUIContent[layers.Length + 2];
                for (int j = 0; j < layers.Length; j++) options[j] = new GUIContent(layers[j]);
                options[layers.Length] = new GUIContent("");
                options[layers.Length + 1] = new GUIContent("Add Layer...");
                DoPopup(position, id, count, options, style);
                Event.current.Use();
                return layer;
            }
            if (evt.type == EventType.Repaint)
            {
                var content = showMixedValue ? s_MixedValueContent : EditorGUIUtility.TempContent(InternalEditorUtility.GetLayerName(layer));
                style.Draw(position, content, id, false, position.Contains(evt.mousePosition));
            }
            return layer;
        }

        public static int MaskField(Rect position, GUIContent label, int mask, string[] displayedOptions) =>
            MaskField(position, label, mask, displayedOptions, PopupStyle);

        public static int MaskField(Rect position, GUIContent label, int mask, string[] displayedOptions, GUIStyle style)
        {
            if (!PickerFieldsMeasured("MaskField")) return mask;
            return MaskFieldInternal(position, label, mask, displayedOptions, style);
        }

        public static int MaskField(Rect position, string label, int mask, string[] displayedOptions) =>
            MaskField(position, EditorGUIUtility.TempContent(label), mask, displayedOptions, PopupStyle);

        public static int MaskField(Rect position, string label, int mask, string[] displayedOptions, GUIStyle style) =>
            MaskField(position, EditorGUIUtility.TempContent(label), mask, displayedOptions, style);

        public static int MaskField(Rect position, int mask, string[] displayedOptions) =>
            MaskField(position, mask, displayedOptions, PopupStyle);

        public static int MaskField(Rect position, int mask, string[] displayedOptions, GUIStyle style)
        {
            if (!PickerFieldsMeasured("MaskField")) return mask;
            return MaskFieldInternal(position, mask, displayedOptions, style);
        }

        internal static int MaskFieldInternal(Rect position, GUIContent label, int mask, string[] displayedOptions, GUIStyle style)
        {
            var id = GUIUtility.GetControlID(s_MaskField, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);
            return MaskFieldGUI.DoMaskField(position, id, mask, displayedOptions, style);
        }

        internal static int MaskFieldInternal(Rect position, int mask, string[] displayedOptions, GUIStyle style)
        {
            var id = GUIUtility.GetControlID(s_MaskField, FocusType.Keyboard, position);
            return MaskFieldGUI.DoMaskField(IndentedRect(position), id, mask, displayedOptions, style);
        }

        public static Enum EnumFlagsField(Rect position, Enum enumValue) => EnumFlagsField(position, enumValue, PopupStyle);

        public static Enum EnumFlagsField(Rect position, Enum enumValue, GUIStyle style) => EnumFlagsField(position, GUIContent.none, enumValue, style);

        public static Enum EnumFlagsField(Rect position, string label, Enum enumValue) => EnumFlagsField(position, label, enumValue, PopupStyle);

        public static Enum EnumFlagsField(Rect position, string label, Enum enumValue, GUIStyle style) =>
            EnumFlagsField(position, EditorGUIUtility.TempContent(label), enumValue, style);

        public static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue) => EnumFlagsField(position, label, enumValue, PopupStyle);

        public static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue, GUIStyle style) =>
            EnumFlagsField(position, label, enumValue, false, style);

        public static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue, bool includeObsolete, GUIStyle style = null)
        {
            if (!PickerFieldsMeasured("EnumFlagsField")) return enumValue;
            return EnumFlagsField(position, label, enumValue, enumValue.GetType(), includeObsolete, out _, out _, style ?? PopupStyle);
        }

        internal static Enum EnumFlagsField(Rect position, GUIContent label, Enum enumValue, Type enumType, bool includeObsolete, out int changedFlags, out bool changedToValue, GUIStyle style)
        {
            if (!enumType.IsEnum)
                throw new ArgumentException("Parameter enumValue must be of type System.Enum", nameof(enumValue));

            var enumData = EnumDataUtility.GetCachedEnumData(enumType, !includeObsolete);
            if (!enumData.serializable)
                throw new NotSupportedException(string.Format("Unsupported enum base type for {0}", enumType.Name));

            var id = GUIUtility.GetControlID(s_EnumFlagsField, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);

            var flagsInt = EnumDataUtility.EnumFlagsToInt(enumData, enumValue);

            BeginChangeCheck();
            flagsInt = MaskFieldGUI.DoMaskField(position, id, flagsInt, enumData.displayNames, enumData.flagValues, style, out changedFlags, out changedToValue);
            if (!EndChangeCheck()) return enumValue;
            return EnumDataUtility.IntToEnumFlags(enumType, flagsInt);
        }

        internal static int EnumFlagsField(Rect position, GUIContent label, int enumValue, Type enumType, bool includeObsolete, GUIStyle style)
        {
            if (!enumType.IsEnum)
                throw new ArgumentException("Specified enumType must be System.Enum", nameof(enumType));

            var enumData = EnumDataUtility.GetCachedEnumData(enumType, !includeObsolete);
            if (!enumData.serializable)
                throw new NotSupportedException(string.Format("Unsupported enum base type for {0}", enumType.Name));

            var id = GUIUtility.GetControlID(s_EnumFlagsField, FocusType.Keyboard, position);
            position = PrefixLabel(position, id, label);
            return MaskFieldGUI.DoMaskField(position, id, enumValue, enumData.displayNames, enumData.flagValues, style, out _, out _, enumType.GetEnumUnderlyingType());
        }

        public static Enum EnumPopup(Rect position, Enum selected) => EnumPopup(position, selected, PopupStyle);

        public static Enum EnumPopup(Rect position, Enum selected, GUIStyle style) => EnumPopup(position, GUIContent.none, selected, style);

        public static Enum EnumPopup(Rect position, string label, Enum selected) => EnumPopup(position, label, selected, PopupStyle);

        public static Enum EnumPopup(Rect position, string label, Enum selected, GUIStyle style) =>
            EnumPopup(position, EditorGUIUtility.TempContent(label), selected, style);

        public static Enum EnumPopup(Rect position, GUIContent label, Enum selected) => EnumPopup(position, label, selected, PopupStyle);

        public static Enum EnumPopup(Rect position, GUIContent label, Enum selected, GUIStyle style)
        {
            if (!PickerFieldsMeasured("EnumPopup")) return selected;
            return EnumPopupInternal(position, label, selected, selected.GetType(), null, false, style);
        }

        public static Enum EnumPopup(Rect position, GUIContent label, Enum selected, Func<Enum, bool> checkEnabled, bool includeObsolete = false, GUIStyle style = null)
        {
            if (!PickerFieldsMeasured("EnumPopup")) return selected;
            return EnumPopupInternal(position, label, selected, selected.GetType(), checkEnabled, includeObsolete, style ?? PopupStyle);
        }

        static readonly Dictionary<(Type, bool), GUIContent[]> s_EnumOptions = new Dictionary<(Type, bool), GUIContent[]>();

        /// <summary>An enum's options, made once per type (and obsolete-value choice).</summary>
        internal static GUIContent[] EnumOptions(Type enumType, bool includeObsolete, EnumData enumData)
        {
            if (s_EnumOptions.TryGetValue((enumType, includeObsolete), out var options)) return options;
            options = new GUIContent[enumData.displayNames.Length];
            for (int i = 0; i < options.Length; i++)
                options[i] = new GUIContent(enumData.displayNames[i], enumData.tooltip != null && i < enumData.tooltip.Length ? enumData.tooltip[i] : null);
            s_EnumOptions[(enumType, includeObsolete)] = options;
            return options;
        }

        static Enum EnumPopupInternal(Rect position, GUIContent label, Enum selected, Type enumType, Func<Enum, bool> checkEnabled, bool includeObsolete, GUIStyle style)
        {
            if (!enumType.IsEnum)
                throw new ArgumentException("Parameter selected must be of type System.Enum", nameof(selected));

            var enumData = EnumDataUtility.GetCachedEnumData(enumType, !includeObsolete);
            var i = showMixedValue ? -1 : Array.IndexOf(enumData.values, selected);
            var options = EnumOptions(enumType, includeObsolete, enumData);
            i = EnumPopupIndex(position, label, i, options, checkEnabled == null ? null : index => checkEnabled(enumData.values[index]), style);
            return (i < 0 || i >= enumData.flagValues.Length) ? selected : enumData.values[i];
        }

        static int EnumPopupInternal(Rect position, GUIContent label, int flagValue, Type enumType, Func<Enum, bool> checkEnabled, bool includeObsolete, GUIStyle style)
        {
            if (!enumType.IsEnum)
                throw new ArgumentException("Parameter selected must be of type System.Enum", nameof(enumType));

            var enumData = EnumDataUtility.GetCachedEnumData(enumType, !includeObsolete);
            var i = showMixedValue ? -1 : Array.IndexOf(enumData.flagValues, flagValue);
            var options = EnumOptions(enumType, includeObsolete, enumData);
            i = EnumPopupIndex(position, label, i, options, checkEnabled == null ? null : index => checkEnabled(enumData.values[index]), style);
            return (i < 0 || i >= enumData.flagValues.Length) ? flagValue : enumData.flagValues[i];
        }

        static int EnumPopupIndex(Rect position, GUIContent label, int selected, GUIContent[] options, Func<int, bool> checkEnabled, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_PopupHash, FocusType.Keyboard, position);
            if (label != null) position = PrefixLabel(position, id, label);
            return DoPopup(position, id, selected, options, checkEnabled, style);
        }

        /// <summary>An enum property: a mask field for a [Flags] enum, else a popup.</summary>
        static void EnumPopup(Rect position, SerializedProperty property, GUIContent label)
        {
            Type type = PropertyFieldType(property);
            if (type != null && type.IsEnum)
            {
                BeginChangeCheck();
                int value = type.IsDefined(typeof(FlagsAttribute), false)
                    ? EnumFlagsField(position, label, property.intValue, type, false, PopupStyle)
                    : EnumPopupInternal(position, label, property.intValue, type, null, false, PopupStyle);
                if (EndChangeCheck())
                {
                    // A negative flag of an unsigned enum is converted, or it would be clamped.
                    Type underlying = type.GetEnumUnderlyingType();
                    if (value < 0 && (underlying == typeof(uint) || underlying == typeof(ushort) || underlying == typeof(byte)))
                        property.longValue = (uint)value;
                    else
                        property.intValue = value;
                }
            }
            else if (EnumPropertyOptions(property) is GUIContent[] options)
            {
                // An engine component's enum, without a script field: its value's names.
                BeginChangeCheck();
                int idx = Popup(position, label, property.enumValueIndex, options);
                if (EndChangeCheck()) property.enumValueIndex = idx;
            }
            else
            {
                // A native enum (a settings or material property) has no C# type to name its values.
                Stubs.Ignored("UnityEditor.EditorGUI.EnumPopup of a native enum property", "an enum property without a C# enum type is drawn without its names");
                int id = GUIUtility.GetControlID(s_PopupHash, FocusType.Keyboard, position);
                PrefixLabel(position, id, label);
            }
        }

        static readonly Dictionary<Type, GUIContent[]> s_EnumPropertyOptions = new Dictionary<Type, GUIContent[]>();

        /// <summary>The value names of an enum property of an engine object (null when its node has
        /// no C# enum type), made once per enum type.</summary>
        internal static GUIContent[] EnumPropertyOptions(SerializedProperty property)
        {
            var type = property.UnboundClrType;
            if (type == null || !type.IsEnum) return null;
            if (s_EnumPropertyOptions.TryGetValue(type, out var options)) return options;
            var names = property.enumDisplayNames;
            options = new GUIContent[names.Length];
            for (int i = 0; i < names.Length; i++) options[i] = new GUIContent(names[i]);
            s_EnumPropertyOptions[type] = options;
            return options;
        }

        /// <summary>The type of the script field a property was read from (a collection's element
        /// type for its elements).</summary>
        static Type PropertyFieldType(SerializedProperty property)
        {
            var field = property.UnboundField;
            if (field != null) return field.FieldType;
            var declaring = property.UnboundDeclaringField?.FieldType;
            if (declaring == null) return null;
            if (declaring.IsArray) return declaring.GetElementType();
            if (declaring.IsGenericType && declaring.GetGenericTypeDefinition() == typeof(List<>)) return declaring.GetGenericArguments()[0];
            return declaring;
        }

        internal static int s_DropdownButtonHash = "DropdownButton".GetHashCode();

        public static bool DropdownButton(Rect position, GUIContent content, FocusType focusType, GUIStyle style)
        {
            if (!PickerFieldsMeasured("DropdownButton")) return false;
            int id = GUIUtility.GetControlID(s_DropdownButtonHash, focusType, position);
            return DropdownButton(id, position, content, style);
        }

        /// <summary>A button that is pressed on mouse down, like a popup's.</summary>
        internal static bool DropdownButton(int id, Rect position, GUIContent content, GUIStyle style)
        {
            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.Repaint:
                    var hovered = position.Contains(evt.mousePosition);
                    if (showMixedValue)
                    {
                        BeginHandleMixedValueContentColor();
                        style.Draw(position, s_MixedValueContent, id, false, hovered);
                        EndHandleMixedValueContentColor();
                    }
                    else
                        style.Draw(position, content, id, false, hovered);
                    break;
                case EventType.MouseDown:
                    if (GUIUtility.HitTest(position, evt) && evt.button == 0)
                    {
                        GUI.GrabMouseControl(id);
                        evt.Use();
                        return true;
                    }
                    break;
                case EventType.MouseUp:
                    if (GUI.HasMouseControl(id))
                    {
                        GUI.ReleaseMouseControl();
                        evt.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == id && evt.character == ' ')
                    {
                        evt.Use();
                        return true;
                    }
                    break;
            }
            return false;
        }

        /// <summary>A layer mask property: a button naming its layers that opens the mask's menu
        /// (Unbound shows none, said once).</summary>
        static void LayerMaskPropertyField(Rect position, SerializedProperty property, GUIContent label)
        {
            TagManager.GetDefinedLayers(ref m_FlagNames, ref m_FlagValues);
            MaskFieldGUI.GetMaskButtonValue(property.intValue, m_FlagNames, m_FlagValues, out var toggleLabel, out var toggleLabelMixed);
            if (label != null) position = PrefixLabel(position, label, EditorStyles.label);
            var toggleLabelContent = property.hasMultipleDifferentValues ? mixedValueContent : MaskFieldGUI.DoMixedLabel(toggleLabel, toggleLabelMixed, position, EditorStyles.layerMaskField);
            bool toggled = DropdownButton(position, toggleLabelContent, FocusType.Keyboard, EditorStyles.layerMaskField);
            if (toggled)
            {
                PopupWindow.Show(position, new MaskFieldDropDown(property));
                GUIUtility.ExitGUI();
            }
        }
    }

    public sealed partial class EditorGUILayout
    {
        public static string TagField(string tag, params GUILayoutOption[] options) => TagField(tag, EditorGUI.PopupStyle, options);

        public static string TagField(string tag, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.TagField(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), tag, style);

        public static string TagField(string label, string tag, params GUILayoutOption[] options) =>
            TagField(EditorGUIUtility.TempContent(label), tag, EditorGUI.PopupStyle, options);

        public static string TagField(string label, string tag, GUIStyle style, params GUILayoutOption[] options) =>
            TagField(EditorGUIUtility.TempContent(label), tag, style, options);

        public static string TagField(GUIContent label, string tag, params GUILayoutOption[] options) => TagField(label, tag, EditorGUI.PopupStyle, options);

        public static string TagField(GUIContent label, string tag, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.TagField(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, tag, style);

        public static int LayerField(int layer, params GUILayoutOption[] options) => LayerField(layer, EditorGUI.PopupStyle, options);

        public static int LayerField(int layer, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.LayerField(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), layer, style);

        public static int LayerField(string label, int layer, params GUILayoutOption[] options) =>
            LayerField(EditorGUIUtility.TempContent(label), layer, EditorGUI.PopupStyle, options);

        public static int LayerField(string label, int layer, GUIStyle style, params GUILayoutOption[] options) =>
            LayerField(EditorGUIUtility.TempContent(label), layer, style, options);

        public static int LayerField(GUIContent label, int layer, params GUILayoutOption[] options) => LayerField(label, layer, EditorGUI.PopupStyle, options);

        public static int LayerField(GUIContent label, int layer, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.LayerField(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, layer, style);

        public static int MaskField(GUIContent label, int mask, string[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.MaskField(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, mask, displayedOptions, style);

        public static int MaskField(string label, int mask, string[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.MaskField(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, mask, displayedOptions, style);

        public static int MaskField(GUIContent label, int mask, string[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.MaskField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.PopupStyle, options), label, mask, displayedOptions, EditorGUI.PopupStyle);

        public static int MaskField(string label, int mask, string[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.MaskField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.PopupStyle, options), label, mask, displayedOptions, EditorGUI.PopupStyle);

        public static int MaskField(int mask, string[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.MaskField(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), mask, displayedOptions, style);

        public static int MaskField(int mask, string[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.MaskField(GetControlRect(false, EditorGUI.kSingleLineHeight, EditorGUI.PopupStyle, options), mask, displayedOptions, EditorGUI.PopupStyle);

        public static Enum EnumFlagsField(Enum enumValue, params GUILayoutOption[] options) => EnumFlagsField(enumValue, EditorGUI.PopupStyle, options);

        public static Enum EnumFlagsField(Enum enumValue, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.EnumFlagsField(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), enumValue, style);

        public static Enum EnumFlagsField(string label, Enum enumValue, params GUILayoutOption[] options) => EnumFlagsField(label, enumValue, EditorGUI.PopupStyle, options);

        public static Enum EnumFlagsField(string label, Enum enumValue, GUIStyle style, params GUILayoutOption[] options) =>
            EnumFlagsField(EditorGUIUtility.TempContent(label), enumValue, style, options);

        public static Enum EnumFlagsField(GUIContent label, Enum enumValue, params GUILayoutOption[] options) => EnumFlagsField(label, enumValue, EditorGUI.PopupStyle, options);

        public static Enum EnumFlagsField(GUIContent label, Enum enumValue, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.EnumFlagsField(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, enumValue, style);

        public static Enum EnumFlagsField(GUIContent label, Enum enumValue, bool includeObsolete, params GUILayoutOption[] options) =>
            EnumFlagsField(label, enumValue, includeObsolete, EditorGUI.PopupStyle, options);

        public static Enum EnumFlagsField(GUIContent label, Enum enumValue, bool includeObsolete, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.EnumFlagsField(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, enumValue, includeObsolete, style);

        public static Enum EnumPopup(Enum selected, params GUILayoutOption[] options) => EnumPopup(selected, EditorGUI.PopupStyle, options);

        public static Enum EnumPopup(Enum selected, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.EnumPopup(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), selected, style);

        public static Enum EnumPopup(string label, Enum selected, params GUILayoutOption[] options) => EnumPopup(label, selected, EditorGUI.PopupStyle, options);

        public static Enum EnumPopup(string label, Enum selected, GUIStyle style, params GUILayoutOption[] options) =>
            EnumPopup(EditorGUIUtility.TempContent(label), selected, style, options);

        public static Enum EnumPopup(GUIContent label, Enum selected, params GUILayoutOption[] options) => EnumPopup(label, selected, EditorGUI.PopupStyle, options);

        public static Enum EnumPopup(GUIContent label, Enum selected, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.EnumPopup(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, selected, style);

        public static Enum EnumPopup(GUIContent label, Enum selected, Func<Enum, bool> checkEnabled, bool includeObsolete, params GUILayoutOption[] options) =>
            EnumPopup(label, selected, checkEnabled, includeObsolete, EditorGUI.PopupStyle, options);

        public static Enum EnumPopup(GUIContent label, Enum selected, Func<Enum, bool> checkEnabled, bool includeObsolete, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.EnumPopup(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, selected, checkEnabled, includeObsolete, style);
    }

    /// <summary>Unity's mask field: a popup button naming the mask, whose menu sends the new mask
    /// back to the field as the MaskMenuChanged command.</summary>
    internal static partial class MaskFieldGUI
    {
        class MaskCallbackInfo
        {
            public static MaskCallbackInfo m_Instance;
            const string kMaskMenuChangedMessage = "MaskMenuChanged";
            readonly int m_ControlID;
            int m_NewMask;
            public MaskFieldDropDown m_DropDown;
            bool m_Validate = false;

            public MaskCallbackInfo(int controlID) => m_ControlID = controlID;

            public static int GetSelectedValueForControl(int controlID, int mask, out int changedFlags, out bool changedToValue)
            {
                var evt = Event.current;
                changedFlags = 0;
                changedToValue = false;
                if (evt.type == EventType.ExecuteCommand && evt.commandName == kMaskMenuChangedMessage)
                {
                    if (m_Instance == null)
                    {
                        Debug.LogError("Mask menu has no instance");
                        return mask;
                    }
                    if (m_Instance.m_ControlID == controlID)
                    {
                        changedFlags = mask ^ m_Instance.m_NewMask;
                        changedToValue = (m_Instance.m_NewMask & changedFlags) != 0;
                        if (changedFlags != 0 || EditorGUI.showMixedValue)
                        {
                            mask = m_Instance.m_NewMask;
                            GUI.changed = true;
                        }
                    }
                }
                return mask;
            }

            internal void SetMaskValueDelegate(object userData, string[] options, int selected)
            {
                m_NewMask = ((int[])userData)[selected];
                Stubs.Ignored("UnityEditor.MaskFieldGUI's menu command", "a mask menu's choice is not sent to the field: Unbound has no editor views");
            }

            public void UpdateFlagChanges(int controlID, int mask, int[] optionMaskValues)
            {
                var evt = Event.current;
                if (evt.type == EventType.ExecuteCommand)
                {
                    m_Validate = true;
                }
                else if (evt.type == EventType.Repaint && m_Validate)
                {
                    if (m_DropDown == null) return;
                    if (mask != m_NewMask && m_ControlID == controlID)
                        m_DropDown.UpdateMaskValues(mask, optionMaskValues);
                    m_Validate = false;
                }
            }
        }

        internal static int DoMaskField(Rect position, int controlID, int mask, string[] flagNames, GUIStyle style, bool autoSelectEverything = true) =>
            DoMaskField(position, controlID, mask, flagNames, style, out _, out _, autoSelectEverything);

        internal static int DoMaskField(Rect position, int controlID, int mask, string[] flagNames, int[] flagValues, GUIStyle style, bool autoSelectEverything = true) =>
            DoMaskField(position, controlID, mask, flagNames, flagValues, style, out _, out _, autoSelectEverything: autoSelectEverything);

        internal static int DoMaskField(Rect position, int controlID, int mask, string[] flagNames, GUIStyle style, out int changedFlags, out bool changedToValue, bool autoSelectEverything = true)
        {
            var flagValues = new int[flagNames.Length];
            for (int i = 0; i < flagValues.Length; ++i) flagValues[i] = 1 << i;
            return DoMaskField(position, controlID, mask, flagNames, flagValues, style, out changedFlags, out changedToValue, autoSelectEverything: autoSelectEverything);
        }

        internal static void DestroyMaskCallBackInfo() => MaskCallbackInfo.m_Instance = null;

        internal static int DoMaskField(Rect position, int controlID, int mask, string[] flagNames, int[] flagValues, GUIStyle style, out int changedFlags, out bool changedToValue, Type enumType = null, bool autoSelectEverything = true)
        {
            mask = MaskCallbackInfo.GetSelectedValueForControl(controlID, mask, out changedFlags, out changedToValue);
            GetMenuOptions(mask, flagNames, flagValues, out var buttonText, out var buttonTextMixed, out var optionNames, out var optionMaskValues, out _, enumType, autoSelectEverything);

            MaskCallbackInfo.m_Instance?.UpdateFlagChanges(controlID, mask, optionMaskValues);

            Event evt = Event.current;
            if (evt.type == EventType.Repaint)
            {
                var buttonContent = EditorGUI.showMixedValue ? EditorGUI.mixedValueContent : DoMixedLabel(buttonText, buttonTextMixed, position, style);
                style.Draw(position, buttonContent, controlID, false, position.Contains(evt.mousePosition));
            }
            else if ((evt.type == EventType.MouseDown && position.Contains(evt.mousePosition)) || EditorGUI.MainActionKeyForControl(evt, controlID))
            {
                MaskCallbackInfo.m_Instance = new MaskCallbackInfo(controlID);
                MaskCallbackInfo.m_Instance.m_DropDown = new MaskFieldDropDown(optionNames, flagValues, optionMaskValues, mask, MaskCallbackInfo.m_Instance.SetMaskValueDelegate, autoSelectEverything);
                PopupWindow.Show(position, MaskCallbackInfo.m_Instance.m_DropDown);
            }
            return mask;
        }

        static readonly List<string[]> s_OptionNames = new List<string[]>();
        static readonly List<int[]> s_OptionValues = new List<int[]>();
        static readonly List<int[]> s_SelectedOptions = new List<int[]>();
        static readonly HashSet<int> s_SelectedOptionsSet = new HashSet<int>();
        static readonly List<int> s_UsedFlags = new List<int>();
        static readonly StringBuilder s_Text = new StringBuilder();

        static T[] GetBuffer<T>(List<T[]> pool, int bufferLength)
        {
            for (int i = pool.Count; i <= bufferLength; ++i) pool.Add(null);
            if (pool[bufferLength] == null) pool[bufferLength] = new T[bufferLength];
            var buffer = pool[bufferLength];
            for (int i = 0, length = buffer.Length; i < length; ++i) buffer[i] = default;
            return buffer;
        }

        internal static void GetMaskButtonValue(int mask, string[] flagNames, int[] flagValues, out string buttonText, out string buttonMixedValuesText)
        {
            const int everythingValue = ~0;
            bool hasNothingName = flagValues[0] == 0;
            bool hasEverythingName = flagValues[flagValues.Length - 1] == everythingValue;
            var nothingName = hasNothingName ? flagNames[0] : "Nothing";
            var everythingName = hasEverythingName ? flagNames[flagValues.Length - 1] : "Everything";
            var flagCount = flagNames.Length - (hasNothingName ? 1 : 0) - (hasEverythingName ? 1 : 0);
            var flagStartIndex = hasNothingName ? 1 : 0;
            var flagEndIndex = flagStartIndex + flagCount;

            var flagMask = 0;
            var intermediateMask = 0;
            var usedFlags = s_UsedFlags;
            usedFlags.Clear();
            buttonText = null;
            buttonMixedValuesText = null;

            for (var flagIndex = flagStartIndex; flagIndex < flagEndIndex; flagIndex++)
            {
                var flagValue = flagValues[flagIndex];
                flagMask |= flagValue;
                if (mask == flagValues[flagIndex]) buttonText = flagNames[flagIndex];
                if ((mask & flagValue) == flagValue)
                {
                    intermediateMask |= flagValue;
                    usedFlags.Add(flagIndex);
                }
            }

            if (buttonText == null)
            {
                if (flagMask == intermediateMask) buttonText = everythingName;
                else if (intermediateMask == 0) buttonText = nothingName;
                else
                {
                    buttonText = "Mixed...";
                    var sb = s_Text.Clear();
                    sb.Append(flagNames[usedFlags[0]]);
                    for (int i = 1; i < usedFlags.Count; ++i) sb.Append(", ").Append(flagNames[usedFlags[i]]);
                    buttonMixedValuesText = sb.ToString();
                }
            }
        }

        internal static GUIContent DoMixedLabel(string label, string mixedLabel, Rect rect, GUIStyle style)
        {
            if (mixedLabel == null) return EditorGUIUtility.TempContent(label);
            var content = EditorGUIUtility.TempContent(mixedLabel);
            var size = style.CalcSize(content);
            // A label too wide for the button reverts to the shorter one.
            if (size.x > rect.width) content = EditorGUIUtility.TempContent(label);
            return content;
        }

        internal static void CalculateMaskValues(int mask, int[] flagValues, ref int[] optionMaskValues)
        {
            uint selectedValue = (uint)mask;
            var flagStartIndex = 0;
            if (flagValues[0] == 0) flagStartIndex++;
            if (flagValues.Length > 1 && flagValues[1] == -1) flagStartIndex++;

            if (mask == ~0)
            {
                uint allLayersMask = 0;
                for (var flagIndex = flagStartIndex; flagIndex < flagValues.Length; flagIndex++)
                    allLayersMask |= (uint)flagValues[flagIndex];
                selectedValue = allLayersMask;
            }

            var flagEndIndex = flagStartIndex + optionMaskValues.Length - 2;
            for (var flagIndex = flagStartIndex; flagIndex < flagEndIndex; flagIndex++)
            {
                uint flagValue = (uint)flagValues[flagIndex];
                bool flagSet = (selectedValue & flagValue) == flagValue;
                optionMaskValues[flagIndex - flagStartIndex + 2] = (int)(flagSet ? selectedValue & ~flagValue : selectedValue | flagValue);
            }
        }

        internal static void GetMenuOptions(int mask, string[] flagNames, int[] flagValues,
            out string buttonText, out string buttonMixedValuesText, out string[] optionNames, out int[] optionMaskValues, out int[] selectedOptions, Type enumType = null, bool autoSelectEverything = true)
        {
            const int everythingValue = ~0;
            bool hasNothingName = flagValues[0] == 0;
            bool hasEverythingName = flagValues[flagValues.Length - 1] == everythingValue;
            var nothingName = hasNothingName ? flagNames[0] : "Nothing";
            var everythingName = hasEverythingName ? flagNames[flagValues.Length - 1] : "Everything";
            var optionCount = flagNames.Length + (hasNothingName ? 0 : 1) + (hasEverythingName ? 0 : 1);
            var flagCount = flagNames.Length - (hasNothingName ? 1 : 0) - (hasEverythingName ? 1 : 0);
            var flagStartIndex = hasNothingName ? 1 : 0;
            var flagEndIndex = flagStartIndex + flagCount;

            optionNames = GetBuffer(s_OptionNames, optionCount);
            optionNames[0] = nothingName;
            optionNames[1] = everythingName;
            for (var flagIndex = flagStartIndex; flagIndex < flagEndIndex; flagIndex++)
                optionNames[flagIndex - flagStartIndex + 2] = flagNames[flagIndex];

            var flagMask = 0;
            var intermediateMask = 0;
            s_SelectedOptionsSet.Clear();
            for (var flagIndex = flagStartIndex; flagIndex < flagEndIndex; flagIndex++)
            {
                var flagValue = flagValues[flagIndex];
                flagMask |= flagValue;
                if ((mask & flagValue) == flagValue)
                {
                    s_SelectedOptionsSet.Add(flagIndex - flagStartIndex + 2);
                    intermediateMask |= flagValue;
                }
            }

            buttonText = null;
            buttonMixedValuesText = null;
            for (var flagIndex = flagStartIndex; flagIndex < flagEndIndex; flagIndex++)
                if (mask == flagValues[flagIndex]) buttonText = flagNames[flagIndex];

            if (buttonText == null)
            {
                if (flagMask == intermediateMask && autoSelectEverything)
                {
                    s_SelectedOptionsSet.Add(1);
                    buttonText = everythingName;
                }
                else if (mask == 0 || s_SelectedOptionsSet.Count == 0)
                {
                    s_SelectedOptionsSet.Add(0);
                    buttonText = nothingName;
                }
                else
                {
                    buttonText = "Mixed...";
                    var sb = s_Text.Clear();
                    var iterator = s_SelectedOptionsSet.GetEnumerator();
                    iterator.MoveNext();
                    sb.Append(flagNames[iterator.Current + flagStartIndex - 2]);
                    while (iterator.MoveNext()) sb.Append(", ").Append(flagNames[iterator.Current + flagStartIndex - 2]);
                    buttonMixedValuesText = sb.ToString();
                }
            }

            selectedOptions = GetBuffer(s_SelectedOptions, s_SelectedOptionsSet.Count);
            var x = 0;
            foreach (var selected in s_SelectedOptionsSet) selectedOptions[x++] = selected;

            optionMaskValues = GetBuffer(s_OptionValues, optionCount);
            optionMaskValues[0] = 0;
            optionMaskValues[1] = everythingValue;
            if (EditorGUI.showMixedValue) intermediateMask = 0;
            CalculateMaskValues(intermediateMask, flagValues, ref optionMaskValues);
        }
    }

    /// <summary>The mask field's menu, a popup window's content. Unbound draws no popup windows,
    /// so its options are not shown (said once).</summary>
    internal partial class MaskFieldDropDown : PopupWindowContent
    {
        internal const int m_LayerCount = 32;
        readonly SerializedProperty m_SerializedProperty;
        readonly string[] m_OptionNames;
        readonly int[] m_FlagValues;
        int[] m_OptionMaskValues;
        int[] m_SelectionMaskValues;
        readonly bool m_SingleSelection;
        readonly EditorUtility.SelectMenuItemFunction m_MaskChangeCallback;
        readonly float m_windowSize = 100.0f;

        public MaskFieldDropDown(SerializedProperty property)
        {
            m_SerializedProperty = property;
            m_SingleSelection = false;
        }

        public MaskFieldDropDown(string[] optionNames, int[] optionMaskValues, int mask, EditorUtility.SelectMenuItemFunction maskChangeCallback)
            : this(optionNames, null, optionMaskValues, mask, maskChangeCallback)
        {
        }

        public MaskFieldDropDown(string[] optionNames, int[] flagValues, int[] optionMaskValues, int mask, EditorUtility.SelectMenuItemFunction maskChangeCallback, bool autoSelectEverything = true)
        {
            m_OptionMaskValues = (int[])optionMaskValues.Clone();
            m_OptionNames = (string[])optionNames.Clone();
            m_FlagValues = new int[optionNames.Length];
            m_FlagValues[0] = 0;
            m_FlagValues[1] = -1;
            if (flagValues == null)
            {
                for (int i = 2; i < m_FlagValues.Length; ++i) m_FlagValues[i] = 1 << i;
            }
            else
            {
                int index = 0;
                int length = flagValues.Length;
                if (flagValues[0] == 0)
                {
                    index = 1;
                    length--;
                }
                if (flagValues[flagValues.Length - 1] == ~0) length--;
                Array.Copy(flagValues, index, m_FlagValues, 2, length);
            }
            m_SelectionMaskValues = new[] { mask };
            m_SingleSelection = true;
            m_MaskChangeCallback = maskChangeCallback;
        }

        public void UpdateMaskValues(int mask, int[] optionMaskValues)
        {
            m_OptionMaskValues = (int[])optionMaskValues.Clone();
            m_SelectionMaskValues = new[] { mask };
        }

        public override Vector2 GetWindowSize()
        {
            var rowCount = m_FlagValues == null ? (m_OptionNames?.Length ?? 0) + 2 : m_OptionNames.Length;
            return new Vector2(m_windowSize, (EditorGUI.kSingleLineHeight + 2) * rowCount);
        }

        public override void OnGUI(Rect rect) =>
            Stubs.Ignored("UnityEditor.MaskFieldDropDown", "a mask field's menu draws no options: Unbound draws no popup windows");

        public override void OnOpen()
        {
            if (!m_SingleSelection)
                Stubs.Ignored("UnityEditor.MaskFieldDropDown of a property", "a layer mask property's menu has no options: Unbound draws no popup windows");
        }

        public override void OnClose()
        {
            Event.current?.Use();
            MaskFieldGUI.DestroyMaskCallBackInfo();
            base.OnClose();
        }
    }

    public partial class EditorUtility
    {
        public delegate void SelectMenuItemFunction(object userData, string[] options, int selected);
    }

}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        /// <summary>A layer's name ("" for an unnamed or out-of-range layer).</summary>
        public static string GetLayerName(int layer)
        {
            var layers = Unbound.Runtime.TagManager.Layers;
            return layer >= 0 && layer < layers.Length ? layers[layer] ?? "" : "";
        }

        /// <summary>The named layers as the layer field's menu lists them: "index: name".</summary>
        internal static string[] GetLayersWithId()
        {
            var result = new List<string>();
            var layers = Unbound.Runtime.TagManager.Layers;
            for (int i = 0; i < layers.Length && i < 32; i++)
                if (!string.IsNullOrEmpty(layers[i])) result.Add(i + ": " + layers[i]);
            return result.ToArray();
        }
    }
}
