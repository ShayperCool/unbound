using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.IMGUI.Controls
{
    /// <summary>An item of a TreeView: its id, depth, name, parent and children.</summary>
    public partial class TreeViewItem : IComparable<TreeViewItem>
    {
        int m_ID;
        int m_Depth;
        string m_DisplayName;
        TreeViewItem m_Parent;
        List<TreeViewItem> m_Children;
        Texture2D m_Icon;

        public TreeViewItem() { }

        public TreeViewItem(int id) { m_ID = id; }

        public TreeViewItem(int id, int depth) { m_ID = id; m_Depth = depth; }

        public TreeViewItem(int id, int depth, string displayName) { m_ID = id; m_Depth = depth; m_DisplayName = displayName; }

        internal TreeViewItem(int id, int depth, TreeViewItem parent, string displayName)
        {
            m_ID = id;
            m_Depth = depth;
            m_Parent = parent;
            m_DisplayName = displayName;
        }

        public virtual int id { get => m_ID; set => m_ID = value; }

        public virtual string displayName { get => m_DisplayName; set => m_DisplayName = value; }

        public virtual int depth { get => m_Depth; set => m_Depth = value; }

        public virtual bool hasChildren => m_Children != null && m_Children.Count > 0;

        public virtual List<TreeViewItem> children { get => m_Children; set => m_Children = value; }

        public virtual TreeViewItem parent { get => m_Parent; set => m_Parent = value; }

        public virtual Texture2D icon { get => m_Icon; set => m_Icon = value; }

        public void AddChild(TreeViewItem child)
        {
            m_Children ??= new List<TreeViewItem>();
            m_Children.Add(child);
            if (child != null) child.m_Parent = this;
        }

        /// <summary>Items order by their display names.</summary>
        public virtual int CompareTo(TreeViewItem other) => string.Compare(displayName, other?.displayName, StringComparison.Ordinal) switch { < 0 => -1, > 0 => 1, _ => 0 };

        public override string ToString() => $"Item: '{displayName}' ({id}), has {(hasChildren ? children.Count : 0)} children, depth {depth}, parent id {(parent != null ? parent.id : -1)}";
    }

    [Flags]
    public enum TreeViewSelectionOptions
    {
        None = 0,
        FireSelectionChanged = 1,
        RevealAndFrame = 2,
    }

    /// <summary>A TreeView's state as windows serialize it: selection, expansion, search, scroll.</summary>
    [Serializable]
    public partial class TreeViewState
    {
        public Vector2 scrollPos;
        [SerializeField] List<int> m_SelectedIDs = new List<int>();
        [SerializeField] int m_LastClickedID;
        [SerializeField] List<int> m_ExpandedIDs = new List<int>();
        [SerializeField] string m_SearchString;

        public TreeViewState() { }

        public List<int> selectedIDs { get => m_SelectedIDs; set => m_SelectedIDs = value; }

        public int lastClickedID { get => m_LastClickedID; set => m_LastClickedID = value; }

        public List<int> expandedIDs { get => m_ExpandedIDs; set => m_ExpandedIDs = value; }

        public string searchString { get => m_SearchString; set => m_SearchString = value; }
    }

    /// <summary>
    /// A tree of items built by <see cref="BuildRoot"/>, its rows (the expanded items, or the items
    /// matching the search) and its selection and expansion, as the Editor keeps them in its
    /// <see cref="TreeViewState"/>. Unbound has no editor windows: drawing (OnGUI) does nothing.
    /// </summary>
    public abstract partial class TreeView
    {
        readonly TreeViewState m_State;
        MultiColumnHeader m_Header;
        TreeViewItem m_Root;
        IList<TreeViewItem> m_Rows;
        bool m_RowsDirty = true;
        bool m_Initialized;
        float m_RowHeight = 16f;
        float m_DepthIndentWidth = 14f;
        float m_BaseIndent = 2f;
        float m_ExtraSpaceBeforeIconAndLabel;

        public TreeView(TreeViewState state) : this(state, null) { }

        public TreeView(TreeViewState state, MultiColumnHeader multiColumnHeader)
        {
            m_State = state ?? throw new ArgumentNullException(nameof(state));
            m_Header = multiColumnHeader;
        }

        protected abstract TreeViewItem BuildRoot();

        public TreeViewState state => m_State;

        public MultiColumnHeader multiColumnHeader { get => m_Header; set => m_Header = value; }

        protected TreeViewItem rootItem => m_Root;

        protected bool isInitialized => m_Initialized;

        public void Reload()
        {
            m_Root = BuildRoot();
            if (m_Root == null) throw new NullReferenceException("BuildRoot should ensure the root item is not null");
            if (m_Root.children == null)
                throw new InvalidOperationException("TreeView: 'rootItem.children == null'. Did you forget to add children? If you intend to only create the list of rows (not the full tree) then you need to override: BuildRows, GetAncestors and GetDescendantsThatHaveChildren.");
            m_Initialized = true;
            m_RowsDirty = true;
            BuildRowsNow();
        }

        void BuildRowsNow()
        {
            m_Rows = BuildRows(m_Root) ?? new List<TreeViewItem>();
            m_RowsDirty = false;
        }

        [Unbound.EditorFaithful("no window draws tree views (as in the batch-mode Editor)")]
        public void Repaint() { }

        /// <summary>The rows: the items under expanded parents, or those matching the search.</summary>
        protected virtual IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            var rows = new List<TreeViewItem>();
            if (root?.children == null) return rows;
            if (hasSearch) Search(root, searchString, rows);
            else AddExpandedRows(root, rows);
            return rows;
        }

        void Search(TreeViewItem item, string search, List<TreeViewItem> rows)
        {
            if (item.children == null) return;
            foreach (var child in item.children)
            {
                if (child == null) continue;
                if (DoesItemMatchSearch(child, search)) rows.Add(child);
                Search(child, search, rows);
            }
        }

        protected void AddExpandedRows(TreeViewItem root, IList<TreeViewItem> rows)
        {
            if (root?.children == null) return;
            foreach (var child in root.children)
            {
                if (child == null) continue;
                rows.Add(child);
                if (child.hasChildren && IsExpanded(child.id)) AddExpandedRows(child, rows);
            }
        }

        public virtual IList<TreeViewItem> GetRows()
        {
            if (!m_Initialized) return null;
            if (m_RowsDirty) BuildRowsNow();
            return m_Rows;
        }

        void RowsChanged()
        {
            m_RowsDirty = true;
        }

        protected IList<TreeViewItem> FindRows(IList<int> ids) => GetRows()?.Where(r => ids.Contains(r.id)).ToList() ?? new List<TreeViewItem>();

        protected TreeViewItem FindItem(int id, TreeViewItem searchFromThisItem)
        {
            if (searchFromThisItem == null) return null;
            if (searchFromThisItem.id == id) return searchFromThisItem;
            if (searchFromThisItem.children == null) return null;
            foreach (var child in searchFromThisItem.children)
            {
                var found = FindItem(id, child);
                if (found != null) return found;
            }
            return null;
        }

        protected int FindRowOfItem(TreeViewItem item) => GetRows()?.IndexOf(item) ?? -1;

        // ---------------------------------------------------------------- expansion

        public void ExpandAll()
        {
            SetExpanded(GetDescendantsThatHaveChildren(m_Root?.id ?? 0));
        }

        public void CollapseAll() => SetExpanded(new List<int>());

        public void SetExpandedRecursive(int id, bool expanded)
        {
            var ids = new HashSet<int>(m_State.expandedIDs);
            foreach (var d in GetDescendantsThatHaveChildren(id))
            {
                if (expanded) ids.Add(d);
                else ids.Remove(d);
            }
            SetExpanded(ids.ToList());
        }

        public bool SetExpanded(int id, bool expanded)
        {
            var ids = m_State.expandedIDs;
            int at = ids.BinarySearch(id);
            if (expanded == at >= 0) return false;
            if (expanded) ids.Insert(~at, id);
            else ids.RemoveAt(at);
            RowsChanged();
            ExpandedStateChanged();
            return true;
        }

        public void SetExpanded(IList<int> ids)
        {
            var sorted = ids.Distinct().ToList();
            sorted.Sort();
            m_State.expandedIDs = sorted;
            RowsChanged();
            ExpandedStateChanged();
        }

        public IList<int> GetExpanded() => m_State.expandedIDs;

        public bool IsExpanded(int id) => m_State.expandedIDs.BinarySearch(id) >= 0;

        // ---------------------------------------------------------------- selection

        public IList<int> GetSelection() => m_State.selectedIDs;

        public void SetSelection(IList<int> selectedIDs) => SetSelection(selectedIDs, TreeViewSelectionOptions.None);

        public void SetSelection(IList<int> selectedIDs, TreeViewSelectionOptions options)
        {
            m_State.selectedIDs = selectedIDs.ToList();
            if ((options & TreeViewSelectionOptions.RevealAndFrame) != 0 && selectedIDs.Count > 0)
            {
                var expanded = new HashSet<int>(m_State.expandedIDs);
                foreach (var id in selectedIDs)
                    foreach (var a in GetAncestors(id)) expanded.Add(a);
                SetExpanded(expanded.ToList());
                m_State.lastClickedID = selectedIDs[selectedIDs.Count - 1];
            }
            if ((options & TreeViewSelectionOptions.FireSelectionChanged) != 0) SelectionChanged(m_State.selectedIDs);
        }

        public bool IsSelected(int id) => m_State.selectedIDs.Contains(id);

        public bool HasSelection() => m_State.selectedIDs.Count > 0;

        [Unbound.EditorFaithful("keyboard focus belongs to a window's GUI: with no window drawn, none has it")]
        public bool HasFocus() => false;

        [Unbound.EditorFaithful("keyboard focus belongs to a window's GUI: with no window drawn, none takes it")]
        public void SetFocus() { }

        /// <summary>Focuses the tree and makes sure an item is selected: the last clicked one is
        /// revealed, else the first row is selected.</summary>
        public void SetFocusAndEnsureSelectedItem()
        {
            SetFocus();
            var rows = GetRows();
            if (rows == null || rows.Count == 0) return;
            foreach (var row in rows)
                if (row.id == m_State.lastClickedID)
                {
                    FrameItem(m_State.lastClickedID);
                    return;
                }
            SetSelection(new[] { rows[0].id }, TreeViewSelectionOptions.FireSelectionChanged | TreeViewSelectionOptions.RevealAndFrame);
        }

        public void SelectAllRows()
        {
            var rows = GetRows();
            if (rows == null) return;
            m_State.selectedIDs = rows.Where(r => CanMultiSelect(r)).Select(r => r.id).ToList();
            SelectionChanged(m_State.selectedIDs);
        }

        public void FrameItem(int id)
        {
            var expanded = new HashSet<int>(m_State.expandedIDs);
            foreach (var a in GetAncestors(id)) expanded.Add(a);
            SetExpanded(expanded.ToList());
        }

        public bool BeginRename(TreeViewItem item) => BeginRename(item, 0f);

        public bool BeginRename(TreeViewItem item, float delay)
        {
            Unbound.Runtime.Stubs.Ignored("TreeView.BeginRename", "shows no rename field (no editor window draws tree views)");
            return false;
        }

        [Unbound.EditorFaithful("no rename field is ever shown, so there is none to end")]
        public void EndRename() { }

        /// <summary>No editor windows: a tree view draws nothing.</summary>
        public virtual void OnGUI(Rect rect) => Unbound.Runtime.Stubs.Ignored("TreeView.OnGUI", "is not drawn (no editor window draws tree views)");

        // ---------------------------------------------------------------- overridables

        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void SelectionChanged(IList<int> selectedIds) { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void SingleClickedItem(int id) { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void DoubleClickedItem(int id) { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void ContextClickedItem(int id) { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void ContextClicked() { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void ExpandedStateChanged() { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void SearchChanged(string newSearch) { }
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void KeyEvent() { }

        /// <summary>The ids above an item, nearest first, the root's last.</summary>
        protected virtual IList<int> GetAncestors(int id)
        {
            var result = new List<int>();
            for (var item = FindItem(id, m_Root)?.parent; item != null; item = item.parent) result.Add(item.id);
            return result;
        }

        /// <summary>The ids of an item and its descendants that have children, depth first.</summary>
        protected virtual IList<int> GetDescendantsThatHaveChildren(int id)
        {
            var result = new List<int>();
            void Walk(TreeViewItem item)
            {
                if (item == null || !item.hasChildren) return;
                result.Add(item.id);
                foreach (var c in item.children) Walk(c);
            }
            Walk(FindItem(id, m_Root));
            return result;
        }

        [Unbound.EditorFaithful("Unity's base implementation")] protected virtual bool CanMultiSelect(TreeViewItem item) => true;
        [Unbound.EditorFaithful("Unity's base implementation")] protected virtual bool CanRename(TreeViewItem item) => false;
        [Unbound.EditorFaithful("Unity's base implementation")] protected virtual bool CanBeParent(TreeViewItem item) => true;
        protected virtual bool CanChangeExpandedState(TreeViewItem item) => item.hasChildren;

        protected virtual bool DoesItemMatchSearch(TreeViewItem item, string search) =>
            item.displayName != null && item.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        protected virtual void BeforeRowsGUI() => Unbound.Runtime.Stubs.Ignored("TreeView.BeforeRowsGUI", "is not drawn (no editor window draws tree views)");
        [Unbound.EditorFaithful("Unity's base hook is empty too")] protected virtual void AfterRowsGUI() { }
        protected virtual void RefreshCustomRowHeights() =>
            Unbound.Runtime.Stubs.Ignored("TreeView.RefreshCustomRowHeights", "is not honoured yet: rows keep the fixed row height");
        protected virtual float GetCustomRowHeight(int row, TreeViewItem item) => m_RowHeight;
        protected virtual void CommandEventHandling() => Unbound.Runtime.Stubs.Ignored("TreeView.CommandEventHandling", "handles no command events (no editor window draws tree views)");

        // ---------------------------------------------------------------- properties

        public bool hasSearch => !string.IsNullOrEmpty(m_State.searchString);

        public string searchString
        {
            get => m_State.searchString;
            set
            {
                if (m_State.searchString == value) return;
                m_State.searchString = value;
                RowsChanged();
                SearchChanged(value);
            }
        }

        protected float rowHeight { get => m_RowHeight; set => m_RowHeight = Mathf.Max(value, 1f); }

        public float totalHeight => (GetRows()?.Count ?? 0) * m_RowHeight;

        protected bool showBorder { get; set; }

        protected bool showAlternatingRowBackgrounds { get; set; }

        protected float depthIndentWidth { get => m_DepthIndentWidth; set => m_DepthIndentWidth = value; }

        protected float baseIndent { get => m_BaseIndent; set => m_BaseIndent = value; }

        protected float extraSpaceBeforeIconAndLabel { get => m_ExtraSpaceBeforeIconAndLabel; set => m_ExtraSpaceBeforeIconAndLabel = value; }

        /// <summary>The foldout's width: its style's (looked up from the current skin, as the Editor's
        /// tree view styles are, the first time).</summary>
        protected float foldoutWidth => TreeViewStyles.Foldout.fixedWidth;

        protected float cellMargin { get; set; }

        protected bool useScrollView { get; set; } = true;

        protected int columnIndexForTreeFoldouts { get; set; }

        protected float customFoldoutYOffset { get; set; }

        protected bool enableItemHovering { get; set; }

        [Unbound.EditorFaithful("hovering only happens while a window draws the tree, which none does")]
        protected TreeViewItem hoveredItem => null;

        [Unbound.EditorFaithful("dragging only happens while a window draws the tree, which none does")]
        protected bool isDragging => false;

        [Unbound.EditorFaithful("scroll bars only show while a window draws the tree, which none does")]
        protected bool showingHorizontalScrollBar => false;

        [Unbound.EditorFaithful("scroll bars only show while a window draws the tree, which none does")]
        protected bool showingVerticalScrollBar => false;

        protected Rect treeViewRect { get; set; }

        public int treeViewControlID { get; set; }

        protected float GetFoldoutIndent(TreeViewItem item) => m_BaseIndent + (hasSearch ? 0 : item.depth * m_DepthIndentWidth);

        protected float GetContentIndent(TreeViewItem item) => GetFoldoutIndent(item) + foldoutWidth;

        protected IList<int> SortItemIDsInRowOrder(IList<int> ids)
        {
            var rows = GetRows();
            if (rows == null) return ids;
            var order = new Dictionary<int, int>();
            for (int i = 0; i < rows.Count; i++) order[rows[i].id] = i;
            return ids.Where(order.ContainsKey).OrderBy(id => order[id]).ToList();
        }

        protected Rect GetRowRect(int row) => new Rect(0, row * m_RowHeight, treeViewRect.width, m_RowHeight);

        protected void CenterRectUsingSingleLineHeight(ref Rect rect)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (rect.height > line)
            {
                rect.y += (rect.height - line) * 0.5f;
                rect.height = line;
            }
        }

        protected void GetFirstAndLastVisibleRows(out int firstRowVisible, out int lastRowVisible)
        {
            int count = GetRows()?.Count ?? 0;
            firstRowVisible = count > 0 ? 0 : -1;
            lastRowVisible = count - 1;
        }

        // ---------------------------------------------------------------- tree helpers

        /// <summary>Parents and children from a list of items in tree order with their depths.</summary>
        protected static void SetupParentsAndChildrenFromDepths(TreeViewItem root, IList<TreeViewItem> rows)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "The root is null");
            if (rows == null) throw new ArgumentNullException(nameof(rows), "The rows list is null");
            root.children = new List<TreeViewItem>();
            var parents = new List<TreeViewItem>();
            foreach (var item in rows)
            {
                if (item == null) continue;
                item.children = null;
                while (parents.Count > 0 && parents[parents.Count - 1].depth >= item.depth) parents.RemoveAt(parents.Count - 1);
                var parent = parents.Count > 0 ? parents[parents.Count - 1] : root;
                parent.AddChild(item);
                parents.Add(item);
            }
        }

        /// <summary>Depths from the parent and child links, under the root's own depth.</summary>
        protected static void SetupDepthsFromParentsAndChildren(TreeViewItem root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "The root is null");
            void Walk(TreeViewItem item)
            {
                if (item.children == null) return;
                foreach (var c in item.children)
                {
                    if (c == null) continue;
                    c.depth = item.depth + 1;
                    c.parent = item;
                    Walk(c);
                }
            }
            Walk(root);
        }

        static readonly List<TreeViewItem> s_CollapsedChildren = new List<TreeViewItem> { null };

        protected static List<TreeViewItem> CreateChildListForCollapsedParent() => new List<TreeViewItem> { null };

        protected static bool IsChildListForACollapsedParent(IList<TreeViewItem> childList) => childList != null && childList.Count == 1 && childList[0] == null;
    }

    /// <summary>The Editor's tree view styles, looked up by name from the current skin when first
    /// needed (outside an editor window the game's skin has none of them: each lookup warns, as the
    /// Editor's does).</summary>
    internal static class TreeViewStyles
    {
        static bool s_Loaded;
        static GUIStyle s_Foldout;

        internal static GUIStyle Foldout
        {
            get
            {
                if (!s_Loaded)
                {
                    s_Loaded = true;
                    s_Foldout = "IN Foldout";
                    GUIStyle unused;
                    unused = "TV Insertion";
                    unused = "TV InsertionRelativeToSibling";
                    unused = "TV Ping";
                    unused = "ToolbarButton";
                    unused = "TV Line";
                    unused = "TV LineBold";
                    unused = "TV Selection";
                }
                return s_Foldout ?? GUIStyle.none;
            }
        }
    }

    /// <summary>The columns of a multi-column header, which are visible and how they sort.</summary>
    public partial class MultiColumnHeaderState
    {
        [SerializeField] Column[] m_Columns;
        [SerializeField] int[] m_VisibleColumns;
        [SerializeField] List<int> m_SortedColumns = new List<int>();
        [SerializeField] int m_MaxNumberOfSortedColumns = 3;

        public MultiColumnHeaderState(Column[] columns)
        {
            if (columns == null) throw new ArgumentException("columns are no allowed to be null", nameof(columns));
            if (columns.Length == 0) throw new ArgumentException("columns array should at least have one column: it is empty", nameof(columns));
            m_Columns = columns;
            m_VisibleColumns = Enumerable.Range(0, columns.Length).ToArray();
        }

        public static bool CanOverwriteSerializedFields(MultiColumnHeaderState source, MultiColumnHeaderState destination) =>
            source != null && destination != null && source.m_Columns != null && destination.m_Columns != null && source.m_Columns.Length == destination.m_Columns.Length;

        public static void OverwriteSerializedFields(MultiColumnHeaderState source, MultiColumnHeaderState destination)
        {
            if (!CanOverwriteSerializedFields(source, destination)) return;
            destination.m_VisibleColumns = source.m_VisibleColumns.ToArray();
            destination.m_SortedColumns = new List<int>(source.m_SortedColumns);
            for (int i = 0; i < source.m_Columns.Length; i++)
            {
                destination.m_Columns[i].width = source.m_Columns[i].width;
                destination.m_Columns[i].sortedAscending = source.m_Columns[i].sortedAscending;
            }
        }

        public int sortedColumnIndex
        {
            get => m_SortedColumns.Count > 0 ? m_SortedColumns[0] : -1;
            set
            {
                int current = sortedColumnIndex;
                if (value == current) return;
                if (value < 0) { m_SortedColumns.Clear(); return; }
                m_SortedColumns.Remove(value);
                m_SortedColumns.Insert(0, value);
                while (m_SortedColumns.Count > m_MaxNumberOfSortedColumns) m_SortedColumns.RemoveAt(m_SortedColumns.Count - 1);
            }
        }

        public int maximumNumberOfSortedColumns
        {
            get => m_MaxNumberOfSortedColumns;
            set
            {
                m_MaxNumberOfSortedColumns = value;
                while (m_SortedColumns.Count > m_MaxNumberOfSortedColumns) m_SortedColumns.RemoveAt(m_SortedColumns.Count - 1);
            }
        }

        public int[] sortedColumns
        {
            get => m_SortedColumns.ToArray();
            set => m_SortedColumns = value == null ? new List<int>() : new List<int>(value);
        }

        public Column[] columns => m_Columns;

        public int[] visibleColumns { get => m_VisibleColumns; set => m_VisibleColumns = value; }

        public float widthOfAllVisibleColumns => m_VisibleColumns.Sum(i => m_Columns[i].width);

        internal void SwapColumns(int columnIndex, int targetColumnIndex)
        {
            (m_Columns[columnIndex], m_Columns[targetColumnIndex]) = (m_Columns[targetColumnIndex], m_Columns[columnIndex]);
        }

        [Serializable]
        public partial class Column
        {
            public float width = 50f;
            public bool sortedAscending;
            public GUIContent headerContent = new GUIContent();
            public string contextMenuText;
            public TextAlignment headerTextAlignment = TextAlignment.Left;
            public TextAlignment sortingArrowAlignment = TextAlignment.Center;
            public float minWidth = 20f;
            public float maxWidth = 1000000f;
            public bool autoResize = true;
            public bool allowToggleVisibility = true;
            public bool canSort = true;
            public int userData;
            internal object userDataObj;

            public Column() { }
        }
    }

    /// <summary>A multi-column header over its state: sorting and visibility, drawn by nothing.</summary>
    public partial class MultiColumnHeader
    {
        MultiColumnHeaderState m_State;
        float m_Height = DefaultGUI.defaultHeight;

        public MultiColumnHeader(MultiColumnHeaderState state)
        {
            m_State = state;
        }

        public float height { get => m_Height; set => m_Height = value; }

        public bool canSort { get; set; } = true;

        [Unbound.EditorFaithful("Unity's value outside the header's OnGUI, which no window runs")]
        protected int currentColumnIndex => -1;

        protected bool allowDraggingColumnsToReorder { get; set; }

        public int sortedColumnIndex
        {
            get => m_State.sortedColumnIndex;
            set
            {
                if (value == m_State.sortedColumnIndex) return;
                m_State.sortedColumnIndex = value;
                OnSortingChanged();
            }
        }

        public MultiColumnHeaderState state { get => m_State; set => m_State = value; }

        public event HeaderCallback sortingChanged;

        public event HeaderCallback visibleColumnsChanged;

        public event Action<int> columnSettingsChanged;

        public event Action<int, int> columnsSwapped;

        public delegate void HeaderCallback(MultiColumnHeader multiColumnHeader);

        public void SetSortingColumns(int[] columnIndices, bool[] sortAscending)
        {
            if (columnIndices == null || sortAscending == null || columnIndices.Length != sortAscending.Length) throw new ArgumentException("Arrays must not be null and have the same length");
            for (int i = 0; i < columnIndices.Length; i++) m_State.columns[columnIndices[i]].sortedAscending = sortAscending[i];
            m_State.sortedColumns = columnIndices;
            OnSortingChanged();
        }

        public void SetSorting(int columnIndex, bool sortAscending)
        {
            bool changed = m_State.columns[columnIndex].sortedAscending != sortAscending;
            m_State.columns[columnIndex].sortedAscending = sortAscending;
            if (m_State.sortedColumnIndex != columnIndex)
            {
                m_State.sortedColumnIndex = columnIndex;
                changed = true;
            }
            if (changed) OnSortingChanged();
        }

        public void SetSortDirection(int columnIndex, bool sortAscending)
        {
            if (m_State.columns[columnIndex].sortedAscending == sortAscending) return;
            m_State.columns[columnIndex].sortedAscending = sortAscending;
            if (m_State.sortedColumnIndex == columnIndex) OnSortingChanged();
        }

        public bool IsSortedAscending(int columnIndex) => m_State.columns[columnIndex].sortedAscending;

        public MultiColumnHeaderState.Column GetColumn(int columnIndex) => m_State.columns[columnIndex];

        public bool IsColumnVisible(int columnIndex) => Array.IndexOf(m_State.visibleColumns, columnIndex) >= 0;

        public int GetVisibleColumnIndex(int columnIndex)
        {
            int at = Array.IndexOf(m_State.visibleColumns, columnIndex);
            if (at < 0) throw new ArgumentException($"Invalid columnIndex: {columnIndex}. The column is not visible");
            return at;
        }

        public Rect GetCellRect(int visibleColumnIndex, Rect rowRect)
        {
            var column = GetColumnRect(visibleColumnIndex);
            return new Rect(rowRect.x + column.x, rowRect.y, column.width, rowRect.height);
        }

        public Rect GetColumnRect(int visibleColumnIndex)
        {
            float x = 0;
            for (int i = 0; i < visibleColumnIndex; i++) x += m_State.columns[m_State.visibleColumns[i]].width;
            return new Rect(x, 0, m_State.columns[m_State.visibleColumns[visibleColumnIndex]].width, m_Height);
        }

        [Unbound.EditorFaithful("Unity only flags the resize for the header's next draw, which no window does")]
        public void ResizeToFit() { }

        public virtual void OnGUI(Rect rect, float xScroll) => Unbound.Runtime.Stubs.Ignored("MultiColumnHeader.OnGUI", "is not drawn (no editor window draws headers)");

        protected virtual void ColumnHeaderClicked(MultiColumnHeaderState.Column column, int columnIndex)
        {
            if (m_State.sortedColumnIndex == columnIndex) column.sortedAscending = !column.sortedAscending;
            else m_State.sortedColumnIndex = columnIndex;
            OnSortingChanged();
        }

        protected virtual void OnSortingChanged() => sortingChanged?.Invoke(this);

        protected virtual void ColumnHeaderGUI(MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex) =>
            Unbound.Runtime.Stubs.Ignored("MultiColumnHeader.ColumnHeaderGUI", "is not drawn (no editor window draws headers)");

        protected void SortingButton(MultiColumnHeaderState.Column column, Rect headerRect, int columnIndex) =>
            Unbound.Runtime.Stubs.Ignored("MultiColumnHeader.SortingButton", "is not drawn (no editor window draws headers)");

        /// <summary>The header's context menu: Resize to Fit, then a visibility toggle per column.</summary>
        protected virtual void AddColumnHeaderContextMenuItems(GenericMenu menu)
        {
            menu.AddItem(EditorGUIUtility.TrTextContent("Resize to Fit"), false, ResizeToFit);
            menu.AddSeparator("");
            for (int i = 0; i < m_State.columns.Length; i++)
            {
                var column = m_State.columns[i];
                var text = !string.IsNullOrEmpty(column.contextMenuText) ? column.contextMenuText : column.headerContent.text;
                if (column.allowToggleVisibility)
                    menu.AddItem(new GUIContent(text), Array.IndexOf(m_State.visibleColumns, i) >= 0, index => ToggleVisibility((int)index), i);
                else
                    menu.AddDisabledItem(new GUIContent(text));
            }
        }

        protected virtual void OnVisibleColumnsChanged() => visibleColumnsChanged?.Invoke(this);

        protected virtual void ToggleVisibility(int columnIndex)
        {
            var visible = m_State.visibleColumns.ToList();
            if (visible.Contains(columnIndex)) visible.Remove(columnIndex);
            else { visible.Add(columnIndex); visible.Sort(); }
            m_State.visibleColumns = visible.ToArray();
            OnVisibleColumnsChanged();
        }

        [Unbound.EditorFaithful("no window draws headers (as in the batch-mode Editor)")]
        public void Repaint() { }

        internal void MoveColumn(int columnIndex, int targetColumnIndex)
        {
            m_State.SwapColumns(columnIndex, targetColumnIndex);
            columnsSwapped?.Invoke(columnIndex, targetColumnIndex);
            columnSettingsChanged?.Invoke(columnIndex);
        }

        [Unbound.EditorFaithful("Unity's constants (measured: tests/projects/editorui MultiColumnHeader height 27)")]
        public static partial class DefaultGUI
        {
            public static float defaultHeight => 27f;
            public static float minimumHeight => 20f;
            public static float columnContentMargin => 3f;
            internal static float labelSpaceFromBottom => 3f;
        }
    }
}
