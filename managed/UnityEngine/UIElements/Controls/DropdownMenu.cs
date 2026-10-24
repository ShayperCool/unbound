using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    public static partial class AlignmentUtils
    {
        internal static float RoundToPixelGrid(float v, float pixelsPerPoint, float offset = 0.02f) => Mathf.Floor(v * pixelsPerPoint + 0.5f + offset) / pixelsPerPoint;

        internal static float CeilToPixelGrid(float v, float pixelsPerPoint, float offset = -0.02f) => Mathf.Ceil(v * pixelsPerPoint + offset) / pixelsPerPoint;

        internal static float FloorToPixelGrid(float v, float pixelsPerPoint, float offset = 0.02f) => Mathf.Floor(v * pixelsPerPoint + offset) / pixelsPerPoint;

        public static float RoundToPanelPixelSize(this VisualElement ve, float v) => RoundToPixelGrid(v, ve.scaledPixelsPerPoint);

        public static float CeilToPanelPixelSize(this VisualElement ve, float v) => CeilToPixelGrid(v, ve.scaledPixelsPerPoint);

        public static float FloorToPanelPixelSize(this VisualElement ve, float v) => FloorToPixelGrid(v, ve.scaledPixelsPerPoint);
    }

    /// <summary>Keyboard and navigation events mapped to list-like operations (next, previous, submit...).</summary>
    public partial class KeyboardNavigationManipulator : Manipulator
    {
        readonly Action<KeyboardNavigationOperation, EventBase> m_Action;

        public KeyboardNavigationManipulator(Action<KeyboardNavigationOperation, EventBase> action)
        {
            m_Action = action;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            target.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
            target.RegisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove);
            target.UnregisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
            target.UnregisterCallback<NavigationCancelEvent>(OnNavigationCancel);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        internal void OnKeyDown(KeyDownEvent evt)
        {
            var op = KeyboardNavigationOperation.None;
            switch (evt.keyCode)
            {
                case KeyCode.A:
                    if (evt.actionKey) op = KeyboardNavigationOperation.SelectAll;
                    break;
                case KeyCode.Home:
                    op = KeyboardNavigationOperation.Begin;
                    break;
                case KeyCode.End:
                    op = KeyboardNavigationOperation.End;
                    break;
                case KeyCode.PageUp:
                    op = KeyboardNavigationOperation.PageUp;
                    break;
                case KeyCode.PageDown:
                    op = KeyboardNavigationOperation.PageDown;
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.RightArrow:
                case KeyCode.LeftArrow:
                    // Arrows navigate through the navigation events the key down sends.
                    evt.StopPropagation();
                    break;
            }
            if (op != KeyboardNavigationOperation.None) m_Action?.Invoke(op, evt);
        }

        void OnNavigationCancel(NavigationCancelEvent evt) => m_Action?.Invoke(KeyboardNavigationOperation.Cancel, evt);

        void OnNavigationSubmit(NavigationSubmitEvent evt) => m_Action?.Invoke(KeyboardNavigationOperation.Submit, evt);

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            switch (evt.direction)
            {
                case NavigationMoveEvent.Direction.Up: m_Action?.Invoke(KeyboardNavigationOperation.Previous, evt); break;
                case NavigationMoveEvent.Direction.Down: m_Action?.Invoke(KeyboardNavigationOperation.Next, evt); break;
                case NavigationMoveEvent.Direction.Left: m_Action?.Invoke(KeyboardNavigationOperation.MoveLeft, evt); break;
                case NavigationMoveEvent.Direction.Right: m_Action?.Invoke(KeyboardNavigationOperation.MoveRight, evt); break;
            }
        }
    }

    /// <summary>The runtime drop-down menu: items in a scroll view placed under (or above) the target,
    /// in a full-panel container on the panel's root visual container.</summary>
    public partial class GenericDropdownMenu : IGenericMenu
    {
        internal partial class MenuItem
        {
            public string name;
            public VisualElement element;
            public Action action;
            public Action<object> actionUserData;
        }

        public static readonly string ussClassName = "unity-base-dropdown";
        public static readonly string itemUssClassName = ussClassName + "__item";
        public static readonly string itemContentUssClassName = ussClassName + "__item-content";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string containerInnerUssClassName = ussClassName + "__container-inner";
        public static readonly string containerOuterUssClassName = ussClassName + "__container-outer";
        public static readonly string checkmarkUssClassName = ussClassName + "__checkmark";
        public static readonly string separatorUssClassName = ussClassName + "__separator";
        public static readonly string contentWidthUssClassName = ussClassName + "--content-width-menu";
        const float k_MenuItemPadding = 20f;
        const float k_MenuPadding = 2f;

        readonly List<MenuItem> m_Items = new List<MenuItem>();
        readonly VisualElement m_MenuContainer;
        readonly VisualElement m_OuterContainer;
        readonly ScrollView m_ScrollView;
        VisualElement m_PanelRootVisualContainer;
        VisualElement m_TargetElement;
        Rect m_DesiredRect;
        KeyboardNavigationManipulator m_NavigationManipulator;
        float m_PositionTop;
        float m_PositionLeft;
        float m_ContentWidth;
        bool m_FitContentWidth;
        bool m_ShownAboveTarget;
        Vector2 m_MousePosition;

        internal List<MenuItem> items => m_Items;
        internal VisualElement menuContainer => m_MenuContainer;
        internal VisualElement outerContainer => m_OuterContainer;
        internal ScrollView scrollView => m_ScrollView;
        internal bool isSingleSelectionDropdown { get; set; }
        internal bool closeOnParentResize { get; set; }

        public VisualElement contentContainer => m_ScrollView.contentContainer;

        public GenericDropdownMenu()
        {
            m_MenuContainer = new VisualElement();
            m_MenuContainer.AddToClassList(ussClassName);
            m_OuterContainer = new VisualElement();
            m_OuterContainer.AddToClassList(containerOuterUssClassName);
            m_MenuContainer.Add(m_OuterContainer);
            m_ScrollView = new ScrollView();
            m_ScrollView.AddToClassList(containerInnerUssClassName);
            m_ScrollView.pickingMode = PickingMode.Position;
            m_ScrollView.contentContainer.focusable = true;
            m_ScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            m_ScrollView.mode = ScrollViewMode.VerticalAndHorizontal;
            m_OuterContainer.hierarchy.Add(m_ScrollView);
            m_MenuContainer.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_MenuContainer.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            isSingleSelectionDropdown = true;
            closeOnParentResize = true;
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (evt.destinationPanel == null) return;
            contentContainer.AddManipulator(m_NavigationManipulator = new KeyboardNavigationManipulator(Apply));
            m_MenuContainer.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_MenuContainer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_MenuContainer.RegisterCallback<PointerUpEvent>(OnPointerUp);
            evt.destinationPanel.visualTree.RegisterCallback<GeometryChangedEvent>(OnParentResized);
            m_ScrollView.UaRegisterCallback<GeometryChangedEvent>(OnInitialDisplay, UaInvokePolicy.Once);
            m_ScrollView.RegisterCallback<GeometryChangedEvent>(OnContainerGeometryChanged);
            m_ScrollView.RegisterCallback<FocusOutEvent>(OnFocusOut);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (evt.originPanel == null) return;
            contentContainer.RemoveManipulator(m_NavigationManipulator);
            m_MenuContainer.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_MenuContainer.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            m_MenuContainer.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            evt.originPanel.visualTree.UnregisterCallback<GeometryChangedEvent>(OnParentResized);
            m_ScrollView.UnregisterCallback<GeometryChangedEvent>(OnContainerGeometryChanged);
            m_ScrollView.UnregisterCallback<FocusOutEvent>(OnFocusOut);
        }

        void Hide(bool giveFocusBack = false)
        {
            m_MenuContainer.RemoveFromHierarchy();
            if (m_TargetElement != null)
            {
                m_TargetElement.UnregisterCallback<DetachFromPanelEvent>(OnTargetElementDetachFromPanel);
                m_TargetElement.pseudoStates ^= PseudoStates.Active;
                if (giveFocusBack && m_TargetElement.canGrabFocus) m_TargetElement.Focus();
            }
            m_TargetElement = null;
        }

        void Apply(KeyboardNavigationOperation op, EventBase sourceEvent)
        {
            if (Apply(op)) sourceEvent.StopPropagation();
        }

        bool Apply(KeyboardNavigationOperation op)
        {
            int selected = GetSelectedIndex();
            switch (op)
            {
                case KeyboardNavigationOperation.Cancel:
                    Hide(true);
                    return true;
                case KeyboardNavigationOperation.Submit:
                {
                    var item = selected != -1 ? m_Items[selected] : null;
                    if (selected >= 0 && item.element.enabledSelf)
                    {
                        item.action?.Invoke();
                        item.actionUserData?.Invoke(item.element.userData);
                    }
                    Hide(true);
                    return true;
                }
                case KeyboardNavigationOperation.Previous:
                    SelectUpFrom(selected < 0 ? m_Items.Count - 1 : selected - 1, selected);
                    return true;
                case KeyboardNavigationOperation.Next:
                    SelectDownFrom(selected + 1, selected);
                    return true;
                case KeyboardNavigationOperation.PageUp:
                case KeyboardNavigationOperation.Begin:
                    SelectDownFrom(0, selected);
                    return true;
                case KeyboardNavigationOperation.PageDown:
                case KeyboardNavigationOperation.End:
                    SelectUpFrom(m_Items.Count - 1, selected);
                    return true;
                default:
                    return false;
            }
        }

        void SelectDownFrom(int index, int selected)
        {
            for (; index < m_Items.Count; index++)
            {
                if (!m_Items[index].element.enabledSelf) continue;
                ChangeSelectedIndex(index, selected);
                return;
            }
        }

        void SelectUpFrom(int index, int selected)
        {
            for (; index >= 0; index--)
            {
                if (!m_Items[index].element.enabledSelf) continue;
                ChangeSelectedIndex(index, selected);
                return;
            }
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            m_MousePosition = m_ScrollView.WorldToLocal((Vector2)evt.position);
            UpdateSelection(evt.elementTarget);
            if (evt.pointerId != PointerId.mousePointerId) m_MenuContainer.elementPanel?.PreventCompatibilityMouseEvents(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            m_MousePosition = m_ScrollView.WorldToLocal((Vector2)evt.position);
            UpdateSelection(evt.elementTarget);
            if (evt.pointerId != PointerId.mousePointerId) m_MenuContainer.elementPanel?.PreventCompatibilityMouseEvents(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            int selected = GetSelectedIndex();
            if (selected != -1)
            {
                var item = m_Items[selected];
                item.action?.Invoke();
                item.actionUserData?.Invoke(item.element.userData);
                if (isSingleSelectionDropdown) Hide(true);
            }
            if (evt.pointerId != PointerId.mousePointerId) m_MenuContainer.elementPanel?.PreventCompatibilityMouseEvents(evt.pointerId);
            evt.StopPropagation();
        }

        void OnFocusOut(FocusOutEvent evt)
        {
            if (!m_ScrollView.ContainsPoint(m_MousePosition)) Hide();
            else m_MenuContainer.schedule.Execute(contentContainer.Focus);
        }

        void OnParentResized(GeometryChangedEvent evt)
        {
            if (closeOnParentResize) Hide(true);
        }

        void UpdateSelection(VisualElement target)
        {
            if (!m_ScrollView.ContainsPoint(m_MousePosition))
            {
                int selected = GetSelectedIndex();
                if (selected >= 0) m_Items[selected].element.pseudoStates &= ~PseudoStates.Hover;
            }
            else if (target != null && (target.pseudoStates & PseudoStates.Hover) != PseudoStates.Hover)
            {
                int selected = GetSelectedIndex();
                if (selected >= 0) m_Items[selected].element.pseudoStates &= ~PseudoStates.Hover;
                target.pseudoStates |= PseudoStates.Hover;
            }
        }

        void ChangeSelectedIndex(int newIndex, int previousIndex)
        {
            if (previousIndex >= 0 && previousIndex < m_Items.Count) m_Items[previousIndex].element.pseudoStates &= ~PseudoStates.Hover;
            if (newIndex < 0 || newIndex >= m_Items.Count) return;
            m_Items[newIndex].element.pseudoStates |= PseudoStates.Hover;
            m_ScrollView.ScrollTo(m_Items[newIndex].element);
        }

        int GetSelectedIndex()
        {
            for (int i = 0; i < m_Items.Count; i++)
                if ((m_Items[i].element.pseudoStates & PseudoStates.Hover) == PseudoStates.Hover) return i;
            return -1;
        }

        public void AddItem(string itemName, bool isChecked, Action action)
        {
            var item = AddItem(itemName, isChecked, true);
            if (item != null) item.action = action;
        }

        public void AddItem(string itemName, bool isChecked, Action<object> action, object data)
        {
            var item = AddItem(itemName, isChecked, true, data);
            if (item != null) item.actionUserData = action;
        }

        public void AddDisabledItem(string itemName, bool isChecked) => AddItem(itemName, isChecked, false);

        public void AddSeparator(string path)
        {
            var separator = new VisualElement();
            separator.AddToClassList(separatorUssClassName);
            separator.pickingMode = PickingMode.Ignore;
            m_ScrollView.Add(separator);
        }

        MenuItem AddItem(string itemName, bool isChecked, bool isEnabled, object data = null)
        {
            if (string.IsNullOrEmpty(itemName) || itemName.EndsWith("/", StringComparison.Ordinal))
            {
                AddSeparator(itemName);
                return null;
            }
            foreach (var existing in m_Items)
                if (itemName == existing.name) return null;
            var row = new VisualElement();
            row.AddToClassList(itemUssClassName);
            row.SetEnabled(isEnabled);
            row.userData = data;
            var content = new VisualElement { pickingMode = PickingMode.Ignore };
            content.AddToClassList(itemContentUssClassName);
            var checkmark = new VisualElement();
            checkmark.AddToClassList(checkmarkUssClassName);
            checkmark.pickingMode = PickingMode.Ignore;
            content.Add(checkmark);
            if (isChecked) row.pseudoStates |= PseudoStates.Checked;
            var label = new Label(itemName);
            label.AddToClassList(labelUssClassName);
            label.pickingMode = PickingMode.Ignore;
            content.Add(label);
            row.Add(content);
            m_ScrollView.Add(row);
            var item = new MenuItem { name = itemName, element = row };
            m_Items.Add(item);
            return item;
        }

        internal void UpdateItem(string itemName, bool isChecked)
        {
            var item = m_Items.Find(x => x.name == itemName);
            if (item == null) return;
            if (isChecked) item.element.pseudoStates |= PseudoStates.Checked;
            else item.element.pseudoStates &= ~PseudoStates.Checked;
        }

        public void DropDown(Rect position, VisualElement targetElement = null, bool anchored = false)
        {
            if (targetElement == null)
            {
                Debug.LogError("VisualElement Generic Menu needs a target to find a root to attach to.");
                return;
            }
            m_TargetElement = targetElement;
            m_TargetElement.RegisterCallback<DetachFromPanelEvent>(OnTargetElementDetachFromPanel);
            m_PanelRootVisualContainer = m_TargetElement.GetRootVisualContainer();
            if (m_PanelRootVisualContainer == null)
            {
                Debug.LogError("Could not find rootVisualContainer...");
                return;
            }
            m_PanelRootVisualContainer.Add(m_MenuContainer);
            var rootLayout = m_PanelRootVisualContainer.layout;
            m_MenuContainer.style.left = rootLayout.x;
            m_MenuContainer.style.top = rootLayout.y;
            m_MenuContainer.style.width = rootLayout.width;
            m_MenuContainer.style.height = rootLayout.height;
            m_MenuContainer.style.fontSize = m_TargetElement.computedValues.fontSize;
            m_MenuContainer.style.unityFont = m_TargetElement.computedValues.unityFont;
            m_MenuContainer.style.unityFontDefinition = m_TargetElement.computedValues.unityFontDefinition;
            var local = m_PanelRootVisualContainer.WorldToLocal(position);
            m_PositionTop = local.y + position.height - rootLayout.y;
            m_PositionLeft = local.x - rootLayout.x;
            m_OuterContainer.style.left = m_PositionLeft;
            m_OuterContainer.style.top = m_PositionTop;
            m_OuterContainer.style.maxHeight = Length.None();
            m_OuterContainer.style.maxWidth = Length.None();
            m_DesiredRect = anchored ? position : Rect.zero;
            m_MenuContainer.schedule.Execute(contentContainer.Focus);
            m_ShownAboveTarget = false;
            EnsureVisibilityInParent();
            targetElement.pseudoStates |= PseudoStates.Active;
        }

        public void DropDown(Rect position, VisualElement targetElement = null, bool anchored = false, bool fitContentWidthIfAnchored = false)
        {
            m_FitContentWidth = anchored && fitContentWidthIfAnchored;
            m_OuterContainer.EnableInClassList(contentWidthUssClassName, m_FitContentWidth);
            DropDown(position, targetElement, anchored);
        }

        void OnTargetElementDetachFromPanel(DetachFromPanelEvent evt) => Hide();

        void OnContainerGeometryChanged(GeometryChangedEvent evt) => EnsureVisibilityInParent();

        void OnInitialDisplay(GeometryChangedEvent evt) => m_ContentWidth = GetLargestItemWidth() + k_MenuItemPadding;

        /// <summary>Keeps the menu inside the panel: shifted left when it would overflow the right edge,
        /// shown above the target when there is more room there.</summary>
        void EnsureVisibilityInParent()
        {
            if (m_PanelRootVisualContainer == null || float.IsNaN(m_OuterContainer.layout.width) || float.IsNaN(m_OuterContainer.layout.height)) return;
            var rootLayout = m_PanelRootVisualContainer.layout;
            if (m_DesiredRect == Rect.zero)
            {
                float left = Math.Max(0f, Mathf.Min(m_PositionLeft, rootLayout.width - m_OuterContainer.layout.width));
                float top = Mathf.Min(m_PositionTop, Mathf.Max(0f, rootLayout.height - m_OuterContainer.layout.height));
                m_OuterContainer.style.left = left;
                m_OuterContainer.style.top = top;
            }
            else
            {
                float width = m_ContentWidth;
                if (m_ScrollView.isVerticalScrollDisplayed) width += Mathf.Ceil(m_ScrollView.verticalScroller.computedValues.width.value);
                width = m_FitContentWidth ? width : m_DesiredRect.width;
                m_OuterContainer.style.width = width;
                float room = rootLayout.width - m_PositionLeft;
                if (room <= width) m_PositionLeft -= width - room + k_MenuPadding;
                m_PositionLeft = Math.Max(m_PositionLeft, 0f);
                if (m_PositionLeft == 0f) m_OuterContainer.style.maxWidth = Math.Min(rootLayout.width, width);
                m_OuterContainer.style.left = m_PositionLeft;
            }
            var target = m_MenuContainer.WorldToLocal(m_TargetElement.worldBound);
            float minHeight = m_Items.Count == 0 ? k_MenuItemPadding : m_Items[0].element.layout.height + k_MenuItemPadding;
            float height = m_OuterContainer.layout.height;
            float targetTop = target.y;
            float outerTop = m_OuterContainer.worldBound.y;
            float rootHeight = m_PanelRootVisualContainer.worldBound.height;
            float spaceHere = m_ShownAboveTarget ? targetTop - outerTop : rootHeight - outerTop;
            float spaceOther = m_ShownAboveTarget ? rootHeight - outerTop : targetTop;
            bool overflows = spaceHere < height;
            if (overflows && spaceOther > spaceHere)
            {
                m_PositionTop = m_OuterContainer.RoundToPanelPixelSize(Math.Max(targetTop - height, 0f));
                m_OuterContainer.style.maxHeight = m_PositionTop == 0f ? (Length)Math.Max(targetTop, minHeight) : Length.None();
                m_OuterContainer.style.top = m_PositionTop;
                m_ShownAboveTarget = true;
            }
            else if (overflows)
            {
                if (spaceHere < minHeight)
                {
                    m_OuterContainer.style.maxHeight = minHeight;
                    m_PositionTop = rootHeight - minHeight;
                }
                else
                {
                    m_OuterContainer.style.maxHeight = spaceHere;
                }
                m_OuterContainer.style.top = m_PositionTop;
            }
        }

        float GetLargestItemWidth()
        {
            float largest = 0f;
            if (m_Items.Count == 0 && m_ScrollView.contentContainer.childCount > 0)
            {
                foreach (var child in m_ScrollView.contentContainer.Children())
                {
                    m_Items.Add(new MenuItem { element = child });
                    largest = Math.Max(largest, child.layout.width);
                }
                return largest;
            }
            foreach (var item in m_Items)
            {
                float width = item.element.Q(null, itemContentUssClassName)?.layout.width ?? item.element.layout.width;
                largest = Math.Max(largest, width);
            }
            return largest;
        }
    }
}
