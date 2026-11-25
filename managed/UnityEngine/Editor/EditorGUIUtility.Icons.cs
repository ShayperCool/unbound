using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>The Editor's mouse cursors (the release's declaration).</summary>
    public enum MouseCursor
    {
        Arrow = 0,
        Text = 1,
        ResizeVertical = 2,
        ResizeHorizontal = 3,
        Link = 4,
        SlideArrow = 5,
        ResizeUpRight = 6,
        ResizeUpLeft = 7,
        MoveArrow = 8,
        RotateArrow = 9,
        ScaleArrow = 10,
        ArrowPlus = 11,
        ArrowMinus = 12,
        Pan = 13,
        Orbit = 14,
        Zoom = 15,
        FPS = 16,
        CustomCursor = 17,
        SplitResizeUpDown = 18,
        SplitResizeLeftRight = 19,
        NotAllowed = 20,
    }

    public sealed partial class EditorGUIUtility
    {
        static readonly Dictionary<int, Texture2D> s_ObjectIcons = new Dictionary<int, Texture2D>();

        /// <summary>
        /// An object's custom icon as the Unity 6000.0.71f1 Editor keeps it in Play mode
        /// (tests/projects/editorwindowgui icons.*): a GameObject's and a ScriptableObject's are
        /// kept, a Transform's is not (setting one leaves it without an icon), a null icon clears
        /// it; a null object throws ArgumentNullException and a destroyed one
        /// MissingReferenceException, as a [NotNull] binding argument does. Other object kinds
        /// (assets, MonoScripts) are unmeasured and report themselves ignored.
        /// </summary>
        public static void SetIconForObject(Object obj, Texture2D icon)
        {
            if (!Compat.EditorWindowGuiMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUIUtility.SetIconForObject");
                return;
            }
            if (!obj) throw ObjectErrors.Argument(obj, "obj");
            if (obj is Transform) return;
            if (obj is not GameObject && obj is not ScriptableObject)
            {
                Stubs.Ignored("EditorGUIUtility.SetIconForObject", "keeps icons of GameObjects and ScriptableObjects only (other objects, scripts' components among them, are unmeasured)");
                return;
            }
            if (icon == null) s_ObjectIcons.Remove(obj.GetInstanceID());
            else s_ObjectIcons[obj.GetInstanceID()] = icon;
        }

        /// <summary>The icon <see cref="SetIconForObject"/> gave the object (a destroyed icon reads as
        /// null), else none.</summary>
        public static Texture2D GetIconForObject(Object obj)
        {
            if (!Compat.EditorWindowGuiMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUIUtility.GetIconForObject");
                return null;
            }
            if (!obj) throw ObjectErrors.Argument(obj, "obj");
            if (!s_ObjectIcons.TryGetValue(obj.GetInstanceID(), out var icon)) return null;
            if (icon) return icon;
            s_ObjectIcons.Remove(obj.GetInstanceID());
            return null;
        }

        public static void AddCursorRect(Rect position, MouseCursor mouse) => AddCursorRect(position, mouse, 0);

        /// <summary>Unity's C#: a cursor rect is added in a repaint (outside OnGUI there is no current
        /// event, and the call throws NullReferenceException; measured: tests/projects/editorwindowgui
        /// cursor.*), clipped to the topmost clip. Unbound draws no editor view, so the rect
        /// changes no cursor.</summary>
        public static void AddCursorRect(Rect position, MouseCursor mouse, int controlID)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect r = GUIClip.Unclip(position);
            Rect clip = GUIClip.topmostRect;
            Rect clipped = Rect.MinMaxRect(Mathf.Max(r.x, clip.x), Mathf.Max(r.y, clip.y), Mathf.Min(r.xMax, clip.xMax), Mathf.Min(r.yMax, clip.yMax));
            if (clipped.width <= 0 || clipped.height <= 0) return;
            Stubs.Ignored("EditorGUIUtility.AddCursorRect", "changes no cursor (Unbound draws no editor view)");
        }
    }
}
