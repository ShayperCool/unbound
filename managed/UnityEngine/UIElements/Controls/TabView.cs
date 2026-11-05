using System;
using System.Collections.Generic;

// TabView and Tab, from the Unity 6000.0 Manual pages "TabView" and "Tab" (UI Toolkit control
// reference, with their UXML attribute and USS class tables), the Scripting API pages of both types
// and of TabView.activeTab, selectedTabIndex, activeTabChanged, ReorderTab, contentContainer and
// Tab.closing, selected, and the installed 6000.0 doc XML. Documented:
//   - a TabView "groups a one or more Tab elements"; it "allows a group of tabs to interact between
//     each other by hiding the inactive tabs and displaying the active one", and can re-order them;
//   - tabs are added with TabView.Add (Manual C# example) or as <Tab> children of <TabView> (UXML
//     example); the content of a Tab is added with Tab.Add, into its content container;
//   - "A Tab has a header that can respond to pointer and mouse events. The header can hold an icon,
//     a label, or both";
//   - activeTab: "Setting a valid tab will also deselect the previous active tab before setting the
//     newly assigned tab to active"; selectedTabIndex is the active tab's index, and "Setting a valid
//     index will set the Tab at the given index to active";
//   - activeTabChanged "receives two tabs, the first being the previously selected tab and the second
//     being the newly selected tab"; Tab.selected "is called when a tab has been selected";
//   - closeable: "a close icon appears on the Tab. If a user selects the close icon, the Tab closes";
//     closing "is called before a tab is closed. Return true if the tab can be closed ... all of them
//     will be executed synchronously but only the last value will be taken into account"; closed "is
//     called when a tab has been closed"; tabClosed passes "the tab that was closed" and "the index of
//     the tab that was closed";
//   - reorderable "adds dragging support to tabs"; ReorderTab "Moves the tab from the specified index
//     to a new index"; tabReordered passes the source and destination indices;
//   - USS class names from the pages' USS class tables; TabView.headerContainerClassName (described
//     as "USS class name for the header container", value not in the Manual table) is the selector of
//     Unity's shipped default theme, read as data. The Manual's USS example styles
//     `.unity-tab__header:checked > .unity-tab__header-underline`: the active tab's header is :checked
//     and the underline is a direct child of the header.
// Unbound's choices (unmeasured; the pages say nothing):
//   - element structure: a TabView holds a header container then its content container; a Tab holds
//     its header then its content container. Inside a TabView, each tab's header moves into the
//     TabView's header container (in tab order) and the Tab element stays in the content container;
//     a Tab outside a TabView keeps its header. The header holds, in order, the drag handle (two bars,
//     only in a reorderable TabView), the icon Image, the label, the underline and the close button
//     (only when closeable). The close button is a plain VisualElement;
//   - the icon Image is always present (with the --empty class when there is no icon, --standalone
//     when the label is empty); the label has the --empty class when it is null or empty;
//   - closeableUssClassName, reorderableUssClassName and draggingUssClassName go on the tab's header;
//   - inactive tabs are hidden with an inline `display: none` on the Tab element, the active one has
//     no inline display; a tab leaving a TabView gets its header back, without the inline display;
//   - the first tab added to a TabView without an active tab becomes active, with the events of a
//     selection; elements that are not Tabs stay in the content container and are not tabs;
//   - a click (ClickEvent) on a header selects its tab; a click on the close button closes it and
//     does not select it;
//   - selection sends Tab.selected (new tab) then activeTabChanged (previous, new); setting the active
//     tab again, a tab outside this TabView, null or an index out of range changes nothing and sends
//     nothing;
//   - with no closing handler a tab can be closed; closing removes the tab from its parent, then sends
//     Tab.closed, then TabView.tabClosed (tab, its former index). Closing (or removing) the active tab
//     activates the tab now at its index, else the one before it, else none, sending selected (if
//     any) and activeTabChanged (closed tab, new tab or null) before closed. Only a tab still in the
//     content container counts, so Clear() sends a single activeTabChanged (active tab, null) and no
//     selected. Removing a tab from the hierarchy by code sends no closing/closed/tabClosed;
//   - ReorderTab with an index out of range, or from == to, changes nothing and sends nothing; it
//     keeps the active tab and sends tabReordered (from, to) after moving the tab, its header and its
//     element; the tab ends at index `to`;
//   - GetTab/GetTabHeader throw ArgumentOutOfRangeException for an index out of range;
//   - pointer dragging (reorderable only): a primary-button press on a header, then a move of more
//     than 5 pixels starts a drag (the header captures the pointer and gets the dragging class); while
//     dragging, the tab moves to the index given by how many other headers' horizontal centers lie
//     before the pointer, through ReorderTab; releasing the pointer ends the drag and makes the
//     dragged tab active. There is no drag preview element, and the vertical layout is not supported
//     (no public 6000.0 API selects it);
//   - view data: tab order and selection are not persisted (the Manual describes persistence for
//     Editor UI; Unbound behaves as a runtime panel). Keyboard navigation between tabs is not added.
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
        Tab m_ActiveTab;
        bool m_Reorderable;
        bool m_Moving;

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
                if (value == null || value == m_ActiveTab || !m_Tabs.Contains(value)) return;
                Activate(value);
            }
        }

        public int selectedTabIndex
        {
            get => m_ActiveTab == null ? -1 : m_Tabs.IndexOf(m_ActiveTab);
            set
            {
                if (value < 0 || value >= m_Tabs.Count) return;
                activeTab = m_Tabs[value];
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
            m_HeaderContainer = new VisualElement();
            m_HeaderContainer.AddToClassList(headerContainerClassName);
            hierarchy.Add(m_HeaderContainer);
            m_ContentContainer = new VisualElement();
            m_ContentContainer.AddToClassList(contentContainerUssClassName);
            hierarchy.Add(m_ContentContainer);
            m_ContentContainer.elementAdded += OnElementAdded;
            m_ContentContainer.elementRemoved += OnElementRemoved;
        }

        public Tab GetTab(int index)
        {
            if (index < 0 || index >= m_Tabs.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Tabs[index];
        }

        public VisualElement GetTabHeader(int index)
        {
            if (index < 0 || index >= m_TabHeaders.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return m_TabHeaders[index];
        }

        public void ReorderTab(int from, int to)
        {
            int count = m_Tabs.Count;
            if (from < 0 || from >= count || to < 0 || to >= count || from == to) return;
            var tab = m_Tabs[from];
            var tabHeader = m_TabHeaders[from];
            m_Tabs.RemoveAt(from);
            m_TabHeaders.RemoveAt(from);
            m_Tabs.Insert(to, tab);
            m_TabHeaders.Insert(to, tabHeader);
            m_Moving = true;
            try
            {
                m_HeaderContainer.hierarchy.Insert(to, tabHeader);
                tab.RemoveFromHierarchy();
                int at = to + 1 < m_Tabs.Count
                    ? m_ContentContainer.hierarchy.IndexOf(m_Tabs[to + 1])
                    : m_ContentContainer.hierarchy.IndexOf(m_Tabs[to - 1]) + 1;
                m_ContentContainer.hierarchy.Insert(at, tab);
            }
            finally
            {
                m_Moving = false;
            }
            tabReordered?.Invoke(from, to);
        }

        void Activate(Tab tab)
        {
            var previous = m_ActiveTab;
            previous?.SetInactive();
            m_ActiveTab = tab;
            if (tab != null)
            {
                tab.SetActive();
                tab.NotifySelected();
            }
            activeTabChanged?.Invoke(previous, tab);
        }

        void OnElementAdded(VisualElement child, int index)
        {
            if (m_Moving || !(child is Tab tab)) return;
            int tabIndex = 0;
            for (int i = 0; i < index && i < m_ContentContainer.hierarchy.childCount; i++)
                if (m_ContentContainer.hierarchy[i] is Tab) tabIndex++;
            m_Tabs.Insert(tabIndex, tab);
            m_TabHeaders.Insert(tabIndex, tab.tabHeader);
            tab.AttachTo(this);
            m_HeaderContainer.hierarchy.Insert(tabIndex, tab.tabHeader);
            tab.EnableTabDragHandles(m_Reorderable);
            tab.SetInactive();
            if (m_ActiveTab == null) Activate(tab);
        }

        void OnElementRemoved(VisualElement child)
        {
            if (m_Moving || !(child is Tab tab)) return;
            int index = m_Tabs.IndexOf(tab);
            if (index < 0) return;
            m_Tabs.RemoveAt(index);
            m_TabHeaders.RemoveAt(index);
            tab.DetachFrom(this);
            if (tab != m_ActiveTab) return;
            m_ActiveTab = null;
            // Only a tab still in the content container can become active: Clear() empties it before
            // it reports each removed child, so the tabs listed after this one are already gone.
            Tab next = index < m_Tabs.Count && InContent(m_Tabs[index]) ? m_Tabs[index]
                : index > 0 && InContent(m_Tabs[index - 1]) ? m_Tabs[index - 1]
                : null;
            if (next != null)
            {
                m_ActiveTab = next;
                next.SetActive();
                next.NotifySelected();
            }
            activeTabChanged?.Invoke(tab, next);
        }

        bool InContent(Tab tab) => m_ContentContainer.hierarchy.IndexOf(tab) >= 0;

        internal void NotifyTabClosed(Tab tab, int index) => tabClosed?.Invoke(tab, index);

        /// <summary>Moves a dragged tab to the index the pointer's horizontal position gives.</summary>
        internal void DragTo(Tab tab, float pointerX)
        {
            int from = m_Tabs.IndexOf(tab);
            if (from < 0) return;
            int to = 0;
            for (int i = 0; i < m_TabHeaders.Count; i++)
            {
                if (i == from) continue;
                if (m_TabHeaders[i].worldBound.center.x < pointerX) to++;
            }
            ReorderTab(from, to);
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
        public static readonly string reorderableItemHandleUssClassName = ussClassName + "__reorderable-handle";
        public static readonly string reorderableItemHandleBarUssClassName = ussClassName + "__reorderable-handle-bar";
        public static readonly string closeableUssClassName = tabHeaderUssClassName + "__closeable";
        public static readonly string closeButtonUssClassName = ussClassName + "__close-button";

        const float k_DragThreshold = 5f;

        readonly VisualElement m_TabHeader;
        readonly Image m_HeaderImage;
        readonly Label m_HeaderLabel;
        readonly VisualElement m_Underline;
        readonly VisualElement m_ContentContainer;
        VisualElement m_DragHandle;
        VisualElement m_CloseButton;
        TabView m_TabView;
        string m_Label;
        Background m_IconImage;
        bool m_Closeable;
        int m_DragPointerId = -1;
        Vector2 m_DragStart;
        bool m_Dragging;

        public event Action<Tab> selected;
        public event Func<bool> closing;
        public event Action<Tab> closed;

        public VisualElement tabHeader => m_TabHeader;

        internal Label headerLabel => m_HeaderLabel;

        public override VisualElement contentContainer => m_ContentContainer;

        public string label
        {
            get => m_Label;
            set
            {
                m_Label = value;
                m_HeaderLabel.text = value ?? string.Empty;
                UpdateHeaderClasses();
            }
        }

        public Background iconImage
        {
            get => m_IconImage;
            set
            {
                m_IconImage = value;
                m_HeaderImage.image = null;
                m_HeaderImage.sprite = null;
                m_HeaderImage.vectorImage = null;
                if (value.texture != null) m_HeaderImage.image = value.texture;
                else if (value.sprite != null) m_HeaderImage.sprite = value.sprite;
                else if (value.renderTexture != null) m_HeaderImage.image = value.renderTexture;
                else if (value.vectorImage != null) m_HeaderImage.vectorImage = value.vectorImage;
                UpdateHeaderClasses();
            }
        }

        public bool closeable
        {
            get => m_Closeable;
            set
            {
                if (m_Closeable == value) return;
                m_Closeable = value;
                EnableTabCloseButton(value);
            }
        }

        public Tab() : this(null, default(Background)) { }

        public Tab(string label) : this(label, default(Background)) { }

        public Tab(Background iconImage) : this(null, iconImage) { }

        public Tab(string label, Background iconImage)
        {
            AddToClassList(ussClassName);
            m_TabHeader = new VisualElement();
            m_TabHeader.AddToClassList(tabHeaderUssClassName);
            m_HeaderImage = new Image();
            m_HeaderImage.AddToClassList(tabHeaderImageUssClassName);
            m_TabHeader.hierarchy.Add(m_HeaderImage);
            m_HeaderLabel = new Label();
            m_HeaderLabel.AddToClassList(tabHeaderLabelUssClassName);
            m_TabHeader.hierarchy.Add(m_HeaderLabel);
            m_Underline = new VisualElement();
            m_Underline.AddToClassList(tabHeaderUnderlineUssClassName);
            m_TabHeader.hierarchy.Add(m_Underline);
            hierarchy.Add(m_TabHeader);
            m_ContentContainer = new VisualElement();
            m_ContentContainer.AddToClassList(contentUssClassName);
            hierarchy.Add(m_ContentContainer);

            m_TabHeader.RegisterCallback<ClickEvent>(OnHeaderClick);
            m_TabHeader.RegisterCallback<PointerDownEvent>(OnHeaderPointerDown);
            m_TabHeader.RegisterCallback<PointerMoveEvent>(OnHeaderPointerMove);
            m_TabHeader.RegisterCallback<PointerUpEvent>(OnHeaderPointerUp);
            m_TabHeader.RegisterCallback<PointerCaptureOutEvent>(OnHeaderPointerCaptureOut);

            this.label = label;
            this.iconImage = iconImage;
        }

        void UpdateHeaderClasses()
        {
            bool noLabel = string.IsNullOrEmpty(m_Label);
            m_HeaderLabel.EnableInClassList(tabHeaderEmptyLabeUssClassName, noLabel);
            m_HeaderImage.EnableInClassList(tabHeaderEmptyImageUssClassName, m_IconImage.IsEmpty());
            m_HeaderImage.EnableInClassList(tabHeaderStandaloneImageUssClassName, noLabel);
        }

        internal void EnableTabDragHandles(bool enable)
        {
            m_TabHeader.EnableInClassList(reorderableUssClassName, enable);
            if (enable)
            {
                if (m_DragHandle != null) return;
                m_DragHandle = new VisualElement { pickingMode = PickingMode.Ignore };
                m_DragHandle.AddToClassList(reorderableItemHandleUssClassName);
                for (int i = 0; i < 2; i++)
                {
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.AddToClassList(reorderableItemHandleBarUssClassName);
                    m_DragHandle.hierarchy.Add(bar);
                }
                m_TabHeader.hierarchy.Insert(0, m_DragHandle);
                return;
            }
            EndDrag(false);
            m_DragHandle?.RemoveFromHierarchy();
            m_DragHandle = null;
        }

        internal void EnableTabCloseButton(bool enable)
        {
            m_TabHeader.EnableInClassList(closeableUssClassName, enable);
            if (enable)
            {
                if (m_CloseButton != null) return;
                m_CloseButton = new VisualElement();
                m_CloseButton.AddToClassList(closeButtonUssClassName);
                m_CloseButton.RegisterCallback<ClickEvent>(OnCloseButtonClick);
                m_TabHeader.hierarchy.Add(m_CloseButton);
                return;
            }
            m_CloseButton?.RemoveFromHierarchy();
            m_CloseButton = null;
        }

        internal void SetActive()
        {
            m_TabHeader.pseudoStates |= PseudoStates.Checked;
            if (m_TabView != null) style.display = StyleKeyword.Null;
        }

        internal void SetInactive()
        {
            m_TabHeader.pseudoStates &= ~PseudoStates.Checked;
            if (m_TabView != null) style.display = DisplayStyle.None;
        }

        internal void NotifySelected() => selected?.Invoke(this);

        internal void AttachTo(TabView view) => m_TabView = view;

        internal void DetachFrom(TabView view)
        {
            if (m_TabView != view) return;
            EndDrag(false);
            m_TabView = null;
            EnableTabDragHandles(false);
            m_TabHeader.pseudoStates &= ~PseudoStates.Checked;
            style.display = StyleKeyword.Null;
            hierarchy.Insert(0, m_TabHeader);
        }

        /// <summary>Asks the closing handlers (the last answer counts; none means yes), then removes
        /// the tab and sends closed and, inside a TabView, tabClosed.</summary>
        internal bool TryClose()
        {
            var handlers = closing;
            if (handlers != null)
            {
                bool allow = true;
                foreach (Func<bool> handler in handlers.GetInvocationList()) allow = handler();
                if (!allow) return false;
            }
            var view = m_TabView;
            int index = view != null ? view.tabs.IndexOf(this) : -1;
            RemoveFromHierarchy();
            closed?.Invoke(this);
            view?.NotifyTabClosed(this, index);
            return true;
        }

        void OnCloseButtonClick(ClickEvent evt)
        {
            evt.StopPropagation();
            TryClose();
        }

        void OnHeaderClick(ClickEvent evt)
        {
            if (m_TabView != null) m_TabView.activeTab = this;
        }

        void OnHeaderPointerDown(PointerDownEvent evt)
        {
            if (m_TabView == null || !m_TabView.reorderable || evt.button != 0 || m_DragPointerId >= 0) return;
            if (m_CloseButton != null && evt.target is VisualElement target && (target == m_CloseButton || m_CloseButton.Contains(target))) return;
            m_DragPointerId = evt.pointerId;
            m_DragStart = evt.position;
            m_Dragging = false;
        }

        void OnHeaderPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != m_DragPointerId || m_TabView == null) return;
            if (!m_Dragging)
            {
                if (((Vector2)evt.position - m_DragStart).magnitude <= k_DragThreshold) return;
                m_Dragging = true;
                m_TabHeader.AddToClassList(draggingUssClassName);
                m_TabHeader.CapturePointer(m_DragPointerId);
            }
            m_TabView.DragTo(this, evt.position.x);
        }

        void OnHeaderPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != m_DragPointerId) return;
            EndDrag(true);
        }

        void OnHeaderPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (m_Dragging) EndDrag(true);
        }

        void EndDrag(bool select)
        {
            int pointerId = m_DragPointerId;
            bool dragged = m_Dragging;
            m_DragPointerId = -1;
            m_Dragging = false;
            if (!dragged) return;
            m_TabHeader.RemoveFromClassList(draggingUssClassName);
            if (m_TabHeader.HasPointerCapture(pointerId)) m_TabHeader.ReleasePointer(pointerId);
            if (select && m_TabView != null) m_TabView.activeTab = this;
        }
    }
}
