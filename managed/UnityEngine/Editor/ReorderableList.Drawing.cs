using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditorInternal
{
    /// <summary>
    /// Drawing a list and its input (Unity's C#, ReorderableList.cs of 6000.0): the header, the
    /// elements (culled to a visible rect, sliding while one is dragged) and the footer, each by
    /// its callback or the default drawing, with the element rows the list measures; selection by
    /// clicks (with the action key and shift on a multi-select list), the arrow keys, Delete,
    /// reordering by dragging, the footer's add and remove buttons and the callbacks. Matches the
    /// Unity 6000.0.71f1 Editor (tests/projects/editorwindowgui reorderable.*: rects, callback
    /// order, active state, culling, a layout list, and the styles the game skin lacks logged once
    /// when the defaults are first made; tests/projects/reorderableinput: the input and the
    /// callbacks). The Editor's inspector scrolling to the selected row or while dragging is not
    /// done (Unbound has no inspector windows).
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

        float GetElementYOffset(int index) => ElementYOffset(index);

        float GetElementYOffset(int index, int skipIndex)
        {
            if (index >= m_CachedElementCount) return 0;
            float skipOffset = skipIndex >= 0 && skipIndex < index ? ElementRowHeight(skipIndex) : 0;
            return ElementYOffset(index) - skipOffset;
        }

        float GetElementHeight(int index) => ElementRowHeight(index);

        Rect GetRowRect(int index, Rect listRect) => new Rect(listRect.x, listRect.y + GetElementYOffset(index), listRect.width, GetElementHeight(index));

        int m_RecursionCounter;

        void DoListElements(Rect listRect, Rect visibleRect)
        {
            // Row heights may change with the width the callback draws into.
            if (drawElementCallback != null && Event.current.type == EventType.Repaint && listRect != m_LastListRect)
            {
                m_LastListRect = listRect;
                InvalidateCacheRecursive();
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
                if (m_Dragging && Event.current.type == EventType.Repaint)
                {
                    // Dragging: the other rows in their places with the gap where the dragged one
                    // would land, sliding there, and the dragged row under the mouse.
                    int targetIndex = CalculateRowIndex(listRect, elementCount);
                    m_NonDragTargetIndices.Clear();
                    for (int i = 0; i < elementCount; i++)
                        if (i != index) m_NonDragTargetIndices.Add(i);
                    m_NonDragTargetIndices.Insert(targetIndex, -1);

                    bool targetSeen = false;
                    for (int i = 0; i < m_NonDragTargetIndices.Count; i++)
                    {
                        int next = Mathf.Min(i + 1, elementCount - 1);
                        int previous = Mathf.Max(i - 1, 0);
                        if (UseCulling)
                        {
                            if (visibleRect.y > GetElementYOffset(next) + GetElementHeight(next)) continue;
                            if (visibleRect.y + visibleRect.height < GetElementYOffset(previous)) break;
                        }

                        int nonDragTargetIndex = m_NonDragTargetIndices[i];
                        if (nonDragTargetIndex != -1)
                        {
                            elementRect.height = GetElementHeight(nonDragTargetIndex);
                            elementRect.y = listRect.y + GetElementYOffset(nonDragTargetIndex, index);
                            if (targetSeen) elementRect.y += GetElementHeight(index);
                            elementRect.y = m_SlideGroup.GetRect(nonDragTargetIndex, elementRect).y;

                            if (drawElementBackgroundCallback == null)
                                defaultBehaviours.DrawElementBackground(elementRect, nonDragTargetIndex, false, false, draggable);
                            else
                                drawElementBackgroundCallback(elementRect, nonDragTargetIndex, false, false);
                            defaultBehaviours.DrawElementDraggingHandle(elementRect, i, false, false, draggable);

                            elementContentRect = GetContentRect(elementRect);
                            if (drawElementCallback == null)
                                DrawDefaultElement(elementContentRect, nonDragTargetIndex, false, false);
                            else
                                drawElementCallback(elementContentRect, nonDragTargetIndex, false, false);
                        }
                        else
                            targetSeen = true;
                    }

                    if (index >= 0)
                    {
                        elementRect.y = GetClampedDragPosition(listRect) - m_DragOffset + listRect.y;
                        elementRect.height = GetElementHeight(index);
                        if (drawElementBackgroundCallback == null)
                            defaultBehaviours.DrawElementBackground(elementRect, index, true, true, draggable);
                        else
                            drawElementBackgroundCallback(elementRect, index, true, true);
                        defaultBehaviours.DrawElementDraggingHandle(elementRect, index, true, true, draggable);

                        elementContentRect = GetContentRect(elementRect);
                        if (drawElementCallback == null)
                            DrawDefaultElement(elementContentRect, index, true, true);
                        else
                            drawElementCallback(elementContentRect, index, true, true);
                    }
                }
                else
                {
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
                            DrawDefaultElement(elementContentRect, i, activeElement, focusedElement);
                        else
                            drawElementCallback(elementContentRect, i, activeElement, focusedElement);

                        if (handlingInput && Event.current.type == EventType.Used)
                        {
                            index = i;
                            handlingInput = false;
                        }
                    }
                }

                DoDraggingAndSelection(listRect, elementCount);
                if (EditorGUI.EndChangeCheck()) InvalidateCacheRecursive();
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

            // Drawn again when a list inside an element changed this one's rows.
            if (CheckForChildInvalidation() && m_RecursionCounter < 2)
            {
                m_RecursionCounter++;
                DoListElements(listRect, visibleRect);
            }
        }

        void DrawDefaultElement(Rect rect, int i, bool active, bool focused)
        {
            if (m_SerializedProperty != null)
                defaultBehaviours.DrawElement(rect, m_SerializedProperty.GetArrayElementAtIndex(i), null, active, focused, draggable, m_IsEditable);
            else
                defaultBehaviours.DrawElement(rect, null, list[i], active, focused, draggable, m_IsEditable);
        }

        /// <summary>Selection, keys, dragging and the callbacks of a mouse or key event (Unity's C#).</summary>
        void DoDraggingAndSelection(Rect listRect, int elementCount)
        {
            Event evt = Event.current;
            int id = m_KeyboardControlId;
            int oldIndex = index;
            bool clicked = false;
            switch (evt.GetTypeForControl(id))
            {
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != id || m_Dragging || clicked)
                        return;
                    if (evt.keyCode == KeyCode.DownArrow)
                    {
                        index += 1;
                        if (index >= elementCount) index = 0;
                        evt.Use();
                    }
                    if (evt.keyCode == KeyCode.UpArrow)
                    {
                        index -= 1;
                        if (index < 0) index = elementCount - 1;
                        evt.Use();
                    }
                    if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow)
                    {
                        if (m_SerializedProperty != null)
                            foreach (var selected in m_Selection)
                                if (selected >= 0) m_SerializedProperty.GetArrayElementAtIndex(selected).isExpanded = evt.keyCode == KeyCode.RightArrow;
                        InvalidateParentCaches(m_PropertyPath);
                        GUI.changed = true;
                        evt.Use();
                    }
                    if (Application.platform != RuntimePlatform.OSXEditor && evt.keyCode == KeyCode.Delete
                        || Application.platform == RuntimePlatform.OSXEditor && evt.keyCode == KeyCode.Backspace && evt.modifiers.HasFlag(EventModifiers.Command))
                    {
                        m_ScheduleRemove = true;
                        InvalidateParentCaches(m_PropertyPath);
                        evt.Use();
                    }
                    if (evt.keyCode == KeyCode.Escape && GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        m_Dragging = false;
                        evt.Use();
                    }
                    if (evt.type == EventType.Used)
                    {
                        // No arrowing past the ends of the list.
                        int size = m_SerializedProperty != null ? m_SerializedProperty.arraySize : list.Count;
                        m_Selection.RemoveAll(i => i < 0 || i >= size);
                    }
                    break;

                case EventType.MouseDown:
                    if (!listRect.Contains(evt.mousePosition) || evt.button > 0)
                        break;
                    // A click on the list ends the text editing of any field.
                    EditorGUI.EndEditingActiveTextField();
                    int selectedRow = GetRowIndex(evt.mousePosition.y - listRect.y, elementCount);
                    if (EditorGUI.actionKey && multiSelect)
                    {
                        if (IsSelected(selectedRow)) Deselect(selectedRow);
                        else Select(selectedRow, true);
                    }
                    else if (evt.shift && multiSelect && m_Selection.Count > 0)
                    {
                        int selectionFromTarget = selectedRow < m_Selection[0] ? m_Selection[m_Selection.Count - 1] : m_Selection[0];
                        SelectRange(selectionFromTarget, selectedRow);
                    }
                    else
                        Select(selectedRow);

                    if (index >= 0 && draggable)
                    {
                        float mousePosInList = evt.mousePosition.y - listRect.y;
                        m_DragOffset = mousePosInList - GetElementYOffset(GetRowIndex(mousePosInList, elementCount));
                        UpdateDraggedY(listRect);
                        GUIUtility.hotControl = id;
                        m_SlideGroup.Reset();
                        m_NonDragTargetIndices = new List<int>();
                    }
                    GrabKeyboardFocus();
                    evt.Use();
                    clicked = true;
                    break;

                case EventType.MouseDrag:
                    if (!draggable || GUIUtility.hotControl != id || evt.modifiers != EventModifiers.None)
                    {
                        m_Dragging = false;
                        break;
                    }
                    // Dragging starts with the first drag event after the hot control is taken.
                    m_Dragging = true;
                    if (m_Selection.Count > 1)
                        index = GetRowIndex(evt.mousePosition.y - listRect.y, elementCount);
                    onMouseDragCallback?.Invoke(this);
                    UpdateDraggedY(listRect);
                    evt.Use();
                    break;

                case EventType.MouseUp:
                    clicked = false;
                    if (!draggable)
                    {
                        // A mouse up on the row the mouse went down on (for renaming, say).
                        if (onMouseUpCallback != null && IsMouseInsideActiveElement(listRect, elementCount))
                            onMouseUpCallback(this);
                        break;
                    }
                    if (GUIUtility.hotControl != id)
                        break;
                    evt.Use();
                    try
                    {
                        int targetIndex = CalculateRowIndex(listRect, elementCount);
                        if (index != targetIndex && m_Dragging)
                        {
                            if (m_SerializedObject != null && m_SerializedProperty != null)
                            {
                                Undo.RegisterCompleteObjectUndo(m_SerializedObject.targetObjects, Defaults.undoMove);
                                m_SerializedProperty.MoveArrayElement(index, targetIndex);
                            }
                            else if (list != null)
                            {
                                // An IList, maybe of a fixed length: shift the elements between.
                                object tempObject = list[index];
                                for (int i = 0; i < list.Count - 1; i++)
                                    if (i >= index) list[i] = list[i + 1];
                                for (int i = list.Count - 1; i > 0; i--)
                                    if (i > targetIndex) list[i] = list[i - 1];
                                list[targetIndex] = tempObject;
                            }

                            int oldActiveElement = index;
                            int newActiveElement = targetIndex;
                            if (m_SerializedObject != null && m_SerializedProperty != null && !isOverMaxMultiEditLimit)
                                EditorGUIUtility.MoveArrayExpandedState(m_SerializedProperty, oldActiveElement, newActiveElement);

                            index = targetIndex;
                            if (onReorderCallbackWithDetails != null)
                                onReorderCallbackWithDetails(this, oldActiveElement, newActiveElement);
                            else
                                onReorderCallback?.Invoke(this);
                            onChangedCallback?.Invoke(this);
                            GUI.changed = true;
                        }
                        else
                            onMouseUpCallback?.Invoke(this);
                    }
                    catch (ObjectDisposedException) { }
                    finally
                    {
                        // A callback may have exited the GUI: clean up still.
                        GUIUtility.hotControl = 0;
                        m_NonDragTargetIndices = null;
                        m_Dragging = false;
                    }
                    break;
            }

            if ((index != oldIndex || clicked) && onSelectCallback != null)
                onSelectCallback(this);
        }

        bool IsMouseInsideActiveElement(Rect listRect, int elementCount)
        {
            int mouseRowIndex = GetRowIndex(Event.current.mousePosition.y - listRect.y, elementCount);
            return IsSelected(mouseRowIndex) && GetRowRect(mouseRowIndex, listRect).Contains(Event.current.mousePosition);
        }

        void UpdateDraggedY(Rect listRect) => m_DraggedY = Event.current.mousePosition.y - listRect.y;

        float GetClampedDragPosition(Rect listRect) =>
            Mathf.Clamp(m_DraggedY, m_DragOffset, listRect.height - GetElementHeight(index) + m_DragOffset);

        int CalculateRowIndex(Rect listRect, int elementCount)
        {
            int rowIndex = GetRowIndex(GetClampedDragPosition(listRect) - m_DragOffset, elementCount, true);
            float activeElementY = m_DraggedY - m_DragOffset;
            float position = activeElementY - GetElementYOffset(index);
            if (position < 0)
            {
                while (rowIndex > 0 && GetElementYOffset(rowIndex - 1) + GetElementHeight(rowIndex - 1) / 2 > activeElementY)
                    rowIndex--;
            }
            return rowIndex;
        }

        /// <summary>The row at a height in the list (the dragged row's destination when
        /// <paramref name="skipActiveElement"/>).</summary>
        int GetRowIndex(float localY, int elementCount, bool skipActiveElement = false)
        {
            for (int i = 0; i < elementCount; i++)
            {
                float levelOffset = GetElementYOffset(i);
                if (skipActiveElement)
                {
                    if (i >= index) levelOffset += -GetElementHeight(index) + GetElementHeight(i) / 2;
                    else levelOffset -= GetElementHeight(i) / 2;
                }
                if (levelOffset > localY) return --i;
            }
            return elementCount - 1;
        }

        void DoListHeader(Rect headerRect)
        {
            m_RecursionCounter = 0;
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

            /// <summary>The default footer: the add button (its drop-down form with an add drop-down
            /// callback) and the remove button, each disabled when its can-callback refuses, the remove
            /// one also without a selected row; Delete on the focused list presses remove.</summary>
            public void DrawFooter(Rect rect, ReorderableList list)
            {
                float rightEdge = rect.xMax - 10f;
                float leftEdge = rightEdge - 8f;
                if (list.displayAdd) leftEdge -= 25;
                if (list.displayRemove) leftEdge -= 25;
                rect = new Rect(leftEdge, rect.y, rightEdge - leftEdge, rect.height);
                Rect addRect = new Rect(leftEdge + 4, rect.y, 25, 16);
                Rect removeRect = new Rect(rightEdge - 29, rect.y, 25, 16);
                if (Event.current.type == EventType.Repaint)
                    footerBackground.Draw(rect, false, false, false, false);
                if (list.displayAdd)
                {
                    using (new EditorGUI.DisabledScope(list.onCanAddCallback != null && !list.onCanAddCallback(list) || list.isOverMaxMultiEditLimit))
                    {
                        if (GUI.Button(addRect, list.onAddDropdownCallback != null ? iconToolbarPlusMore : iconToolbarPlus, preButton))
                        {
                            if (list.onAddDropdownCallback != null)
                                list.onAddDropdownCallback(addRect, list);
                            else if (list.onAddCallback != null)
                                list.onAddCallback(list);
                            else
                                DoAddButton(list);
                            list.onChangedCallback?.Invoke(list);
                            list.InvalidateCacheRecursive();
                        }
                    }
                }
                if (list.displayRemove)
                {
                    using (new EditorGUI.DisabledScope(list.index < 0 || list.index >= list.count
                        || list.onCanRemoveCallback != null && !list.onCanRemoveCallback(list)
                        || list.isOverMaxMultiEditLimit))
                    {
                        if (GUI.Button(removeRect, iconToolbarMinus, preButton) || GUI.enabled && list.m_ScheduleRemove)
                        {
                            if (list.onRemoveCallback == null) DoRemoveButton(list);
                            else list.onRemoveCallback(list);
                            list.onChangedCallback?.Invoke(list);
                            list.InvalidateCacheRecursive();
                            GUI.changed = true;
                        }
                    }
                }
                list.m_ScheduleRemove = false;
            }

            /// <summary>The default add: an element at the end, selected (a serialized array's
            /// copies the last; an IList's is a new instance of its element type, "" for strings).</summary>
            internal void DoAddButton(ReorderableList list, Object value)
            {
                if (GUIUtility.keyboardControl != list.m_KeyboardControlId) list.GrabKeyboardFocus();
                if (list.serializedProperty != null)
                {
                    list.serializedProperty = list.serializedProperty.serializedObject.FindProperty(list.m_PropertyPath);
                    list.serializedProperty.arraySize = list.count + 1;
                    list.index = list.serializedProperty.arraySize - 1;
                    if (value != null)
                        list.serializedProperty.GetArrayElementAtIndex(list.index).objectReferenceValue = value;
                }
                else
                {
                    Type listType = list.list.GetType();
                    Type elementType = listType.IsGenericType ? listType.GetTypeInfo().GenericTypeArguments[0] : listType.GetElementType();
                    if (value != null)
                        list.index = list.list.Add(value);
                    else if (elementType == typeof(string))
                        list.index = list.list.Add("");
                    else if (list.list.GetType().GetGenericArguments()[0] != null)
                        list.index = list.list.Add(Activator.CreateInstance(list.list.GetType().GetGenericArguments()[0]));
                    else if (elementType != null && elementType.GetConstructor(Type.EmptyTypes) == null)
                        Debug.LogError("Cannot add element. Type " + elementType + " has no default constructor. Implement a default constructor or implement your own add behaviour.");
                    else if (elementType != null)
                        list.index = list.list.Add(Activator.CreateInstance(elementType));
                    else
                        Debug.LogError("Cannot add element of type Null.");
                }
                Undo.SetCurrentGroupName(undoAdd);
                list.InvalidateForGUI();
            }

            public void DoAddButton(ReorderableList list) => DoAddButton(list, null);

            /// <summary>The default remove: the selected elements (an IList's: the active one for each),
            /// then the row above the last removed one selected.</summary>
            public void DoRemoveButton(ReorderableList list)
            {
                if (GUIUtility.keyboardControl != list.m_KeyboardControlId) list.GrabKeyboardFocus();
                int[] deleteIndexes = list.m_Selection.Count > 0 ? list.selectedIndices.Reverse<int>().ToArray() : new[] { list.index };
                int lastDeletedIndex = -1;
                // In reverse, so the indices stay valid.
                foreach (var index in deleteIndexes)
                {
                    if (index >= list.count) continue;
                    if (list.serializedProperty != null)
                    {
                        list.serializedProperty.DeleteArrayElementAtIndex(index);
                        if (index < list.count - 1)
                        {
                            SerializedProperty currentProperty = list.serializedProperty.GetArrayElementAtIndex(index);
                            for (int i = index + 1; i < list.count; i++)
                            {
                                SerializedProperty nextProperty = list.serializedProperty.GetArrayElementAtIndex(i);
                                currentProperty.isExpanded = nextProperty.isExpanded;
                                currentProperty = nextProperty;
                            }
                        }
                    }
                    else
                        list.list.RemoveAt(list.index);
                    lastDeletedIndex = index;
                }
                list.index = Mathf.Clamp(lastDeletedIndex - 1, 0, list.count - 1);
                Undo.SetCurrentGroupName(undoRemove);
                list.InvalidateForGUI();
            }

            public void DrawNoneElement(Rect rect, bool draggable) => EditorGUI.LabelField(rect, s_ListIsEmpty);

            public void DrawOverMaxMultiEditElement(Rect rect, int maxMultiEditElementCount, bool draggable) =>
                EditorGUI.LabelField(rect, OverMaxMultiEditLimit(maxMultiEditElementCount), defaultLabel);
        }
    }
}

namespace UnityEditor
{
    /// <summary>Rects that slide to their new places over several repaints (Unity's C#): a
    /// ReorderableList's rows while one is dragged.</summary>
    internal partial class GUISlideGroup
    {
        internal static GUISlideGroup current;
        readonly Dictionary<int, Rect> animIDs = new Dictionary<int, Rect>();
        const float kLerp = .1f;
        const float kSnap = .5f;

        public GUISlideGroup() { }

        public void Begin()
        {
            if (current != null)
            {
                Debug.LogError("You cannot nest animGroups");
                return;
            }
            current = this;
        }

        public void End() => current = null;

        public void Reset()
        {
            current = null;
            animIDs.Clear();
        }

        public Rect GetRect(int id, Rect r) => Event.current.type != EventType.Repaint ? r : GetRect(id, r, out _);

        Rect GetRect(int id, Rect r, out bool changed)
        {
            if (!animIDs.TryGetValue(id, out var current))
            {
                animIDs.Add(id, r);
                changed = false;
                return r;
            }
            if (current.y != r.y || current.height != r.height || current.x != r.x || current.width != r.width)
            {
                if (Mathf.Abs(current.y - r.y) > kSnap) r.y = Mathf.Lerp(current.y, r.y, kLerp);
                if (Mathf.Abs(current.height - r.height) > kSnap) r.height = Mathf.Lerp(current.height, r.height, kLerp);
                if (Mathf.Abs(current.x - r.x) > kSnap) r.x = Mathf.Lerp(current.x, r.x, kLerp);
                if (Mathf.Abs(current.width - r.width) > kSnap) r.width = Mathf.Lerp(current.width, r.width, kLerp);
                animIDs[id] = r;
                changed = true;
                HandleUtility.Repaint();
            }
            else
                changed = false;
            return r;
        }
    }

    public sealed partial class EditorGUI
    {
        /// <summary>Ends the text editing of the field being edited, if any.</summary>
        internal static void EndEditingActiveTextField() => activeEditor?.EndEditing();
    }
}
