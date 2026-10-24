using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// Editor GUI sizing. The private fields keep Unity's names: inspector plugins (Odin) read and
    /// write them by reflection.
    /// </summary>
    public sealed partial class EditorGUIUtility : GUIUtility
    {
        static float s_LabelWidth;
        static float s_FieldWidth;
        static Stack<float> s_ContextWidthStack = new Stack<float>(10);

        public static bool hierarchyMode { get; set; }

        // The Editor's icons ship inside the Editor: contents carry no image.
        public static GUIContent IconContent(string name) => new GUIContent();
        public static GUIContent IconContent(string name, string text) => new GUIContent(text);
        internal static GUIContent IconContent(string name, bool logError) => new GUIContent();
        public static GUIContent TrIconContent(string iconName, string tooltip = null) => new GUIContent(null, null, tooltip);
        public static GUIContent TrIconContent(Texture icon, string tooltip = null) => new GUIContent(null, icon, tooltip);

        /// <summary>A project asset by path (packages load their icons so), else one under
        /// "Assets/Editor Default Resources"; the Editor's own resources ship inside the Editor: null.</summary>
        public static Object Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath(path, typeof(Object))
                ?? AssetDatabase.LoadAssetAtPath("Assets/Editor Default Resources/" + path, typeof(Object));
        }

        /// <summary>The width of the current GUI area: the innermost group's, else the screen's.</summary>
        internal static float contextWidth
        {
            get
            {
                if (s_ContextWidthStack.Count > 0 && s_ContextWidthStack.Peek() > 0f) return s_ContextWidthStack.Peek();
                float width = GUIClip.GetTopRect().width;
                return width < 1f || width >= 40000f ? Screen.width : width;
            }
        }

        public static float labelWidth
        {
            get
            {
                if (s_LabelWidth > 0f) return s_LabelWidth;
                return hierarchyMode ? Mathf.Max(Mathf.Ceil(contextWidth * 0.45f) - 40f, 120f) : 150f;
            }
            set => s_LabelWidth = value;
        }

        public static float fieldWidth
        {
            get => s_FieldWidth > 0f ? s_FieldWidth : 50f;
            set => s_FieldWidth = value;
        }
    }
}
