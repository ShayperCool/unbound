using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class ImguiSkinProbe
{
    sealed class PropertyScopeData : ScriptableObject
    {
        [Tooltip("Stored on the field")]
        public int authoredValue = 3;
        public int commonValue = 7;
        public int[] items = { 11, 22 };
    }

    void RecordPropertyScopes(string phase)
    {
        var first = ScriptableObject.CreateInstance<PropertyScopeData>();
        var second = ScriptableObject.CreateInstance<PropertyScopeData>();
        second.authoredValue++;
        bool previousMixed = EditorGUI.showMixedValue;
        bool previousEnabled = GUI.enabled;
        Color previousBackground = GUI.backgroundColor;
        try
        {
            using var both = new SerializedObject(new UnityEngine.Object[] { first, second });
            var mixed = both.FindProperty(nameof(PropertyScopeData.authoredValue));
            var common = both.FindProperty(nameof(PropertyScopeData.commonValue));
            var area = new Rect(5, 60, 140, 18);
            var labels = new List<string>();
            var icon = Texture2D.whiteTexture;
            string Describe(GUIContent label) => label == null ? "null" :
                "[" + (label.text == null ? "null" : Json(label.text)) + "," +
                (label.tooltip == null ? "null" : Json(label.tooltip)) + "," + Flag(label.image == icon) + "]";
            void Label(string name, GUIContent requested)
            {
                var actual = EditorGUI.BeginProperty(area, requested, mixed);
                try
                {
                    labels.Add("[" + Json(name) + "," + Describe(actual) + "," +
                        Flag(ReferenceEquals(requested, actual)) + "," + Describe(requested) + "]");
                }
                finally { EditorGUI.EndProperty(); }
            }

            Label("absent", null);
            Label("inheritTooltip", new GUIContent("Custom", icon));
            Label("givenTooltip", new GUIContent("Custom", "Override"));
            Label("none", GUIContent.none);
            Label("nullText", new GUIContent { text = null });

            using (var outer = new EditorGUI.PropertyScope(area, null, mixed))
            {
                string before = Describe(outer.content);
                using var inner = new EditorGUI.PropertyScope(new Rect(9, 82, 140, 18), null, common);
                labels.Add("[" + Json("nested") + "," + before + "," + Describe(inner.content) + "," +
                    Flag(ReferenceEquals(outer.content, inner.content)) + "," + Describe(outer.content) + "]");
            }
            Put("propertyLabels/" + phase, "[" + string.Join(",", labels) + "]");

            using (var single = new SerializedObject(first))
            {
                var array = single.FindProperty(nameof(PropertyScopeData.items));
                string Element(int index)
                {
                    using var scope = new EditorGUI.PropertyScope(area, null, array.GetArrayElementAtIndex(index));
                    return Json(scope.content.text);
                }
                string before = Element(0);
                array.MoveArrayElement(0, 1);
                Put("propertyArrayLabel/" + phase, "[" + before + "," + Element(1) + "]");
            }

            var state = new List<string>();
            void Observe(string step) => state.Add("[" + Json(step) + "," + Flag(EditorGUI.showMixedValue) +
                "," + Flag(GUI.enabled) + "," + Col(GUI.backgroundColor) + "]");
            GUI.enabled = false;
            GUI.backgroundColor = Color.red;
            EditorGUI.showMixedValue = false;
            using (new EditorGUI.PropertyScope(area, null, mixed))
            {
                Observe("outerMixed");
                var inner = new EditorGUI.PropertyScope(new Rect(9, 82, 140, 18), null, common);
                Observe("innerCommon");
                GUI.enabled = true;
                GUI.backgroundColor = Color.green;
                inner.Dispose();
                Observe("innerDisposed");
                GUI.backgroundColor = Color.blue;
                inner.Dispose();
                Observe("innerDisposedAgain");
            }
            Observe("outerDisposed");

            GUI.enabled = true;
            second.hideFlags = HideFlags.NotEditable;
            using var locked = new SerializedObject(second);
            var lockedProperty = locked.FindProperty(nameof(PropertyScopeData.authoredValue));
            using (new EditorGUI.PropertyScope(area, null, lockedProperty)) Observe("locked");
            Observe("lockedDisposed");
            Put("propertyState/" + phase, "[" + string.Join(",", state) + "]");
        }
        finally
        {
            EditorGUI.showMixedValue = previousMixed;
            GUI.enabled = previousEnabled;
            GUI.backgroundColor = previousBackground;
            DestroyImmediate(first);
            DestroyImmediate(second);
        }
    }

    static string Flag(bool value) => value ? "true" : "false";
}
