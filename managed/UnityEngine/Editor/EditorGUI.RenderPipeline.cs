using UnityEditor.Rendering;
using UnityEngine;

namespace UnityEditor
{
    // Unity's rendering layer mask fields (UnityCsReference 6000.0, Editor/Mono/EditorGUI.RenderPipeline.cs
    // and EditorGUILayout.RenderPipeline.cs); measured: tests/projects/editorrenderinglayers.
    public sealed partial class EditorGUI
    {
        static readonly int s_RenderingLayerMaskField = nameof(s_RenderingLayerMaskField).GetHashCode();

        // The forms with the Editor skin's mask field style: only this release family's styles are
        // measured; another family's report themselves and keep the value given, as Popup's do.
        public static void RenderingLayerMaskField(Rect position, string label, SerializedProperty property)
        {
            if (Unmeasured("UnityEditor.EditorGUI.RenderingLayerMaskField")) return;
            RenderingLayerMaskFieldInternal(position, new GUIContent(label), property.uintValue, property, EditorStyles.layerMaskField);
        }

        public static void RenderingLayerMaskField(Rect position, GUIContent label, SerializedProperty property)
        {
            if (Unmeasured("UnityEditor.EditorGUI.RenderingLayerMaskField")) return;
            RenderingLayerMaskFieldInternal(position, label, property.uintValue, property, EditorStyles.layerMaskField);
        }

        public static RenderingLayerMask RenderingLayerMaskField(Rect position, string label, RenderingLayerMask layers) =>
            Unmeasured("UnityEditor.EditorGUI.RenderingLayerMaskField") ? layers : RenderingLayerMaskField(position, label, layers, EditorStyles.layerMaskField);

        public static RenderingLayerMask RenderingLayerMaskField(Rect position, string label, RenderingLayerMask layers, GUIStyle style) =>
            RenderingLayerMaskField(position, new GUIContent(label), layers, style);

        public static RenderingLayerMask RenderingLayerMaskField(Rect position, GUIContent label, RenderingLayerMask layers) =>
            Unmeasured("UnityEditor.EditorGUI.RenderingLayerMaskField") ? layers : RenderingLayerMaskField(position, label, layers, EditorStyles.layerMaskField);

        public static RenderingLayerMask RenderingLayerMaskField(Rect position, GUIContent label, RenderingLayerMask layers, GUIStyle style) =>
            RenderingLayerMaskFieldInternal(position, label, layers, null, style);

        static uint RenderingLayerMaskFieldInternal(Rect position, GUIContent label, uint layers, SerializedProperty property, GUIStyle style)
        {
            var (names, values) = RenderPipelineEditorUtility.GetRenderingLayerNamesAndValuesForMask(layers);
            var bitCount = RenderPipelineEditorUtility.GetActiveMaxRenderingLayers();

            BeginChangeCheck();
            var mixedValue = showMixedValue || property is { hasMultipleDifferentValues: true };
            var newValue = DrawMaskField(position, label, layers, names, values, style, mixedValue, !RenderPipelineEditorUtility.DoesMaskContainRenderingLayersOutsideOfMaxBitCount(layers, bitCount));
            var uintValue = unchecked((uint)newValue);

            if (EndChangeCheck())
            {
                if (uintValue != uint.MaxValue && BitOperationUtils.AreAllBitsSetForValues(uintValue, values, bitCount))
                    uintValue = uint.MaxValue;

                if (property != null)
                    ApplyModifiedProperties(property, uintValue);
            }

            if (RenderPipelineEditorUtility.DoesMaskContainRenderingLayersOutsideOfMaxBitCount(uintValue, bitCount))
                EditorGUILayout.HelpBox(RenderPipelineEditorUtility.GetOutsideOfMaxBitCountWarningMessage(bitCount), MessageType.Warning);

            return uintValue;
        }

        static int DrawMaskField(Rect position, GUIContent label, uint layers, string[] names, int[] values, GUIStyle style, bool mixedValue, bool autoSelectEverything)
        {
            var id = GUIUtility.GetControlID(s_RenderingLayerMaskField, FocusType.Keyboard, position);
            if (label != null)
                position = PrefixLabel(position, id, label);

            using var scope = new MixedValueScope(mixedValue);

            return MaskFieldGUI.DoMaskField(position, id, unchecked((int)layers), names, values, style, autoSelectEverything: autoSelectEverything);
        }

        static void ApplyModifiedProperties(SerializedProperty property, uint uintValue)
        {
            var bits = property.FindPropertyRelative("m_Bits");
            Debug.Assert(bits != null, $"Property for RenderingLayerMask doesn't contain m_Bits. You should use new {nameof(RenderingLayerMask)} type with this drawer.");
            bits.uintValue = uintValue;
            property.serializedObject.ApplyModifiedProperties();
            property.serializedObject.SetIsDifferentCacheDirty();
        }
    }

    // (As Unity's: the forms that forward drop their layout options and, for the string label
    // with a style, the style.)
    public sealed partial class EditorGUILayout
    {
        static bool RenderingLayersUnmeasured() => EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.RenderingLayerMaskField");

        public static uint RenderingLayerMaskField(string label, uint layers, params GUILayoutOption[] options) =>
            RenderingLayersUnmeasured() ? layers : RenderingLayerMaskField(label, layers, EditorStyles.layerMaskField);

        public static uint RenderingLayerMaskField(string label, uint layers, GUIStyle style, params GUILayoutOption[] options) =>
            RenderingLayersUnmeasured() ? layers : RenderingLayerMaskField(new GUIContent(label), layers, style);

        public static uint RenderingLayerMaskField(GUIContent label, uint layers, params GUILayoutOption[] options) =>
            RenderingLayersUnmeasured() ? layers : RenderingLayerMaskField(label, layers, EditorStyles.layerMaskField);

        public static RenderingLayerMask RenderingLayerMaskField(string label, RenderingLayerMask layers, params GUILayoutOption[] options) =>
            RenderingLayersUnmeasured() ? layers : RenderingLayerMaskField(label, layers, EditorStyles.layerMaskField);

        public static RenderingLayerMask RenderingLayerMaskField(string label, RenderingLayerMask layers, GUIStyle style, params GUILayoutOption[] options) =>
            RenderingLayersUnmeasured() ? layers : RenderingLayerMaskField(new GUIContent(label), layers, EditorStyles.layerMaskField);

        public static RenderingLayerMask RenderingLayerMaskField(GUIContent label, RenderingLayerMask layers, params GUILayoutOption[] options) =>
            RenderingLayersUnmeasured() ? layers : RenderingLayerMaskField(label, layers, EditorStyles.layerMaskField);

        // (The layout rect and the default style are the Editor skin's: every layout form is
        // measured per family; another family's take no layout space.)
        public static uint RenderingLayerMaskField(GUIContent label, uint layers, GUIStyle style, params GUILayoutOption[] options)
        {
            if (RenderingLayersUnmeasured()) return layers;
            Rect r = GetControlRect(true, EditorGUI.kSingleLineHeight, options);
            return EditorGUI.RenderingLayerMaskField(r, label, layers, style);
        }

        public static RenderingLayerMask RenderingLayerMaskField(GUIContent label, RenderingLayerMask layers, GUIStyle style, params GUILayoutOption[] options)
        {
            if (RenderingLayersUnmeasured()) return layers;
            var rect = GetControlRect(true, EditorGUI.kSingleLineHeight, options);
            return EditorGUI.RenderingLayerMaskField(rect, label, layers);
        }

        public static void RenderingLayerMaskField(GUIContent label, SerializedProperty property, params GUILayoutOption[] options)
        {
            if (RenderingLayersUnmeasured()) return;
            Rect r = GetControlRect(true, EditorGUI.kSingleLineHeight, options);
            EditorGUI.RenderingLayerMaskField(r, label, property);
        }
    }
}
