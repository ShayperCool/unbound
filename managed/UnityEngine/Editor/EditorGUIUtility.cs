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

        /// <summary>The dark (Pro) skin is the Editor's: the user's choice in its preferences
        /// (<c>UserSkin</c>), dark when never chosen.</summary>
        public static bool isProSkin => EditorPrefs.GetInt("UserSkin", 1) == 1;

        public new static float pixelsPerPoint => GUIUtility.pixelsPerPoint;

        static readonly Dictionary<string, GUIContent> s_TrContents = new Dictionary<string, GUIContent>();

        public static GUIContent TrTextContent(string key, string text, string tooltip, Texture icon)
        {
            if (!s_TrContents.TryGetValue(key, out var content))
            {
                content = new GUIContent(L10n.Tr(text));
                if (tooltip != null) content.tooltip = L10n.Tr(tooltip);
                if (icon != null) content.image = icon;
                s_TrContents[key] = content;
            }
            return content;
        }

        public static GUIContent TrTextContent(string text, string tooltip = null, Texture icon = null) =>
            TrTextContent($"{text ?? ""}|{tooltip ?? ""}", text, tooltip, icon);

        // The Editor's icons ship inside the Editor: named icons load as none.
        public static GUIContent TrTextContent(string text, string tooltip, string iconName) => TrTextContent(text, tooltip, (Texture)null);

        public static GUIContent TrTextContent(string text, Texture icon) => TrTextContent(text, null, icon);

        public static GUIContent TrTextContentWithIcon(string text, Texture icon) => TrTextContent(text, null, icon);

        public static GUIContent TrTextContentWithIcon(string text, string iconName) => TrTextContent(text, null, (Texture)null);

        public static GUIContent TrTextContentWithIcon(string text, string tooltip, string iconName) => TrTextContent(text, tooltip, (Texture)null);

        public static GUIContent TrTextContentWithIcon(string text, string tooltip, Texture icon) => TrTextContent(text, tooltip, icon);

        /// <summary>A temporary content: its text, no tooltip (null, measured).</summary>
        public static GUIContent TrTempContent(string t) => new GUIContent(L10n.Tr(t)) { tooltip = null };

        public static GUIContent[] TrTempContent(string[] texts)
        {
            var result = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++) result[i] = new GUIContent(L10n.Tr(texts[i]));
            return result;
        }

        public static GUIContent[] TrTempContent(string[] texts, string[] tooltips)
        {
            var result = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++) result[i] = new GUIContent(L10n.Tr(texts[i]), L10n.Tr(tooltips[i]));
            return result;
        }

        // The Editor's icons ship inside the Editor: contents carry no image.
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
