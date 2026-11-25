using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The view whose GUI runs: in Unbound the Game view, while a GUI pass runs (a tree view keeps
    /// it to ask for focus and repaints). It has focus while the application has; it repaints every
    /// frame, so asking for a repaint does nothing.
    /// </summary>
    internal partial class GUIView
    {
        static GUIView s_GameView;

        static GUIView GameView
        {
            get
            {
                if (s_GameView == null)
                {
                    using (Unbound.Runtime.ObjectRegistry.AsAssets())
                        s_GameView = (GUIView)CreateInstanceWithoutCallbacks(typeof(GUIView));
                    s_GameView.name = "GameView";
                    s_GameView.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_GameView;
            }
        }

        public static GUIView current => Event.current != null ? GameView : null;

        public static GUIView mouseOverView => Application.isFocused ? GameView : null;

        public bool hasFocus => Application.isFocused;

        [Unbound.EditorFaithful("Unbound's one view, the Game view, has the focus while the application has")]
        public void Focus() { }

        [Unbound.EditorFaithful("Unbound repaints the Game view every frame")]
        public void Repaint() { }

        internal void MarkHotRegion(Rect hotRegionRect) { }
    }

    /// <summary>The tree views' named styles: the Editor skin's (the Editor's windows make them
    /// before any game GUI runs), else the current skin's.</summary>
    internal static class TreeViewEditorStyles
    {
        internal static GUIStyle Get(string name) => Unbound.Runtime.EditorGuiSkins.Current.FindStyle(name) ?? GUI.skin.GetStyle(name);
    }

    /// <summary>A drag that starts once the mouse has moved 6 points from where it went down.</summary>
    internal partial class DragAndDropDelay
    {
        public Vector2 mouseDownPosition;

        public bool CanStartDrag() => Vector2.Distance(mouseDownPosition, Event.current.mousePosition) > 6;
    }

    public sealed partial class EditorGUI
    {
        static readonly int s_MouseDeltaReaderHash = "s_MouseDeltaReaderHash".GetHashCode();

        internal static readonly StyleSheets.SVC<float> kWindowToolbarHeight = new StyleSheets.SVC<float>("--window-toolbar-height", 21f);

        internal static EditorGUIUtility.SkinnedColor kSplitLineSkinnedColor = new EditorGUIUtility.SkinnedColor(new Color(0.6f, 0.6f, 0.6f, 1.333f), new Color(0.12f, 0.12f, 0.12f, 1.333f));

        internal static float WidthResizer(Rect position, float width, float minWidth, float maxWidth) =>
            Resizer.Resize(position, width, minWidth, maxWidth, true, out _);

        internal static float WidthResizer(Rect position, float width, float minWidth, float maxWidth, out bool hasControl) =>
            Resizer.Resize(position, width, minWidth, maxWidth, true, out hasControl);

        internal static float HeightResizer(Rect position, float height, float minHeight, float maxHeight) =>
            Resizer.Resize(position, height, minHeight, maxHeight, false, out _);

        internal static float HeightResizer(Rect position, float height, float minHeight, float maxHeight, out bool hasControl) =>
            Resizer.Resize(position, height, minHeight, maxHeight, false, out hasControl);

        static class Resizer
        {
            static float s_StartSize;
            static Vector2 s_MouseDeltaReaderStartPos;

            internal static float Resize(Rect position, float size, float minSize, float maxSize, bool horizontal, out bool hasControl)
            {
                int id = GUIUtility.GetControlID(s_MouseDeltaReaderHash, FocusType.Passive, position);
                Event evt = Event.current;
                switch (evt.GetTypeForControl(id))
                {
                    case EventType.MouseDown:
                        if (GUIUtility.hotControl == 0 && position.Contains(evt.mousePosition) && evt.button == 0)
                        {
                            GUIUtility.hotControl = id;
                            GUIUtility.keyboardControl = 0;
                            // Screen space, unaffected by scroll views.
                            s_MouseDeltaReaderStartPos = GUIClip.Unclip(evt.mousePosition);
                            s_StartSize = size;
                            evt.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id)
                        {
                            evt.Use();
                            Vector2 screenPos = GUIClip.Unclip(evt.mousePosition);
                            float delta = horizontal ? (screenPos - s_MouseDeltaReaderStartPos).x : (screenPos - s_MouseDeltaReaderStartPos).y;
                            float newSize = s_StartSize + delta;
                            if (newSize >= minSize && newSize <= maxSize)
                                size = newSize;
                            else
                                size = Mathf.Clamp(newSize, minSize, maxSize);
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == id && evt.button == 0)
                        {
                            GUIUtility.hotControl = 0;
                            evt.Use();
                        }
                        break;
                    case EventType.Repaint:
                        var cursor = horizontal ? MouseCursor.SplitResizeLeftRight : MouseCursor.SplitResizeUpDown;
                        EditorGUIUtility.AddCursorRect(position, cursor, id);
                        break;
                }
                hasControl = GUIUtility.hotControl == id;
                return size;
            }
        }

        internal static void DrawOutline(Rect rect, float size, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color orgColor = GUI.color;
            GUI.color = GUI.color * color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, size), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - size, rect.width, size), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y + 1, size, rect.height - 2 * size), EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - size, rect.y + 1, size, rect.height - 2 * size), EditorGUIUtility.whiteTexture);
            GUI.color = orgColor;
        }
    }

    public sealed partial class EditorGUIUtility
    {
        /// <summary>A colour for each theme.</summary>
        internal partial class SkinnedColor
        {
            Color normalColor;
            Color proColor;

            public SkinnedColor(Color color, Color proColor)
            {
                normalColor = color;
                this.proColor = proColor;
            }

            public SkinnedColor(Color color)
            {
                normalColor = color;
                proColor = color;
            }

            public Color color
            {
                get => isProSkin ? proColor : normalColor;
                set
                {
                    if (isProSkin) proColor = value;
                    else normalColor = value;
                }
            }

            public static implicit operator Color(SkinnedColor colorSkin) => colorSkin.color;
        }

        internal static bool HasHolddownKeyModifiers(Event evt) => evt.shift | evt.control | evt.alt | evt.command;

        internal static bool renameWasCompleted
        {
            get => EditorGUI.RecycledTextEditor.s_EditingWasCompleted;
            set => EditorGUI.RecycledTextEditor.s_EditingWasCompleted = value;
        }
    }
}

namespace UnityEditor.StyleSheets
{
    /// <summary>A value of the Editor's style catalog (a theme variable): Unbound gives the
    /// default, the value the Editor's themes have for the variables the tree views read.</summary>
    internal partial class SVC<T>
    {
        protected T m_Value;

        public SVC(string property, T defaultValue = default) => m_Value = defaultValue;

        public T value => m_Value;

        public static implicit operator T(SVC<T> sc) => sc.m_Value;
    }
}

namespace UnityEditorInternal
{
    public partial class InternalEditorUtility
    {
        public static List<int> GetNewSelection(int clickedInstanceID, List<int> allInstanceIDs, List<int> selectedInstanceIDs, int lastClickedInstanceID, bool keepMultiSelection, bool useShiftAsActionKey, bool allowMultiSelection) =>
            GetNewSelection(clickedInstanceID, allInstanceIDs, selectedInstanceIDs, lastClickedInstanceID, keepMultiSelection, useShiftAsActionKey, allowMultiSelection, Event.current?.shift ?? false, UnityEditor.EditorGUI.actionKey);

        /// <summary>The selection after a click on an entry: the entry alone; with the action key the
        /// entry toggled; with Shift the range from the selection to the entry (Unity's C# rule for
        /// ids without assets to import).</summary>
        internal static List<int> GetNewSelection(int clickedInstanceID, List<int> allInstanceIDs, List<int> selectedInstanceIDs, int lastClickedInstanceID, bool keepMultiSelection, bool useShiftAsActionKey, bool allowMultiSelection, bool shiftKeyIsDown, bool actionKeyIsDown)
        {
            bool useShift = shiftKeyIsDown || (actionKeyIsDown && useShiftAsActionKey);
            bool useActionKey = actionKeyIsDown && !useShiftAsActionKey;
            if (!allowMultiSelection)
                useShift = useActionKey = false;

            if (useActionKey && !useShift)
            {
                var newSelection = new List<int>(selectedInstanceIDs);
                if (newSelection.Contains(clickedInstanceID))
                {
                    if (!keepMultiSelection) newSelection.Remove(clickedInstanceID);
                }
                else
                    newSelection.Add(clickedInstanceID);
                return newSelection;
            }
            if (useShift)
            {
                if (clickedInstanceID == lastClickedInstanceID)
                    return new List<int>(selectedInstanceIDs);

                if (!GetFirstAndLastSelected(allInstanceIDs, selectedInstanceIDs, out int firstIndex, out int lastIndex))
                    return new List<int>(1) { clickedInstanceID };

                int newIndex = -1;
                int prevIndex = -1;
                for (int i = 0; i < allInstanceIDs.Count; ++i)
                {
                    if (allInstanceIDs[i] == clickedInstanceID) newIndex = i;
                    if (lastClickedInstanceID != 0 && allInstanceIDs[i] == lastClickedInstanceID) prevIndex = i;
                }

                int dir = 0;
                if (prevIndex != -1) dir = newIndex > prevIndex ? 1 : -1;

                int from = 0, to = 0;
                var addExisting = false;
                bool usingArrowKeys = Event.current != null && (Event.current.keyCode == KeyCode.DownArrow || Event.current.keyCode == KeyCode.UpArrow);
                var clickedInTheMiddle = lastIndex > newIndex && firstIndex < newIndex;

                if (selectedInstanceIDs.Count > 1)
                {
                    var clickedID = allInstanceIDs[newIndex];
                    var noGapsInSelection = (allInstanceIDs.Count - firstIndex + selectedInstanceIDs.Count) == allInstanceIDs.Count - lastIndex;
                    var isInSelection = selectedInstanceIDs.Contains(clickedID);
                    if (isInSelection || noGapsInSelection || clickedInTheMiddle)
                    {
                        from = dir > 0 ? firstIndex : newIndex;
                        to = dir > 0 ? newIndex : lastIndex;
                        if (clickedInTheMiddle && !noGapsInSelection && !isInSelection)
                            addExisting = true;
                    }
                    else if (dir > 0)
                    {
                        if (newIndex > lastIndex)
                        {
                            from = lastIndex + 1;
                            to = newIndex;
                            addExisting = true;
                        }
                        else
                        {
                            from = newIndex;
                            to = lastIndex;
                        }
                    }
                    else if (dir < 0)
                    {
                        if (newIndex < firstIndex)
                        {
                            from = newIndex;
                            to = firstIndex - 1;
                            addExisting = true;
                        }
                        else
                        {
                            from = firstIndex;
                            to = newIndex;
                        }
                    }
                }

                if (!addExisting || usingArrowKeys)
                {
                    if (newIndex > lastIndex)
                    {
                        from = firstIndex;
                        to = newIndex;
                    }
                    else if (newIndex >= firstIndex && newIndex < lastIndex)
                    {
                        if (dir > 0)
                        {
                            from = newIndex;
                            to = lastIndex;
                        }
                        else
                        {
                            from = firstIndex;
                            to = newIndex;
                        }
                    }
                    else
                    {
                        from = newIndex;
                        to = lastIndex;
                    }
                }

                var allSelected = new List<int>();
                if (addExisting && !usingArrowKeys)
                {
                    allSelected.AddRange(selectedInstanceIDs);
                    allSelected.AddRange(allInstanceIDs.GetRange(from, to - from + 1));
                    if (clickedInTheMiddle) allSelected = allSelected.Distinct().ToList();
                }
                else
                    allSelected.AddRange(allInstanceIDs.GetRange(from, to - from + 1));
                return allSelected;
            }
            // The clicked entry alone; a click on a selected entry keeps a multi-selection (for a drag
            // or a context click).
            if (keepMultiSelection && selectedInstanceIDs.Contains(clickedInstanceID))
                return new List<int>(selectedInstanceIDs);
            return new List<int>(1) { clickedInstanceID };
        }

        static bool GetFirstAndLastSelected(List<int> allEntries, List<int> selectedInstanceIDs, out int firstIndex, out int lastIndex)
        {
            firstIndex = -1;
            lastIndex = -1;
            for (int i = 0; i < allEntries.Count; ++i)
            {
                if (selectedInstanceIDs.Contains(allEntries[i]))
                {
                    if (firstIndex == -1) firstIndex = i;
                    lastIndex = i;
                }
            }
            return firstIndex != -1 && lastIndex != -1;
        }
    }
}
