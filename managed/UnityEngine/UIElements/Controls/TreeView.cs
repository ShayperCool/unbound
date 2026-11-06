using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements;

// TreeView, BaseTreeView and TreeViewItemData<T>, from the Unity 6000.0 Scripting API pages of these
// types and their members and the Manual page "TreeView" (UXML element reference, with its attribute
// and USS class tables). The tree is shown through BaseVerticalCollectionView's virtualization: the
// controller's items source is the list of visible rows (the items whose ancestors are all
// expanded), in depth-first order, and each pooled row element holds an indent, a foldout toggle and
// the element made by makeItem.
//
// Documented and implemented here:
//   - SetRootItems "Sets the root items to use with the default tree view controller"; it forces a
//     DefaultTreeViewController<T>;
//   - the controller's itemsSource "Contains items that are expanded in the tree"; GetIndexForId
//     returns -1 "if the item is not visible"; GetParentId returns -1 at the root;
//   - bindItem receives the visible index, used with GetItemDataForIndex<T> (Manual note);
//   - ExpandItem/CollapseItem with expandAllChildren/collapseAllChildren and refresh ("Set to false
//     when doing multiple operations ... only do one RefreshItems once all operations are done");
//     ExpandRootItems, ExpandAll, CollapseAll, IsExpanded;
//   - autoExpand: "When true, items are automatically expanded when added to the TreeView";
//   - itemExpandedChanged is "Raised when an item is expanded or collapsed"; its id "Returns -1 when
//     expandAll() or collapseAll() is being called";
//   - selection by id (SetSelectionById, SetSelectionByIdWithoutNotify, AddToSelectionById,
//     RemoveFromSelectionById) on top of the collection view's selection by index; selecting by id
//     "will also expand the selected item(s) if not expanded already" (Remarks of the first three);
//   - the USS classes unity-tree-view, __item, __item-toggle, __item-indent, __item-content.
//
// Unbound's choices (unmeasured; the pages do not describe them) are marked "Unbound's choice" where
// they are made. Not implemented here and still generated stubs (they warn when called): AddItem,
// TryRemoveItem, view-data persistence of the expanded state, drag and drop reordering, custom view
// controllers, and the controller-only members of BaseTreeViewController that are not virtual.
namespace UnityEngine.UIElements
{
    /// <summary>Base class of tree views: a collection view whose rows are the visible items of a tree.</summary>
    public abstract partial class BaseTreeView : BaseVerticalCollectionView
    {
        public new static readonly string ussClassName = "unity-tree-view";
        public new static readonly string itemUssClassName = ussClassName + "__item";
        public static readonly string itemToggleUssClassName = ussClassName + "__item-toggle";
        [Obsolete("Individual item indents are no longer used, see itemIndentUssClassName instead")]
        public static readonly string itemIndentsContainerUssClassName = ussClassName + "__item-indents";
        public static readonly string itemIndentUssClassName = ussClassName + "__item-indent";
        public static readonly string itemContentContainerUssClassName = ussClassName + "__item-content";

        // Unbound's choice (unmeasured): the expanded state is held by the view, by id, so it survives a
        // new SetRootItems for the ids that still exist there with children. Items start collapsed
        // unless autoExpand is set when they are added.
        readonly HashSet<int> m_UaExpanded = new HashSet<int>();
        bool m_AutoExpand;

        public new IList itemsSource
        {
            get => base.itemsSource;
            internal set => base.itemsSource = value;
        }

        public new BaseTreeViewController viewController => base.viewController as BaseTreeViewController;

        /// <summary>Unbound's choice (unmeasured): only items added after autoExpand is set are expanded
        /// by it (SetRootItems); setting it changes no existing item.</summary>
        public bool autoExpand
        {
            get => m_AutoExpand;
            set => m_AutoExpand = value;
        }

        public event Action<TreeViewExpansionChangedArgs> itemExpandedChanged;

        UaTreeModel uaTree => (base.viewController as IUaTreeController)?.tree;

        public BaseTreeView() : this((int)ItemHeightUnset) { }

        public BaseTreeView(int itemHeight) : base(null, itemHeight)
        {
            AddToClassList(ussClassName);
        }

        public void SetRootItems<T>(IList<TreeViewItemData<T>> rootItems) => SetRootItemsInternal(rootItems);

        // The typed operations TreeView implements (Unity's signatures), declared here so that the
        // hand-written code builds alone for `unbound gen-stubs`.
        internal abstract void SetRootItemsInternal<T>(IList<TreeViewItemData<T>> rootItems);
        private protected abstract IEnumerable<TreeViewItemData<T>> GetSelectedItemsInternal<T>();
        private protected abstract T GetItemDataForIndexInternal<T>(int index);
        private protected abstract T GetItemDataForIdInternal<T>(int id);

        public IEnumerable<int> GetRootIds()
        {
            var tree = uaTree;
            if (tree == null) return Array.Empty<int>();
            return tree.roots.Select(n => n.id).ToArray();
        }

        public int GetTreeCount() => uaTree?.count ?? 0;

        private protected override void CreateVirtualizationController() => CreateVirtualizationController<UaTreeRow>();

        public override void SetViewController(CollectionViewController controller)
        {
            base.SetViewController(controller);
            if (controller != null && !(controller is IUaTreeController))
                Debug.LogError($"Unbound: tree views only support the default controller set by SetRootItems yet; {controller.GetType().FullName} is not supported and its items are not shown");
        }

        /// <summary>Unbound's choice (unmeasured): an index outside the visible rows answers -1.</summary>
        public int GetIdForIndex(int index) => base.viewController?.GetIdForIndex(index) ?? -1;

        public int GetParentIdForIndex(int index)
        {
            var tree = uaTree;
            return tree != null && tree.TryGetRow(index, out var node) ? node.parentId : -1;
        }

        public IEnumerable<int> GetChildrenIdsForIndex(int index)
        {
            var tree = uaTree;
            if (tree == null || !tree.TryGetRow(index, out var node)) return Array.Empty<int>();
            return node.children.Select(c => c.id).ToArray();
        }

        public IEnumerable<TreeViewItemData<T>> GetSelectedItems<T>() => GetSelectedItemsInternal<T>();

        public T GetItemDataForIndex<T>(int index) => GetItemDataForIndexInternal<T>(index);

        public T GetItemDataForId<T>(int id) => GetItemDataForIdInternal<T>(id);

        /// <summary>The default controller of data type T, or null without tree data. Unbound's choice
        /// (unmeasured): asking for another data type than the tree's throws ArgumentException.</summary>
        internal UaTreeController<T> UaControllerOf<T>()
        {
            var controller = base.viewController;
            if (controller == null || controller is UaTreeController<T>) return controller as UaTreeController<T>;
            var type = controller is IUaTreeController tree ? tree.dataType : controller.GetType();
            throw new ArgumentException($"Type parameter ({typeof(T)}) differs from the data type of the tree view ({type}).");
        }

        /// <summary>Sets the root items on the default controller of type T, creating it when the view
        /// has another controller (the Scripting API: "This will force the use of a
        /// DefaultTreeViewController").</summary>
        internal void UaSetRootItems<T>(IList<TreeViewItemData<T>> rootItems)
        {
            if (!(base.viewController is UaTreeController<T> controller))
            {
                controller = new UaTreeController<T>();
                SetViewController(controller);
            }
            controller.SetRoots(rootItems);
            var tree = controller.tree;
            // Unbound's choice (unmeasured): ids that no longer name an item with children lose their
            // expanded state; with autoExpand every added item with children is expanded, without
            // itemExpandedChanged.
            m_UaExpanded.RemoveWhere(id => !tree.TryGet(id, out var node) || node.children.Count == 0);
            if (m_AutoExpand)
                foreach (var node in tree.all)
                    if (node.children.Count > 0) m_UaExpanded.Add(node.id);
            controller.RebuildRows();
            // Unbound's choice (unmeasured): as a ListView's new items source does, the rows are refreshed
            // and itemsSourceChanged is sent.
            RefreshItems();
            controller.RaiseRowsChanged();
        }

        public void SetSelectionById(int id) => SetSelectionById(new[] { id });

        public void SetSelectionById(IEnumerable<int> ids) => SetSelectionInternalById(ids, true);

        public void SetSelectionByIdWithoutNotify(IEnumerable<int> ids) => SetSelectionInternalById(ids, false);

        internal void SetSelectionInternalById(IEnumerable<int> ids, bool sendNotification)
        {
            if (ids == null) return;
            SetSelectionInternal(UaShowAndIndex(ids), sendNotification);
        }

        public void AddToSelectionById(int id)
        {
            var indices = UaShowAndIndex(new[] { id });
            if (indices.Count > 0) AddToSelection(indices[0]);
        }

        /// <summary>Unbound's choice (unmeasured): an id whose row is hidden under a collapsed ancestor is
        /// not removed.</summary>
        public void RemoveFromSelectionById(int id)
        {
            int index = uaTree?.RowOf(id) ?? -1;
            if (index >= 0) RemoveFromSelection(index);
        }

        /// <summary>Expands the items selected by id and their collapsed ancestors, then answers their
        /// visible indices. Documented (6000.0 Scripting API, Remarks of SetSelectionById(int),
        /// SetSelectionById(IEnumerable&lt;int&gt;), AddToSelectionById and
        /// SetSelectionByIdWithoutNotify): "This will also expand the selected item(s) if not expanded
        /// already." The item itself is expanded as ExpandItem(id) expands it (only an item with children
        /// changes state). Unbound's choices (unmeasured; the pages do not say): its collapsed ancestors
        /// are expanded too, so it has a visible index to select; every item whose state changed sends
        /// itemExpandedChanged (that event is "Raised when an item is expanded or collapsed"), also from
        /// SetSelectionByIdWithoutNotify, whose "without triggering a selection change callback" names
        /// only the selection callback; the events follow the given ids, each id's ancestors root first,
        /// then the item, after the rows are refreshed; unknown ids are skipped.</summary>
        List<int> UaShowAndIndex(IEnumerable<int> ids)
        {
            var indices = new List<int>();
            var tree = uaTree;
            if (tree == null) return indices;
            var known = ids.Where(id => tree.TryGet(id, out _)).ToList();
            var opened = new List<int>();
            var chain = new List<UaTreeNode>();
            foreach (int id in known)
            {
                tree.TryGet(id, out var node);
                chain.Clear();
                int parent = node.parentId;
                while (parent != -1 && tree.TryGet(parent, out var ancestor))
                {
                    chain.Add(ancestor);
                    parent = ancestor.parentId;
                }
                chain.Reverse();
                chain.Add(node);
                foreach (var item in chain)
                    if (UaMark(item, true, false)) opened.Add(item.id);
            }
            if (opened.Count > 0)
            {
                UaRebuildRows();
                // Re-index the current selection to its rows after the expansion, quietly, so the
                // refresh does not report a moved selection: SetSelectionByIdWithoutNotify is
                // documented "without triggering a selection change callback", and the notifying
                // variants report only the new selection.
                var kept = new List<int>();
                foreach (int selected in selectedIds)
                {
                    int row = tree.RowOf(selected);
                    if (row >= 0) kept.Add(row);
                }
                SetSelectionInternal(kept, false);
                RefreshItems();
                foreach (int id in opened) UaRaiseExpanded(id, true, false);
            }
            foreach (int id in known)
            {
                int index = tree.RowOf(id);
                if (index >= 0) indices.Add(index);
            }
            return indices;
        }

        public bool IsExpanded(int id) => m_UaExpanded.Contains(id);

        /// <summary>Unbound's choices (unmeasured): an unknown id, or an item without children, changes
        /// nothing; nothing is sent when no expanded state changed. The visible rows and the id/index
        /// mapping are updated at once, even with refresh false (only the bound row elements wait for
        /// RefreshItems); itemExpandedChanged is sent after the refresh.</summary>
        public void CollapseItem(int id, bool collapseAllChildren = false, bool refresh = true) => UaSetExpanded(id, false, collapseAllChildren, refresh);

        public void ExpandItem(int id, bool expandAllChildren = false, bool refresh = true) => UaSetExpanded(id, true, expandAllChildren, refresh);

        /// <summary>Unbound's choice (unmeasured): one itemExpandedChanged per root whose state changed.</summary>
        public void ExpandRootItems()
        {
            var tree = uaTree;
            if (tree == null) return;
            var opened = new List<int>();
            foreach (var root in tree.roots)
                if (UaMark(root, true, false)) opened.Add(root.id);
            if (opened.Count == 0) return;
            UaRebuildRows();
            RefreshItems();
            foreach (int id in opened) UaRaiseExpanded(id, true, false);
        }

        /// <summary>Documented: the event's id is -1 for ExpandAll. Unbound's choice (unmeasured): one
        /// event, sent whenever the view has tree data, with isAppliedToAllChildren true.</summary>
        public void ExpandAll() => UaSetAll(true);

        public void CollapseAll() => UaSetAll(false);

        void UaSetAll(bool expanded)
        {
            var tree = uaTree;
            if (tree == null) return;
            foreach (var root in tree.roots) UaMark(root, expanded, true);
            UaRebuildRows();
            RefreshItems();
            UaRaiseExpanded(-1, expanded, true);
        }

        void UaSetExpanded(int id, bool expanded, bool allChildren, bool refresh)
        {
            var tree = uaTree;
            if (tree == null || !tree.TryGet(id, out var node)) return;
            if (!UaMark(node, expanded, allChildren)) return;
            UaRebuildRows();
            if (refresh) RefreshItems();
            UaRaiseExpanded(id, expanded, allChildren);
        }

        /// <summary>Sets the expanded state of an item with children (and of its descendants with
        /// children when <paramref name="deep"/>); answers whether any state changed.</summary>
        bool UaMark(UaTreeNode node, bool expanded, bool deep)
        {
            bool changed = false;
            if (node.children.Count > 0)
                changed = expanded ? m_UaExpanded.Add(node.id) : m_UaExpanded.Remove(node.id);
            if (deep)
                foreach (var child in node.children)
                    changed |= UaMark(child, expanded, true);
            return changed;
        }

        void UaRebuildRows() => (base.viewController as IUaTreeController)?.RebuildRows();

        void UaRaiseExpanded(int id, bool expanded, bool allChildren)
        {
            if (itemExpandedChanged == null) return;
            itemExpandedChanged(new TreeViewExpansionChangedArgs { id = id, isExpanded = expanded, isAppliedToAllChildren = allChildren });
        }

        /// <summary>Unbound's choice (unmeasured): the move-right key expands the first selected item and
        /// move-left collapses it (with alt: its whole hierarchy); nothing else moves.</summary>
        private protected override bool HandleItemNavigation(bool moveIn, bool altPressed)
        {
            var tree = uaTree;
            if (tree == null || !tree.TryGetRow(selectedIndex, out var node) || node.children.Count == 0) return false;
            if (moveIn == IsExpanded(node.id) && !altPressed) return false;
            UaSetExpanded(node.id, moveIn, altPressed, true);
            return true;
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

        // The callbacks behave as ListView's: a new makeItem rebuilds the rows, a new bindItem refreshes
        // them (the Manual: call Rebuild after changing makeItem or destroyItem).
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

        public new TreeViewController viewController => base.viewController as TreeViewController;

        public TreeView() : this((int)ItemHeightUnset, null, null) { }

        public TreeView(Func<VisualElement> makeItem, Action<VisualElement, int> bindItem) : this((int)ItemHeightUnset, makeItem, bindItem) { }

        public TreeView(int itemHeight, Func<VisualElement> makeItem, Action<VisualElement, int> bindItem) : base(itemHeight)
        {
            m_TemplateMakeItem = UaTemplateMakeItem;
            m_MakeItem = makeItem;
            m_BindItem = bindItem;
        }

        VisualElement UaTemplateMakeItem() => m_ItemTemplate != null ? m_ItemTemplate.Instantiate() : new Label(k_InvalidTemplateError);

        /// <summary>Rows come from <see cref="itemTemplate"/> with no <see cref="bindItem"/>: there is a
        /// template, makeItem is still the template factory the itemTemplate setter assigned (not a
        /// game callback), and no bind callback is set. The itemTemplate page says the template
        /// replaces makeItem; it does not say what such rows show without bindItem. Unbound has no
        /// runtime data binding yet, so its interim is to show the template rows unbound, with a
        /// warning, instead of an empty tree. Replacing makeItem or clearing the template ends the
        /// exemption.</summary>
        internal bool bindsTemplateWithoutBindItem =>
            m_ItemTemplate != null && m_BindItem == null && ReferenceEquals(m_MakeItem, m_TemplateMakeItem);

        internal override bool HasValidDataAndBindings() =>
            base.HasValidDataAndBindings() && ((makeItem != null) == (bindItem != null) || bindsTemplateWithoutBindItem);

        /// <summary>The controller made when items are assigned without SetRootItems.</summary>
        protected override CollectionViewController CreateViewController() => new UaTreeController<object>();

        internal override void SetRootItemsInternal<T>(IList<TreeViewItemData<T>> rootItems) => UaSetRootItems(rootItems);

        private protected override IEnumerable<TreeViewItemData<T>> GetSelectedItemsInternal<T>()
        {
            var controller = UaControllerOf<T>();
            if (controller == null) return Array.Empty<TreeViewItemData<T>>();
            return selectedIndices.Select(controller.GetTreeViewItemDataForIndex).ToArray();
        }

        /// <summary>Unbound's choice (unmeasured): without tree data, or for an index outside the
        /// visible rows, the default value.</summary>
        private protected override T GetItemDataForIndexInternal<T>(int index)
        {
            var controller = UaControllerOf<T>();
            return controller != null ? controller.GetDataForIndex(index) : default;
        }

        /// <summary>Unbound's choice (unmeasured): without tree data, or for an unknown id, the default
        /// value.</summary>
        private protected override T GetItemDataForIdInternal<T>(int id)
        {
            var controller = UaControllerOf<T>();
            return controller != null ? controller.GetDataForId(id) : default;
        }
    }

    /// <summary>Item structure provided to a TreeView using the default implementation: an id, its
    /// data and its children.</summary>
    public readonly partial struct TreeViewItemData<T>
    {
        readonly List<TreeViewItemData<T>> m_UaChildren;

        /// <summary>Unbound's choice (unmeasured): the children list is kept as given (null for none),
        /// not copied.</summary>
        public TreeViewItemData(int id, T data, List<TreeViewItemData<T>> children = null)
        {
            this.id = id;
            this.data = data;
            m_UaChildren = children;
        }

        public int id { get; }

        public T data { get; }

        public IEnumerable<TreeViewItemData<T>> children => m_UaChildren ?? (IEnumerable<TreeViewItemData<T>>)Array.Empty<TreeViewItemData<T>>();

        public bool hasChildren => m_UaChildren != null && m_UaChildren.Count > 0;
    }

    /// <summary>"A data structure for the tree view item expansion event" (6000.0 Scripting API,
    /// TreeViewExpansionChangedArgs): id is "The id of the item being expanded or collapsed. Returns -1
    /// when expandAll() or collapseAll() is being called"; isExpanded "Indicates whether the item is
    /// expanded (true) or collapsed (false)"; isAppliedToAllChildren "Indicates whether the
    /// expandAllChildren or collapsedAllChildren is applied when expanding the item". The values are
    /// those of the expand or collapse call that raised itemExpandedChanged.</summary>
    public partial class TreeViewExpansionChangedArgs
    {
        public TreeViewExpansionChangedArgs() { }

        public int id { get; set; }

        public bool isExpanded { get; set; }

        public bool isAppliedToAllChildren { get; set; }
    }

    // The tree view controllers are still generated stubs. They are declared here with the stub
    // members UaTreeController overrides, written as the generator writes them (they warn when
    // called), so that the hand-written code builds alone for `unbound gen-stubs`; the generator adds
    // the rest of each controller.
    [global::Unbound.Stub]
    public abstract partial class BaseTreeViewController : CollectionViewController
    {
        const string k_Type = "UnityEngine.UIElements.BaseTreeViewController.";

        [global::Unbound.Stub]
        public virtual IEnumerable<int> GetAllItemIds(IEnumerable<int> rootIds = null) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetAllItemIds)); return Array.Empty<int>(); }
        [global::Unbound.Stub]
        public virtual int GetParentId(int id) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetParentId)); return default; }
        [global::Unbound.Stub]
        public virtual IEnumerable<int> GetChildrenIds(int id) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetChildrenIds)); return Array.Empty<int>(); }
        [global::Unbound.Stub]
        public virtual int GetTreeItemsCount() { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetTreeItemsCount)); return default; }
        [global::Unbound.Stub]
        public virtual bool HasChildren(int id) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(HasChildren)); return default; }
        [global::Unbound.Stub]
        public virtual bool CanChangeExpandedState(int id) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(CanChangeExpandedState)); return default; }
    }

    [global::Unbound.Stub]
    public abstract partial class TreeViewController : BaseTreeViewController
    {
        const string k_Type = "UnityEngine.UIElements.TreeViewController.";

        [global::Unbound.Stub]
        protected override VisualElement MakeItem() { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(MakeItem)); return default; }
        [global::Unbound.Stub]
        protected override void BindItem(VisualElement element, int index) => global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(BindItem));
        [global::Unbound.Stub]
        protected override void UnbindItem(VisualElement element, int index) => global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(UnbindItem));
        [global::Unbound.Stub]
        protected override void DestroyItem(VisualElement element) => global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(DestroyItem));
    }

    [global::Unbound.Stub]
    public partial class DefaultTreeViewController<T> : TreeViewController
    {
        const string k_Type = "UnityEngine.UIElements.DefaultTreeViewController<T>.";

        [global::Unbound.Stub]
        public virtual TreeViewItemData<T> GetTreeViewItemDataForId(int id) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetTreeViewItemDataForId)); return default; }
        [global::Unbound.Stub]
        public virtual TreeViewItemData<T> GetTreeViewItemDataForIndex(int index) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetTreeViewItemDataForIndex)); return default; }
        [global::Unbound.Stub]
        public virtual T GetDataForId(int id) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetDataForId)); return default; }
        [global::Unbound.Stub]
        public virtual T GetDataForIndex(int index) { global::Unbound.Runtime.Stubs.NotImplemented(k_Type + nameof(GetDataForIndex)); return default; }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>One item of a tree view's tree: its id, place and boxed TreeViewItemData.</summary>
    internal sealed class UaTreeNode
    {
        public int id;
        public int parentId;
        public int depth;
        public object item;
        public readonly List<UaTreeNode> children = new List<UaTreeNode>();
    }

    /// <summary>A tree view's items by id, and its visible rows: the items whose ancestors are all
    /// expanded, depth first, children in their given order.</summary>
    internal sealed class UaTreeModel
    {
        readonly Dictionary<int, UaTreeNode> m_ById = new Dictionary<int, UaTreeNode>();
        readonly List<UaTreeNode> m_Roots = new List<UaTreeNode>();
        readonly List<UaTreeNode> m_All = new List<UaTreeNode>();
        readonly List<UaTreeNode> m_Rows = new List<UaTreeNode>();
        readonly Dictionary<int, int> m_RowOfId = new Dictionary<int, int>();

        public int count => m_All.Count;
        public IReadOnlyList<UaTreeNode> roots => m_Roots;
        public IReadOnlyList<UaTreeNode> all => m_All;
        public IReadOnlyList<UaTreeNode> rows => m_Rows;

        public bool TryGet(int id, out UaTreeNode node) => m_ById.TryGetValue(id, out node);

        public bool TryGetRow(int index, out UaTreeNode node)
        {
            node = index >= 0 && index < m_Rows.Count ? m_Rows[index] : null;
            return node != null;
        }

        public int RowOf(int id) => m_RowOfId.TryGetValue(id, out int index) ? index : -1;

        public void Clear()
        {
            m_ById.Clear();
            m_Roots.Clear();
            m_All.Clear();
            m_Rows.Clear();
            m_RowOfId.Clear();
        }

        /// <summary>Adds an item under a parent (null at the root). Unbound's choice (unmeasured): a
        /// repeated id logs one error and is skipped with its children, since ids name items.</summary>
        public UaTreeNode Add(int id, object item, UaTreeNode parent)
        {
            if (m_ById.ContainsKey(id))
            {
                Debug.LogError($"Unbound: tree view item id {id} is used more than once; the repeated item and its children are not shown");
                return null;
            }
            var node = new UaTreeNode { id = id, item = item, parentId = parent?.id ?? -1, depth = parent != null ? parent.depth + 1 : 0 };
            m_ById.Add(id, node);
            m_All.Add(node);
            (parent != null ? parent.children : m_Roots).Add(node);
            return node;
        }

        public void RebuildRows(Func<int, bool> isExpanded)
        {
            m_Rows.Clear();
            m_RowOfId.Clear();
            var stack = new Stack<UaTreeNode>();
            for (int i = m_Roots.Count - 1; i >= 0; i--) stack.Push(m_Roots[i]);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                m_RowOfId[node.id] = m_Rows.Count;
                m_Rows.Add(node);
                if (node.children.Count == 0 || !isExpanded(node.id)) continue;
                for (int i = node.children.Count - 1; i >= 0; i--) stack.Push(node.children[i]);
            }
        }
    }

    /// <summary>What a tree view needs from its default controller, whatever its data type.</summary>
    internal interface IUaTreeController
    {
        UaTreeModel tree { get; }
        Type dataType { get; }
        void RebuildRows();
    }

    /// <summary>The default tree view controller of data type T: the tree of TreeViewItemData, its
    /// visible rows as items source, and the row elements made and bound by the TreeView's callbacks.
    /// Every member the collection view machinery calls is overridden here, since the generated
    /// controller bases are still stubs.</summary>
    internal sealed class UaTreeController<T> : DefaultTreeViewController<T>, IUaTreeController
    {
        readonly UaTreeModel m_Tree = new UaTreeModel();
        readonly List<TreeViewItemData<T>> m_Source = new List<TreeViewItemData<T>>();

        public UaTreeModel tree => m_Tree;
        public Type dataType => typeof(T);

        BaseTreeView uaView => view as BaseTreeView;

        public void SetRoots(IList<TreeViewItemData<T>> rootItems)
        {
            m_Tree.Clear();
            if (rootItems == null) return;
            foreach (var item in rootItems) AddRecursive(item, null);
        }

        void AddRecursive(TreeViewItemData<T> item, UaTreeNode parent)
        {
            var node = m_Tree.Add(item.id, item, parent);
            if (node == null) return;
            foreach (var child in item.children) AddRecursive(child, node);
        }

        public void RebuildRows()
        {
            var v = uaView;
            m_Tree.RebuildRows(id => v != null && v.IsExpanded(id));
            m_Source.Clear();
            foreach (var node in m_Tree.rows) m_Source.Add((TreeViewItemData<T>)node.item);
        }

        public void RaiseRowsChanged() => RaiseItemsSourceChanged();

        /// <summary>The visible rows' TreeViewItemData (the Scripting API: the source "contains the items
        /// wrappers"). Unbound's choice (unmeasured): assigning a source is refused with an error.</summary>
        public override IList itemsSource
        {
            get => m_Source;
            set => Debug.LogError("Unbound: a tree view's items come from SetRootItems; assigning itemsSource is not supported");
        }

        public override int GetItemsCount() => m_Tree.rows.Count;

        public override int GetIndexForId(int id) => m_Tree.RowOf(id);

        public override int GetIdForIndex(int index) => m_Tree.TryGetRow(index, out var node) ? node.id : -1;

        /// <summary>Unbound's choice (unmeasured): an item of the collection view (selectedItem,
        /// selectedItems, itemsChosen) is its data.</summary>
        public override object GetItemForIndex(int index) => m_Tree.TryGetRow(index, out var node) ? (object)((TreeViewItemData<T>)node.item).data : null;

        public override object GetItemForId(int id) => m_Tree.TryGet(id, out var node) ? (object)((TreeViewItemData<T>)node.item).data : null;

        public override TreeViewItemData<T> GetTreeViewItemDataForId(int id) => m_Tree.TryGet(id, out var node) ? (TreeViewItemData<T>)node.item : default;

        public override TreeViewItemData<T> GetTreeViewItemDataForIndex(int index) => m_Tree.TryGetRow(index, out var node) ? (TreeViewItemData<T>)node.item : default;

        public override T GetDataForId(int id) => GetTreeViewItemDataForId(id).data;

        public override T GetDataForIndex(int index) => GetTreeViewItemDataForIndex(index).data;

        public override int GetParentId(int id) => m_Tree.TryGet(id, out var node) ? node.parentId : -1;

        public override IEnumerable<int> GetChildrenIds(int id) =>
            m_Tree.TryGet(id, out var node) ? node.children.Select(c => c.id).ToArray() : Array.Empty<int>();

        public override bool HasChildren(int id) => m_Tree.TryGet(id, out var node) && node.children.Count > 0;

        public override int GetTreeItemsCount() => m_Tree.count;

        public override IEnumerable<int> GetAllItemIds(IEnumerable<int> rootIds = null)
        {
            var ids = new List<int>();
            void Walk(UaTreeNode node)
            {
                ids.Add(node.id);
                foreach (var child in node.children) Walk(child);
            }
            if (rootIds == null)
            {
                foreach (var root in m_Tree.roots) Walk(root);
            }
            else
            {
                foreach (int id in rootIds)
                    if (m_Tree.TryGet(id, out var node)) Walk(node);
            }
            return ids;
        }

        /// <summary>Unbound's choice (unmeasured): only items with children expand or collapse.</summary>
        public override bool CanChangeExpandedState(int id) => HasChildren(id);

        /// <summary>Every refresh shows the rows of the current expanded state.</summary>
        internal override void PreRefresh() => RebuildRows();

        internal override void InvokeMakeItem(ReusableCollectionItem reusableItem)
        {
            reusableItem.Init(MakeItem());
            if (reusableItem is UaTreeRow row) row.toggled = OnRowToggled;
        }

        internal override void InvokeBindItem(ReusableCollectionItem reusableItem, int index)
        {
            if (reusableItem is UaTreeRow row)
            {
                // Rows are pooled by the view and outlive a controller: SetRootItems with another data
                // type replaces the controller and only refreshes, so the bound controller takes the
                // toggle over.
                row.toggled = OnRowToggled;
                if (m_Tree.TryGetRow(index, out var node))
                {
                    row.Indent(node.depth);
                    row.SetToggle(node.children.Count > 0, uaView != null && uaView.IsExpanded(node.id));
                }
            }
            BindItem(reusableItem.bindableElement, index);
            reusableItem.SetSelected(view.selectedIndices.Contains(index));
            reusableItem.rootElement.pseudoStates &= ~PseudoStates.Hover;
            reusableItem.index = index;
        }

        internal override void InvokeDestroyItem(ReusableCollectionItem reusableItem) => DestroyItem(reusableItem.bindableElement);

        void OnRowToggled(UaTreeRow row, bool expanded)
        {
            var v = uaView;
            if (v == null || !m_Tree.TryGetRow(row.index, out var node)) return;
            if (expanded) v.ExpandItem(node.id);
            else v.CollapseItem(node.id);
        }

        // Without callbacks a row is a Label showing its data, as a ListView without callbacks shows its
        // items; one callback without the other is an error, as for ListView.
        protected override VisualElement MakeItem()
        {
            var treeView = view as TreeView;
            if (treeView?.makeItem == null)
            {
                if (treeView?.bindItem != null) throw new NotImplementedException("You must specify makeItem if bindItem is specified.");
                return new Label();
            }
            return treeView.makeItem();
        }

        protected override void BindItem(VisualElement element, int index)
        {
            var treeView = view as TreeView;
            if (treeView?.bindItem == null)
            {
                if (treeView != null && treeView.bindsTemplateWithoutBindItem)
                {
                    // Unbound interim: the template row is shown as instantiated. No data is bound and
                    // no data source is assigned to it.
                    Unbound.Runtime.Stubs.Ignored("TreeView.itemTemplate",
                        "rows are shown unbound: runtime data binding (bindItem omitted with an item template) not implemented");
                    return;
                }
                if (treeView?.makeItem != null) throw new NotImplementedException("You must specify bindItem if makeItem is specified.");
                if (element is Label label)
                {
                    object data = GetItemForIndex(index);
                    label.text = data?.ToString() ?? "null";
                }
                return;
            }
            treeView.bindItem(element, index);
        }

        protected override void UnbindItem(VisualElement element, int index) => (view as TreeView)?.unbindItem?.Invoke(element, index);

        protected override void DestroyItem(VisualElement element) => (view as TreeView)?.destroyItem?.Invoke(element);
    }

    /// <summary>A pooled tree view row: the item element, with the classes of the Manual's USS table,
    /// holding an indent, the expand/collapse toggle and a content container with the element made by
    /// makeItem.</summary>
    internal sealed class UaTreeRow : ReusableCollectionItem
    {
        VisualElement m_Root;
        VisualElement m_Indent;
        Toggle m_Toggle;
        VisualElement m_Content;
        int m_Depth;

        internal Action<UaTreeRow, bool> toggled;

        public override VisualElement rootElement => m_Root;

        internal int depth => m_Depth;
        internal VisualElement indentElement => m_Indent;
        internal Toggle toggle => m_Toggle;
        internal VisualElement contentElement => m_Content;

        // Unbound's choice (unmeasured): the element structure (indent, toggle, content in a row) and its
        // inline flex settings; the theme's selectors give the sizes and looks.
        public override void Init(VisualElement item)
        {
            base.Init(item);
            m_Root = new VisualElement();
            m_Root.AddToClassList(BaseTreeView.itemUssClassName);
            m_Root.style.flexDirection = FlexDirection.Row;
            m_Indent = new VisualElement();
            m_Indent.AddToClassList(BaseTreeView.itemIndentUssClassName);
            m_Toggle = new Toggle();
            m_Toggle.AddToClassList(BaseTreeView.itemToggleUssClassName);
            m_Toggle.RegisterValueChangedCallback(OnToggleChanged);
            m_Toggle.RegisterCallback<GeometryChangedEvent>(_ => ApplyIndent());
            m_Content = new VisualElement();
            m_Content.AddToClassList(BaseTreeView.itemContentContainerUssClassName);
            m_Content.style.flexGrow = 1f;
            m_Root.Add(m_Indent);
            m_Root.Add(m_Toggle);
            m_Root.Add(m_Content);
            if (item != null) m_Content.Add(item);
        }

        void OnToggleChanged(ChangeEvent<bool> evt)
        {
            if (evt.target != m_Toggle) return;
            toggled?.Invoke(this, evt.newValue);
        }

        public void Indent(int depth)
        {
            m_Depth = depth;
            ApplyIndent();
        }

        /// <summary>Unbound's choice (unmeasured), the indent width source: one level is as wide as the
        /// row's toggle with its horizontal margins, as laid out from the theme, so a child's toggle
        /// lines up with its parent's content. Until the toggle has a layout only depth 0 is applied.</summary>
        void ApplyIndent()
        {
            if (m_Indent == null) return;
            if (m_Depth == 0)
            {
                m_Indent.style.width = 0f;
                return;
            }
            var toggleStyle = m_Toggle.resolvedStyle;
            float level = toggleStyle.width + toggleStyle.marginLeft + toggleStyle.marginRight;
            if (float.IsNaN(level) || level <= 0f) return;
            m_Indent.style.width = m_Depth * level;
        }

        /// <summary>Items without children keep the toggle's space but hide it (Unbound's choice,
        /// unmeasured), so rows of the same depth line up.</summary>
        public void SetToggle(bool hasChildren, bool expanded)
        {
            m_Toggle.style.visibility = hasChildren ? Visibility.Visible : Visibility.Hidden;
            m_Toggle.SetValueWithoutNotify(expanded);
        }
    }
}
