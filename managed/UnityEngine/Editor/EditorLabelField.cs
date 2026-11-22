using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    public sealed partial class EditorGUI
    {
        static readonly int s_FloatFieldHash = "EditorTextField".GetHashCode();

        public static void LabelField(Rect position, string label) => LabelField(position, GUIContent.Temp(label));

        public static void LabelField(Rect position, GUIContent label)
        {
            RequireGUI();
            if (Compat.InspectorGuiStylesMeasured)
                LabelField(position, GUIContent.none, label, EditorStyles.label);
            else
                EditorGUIControls.Label(position, label);
        }

        public static void LabelField(Rect position, string label, string label2)
        {
            RequireGUI();
            if (Compat.InspectorGuiStylesMeasured)
                LabelField(position, label, label2, EditorStyles.label);
            else
                EditorGUIControls.Label(position, GUIContent.Temp(label + " " + label2));
        }

        public static void LabelField(Rect position, string label, GUIStyle style) =>
            LabelField(position, GUIContent.none, GUIContent.Temp(label), style);

        public static void LabelField(Rect position, GUIContent label, GUIStyle style) =>
            LabelField(position, GUIContent.none, label, style);

        public static void LabelField(Rect position, string label, string label2, GUIStyle style) =>
            LabelField(position, new GUIContent(label), GUIContent.Temp(label2), style);

        public static void LabelField(Rect position, GUIContent label, GUIContent label2) =>
            LabelField(position, label, label2, DefaultPrefixLabelStyle);

        public static void LabelField(Rect position, GUIContent label, GUIContent label2, GUIStyle style)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUI.LabelField");
                return;
            }

            RequireGUI();
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Passive, position);
            Rect field = PrefixLabel(position, id, label);
            if (Event.current.type == EventType.Repaint)
                style.Draw(field, label2, id);
        }
    }

    public sealed partial class EditorGUILayout
    {
        public static void LabelField(string label, params GUILayoutOption[] options) =>
            LabelField(GUIContent.Temp(label), options);

        public static void LabelField(GUIContent label, params GUILayoutOption[] options)
        {
            if (Compat.InspectorGuiStylesMeasured)
                LabelField(GUIContent.none, label, EditorGUI.DefaultPrefixLabelStyle, options);
            else
                EditorGUIControls.Label(Line(options), label);
        }

        public static void LabelField(string label, string label2, params GUILayoutOption[] options)
        {
            if (Compat.InspectorGuiStylesMeasured)
                LabelField(label, label2, EditorGUI.DefaultPrefixLabelStyle, options);
            else
                EditorGUIControls.Label(Line(options), GUIContent.Temp(label + " " + label2));
        }

        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) =>
            LabelField(GUIContent.none, GUIContent.Temp(label), style, options);

        public static void LabelField(GUIContent label, GUIStyle style, params GUILayoutOption[] options) =>
            LabelField(GUIContent.none, label, style, options);

        public static void LabelField(string label, string label2, GUIStyle style, params GUILayoutOption[] options) =>
            LabelField(new GUIContent(label), GUIContent.Temp(label2), style, options);

        public static void LabelField(GUIContent label, GUIContent label2, params GUILayoutOption[] options) =>
            LabelField(label, label2, EditorGUI.DefaultPrefixLabelStyle, options);

        public static void LabelField(GUIContent label, GUIContent label2, GUIStyle style, params GUILayoutOption[] options)
        {
            if (!Compat.InspectorGuiStylesMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.LabelField");
                return;
            }

            EditorGUI.RequireGUI();
            if (!style.wordWrap)
            {
                EditorGUI.LabelField(GetControlRect(true, EditorGUIUtility.singleLineHeight, options), label, label2, style);
                return;
            }

            BeginHorizontal();
            int indent = EditorGUI.indentLevel;
            try
            {
                PrefixLabel(label, style);
                Rect field = GUILayoutUtility.GetRect(label2, style, options);
                EditorGUI.indentLevel = 0;
                EditorGUI.LabelField(field, label2, style);
            }
            finally
            {
                EditorGUI.indentLevel = indent;
                EndHorizontal();
            }
        }
    }
}
