using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine.UIElements
{
    /// <summary>A virtualized vertical list: a pool of item elements in a scroll view, rebound to the
    /// items they show as it scrolls, with single or multiple selection.</summary>
    public abstract partial class BaseVerticalCollectionView : BindableElement
    {
        sealed class Selection
        {
            readonly HashSet<int> m_IndexLookup = new HashSet<int>();
            readonly HashSet<int> m_IdLookup = new HashSet<int>();
            int m_MinIndex = -1;
            int m_MaxIndex = -1;
            public readonly List<int> indices = new List<int>();
            public readonly Dictionary<int, object> items = new Dictionary<int, object>();
            public List<int> selectedIds { get; set; }
            public int indexCount => indices.Count;
            public int idCount => selectedIds.Count;
            public int minIndex => m_MinIndex == -1 ? m_MinIndex = indices.Min() : m_MinIndex;
            public int maxIndex => m_MaxIndex == -1 ? m_MaxIndex = indices.Max() : m_MaxIndex;

            public int capacity
            {
                get => indices.Capacity;
                set
                {
                    indices.Capacity = value;
                    if (selectedIds.Capacity < value) selectedIds.Capacity = value;
                }
            }

            public int FirstIndex() => indices.Count > 0 ? indices[0] : -1;

            public object FirstObject() => items.TryGetValue(FirstIndex(), out var value) ? value : null;

            public bool ContainsIndex(int index) => m_IndexLookup.Contains(index);

            public bool ContainsId(int id) => m_IdLookup.Contains(id);

            public void AddId(int id)
            {
                selectedIds.Add(id);
                m_IdLookup.Add(id);
            }

            public void AddIndex(int index, object obj)
            {
                m_IndexLookup.Add(index);
                indices.Add(index);
                items[index] = obj;
                if (index < m_MinIndex) m_MinIndex = index;
                if (index > m_MaxIndex) m_MaxIndex = index;
            }

            public bool TryRemove(int index)
            {
                if (!m_IndexLookup.Remove(index)) return false;
                int at = indices.IndexOf(index);
                if (at >= 0)
                {
                    indices.RemoveAt(at);
                    items.Remove(index);
                    if (index == m_MinIndex) m_MinIndex = -1;
                    if (index == m_MaxIndex) m_MaxIndex = -1;
                }
                return true;
            }

            public void RemoveId(int id)
            {
                selectedIds.Remove(id);
                m_IdLookup.Remove(id);
            }

            public void ClearIndices()
            {
                m_IndexLookup.Clear();
                indices.Clear();
                m_MinIndex = -1;
                m_MaxIndex = -1;
            }

            public void Clear()
            {
                items.Clear();
                m_IdLookup.Clear();
                selectedIds.Clear();
                ClearIndices();
            }
        }

        internal const string internalBindingKey = "__unity-collection-view-internal-binding";
        internal const int s_DefaultItemHeight = 22;
        internal const float ItemHeightUnset = -1f;
        internal static CustomStyleProperty<int> s_ItemHeightProperty = new CustomStyleProperty<int>("--unity-item-height");
        internal static readonly string k_InvalidTemplateError = "Template Not Found";
        static readonly List<ReusableCollectionItem> k_EmptyItems = new List<ReusableCollectionItem>();

        public static readonly string ussClassName = "unity-collection-view";
        public static readonly string borderUssClassName = ussClassName + "--with-border";
        public static readonly string itemUssClassName = ussClassName + "__item";
        public static readonly string dragHoverBarUssClassName = ussClassName + "__drag-hover-bar";
        public static readonly string dragHoverMarkerUssClassName = ussClassName + "__drag-hover-marker";
        public static readonly string itemDragHoverUssClassName = itemUssClassName + "--drag-hover";
        public static readonly string itemSelectedVariantUssClassName = itemUssClassName + "--selected";
        public static readonly string itemAlternativeBackgroundUssClassName = itemUssClassName + "--alternative-background";
        public static readonly string listScrollViewUssClassName = ussClassName + "__scroll-view";
        internal static readonly string backgroundFillUssClassName = ussClassName + "__background-fill";

        SelectionType m_SelectionType;
        internal bool allowSingleClickChoice;
        bool m_HorizontalScrollingEnabled;
        AlternatingRowBackground m_ShowAlternatingRowBackgrounds = AlternatingRowBackground.None;
        internal float m_FixedItemHeight = s_DefaultItemHeight;
        internal bool m_ItemHeightIsInline;
        CollectionVirtualizationMethod m_VirtualizationMethod;
        readonly ScrollView m_ScrollView;
        CollectionViewController m_ViewController;
        CollectionVirtualizationController m_VirtualizationController;
        KeyboardNavigationManipulator m_NavigationManipulator;
        readonly List<int> m_SelectedIds = new List<int>();
        readonly Selection m_Selection;
        float m_LastHeight;
        bool m_IsRangeSelectionDirectionUp;
        readonly Action<int, int> m_ItemIndexChangedCallback;
        readonly Action m_ItemsSourceChangedCallback;
        IVisualElementScheduledItem m_RebuildScheduled;
        internal int m_PreviousRefreshedCount;
        Vector3 m_TouchDownPosition;
        long m_LastPointerDownTimeStamp;
        int m_PointerDownCount;

        public IList itemsSource
        {
            get => viewController?.itemsSource;
            set => GetOrCreateViewController().itemsSource = value;
        }

        [Obsolete("makeItem has been moved to ListView and TreeView. Use these ones instead.")]
        public Func<VisualElement> makeItem
        {
            get => throw new UnityException("makeItem has been moved to ListView and TreeView. Use these ones instead.");
            set => throw new UnityException("makeItem has been moved to ListView and TreeView. Use these ones instead.");
        }

        [Obsolete("bindItem has been moved to ListView and TreeView. Use these ones instead.")]
        public Action<VisualElement, int> bindItem
        {
            get => throw new UnityException("bindItem has been moved to ListView and TreeView. Use these ones instead.");
            set => throw new UnityException("bindItem has been moved to ListView and TreeView. Use these ones instead.");
        }

        [Obsolete("unbindItem has been moved to ListView and TreeView. Use these ones instead.")]
        public Action<VisualElement, int> unbindItem
        {
            get => throw new UnityException("unbindItem has been moved to ListView and TreeView. Use these ones instead.");
            set => throw new UnityException("unbindItem has been moved to ListView and TreeView. Use these ones instead.");
        }

        [Obsolete("destroyItem has been moved to ListView and TreeView. Use these ones instead.")]
        public Action<VisualElement> destroyItem
        {
            get => throw new UnityException("destroyItem has been moved to ListView and TreeView. Use these ones instead.");
            set => throw new UnityException("destroyItem has been moved to ListView and TreeView. Use these ones instead.");
        }

        public override VisualElement contentContainer => null;

        public SelectionType selectionType
        {
            get => m_SelectionType;
            set
            {
                m_SelectionType = value;
                if (m_SelectionType == SelectionType.None) ClearSelection();
                else if (m_SelectionType == SelectionType.Single && m_Selection.indexCount > 1) SetSelection(m_Selection.FirstIndex());
            }
        }

        public object selectedItem => m_Selection.FirstObject();

        public IEnumerable<object> selectedItems
        {
            get
            {
                foreach (int index in m_Selection.indices)
                    yield return m_Selection.items.TryGetValue(index, out var item) ? item : null;
            }
        }

        public int selectedIndex
        {
            get => m_Selection.indexCount == 0 ? -1 : m_Selection.FirstIndex();
            set => SetSelection(value);
        }

        public IEnumerable<int> selectedIndices => m_Selection.indices;

        public IEnumerable<int> selectedIds => m_Selection.selectedIds;

        internal IEnumerable<ReusableCollectionItem> activeItems => m_VirtualizationController?.activeItems ?? k_EmptyItems;

        internal ScrollView scrollView => m_ScrollView;

        internal CollectionVirtualizationController virtualizationController => GetOrCreateVirtualizationController();

        public CollectionViewController viewController => m_ViewController;

        [Obsolete("resolvedItemHeight is deprecated and will be removed from the API.")]
        public float resolvedItemHeight => ResolveItemHeight();

        public bool showBorder
        {
            get => m_ScrollView.ClassListContains(borderUssClassName);
            set => m_ScrollView.EnableInClassList(borderUssClassName, value);
        }

        public bool reorderable
        {
            get => m_Reorderable;
            set
            {
                if (m_Reorderable == value) return;
                if (value) Debug.LogError("Unbound: reordering list views by dragging is not supported yet");
                m_Reorderable = value;
                Rebuild();
            }
        }

        bool m_Reorderable;

        public bool horizontalScrollingEnabled
        {
            get => m_HorizontalScrollingEnabled;
            set
            {
                if (m_HorizontalScrollingEnabled == value) return;
                m_HorizontalScrollingEnabled = value;
                m_ScrollView.horizontalScrollerVisibility = value ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
                m_ScrollView.mode = value ? ScrollViewMode.VerticalAndHorizontal : ScrollViewMode.Vertical;
            }
        }

        public AlternatingRowBackground showAlternatingRowBackgrounds
        {
            get => m_ShowAlternatingRowBackgrounds;
            set
            {
                if (m_ShowAlternatingRowBackgrounds == value) return;
                m_ShowAlternatingRowBackgrounds = value;
                RefreshItems();
            }
        }

        public CollectionVirtualizationMethod virtualizationMethod
        {
            get => m_VirtualizationMethod;
            set
            {
                if (m_VirtualizationMethod == value) return;
                m_VirtualizationMethod = value;
                CreateVirtualizationController();
                Rebuild();
            }
        }

        [Obsolete("itemHeight is deprecated, use fixedItemHeight instead.")]
        public int itemHeight
        {
            get => (int)fixedItemHeight;
            set => fixedItemHeight = value;
        }

        public float fixedItemHeight
        {
            get => m_FixedItemHeight;
            set
            {
                if (value < 0f) throw new ArgumentOutOfRangeException(nameof(fixedItemHeight), "Value needs to be positive for virtualization.");
                m_ItemHeightIsInline = true;
                if (Math.Abs(m_FixedItemHeight - value) <= float.Epsilon) return;
                m_FixedItemHeight = value;
                RefreshItems();
            }
        }

        internal float lastHeight => m_LastHeight;

        internal bool isRebuildScheduled => m_RebuildScheduled?.isActive ?? false;

        [Obsolete("onItemsChosen is deprecated, use itemsChosen instead")]
        public event Action<IEnumerable<object>> onItemsChosen
        {
            add => itemsChosen += value;
            remove => itemsChosen -= value;
        }

        public event Action<IEnumerable<object>> itemsChosen;

        [Obsolete("onSelectionChange is deprecated, use selectionChanged instead")]
        public event Action<IEnumerable<object>> onSelectionChange
        {
            add => selectionChanged += value;
            remove => selectionChanged -= value;
        }

        public event Action<IEnumerable<object>> selectionChanged;

        [Obsolete("onSelectedIndicesChange is deprecated, use selectedIndicesChanged instead")]
        public event Action<IEnumerable<int>> onSelectedIndicesChange
        {
            add => selectedIndicesChanged += value;
            remove => selectedIndicesChanged -= value;
        }

        public event Action<IEnumerable<int>> selectedIndicesChanged;

        public event Action<int, int> itemIndexChanged;

        public event Action itemsSourceChanged;

        internal event Action selectionNotChanged;

        internal float ResolveItemHeight(float height = -1f) => height < 0f ? fixedItemHeight : height;

        private protected virtual void CreateVirtualizationController() => CreateVirtualizationController<ReusableCollectionItem>();

        internal CollectionVirtualizationController GetOrCreateVirtualizationController()
        {
            if (m_VirtualizationController == null) CreateVirtualizationController();
            return m_VirtualizationController;
        }

        internal void CreateVirtualizationController<T>() where T : ReusableCollectionItem, new()
        {
            switch (virtualizationMethod)
            {
                case CollectionVirtualizationMethod.FixedHeight:
                    m_VirtualizationController = new FixedHeightVirtualizationController<T>(this);
                    break;
                case CollectionVirtualizationMethod.DynamicHeight:
                    Debug.LogError("Unbound: dynamic height list virtualization is not supported yet; items are laid out with the fixed item height");
                    m_VirtualizationController = new FixedHeightVirtualizationController<T>(this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(virtualizationMethod), virtualizationMethod, "Unsupported virtualizationMethod virtualization");
            }
        }

        internal CollectionViewController GetOrCreateViewController()
        {
            if (m_ViewController == null) SetViewController(CreateViewController());
            return m_ViewController;
        }

        protected abstract CollectionViewController CreateViewController();

        public virtual void SetViewController(CollectionViewController controller)
        {
            if (m_ViewController != null)
            {
                m_ViewController.itemIndexChanged -= m_ItemIndexChangedCallback;
                m_ViewController.itemsSourceChanged -= m_ItemsSourceChangedCallback;
                m_ViewController.Dispose();
                m_ViewController = null;
            }
            m_ViewController = controller;
            if (m_ViewController == null) return;
            m_ViewController.SetView(this);
            m_ViewController.itemIndexChanged += m_ItemIndexChangedCallback;
            m_ViewController.itemsSourceChanged += m_ItemsSourceChangedCallback;
        }

        public BaseVerticalCollectionView()
        {
            AddToClassList(ussClassName);
            m_Selection = new Selection { selectedIds = m_SelectedIds };
            selectionType = SelectionType.Single;
            m_ScrollView = new ScrollView();
            m_ScrollView.AddToClassList(listScrollViewUssClassName);
            m_ScrollView.verticalScroller.valueChanged += v => OnScroll(new Vector2(0f, v));
            m_ScrollView.RegisterCallback<GeometryChangedEvent>(OnSizeChanged);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            m_ScrollView.contentContainer.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_ScrollView.contentContainer.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            hierarchy.Add(m_ScrollView);
            m_ScrollView.contentContainer.focusable = true;
            m_ScrollView.contentContainer.usageHints &= ~UsageHints.GroupTransform;
            m_ScrollView.viewDataKey = "unity-vertical-collection-scroll-view";
            m_ScrollView.verticalScroller.viewDataKey = null;
            m_ScrollView.horizontalScroller.viewDataKey = null;
            focusable = true;
            isCompositeRoot = true;
            delegatesFocus = true;
            m_ItemIndexChangedCallback = OnItemIndexChanged;
            m_ItemsSourceChangedCallback = OnItemsSourceChanged;
        }

        public BaseVerticalCollectionView(IList itemsSource, float itemHeight = -1f) : this()
        {
            if (Math.Abs(itemHeight - ItemHeightUnset) > float.Epsilon)
            {
                m_FixedItemHeight = itemHeight;
                m_ItemHeightIsInline = true;
            }
            if (itemsSource != null) this.itemsSource = itemsSource;
        }

        [Obsolete("makeItem and bindItem are now in ListView and TreeView directly, please use a constructor without these parameters.")]
        public BaseVerticalCollectionView(IList itemsSource, float itemHeight = -1f, Func<VisualElement> makeItem = null, Action<VisualElement, int> bindItem = null) : this()
        {
            if (Math.Abs(itemHeight - ItemHeightUnset) > float.Epsilon)
            {
                m_FixedItemHeight = itemHeight;
                m_ItemHeightIsInline = true;
            }
            this.itemsSource = itemsSource;
        }

        public VisualElement GetRootElementForId(int id) => activeItems.FirstOrDefault(t => t.id == id)?.rootElement;

        public VisualElement GetRootElementForIndex(int index) => GetRootElementForId(viewController.GetIdForIndex(index));

        internal virtual bool HasValidDataAndBindings() => m_ViewController != null && itemsSource != null;

        void OnItemIndexChanged(int srcIndex, int dstIndex)
        {
            itemIndexChanged?.Invoke(srcIndex, dstIndex);
            RefreshItems();
        }

        void OnItemsSourceChanged() => itemsSourceChanged?.Invoke();

        public void RefreshItem(int index)
        {
            foreach (var item in activeItems)
            {
                if (item.index != index) continue;
                viewController.InvokeUnbindItem(item, index);
                viewController.InvokeBindItem(item, index);
                break;
            }
        }

        public void RefreshItems()
        {
            if (m_ViewController == null) return;
            if (m_RebuildScheduled != null && m_RebuildScheduled.isActive)
            {
                Rebuild();
                return;
            }
            m_ViewController.PreRefresh();
            RefreshSelection();
            virtualizationController.Refresh(false);
            PostRefresh();
        }

        [Obsolete("Refresh() has been deprecated. Use Rebuild() instead. (UnityUpgradable) -> Rebuild()")]
        public void Refresh() => Rebuild();

        public void Rebuild()
        {
            if (m_ViewController == null) return;
            m_ViewController.PreRefresh();
            RefreshSelection();
            virtualizationController.Refresh(true);
            PostRefresh();
            m_RebuildScheduled?.Pause();
        }

        internal void ScheduleRebuild()
        {
            if (m_RebuildScheduled == null) m_RebuildScheduled = schedule.Execute(Rebuild);
            else if (!m_RebuildScheduled.isActive) m_RebuildScheduled.Resume();
        }

        void RefreshSelection()
        {
            bool changed = false;
            int previousCount = m_Selection.indexCount;
            m_Selection.items.Clear();
            if (viewController?.itemsSource == null)
            {
                m_Selection.ClearIndices();
            }
            else if (m_Selection.idCount > 0)
            {
                var indices = new List<int>();
                foreach (int id in m_Selection.selectedIds)
                {
                    int index = viewController.GetIndexForId(id);
                    if (index < 0)
                    {
                        changed = true;
                        continue;
                    }
                    if (!m_Selection.ContainsIndex(index)) changed = true;
                    indices.Add(index);
                }
                m_Selection.ClearIndices();
                foreach (int index in indices) m_Selection.AddIndex(index, viewController.GetItemForIndex(index));
            }
            else if (m_Selection.indexCount > 0)
            {
                m_Selection.ClearIndices();
                changed = true;
            }
            if (changed || m_Selection.indexCount != previousCount) NotifyOfSelectionChange();
        }

        private protected virtual void PostRefresh()
        {
            if (!HasValidDataAndBindings()) return;
            m_LastHeight = m_ScrollView.layout.height;
            if (panel != null && !float.IsNaN(m_ScrollView.layout.height)) Resize(m_ScrollView.layout.size);
        }

        public void ScrollTo(VisualElement visualElement) => m_ScrollView.ScrollTo(visualElement);

        public void ScrollToItem(int index)
        {
            if (HasValidDataAndBindings()) virtualizationController.ScrollToItem(index);
        }

        [Obsolete("ScrollToId() has been deprecated. Use ScrollToItemById() instead. (UnityUpgradable) -> ScrollToItemById(*)")]
        public void ScrollToId(int id) => ScrollToItemById(id);

        public void ScrollToItemById(int id)
        {
            if (HasValidDataAndBindings()) virtualizationController.ScrollToItem(viewController.GetIndexForId(id));
        }

        void OnScroll(Vector2 offset)
        {
            if (HasValidDataAndBindings()) virtualizationController.OnScroll(offset);
        }

        void Resize(Vector2 size)
        {
            virtualizationController.Resize(size);
            m_LastHeight = size.y;
            virtualizationController.UpdateBackground();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (evt.destinationPanel == null) return;
            m_ScrollView.contentContainer.AddManipulator(m_NavigationManipulator = new KeyboardNavigationManipulator(Apply));
            m_ScrollView.contentContainer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_ScrollView.contentContainer.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.contentContainer.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_ScrollView.contentContainer.RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (evt.originPanel == null) return;
            m_ScrollView.contentContainer.RemoveManipulator(m_NavigationManipulator);
            m_ScrollView.contentContainer.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            m_ScrollView.contentContainer.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.contentContainer.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_ScrollView.contentContainer.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        }

        [Obsolete("OnKeyDown is obsolete and will be removed from ListView. Use the event system instead, i.e. SendEvent(EventBase e).")]
        public void OnKeyDown(KeyDownEvent evt) => m_NavigationManipulator.OnKeyDown(evt);

        bool Apply(KeyboardNavigationOperation op, bool shiftKey, bool altKey)
        {
            if (selectionType == SelectionType.None || !HasValidDataAndBindings()) return false;
            void SelectAndScroll(int index)
            {
                if (index < 0 || index >= m_ViewController.itemsSource.Count) return;
                if (selectionType == SelectionType.Multiple && shiftKey && m_Selection.indexCount != 0) DoRangeSelection(index);
                else selectedIndex = index;
                ScrollToItem(index);
            }
            switch (op)
            {
                case KeyboardNavigationOperation.SelectAll:
                    SelectAll();
                    return true;
                case KeyboardNavigationOperation.Cancel:
                    ClearSelection();
                    return true;
                case KeyboardNavigationOperation.Submit:
                    itemsChosen?.Invoke(selectedItems);
                    ScrollToItem(selectedIndex);
                    return true;
                case KeyboardNavigationOperation.Previous:
                    if (selectedIndex > 0)
                    {
                        SelectAndScroll(selectedIndex - 1);
                        return true;
                    }
                    break;
                case KeyboardNavigationOperation.Next:
                    if (selectedIndex + 1 < m_ViewController.itemsSource.Count)
                    {
                        SelectAndScroll(selectedIndex + 1);
                        return true;
                    }
                    break;
                case KeyboardNavigationOperation.Begin:
                    SelectAndScroll(0);
                    return true;
                case KeyboardNavigationOperation.End:
                    SelectAndScroll(m_ViewController.itemsSource.Count - 1);
                    return true;
                case KeyboardNavigationOperation.PageDown:
                    if (m_Selection.indexCount > 0)
                    {
                        int from = m_IsRangeSelectionDirectionUp ? m_Selection.minIndex : m_Selection.maxIndex;
                        SelectAndScroll(Mathf.Min(viewController.itemsSource.Count - 1, from + (virtualizationController.visibleItemCount - 1)));
                    }
                    return true;
                case KeyboardNavigationOperation.PageUp:
                    if (m_Selection.indexCount > 0)
                    {
                        int from = m_IsRangeSelectionDirectionUp ? m_Selection.minIndex : m_Selection.maxIndex;
                        SelectAndScroll(Mathf.Max(0, from - (virtualizationController.visibleItemCount - 1)));
                    }
                    return true;
                case KeyboardNavigationOperation.MoveRight:
                    if (m_Selection.indexCount > 0) return HandleItemNavigation(true, altKey);
                    break;
                case KeyboardNavigationOperation.MoveLeft:
                    if (m_Selection.indexCount > 0) return HandleItemNavigation(false, altKey);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(op), op, null);
            }
            return false;
        }

        void Apply(KeyboardNavigationOperation op, EventBase sourceEvent)
        {
            bool shift = sourceEvent is KeyDownEvent { shiftKey: true } || sourceEvent is INavigationEvent { shiftKey: true };
            bool alt = sourceEvent is KeyDownEvent { altKey: true } || sourceEvent is INavigationEvent { altKey: true };
            if (Apply(op, shift, alt)) sourceEvent.StopPropagation();
            focusController?.IgnoreEvent(sourceEvent);
        }

        private protected virtual bool HandleItemNavigation(bool moveIn, bool altKey) => false;

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.button != 0) return;
            if ((evt.pressedButtons & 1) == 0) ProcessPointerUp(evt);
            else ProcessPointerDown(evt);
        }

        void OnPointerDown(PointerDownEvent evt) => ProcessPointerDown(evt);

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (HasValidDataAndBindings() && evt.isPrimary) ClearSelection();
        }

        void OnPointerUp(PointerUpEvent evt) => ProcessPointerUp(evt);

        void ProcessPointerDown(IPointerEvent evt)
        {
            if (!HasValidDataAndBindings() || !evt.isPrimary) return;
            if (evt.button != 0 && evt.button != 1) return;
            if (evt.pointerType != PointerType.mouse)
            {
                m_TouchDownPosition = evt.position;
                long timestamp = (evt as PointerDownEvent)?.timestamp ?? 0;
                m_PointerDownCount = timestamp - m_LastPointerDownTimeStamp >= Event.GetDoubleClickTime() ? 1 : m_PointerDownCount + 1;
                m_LastPointerDownTimeStamp = timestamp;
            }
            else
            {
                m_PointerDownCount = evt.clickCount;
                DoSelect(evt.localPosition, evt.button, m_PointerDownCount, evt.actionKey, evt.shiftKey);
            }
        }

        void ProcessPointerUp(IPointerEvent evt)
        {
            if (!HasValidDataAndBindings() || !evt.isPrimary) return;
            if (evt.button != 0 && evt.button != 1) return;
            if (evt.pointerType != PointerType.mouse)
            {
                if ((evt.position - m_TouchDownPosition).sqrMagnitude <= 100f)
                    DoSelect(evt.localPosition, evt.button, m_PointerDownCount, evt.actionKey, evt.shiftKey);
                long timestamp = (evt as PointerUpEvent)?.timestamp ?? 0;
                m_PointerDownCount = timestamp - m_LastPointerDownTimeStamp < Event.GetDoubleClickTime() ? m_PointerDownCount : 0;
            }
            else
            {
                int index = virtualizationController.GetIndexFromPosition(evt.localPosition);
                if (selectionType == SelectionType.Multiple && evt.button == 0 && !evt.shiftKey && !evt.actionKey && m_Selection.indexCount > 1 && m_Selection.ContainsIndex(index))
                    SetSelection(index);
            }
        }

        void DoSelect(Vector2 localPosition, int mouseButton, int clickCount, bool actionKey, bool shiftKey)
        {
            int index = virtualizationController.GetIndexFromPosition(localPosition);
            int clicks = m_Selection.indexCount > 0 && m_Selection.FirstIndex() != index ? 1 : clickCount > 2 ? 2 : clickCount;
            if (index > viewController.itemsSource.Count - 1 || selectionType == SelectionType.None) return;
            int id = viewController.GetIdForIndex(index);
            if (clicks == 1)
            {
                if (selectionType == SelectionType.Multiple && actionKey)
                {
                    if (m_Selection.ContainsId(id)) RemoveFromSelection(index);
                    else AddToSelection(index);
                    return;
                }
                if (selectionType == SelectionType.Multiple && shiftKey)
                {
                    if (m_Selection.indexCount == 0) SetSelection(index);
                    else DoRangeSelection(index);
                    return;
                }
                if (selectionType == SelectionType.Multiple && m_Selection.ContainsIndex(index))
                {
                    selectionNotChanged?.Invoke();
                    return;
                }
                if (selectionType == SelectionType.Single && m_Selection.ContainsIndex(index)) selectionNotChanged?.Invoke();
                else SetSelection(index);
                if (allowSingleClickChoice && mouseButton == 0) itemsChosen?.Invoke(selectedItems);
            }
            else if (clicks == 2)
            {
                if (itemsChosen == null) return;
                bool wasSelected = selectedIndices.Contains(index);
                SetSelection(index);
                if (wasSelected && !allowSingleClickChoice && mouseButton == 0) itemsChosen?.Invoke(selectedItems);
            }
        }

        internal void DoRangeSelection(int rangeSelectionFinalIndex)
        {
            int anchor = m_IsRangeSelectionDirectionUp ? m_Selection.maxIndex : m_Selection.minIndex;
            ClearSelectionWithoutValidation();
            var range = new List<int>();
            m_IsRangeSelectionDirectionUp = rangeSelectionFinalIndex < anchor;
            if (m_IsRangeSelectionDirectionUp)
                for (int i = rangeSelectionFinalIndex; i <= anchor; i++) range.Add(i);
            else
                for (int i = rangeSelectionFinalIndex; i >= anchor; i--) range.Add(i);
            AddToSelection(range);
        }

        internal void SelectAll()
        {
            if (!HasValidDataAndBindings() || selectionType != SelectionType.Multiple) return;
            for (int i = 0; i < m_ViewController.itemsSource.Count; i++)
            {
                int id = viewController.GetIdForIndex(i);
                object item = viewController.GetItemForIndex(i);
                foreach (var active in activeItems)
                    if (active.id == id) active.SetSelected(true);
                if (m_Selection.ContainsId(id)) continue;
                m_Selection.AddId(id);
                m_Selection.AddIndex(i, item);
            }
            NotifyOfSelectionChange();
        }

        public void AddToSelection(int index) => AddToSelection(new[] { index });

        internal void AddToSelection(IList<int> indexes)
        {
            if (!HasValidDataAndBindings() || indexes == null || indexes.Count == 0) return;
            foreach (int index in indexes) AddToSelectionWithoutValidation(index);
            NotifyOfSelectionChange();
        }

        void AddToSelectionWithoutValidation(int index)
        {
            if (m_Selection.ContainsIndex(index)) return;
            int id = viewController.GetIdForIndex(index);
            object item = viewController.GetItemForIndex(index);
            foreach (var active in activeItems)
                if (active.id == id) active.SetSelected(true);
            m_Selection.AddId(id);
            m_Selection.AddIndex(index, item);
        }

        public void RemoveFromSelection(int index)
        {
            if (!HasValidDataAndBindings()) return;
            RemoveFromSelectionWithoutValidation(index);
            NotifyOfSelectionChange();
        }

        void RemoveFromSelectionWithoutValidation(int index)
        {
            if (!m_Selection.TryRemove(index)) return;
            int id = viewController.GetIdForIndex(index);
            foreach (var active in activeItems)
                if (active.id == id) active.SetSelected(false);
            m_Selection.RemoveId(id);
        }

        public void SetSelection(int index)
        {
            if (index < 0)
            {
                ClearSelection();
                return;
            }
            SetSelection(new[] { index });
        }

        public void SetSelection(IEnumerable<int> indices) => SetSelectionInternal(indices, true);

        public void SetSelectionWithoutNotify(IEnumerable<int> indices) => SetSelectionInternal(indices, false);

        internal void SetSelectionInternal(IEnumerable<int> indices, bool sendNotification)
        {
            if (!HasValidDataAndBindings() || indices == null || MatchesExistingSelection(indices)) return;
            ClearSelectionWithoutValidation();
            if (indices is ICollection collection && m_Selection.capacity < collection.Count) m_Selection.capacity = collection.Count;
            foreach (int index in indices) AddToSelectionWithoutValidation(index);
            if (sendNotification) NotifyOfSelectionChange();
        }

        bool MatchesExistingSelection(IEnumerable<int> indices)
        {
            var list = indices as IList<int> ?? indices.ToList();
            if (list.Count != m_Selection.indexCount) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != m_Selection.indices[i]) return false;
            return true;
        }

        void NotifyOfSelectionChange()
        {
            if (!HasValidDataAndBindings()) return;
            selectionChanged?.Invoke(selectedItems);
            selectedIndicesChanged?.Invoke(m_Selection.indices);
        }

        public void ClearSelection()
        {
            if (!HasValidDataAndBindings() || m_Selection.idCount == 0) return;
            ClearSelectionWithoutValidation();
            NotifyOfSelectionChange();
        }

        void ClearSelectionWithoutValidation()
        {
            foreach (var active in activeItems) active.SetSelected(false);
            m_Selection.Clear();
        }

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt.eventTypeId == FocusInEvent.TypeId()) m_VirtualizationController?.OnFocusIn(evt.elementTarget);
            else if (evt.eventTypeId == FocusOutEvent.TypeId()) m_VirtualizationController?.OnFocusOut(((FocusOutEvent)evt).relatedTarget as VisualElement);
            else if (evt.eventTypeId == NavigationSubmitEvent.TypeId() && evt.target == this) m_ScrollView.contentContainer.Focus();
        }

        [Obsolete("ExecuteDefaultAction override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.", false)]
        protected override void ExecuteDefaultAction(EventBase evt) { }

        void OnSizeChanged(GeometryChangedEvent evt)
        {
            if (!HasValidDataAndBindings()) return;
            if (Mathf.Approximately(evt.newRect.width, evt.oldRect.width) && Mathf.Approximately(evt.newRect.height, evt.oldRect.height)) return;
            Resize(evt.newRect.size);
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent e)
        {
            if (m_ItemHeightIsInline || !e.customStyle.TryGetValue(s_ItemHeightProperty, out int height)) return;
            if (Math.Abs(m_FixedItemHeight - height) <= float.Epsilon) return;
            m_FixedItemHeight = height;
            RefreshItems();
        }
    }

    public abstract partial class BaseListView : BaseVerticalCollectionView
    {
        public new static readonly string ussClassName = "unity-list-view";
        public new static readonly string itemUssClassName = ussClassName + "__item";
        public static readonly string emptyLabelUssClassName = ussClassName + "__empty-label";
        public static readonly string overMaxMultiEditLimitClassName = ussClassName + "__over-max-multi-edit-limit-label";
        public static readonly string reorderableUssClassName = ussClassName + "__reorderable";
        public static readonly string reorderableItemUssClassName = reorderableUssClassName + "-item";
        public static readonly string reorderableItemContainerUssClassName = reorderableItemUssClassName + "__container";
        public static readonly string reorderableItemHandleUssClassName = reorderableUssClassName + "-handle";
        public static readonly string reorderableItemHandleBarUssClassName = reorderableItemHandleUssClassName + "-bar";
        public static readonly string footerUssClassName = ussClassName + "__footer";
        public static readonly string foldoutHeaderUssClassName = ussClassName + "__foldout-header";
        public static readonly string arraySizeFieldUssClassName = ussClassName + "__size-field";
        public static readonly string arraySizeFieldWithHeaderUssClassName = arraySizeFieldUssClassName + "--with-header";
        public static readonly string arraySizeFieldWithFooterUssClassName = arraySizeFieldUssClassName + "--with-footer";
        public static readonly string listViewWithHeaderUssClassName = ussClassName + "--with-header";
        public static readonly string listViewWithFooterUssClassName = ussClassName + "--with-footer";
        public static readonly string scrollViewWithFooterUssClassName = ussClassName + "__scroll-view--with-footer";
        public static readonly string footerAddButtonName = ussClassName + "__add-button";
        public static readonly string footerRemoveButtonName = ussClassName + "__remove-button";
        static readonly string k_EmptyListStr = "List is empty";

        bool m_ShowBoundCollectionSize = true;
        bool m_ShowFoldoutHeader;
        string m_HeaderTitle;
        bool m_ShowAddRemoveFooter;
        Label m_ListViewLabel;
        VisualElement m_NoneElement;
        Func<VisualElement> m_MakeNoneElement;
        ListViewReorderMode m_ReorderMode;
        Action<IEnumerable<int>> m_ItemAddedCallback;
        Action<IEnumerable<int>> m_ItemRemovedCallback;
        Action m_ItemsSourceSizeChangedCallback;

        public bool showBoundCollectionSize
        {
            get => m_ShowBoundCollectionSize;
            set => m_ShowBoundCollectionSize = value;
        }

        public bool showFoldoutHeader
        {
            get => m_ShowFoldoutHeader;
            set
            {
                if (m_ShowFoldoutHeader == value) return;
                m_ShowFoldoutHeader = value;
                if (value) Debug.LogError("Unbound: list view foldout headers are not supported yet");
            }
        }

        public string headerTitle
        {
            get => m_HeaderTitle;
            set => m_HeaderTitle = value;
        }

        public bool showAddRemoveFooter
        {
            get => m_ShowAddRemoveFooter;
            set
            {
                if (m_ShowAddRemoveFooter == value) return;
                m_ShowAddRemoveFooter = value;
                if (value) Debug.LogError("Unbound: list view add/remove footers are not supported yet");
            }
        }

        public new BaseListViewController viewController => base.viewController as BaseListViewController;

        public ListViewReorderMode reorderMode
        {
            get => m_ReorderMode;
            set => m_ReorderMode = value;
        }

        public Func<VisualElement> makeNoneElement
        {
            get => m_MakeNoneElement;
            set
            {
                m_MakeNoneElement = value;
                m_NoneElement?.RemoveFromHierarchy();
                m_NoneElement = null;
                RefreshItems();
            }
        }

        public bool allowAdd { get; set; } = true;

        public Action<BaseListView, Button> overridingAddButtonBehavior { get; set; }

        public Action<BaseListView> onAdd { get; set; }

        public bool allowRemove { get; set; } = true;

        public Action<BaseListView> onRemove { get; set; }

        public event Action<IEnumerable<int>> itemsAdded;

        public event Action<IEnumerable<int>> itemsRemoved;

        internal event Action itemsSourceSizeChanged;

        /// <summary>An empty list shows "List is empty" (or the none element) in its viewport.</summary>
        internal void UpdateListViewLabel()
        {
            if (!HasValidDataAndBindings()) return;
            bool empty = itemsSource.Count == 0;
            if (empty)
            {
                if (m_MakeNoneElement != null)
                {
                    m_NoneElement ??= m_MakeNoneElement();
                    scrollView.contentViewport.Add(m_NoneElement);
                    m_ListViewLabel?.RemoveFromHierarchy();
                    m_ListViewLabel = null;
                }
                else
                {
                    m_ListViewLabel ??= new Label();
                    m_ListViewLabel.text = k_EmptyListStr;
                    scrollView.contentViewport.Add(m_ListViewLabel);
                    m_NoneElement?.RemoveFromHierarchy();
                    m_NoneElement = null;
                }
            }
            else
            {
                m_NoneElement?.RemoveFromHierarchy();
                m_NoneElement = null;
                m_ListViewLabel?.RemoveFromHierarchy();
                m_ListViewLabel = null;
            }
            m_ListViewLabel?.EnableInClassList(emptyLabelUssClassName, empty);
            m_ListViewLabel?.EnableInClassList(overMaxMultiEditLimitClassName, false);
        }

        private protected override void CreateVirtualizationController() => CreateVirtualizationController<ReusableListViewItem>();

        public override void SetViewController(CollectionViewController controller)
        {
            m_ItemAddedCallback ??= indices => itemsAdded?.Invoke(indices);
            m_ItemRemovedCallback ??= indices => itemsRemoved?.Invoke(indices);
            m_ItemsSourceSizeChangedCallback ??= OnItemsSourceSizeChanged;
            if (viewController != null)
            {
                viewController.itemsAdded -= m_ItemAddedCallback;
                viewController.itemsRemoved -= m_ItemRemovedCallback;
                viewController.itemsSourceSizeChanged -= m_ItemsSourceSizeChangedCallback;
            }
            base.SetViewController(controller);
            if (viewController != null)
            {
                viewController.itemsAdded += m_ItemAddedCallback;
                viewController.itemsRemoved += m_ItemRemovedCallback;
                viewController.itemsSourceSizeChanged += m_ItemsSourceSizeChangedCallback;
            }
        }

        void OnItemsSourceSizeChanged()
        {
            RefreshItems();
            itemsSourceSizeChanged?.Invoke();
        }

        public BaseListView() : this(null) { }

        public BaseListView(IList itemsSource, float itemHeight = -1f) : base(itemsSource, itemHeight)
        {
            AddToClassList(ussClassName);
            pickingMode = PickingMode.Ignore;
            allowAdd = true;
            allowRemove = true;
        }

        private protected override void PostRefresh()
        {
            UpdateListViewLabel();
            base.PostRefresh();
        }

        private protected override bool HandleItemNavigation(bool moveIn, bool altPressed) => false;
    }

    public partial class ListView : BaseListView
    {
        Func<VisualElement> m_MakeItem;
        readonly Func<VisualElement> m_TemplateMakeItem;
        VisualTreeAsset m_ItemTemplate;
        Action<VisualElement, int> m_BindItem;
        Action<VisualElement, int> m_UnbindItem;
        Action<VisualElement> m_DestroyItem;

        public new Func<VisualElement> makeItem
        {
            get => m_MakeItem;
            set
            {
                if (value == m_MakeItem) return;
                m_MakeItem = value;
                Rebuild();
            }
        }

        public VisualTreeAsset itemTemplate
        {
            get => m_ItemTemplate;
            set
            {
                if (m_ItemTemplate == value) return;
                m_ItemTemplate = value;
                if (m_TemplateMakeItem != makeItem) makeItem = m_TemplateMakeItem;
                else Rebuild();
            }
        }

        public new Action<VisualElement, int> bindItem
        {
            get => m_BindItem;
            set
            {
                if (value == m_BindItem) return;
                m_BindItem = value;
                RefreshItems();
            }
        }

        public new Action<VisualElement, int> unbindItem
        {
            get => m_UnbindItem;
            set => m_UnbindItem = value;
        }

        public new Action<VisualElement> destroyItem
        {
            get => m_DestroyItem;
            set => m_DestroyItem = value;
        }

        internal void SetMakeItemWithoutNotify(Func<VisualElement> func) => m_MakeItem = func;

        VisualElement TemplateMakeItem() => m_ItemTemplate != null ? m_ItemTemplate.Instantiate() : new Label(k_InvalidTemplateError);

        internal void SetBindItemWithoutNotify(Action<VisualElement, int> callback) => m_BindItem = callback;

        internal override bool HasValidDataAndBindings() => base.HasValidDataAndBindings() && (makeItem != null) == (bindItem != null);

        protected override CollectionViewController CreateViewController() => new ListViewController();

        public ListView()
        {
            AddToClassList(BaseListView.ussClassName);
            m_TemplateMakeItem = TemplateMakeItem;
        }

        public ListView(IList itemsSource, float itemHeight = -1f, Func<VisualElement> makeItem = null, Action<VisualElement, int> bindItem = null) : base(itemsSource, itemHeight)
        {
            AddToClassList(BaseListView.ussClassName);
            m_TemplateMakeItem = TemplateMakeItem;
            this.makeItem = makeItem;
            this.bindItem = bindItem;
        }
    }

    /// <summary>The data side of a collection view: its items source and how items are made and bound.</summary>
    public abstract partial class CollectionViewController : IDisposable
    {
        BaseVerticalCollectionView m_View;
        IList m_ItemsSource;

        public virtual IList itemsSource
        {
            get => m_ItemsSource;
            set
            {
                if (m_ItemsSource == value) return;
                m_ItemsSource = value;
                m_View.RefreshItems();
                RaiseItemsSourceChanged();
            }
        }

        protected BaseVerticalCollectionView view => m_View;

        public event Action itemsSourceChanged;

        public event Action<int, int> itemIndexChanged;

        protected void SetItemsSourceWithoutNotify(IList source) => m_ItemsSource = source;

        public void SetView(BaseVerticalCollectionView collectionView)
        {
            m_View = collectionView;
            PrepareView();
            if (m_View == null) throw new InvalidOperationException("View must not be null.");
        }

        protected virtual void PrepareView() { }

        public virtual void Dispose()
        {
            itemsSourceChanged = null;
            itemIndexChanged = null;
            m_View = null;
        }

        public virtual int GetItemsCount() => m_ItemsSource?.Count ?? 0;

        internal virtual int GetItemsMinCount() => GetItemsCount();

        public virtual int GetIndexForId(int id) => id;

        public virtual int GetIdForIndex(int index) => index;

        public virtual object GetItemForIndex(int index)
        {
            if (m_ItemsSource == null || index < 0 || index >= m_ItemsSource.Count) return null;
            return m_ItemsSource[index];
        }

        public virtual object GetItemForId(int id)
        {
            if (m_ItemsSource == null) return null;
            int index = GetIndexForId(id);
            return index < 0 || index >= m_ItemsSource.Count ? null : m_ItemsSource[index];
        }

        internal virtual void InvokeMakeItem(ReusableCollectionItem reusableItem) => reusableItem.Init(MakeItem());

        internal virtual void SetBindingContext(ReusableCollectionItem reusableItem, int index) { }

        internal virtual void InvokeBindItem(ReusableCollectionItem reusableItem, int index)
        {
            BindItem(reusableItem.bindableElement, index);
            SetBindingContext(reusableItem, index);
            reusableItem.SetSelected(m_View.selectedIndices.Contains(index));
            reusableItem.rootElement.pseudoStates &= ~PseudoStates.Hover;
            reusableItem.index = index;
        }

        internal virtual void InvokeUnbindItem(ReusableCollectionItem reusableItem, int index)
        {
            UnbindItem(reusableItem.bindableElement, index);
            reusableItem.index = -1;
        }

        internal virtual void InvokeDestroyItem(ReusableCollectionItem reusableItem) => DestroyItem(reusableItem.bindableElement);

        internal virtual void PreRefresh() { }

        protected abstract VisualElement MakeItem();

        protected abstract void BindItem(VisualElement element, int index);

        protected abstract void UnbindItem(VisualElement element, int index);

        protected abstract void DestroyItem(VisualElement element);

        protected void RaiseItemsSourceChanged() => itemsSourceChanged?.Invoke();

        protected void RaiseItemIndexChanged(int srcIndex, int dstIndex) => itemIndexChanged?.Invoke(srcIndex, dstIndex);
    }

    public abstract partial class BaseListViewController : CollectionViewController
    {
        protected BaseListView baseListView => view as BaseListView;

        public event Action itemsSourceSizeChanged;

        public event Action<IEnumerable<int>> itemsAdded;

        public event Action<IEnumerable<int>> itemsRemoved;

        internal override void InvokeMakeItem(ReusableCollectionItem reusableItem)
        {
            if (!(reusableItem is ReusableListViewItem listItem)) return;
            listItem.Init(MakeItem(), false);
            PostInitRegistration(listItem);
        }

        /// <summary>Items are laid out by the list: no margins top and bottom, no growing or shrinking.</summary>
        internal void PostInitRegistration(ReusableListViewItem listItem)
        {
            var s = listItem.bindableElement.style;
            s.position = Position.Relative;
            s.flexBasis = StyleKeyword.Initial;
            s.marginTop = 0f;
            s.marginBottom = 0f;
            s.paddingTop = 0f;
            s.flexGrow = 0f;
            s.flexShrink = 0f;
        }

        internal override void SetBindingContext(ReusableCollectionItem reusableItem, int index) => base.SetBindingContext(reusableItem, index);

        internal override void InvokeBindItem(ReusableCollectionItem reusableItem, int index) => base.InvokeBindItem(reusableItem, index);

        public virtual bool NeedsDragHandle(int index) => true;

        internal virtual void RemoveItems(int itemCount)
        {
            if (itemCount <= 0) return;
            int count = GetItemsCount();
            var indices = new List<int>();
            for (int i = count - itemCount; i < count; i++) indices.Add(i);
            RemoveItems(indices);
        }

        public virtual void AddItems(int itemCount)
        {
            if (itemCount <= 0) return;
            EnsureItemSourceCanBeResized();
            int count = GetItemsCount();
            var added = new List<int>();
            if (itemsSource.IsFixedSize)
            {
                itemsSource = AddToArray((Array)itemsSource, itemCount);
                for (int i = 0; i < itemCount; i++) added.Add(count + i);
            }
            else
            {
                var listType = itemsSource.GetType().GetInterfaces().FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>));
                bool valueType = listType != null && listType.GetGenericArguments()[0].IsValueType;
                for (int i = 0; i < itemCount; i++)
                {
                    added.Add(count + i);
                    itemsSource.Add(valueType ? Activator.CreateInstance(listType.GetGenericArguments()[0]) : null);
                }
            }
            RaiseItemsAdded(added);
            RaiseOnSizeChanged();
        }

        public virtual void Move(int index, int newIndex)
        {
            if (itemsSource == null || index == newIndex) return;
            if (Mathf.Min(index, newIndex) < 0 || Mathf.Max(index, newIndex) >= itemsSource.Count) return;
            int destination = newIndex;
            int step = newIndex < index ? 1 : -1;
            while (Mathf.Min(index, newIndex) < Mathf.Max(index, newIndex))
            {
                Swap(index, newIndex);
                newIndex += step;
            }
            RaiseItemIndexChanged(index, destination);
        }

        public virtual void RemoveItem(int index) => RemoveItems(new List<int> { index });

        public virtual void RemoveItems(List<int> indices)
        {
            EnsureItemSourceCanBeResized();
            if (indices == null) return;
            indices.Sort();
            RaiseItemsRemoved(indices);
            if (itemsSource.IsFixedSize)
            {
                itemsSource = RemoveFromArray((Array)itemsSource, indices);
            }
            else
            {
                for (int i = indices.Count - 1; i >= 0; i--) itemsSource.RemoveAt(indices[i]);
            }
            RaiseOnSizeChanged();
        }

        public virtual void ClearItems()
        {
            if (itemsSource == null) return;
            EnsureItemSourceCanBeResized();
            var indices = Enumerable.Range(0, itemsSource.Count - 1);
            itemsSource.Clear();
            RaiseItemsRemoved(indices);
            RaiseOnSizeChanged();
        }

        protected void RaiseOnSizeChanged() => itemsSourceSizeChanged?.Invoke();

        protected void RaiseItemsAdded(IEnumerable<int> indices) => itemsAdded?.Invoke(indices);

        protected void RaiseItemsRemoved(IEnumerable<int> indices) => itemsRemoved?.Invoke(indices);

        static Array AddToArray(Array source, int itemCount)
        {
            var elementType = source.GetType().GetElementType() ?? throw new InvalidOperationException("Cannot resize source, because its size is fixed.");
            var array = Array.CreateInstance(elementType, source.Length + itemCount);
            Array.Copy(source, array, source.Length);
            return array;
        }

        static Array RemoveFromArray(Array source, List<int> indicesToRemove)
        {
            int length = source.Length;
            int remaining = length - indicesToRemove.Count;
            if (remaining < 0) throw new InvalidOperationException("Cannot remove more items than the current count from source.");
            var elementType = source.GetType().GetElementType();
            var array = Array.CreateInstance(elementType, remaining);
            int at = 0;
            for (int i = 0; i < length; i++)
                if (!indicesToRemove.Contains(i)) array.SetValue(source.GetValue(i), at++);
            return array;
        }

        void Swap(int lhs, int rhs) => (itemsSource[lhs], itemsSource[rhs]) = (itemsSource[rhs], itemsSource[lhs]);

        void EnsureItemSourceCanBeResized()
        {
            if (itemsSource == null) throw new InvalidOperationException("Unable to add or remove items because the source is not defined. Please assign a valid items source.");
            if (itemsSource.IsFixedSize && !itemsSource.GetType().IsArray) throw new InvalidOperationException("Unable to add or remove items because the items source is a fixed size.");
        }
    }

    public partial class ListViewController : BaseListViewController
    {
        protected ListView listView => view as ListView;

        protected override VisualElement MakeItem()
        {
            if (listView.makeItem == null)
            {
                if (listView.bindItem != null) throw new NotImplementedException("You must specify makeItem if bindItem is specified.");
                return new Label();
            }
            return listView.makeItem();
        }

        protected override void BindItem(VisualElement element, int index)
        {
            if (listView.bindItem == null)
            {
                if (listView.makeItem != null) throw new NotImplementedException("You must specify bindItem if makeItem is specified.");
                if (element is Label label)
                {
                    var item = listView.itemsSource[index];
                    label.text = item?.ToString() ?? "null";
                }
                return;
            }
            listView.bindItem(element, index);
        }

        protected override void UnbindItem(VisualElement element, int index) => listView.unbindItem?.Invoke(element, index);

        protected override void DestroyItem(VisualElement element) => listView.destroyItem?.Invoke(element);
    }

    /// <summary>A pooled item element of a collection view, bound to one index at a time.</summary>
    internal partial class ReusableCollectionItem
    {
        public const int UndefinedIndex = -1;

        public virtual VisualElement rootElement => bindableElement;

        public VisualElement bindableElement { get; protected set; }

        public int index { get; set; }

        public int id { get; set; }

        internal bool isDragGhost { get; private set; }

        internal event Action<ReusableCollectionItem> onDestroy;

        public ReusableCollectionItem()
        {
            index = id = UndefinedIndex;
        }

        public virtual void Init(VisualElement item) => bindableElement = item;

        public virtual void PreAttachElement() => rootElement.AddToClassList(BaseVerticalCollectionView.itemUssClassName);

        public virtual void DetachElement()
        {
            rootElement.RemoveFromClassList(BaseVerticalCollectionView.itemUssClassName);
            rootElement?.RemoveFromHierarchy();
            SetSelected(false);
            SetDragGhost(false);
            index = id = UndefinedIndex;
        }

        public virtual void DestroyElement() => onDestroy?.Invoke(this);

        public virtual void SetSelected(bool selected)
        {
            if (selected)
            {
                rootElement.AddToClassList(BaseVerticalCollectionView.itemSelectedVariantUssClassName);
                rootElement.pseudoStates |= PseudoStates.Checked;
            }
            else
            {
                rootElement.RemoveFromClassList(BaseVerticalCollectionView.itemSelectedVariantUssClassName);
                rootElement.pseudoStates &= ~PseudoStates.Checked;
            }
        }

        public virtual void SetDragGhost(bool dragGhost)
        {
            isDragGhost = dragGhost;
            rootElement.style.maxHeight = isDragGhost ? StyleKeyword.Undefined : StyleKeyword.Initial;
            bindableElement.style.display = isDragGhost ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    internal partial class ReusableListViewItem : ReusableCollectionItem
    {
        public override VisualElement rootElement => bindableElement;

        public override void SetDragGhost(bool dragGhost) => base.SetDragGhost(dragGhost);

        public void Init(VisualElement item, bool usesAnimatedDragger)
        {
            base.Init(item);
            if (usesAnimatedDragger) Debug.LogError("Unbound: animated list view reordering is not supported yet");
        }

        public override void PreAttachElement()
        {
            base.PreAttachElement();
            rootElement.AddToClassList(BaseListView.itemUssClassName);
        }

        public override void DetachElement()
        {
            base.DetachElement();
            rootElement.RemoveFromClassList(BaseListView.itemUssClassName);
        }
    }

    internal abstract partial class CollectionVirtualizationController
    {
        protected readonly ScrollView m_ScrollView;

        public abstract int firstVisibleIndex { get; protected set; }

        public abstract int visibleItemCount { get; }

        public abstract IEnumerable<ReusableCollectionItem> activeItems { get; }

        protected CollectionVirtualizationController(ScrollView scrollView)
        {
            m_ScrollView = scrollView;
        }

        public abstract void Refresh(bool rebuild);

        public abstract void ScrollToItem(int id);

        public abstract void Resize(Vector2 size);

        public abstract void OnScroll(Vector2 offset);

        public abstract int GetIndexFromPosition(Vector2 position);

        public abstract float GetExpectedItemHeight(int index);

        public abstract float GetExpectedContentHeight();

        public abstract void OnFocusIn(VisualElement leafTarget);

        public abstract void OnFocusOut(VisualElement willFocus);

        public abstract void UpdateBackground();

        public abstract void UnbindAll();
    }

    /// <summary>Items recycled as the list scrolls: the active items show consecutive indices from the
    /// first visible one, in content container order.</summary>
    internal abstract partial class VerticalVirtualizationController<T> : CollectionVirtualizationController where T : ReusableCollectionItem, new()
    {
        readonly Stack<T> m_Pool = new Stack<T>();
        protected BaseVerticalCollectionView m_CollectionView;
        protected const int k_ExtraVisibleItems = 2;
        protected List<T> m_ActiveItems;
        int? m_DeferredScrollToItemIndex;
        IVisualElementScheduledItem m_ScheduleDeferredScrollToItem;
        IVisualElementScheduledItem m_ScrollScheduledItem;
        int m_LastFocusedElementIndex = -1;
        readonly List<int> m_LastFocusedElementTreeChildIndexes = new List<int>();
        protected List<T> m_ScrollInsertionList = new List<T>();
        VisualElement m_EmptyRows;
        int m_FirstVisibleIndex;

        public override IEnumerable<ReusableCollectionItem> activeItems => m_ActiveItems;

        internal int itemsCount => m_CollectionView.viewController?.GetItemsCount() ?? m_CollectionView.itemsSource.Count;

        internal T lastVisibleItem
        {
            get
            {
                for (int i = m_ActiveItems.Count - 1; i >= 0; i--)
                    if (VisibleItemPredicate(m_ActiveItems[i])) return m_ActiveItems[i];
                return null;
            }
        }

        public override int visibleItemCount
        {
            get
            {
                int count = 0;
                foreach (var item in m_ActiveItems)
                    if (VisibleItemPredicate(item)) count++;
                return count;
            }
        }

        public override int firstVisibleIndex
        {
            get => Mathf.Min(m_FirstVisibleIndex, m_CollectionView.viewController != null ? m_CollectionView.viewController.GetItemsCount() - 1 : m_FirstVisibleIndex);
            protected set => m_FirstVisibleIndex = value;
        }

        protected float lastHeight => m_CollectionView.lastHeight;

        protected virtual bool alwaysRebindOnRefresh => true;

        protected virtual bool VisibleItemPredicate(T i) => i.rootElement.style.display == DisplayStyle.Flex;

        protected VerticalVirtualizationController(BaseVerticalCollectionView collectionView) : base(collectionView.scrollView)
        {
            m_CollectionView = collectionView;
            m_ActiveItems = new List<T>();
            m_ScrollView.contentContainer.disableClipping = false;
            collectionView.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                if (m_ScrollScheduledItem == null || !m_ScrollScheduledItem.isActive) return;
                m_ScrollScheduledItem.Pause();
                m_ScrollScheduledItem = null;
            });
        }

        public override void Refresh(bool rebuild)
        {
            bool valid = m_CollectionView.HasValidDataAndBindings();
            m_CollectionView.m_PreviousRefreshedCount = m_CollectionView.itemsSource?.Count ?? 0;
            for (int i = 0; i < m_ActiveItems.Count; i++)
            {
                int index = firstVisibleIndex + i;
                var item = m_ActiveItems[i];
                bool shown = item.rootElement.style.display == DisplayStyle.Flex;
                if (rebuild)
                {
                    if (valid && item.index != -1) m_CollectionView.viewController.InvokeUnbindItem(item, item.index);
                    Release(item);
                }
                else if (m_CollectionView.itemsSource != null && index >= 0 && index < itemsCount)
                {
                    if (!valid) continue;
                    if (item.index != -1) m_CollectionView.viewController.InvokeUnbindItem(item, item.index);
                    if (shown || alwaysRebindOnRefresh) Setup(item, index);
                }
                else
                {
                    ReleaseItem(i--);
                }
            }
            if (!rebuild) return;
            while (m_Pool.Count > 0) m_Pool.Pop().DestroyElement();
            m_ActiveItems.Clear();
            m_ScrollView.Clear();
        }

        public override void UnbindAll()
        {
            if (!m_CollectionView.HasValidDataAndBindings()) return;
            foreach (var item in m_ActiveItems) m_CollectionView.viewController.InvokeUnbindItem(item, item.index);
        }

        void Release(T item)
        {
            item.DetachElement();
            m_Pool.Push(item);
        }

        /// <summary>Binds a recycled item to an index and moves it to its place among the active items.</summary>
        protected void Setup(T recycledItem, int newIndex)
        {
            if (recycledItem.isDragGhost) recycledItem.SetDragGhost(false);
            if (newIndex >= itemsCount)
            {
                recycledItem.rootElement.style.display = DisplayStyle.None;
                if (recycledItem.index >= 0 && recycledItem.index < itemsCount)
                {
                    m_CollectionView.viewController.InvokeUnbindItem(recycledItem, recycledItem.index);
                    recycledItem.index = -1;
                }
                return;
            }
            recycledItem.rootElement.style.display = DisplayStyle.Flex;
            int id = m_CollectionView.viewController.GetIdForIndex(newIndex);
            if (recycledItem.index == newIndex && recycledItem.id == id) return;
            bool alternate = m_CollectionView.showAlternatingRowBackgrounds != AlternatingRowBackground.None && newIndex % 2 == 1;
            recycledItem.rootElement.EnableInClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName, alternate);
            int previousIndex = recycledItem.index;
            if (recycledItem.index != -1) m_CollectionView.viewController.InvokeUnbindItem(recycledItem, recycledItem.index);
            recycledItem.index = newIndex;
            recycledItem.id = id;
            int position = newIndex - firstVisibleIndex;
            if (position >= m_ScrollView.contentContainer.childCount) recycledItem.rootElement.BringToFront();
            else if (position >= 0) recycledItem.rootElement.PlaceBehind(m_ScrollView.contentContainer[position]);
            else recycledItem.rootElement.SendToBack();
            m_CollectionView.viewController.InvokeBindItem(recycledItem, newIndex);
            HandleFocus(recycledItem, previousIndex);
        }

        protected bool ShouldDeferScrollToItem(int index)
        {
            if (!m_ScrollView.contentContainer.yogaNode.IsDirty) return false;
            m_DeferredScrollToItemIndex = index;
            return true;
        }

        protected void ScheduleDeferredScrollToItem()
        {
            if (!m_DeferredScrollToItemIndex.HasValue) return;
            if (m_ScheduleDeferredScrollToItem == null)
            {
                m_ScheduleDeferredScrollToItem = m_CollectionView.schedule.Execute(() =>
                {
                    if (!m_DeferredScrollToItemIndex.HasValue) return;
                    int index = m_DeferredScrollToItemIndex.Value;
                    m_DeferredScrollToItemIndex = null;
                    ScrollToItem(index);
                });
                return;
            }
            m_ScheduleDeferredScrollToItem.Pause();
            m_ScheduleDeferredScrollToItem.Resume();
        }

        public override void OnFocusIn(VisualElement leafTarget)
        {
            if (leafTarget == m_ScrollView.contentContainer) return;
            m_LastFocusedElementTreeChildIndexes.Clear();
            if (Unbound.UIElements.UaTreePaths.FindElementInTree(m_ScrollView.contentContainer, leafTarget, m_LastFocusedElementTreeChildIndexes))
            {
                var root = m_ScrollView.contentContainer[m_LastFocusedElementTreeChildIndexes[0]];
                foreach (var item in activeItems)
                {
                    if (item.rootElement != root) continue;
                    m_LastFocusedElementIndex = item.index;
                    break;
                }
                m_LastFocusedElementTreeChildIndexes.RemoveAt(0);
            }
            else
            {
                m_LastFocusedElementIndex = -1;
            }
        }

        public override void OnFocusOut(VisualElement willFocus)
        {
            if (willFocus != null && willFocus == m_ScrollView.contentContainer) return;
            m_LastFocusedElementTreeChildIndexes.Clear();
            m_LastFocusedElementIndex = -1;
        }

        void HandleFocus(ReusableCollectionItem recycledItem, int previousIndex)
        {
            if (m_LastFocusedElementIndex == -1) return;
            if (m_LastFocusedElementIndex == recycledItem.index)
                Unbound.UIElements.UaTreePaths.ElementAtTreePath(recycledItem.rootElement, m_LastFocusedElementTreeChildIndexes)?.Focus();
            else if (m_LastFocusedElementIndex != previousIndex)
                Unbound.UIElements.UaTreePaths.ElementAtTreePath(recycledItem.rootElement, m_LastFocusedElementTreeChildIndexes)?.Blur();
            else
                m_ScrollView.contentContainer.Focus();
        }

        /// <summary>With alternating backgrounds on all rows, empty rows fill the viewport below the items.</summary>
        public override void UpdateBackground()
        {
            float empty;
            if (m_CollectionView.showAlternatingRowBackgrounds != AlternatingRowBackground.All || (empty = m_ScrollView.contentViewport.resolvedStyle.height - GetExpectedContentHeight()) <= 0f)
            {
                m_EmptyRows?.RemoveFromHierarchy();
                return;
            }
            if (lastVisibleItem == null) return;
            if (m_EmptyRows == null)
            {
                m_EmptyRows = new VisualElement();
                m_EmptyRows.AddToClassList(BaseVerticalCollectionView.backgroundFillUssClassName);
            }
            if (m_EmptyRows.parent == null) m_ScrollView.contentViewport.Add(m_EmptyRows);
            float rowHeight = GetExpectedItemHeight(-1);
            int rows = Mathf.FloorToInt(empty / rowHeight) + 1;
            for (int i = m_EmptyRows.childCount; i < rows; i++)
            {
                var row = new VisualElement();
                row.style.flexShrink = 0f;
                m_EmptyRows.Add(row);
            }
            int index = lastVisibleItem?.index ?? -1;
            for (int j = 0; j < m_EmptyRows.hierarchy.childCount; j++)
            {
                var row = m_EmptyRows.hierarchy[j];
                index++;
                row.style.height = rowHeight;
                row.EnableInClassList(BaseVerticalCollectionView.itemAlternativeBackgroundUssClassName, index % 2 == 1);
            }
        }

        internal virtual T GetOrMakeItemAtIndex(int activeItemIndex = -1, int scrollViewIndex = -1)
        {
            var item = m_Pool.Count > 0 ? m_Pool.Pop() : new T();
            if (item.rootElement == null)
            {
                m_CollectionView.viewController.InvokeMakeItem(item);
                item.onDestroy += OnDestroyItem;
            }
            item.PreAttachElement();
            if (activeItemIndex == -1) m_ActiveItems.Add(item);
            else m_ActiveItems.Insert(activeItemIndex, item);
            if (scrollViewIndex == -1) m_ScrollView.Add(item.rootElement);
            else m_ScrollView.Insert(scrollViewIndex, item.rootElement);
            return item;
        }

        internal virtual void ReleaseItem(int activeItemsIndex)
        {
            var item = m_ActiveItems[activeItemsIndex];
            if (item.index != -1) m_CollectionView.viewController.InvokeUnbindItem(item, item.index);
            Release(item);
            m_ActiveItems.Remove(item);
        }

        void OnDestroyItem(ReusableCollectionItem item)
        {
            m_CollectionView.viewController.InvokeDestroyItem(item);
            item.onDestroy -= OnDestroyItem;
        }

        protected virtual void OnScrollUpdate() { }

        protected void ScheduleScroll()
        {
            if (m_ScrollScheduledItem == null)
            {
                m_ScrollScheduledItem = m_CollectionView.schedule.Execute(OnScrollUpdate);
                return;
            }
            m_ScrollScheduledItem.Pause();
            m_ScrollScheduledItem.Resume();
        }
    }

    internal partial class FixedHeightVirtualizationController<T> : VerticalVirtualizationController<T> where T : ReusableCollectionItem, new()
    {
        int? m_ScrolledToItemIndex;
        bool m_ForcedScroll;

        float resolvedItemHeight => m_CollectionView.ResolveItemHeight();

        protected override bool VisibleItemPredicate(T i) => true;

        public FixedHeightVirtualizationController(BaseVerticalCollectionView collectionView) : base(collectionView)
        {
            collectionView.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (!m_ScrolledToItemIndex.HasValue) return;
                if (ShouldDeferScrollToItem(m_ScrolledToItemIndex ?? -1)) ScheduleDeferredScrollToItem();
                m_ScrolledToItemIndex = null;
            });
        }

        public override int GetIndexFromPosition(Vector2 position) => (int)(position.y / resolvedItemHeight);

        public override float GetExpectedItemHeight(int index) => resolvedItemHeight;

        public override float GetExpectedContentHeight() => itemsCount * resolvedItemHeight;

        public override void ScrollToItem(int index)
        {
            if (index < -1) return;
            if (visibleItemCount == 0)
            {
                m_ScrolledToItemIndex = index;
                return;
            }
            if (ShouldDeferScrollToItem(index)) ScheduleDeferredScrollToItem();
            float height = resolvedItemHeight;
            m_ForcedScroll = true;
            if (index == -1)
            {
                int fitting = (int)(lastHeight / height);
                m_ScrollView.scrollOffset = itemsCount < fitting ? new Vector2(0f, 0f) : new Vector2(0f, (itemsCount + 1) * height);
                return;
            }
            if (firstVisibleIndex >= index)
            {
                m_ScrollView.scrollOffset = Vector2.up * (height * index);
                return;
            }
            int visible = (int)(lastHeight / height);
            if (index < firstVisibleIndex + visible) return;
            int first = index - visible + 1;
            float partial = height - (lastHeight - visible * height);
            m_ScrollView.scrollOffset = new Vector2(m_ScrollView.scrollOffset.x, height * first + partial);
        }

        /// <summary>The content is as tall as all items; enough items for the viewport (plus two) are active.</summary>
        public override void Resize(Vector2 size)
        {
            float contentHeight = GetExpectedContentHeight();
            m_ScrollView.contentContainer.style.height = contentHeight;
            float maxOffset = Mathf.Max(0f, contentHeight - m_ScrollView.contentViewport.layout.height);
            float offset = Mathf.Min(m_ScrollView.scrollOffset.y, maxOffset);
            m_ScrollView.verticalScroller.slider.SetHighValueWithoutNotify(maxOffset);
            m_ScrollView.verticalScroller.slider.SetValueWithoutNotify(offset);
            int wanted = 0;
            float fitting = size.y / resolvedItemHeight;
            if (fitting > 0f) wanted = (int)fitting + k_ExtraVisibleItems;
            int count = Mathf.Min(wanted, itemsCount);
            int visible = visibleItemCount;
            if (visible != count)
            {
                if (visible > count)
                {
                    for (int i = 0; i < visible - count; i++) ReleaseItem(m_ActiveItems.Count - 1);
                }
                else
                {
                    for (int j = 0; j < count - visible; j++)
                    {
                        int index = j + firstVisibleIndex + visible;
                        var item = GetOrMakeItemAtIndex();
                        Setup(item, index);
                    }
                }
            }
            OnScrollUpdate();
        }

        public override void OnScroll(Vector2 scrollOffset)
        {
            if (m_ForcedScroll) OnScrollUpdate();
            else ScheduleScroll();
        }

        /// <summary>The content's top padding skips the items above the first visible one; items that
        /// scrolled out are moved to the other end and rebound.</summary>
        protected override void OnScrollUpdate()
        {
            float offset = Mathf.Max(0f, m_ScrollView.scrollOffset.y);
            float height = resolvedItemHeight;
            int first = (int)(offset / height);
            m_ScrollView.contentContainer.style.paddingTop = first * height;
            m_ScrollView.contentContainer.style.height = itemsCount * height;
            if (first != firstVisibleIndex)
            {
                firstVisibleIndex = first;
                if (m_ActiveItems.Count > 0)
                {
                    if (firstVisibleIndex < m_ActiveItems[0].index)
                    {
                        int moved = m_ActiveItems[0].index - firstVisibleIndex;
                        for (int i = 0; i < moved && m_ActiveItems.Count > 0; i++)
                        {
                            var last = m_ActiveItems[m_ActiveItems.Count - 1];
                            m_ScrollInsertionList.Add(last);
                            m_ActiveItems.RemoveAt(m_ActiveItems.Count - 1);
                            last.rootElement.SendToBack();
                        }
                        m_ActiveItems.InsertRange(0, m_ScrollInsertionList);
                        m_ScrollInsertionList.Clear();
                    }
                    else if (firstVisibleIndex < m_ActiveItems[m_ActiveItems.Count - 1].index)
                    {
                        int moved = 0;
                        while (firstVisibleIndex > m_ActiveItems[moved].index)
                        {
                            var item = m_ActiveItems[moved];
                            m_ScrollInsertionList.Add(item);
                            moved++;
                            item.rootElement.BringToFront();
                        }
                        m_ActiveItems.RemoveRange(0, moved);
                        m_ActiveItems.AddRange(m_ScrollInsertionList);
                        m_ScrollInsertionList.Clear();
                    }
                    for (int j = 0; j < m_ActiveItems.Count; j++) Setup(m_ActiveItems[j], j + firstVisibleIndex);
                }
            }
            m_ForcedScroll = false;
        }

        internal override T GetOrMakeItemAtIndex(int activeItemIndex = -1, int scrollViewIndex = -1)
        {
            var item = base.GetOrMakeItemAtIndex(activeItemIndex, scrollViewIndex);
            item.rootElement.style.height = resolvedItemHeight;
            return item;
        }
    }
}

namespace Unbound.UIElements
{
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    internal static class UaTreePaths
    {
        /// <summary>The child indices leading from an ancestor down to an element.</summary>
        internal static bool FindElementInTree(VisualElement root, VisualElement element, List<int> path)
        {
            var stack = new List<int>();
            for (var e = element; e != null; e = e.hierarchy.parent)
            {
                var p = e.hierarchy.parent;
                if (p == null) return false;
                stack.Add(p.hierarchy.IndexOf(e));
                if (p == root)
                {
                    stack.Reverse();
                    path.AddRange(stack);
                    return true;
                }
            }
            return false;
        }

        internal static VisualElement ElementAtTreePath(VisualElement root, List<int> path)
        {
            var e = root;
            foreach (int i in path)
            {
                if (i < 0 || i >= e.hierarchy.childCount) return null;
                e = e.hierarchy[i];
            }
            return e;
        }
    }
}
