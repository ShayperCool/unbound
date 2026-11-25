using System;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;

namespace UnityEditorInternal
{
    /// <summary>
    /// Drawing a list (Unity's C#, ReorderableList.cs of 6000.0): the header, the elements (culled
    /// to a visible rect) and the footer, each by its callback or the default drawing, with the
    /// element rows the list measures (matches the Unity 6000.0.71f1 Editor, tests/projects/
    /// editorwindowgui reorderable.*: rects, callback order, active state, culling, a layout list,
    /// and the styles the game skin lacks logged once when the defaults are first made). Unbound
    /// does not drag, select or reorder on input yet: an input event reports itself ignored.
    /// </summary>
    public partial class ReorderableList
    {
        public delegate void HeaderCallbackDelegate(Rect rect);
        public delegate void FooterCallbackDelegate(Rect rect);
        public delegate void ElementCallbackDelegate(Rect rect, int index, bool isActive, bool isFocused);
        public delegate void DrawNoneElementCallback(Rect rect);

        public HeaderCallbackDelegate drawHeaderCallback;
        public FooterCallbackDelegate drawFooterCallback;
        public ElementCallbackDelegate drawElementCallback;
        public ElementCallbackDelegate drawElementBackgroundCallback;
        public DrawNoneElementCallback drawNoneElementCallback;

        // Set by Unity's property drawing of lists (PropertyField), which Unbound does not use.
        internal bool m_IsEditable;
        internal bool m_HasPropertyDrawer;

        const float ListElementBottomPadding = 4;
        static Defaults s_Defaults;
        Rect m_LastListRect = Rect.zero;

        public static Defaults defaultBehaviours => s_Defaults ??= new Defaults();

        float HeaderHeight => Mathf.Max(m_DisplayHeader ? headerHeight : 0, Defaults.minHeaderHeight);

        // headerHeight is about 3 when the header has no content: then a 1-pixel top margin.
        float ListElementTopPadding => headerHeight > 5 ? 4 : 1;

        static bool UseCulling => GUI.matrix.rotation == Quaternion.identity && GUI.matrix.lossyScale == Vector3.one;

        float ElementYOffset(int index) => index <= 0 || index >= m_CachedElementCount ? 0 : m_ElementEnds[index - 1];

        float ElementRowHeight(int index) => index < 0 || index >= m_CachedElementCount ? 0 : m_ElementEnds[index] - ElementYOffset(index);

        Rect GetContentRect(Rect rect)
        {
            Rect r = rect;
            if (draggable) r.xMin += Defaults.dragHandleWidth;
            else r.xMin += Defaults.padding;
            if (m_HasPropertyDrawer) r.xMin += Defaults.propertyDrawerPadding;
            r.xMax -= Defaults.padding;
            return r;
        }

        public void DoLayoutList()
        {
            if (!Compat.EditorReorderableListLayoutMeasured)
            {
                Stubs.NotImplemented("UnityEditorInternal.ReorderableList.DoLayoutList");
                return;
            }
            GUILayout.BeginVertical();
            Rect headerRect = GUILayoutUtility.GetRect(0, HeaderHeight, GUILayout.ExpandWidth(true));
            Rect listRect = GUILayoutUtility.GetRect(10, GetElementAreaHeight(), GUILayout.ExpandWidth(true));
            Rect footerRect = GUILayoutUtility.GetRect(4, footerHeight, GUILayout.ExpandWidth(true));
            DoListHeader(headerRect);
            DoListElements(listRect, Defaults.infinityRect);
            DoListFooter(footerRect);
            GUILayout.EndVertical();
        }

        public void DoList(Rect rect) => DoList(rect, Defaults.infinityRect);

        public void DoList(Rect rect, Rect visibleRect)
        {
            if (!Compat.EditorReorderableListLayoutMeasured)
            {
                Stubs.NotImplemented("UnityEditorInternal.ReorderableList.DoList");
                return;
            }
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            Rect listRect = new Rect(rect.x, headerRect.y + headerRect.height, rect.width, GetElementAreaHeight());
            Rect footerRect = new Rect(rect.x, listRect.y + listRect.height, rect.width, footerHeight);
            visibleRect.y -= headerRect.height;
            visibleRect.height -= headerRect.height;
            DoListHeader(headerRect);
            DoListElements(listRect, visibleRect);
            DoListFooter(footerRect);
        }

        void DoListElements(Rect listRect, Rect visibleRect)
        {
            // Row heights may change with the width the callback draws into.
            if (drawElementCallback != null && Event.current.type == EventType.Repaint && listRect != m_LastListRect)
            {
                m_LastListRect = listRect;
                InvalidateCache();
            }
            GetElementAreaHeight();

            var prevIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            if (showDefaultBackground && Event.current.type == EventType.Repaint)
                defaultBehaviours.boxBackground.Draw(listRect, false, false, false, false);

            listRect.yMin += ListElementTopPadding;
            listRect.yMax -= ListElementBottomPadding;
            if (showDefaultBackground)
            {
                listRect.xMin += 1;
                listRect.xMax -= 1;
            }

            var elementRect = listRect;
            elementRect.height = elementHeight;
            Rect elementContentRect;
            bool handlingInput = Event.current.type == EventType.MouseDown;
            int elementCount = GetCount(out bool overMultiEditLimit);

            if ((m_SerializedProperty != null && m_SerializedProperty.isArray || list != null) && elementCount > 0 && !overMultiEditLimit)
            {
                EditorGUI.BeginChangeCheck();
                for (int i = 0; i < elementCount; i++)
                {
                    if (UseCulling)
                    {
                        if (visibleRect.y > ElementYOffset(i) + ElementRowHeight(i)) continue;
                        if (visibleRect.y + visibleRect.height < ElementYOffset(i > 0 ? i - 1 : i)) break;
                    }

                    bool activeElement = IsSelected(i);
                    bool focusedElement = activeElement && HasKeyboardControl();
                    elementRect.height = ElementRowHeight(i);
                    elementRect.y = listRect.y + ElementYOffset(i);

                    if (drawElementBackgroundCallback == null)
                        defaultBehaviours.DrawElementBackground(elementRect, i, activeElement, focusedElement, draggable);
                    else
                        drawElementBackgroundCallback(elementRect, i, activeElement, focusedElement);
                    defaultBehaviours.DrawElementDraggingHandle(elementRect, i, activeElement, focusedElement, draggable);

                    elementContentRect = GetContentRect(elementRect);
                    if (drawElementCallback == null)
                    {
                        if (m_SerializedProperty != null)
                            defaultBehaviours.DrawElement(elementContentRect, m_SerializedProperty.GetArrayElementAtIndex(i), null, activeElement, focusedElement, draggable, m_IsEditable);
                        else
                            defaultBehaviours.DrawElement(elementContentRect, null, list[i], activeElement, focusedElement, draggable, m_IsEditable);
                    }
                    else
                        drawElementCallback(elementContentRect, i, activeElement, focusedElement);

                    if (handlingInput && Event.current.type == EventType.Used)
                    {
                        index = i;
                        handlingInput = false;
                    }
                }

                DoDraggingAndSelection();
                if (EditorGUI.EndChangeCheck()) InvalidateCache();
            }
            else
            {
                // No content: one empty row.
                elementRect.y = listRect.y;
                if (drawElementBackgroundCallback == null)
                    defaultBehaviours.DrawElementBackground(elementRect, -1, false, false, false);
                else
                    drawElementBackgroundCallback(elementRect, -1, false, false);
                defaultBehaviours.DrawElementDraggingHandle(elementRect, -1, false, false, false);

                elementContentRect = elementRect;
                elementContentRect.xMin += Defaults.padding;
                elementContentRect.xMax -= Defaults.padding;
                if (drawNoneElementCallback == null)
                {
                    if (overMultiEditLimit)
                    {
                        elementContentRect.height *= 2;
                        defaultBehaviours.DrawOverMaxMultiEditElement(elementContentRect, m_SerializedProperty.serializedObject.maxArraySizeForMultiEditing, draggable);
                    }
                    else
                        defaultBehaviours.DrawNoneElement(elementContentRect, draggable);
                }
                else
                    drawNoneElementCallback(elementContentRect);
            }

            EditorGUI.indentLevel = prevIndent;
        }

        /// <summary>Unity selects, drags and reorders on mouse and key events here.</summary>
        static void DoDraggingAndSelection()
        {
            var type = Event.current.type;
            if (type is EventType.MouseDown or EventType.MouseDrag or EventType.MouseUp or EventType.KeyDown or EventType.DragUpdated or EventType.DragPerform)
                Stubs.Ignored("ReorderableList.DoList", "does not select, drag or reorder on input yet");
        }

        void DoListHeader(Rect headerRect)
        {
            // A whole-list property scope for prefab overrides and the context menu.
            if (m_SerializedProperty != null && m_DisplayHeader)
                EditorGUI.BeginProperty(headerRect, GUIContent.none, m_SerializedProperty);

            if (showDefaultBackground && Event.current.type == EventType.Repaint)
                defaultBehaviours.DrawHeaderBackground(headerRect);

            headerRect.xMin += Defaults.padding;
            headerRect.xMax -= Defaults.padding;
            headerRect.height -= 2;
            headerRect.y += 1;

            if (drawHeaderCallback != null)
                drawHeaderCallback(headerRect);
            else if (m_DisplayHeader)
                defaultBehaviours.DrawHeader(headerRect, m_SerializedProperty?.serializedObject, m_SerializedProperty, list);

            if (m_SerializedProperty != null && m_DisplayHeader)
                EditorGUI.EndProperty();
        }

        void DoListFooter(Rect footerRect)
        {
            if (drawFooterCallback != null)
                drawFooterCallback(footerRect);
            else if (displayAdd || displayRemove)
                defaultBehaviours.DrawFooter(footerRect, this);
        }

        /// <summary>The default drawing and styles of a list (Unity's C#). The styles are looked up by
        /// name in the current skin when the defaults are first made.</summary>
        public partial class Defaults
        {
            public GUIContent iconToolbarPlus = EditorGUIUtility.TrIconContent("Toolbar Plus", "Add to the list");
            public GUIContent iconToolbarPlusMore = EditorGUIUtility.TrIconContent("Toolbar Plus More", "Choose to add to the list");
            public GUIContent iconToolbarMinus = EditorGUIUtility.TrIconContent("Toolbar Minus", "Remove selection from the list");
            public readonly GUIStyle draggingHandle = "RL DragHandle";
            public readonly GUIStyle headerBackground = "RL Header";
            public readonly GUIStyle emptyHeaderBackground = "RL Empty Header";
            public readonly GUIStyle footerBackground = "RL Footer";
            public readonly GUIStyle boxBackground = "RL Background";
            public readonly GUIStyle preButton = "RL FooterButton";
            public readonly GUIStyle elementBackground = "RL Element";
            internal readonly GUIStyle defaultLabel = new GUIStyle(EditorStyles.label);
            public const int padding = 6;
            public const int dragHandleWidth = 20;
            internal const int propertyDrawerPadding = 8;
            internal const int minHeaderHeight = 2;
            const float elementPadding = 2;
            static readonly GUIContent s_ListIsEmpty = EditorGUIUtility.TrTextContent("List is Empty");
            internal static readonly string undoAdd = "Add Element To Array";
            internal static readonly string undoRemove = "Remove Element From Array";
            internal static readonly string undoMove = "Reorder Element In Array";
            internal static readonly Rect infinityRect = new Rect(float.NegativeInfinity, float.NegativeInfinity, float.PositiveInfinity, float.PositiveInfinity);

            internal static float ElementPadding(float height) => height != 0 ? elementPadding : 0;

            public Defaults()
            {
                defaultLabel.wordWrap = true;
                defaultLabel.alignment = TextAnchor.MiddleCenter;
            }

            static int ArrayCountInPropertyPath(SerializedProperty prop) => Regex.Matches(prop.propertyPath, ".Array.data").Count;

            static float FieldLabelSize(Rect r, SerializedProperty prop) => r.width * 0.45f - 20 - ArrayCountInPropertyPath(prop) * 22 + (prop.depth < 2 ? 7 : 0);

            static GUIContent OverMaxMultiEditLimit(int maxMultiEditElementCount) =>
                EditorGUIUtility.TrTextContent($"This field cannot display arrays with more than {maxMultiEditElementCount} elements when multiple objects are selected.");

            public void DrawHeaderBackground(Rect headerRect)
            {
                if (Event.current.type != EventType.Repaint) return;
                // A height under 5 is a header with no content.
                if (headerRect.height < 5f) emptyHeaderBackground.Draw(headerRect, false, false, false, false);
                else headerBackground.Draw(headerRect, false, false, false, false);
            }

            public void DrawHeader(Rect headerRect, SerializedObject serializedObject, SerializedProperty element, System.Collections.IList elementList) =>
                EditorGUI.LabelField(headerRect, new GUIContent(element != null ? "Serialized Property" : "IList"));

            public void DrawElementBackground(Rect rect, int index, bool selected, bool focused, bool draggable)
            {
                if (Event.current.type == EventType.Repaint) elementBackground.Draw(rect, false, selected, selected, focused);
            }

            public void DrawElementDraggingHandle(Rect rect, int index, bool selected, bool focused, bool draggable)
            {
                if (Event.current.type == EventType.Repaint && draggable)
                    draggingHandle.Draw(new Rect(rect.x + 5, rect.y + 8, 10, 6), false, false, false, false);
            }

            public void DrawElement(Rect rect, SerializedProperty element, object listItem, bool selected, bool focused, bool draggable) =>
                DrawElement(rect, element, listItem, selected, focused, draggable, false);

            public void DrawElement(Rect rect, SerializedProperty element, object listItem, bool selected, bool focused, bool draggable, bool editable)
            {
                rect.y += ElementPadding(rect.height) / 2;
                var prop = element ?? listItem as SerializedProperty;
                if (editable)
                {
                    float oldLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = FieldLabelSize(rect, prop);
                    EditorGUI.PropertyField(rect, prop, true);
                    if (Event.current.type == EventType.ContextClick && rect.Contains(Event.current.mousePosition)) Event.current.Use();
                    EditorGUIUtility.labelWidth = oldLabelWidth;
                    return;
                }
                EditorGUI.LabelField(rect, new GUIContent(element?.displayName ?? listItem?.ToString() ?? "null"));
            }

            /// <summary>The default footer's add and remove buttons are not implemented yet.</summary>
            public void DrawFooter(Rect rect, ReorderableList list) => Stubs.NotImplemented("UnityEditorInternal.ReorderableList.Defaults.DrawFooter");

            public void DrawNoneElement(Rect rect, bool draggable) => EditorGUI.LabelField(rect, s_ListIsEmpty);

            public void DrawOverMaxMultiEditElement(Rect rect, int maxMultiEditElementCount, bool draggable) =>
                EditorGUI.LabelField(rect, OverMaxMultiEditLimit(maxMultiEditElementCount), defaultLabel);
        }
    }
}
