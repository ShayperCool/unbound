using System;
using System.Collections.Generic;

// TabView and Tab. Documented (Unity 6000.0 Manual "TabView"/"Tab" and their Scripting API pages):
// a TabView groups Tab elements and shows the active one; activeTab "will also deselect the previous
// active tab"; activeTabChanged receives (previous, new); selectedTabIndex is the active tab's
// index; closeable tabs get a close icon; closing "is called before a tab is closed ... only the last
// value will be taken into account"; tabClosed passes the tab and its index; ReorderTab moves a tab;
// the USS class tables.
// Matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (tabs section):
//   - structure and names: a TabView holds its header container and content container, each named
//     after its USS class; a Tab holds its header (image, label, underline, then the close button
//     when closeable, the drag handle first when reorderable) and its content container; inside a
//     TabView the headers live in the header container in tab order;
//   - the active tab and its header carry the :checked pseudo state (the theme shows only the
//     checked tab); the first tab added becomes active;
//   - a pointer press on a header sends Tab.selected, even for the active tab; the TabView then
//     makes it active (activeTabChanged only on a change). Setting activeTab or selectedTabIndex in
//     code sends activeTabChanged only, not selected;
//   - a pointer press on the close button asks closing (the last handler's answer counts), removes
//     the tab, then sends closed and tabClosed (tab, its last index); the press is not a selection;
//   - removing the active tab activates the first remaining tab;
//   - activeTab = null with tabs present throws NullReferenceException, a tab outside the TabView
//     throws; GetTab/GetTabHeader return null out of range; selectedTabIndex out of range is ignored.
// Tab dragging (reorderable): a left press on a tab's drag handle starts tracking; a move of more
// than 5 points along the header row starts the drag, activating the tab under the press point;
// releasing moves it before or after the header whose half the pointer is over (ReorderTab), Escape
// or a lost capture cancels. Follows the same rules; not measured by a capture.
namespace UnityEngine.UIElements
{
    public partial class TabView : VisualElement
    {
        public static readonly string ussClassName = "unity-tab-view";
        public static readonly string headerContainerClassName = ussClassName + "__header-container";
        public static readonly string contentContainerUssClassName = ussClassName + "__content-container";
        public static readonly string reorderableUssClassName = ussClassName + "__reorderable";
        public static readonly string verticalUssClassName = ussClassName + "__vertical";

        readonly VisualElement m_HeaderContainer;
        readonly VisualElement m_ContentContainer;
        readonly List<Tab> m_Tabs = new List<Tab>();
        readonly List<VisualElement> m_TabHeaders = new List<VisualElement>();
        readonly Action<Tab> m_OnTabSelected;
        readonly Action<Tab> m_OnTabClosed;
        Tab m_ActiveTab;
        bool m_Reorderable;
        bool m_Reordering;

        public event Action<Tab, Tab> activeTabChanged;
        public event Action<int, int> tabReordered;
        public event Action<Tab, int> tabClosed;

        public override VisualElement contentContainer => m_ContentContainer;

        internal VisualElement header => m_HeaderContainer;

        internal List<Tab> tabs => m_Tabs;

        internal List<VisualElement> tabHeaders => m_TabHeaders;

        public Tab activeTab
        {
            get => m_ActiveTab;
            set
            {
                if (value == null && m_Tabs.Count > 0)
                    throw new NullReferenceException("Active tab cannot be null when there are available tabs.");
                if (m_Tabs.IndexOf(value) == -1)
                    throw new Exception("The tab to be set as active does not exist in this TabView.");
                if (value == m_ActiveTab) return;
                var previous = m_ActiveTab;
                previous?.SetInactive();
                m_ActiveTab = value;
                m_ActiveTab?.SetActive();
                activeTabChanged?.Invoke(previous, value);
            }
        }

        public int selectedTabIndex
        {
            get => m_ActiveTab == null || m_Tabs.Count == 0 ? -1 : m_Tabs.IndexOf(m_ActiveTab);
            set
            {
                if (value >= 0 && value < m_Tabs.Count) activeTab = m_Tabs[value];
            }
        }

        public bool reorderable
        {
            get => m_Reorderable;
            set
            {
                if (m_Reorderable == value) return;
                m_Reorderable = value;
                EnableInClassList(reorderableUssClassName, value);
                foreach (var tab in m_Tabs) tab.EnableTabDragHandles(value);
            }
        }

        public TabView()
        {
            AddToClassList(ussClassName);
            m_HeaderContainer = new VisualElement { name = headerContainerClassName };
            m_HeaderContainer.AddToClassList(headerContainerClassName);
            hierarchy.Add(m_HeaderContainer);
            m_ContentContainer = new VisualElement { name = contentContainerUssClassName };
            m_ContentContainer.AddToClassList(contentContainerUssClassName);
            hierarchy.Add(m_ContentContainer);
            m_OnTabSelected = tab => activeTab = tab;
            m_OnTabClosed = tab => tabClosed?.Invoke(tab, tab.index);
            m_ContentContainer.elementAdded += OnElementAdded;
            m_ContentContainer.elementRemoved += OnElementRemoved;
        }

        public Tab GetTab(int index) => index < 0 || index >= m_Tabs.Count ? null : m_Tabs[index];

        public VisualElement GetTabHeader(int index) => index < 0 || index >= m_Tabs.Count ? null : m_TabHeaders[index];

        public void ReorderTab(int from, int to)
        {
            var tabHeader = m_TabHeaders[from];
            var tab = m_Tabs[from];
            if (!tabHeader.visible || !m_Reorderable || from == to) return;
            m_Reordering = true;
            try
            {
                m_TabHeaders.RemoveAt(from);
                m_TabHeaders.Insert(to, tabHeader);
                m_Tabs.RemoveAt(from);
                m_Tabs.Insert(to, tab);
                m_HeaderContainer.Insert(to, tabHeader);
                Insert(to, tab);
            }
            finally
            {
                m_Reordering = false;
            }
            UpdateIndexes();
            tabReordered?.Invoke(from, to);
        }

        void UpdateIndexes()
        {
            for (int i = 0; i < m_Tabs.Count; i++) m_Tabs[i].index = i;
        }

        void OnElementAdded(VisualElement child, int index)
        {
            if (m_Reordering || !(child is Tab tab)) return;
            var tabHeader = tab.tabHeader;
            if (tabHeader != null)
            {
                m_HeaderContainer.Insert(index, tabHeader);
                m_TabHeaders.Insert(index, tabHeader);
                m_Tabs.Insert(index, tab);
                tab.EnableTabDragHandles(m_Reorderable);
                tab.closed += m_OnTabClosed;
            }
            tab.selected += m_OnTabSelected;
            UpdateIndexes();
            if (m_ActiveTab == null) activeTab = tab;
        }

        void OnElementRemoved(VisualElement child)
        {
            if (m_Reordering || !(child is Tab tab)) return;
            var tabHeader = tab.tabHeader;
            m_HeaderContainer.Remove(tabHeader);
            m_TabHeaders.Remove(tabHeader);
            m_Tabs.Remove(tab);
            // The tab keeps its closed/selected subscriptions: closing removes the tab before it
            // sends closed, and tabClosed still follows (Unity 6000.0.71f1 Editor, close-tab-2).
            tab.EnableTabDragHandles(false);
            tab.hierarchy.Insert(0, tabHeader);
            tab.SetInactive();
            UpdateIndexes();
            if (m_ActiveTab == tab && m_Tabs.Count > 0) activeTab = m_Tabs[0];
            else if (m_Tabs.Count == 0) m_ActiveTab = null;
        }
    }

    public partial class Tab : VisualElement
    {
        public static readonly string ussClassName = "unity-tab";
        public static readonly string tabHeaderUssClassName = ussClassName + "__header";
        public static readonly string tabHeaderImageUssClassName = tabHeaderUssClassName + "-image";
        public static readonly string tabHeaderEmptyImageUssClassName = tabHeaderImageUssClassName + "--empty";
        public static readonly string tabHeaderStandaloneImageUssClassName = tabHeaderImageUssClassName + "--standalone";
        public static readonly string tabHeaderLabelUssClassName = tabHeaderUssClassName + "-label";
        public static readonly string tabHeaderEmptyLabeUssClassName = tabHeaderLabelUssClassName + "--empty";
        public static readonly string tabHeaderUnderlineUssClassName = tabHeaderUssClassName + "-underline";
        public static readonly string contentUssClassName = ussClassName + "__content-container";
        public static readonly string draggingUssClassName = ussClassName + "--dragging";
        public static readonly string reorderableUssClassName = ussClassName + "__reorderable";
        public static readonly string reorderableItemHandleUssClassName = reorderableUssClassName + "-handle";
        public static readonly string reorderableItemHandleBarUssClassName = reorderableItemHandleUssClassName + "-bar";
        public static readonly string closeableUssClassName = tabHeaderUssClassName + "__closeable";
        public static readonly string closeButtonUssClassName = ussClassName + "__close-button";

        readonly VisualElement m_TabHeader;
        readonly Image m_TabHeaderImage;
        readonly Label m_TabHeaderLabel;
        readonly VisualElement m_ContentContainer;
        readonly VisualElement m_DragHandle;
        readonly VisualElement m_CloseButton;
        string m_Label;
        Background m_IconImage;
        bool m_Closeable;

        public event Action<Tab> selected;
        public event Func<bool> closing;
        public event Action<Tab> closed;

        public VisualElement tabHeader => m_TabHeader;

        internal Label headerLabel => m_TabHeaderLabel;

        internal TabDragger dragger { get; }

        /// <summary>The tab's index in its TabView, kept by the TabView.</summary>
        internal int index { get; set; }

        public override VisualElement contentContainer => m_ContentContainer;

        public string label
        {
            get => m_Label;
            set
            {
                if (string.CompareOrdinal(value, m_Label) == 0) return;
                m_TabHeaderLabel.text = value;
                m_TabHeaderLabel.EnableInClassList(tabHeaderEmptyLabeUssClassName, string.IsNullOrEmpty(value));
                m_TabHeaderImage.EnableInClassList(tabHeaderStandaloneImageUssClassName, string.IsNullOrEmpty(value));
                m_Label = value;
            }
        }

        public Background iconImage
        {
            get => m_IconImage;
            set
            {
                if (value == m_IconImage) return;
                m_IconImage = value;
                m_TabHeaderImage.image = null;
                m_TabHeaderImage.sprite = null;
                m_TabHeaderImage.vectorImage = null;
                if (value.IsEmpty())
                {
                    m_TabHeaderImage.AddToClassList(tabHeaderEmptyImageUssClassName);
                    m_TabHeaderImage.RemoveFromClassList(tabHeaderStandaloneImageUssClassName);
                    return;
                }
                if (value.texture != null) m_TabHeaderImage.image = value.texture;
                else if (value.sprite != null) m_TabHeaderImage.sprite = value.sprite;
                else if (value.renderTexture != null) m_TabHeaderImage.image = value.renderTexture;
                else m_TabHeaderImage.vectorImage = value.vectorImage;
                m_TabHeaderImage.RemoveFromClassList(tabHeaderEmptyImageUssClassName);
                m_TabHeaderImage.EnableInClassList(tabHeaderStandaloneImageUssClassName, string.IsNullOrEmpty(m_Label));
            }
        }

        public bool closeable
        {
            get => m_Closeable;
            set
            {
                if (m_Closeable == value) return;
                m_Closeable = value;
                m_TabHeader.EnableInClassList(closeableUssClassName, value);
                EnableTabCloseButton(value);
            }
        }

        public Tab() : this(null, default(Background)) { }

        public Tab(string label) : this(label, default(Background)) { }

        public Tab(Background iconImage) : this(null, iconImage) { }

        public Tab(string label, Background iconImage)
        {
            AddToClassList(ussClassName);
            m_TabHeader = Named(tabHeaderUssClassName);
            m_DragHandle = Named(reorderableItemHandleUssClassName);
            var leftBar = Named(reorderableItemHandleBarUssClassName);
            leftBar.AddToClassList(reorderableItemHandleBarUssClassName + "--left");
            m_DragHandle.Add(leftBar);
            m_DragHandle.Add(Named(reorderableItemHandleBarUssClassName));
            m_TabHeaderImage = new Image { name = tabHeaderImageUssClassName };
            m_TabHeaderImage.AddToClassList(tabHeaderImageUssClassName);
            m_TabHeaderImage.AddToClassList(tabHeaderEmptyImageUssClassName);
            m_TabHeader.Add(m_TabHeaderImage);
            m_TabHeaderLabel = new Label { name = tabHeaderLabelUssClassName };
            m_TabHeaderLabel.AddToClassList(tabHeaderLabelUssClassName);
            m_TabHeader.Add(m_TabHeaderLabel);
            m_TabHeader.RegisterCallback<PointerDownEvent>(OnTabClicked);
            m_TabHeader.Add(Named(tabHeaderUnderlineUssClassName));
            m_CloseButton = Named(closeButtonUssClassName);
            m_CloseButton.RegisterCallback<PointerDownEvent>(OnCloseButtonClicked);
            hierarchy.Add(m_TabHeader);
            m_ContentContainer = Named(contentUssClassName);
            m_ContentContainer.userData = m_TabHeader;
            hierarchy.Add(m_ContentContainer);
            this.label = label;
            this.iconImage = iconImage;
            m_DragHandle.AddManipulator(dragger = new TabDragger());
        }

        static VisualElement Named(string className)
        {
            var e = new VisualElement { name = className };
            e.AddToClassList(className);
            return e;
        }

        internal void EnableTabDragHandles(bool enable)
        {
            if (enable) m_TabHeader.Insert(0, m_DragHandle);
            else if (m_TabHeader.Contains(m_DragHandle)) m_TabHeader.Remove(m_DragHandle);
        }

        internal void EnableTabCloseButton(bool enable)
        {
            if (enable) m_TabHeader.Add(m_CloseButton);
            else if (m_TabHeader.Contains(m_CloseButton)) m_TabHeader.Remove(m_CloseButton);
        }

        internal void SetActive()
        {
            m_TabHeader.pseudoStates |= PseudoStates.Checked;
            pseudoStates |= PseudoStates.Checked;
        }

        internal void SetInactive()
        {
            m_TabHeader.pseudoStates &= ~PseudoStates.Checked;
            pseudoStates &= ~PseudoStates.Checked;
        }

        void OnTabClicked(PointerDownEvent evt) => selected?.Invoke(this);

        void OnCloseButtonClicked(PointerDownEvent evt)
        {
            // Every closing handler runs; the delegate's result is the last one's answer.
            var handlers = closing;
            if (handlers == null || handlers())
            {
                RemoveFromHierarchy();
                closed?.Invoke(this);
            }
            evt.StopPropagation();
        }
    }

    /// <summary>Drags a tab of a reorderable TabView by its drag handle along the header row.</summary>
    internal partial class TabDragger : PointerManipulator
    {
        // Distance along the header row before a press becomes a drag.
        const float k_StartDragDistance = 5f;

        float m_StartPos;
        float m_LastPos;
        bool m_Moving;
        bool m_Cancelled;
        VisualElement m_Header;
        TabView m_TabView;
        VisualElement m_PreviewElement;
        TabDragLocationPreview m_LocationPreviewElement;
        VisualElement m_TabToMove;
        float m_TabToMovePos;
        VisualElement m_DestinationTab;
        bool m_MoveBeforeDestination;
        int m_DraggingPointerId = PointerId.invalidPointerId;

        internal bool active { get; set; }

        internal bool isVertical { get; set; }

        internal bool moving
        {
            get => m_Moving;
            private set
            {
                if (m_Moving == value) return;
                m_Moving = value;
                m_TabToMove.EnableInClassList(Tab.draggingUssClassName, value);
            }
        }

        public TabDragger()
        {
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (!CanStartManipulation(evt)) return;
            if (active) evt.StopImmediatePropagation();
            else ProcessDownEvent(evt, evt.localPosition, evt.pointerId);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (active) ProcessMoveEvent(evt, evt.localPosition);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (active && CanStopManipulation(evt)) ProcessUpEvent(evt, evt.pointerId);
        }

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (active && CanStopManipulation(evt)) ProcessCancelEvent(evt.pointerId);
        }

        void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (active) ProcessCancelEvent(evt.pointerId);
        }

        void ProcessCancelEvent(int pointerId)
        {
            active = false;
            target.ReleasePointer(pointerId);
            if (moving) EndDragMove(true);
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.Escape || !moving) return;
            active = false;
            if (m_DraggingPointerId != PointerId.invalidPointerId) target.ReleasePointer(m_DraggingPointerId);
            EndDragMove(true);
            e.StopPropagation();
        }

        void ProcessDownEvent(EventBase evt, Vector2 localPosition, int pointerId)
        {
            var current = evt.currentTarget as VisualElement;
            var tabView = current?.GetFirstAncestorOfType<TabView>();
            if (tabView == null || !tabView.reorderable) return;
            target.CapturePointer(pointerId);
            m_DraggingPointerId = pointerId;
            m_TabView = tabView;
            m_Header = tabView.header;
            isVertical = m_Header.resolvedStyle.flexDirection == FlexDirection.Column;
            var inHeader = current.ChangeCoordinatesTo(m_Header, localPosition);
            m_Cancelled = false;
            m_StartPos = isVertical ? inHeader.y : inHeader.x;
            active = true;
            evt.StopPropagation();
        }

        void ProcessMoveEvent(EventBase evt, Vector2 localPosition)
        {
            if (m_Cancelled) return;
            var inHeader = ((VisualElement)evt.currentTarget).ChangeCoordinatesTo(m_Header, localPosition);
            float pos = isVertical ? inHeader.y : inHeader.x;
            if (!moving && Mathf.Abs(m_StartPos - pos) > k_StartDragDistance) BeginDragMove(m_StartPos);
            if (moving)
            {
                m_LastPos = pos;
                UpdateMoveLocation();
            }
            evt.StopPropagation();
        }

        void ProcessUpEvent(EventBase evt, int pointerId)
        {
            active = false;
            target.ReleasePointer(pointerId);
            EndDragMove(false);
            evt.StopPropagation();
        }

        float Extent(VisualElement e) => isVertical ? e.boundingBox.height : e.boundingBox.width;

        // The offset of a header along the row: the extents of the headers before it.
        float Offset(VisualElement tabHeader)
        {
            if (tabHeader == null || !tabHeader.visible) return float.NaN;
            float offset = 0;
            var headers = m_TabView.tabHeaders;
            int index = headers.IndexOf(tabHeader);
            for (int i = 0; i < index; i++)
            {
                float extent = Extent(headers[i]);
                if (!float.IsNaN(extent)) offset += extent;
            }
            return offset;
        }

        void BeginDragMove(float pos)
        {
            // The tab under the press point moves (the first one when the press is before them all).
            var headers = m_TabView.tabHeaders;
            m_TabToMove = headers[0];
            float end = 0;
            foreach (var header in headers)
            {
                end += Extent(header);
                if (end > pos)
                {
                    m_TabToMove = header;
                    break;
                }
            }
            moving = true;
            m_LastPos = pos;
            m_PreviewElement = new VisualElement { pickingMode = PickingMode.Ignore };
            m_PreviewElement.AddToClassList(TabView.ussClassName + "__drag-preview");
            m_LocationPreviewElement = new TabDragLocationPreview();
            m_LocationPreviewElement.AddToClassList(isVertical ? TabDragLocationPreview.verticalUssClassName : TabDragLocationPreview.horizontalUssClassName);
            m_Header.hierarchy.Add(m_PreviewElement);
            m_Header.Add(m_LocationPreviewElement);
            m_TabView.activeTab = m_TabView.tabs[headers.IndexOf(m_TabToMove)];
            m_TabToMovePos = Offset(m_TabToMove);
            UpdateMoveLocation();
        }

        void UpdateMoveLocation()
        {
            // The destination is the header the pointer is over; before it when over its first half.
            float end = 0;
            m_DestinationTab = null;
            m_MoveBeforeDestination = false;
            foreach (var header in m_TabView.tabHeaders)
            {
                m_DestinationTab = header;
                float extent = Extent(header);
                float middle = end + extent / 2f;
                end += extent;
                if (end > m_LastPos)
                {
                    m_MoveBeforeDestination = m_LastPos < middle;
                    break;
                }
            }
            UpdatePreviewPosition();
        }

        void UpdatePreviewPosition()
        {
            float start = m_TabToMovePos + m_LastPos - m_StartPos;
            float width = m_TabToMove.boundingBox.width;
            float destination = Offset(m_DestinationTab);
            float after = m_MoveBeforeDestination || m_DestinationTab == null ? 0f : Extent(m_DestinationTab);
            if (isVertical)
            {
                m_PreviewElement.style.top = start;
                m_PreviewElement.style.height = m_TabToMove.boundingBox.height;
                m_PreviewElement.style.width = width;
                if (m_DestinationTab != null)
                {
                    m_LocationPreviewElement.preview.style.width = width;
                    m_LocationPreviewElement.style.top = destination + after;
                }
            }
            else
            {
                m_PreviewElement.style.left = start;
                m_PreviewElement.style.width = width;
                if (m_DestinationTab != null) m_LocationPreviewElement.style.left = destination + after;
            }
        }

        void EndDragMove(bool cancelled)
        {
            if (!moving || m_Cancelled) return;
            m_Cancelled = cancelled;
            if (!cancelled)
            {
                var headers = m_TabView.tabHeaders;
                int from = headers.IndexOf(m_TabToMove);
                int to = headers.IndexOf(m_DestinationTab);
                if (!m_MoveBeforeDestination) to++;
                if (from < to) to--;
                if (from != to) m_TabView.ReorderTab(from, to);
            }
            m_PreviewElement?.RemoveFromHierarchy();
            m_PreviewElement = null;
            m_LocationPreviewElement?.RemoveFromHierarchy();
            m_LocationPreviewElement = null;
            moving = false;
            m_TabToMove = null;
        }
    }

    /// <summary>The marker of where a dragged tab will land.</summary>
    internal partial class TabDragLocationPreview : VisualElement
    {
        public static readonly string ussClassName = TabView.ussClassName + "__drag-location-preview";
        public static readonly string visualUssClassName = ussClassName + "__visual";
        public static readonly string verticalUssClassName = ussClassName + "__vertical";
        public static readonly string horizontalUssClassName = ussClassName + "__horizontal";

        internal VisualElement preview { get; }

        public TabDragLocationPreview()
        {
            AddToClassList(ussClassName);
            pickingMode = PickingMode.Ignore;
            preview = new VisualElement { pickingMode = PickingMode.Ignore };
            preview.AddToClassList(visualUssClassName);
            Add(preview);
        }
    }
}
