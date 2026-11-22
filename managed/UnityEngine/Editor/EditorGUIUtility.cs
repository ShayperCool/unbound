using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>The built-in GUI skins EditorGUIUtility.GetBuiltinSkin names.</summary>
    public enum EditorSkin
    {
        Game = 0,
        Inspector = 1,
        Scene = 2,
    }

    /// <summary>
    /// Editor GUI sizing. The private fields keep Unity's names: inspector plugins (Odin) read and
    /// write them by reflection.
    /// </summary>
    public sealed partial class EditorGUIUtility : GUIUtility
    {
        static float s_LabelWidth;
        static float s_FieldWidth;
        static Stack<float> s_ContextWidthStack = new Stack<float>(10);

        internal static Object GetBuiltinExtraResource(System.Type type, string path) => Unbound.Runtime.BuiltinResources.GetExtra(type, path);

        public static bool hierarchyMode { get; set; }

        /// <summary>The dark (Pro) skin is the Editor's: the user's choice in its preferences
        /// (<c>UserSkin</c>), dark when never chosen.</summary>
        public static bool isProSkin => EditorPrefs.GetInt("UserSkin", 1) == 1;

        /// <summary>A built-in skin: the game's, the Editor's light one (Inspector) or its dark
        /// one (Scene) (matches the Unity 6000.0.71f1 Editor with the dark theme,
        /// tests/projects/editorskin skin/*/name; not measured with the light theme).</summary>
        public static GUISkin GetBuiltinSkin(EditorSkin skin) => GUIUtility.GetBuiltinSkin((int)skin);

        public new static float pixelsPerPoint => GUIUtility.pixelsPerPoint;

        public static Rect PointsToPixels(Rect rect)
        {
            float ppp = pixelsPerPoint;
            rect.x *= ppp;
            rect.y *= ppp;
            rect.width *= ppp;
            rect.height *= ppp;
            return rect;
        }

        public static Vector2 PointsToPixels(Vector2 position)
        {
            float ppp = pixelsPerPoint;
            position.x *= ppp;
            position.y *= ppp;
            return position;
        }

        public static Rect PixelsToPoints(Rect rect)
        {
            float inv = 1f / pixelsPerPoint;
            rect.x *= inv;
            rect.y *= inv;
            rect.width *= inv;
            rect.height *= inv;
            return rect;
        }

        public static Vector2 PixelsToPoints(Vector2 position)
        {
            float inv = 1f / pixelsPerPoint;
            position.x *= inv;
            position.y *= inv;
            return position;
        }

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
        /// "Assets/Editor Default Resources", else a resource of the Editor itself by its path
        /// there (the documented order of the last two; fonts, skins, style sheets, UXML and
        /// textures in uncompressed formats load, tests/projects/editorskin font/*). A resource
        /// the Editor finds by its bare name (its icons) is not looked up: null.</summary>
        public static Object Load(string path)
        {
            if (path == null) throw new System.NullReferenceException("Object reference not set to an instance of an object");
            if (path.Length == 0) throw new System.ArgumentException("The input asset path cannot be empty.", nameof(path));
            return AssetDatabase.LoadAssetAtPath(path, typeof(Object))
                ?? AssetDatabase.LoadAssetAtPath("Assets/Editor Default Resources/" + path, typeof(Object))
                ?? Unbound.UIElements.UiResources.EditorLoad(path);
        }

        /// <summary>Loads an editor resource via <see cref="Load(string)"/> and logs an error when missing.</summary>
        public static Object LoadRequired(string path)
        {
            var obj = Load(path);
            if (obj == null) Debug.LogError("Unable to find required resource at " + path);
            return obj;
        }

        /// <summary>Highlights an object in the Hierarchy and Project windows that show it. The
        /// Editor answers with no exception and no log (tests/projects/editorlayout
        /// util/pingObject); Unbound has no such window to highlight in.</summary>
        public static void PingObject(Object obj)
        {
            if (obj != null) PingObject(obj.GetInstanceID());
        }

        [Unbound.EditorFaithful("highlights the object in the Hierarchy and Project windows, which Unbound does not have; no exception and no log, as in the Editor (tests/projects/editorlayout util/pingObject)")]
        public static void PingObject(int targetInstanceID) { }

        internal static float CalcContextWidth()
        {
            float width = GUIClip.GetTopRect().width;
            if (width < 1f || width >= 40000f)
                return s_CurrentViewWidth > 0f ? s_CurrentViewWidth : Screen.width;
            return width;
        }

        internal static void LockContextWidth() => s_ContextWidthStack.Push(CalcContextWidth());

        internal static void UnlockContextWidth()
        {
            if (s_ContextWidthStack.Count > 0) s_ContextWidthStack.Pop();
        }

        /// <summary>The width of the current GUI area: the innermost group's, else the screen's.</summary>
        internal static float contextWidth
        {
            get
            {
                if (s_ContextWidthStack.Count > 0 && s_ContextWidthStack.Peek() > 0f) return s_ContextWidthStack.Peek();
                return CalcContextWidth();
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
