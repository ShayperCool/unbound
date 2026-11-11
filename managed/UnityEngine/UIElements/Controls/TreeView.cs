using Unity.Properties;
using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.UIElements;

// TreeView, BaseTreeView, their controllers and TreeViewItemData<T>. The tree is a hierarchy of nodes
// (one per item id) with an expanded flag on every node, leaves included; the visible rows are the
// nodes whose ancestors are all expanded, depth first, and they are the controller's items source.
// Documented (Unity 6000.0 Scripting API and Manual "TreeView"): SetRootItems forces the default
// controller; the items source "Contains items that are expanded in the tree"; GetIndexForId is -1
// for a hidden item; itemExpandedChanged's id is -1 for ExpandAll/CollapseAll; selecting by id
// "will also expand the selected item(s)" (measured otherwise, see below); autoExpand expands added
// items; the USS class table; the --unity-tree-view-indent custom property.
// Matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (tree section): ExpandItem with
// and without its children (which marks every descendant expanded, leaves too), toggle presses and
// Alt presses (a row pressed with Alt expands or collapses its whole subtree), ExpandAll/CollapseAll,
// a collapse of a collapsed item (refreshed and reported all the same), selection by id (each
// collapsed ancestor expanded and reported), right/left navigation (into the first child, expand,
// collapse, back to the parent), new root items (itemsSourceChanged first), row structure and names,
// and every callback in order; tree-data section: Move, AddItem, TryRemoveItem and GetAllItemIds,
// and when the rows follow them; tree-drag sections: reordering by dragging (the controller and
// the dragger are in CollectionDragAndDrop.cs). The custom indent and dynamic height rows follow
// the same model without a capture. View-data persistence of the expanded items is not implemented.
namespace UnityEngine.UIElements
{
    public abstract partial class BaseTreeView : BaseVerticalCollectionView
    {
        public new static readonly string ussClassName = "unity-tree-view";
        public new static readonly string itemUssClassName = ussClassName + "__item";
        public static readonly string itemToggleUssClassName = ussClassName + "__item-toggle";
        [Obsolete("Individual item indents are no longer used, see itemIndentUssClassName instead")]
        public static readonly string itemIndentsContainerUssClassName = ussClassName + "__item-indents";
        public static readonly string itemIndentUssClassName = ussClassName + "__item-indent";
        public static readonly string itemContentContainerUssClassName = ussClassName + "__item-content";

        internal static readonly int invalidId = -1;

        // A theme or game sheet can set the indent per depth level.
        internal static readonly CustomStyleProperty<float> s_TreeViewIndentProperty = new CustomStyleProperty<float>("--unity-tree-view-indent");

        bool m_AutoExpand;

        /// <summary>The indent per level from --unity-tree-view-indent, when set.</summary>
        internal float? customIdent { get; private set; }

        [CreateProperty(ReadOnly = true)]
        public new IList itemsSource
        {
            get => viewController?.itemsSource;
            internal set => GetOrCreateViewController().itemsSource = value;
        }

        public new BaseTreeViewController viewController => base.viewController as BaseTreeViewController;

        /// <summary>Items added while set are expanded; setting it refreshes the rows.</summary>
        [CreateProperty]
        public bool autoExpand
        {
            get => m_AutoExpand;
            set
            {
                if (m_AutoExpand == value) return;
                m_AutoExpand = value;
                RefreshItems();
            }
        }

        public event Action<TreeViewExpansionChangedArgs> itemExpandedChanged;

        internal override ICollectionDragAndDropController CreateDragAndDropController() => new TreeViewReorderableDragAndDropController(this);

        public BaseTreeView() : this(-1) { }

        public BaseTreeView(int itemHeight) : base(null, itemHeight)
        {
            AddToClassList(ussClassName);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        /// <summary>A changed --unity-tree-view-indent (set, changed or removed) rebinds the rows with
        /// it; an unchanged one does nothing.</summary>
        void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            float? indent = evt.customStyle.TryGetValue(s_TreeViewIndentProperty, out float value) ? value : (float?)null;
            if (indent == customIdent) return;
            customIdent = indent;
            if (viewController != null) virtualizationController?.Refresh(false);
        }

        public void SetRootItems<T>(IList<TreeViewItemData<T>> rootItems) => SetRootItemsInternal(rootItems);

        internal abstract void SetRootItemsInternal<T>(IList<TreeViewItemData<T>> rootItems);

        public IEnumerable<int> GetRootIds() => viewController.GetRootItemIds();

        public int GetTreeCount() => viewController.GetTreeItemsCount();

        private protected override void CreateVirtualizationController() => CreateVirtualizationController<ReusableTreeViewItem>();

        public override void SetViewController(CollectionViewController controller)
        {
            if (viewController != null)
            {
                viewController.itemIndexChanged -= OnItemIndexChanged;
                viewController.itemExpandedChanged -= OnItemExpandedChanged;
            }
            base.SetViewController(controller);
            if (viewController != null)
            {
                viewController.itemIndexChanged += OnItemIndexChanged;
                viewController.itemExpandedChanged += OnItemExpandedChanged;
            }
        }

        void OnItemIndexChanged(int srcIndex, int dstIndex) => RefreshItems();

        void OnItemExpandedChanged(TreeViewExpansionChangedArgs args) => itemExpandedChanged?.Invoke(args);

        public int GetIdForIndex(int index) => viewController.GetIdForIndex(index);

        public int GetParentIdForIndex(int index) => viewController.GetParentId(GetIdForIndex(index));

        public IEnumerable<int> GetChildrenIdsForIndex(int index) => viewController.GetChildrenIdsByIndex(index);

        public IEnumerable<TreeViewItemData<T>> GetSelectedItems<T>() => GetSelectedItemsInternal<T>();

        private protected abstract IEnumerable<TreeViewItemData<T>> GetSelectedItemsInternal<T>();

        public T GetItemDataForIndex<T>(int index) => GetItemDataForIndexInternal<T>(index);

        private protected abstract T GetItemDataForIndexInternal<T>(int index);

        public T GetItemDataForId<T>(int id) => GetItemDataForIdInternal<T>(id);

        private protected abstract T GetItemDataForIdInternal<T>(int id);

        public void AddItem<T>(TreeViewItemData<T> item, int parentId = -1, int childIndex = -1, bool rebuildTree = true) =>
            AddItemInternal(item, parentId, childIndex, rebuildTree);

        private protected abstract void AddItemInternal<T>(TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree);

        public bool TryRemoveItem(int id, bool rebuildTree = true) => viewController.TryRemoveItem(id, rebuildTree);

        /// <summary>Right/left navigation: on items with children, expands (right) or collapses
        /// (left) the selected items, all their descendants with Alt. Otherwise left selects the
        /// parent (or, at the root, the previous item with children), right the next item with
        /// children.</summary>
        private protected override bool HandleItemNavigation(bool moveIn, bool altPressed)
        {
            int step = 1;
            bool changed = false;
            foreach (int id in selectedIds)
            {
                int index = viewController.GetIndexForId(id);
                if (!viewController.HasChildrenByIndex(index)) break;
                if (moveIn && !viewController.IsExpandedByIndex(index))
                {
                    ExpandItemByIndex(index, altPressed);
                    changed = true;
                }
                else if (!moveIn && viewController.IsExpandedByIndex(index))
                {
                    CollapseItemByIndex(index, altPressed);
                    changed = true;
                }
            }
            if (changed) return true;
            if (!moveIn)
            {
                int parentId = viewController.GetParentId(viewController.GetIdForIndex(selectedIndex));
                if (parentId != invalidId)
                {
                    SetSelectionById(parentId);
                    ScrollToItemById(parentId);
                    return true;
                }
                step = -1;
            }
            int next = selectedIndex;
            bool hasChildren;
            do
            {
                next += step;
                hasChildren = viewController.HasChildrenByIndex(next);
            }
            while (!hasChildren && next >= 0 && next < itemsSource.Count);
            if (!hasChildren) return false;
            SetSelection(next);
            ScrollToItem(next);
            return true;
        }

        public void SetSelectionById(int id) => SetSelectionById(new[] { id });

        public void SetSelectionById(IEnumerable<int> ids) => SetSelectionInternalById(ids, true);

        public void SetSelectionByIdWithoutNotify(IEnumerable<int> ids) => SetSelectionInternalById(ids, false);

        internal void SetSelectionInternalById(IEnumerable<int> ids, bool sendNotification)
        {
            if (ids == null) return;
            var indices = new List<int>();
            foreach (int id in ids) indices.Add(GetItemIndex(id, true));
            SetSelectionInternal(indices, sendNotification);
        }

        public void AddToSelectionById(int id) => AddToSelection(GetItemIndex(id, true));

        public void RemoveFromSelectionById(int id) => RemoveFromSelection(GetItemIndex(id));

        /// <summary>The visible index of an item; with <paramref name="expand"/>, its collapsed
        /// ancestors are expanded first (each refreshes and reports).</summary>
        int GetItemIndex(int id, bool expand = false)
        {
            if (expand)
            {
                for (int parent = viewController.GetParentId(id); parent != invalidId; parent = viewController.GetParentId(parent))
                    if (!viewController.IsExpanded(parent)) viewController.ExpandItem(parent, false);
            }
            return viewController.GetIndexForId(id);
        }

        public bool IsExpanded(int id) => viewController.IsExpanded(id);

        public void CollapseItem(int id, bool collapseAllChildren = false, bool refresh = true) => viewController.CollapseItem(id, collapseAllChildren, refresh);

        public void ExpandItem(int id, bool expandAllChildren = false, bool refresh = true) => viewController.ExpandItem(id, expandAllChildren, refresh);

        public void ExpandRootItems()
        {
            foreach (int id in viewController.GetRootItemIds()) viewController.ExpandItem(id, false, false);
            RefreshItems();
        }

        public void ExpandAll() => viewController.ExpandAll();

        public void CollapseAll() => viewController.CollapseAll();

        void CollapseItemByIndex(int index, bool collapseAll)
        {
            if (!viewController.HasChildrenByIndex(index)) return;
            viewController.CollapseItemByIndex(index, collapseAll);
            RefreshItems();
        }

        void ExpandItemByIndex(int index, bool expandAll)
        {
            if (!viewController.HasChildrenByIndex(index)) return;
            viewController.ExpandItemByIndex(index, expandAll);
            RefreshItems();
        }
    }

    /// <summary>A tree view made of makeItem/bindItem callbacks, or a UXML item template.</summary>
    public partial class TreeView : BaseTreeView
    {
        Func<VisualElement> m_MakeItem;
        readonly Func<VisualElement> m_TemplateMakeItem;
        VisualTreeAsset m_ItemTemplate;
        Action<VisualElement, int> m_BindItem;
        Action<VisualElement, int> m_UnbindItem;
        Action<VisualElement> m_DestroyItem;

        // A new makeItem rebuilds the rows, a new bindItem refreshes them.
        [CreateProperty]
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

        [CreateProperty]
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

        [CreateProperty]
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

        [CreateProperty]
        public new Action<VisualElement, int> unbindItem
        {
            get => m_UnbindItem;
            set => m_UnbindItem = value;
        }

        [CreateProperty]
        public new Action<VisualElement> destroyItem
        {
            get => m_DestroyItem;
            set => m_DestroyItem = value;
        }

        public new TreeViewController viewController => base.viewController as TreeViewController;

        public TreeView() : this(null, null) { }

        public TreeView(Func<VisualElement> makeItem, Action<VisualElement, int> bindItem) : base(-1)
        {
            m_TemplateMakeItem = TemplateMakeItem;
            m_MakeItem = makeItem;
            m_BindItem = bindItem;
        }

        public TreeView(int itemHeight, Func<VisualElement> makeItem, Action<VisualElement, int> bindItem) : this(makeItem, bindItem)
        {
            fixedItemHeight = itemHeight;
        }

        VisualElement TemplateMakeItem() => m_ItemTemplate != null ? m_ItemTemplate.Instantiate() : new Label(k_InvalidTemplateError);

        internal override bool HasValidDataAndBindings() => base.HasValidDataAndBindings() && (makeItem != null) == (bindItem != null);

        protected override CollectionViewController CreateViewController() => new DefaultTreeViewController<object>();

        internal override void SetRootItemsInternal<T>(IList<TreeViewItemData<T>> rootItems) =>
            TreeViewHelpers<T, DefaultTreeViewController<T>>.SetRootItems(this, rootItems, () => new DefaultTreeViewController<T>());

        private protected override IEnumerable<TreeViewItemData<T>> GetSelectedItemsInternal<T>() =>
            TreeViewHelpers<T, DefaultTreeViewController<T>>.GetSelectedItems(this);

        private protected override T GetItemDataForIndexInternal<T>(int index) =>
            TreeViewHelpers<T, DefaultTreeViewController<T>>.GetItemDataForIndex(this, index);

        private protected override T GetItemDataForIdInternal<T>(int id) =>
            TreeViewHelpers<T, DefaultTreeViewController<T>>.GetItemDataForId(this, id);

        private protected override void AddItemInternal<T>(TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree) =>
            TreeViewHelpers<T, DefaultTreeViewController<T>>.AddItem(this, item, parentId, childIndex, rebuildTree);
    }

    /// <summary>A default tree controller of data type T (single- or multi-column).</summary>
    internal partial interface IDefaultTreeViewController<T>
    {
        void SetRootItems(IList<TreeViewItemData<T>> items);
        void AddItem(in TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree = true);
        TreeViewItemData<T> GetTreeViewItemDataForId(int id);
        TreeViewItemData<T> GetTreeViewItemDataForIndex(int index);
        T GetDataForId(int id);
        T GetDataForIndex(int index);
    }

    /// <summary>The typed data access of tree views through their default controller: a controller of
    /// another data type is an error, another controller answers through GetItemForIndex.</summary>
    internal static partial class TreeViewHelpers<T, TDefaultController> where TDefaultController : BaseTreeViewController, IDefaultTreeViewController<T>
    {
        internal static void SetRootItems(BaseTreeView treeView, IList<TreeViewItemData<T>> rootItems, Func<TDefaultController> createController)
        {
            if (treeView.viewController is TDefaultController controller)
            {
                controller.SetRootItems(rootItems);
                return;
            }
            var created = createController();
            treeView.SetViewController(created);
            created.SetRootItems(rootItems);
        }

        internal static IEnumerable<TreeViewItemData<T>> GetSelectedItems(BaseTreeView treeView)
        {
            if (treeView.viewController is TDefaultController controller)
            {
                foreach (int index in treeView.selectedIndices) yield return controller.GetTreeViewItemDataForIndex(index);
                yield break;
            }
            if (IsDefaultOfOtherType(treeView))
                throw new ArgumentException($"Type parameter ({typeof(T)}) differs from data source ({DataTypeOf(treeView)}) and is not recognized by the controller.");
            throw new ArgumentException("GetSelectedItems<T>() only works when using the default controller. Use your controller along with the selectedIndices enumerable instead.");
        }

        internal static T GetItemDataForIndex(BaseTreeView treeView, int index)
        {
            if (treeView.viewController is TDefaultController controller) return controller.GetDataForIndex(index);
            return Typed(treeView, treeView.viewController?.GetItemForIndex(index));
        }

        internal static T GetItemDataForId(BaseTreeView treeView, int id)
        {
            if (treeView.viewController is TDefaultController controller) return controller.GetDataForId(id);
            var other = treeView.viewController;
            return Typed(treeView, other?.GetItemForIndex(other.GetIndexForId(id)));
        }

        internal static void AddItem(BaseTreeView treeView, TreeViewItemData<T> item, int parentId = -1, int childIndex = -1, bool rebuildTree = true)
        {
            if (treeView.viewController is TDefaultController controller)
            {
                controller.AddItem(in item, parentId, childIndex, rebuildTree);
                return;
            }
            throw new ArgumentException($"Type parameter ({typeof(T)}) differs from data source ({(IsDefaultOfOtherType(treeView) ? DataTypeOf(treeView) : null)})and is not recognized by the controller.");
        }

        // An item of exactly T; otherwise the error names the item's type, or a default controller's.
        static T Typed(BaseTreeView treeView, object item)
        {
            var type = item?.GetType();
            if (type == typeof(T)) return (T)item;
            if (type == null && IsDefaultOfOtherType(treeView)) type = DataTypeOf(treeView);
            throw new ArgumentException($"Type parameter ({typeof(T)}) differs from data source ({type}) and is not recognized by the controller.");
        }

        static bool IsDefaultOfOtherType(BaseTreeView treeView)
        {
            var type = treeView.viewController?.GetType();
            return type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(TDefaultController).GetGenericTypeDefinition();
        }

        static Type DataTypeOf(BaseTreeView treeView) => treeView.viewController?.GetType().GetGenericArguments()[0];
    }

    /// <summary>Item structure provided to a TreeView using the default implementation: an id, its
    /// data and its children.</summary>
    public readonly partial struct TreeViewItemData<T>
    {
        readonly List<TreeViewItemData<T>> m_Children;

        public TreeViewItemData(int id, T data, List<TreeViewItemData<T>> children = null)
        {
            this.id = id;
            this.data = data;
            m_Children = children ?? new List<TreeViewItemData<T>>();
        }

        public int id { get; }

        public T data { get; }

        public IEnumerable<TreeViewItemData<T>> children => m_Children;

        public bool hasChildren => m_Children != null && m_Children.Count > 0;

        internal void InsertChild(TreeViewItemData<T> child, int index)
        {
            if (index < 0 || index >= m_Children.Count) m_Children.Add(child);
            else m_Children.Insert(index, child);
        }

        internal void RemoveChild(int childId)
        {
            for (int i = 0; i < m_Children.Count; i++)
            {
                if (m_Children[i].id != childId) continue;
                m_Children.RemoveAt(i);
                return;
            }
        }
    }

    public partial class TreeViewExpansionChangedArgs
    {
        public TreeViewExpansionChangedArgs() { }

        public int id { get; set; }

        public bool isExpanded { get; set; }

        public bool isAppliedToAllChildren { get; set; }
    }

    /// <summary>The tree controller: the hierarchy of item ids and its visible rows, expansion,
    /// structure queries, and the row elements' indent, toggle and Alt presses.</summary>
    public abstract partial class BaseTreeViewController : CollectionViewController
    {
        private protected UaTreeHierarchy m_UaHierarchy = new UaTreeHierarchy();

        protected BaseTreeView baseTreeView => view as BaseTreeView;

        /// <summary>The visible rows (read-only); tree data is set through the controller.</summary>
        public override IList itemsSource
        {
            get => base.itemsSource;
            set => throw new InvalidOperationException("Can't set itemsSource directly. Override this controller to manage tree data.");
        }

        internal event Action<TreeViewExpansionChangedArgs> itemExpandedChanged;

        protected BaseTreeViewController() => UseVisibleRowsAsItemsSource();

        [Obsolete("RebuildTree is no longer supported and will be removed.", false)]
        [Unbound.EditorFaithful("Unity's: the tree updates itself")]
        public void RebuildTree() { }

        public IEnumerable<int> GetRootItemIds()
        {
            var ids = new List<int>();
            foreach (var node in m_UaHierarchy.root.children) ids.Add(node.id);
            return ids;
        }

        public virtual IEnumerable<int> GetAllItemIds(IEnumerable<int> rootIds = null)
        {
            var ids = new List<int>();
            if (rootIds == null)
            {
                m_UaHierarchy.AddAllIds(ids);
                return ids;
            }
            foreach (int id in rootIds)
            {
                if (!m_UaHierarchy.TryGet(id, out var node)) continue;
                m_UaHierarchy.AddDescendantIds(node, ids);
                ids.Add(id);
            }
            return ids;
        }

        public virtual int GetParentId(int id) =>
            m_UaHierarchy.TryGet(id, out var node) && node.parent != null && node.parent != m_UaHierarchy.root ? node.parent.id : BaseTreeView.invalidId;

        public virtual IEnumerable<int> GetChildrenIds(int id)
        {
            var ids = new List<int>();
            if (m_UaHierarchy.TryGet(id, out var node))
                foreach (var child in node.children) ids.Add(child.id);
            return ids;
        }

        /// <summary>Moves an item under a new parent (-1: the root) at a child index (-1 or past the
        /// end: last) and refreshes the rows it knows (the view's own refresh only with rebuildTree).
        /// Nothing happens for an unknown item, an item moved into itself or under one of its
        /// descendants. As measured (Unity 6000.0.71f1, tests/projects/uitkcollections tree-data): a
        /// parent id that is not in the tree is an ArgumentNullException before anything moves; a
        /// child index below -1 is an IndexOutOfRangeException after the item went to the end of
        /// its new parent, and the rows do not show that until another change refreshes them. The
        /// TreeViewItemData of the parents keep the children they were given.</summary>
        public virtual void Move(int id, int newParentId, int childIndex = -1, bool rebuildTree = true)
        {
            if (id == newParentId || IsChildOf(newParentId, id) || !m_UaHierarchy.TryGet(id, out var node)) return;
            UaTreeNode parent;
            if (newParentId == BaseTreeView.invalidId) parent = m_UaHierarchy.root;
            else if (!m_UaHierarchy.TryGet(newParentId, out parent)) throw new ArgumentNullException(null, "Value cannot be null.\nParameter name: node");
            if (node.parent == parent && GetChildIndexForId(id) < childIndex) childIndex--;
            if (childIndex < -1)
            {
                if (node.parent != parent) m_UaHierarchy.MoveTo(node, parent, -1, refreshRows: false);
                throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
            }
            m_UaHierarchy.MoveTo(node, parent, childIndex);
            if (rebuildTree) RaiseItemParentChanged(id, newParentId);
        }

        /// <summary>Items added or removed since the rows were last refreshed show from here on.</summary>
        internal override void PreRefresh()
        {
            base.PreRefresh();
            m_UaHierarchy.FlushPending();
        }

        public abstract bool TryRemoveItem(int id, bool rebuildTree = true);

        internal override void InvokeMakeItem(ReusableCollectionItem reusableItem)
        {
            if (!(reusableItem is ReusableTreeViewItem treeItem)) return;
            treeItem.Init(MakeItem());
            PostInitRegistration(treeItem);
        }

        internal override void InvokeBindItem(ReusableCollectionItem reusableItem, int index)
        {
            if (reusableItem is ReusableTreeViewItem treeItem)
            {
                // Rows are pooled by the view and can outlive a controller (SetRootItems with another
                // data type replaces it): the controller that binds a row takes its events over.
                if (treeItem.owner != this)
                {
                    treeItem.owner?.UnregisterRow(treeItem);
                    PostInitRegistration(treeItem);
                }
                treeItem.customIndentWidth = baseTreeView.customIdent;
                treeItem.Indent(GetIndentationDepthByIndex(index));
                treeItem.SetExpandedWithoutNotify(IsExpandedByIndex(index));
                treeItem.SetToggleVisibility(HasChildrenByIndex(index));
            }
            base.InvokeBindItem(reusableItem, index);
        }

        internal override void InvokeDestroyItem(ReusableCollectionItem reusableItem)
        {
            if (reusableItem is ReusableTreeViewItem treeItem) (treeItem.owner ?? this).UnregisterRow(treeItem);
            base.InvokeDestroyItem(reusableItem);
        }

        internal void PostInitRegistration(ReusableTreeViewItem treeItem)
        {
            treeItem.onPointerUp += OnItemPointerUp;
            treeItem.onToggleValueChanged += OnToggleValueChanged;
            treeItem.owner = this;
        }

        internal void UnregisterRow(ReusableTreeViewItem treeItem)
        {
            treeItem.onPointerUp -= OnItemPointerUp;
            treeItem.onToggleValueChanged -= OnToggleValueChanged;
            if (treeItem.owner == this) treeItem.owner = null;
        }

        /// <summary>A row pressed with Alt expands or collapses its item and every descendant.</summary>
        void OnItemPointerUp(PointerUpEvent evt)
        {
            if ((evt.modifiers & EventModifiers.Alt) == 0 || baseTreeView == null) return;
            var row = evt.currentTarget as VisualElement;
            var toggle = row?.Q<Toggle>(BaseTreeView.itemToggleUssClassName);
            if (toggle == null) return;
            int index = ((ReusableTreeViewItem)toggle.userData).index;
            if (!HasChildrenByIndex(index)) return;
            bool expanded = IsExpandedByIndex(index);
            int id = GetIdForIndex(index);
            m_UaHierarchy.SetExpanded(m_UaHierarchy.VisibleAt(index), !expanded, true);
            baseTreeView.RefreshItems();
            RaiseItemExpandedChanged(id, !expanded, true);
            evt.StopPropagation();
        }

        void RaiseItemExpandedChanged(int id, bool isExpanded, bool isAppliedToAllChildren) =>
            itemExpandedChanged?.Invoke(new TreeViewExpansionChangedArgs { id = id, isExpanded = isExpanded, isAppliedToAllChildren = isAppliedToAllChildren });

        void OnToggleValueChanged(ChangeEvent<bool> evt)
        {
            // A replaced controller (no view) ignores rows it no longer shows.
            if (baseTreeView == null) return;
            var toggle = evt.target as Toggle;
            int index = ((ReusableTreeViewItem)toggle.userData).index;
            if (IsExpandedByIndex(index)) CollapseItemByIndex(index, false);
            else ExpandItemByIndex(index, false);
            baseTreeView.scrollView.contentContainer.Focus();
        }

        public virtual int GetTreeItemsCount() => m_UaHierarchy.count;

        public override int GetIndexForId(int id) => m_UaHierarchy.TryGet(id, out var node) ? m_UaHierarchy.VisibleIndexOf(node) : BaseTreeView.invalidId;

        /// <summary>The item at a visible index; one past the end answers the last row's.</summary>
        public override int GetIdForIndex(int index)
        {
            int count = m_UaHierarchy.visibleCount;
            if (index == count && count > 0) return m_UaHierarchy.VisibleAt(count - 1).id;
            return IsIndexValid(index) ? m_UaHierarchy.VisibleAt(index).id : BaseTreeView.invalidId;
        }

        public virtual bool HasChildren(int id) => m_UaHierarchy.TryGet(id, out var node) && node.children.Count > 0;

        public bool Exists(int id) => m_UaHierarchy.TryGet(id, out _);

        public bool HasChildrenByIndex(int index) => IsIndexValid(index) && m_UaHierarchy.VisibleAt(index).children.Count > 0;

        public IEnumerable<int> GetChildrenIdsByIndex(int index)
        {
            var ids = new List<int>();
            if (IsIndexValid(index))
                foreach (var child in m_UaHierarchy.VisibleAt(index).children) ids.Add(child.id);
            return ids;
        }

        public int GetChildIndexForId(int id) =>
            m_UaHierarchy.TryGet(id, out var node) && node.parent != null ? node.parent.children.IndexOf(node) : BaseTreeView.invalidId;

        public int GetIndentationDepth(int id)
        {
            int depth = 0;
            for (int parent = GetParentId(id); parent != BaseTreeView.invalidId; parent = GetParentId(parent)) depth++;
            return depth;
        }

        public int GetIndentationDepthByIndex(int index) => GetIndentationDepth(GetIdForIndex(index));

        [Unbound.EditorFaithful("Unity's base implementation")]
        public virtual bool CanChangeExpandedState(int id) => true;

        /// <summary>The item's expanded flag (a leaf can carry one: ExpandAll and expanding with all
        /// children set it on every descendant).</summary>
        public bool IsExpanded(int id) => m_UaHierarchy.TryGet(id, out var node) && node.expanded;

        public bool IsExpandedByIndex(int index) => IsIndexValid(index) && IsExpanded(GetIdForIndex(index));

        public void ExpandItemByIndex(int index, bool expandAllChildren, bool refresh = true)
        {
            if (HasChildrenByIndex(index)) ExpandNode(m_UaHierarchy.VisibleAt(index), expandAllChildren, refresh);
        }

        public void ExpandItem(int id, bool expandAllChildren, bool refresh = true)
        {
            if (HasChildren(id) && CanChangeExpandedState(id) && m_UaHierarchy.TryGet(id, out var node)) ExpandNode(node, expandAllChildren, refresh);
        }

        public void CollapseItemByIndex(int index, bool collapseAllChildren, bool refresh = true)
        {
            if (HasChildrenByIndex(index)) CollapseNode(m_UaHierarchy.VisibleAt(index), collapseAllChildren, refresh);
        }

        /// <summary>Collapses an item with children, reporting it even when it was collapsed.</summary>
        public void CollapseItem(int id, bool collapseAllChildren, bool refresh = true)
        {
            if (HasChildren(id) && CanChangeExpandedState(id) && m_UaHierarchy.TryGet(id, out var node)) CollapseNode(node, collapseAllChildren, refresh);
        }

        public void ExpandAll()
        {
            m_UaHierarchy.SetAllExpanded(true);
            baseTreeView.RefreshItems();
            RaiseItemExpandedChanged(-1, true, true);
        }

        public void CollapseAll()
        {
            m_UaHierarchy.SetAllExpanded(false);
            baseTreeView.RefreshItems();
            RaiseItemExpandedChanged(-1, false, true);
        }

        void ExpandNode(UaTreeNode node, bool expandAllChildren, bool refresh)
        {
            if (!CanChangeExpandedState(node.id)) return;
            m_UaHierarchy.SetExpanded(node, true, expandAllChildren);
            if (refresh) baseTreeView.RefreshItems();
            RaiseItemExpandedChanged(node.id, true, expandAllChildren);
        }

        void CollapseNode(UaTreeNode node, bool collapseAllChildren, bool refresh)
        {
            if (!CanChangeExpandedState(node.id)) return;
            m_UaHierarchy.SetExpanded(node, false, collapseAllChildren);
            if (refresh) baseTreeView.RefreshItems();
            RaiseItemExpandedChanged(node.id, false, collapseAllChildren);
        }

        internal void GetExpandedItemIds(List<int> list)
        {
            list.Clear();
            m_UaHierarchy.AddExpandedIds(list);
        }

        /// <summary>Expands every collapsed ancestor of a node.</summary>
        internal void ExpandAncestorNodes(UaTreeNode node)
        {
            for (var parent = node.parent; parent != null && parent != m_UaHierarchy.root; parent = parent.parent)
                if (!parent.expanded && CanChangeExpandedState(parent.id)) m_UaHierarchy.SetExpanded(parent, true, false);
        }

        bool IsIndexValid(int index) => index >= 0 && index < m_UaHierarchy.visibleCount;

        bool IsChildOf(int childId, int id)
        {
            if (childId == BaseTreeView.invalidId || id == BaseTreeView.invalidId) return false;
            if (!m_UaHierarchy.TryGet(childId, out var child) || !m_UaHierarchy.TryGet(id, out var node)) return false;
            for (var n = child; n != null && n != m_UaHierarchy.root; n = n.parent)
                if (n == node) return true;
            return false;
        }

        internal void RaiseItemParentChanged(int id, int newParentId) => RaiseItemIndexChanged(id, newParentId);

        /// <summary>Makes the hierarchy's visible rows the controller's items source.</summary>
        internal void UseVisibleRowsAsItemsSource() => SetItemsSourceWithoutNotify(m_UaHierarchy.visibleRows);

        internal UaTreeHierarchy uaHierarchy => m_UaHierarchy;

        internal BaseTreeView uaTreeView => baseTreeView;

        internal void UaRaiseItemsSourceChanged() => RaiseItemsSourceChanged();
    }

    public abstract partial class TreeViewController : BaseTreeViewController
    {
        protected TreeView treeView => view as TreeView;

        protected override VisualElement MakeItem()
        {
            if (treeView.makeItem == null)
            {
                if (treeView.bindItem != null) throw new NotImplementedException("You must specify makeItem if bindItem is specified.");
                return new Label();
            }
            return treeView.makeItem();
        }

        protected override void BindItem(VisualElement element, int index)
        {
            if (treeView.bindItem == null)
            {
                if (treeView.makeItem != null) throw new NotImplementedException("You must specify bindItem if makeItem is specified.");
                ((Label)element).text = GetItemForIndex(index)?.ToString() ?? "null";
                return;
            }
            treeView.bindItem(element, index);
        }

        protected override void UnbindItem(VisualElement element, int index) => treeView.unbindItem?.Invoke(element, index);

        protected override void DestroyItem(VisualElement element) => treeView.destroyItem?.Invoke(element);
    }

    /// <summary>The default tree controller of data type T: the hierarchy built from
    /// TreeViewItemData, each node's item data, adding and removing items.</summary>
    public partial class DefaultTreeViewController<T> : TreeViewController, IDefaultTreeViewController<T>
    {
        readonly UaTreeItems<T> m_Items;

        /// <summary>Assigning root items (a list of TreeViewItemData&lt;T&gt;) sets them; another type
        /// is an error.</summary>
        public override IList itemsSource
        {
            get => base.itemsSource;
            set => m_Items.SetItemsSource(value);
        }

        public DefaultTreeViewController() => m_Items = new UaTreeItems<T>(this);

        public void SetRootItems(IList<TreeViewItemData<T>> items) => m_Items.SetRootItems(items);

        public virtual void AddItem(in TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree = true) => m_Items.AddItem(item, parentId, childIndex, rebuildTree);

        public virtual TreeViewItemData<T> GetTreeViewItemDataForId(int id) => m_Items.GetForId(id);

        public virtual TreeViewItemData<T> GetTreeViewItemDataForIndex(int index) => m_Items.GetForIndex(index);

        public override bool TryRemoveItem(int id, bool rebuildTree = true) => m_Items.TryRemoveItem(id, rebuildTree);

        public override object GetItemForId(int id) => GetTreeViewItemDataForId(id).data;

        public virtual T GetDataForId(int id) => GetTreeViewItemDataForId(id).data;

        public virtual T GetDataForIndex(int index) => GetTreeViewItemDataForIndex(index).data;

        public override object GetItemForIndex(int index) => GetDataForIndex(index);
    }

    /// <summary>The tree data of a default tree controller (single- or multi-column) of data type T:
    /// the item data by id beside the controller's hierarchy.</summary>
    internal sealed class UaTreeItems<T>
    {
        readonly BaseTreeViewController m_Controller;
        // By node, not by id: an id given twice names two items (the id answers for the newest).
        readonly Dictionary<UaTreeNode, TreeViewItemData<T>> m_ItemData = new Dictionary<UaTreeNode, TreeViewItemData<T>>();

        public UaTreeItems(BaseTreeViewController controller) => m_Controller = controller;

        UaTreeHierarchy hierarchy => m_Controller.uaHierarchy;

        public void SetItemsSource(IList value)
        {
            if (value == null) SetRootItems(null);
            else if (value is IList<TreeViewItemData<T>> rootItems) SetRootItems(rootItems);
            else Debug.LogError($"Type does not match this tree view controller's data type ({typeof(T)}).");
        }

        /// <summary>Replaces the tree (autoExpand expands every item) and reports
        /// itemsSourceChanged; the rows follow at the next refresh.</summary>
        public void SetRootItems(IList<TreeViewItemData<T>> items)
        {
            // The same list given again (after the game changed it) is read again.
            hierarchy.Clear();
            m_ItemData.Clear();
            if (items != null)
            {
                foreach (var item in items) AddRecursive(item, hierarchy.root);
                var view = m_Controller.uaTreeView;
                if (view != null && view.autoExpand) hierarchy.SetAllExpanded(true);
            }
            m_Controller.UseVisibleRowsAsItemsSource();
            m_Controller.UaRaiseItemsSourceChanged();
        }

        void AddRecursive(TreeViewItemData<T> item, UaTreeNode parent)
        {
            var node = hierarchy.Add(item.id, parent);
            m_ItemData[node] = item;
            foreach (var child in item.children) AddRecursive(child, node);
        }

        /// <summary>Adds an item (with its children) under a parent at a child index (-1 or past the
        /// end: last); the parent's TreeViewItemData gets it as a child too. A parent id that is not
        /// in the tree adds at the root; an id already in the tree adds a second item, and the id
        /// then names the new one (Unity 6000.0.71f1, tests/projects/uitkcollections tree-data).
        /// The rows show the item from the next refresh, at once with rebuildTree or a child index.</summary>
        public void AddItem(in TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree)
        {
            bool known = hierarchy.TryGet(parentId, out var parent);
            if (!known) parent = hierarchy.root;
            var node = hierarchy.Add(item.id, parent);
            if (known && m_ItemData.TryGetValue(parent, out var parentData) && parentData.data != null) parentData.InsertChild(item, childIndex);
            m_ItemData[node] = item;
            foreach (var child in item.children) AddRecursive(child, node);
            var view = m_Controller.uaTreeView;
            if (view.autoExpand) m_Controller.ExpandAncestorNodes(node);
            if (childIndex != -1) hierarchy.MoveTo(node, parent, childIndex);
            if (rebuildTree) view.RefreshItems();
        }

        public TreeViewItemData<T> GetForId(int id) => hierarchy.TryGet(id, out var node) && m_ItemData.TryGetValue(node, out var item) ? item : default;

        /// <summary>The item of a row: of the row's own node, also where its id was given again.</summary>
        public TreeViewItemData<T> GetForIndex(int index) =>
            index >= 0 && index < hierarchy.visibleCount && m_ItemData.TryGetValue(hierarchy.VisibleAt(index), out var item) ? item : default;

        public bool TryRemoveItem(int id, bool rebuildTree)
        {
            if (!hierarchy.TryGet(id, out var node)) return false;
            if (node.parent != null && node.parent != hierarchy.root && m_ItemData.TryGetValue(node.parent, out var parentData) && parentData.data != null) parentData.RemoveChild(id);
            ForgetData(node);
            hierarchy.Remove(node);
            if (rebuildTree) m_Controller.uaTreeView.RefreshItems();
            return true;
        }

        void ForgetData(UaTreeNode node)
        {
            m_ItemData.Remove(node);
            foreach (var child in node.children) ForgetData(child);
        }
    }

    /// <summary>A tree row: a container (row direction) holding the indent, the expand toggle (with
    /// the Foldout toggle classes) and a content container around the made element. The indent is
    /// as wide as the toggle's checkmark with its margins, per depth level.</summary>
    internal partial class ReusableTreeViewItem : ReusableCollectionItem
    {
        Toggle m_Toggle;
        VisualElement m_Container;
        internal VisualElement m_IndentElement;
        VisualElement m_BindableContainer;
        VisualElement m_Checkmark;
        internal int m_Depth;
        float m_IndentWidth;
        internal float? customIndentWidth;
        // The controller whose handlers the row's events reach.
        internal BaseTreeViewController owner;
        readonly EventCallback<PointerUpEvent> m_PointerUpCallback;
        readonly EventCallback<ChangeEvent<bool>> m_ToggleValueChangedCallback;
        readonly EventCallback<GeometryChangedEvent> m_ToggleGeometryChangedCallback;

        public override VisualElement rootElement => m_Container ?? bindableElement;

        internal float indentWidth => customIndentWidth ?? m_IndentWidth;

        public event Action<PointerUpEvent> onPointerUp;

        public event Action<ChangeEvent<bool>> onToggleValueChanged;

        public ReusableTreeViewItem()
        {
            m_PointerUpCallback = OnPointerUp;
            m_ToggleValueChangedCallback = OnToggleValueChanged;
            m_ToggleGeometryChangedCallback = OnToggleGeometryChanged;
        }

        public override void Init(VisualElement item)
        {
            base.Init(item);
            var root = new VisualElement { name = BaseTreeView.itemUssClassName };
            root.AddToClassList(BaseTreeView.itemUssClassName);
            InitExpandHierarchy(root, item);
        }

        protected void InitExpandHierarchy(VisualElement root, VisualElement item)
        {
            m_Container = root;
            m_Container.style.flexDirection = FlexDirection.Row;
            m_IndentElement = new VisualElement { name = BaseTreeView.itemIndentUssClassName };
            m_IndentElement.style.flexDirection = FlexDirection.Row;
            m_Container.hierarchy.Add(m_IndentElement);
            m_Toggle = new Toggle { name = BaseTreeView.itemToggleUssClassName, userData = this };
            m_Toggle.AddToClassList(Foldout.toggleUssClassName);
            m_Toggle.AddToClassList(BaseTreeView.itemToggleUssClassName);
            m_Toggle.visualInput.AddToClassList(Foldout.inputUssClassName);
            m_Checkmark = m_Toggle.visualInput.Q(null, Toggle.checkmarkUssClassName);
            m_Checkmark.AddToClassList(Foldout.checkmarkUssClassName);
            m_Container.hierarchy.Add(m_Toggle);
            m_BindableContainer = new VisualElement { name = BaseTreeView.itemContentContainerUssClassName };
            m_BindableContainer.style.flexGrow = 1f;
            m_BindableContainer.AddToClassList(BaseTreeView.itemContentContainerUssClassName);
            m_Container.Add(m_BindableContainer);
            m_BindableContainer.Add(item);
        }

        public override void PreAttachElement()
        {
            base.PreAttachElement();
            rootElement.AddToClassList(BaseTreeView.itemUssClassName);
            m_Container?.RegisterCallback(m_PointerUpCallback);
            m_Checkmark?.RegisterCallback(m_ToggleGeometryChangedCallback);
            m_Toggle?.RegisterValueChangedCallback(m_ToggleValueChangedCallback);
        }

        public override void DetachElement()
        {
            base.DetachElement();
            rootElement.RemoveFromClassList(BaseTreeView.itemUssClassName);
            m_Container?.UnregisterCallback(m_PointerUpCallback);
            m_Checkmark?.UnregisterCallback(m_ToggleGeometryChangedCallback);
            m_Toggle?.UnregisterValueChangedCallback(m_ToggleValueChangedCallback);
        }

        public void Indent(int depth)
        {
            if (m_IndentElement == null) return;
            m_Depth = depth;
            UpdateIndentLayout();
        }

        public void SetExpandedWithoutNotify(bool expanded) => m_Toggle?.SetValueWithoutNotify(expanded);

        public void SetToggleVisibility(bool visible)
        {
            if (m_Toggle != null) m_Toggle.visible = visible;
        }

        void OnToggleGeometryChanged(GeometryChangedEvent evt)
        {
            var style = m_Checkmark.resolvedStyle;
            float width = style.width + style.marginLeft + style.marginRight;
            if (Math.Abs(width - m_IndentWidth) < float.Epsilon) return;
            m_IndentWidth = width;
            UpdateIndentLayout();
        }

        void UpdateIndentLayout()
        {
            m_IndentElement.style.width = indentWidth * m_Depth;
            m_IndentElement.EnableInClassList(BaseTreeView.itemIndentUssClassName, m_Depth > 0);
        }

        void OnPointerUp(PointerUpEvent evt) => onPointerUp?.Invoke(evt);

        void OnToggleValueChanged(ChangeEvent<bool> evt) => onToggleValueChanged?.Invoke(evt);
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;

    /// <summary>A node of a tree view's hierarchy: an item id, its parent, its ordered children and
    /// its expanded flag.</summary>
    internal sealed class UaTreeNode
    {
        public int id;
        public UaTreeNode parent;
        public readonly List<UaTreeNode> children = new List<UaTreeNode>();
        public bool expanded;
        // Taken out of the tree (rows refreshed before that may still name it).
        public bool removed;
    }

    /// <summary>A tree view's hierarchy by id and its rows: the visible ones (nodes whose ancestors
    /// are all expanded, depth first) and all of them. The rows are a view of the hierarchy as it
    /// was when last refreshed: a move, an expansion or new root items refresh them; items added
    /// or removed show once the tree view refreshes (or one of those changes does), until then the
    /// rows are the old ones while parents and children already answer the new hierarchy (Unity
    /// 6000.0.71f1, tests/projects/uitkcollections tree-data).</summary>
    internal sealed class UaTreeHierarchy
    {
        readonly Dictionary<int, UaTreeNode> m_ById = new Dictionary<int, UaTreeNode>();
        readonly List<UaTreeNode> m_Visible = new List<UaTreeNode>();
        readonly List<UaTreeNode> m_All = new List<UaTreeNode>();
        readonly Stack<(UaTreeNode node, bool visible)> m_Walk = new Stack<(UaTreeNode, bool)>();
        // Items were added or removed since the rows were refreshed.
        bool m_Pending;
        readonly Dictionary<UaTreeNode, int> m_VisibleIndex = new Dictionary<UaTreeNode, int>();
        readonly VisibleRows m_VisibleRows;
        bool m_Dirty = true;

        public readonly UaTreeNode root = new UaTreeNode { id = -1, expanded = true };

        public UaTreeHierarchy() => m_VisibleRows = new VisibleRows(this);

        public int count => m_ById.Count;

        public int visibleCount
        {
            get
            {
                Update();
                return m_Visible.Count;
            }
        }

        /// <summary>The visible rows as a read-only list of item ids (the controller's items source).</summary>
        public IList visibleRows => m_VisibleRows;

        public bool TryGet(int id, out UaTreeNode node) => m_ById.TryGetValue(id, out node);

        public UaTreeNode VisibleAt(int index)
        {
            Update();
            return m_Visible[index];
        }

        public int VisibleIndexOf(UaTreeNode node)
        {
            Update();
            return m_VisibleIndex.TryGetValue(node, out int index) ? index : -1;
        }

        public void Clear()
        {
            m_ById.Clear();
            root.children.Clear();
            m_Dirty = true;
        }

        /// <summary>Adds an item under a parent. An id already in the tree gets a second node; the
        /// id then names the new one, the old one stays where it is.</summary>
        public UaTreeNode Add(int id, UaTreeNode parent)
        {
            var node = new UaTreeNode { id = id, parent = parent };
            m_ById[id] = node;
            parent.children.Add(node);
            m_Pending = true;
            return node;
        }

        /// <summary>Takes a node and its descendants out; their ids no longer name anything.</summary>
        public void Remove(UaTreeNode node)
        {
            node.parent?.children.Remove(node);
            Forget(node);
            m_Pending = true;
        }

        void Forget(UaTreeNode node)
        {
            node.removed = true;
            m_ById.Remove(node.id);
            foreach (var child in node.children) Forget(child);
        }

        /// <summary>Moves a node under a parent at a child index (-1 or past the end: last).</summary>
        public void MoveTo(UaTreeNode node, UaTreeNode parent, int childIndex, bool refreshRows = true)
        {
            node.parent.children.Remove(node);
            node.parent = parent;
            if (childIndex < 0 || childIndex > parent.children.Count) parent.children.Add(node);
            else parent.children.Insert(childIndex, node);
            if (refreshRows) m_Dirty = true;
        }

        /// <summary>Lets the rows follow items added or removed since their last refresh.</summary>
        public void FlushPending()
        {
            if (m_Pending) m_Dirty = true;
        }

        /// <summary>Every row's id, depth first, as of the last refresh, without the removed ones.</summary>
        public void AddAllIds(List<int> ids)
        {
            Update();
            foreach (var node in m_All)
                if (!node.removed) ids.Add(node.id);
        }

        public void SetExpanded(UaTreeNode node, bool expanded, bool recurse)
        {
            node.expanded = expanded;
            if (recurse)
                foreach (var child in node.children) SetExpanded(child, expanded, true);
            m_Dirty = true;
        }

        public void SetAllExpanded(bool expanded)
        {
            foreach (var child in root.children) SetExpanded(child, expanded, true);
            m_Dirty = true;
        }

        /// <summary>Adds the ids under a node, depth first (not the node's own).</summary>
        public void AddDescendantIds(UaTreeNode node, List<int> ids)
        {
            foreach (var child in node.children)
            {
                ids.Add(child.id);
                AddDescendantIds(child, ids);
            }
        }

        public void AddExpandedIds(List<int> ids)
        {
            foreach (var node in m_ById.Values)
                if (node.expanded) ids.Add(node.id);
        }

        void Update()
        {
            if (!m_Dirty) return;
            m_Dirty = false;
            m_Pending = false;
            m_Visible.Clear();
            m_VisibleIndex.Clear();
            m_All.Clear();
            for (int i = root.children.Count - 1; i >= 0; i--) m_Walk.Push((root.children[i], true));
            while (m_Walk.Count > 0)
            {
                var (node, visible) = m_Walk.Pop();
                m_All.Add(node);
                if (visible)
                {
                    m_VisibleIndex[node] = m_Visible.Count;
                    m_Visible.Add(node);
                }
                bool childrenVisible = visible && node.expanded;
                for (int i = node.children.Count - 1; i >= 0; i--) m_Walk.Push((node.children[i], childrenVisible));
            }
        }

        /// <summary>The visible rows' item ids, read-only.</summary>
        sealed class VisibleRows : IList
        {
            readonly UaTreeHierarchy m_Owner;

            public VisibleRows(UaTreeHierarchy owner) => m_Owner = owner;

            public int Count => m_Owner.visibleCount;
            public object this[int index] { get => m_Owner.VisibleAt(index).id; set => throw new NotSupportedException(); }
            public bool IsFixedSize => true;
            public bool IsReadOnly => true;
            public bool IsSynchronized => false;
            public object SyncRoot => this;
            public int Add(object value) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(object value) => IndexOf(value) >= 0;
            public int IndexOf(object value) => value is int id && m_Owner.TryGet(id, out var node) ? m_Owner.VisibleIndexOf(node) : -1;
            public void Insert(int index, object value) => throw new NotSupportedException();
            public void Remove(object value) => throw new NotSupportedException();
            public void RemoveAt(int index) => throw new NotSupportedException();

            public void CopyTo(Array array, int index)
            {
                for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i);
            }

            public IEnumerator GetEnumerator()
            {
                for (int i = 0; i < Count; i++) yield return this[i];
            }
        }
    }
}
