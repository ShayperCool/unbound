using Unity.Properties;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements.Internal;

// MultiColumnListView, MultiColumnTreeView and their columns. A view holds a header (one header
// column per visible column, with sort indicator, icon and title, and a resize handle after each)
// above its scroll view; each row is a row container with one cell per visible column holding the
// column's made cell (the primary column's cell also holds a tree row's toggle and indent).
// Column widths come from a column layout: fixed widths, percentages of the header width, min and
// max widths, and stretchable columns that share the remaining space (GrowAndFill) or grow only
// (Grow). Sorting by header clicks (Ctrl adds a column, Shift removes one) orders the rows through
// a sorted-to-source index map (ColumnSortingMode.Default) or leaves the order to the game
// (Custom). Documented (Unity 6000.0 Scripting API and Manual "MultiColumnListView",
// "MultiColumnTreeView"): the Column and Columns members, makeCell/bindCell/unbindCell/destroyCell,
// sortColumnDescriptions, sortingMode, columnSortingChanged.
// Matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (columns and column-tree
// sections: header and row structure, widths, cell callbacks in order, hiding and showing a
// column, a new width, adding and removing columns, clicks, tree expansion). Not implemented:
// reordering columns by dragging their header, the header's context menu (resize to fit, column
// visibility), view-data persistence of the header state.
namespace UnityEngine.UIElements
{
    public enum SortDirection
    {
        Ascending,
        Descending,
    }

    public enum ColumnSortingMode
    {
        None,
        Default,
        Custom,
    }

    internal enum ColumnDataType
    {
        Name,
        Title,
        Icon,
        Visibility,
        Width,
        MaxWidth,
        MinWidth,
        Stretchable,
        Sortable,
        Optional,
        Resizable,
        HeaderTemplate,
        CellTemplate,
    }

    internal enum ColumnsDataType
    {
        PrimaryColumn,
        StretchMode,
        Reorderable,
        Resizable,
        ResizePreview,
    }

    /// <summary>A column of a multi-column view: header (title, icon or template), cells
    /// (template or callbacks), width limits and flags.</summary>
    public partial class Column
    {
        internal const string k_HeaderTemplateAttributeName = "header-template";
        internal const string k_CellTemplateAttributeName = "cell-template";
        // Default minimum width of a column, in points.
        internal const float kDefaultMinWidth = 35f;
        // Default maximum width: large enough to never bind.
        const float k_DefaultMaxWidth = 8388608f;

        string m_Name;
        string m_Title;
        Background m_Icon;
        bool m_Visible = true;
        Length m_Width = 0f;
        Length m_MinWidth = kDefaultMinWidth;
        Length m_MaxWidth = k_DefaultMaxWidth;
        float m_DesiredWidth = float.NaN;
        bool m_Stretchable;
        bool m_Sortable = true;
        bool m_Optional = true;
        bool m_Resizable = true;
        VisualTreeAsset m_HeaderTemplate;
        VisualTreeAsset m_CellTemplate;
        Func<VisualElement> m_MakeHeader;
        Action<VisualElement> m_BindHeader;
        Action<VisualElement> m_UnbindHeader;
        Action<VisualElement> m_DestroyHeader;
        Func<VisualElement> m_MakeCell;
        Action<VisualElement, int> m_BindCell;
        Action<VisualElement, int> m_UnbindCell;

        [CreateProperty]
        public string name { get => m_Name; set { if (m_Name == value) return; m_Name = value; NotifyChange(ColumnDataType.Name); } }

        [CreateProperty]
        public string title { get => m_Title; set { if (m_Title == value) return; m_Title = value; NotifyChange(ColumnDataType.Title); } }

        [CreateProperty]
        public Background icon { get => m_Icon; set { if (m_Icon == value) return; m_Icon = value; NotifyChange(ColumnDataType.Icon); } }

        public Comparison<int> comparison { get; set; }

        internal int index => collection?.IndexOf(this) ?? -1;

        internal int displayIndex => collection != null ? collection.displayColumns.IndexOf(this) : -1;

        internal int visibleIndex => collection != null ? collection.visibleColumns.IndexOf(this) : -1;

        [CreateProperty]
        public bool visible { get => m_Visible; set { if (m_Visible == value) return; m_Visible = value; NotifyChange(ColumnDataType.Visibility); } }

        /// <summary>The width (points or a percentage of the header width); setting it forgets the
        /// laid-out width.</summary>
        [CreateProperty]
        public Length width
        {
            get => m_Width;
            set
            {
                if (m_Width == value) return;
                m_Width = value;
                desiredWidth = float.NaN;
                NotifyChange(ColumnDataType.Width);
            }
        }

        [CreateProperty]
        public Length minWidth { get => m_MinWidth; set { if (m_MinWidth == value) return; m_MinWidth = value; NotifyChange(ColumnDataType.MinWidth); } }

        [CreateProperty]
        public Length maxWidth { get => m_MaxWidth; set { if (m_MaxWidth == value) return; m_MaxWidth = value; NotifyChange(ColumnDataType.MaxWidth); } }

        /// <summary>The laid-out width (NaN until laid out).</summary>
        internal float desiredWidth
        {
            get => m_DesiredWidth;
            set
            {
                if (m_DesiredWidth == value) return;
                m_DesiredWidth = value;
                resized?.Invoke(this);
            }
        }

        [CreateProperty]
        public bool sortable { get => m_Sortable; set { if (m_Sortable == value) return; m_Sortable = value; NotifyChange(ColumnDataType.Sortable); } }

        [CreateProperty]
        public bool stretchable { get => m_Stretchable; set { if (m_Stretchable == value) return; m_Stretchable = value; NotifyChange(ColumnDataType.Stretchable); } }

        [CreateProperty]
        public bool optional { get => m_Optional; set { if (m_Optional == value) return; m_Optional = value; NotifyChange(ColumnDataType.Optional); } }

        [CreateProperty]
        public bool resizable { get => m_Resizable; set { if (m_Resizable == value) return; m_Resizable = value; NotifyChange(ColumnDataType.Resizable); } }

        public string bindingPath { get; set; }

        [CreateProperty]
        public VisualTreeAsset headerTemplate { get => m_HeaderTemplate; set { if (m_HeaderTemplate == value) return; m_HeaderTemplate = value; NotifyChange(ColumnDataType.HeaderTemplate); } }

        [CreateProperty]
        public VisualTreeAsset cellTemplate { get => m_CellTemplate; set { if (m_CellTemplate == value) return; m_CellTemplate = value; NotifyChange(ColumnDataType.CellTemplate); } }

        public Func<VisualElement> makeHeader { get => m_MakeHeader; set { if (m_MakeHeader == value) return; m_MakeHeader = value; NotifyChange(ColumnDataType.HeaderTemplate); } }

        public Action<VisualElement> bindHeader { get => m_BindHeader; set { if (m_BindHeader == value) return; m_BindHeader = value; NotifyChange(ColumnDataType.HeaderTemplate); } }

        public Action<VisualElement> unbindHeader { get => m_UnbindHeader; set { if (m_UnbindHeader == value) return; m_UnbindHeader = value; NotifyChange(ColumnDataType.HeaderTemplate); } }

        public Action<VisualElement> destroyHeader { get => m_DestroyHeader; set { if (m_DestroyHeader == value) return; m_DestroyHeader = value; NotifyChange(ColumnDataType.HeaderTemplate); } }

        public Func<VisualElement> makeCell { get => m_MakeCell; set { if (m_MakeCell == value) return; m_MakeCell = value; NotifyChange(ColumnDataType.CellTemplate); } }

        public Action<VisualElement, int> bindCell { get => m_BindCell; set { if (m_BindCell == value) return; m_BindCell = value; NotifyChange(ColumnDataType.CellTemplate); } }

        public Action<VisualElement, int> unbindCell { get => m_UnbindCell; set { if (m_UnbindCell == value) return; m_UnbindCell = value; NotifyChange(ColumnDataType.CellTemplate); } }

        public Action<VisualElement> destroyCell { get; set; }

        public Columns collection { get; internal set; }

        internal event Action<Column, ColumnDataType> changed;

        internal event Action<Column> resized;

        void NotifyChange(ColumnDataType type) => changed?.Invoke(this, type);

        internal float GetWidth(float layoutWidth) => width.unit == LengthUnit.Pixel ? width.value : width.value * layoutWidth / 100f;

        internal float GetMaxWidth(float layoutWidth) => maxWidth.unit == LengthUnit.Pixel ? maxWidth.value : maxWidth.value * layoutWidth / 100f;

        internal float GetMinWidth(float layoutWidth) => minWidth.unit == LengthUnit.Pixel ? minWidth.value : minWidth.value * layoutWidth / 100f;
    }

    /// <summary>The columns of a multi-column view, in source order, with a display order (header
    /// reordering) and the visible ones in display order.</summary>
    public partial class Columns : ICollection<Column>
    {
        public enum StretchMode
        {
            Grow,
            GrowAndFill,
        }

        readonly List<Column> m_Columns = new List<Column>();
        List<Column> m_DisplayColumns;
        readonly List<Column> m_VisibleColumns = new List<Column>();
        bool m_VisibleColumnsDirty = true;
        StretchMode m_StretchMode = StretchMode.GrowAndFill;
        bool m_Reorderable = true;
        bool m_Resizable = true;
        bool m_ResizePreview;
        string m_PrimaryColumnName;

        internal IList<Column> columns => m_Columns;

        [CreateProperty]
        public string primaryColumnName { get => m_PrimaryColumnName; set { if (m_PrimaryColumnName == value) return; m_PrimaryColumnName = value; NotifyChange(ColumnsDataType.PrimaryColumn); } }

        [CreateProperty]
        public bool reorderable { get => m_Reorderable; set { if (m_Reorderable == value) return; m_Reorderable = value; NotifyChange(ColumnsDataType.Reorderable); } }

        [CreateProperty]
        public bool resizable { get => m_Resizable; set { if (m_Resizable == value) return; m_Resizable = value; NotifyChange(ColumnsDataType.Resizable); } }

        [CreateProperty]
        public bool resizePreview { get => m_ResizePreview; set { if (m_ResizePreview == value) return; m_ResizePreview = value; NotifyChange(ColumnsDataType.ResizePreview); } }

        internal IEnumerable<Column> displayList => displayColumns;

        internal IEnumerable<Column> visibleList => visibleColumns;

        internal List<Column> displayColumns
        {
            get
            {
                m_DisplayColumns ??= new List<Column>(m_Columns);
                return m_DisplayColumns;
            }
        }

        internal List<Column> visibleColumns
        {
            get
            {
                if (m_VisibleColumnsDirty)
                {
                    m_VisibleColumns.Clear();
                    foreach (var column in displayColumns)
                        if (column.visible) m_VisibleColumns.Add(column);
                    m_VisibleColumnsDirty = false;
                }
                return m_VisibleColumns;
            }
        }

        [CreateProperty]
        public StretchMode stretchMode { get => m_StretchMode; set { if (m_StretchMode == value) return; m_StretchMode = value; NotifyChange(ColumnsDataType.StretchMode); } }

        public int Count => m_Columns.Count;

        [Unbound.EditorFaithful("Unity's: the collection is always writable")]
        public bool IsReadOnly => false;

        public Column this[int index] => m_Columns[index];

        public Column this[string name]
        {
            get
            {
                foreach (var column in m_Columns)
                    if (column.name == name) return column;
                return null;
            }
        }

        internal event Action<ColumnsDataType> changed;
        internal event Action<Column, int> columnAdded;
        internal event Action<Column> columnRemoved;
        internal event Action<Column, ColumnDataType> columnChanged;
        internal event Action<Column> columnResized;
        internal event Action<Column, int, int> columnReordered;

        /// <summary>The primary column (which holds a tree's toggle): the one named
        /// primaryColumnName, else the first visible one.</summary>
        public bool IsPrimary(Column column) =>
            primaryColumnName == column.name || (string.IsNullOrEmpty(primaryColumnName) && column.visibleIndex == 0);

        public IEnumerator<Column> GetEnumerator() => m_Columns.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(Column item) => Insert(m_Columns.Count, item);

        public void Clear()
        {
            while (m_Columns.Count > 0) Remove(m_Columns[m_Columns.Count - 1]);
        }

        public bool Contains(Column item) => m_Columns.Contains(item);

        public bool Contains(string name) => this[name] != null;

        public void CopyTo(Column[] array, int arrayIndex) => m_Columns.CopyTo(array, arrayIndex);

        public bool Remove(Column column)
        {
            if (column == null) throw new ArgumentException("Cannot remove null column");
            if (!m_Columns.Remove(column)) return false;
            m_DisplayColumns?.Remove(column);
            DirtyVisibleColumns();
            column.collection = null;
            column.changed -= OnColumnChanged;
            column.resized -= OnColumnResized;
            columnRemoved?.Invoke(column);
            return true;
        }

        void OnColumnChanged(Column column, ColumnDataType type)
        {
            if (type == ColumnDataType.Visibility) DirtyVisibleColumns();
            columnChanged?.Invoke(column, type);
        }

        void OnColumnResized(Column column) => columnResized?.Invoke(column);

        public int IndexOf(Column column) => m_Columns.IndexOf(column);

        public void Insert(int index, Column column)
        {
            if (column == null) throw new ArgumentException("Cannot insert null column");
            if (column.collection == this) throw new ArgumentException("Already contains this column");
            column.collection?.Remove(column);
            m_Columns.Insert(index, column);
            m_DisplayColumns?.Insert(index, column);
            DirtyVisibleColumns();
            column.collection = this;
            column.changed += OnColumnChanged;
            column.resized += OnColumnResized;
            columnAdded?.Invoke(column, index);
        }

        public void RemoveAt(int index) => Remove(m_Columns[index]);

        /// <summary>Moves a column in the display order.</summary>
        public void ReorderDisplay(int from, int to)
        {
            var column = displayColumns[from];
            displayColumns.RemoveAt(from);
            displayColumns.Insert(to, column);
            DirtyVisibleColumns();
            columnReordered?.Invoke(column, from, to);
        }

        void DirtyVisibleColumns() => m_VisibleColumnsDirty = true;

        void NotifyChange(ColumnsDataType type) => changed?.Invoke(type);
    }

    /// <summary>A column to sort by (by index or name) and its direction.</summary>
    public partial class SortColumnDescription
    {
        int m_ColumnIndex = -1;
        string m_ColumnName;
        SortDirection m_SortDirection;

        [CreateProperty]
        public string columnName { get => m_ColumnName; set { if (m_ColumnName == value) return; m_ColumnName = value; changed?.Invoke(this); } }

        [CreateProperty]
        public int columnIndex { get => m_ColumnIndex; set { if (m_ColumnIndex == value) return; m_ColumnIndex = value; changed?.Invoke(this); } }

        public Column column { get; internal set; }

        [CreateProperty]
        public SortDirection direction { get => m_SortDirection; set { if (m_SortDirection == value) return; m_SortDirection = value; changed?.Invoke(this); } }

        internal event Action<SortColumnDescription> changed;

        public SortColumnDescription() { }

        public SortColumnDescription(int columnIndex, SortDirection direction)
        {
            this.columnIndex = columnIndex;
            this.direction = direction;
        }

        public SortColumnDescription(string columnName, SortDirection direction)
        {
            this.columnName = columnName;
            this.direction = direction;
        }
    }

    public partial class SortColumnDescriptions : ICollection<SortColumnDescription>
    {
        readonly List<SortColumnDescription> m_Descriptions = new List<SortColumnDescription>();

        public int Count => m_Descriptions.Count;

        [Unbound.EditorFaithful("Unity's: the collection is always writable")]
        public bool IsReadOnly => false;

        public SortColumnDescription this[int index] => m_Descriptions[index];

        internal event Action changed;

        public IEnumerator<SortColumnDescription> GetEnumerator() => m_Descriptions.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(SortColumnDescription item) => Insert(m_Descriptions.Count, item);

        public void Clear()
        {
            while (m_Descriptions.Count > 0) Remove(m_Descriptions[0]);
        }

        public bool Contains(SortColumnDescription item) => m_Descriptions.Contains(item);

        public void CopyTo(SortColumnDescription[] array, int arrayIndex) => m_Descriptions.CopyTo(array, arrayIndex);

        public bool Remove(SortColumnDescription desc)
        {
            if (desc == null) throw new ArgumentException("Cannot remove null description");
            if (!m_Descriptions.Remove(desc)) return false;
            desc.column = null;
            desc.changed -= OnDescriptionChanged;
            changed?.Invoke();
            return true;
        }

        void OnDescriptionChanged(SortColumnDescription desc) => changed?.Invoke();

        public int IndexOf(SortColumnDescription desc) => m_Descriptions.IndexOf(desc);

        public void Insert(int index, SortColumnDescription desc)
        {
            if (desc == null) throw new ArgumentException("Cannot insert null description");
            if (Contains(desc)) throw new ArgumentException("Already contains this description");
            m_Descriptions.Insert(index, desc);
            desc.changed += OnDescriptionChanged;
            changed?.Invoke();
        }

        public void RemoveAt(int index) => Remove(m_Descriptions[index]);
    }

    /// <summary>Widths of the visible columns for a header width: fixed and percentage columns take
    /// their width within their limits, stretchable ones share what is left; a change of header
    /// width is distributed over the stretchable columns, then the others, within their limits.</summary>
    internal partial class ColumnLayout : IDisposable
    {
        readonly List<Column> m_StretchableColumns = new List<Column>();
        readonly List<Column> m_FixedColumns = new List<Column>();
        readonly List<Column> m_RelativeWidthColumns = new List<Column>();
        readonly List<Column> m_MixedWidthColumns = new List<Column>();
        readonly Columns m_Columns;
        float m_ColumnsWidth;
        bool m_ColumnsWidthDirty = true;
        float m_MaxColumnsWidth;
        float m_MinColumnsWidth;
        bool m_IsDirty;
        float m_PreviousWidth = float.NaN;
        float m_LayoutWidth = float.NaN;
        bool m_DragResizeInPreviewMode;
        bool m_DragResizing;
        float m_DragStartPos;
        float m_DragLastPos;
        float m_DragInitialColumnWidth;
        readonly List<Column> m_DragStretchableColumns = new List<Column>();
        readonly List<Column> m_DragRelativeColumns = new List<Column>();
        readonly List<Column> m_DragFixedColumns = new List<Column>();
        Dictionary<Column, float> m_PreviewDesiredWidths;
        // Scratch lists for one layout pass.
        readonly List<Column> m_NewStretchable = new List<Column>();
        readonly List<Column> m_SizedStretchable = new List<Column>();
        readonly List<Column> m_ScratchStretchable = new List<Column>();
        readonly List<Column> m_ScratchFixed = new List<Column>();
        readonly List<Column> m_ScratchRelative = new List<Column>();
        readonly Comparison<Column> m_ByMaxWidth;
        readonly Comparison<Column> m_ByMinWidthDescending;

        public Columns columns => m_Columns;

        public bool isDirty => m_IsDirty;

        /// <summary>The laid-out widths of the visible columns, summed.</summary>
        public float columnsWidth
        {
            get
            {
                if (!m_ColumnsWidthDirty) return m_ColumnsWidth;
                m_ColumnsWidth = 0f;
                foreach (var column in m_Columns.visibleColumns)
                    if (!float.IsNaN(column.desiredWidth)) m_ColumnsWidth += column.desiredWidth;
                m_ColumnsWidthDirty = false;
                return m_ColumnsWidth;
            }
        }

        public float layoutWidth => m_LayoutWidth;

        public float minColumnsWidth => m_MinColumnsWidth;

        public float maxColumnsWidth => m_MaxColumnsWidth;

        public bool hasStretchableColumns => m_StretchableColumns.Count > 0;

        public bool hasRelativeWidthColumns => m_RelativeWidthColumns.Count > 0 || m_MixedWidthColumns.Count > 0;

        public event Action layoutRequested;

        public ColumnLayout(Columns columns)
        {
            m_Columns = columns;
            m_ByMaxWidth = (a, b) => a.GetMaxWidth(m_LayoutWidth).CompareTo(b.GetMaxWidth(m_LayoutWidth));
            m_ByMinWidthDescending = (a, b) => b.GetMinWidth(m_LayoutWidth).CompareTo(a.GetMinWidth(m_LayoutWidth));
            for (int i = 0; i < columns.Count; i++) OnColumnAdded(columns[i], i);
            columns.columnAdded += OnColumnAdded;
            columns.columnRemoved += OnColumnRemoved;
            columns.columnReordered += OnColumnReordered;
        }

        /// <summary>Detaches the layout from its columns (a header that is disposed no longer lays
        /// them out).</summary>
        public void Dispose()
        {
            m_Columns.columnAdded -= OnColumnAdded;
            m_Columns.columnRemoved -= OnColumnRemoved;
            m_Columns.columnReordered -= OnColumnReordered;
            for (int i = 0; i < m_Columns.Count; i++)
            {
                m_Columns[i].changed -= OnColumnChanged;
                m_Columns[i].resized -= OnColumnResized;
            }
            layoutRequested = null;
        }

        void OnColumnReordered(Column column, int from, int to) => Dirty();

        public void Dirty()
        {
            if (m_IsDirty) return;
            m_IsDirty = true;
            ClearCache();
            layoutRequested?.Invoke();
        }

        void OnColumnAdded(Column column, int index)
        {
            column.changed += OnColumnChanged;
            column.resized += OnColumnResized;
            Dirty();
        }

        void OnColumnRemoved(Column column)
        {
            column.changed -= OnColumnChanged;
            column.resized -= OnColumnResized;
            Dirty();
        }

        // Visibility, width limits, stretchability and the header/cell templates relayout.
        static bool RequiresLayoutUpdate(ColumnDataType type) =>
            (type >= ColumnDataType.Visibility && type <= ColumnDataType.Stretchable) || type == ColumnDataType.HeaderTemplate || type == ColumnDataType.CellTemplate;

        void OnColumnChanged(Column column, ColumnDataType type)
        {
            if (m_DragResizing || !RequiresLayoutUpdate(type)) return;
            Dirty();
            if (m_Columns.stretchMode != Columns.StretchMode.Grow || type != ColumnDataType.Visibility || !column.visible
                || column.width.unit == LengthUnit.Percent || float.IsNaN(m_LayoutWidth))
                return;
            // A column shown again in Grow mode takes its room from the stretchable columns.
            float others = columnsWidth - (!float.IsNaN(column.desiredWidth) ? column.desiredWidth : 0f);
            if (others > layoutWidth) return;
            bool anyStretchable = false;
            for (int i = 0; i < m_Columns.Count; i++)
            {
                var other = m_Columns[i];
                if (other.visible && other.stretchable && other != column)
                {
                    anyStretchable = true;
                    break;
                }
            }
            if (anyStretchable) MakeRoomForColumn(column);
        }

        void MakeRoomForColumn(Column column)
        {
            UpdateCache();
            float width = column.desiredWidth;
            if (float.IsNaN(width)) width = column.GetWidth(m_LayoutWidth);
            width = Mathf.Clamp(width, column.GetMinWidth(m_LayoutWidth), column.GetMaxWidth(m_LayoutWidth));
            float others = columnsWidth - (!float.IsNaN(column.desiredWidth) ? column.desiredWidth : 0f);
            float delta = width - (m_LayoutWidth - others);
            if (delta < 0f) return;
            m_ScratchStretchable.Clear();
            m_ScratchFixed.Clear();
            m_ScratchStretchable.AddRange(m_StretchableColumns);
            if (column.stretchable) m_ScratchStretchable.Remove(column);
            StretchResizeColumns(m_ScratchStretchable, m_ScratchFixed, m_ScratchFixed, ref delta, false, false);
        }

        void OnColumnResized(Column column) => m_ColumnsWidthDirty = true;

        static bool IsClamped(float value, float min, float max) => value >= min && value <= max;

        public void DoLayout(float width)
        {
            m_LayoutWidth = width;
            if (m_IsDirty) UpdateCache();
            if (hasRelativeWidthColumns) UpdateMinAndMaxColumnsWidth();
            float fixedWidth = 0f;
            float percentGrowth = 0f;
            float sizedStretchableWidth = 0f;
            m_NewStretchable.Clear();
            m_SizedStretchable.Clear();
            for (int c = 0; c < m_Columns.Count; c++)
            {
                var column = m_Columns[c];
                if (!column.visible) continue;
                float min = column.GetMinWidth(m_LayoutWidth);
                float max = column.GetMaxWidth(m_LayoutWidth);
                float preferred = column.GetWidth(m_LayoutWidth);
                bool fills = m_Columns.stretchMode == Columns.StretchMode.GrowAndFill && column.stretchable;
                if (float.IsNaN(column.desiredWidth))
                {
                    if (fills)
                    {
                        m_NewStretchable.Add(column);
                        continue;
                    }
                    column.desiredWidth = Mathf.Clamp(preferred, min, max);
                }
                else
                {
                    if (fills)
                    {
                        m_SizedStretchable.Add(column);
                        sizedStretchableWidth += GetDesiredWidth(column);
                    }
                    if (!IsClamped(column.desiredWidth, min, max)) column.desiredWidth = Mathf.Clamp(preferred, min, max);
                    if (m_Columns.stretchMode == Columns.StretchMode.Grow && column.width.unit == LengthUnit.Percent)
                    {
                        float before = column.desiredWidth;
                        column.desiredWidth = Mathf.Clamp(preferred, min, max);
                        percentGrowth += column.desiredWidth - before;
                    }
                }
                if (!column.stretchable) fixedWidth += column.desiredWidth;
            }
            if (m_NewStretchable.Count > 0)
            {
                // New stretchable columns share the room left by the others, smallest maximum first;
                // already sized ones keep their proportions of what remains.
                float remaining = Math.Max(0f, width - fixedWidth);
                int count = m_StretchableColumns.Count;
                m_NewStretchable.Sort(m_ByMaxWidth);
                foreach (var column in m_NewStretchable)
                {
                    column.desiredWidth = Mathf.Clamp(remaining / count, column.GetMinWidth(m_LayoutWidth), column.GetMaxWidth(m_LayoutWidth));
                    remaining = Math.Max(0f, remaining - column.desiredWidth);
                    count--;
                }
                m_SizedStretchable.Sort(m_ByMaxWidth);
                foreach (var column in m_SizedStretchable)
                {
                    float current = GetDesiredWidth(column);
                    // Columns all laid out at zero width share what remains equally (Unbound's choice for
                    // this degenerate case; not measured).
                    float share = sizedStretchableWidth > 0f ? remaining * (current / sizedStretchableWidth) : remaining / count;
                    column.desiredWidth = Mathf.Clamp(share, column.GetMinWidth(m_LayoutWidth), column.GetMaxWidth(m_LayoutWidth));
                    remaining = Math.Max(0f, remaining - column.desiredWidth);
                    sizedStretchableWidth -= current;
                    count--;
                }
            }
            if (hasStretchableColumns || (hasRelativeWidthColumns && m_Columns.stretchMode == Columns.StretchMode.GrowAndFill))
            {
                float delta = 0f;
                if (m_Columns.stretchMode == Columns.StretchMode.Grow)
                {
                    if (!float.IsNaN(m_PreviousWidth))
                    {
                        if (m_PreviousWidth < width)
                            delta = width > columnsWidth ? Math.Max(m_PreviousWidth, columnsWidth) - width + percentGrowth : 0f;
                        else if (width < columnsWidth - 0.5f && m_PreviousWidth < columnsWidth - 0.5f)
                            delta = 0f;
                        else if (width < columnsWidth - 0.5f && m_PreviousWidth > columnsWidth + 0.5f)
                            delta = columnsWidth - width + percentGrowth;
                        else
                            delta = m_PreviousWidth - width + percentGrowth;
                    }
                }
                else
                {
                    delta = columnsWidth - Mathf.Clamp(width, minColumnsWidth, maxColumnsWidth);
                }
                if (delta != 0f)
                {
                    m_ScratchStretchable.Clear();
                    m_ScratchFixed.Clear();
                    m_ScratchRelative.Clear();
                    m_ScratchStretchable.AddRange(m_StretchableColumns);
                    m_ScratchFixed.AddRange(m_FixedColumns);
                    m_ScratchRelative.AddRange(m_RelativeWidthColumns);
                    StretchResizeColumns(m_ScratchStretchable, m_ScratchFixed, m_ScratchRelative, ref delta, false, false);
                }
            }
            m_PreviousWidth = width;
            m_IsDirty = false;
        }

        /// <summary>Takes <paramref name="delta"/> (positive: too wide) from or gives it to the
        /// columns: back to their preferred widths first, then the stretchable ones proportionally,
        /// then (fitting or dragging) the others to their limits.</summary>
        public void StretchResizeColumns(List<Column> stretchableColumns, List<Column> fixedColumns, List<Column> relativeWidthColumns, ref float delta, bool resizeToFit, bool dragResize)
        {
            if (stretchableColumns.Count == 0 && relativeWidthColumns.Count == 0 && fixedColumns.Count == 0) return;
            if (delta > 0f) DistributeOverflow(stretchableColumns, fixedColumns, relativeWidthColumns, ref delta, resizeToFit, dragResize);
            else DistributeExcess(stretchableColumns, fixedColumns, relativeWidthColumns, ref delta, resizeToFit, dragResize);
        }

        void DistributeOverflow(List<Column> stretchable, List<Column> fixedColumns, List<Column> relative, ref float delta, bool resizeToFit, bool dragResize)
        {
            float left = Math.Abs(delta);
            if (!resizeToFit && !dragResize)
            {
                left = RecomputeToDesiredWidth(fixedColumns, left, true, true);
                left = RecomputeToDesiredWidth(relative, left, true, true);
            }
            left = RecomputeToMinWidthProportionally(stretchable, left, !(resizeToFit | dragResize));
            if (resizeToFit)
            {
                left = RecomputeToMinWidthProportionally(relative, left);
                left = RecomputeToMinWidthProportionally(fixedColumns, left);
                left = RecomputeToMinWidth(relative, left);
                left = RecomputeToMinWidth(fixedColumns, left);
            }
            else if (dragResize || left > 0f)
            {
                left = RecomputeToMinWidth(relative, left, true);
                left = RecomputeToMinWidth(fixedColumns, left, true);
            }
            delta = Math.Max(0f, delta - left);
        }

        void DistributeExcess(List<Column> stretchable, List<Column> fixedColumns, List<Column> relative, ref float delta, bool resizeToFit, bool dragResize)
        {
            float left = Math.Abs(delta);
            if (!resizeToFit)
            {
                left = RecomputeToDesiredWidth(fixedColumns, left, true, false);
                left = RecomputeToDesiredWidth(relative, left, true, false);
            }
            left = RecomputeToMaxWidthProportionally(stretchable, left, !(resizeToFit | dragResize));
            if (resizeToFit)
            {
                left = RecomputeToMaxWidthProportionally(relative, left);
                left = RecomputeToMaxWidthProportionally(fixedColumns, left);
                left = RecomputeToMaxWidth(relative, left);
                left = RecomputeToMaxWidth(fixedColumns, left);
            }
            delta += left;
        }

        float RecomputeToMaxWidthProportionally(List<Column> list, float left, bool setDesiredWidthOnly = false)
        {
            if (left <= 0f) return left;
            list.Sort(m_ByMaxWidth);
            float total = 0f;
            foreach (var column in list) total += GetDesiredWidth(column);
            for (int i = 0; i < list.Count; i++)
            {
                var column = list[i];
                float current = GetDesiredWidth(column);
                float share = left * (current / total);
                float grow = 0f;
                float max = column.GetMaxWidth(m_LayoutWidth);
                if (current < max) grow = Math.Min(share, max - current);
                if (grow > 0f) ResizeColumn(column, current + grow, setDesiredWidthOnly);
                total -= current;
                left -= grow;
                if (left <= 0f) break;
            }
            return left;
        }

        float RecomputeToMinWidthProportionally(List<Column> list, float left, bool setDesiredWidthOnly = false)
        {
            if (left <= 0f) return left;
            list.Sort(m_ByMinWidthDescending);
            float total = 0f;
            foreach (var column in list) total += GetDesiredWidth(column);
            for (int i = 0; i < list.Count; i++)
            {
                var column = list[i];
                float current = GetDesiredWidth(column);
                float share = left * (current / total);
                float shrink = 0f;
                float min = column.GetMinWidth(m_LayoutWidth);
                if (current > min) shrink = Math.Min(share, current - min);
                if (shrink > 0f) ResizeColumn(column, current - shrink, setDesiredWidthOnly);
                total -= current;
                left -= shrink;
                if (left <= 0f) break;
            }
            return left;
        }

        float RecomputeToDesiredWidth(List<Column> list, float left, bool setDesiredWidthOnly, bool distributeOverflow)
        {
            if (distributeOverflow)
            {
                for (int i = list.Count - 1; i >= 0 && left > 0f; i--) left = RecomputeToDesiredWidth(list[i], left, setDesiredWidthOnly, true);
            }
            else
            {
                for (int i = 0; i < list.Count && left > 0f; i++) left = RecomputeToDesiredWidth(list[i], left, setDesiredWidthOnly, false);
            }
            return left;
        }

        float RecomputeToDesiredWidth(Column column, float left, bool setDesiredWidthOnly, bool distributeOverflow)
        {
            float change = 0f;
            float preferred = Mathf.Clamp(column.GetWidth(m_LayoutWidth), column.GetMinWidth(m_LayoutWidth), column.GetMaxWidth(m_LayoutWidth));
            float current = GetDesiredWidth(column);
            if (current > preferred && distributeOverflow) change = Math.Min(left, Math.Abs(current - preferred));
            if (current < preferred && !distributeOverflow) change = Math.Min(left, Math.Abs(preferred - current));
            if (change > 0f) ResizeColumn(column, distributeOverflow ? current - change : current + change, setDesiredWidthOnly);
            return left - change;
        }

        float RecomputeToMinWidth(List<Column> list, float left, bool setDesiredWidthOnly = false)
        {
            if (left <= 0f) return left;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var column = list[i];
                float current = GetDesiredWidth(column);
                float min = column.GetMinWidth(m_LayoutWidth);
                float shrink = current > min ? Math.Min(left, current - min) : 0f;
                if (shrink > 0f) ResizeColumn(column, current - shrink, setDesiredWidthOnly);
                left -= shrink;
                if (left <= 0f) break;
            }
            return left;
        }

        float RecomputeToMaxWidth(List<Column> list, float left, bool setDesiredWidthOnly = false)
        {
            if (left <= 0f) return left;
            for (int i = 0; i < list.Count; i++)
            {
                var column = list[i];
                float current = GetDesiredWidth(column);
                float max = column.GetMaxWidth(m_LayoutWidth);
                float grow = current < max ? Math.Min(left, Math.Abs(max - current)) : 0f;
                if (grow > 0f) ResizeColumn(column, current + grow, setDesiredWidthOnly);
                left -= grow;
                if (left <= 0f) break;
            }
            return left;
        }

        /// <summary>Fits the columns to a width (the header's "resize to fit").</summary>
        public void ResizeToFit(float width)
        {
            float delta = columnsWidth - Mathf.Clamp(width, minColumnsWidth, maxColumnsWidth);
            m_ScratchStretchable.Clear();
            m_ScratchFixed.Clear();
            m_ScratchRelative.Clear();
            m_ScratchStretchable.AddRange(m_StretchableColumns);
            m_ScratchFixed.AddRange(m_FixedColumns);
            m_ScratchRelative.AddRange(m_RelativeWidthColumns);
            StretchResizeColumns(m_ScratchStretchable, m_ScratchFixed, m_ScratchRelative, ref delta, true, false);
            if (m_IsDirty) UpdateCache();
        }

        void ResizeColumn(Column column, float width, bool setDesiredWidthOnly = false)
        {
            if (m_DragResizeInPreviewMode)
            {
                m_PreviewDesiredWidths[column] = width;
                return;
            }
            if (!setDesiredWidthOnly)
                column.width = column.width.unit == LengthUnit.Percent ? new Length(width / layoutWidth * 100f, LengthUnit.Percent) : (Length)width;
            column.desiredWidth = width;
        }

        internal void BeginDragResize(Column column, float pos, bool previewMode)
        {
            if (m_IsDirty) throw new Exception("Cannot begin resizing columns because the layout needs to be updated");
            m_DragResizeInPreviewMode = previewMode;
            m_DragResizing = true;
            m_DragStartPos = pos;
            m_DragLastPos = pos;
            m_DragInitialColumnWidth = column.desiredWidth;
            m_DragStretchableColumns.Clear();
            m_DragFixedColumns.Clear();
            m_DragRelativeColumns.Clear();
            if (m_DragResizeInPreviewMode)
            {
                m_PreviewDesiredWidths ??= new Dictionary<Column, float>();
                m_PreviewDesiredWidths[column] = column.desiredWidth;
            }
            var visible = m_Columns.visibleColumns;
            for (int i = column.visibleIndex + 1; i < visible.Count; i++)
            {
                var after = visible[i];
                if (after.stretchable) m_DragStretchableColumns.Add(after);
                else if (after.width.unit == LengthUnit.Percent) m_DragRelativeColumns.Add(after);
                else m_DragFixedColumns.Add(after);
                if (m_DragResizeInPreviewMode) m_PreviewDesiredWidths[after] = after.desiredWidth;
            }
        }

        public float GetDesiredPosition(Column column)
        {
            if (!column.visible) return float.NaN;
            float position = 0f;
            var visible = m_Columns.visibleColumns;
            for (int i = 0; i < column.visibleIndex; i++)
            {
                float width = GetDesiredWidth(visible[i]);
                if (!float.IsNaN(width)) position += width;
            }
            return position;
        }

        public float GetDesiredWidth(Column c) =>
            m_DragResizeInPreviewMode && m_PreviewDesiredWidths.TryGetValue(c, out float width) ? width : c.desiredWidth;

        /// <summary>Resizes a column to a pointer position: GrowAndFill takes the room from the
        /// columns after it, Grow changes only this column.</summary>
        public void DragResize(Column column, float pos)
        {
            float min = column.GetMinWidth(m_LayoutWidth);
            float max = column.GetMaxWidth(m_LayoutWidth);
            if (m_Columns.stretchMode == Columns.StretchMode.GrowAndFill)
            {
                float delta = pos - m_DragLastPos;
                float width = Mathf.Clamp(GetDesiredWidth(column) + delta, min, max);
                delta = width - GetDesiredWidth(column);
                if (m_DragStretchableColumns.Count == 0 && delta < 0f)
                {
                    StretchResizeColumns(m_DragStretchableColumns, m_DragFixedColumns, m_DragRelativeColumns, ref delta, false, true);
                    width = Mathf.Clamp(GetDesiredWidth(column) + width - GetDesiredWidth(column), min, max);
                }
                else if (delta > 0f && columnsWidth + delta < m_LayoutWidth)
                {
                    float overflow = delta < m_LayoutWidth - columnsWidth ? 0f : delta - (m_LayoutWidth - columnsWidth);
                    StretchResizeColumns(m_DragStretchableColumns, m_DragFixedColumns, m_DragRelativeColumns, ref overflow, false, true);
                    width = Mathf.Clamp(GetDesiredWidth(column) + delta - overflow, min, max);
                }
                else
                {
                    StretchResizeColumns(m_DragStretchableColumns, m_DragFixedColumns, m_DragRelativeColumns, ref delta, false, true);
                    width = Mathf.Clamp(GetDesiredWidth(column) + delta, min, max);
                }
                ResizeColumn(column, width);
            }
            else
            {
                ResizeColumn(column, Math.Max(min, Math.Min(max, m_DragInitialColumnWidth + pos - m_DragStartPos)));
            }
            m_DragLastPos = pos;
        }

        internal void EndDragResize(Column column, bool cancelled)
        {
            if (m_DragResizeInPreviewMode)
            {
                m_DragResizeInPreviewMode = false;
                if (!cancelled)
                    foreach (var entry in m_PreviewDesiredWidths) ResizeColumn(entry.Key, entry.Value, entry.Key != column);
                m_PreviewDesiredWidths.Clear();
            }
            m_DragResizing = false;
            m_DragStretchableColumns.Clear();
            m_DragFixedColumns.Clear();
            m_DragRelativeColumns.Clear();
        }

        void UpdateCache()
        {
            ClearCache();
            foreach (var column in m_Columns.visibleColumns)
            {
                if (column.stretchable) m_StretchableColumns.Add(column);
                else if (column.width.unit == LengthUnit.Pixel) m_FixedColumns.Add(column);
                if (column.width.unit == LengthUnit.Percent) m_RelativeWidthColumns.Add(column);
                if (column.width.unit == LengthUnit.Pixel && (column.minWidth.unit == LengthUnit.Percent || column.maxWidth.unit == LengthUnit.Percent))
                    m_MixedWidthColumns.Add(column);
                m_MaxColumnsWidth += column.GetMaxWidth(m_LayoutWidth);
                m_MinColumnsWidth += column.GetMinWidth(m_LayoutWidth);
            }
        }

        void UpdateMinAndMaxColumnsWidth()
        {
            m_MaxColumnsWidth = 0f;
            m_MinColumnsWidth = 0f;
            foreach (var column in m_Columns.visibleColumns)
            {
                m_MaxColumnsWidth += column.GetMaxWidth(m_LayoutWidth);
                m_MinColumnsWidth += column.GetMinWidth(m_LayoutWidth);
            }
        }

        void ClearCache()
        {
            m_StretchableColumns.Clear();
            m_RelativeWidthColumns.Clear();
            m_FixedColumns.Clear();
            m_MixedWidthColumns.Clear();
            m_MaxColumnsWidth = 0f;
            m_MinColumnsWidth = 0f;
            m_ColumnsWidthDirty = true;
        }
    }

    /// <summary>What multi-column list and tree views share: the header, row cells per visible
    /// column, cell callbacks and sorting.</summary>
    public partial class MultiColumnController : IDisposable
    {
        static readonly PropertyName k_BoundColumnVePropertyName = "__unity-multi-column-bound-column";
        internal static readonly PropertyName bindableElementPropertyName = "__unity-multi-column-bindable-element";
        internal static readonly string baseUssClassName = "unity-multi-column-view";
        public static readonly string headerContainerUssClassName = baseUssClassName + "__header-container";
        public static readonly string rowContainerUssClassName = baseUssClassName + "__row-container";
        public static readonly string cellUssClassName = baseUssClassName + "__cell";
        public static readonly string cellLabelUssClassName = cellUssClassName + "__label";

        List<int> m_SortedToSourceIndex;
        List<int> m_SourceToSortedIndex;
        ColumnSortingMode m_SortingMode;
        BaseVerticalCollectionView m_View;
        VisualElement m_HeaderContainer;
        MultiColumnCollectionHeader m_MultiColumnHeader;
        readonly Comparison<int> m_CombinedComparison;

        internal MultiColumnCollectionHeader header => m_MultiColumnHeader;

        internal ColumnSortingMode sortingMode
        {
            get => m_SortingMode;
            set
            {
                m_SortingMode = value;
                header.sortingEnabled = m_SortingMode != ColumnSortingMode.None;
            }
        }

        public event Action columnSortingChanged;

        public MultiColumnController(Columns columns, SortColumnDescriptions sortDescriptions, List<SortColumnDescription> sortedColumns)
        {
            m_CombinedComparison = CombinedComparison;
            m_MultiColumnHeader = new MultiColumnCollectionHeader(columns, sortDescriptions, sortedColumns);
            m_MultiColumnHeader.columnSortingChanged += OnColumnSortingChanged;
            m_MultiColumnHeader.columnResized += OnColumnResized;
            m_MultiColumnHeader.columns.columnAdded += OnColumnAdded;
            m_MultiColumnHeader.columns.columnRemoved += OnColumnRemoved;
            m_MultiColumnHeader.columns.columnReordered += OnColumnReordered;
            m_MultiColumnHeader.columns.columnChanged += OnColumnsChanged;
            m_MultiColumnHeader.columns.changed += OnColumnChanged;
        }

        static VisualElement DefaultMakeCellItem()
        {
            var label = new Label();
            label.AddToClassList(cellLabelUssClassName);
            return label;
        }

        /// <summary>A row container with one cell per visible column, each holding the column's made
        /// cell (a label without makeCell).</summary>
        public VisualElement MakeItem()
        {
            var row = new VisualElement { name = rowContainerUssClassName };
            row.AddToClassList(rowContainerUssClassName);
            foreach (var column in m_MultiColumnHeader.columns.visibleColumns)
            {
                var cell = new VisualElement();
                cell.AddToClassList(cellUssClassName);
                var content = column.makeCell?.Invoke() ?? DefaultMakeCellItem();
                cell.SetProperty(bindableElementPropertyName, content);
                cell.Add(content);
                row.Add(cell);
            }
            return row;
        }

        /// <summary>Binds each cell with its column's bindCell (the source index) or shows the item's
        /// text, and gives the cell its header column's width.</summary>
        public void BindItem<T>(VisualElement element, int index, T item)
        {
            int cellIndex = 0;
            index = GetSourceIndex(index);
            foreach (var column in m_MultiColumnHeader.columns.visibleColumns)
            {
                if (!m_MultiColumnHeader.columnDataMap.TryGetValue(column, out var data)) continue;
                var cell = element[cellIndex++];
                var content = cell.GetProperty(bindableElementPropertyName) as VisualElement;
                if (column.bindCell != null) column.bindCell(content, index);
                else if (content is Label label) label.text = item?.ToString();
                cell.style.width = data.control.resolvedStyle.width;
                cell.SetProperty(k_BoundColumnVePropertyName, column);
            }
        }

        public void UnbindItem(VisualElement element, int index)
        {
            index = GetSourceIndex(index);
            for (int i = 0; i < element.childCount; i++)
            {
                var cell = element[i];
                if (cell.GetProperty(k_BoundColumnVePropertyName) is Column column)
                    column.unbindCell?.Invoke(cell.GetProperty(bindableElementPropertyName) as VisualElement, index);
            }
        }

        public void DestroyItem(VisualElement element)
        {
            for (int i = 0; i < element.childCount; i++)
            {
                var cell = element[i];
                if (!(cell.GetProperty(k_BoundColumnVePropertyName) is Column column)) continue;
                column.destroyCell?.Invoke(cell.GetProperty(bindableElementPropertyName) as VisualElement);
                cell.ClearProperty(k_BoundColumnVePropertyName);
            }
        }

        /// <summary>Puts the header in a header container above the view's scroll content.</summary>
        public void PrepareView(BaseVerticalCollectionView collectionView)
        {
            if (m_View != null)
            {
                Debug.LogWarning("Trying to initialize multi column view more than once. This shouldn't happen.");
                return;
            }
            m_View = collectionView;
            m_HeaderContainer = new VisualElement { name = headerContainerUssClassName };
            m_HeaderContainer.AddToClassList(headerContainerUssClassName);
            collectionView.scrollView.hierarchy.Insert(0, m_HeaderContainer);
            m_HeaderContainer.Add(m_MultiColumnHeader);
            m_View.scrollView.horizontalScroller.valueChanged += OnHorizontalScrollerValueChanged;
            m_View.scrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
            m_MultiColumnHeader.columnContainer.RegisterCallback<GeometryChangedEvent>(OnColumnContainerGeometryChanged);
        }

        public void Dispose()
        {
            if (m_View != null)
            {
                m_View.scrollView.horizontalScroller.valueChanged -= OnHorizontalScrollerValueChanged;
                m_View.scrollView.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
                m_View = null;
            }
            m_MultiColumnHeader.columnContainer.UnregisterCallback<GeometryChangedEvent>(OnColumnContainerGeometryChanged);
            m_MultiColumnHeader.columnSortingChanged -= OnColumnSortingChanged;
            m_MultiColumnHeader.columnResized -= OnColumnResized;
            m_MultiColumnHeader.columns.columnAdded -= OnColumnAdded;
            m_MultiColumnHeader.columns.columnRemoved -= OnColumnRemoved;
            m_MultiColumnHeader.columns.columnReordered -= OnColumnReordered;
            m_MultiColumnHeader.columns.columnChanged -= OnColumnsChanged;
            m_MultiColumnHeader.columns.changed -= OnColumnChanged;
            m_MultiColumnHeader.RemoveFromHierarchy();
            m_MultiColumnHeader.Dispose();
            m_MultiColumnHeader = null;
            m_HeaderContainer?.RemoveFromHierarchy();
            m_HeaderContainer = null;
        }

        void OnHorizontalScrollerValueChanged(float v) => m_MultiColumnHeader.ScrollHorizontally(v);

        void OnViewportGeometryChanged(GeometryChangedEvent evt)
        {
            float padding = m_MultiColumnHeader.resolvedStyle.paddingLeft + m_MultiColumnHeader.resolvedStyle.paddingRight;
            m_MultiColumnHeader.style.maxWidth = evt.newRect.width - padding;
            UpdateContentContainer(m_View);
        }

        void OnColumnContainerGeometryChanged(GeometryChangedEvent evt) => UpdateContentContainer(m_View);

        // The rows are as wide as the columns, at least the viewport.
        static void UpdateContentContainer(BaseVerticalCollectionView collectionView, MultiColumnCollectionHeader header)
        {
            float width = Mathf.Max(header.columnContainer.layout.width, collectionView.scrollView.contentViewport.resolvedStyle.width);
            collectionView.scrollView.contentContainer.style.width = width;
        }

        void UpdateContentContainer(BaseVerticalCollectionView collectionView) => UpdateContentContainer(collectionView, m_MultiColumnHeader);

        void OnColumnSortingChanged()
        {
            UpdateDragger();
            if (sortingMode == ColumnSortingMode.Default) m_View.RefreshItems();
            columnSortingChanged?.Invoke();
        }

        /// <summary>Rows cannot be dragged while sorted.</summary>
        internal void UpdateDragger()
        {
            if (m_View?.dragger == null) return;
            m_View.dragger.enabled = sortingMode == ColumnSortingMode.None || header.sortedColumnReadonly.Count == 0;
        }

        /// <summary>With the default sorting, orders the source indices by the sorted columns'
        /// comparisons (tree items within their parent).</summary>
        internal void SortIfNeeded()
        {
            UpdateDragger();
            if (sortingMode != ColumnSortingMode.Default || m_View.itemsSource == null) return;
            m_View.virtualizationController.UnbindAll();
            m_SortedToSourceIndex?.Clear();
            m_SourceToSortedIndex?.Clear();
            if (header.sortedColumnReadonly.Count == 0) return;
            int count = m_View.itemsSource.Count;
            m_SortedToSourceIndex ??= new List<int>(count);
            m_SourceToSortedIndex ??= new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                m_SortedToSourceIndex.Add(i);
                m_SourceToSortedIndex.Add(-1);
            }
            m_SortedToSourceIndex.Sort(m_CombinedComparison);
            for (int i = 0; i < m_SortedToSourceIndex.Count; i++) m_SourceToSortedIndex[m_SortedToSourceIndex[i]] = i;
        }

        int CombinedComparison(int a, int b)
        {
            if (m_View.viewController is BaseTreeViewController tree)
            {
                // Tree rows compare as their ancestors under a common parent.
                int idA = tree.GetIdForIndex(a), idB = tree.GetIdForIndex(b);
                int parentA = tree.GetParentId(idA), parentB = tree.GetParentId(idB);
                if (parentA != parentB)
                {
                    int depthA = tree.GetIndentationDepth(idA), depthB = tree.GetIndentationDepth(idB);
                    int originalDepthA = depthA, originalDepthB = depthB;
                    while (depthA > depthB)
                    {
                        depthA--;
                        idA = parentA;
                        parentA = tree.GetParentId(parentA);
                    }
                    while (depthB > depthA)
                    {
                        depthB--;
                        idB = parentB;
                        parentB = tree.GetParentId(parentB);
                    }
                    while (parentA != parentB)
                    {
                        idA = parentA;
                        idB = parentB;
                        parentA = tree.GetParentId(parentA);
                        parentB = tree.GetParentId(parentB);
                    }
                    if (idA == idB) return originalDepthA.CompareTo(originalDepthB);
                    a = tree.GetIndexForId(idA);
                    b = tree.GetIndexForId(idB);
                }
            }
            int result = 0;
            var sortedColumns = header.sortedColumnReadonly;
            for (int i = 0; i < sortedColumns.Count; i++)
            {
                var sorted = sortedColumns[i];
                result = sorted.column.comparison?.Invoke(a, b) ?? 0;
                if (result == 0) continue;
                if (sorted.direction == SortDirection.Descending) result = -result;
                break;
            }
            return result == 0 ? a.CompareTo(b) : result;
        }

        internal int GetSourceIndex(int sortedIndex) => GetIndexFromList(sortedIndex, m_SortedToSourceIndex);

        internal int GetSortedIndex(int sourceIndex) => GetIndexFromList(sourceIndex, m_SourceToSortedIndex);

        static int GetIndexFromList(int index, List<int> indices)
        {
            if (indices == null || index < 0 || index >= indices.Count) return index;
            return indices.Count > 0 ? indices[index] : index;
        }

        void OnColumnResized(int index, float width)
        {
            if (m_View.isRebuildScheduled) return;
            foreach (var item in m_View.activeItems) item.bindableElement.ElementAt(index).style.width = width;
        }

        void OnColumnAdded(Column column, int index) => m_View?.Rebuild();

        void OnColumnRemoved(Column column) => m_View?.Rebuild();

        void OnColumnReordered(Column column, int from, int to) => m_View?.Rebuild();

        void OnColumnsChanged(Column column, ColumnDataType type)
        {
            if (type == ColumnDataType.Visibility) m_View?.ScheduleRebuild();
        }

        void OnColumnChanged(ColumnsDataType type)
        {
            if (type == ColumnsDataType.PrimaryColumn) m_View?.ScheduleRebuild();
        }
    }

    /// <summary>What a multi-column list or tree view keeps across view controllers: its columns,
    /// sort descriptions, sorted columns and sorting mode, applied to the column controller of its
    /// current view controller, whose sorting changes it reports.</summary>
    internal sealed class UaMultiColumnViewState
    {
        MultiColumnController m_Controller;
        readonly Action m_RaiseSortingChanged;

        public Columns columns;
        public ColumnSortingMode sortingMode;
        public SortColumnDescriptions sortDescriptions = new SortColumnDescriptions();
        public readonly List<SortColumnDescription> sortedColumns = new List<SortColumnDescription>();

        public event Action sortingChanged;

        public UaMultiColumnViewState() => m_RaiseSortingChanged = RaiseSortingChanged;

        void RaiseSortingChanged() => sortingChanged?.Invoke();

        public void SetSortingMode(ColumnSortingMode value)
        {
            if (sortingMode == value) return;
            sortingMode = value;
            if (m_Controller != null) m_Controller.sortingMode = value;
        }

        /// <summary>New descriptions go to the header (and are reported); null clears the current ones.</summary>
        public void SetSortDescriptions(SortColumnDescriptions value)
        {
            if (value == null)
            {
                sortDescriptions.Clear();
                return;
            }
            sortDescriptions = value;
            if (m_Controller == null) return;
            m_Controller.header.sortDescriptions = value;
            RaiseSortingChanged();
        }

        public void Attach(MultiColumnController controller)
        {
            if (m_Controller != null) m_Controller.columnSortingChanged -= m_RaiseSortingChanged;
            m_Controller = controller;
            if (controller == null) return;
            controller.sortingMode = sortingMode;
            controller.columnSortingChanged += m_RaiseSortingChanged;
        }
    }

    public partial class MultiColumnListView : BaseListView
    {
        readonly UaMultiColumnViewState m_State = new UaMultiColumnViewState();

        public new MultiColumnListViewController viewController => base.viewController as MultiColumnListViewController;

        public IEnumerable<SortColumnDescription> sortedColumns => m_State.sortedColumns;

        [CreateProperty]
        public Columns columns
        {
            get => m_State.columns;
            private set
            {
                if (value == null)
                {
                    m_State.columns.Clear();
                    return;
                }
                m_State.columns = value;
                if (value.Count > 0) GetOrCreateViewController();
            }
        }

        [CreateProperty]
        public SortColumnDescriptions sortColumnDescriptions
        {
            get => m_State.sortDescriptions;
            private set => m_State.SetSortDescriptions(value);
        }

        public bool sortingEnabled
        {
            get => sortingMode == ColumnSortingMode.Custom;
            set => sortingMode = value ? ColumnSortingMode.Custom : ColumnSortingMode.None;
        }

        [CreateProperty]
        public ColumnSortingMode sortingMode
        {
            get => m_State.sortingMode;
            set => m_State.SetSortingMode(value);
        }

        public event Action columnSortingChanged
        {
            add => m_State.sortingChanged += value;
            remove => m_State.sortingChanged -= value;
        }

        public MultiColumnListView() : this(new Columns()) { }

        public MultiColumnListView(Columns columns)
        {
            scrollView.viewDataKey = "unity-multi-column-scroll-view";
            this.columns = columns ?? new Columns();
        }

        protected override CollectionViewController CreateViewController() =>
            new MultiColumnListViewController(columns, sortColumnDescriptions, m_State.sortedColumns);

        public override void SetViewController(CollectionViewController controller)
        {
            base.SetViewController(controller);
            m_State.Attach(viewController?.columnController);
        }

        private protected override void CreateVirtualizationController() => CreateVirtualizationController<ReusableMultiColumnListViewItem>();
    }

    public partial class MultiColumnListViewController : BaseListViewController
    {
        MultiColumnController m_ColumnController;

        public MultiColumnController columnController => m_ColumnController;

        internal MultiColumnCollectionHeader header => m_ColumnController?.header;

        public MultiColumnListViewController(Columns columns, SortColumnDescriptions sortDescriptions, List<SortColumnDescription> sortedColumns)
        {
            m_ColumnController = new MultiColumnController(columns, sortDescriptions, sortedColumns);
            itemsSourceSizeChanged += SortIfNeeded;
            itemsSourceChanged += SortIfNeeded;
        }

        internal override void PreRefresh()
        {
            base.PreRefresh();
            m_ColumnController.SortIfNeeded();
        }

        void SortIfNeeded()
        {
            m_ColumnController.UpdateDragger();
            if (m_ColumnController.sortingMode == ColumnSortingMode.Default) view.RefreshItems();
        }

        internal override void InvokeMakeItem(ReusableCollectionItem reusableItem)
        {
            if (!(reusableItem is ReusableMultiColumnListViewItem item))
            {
                base.InvokeMakeItem(reusableItem);
                return;
            }
            item.Init(MakeItem(), m_ColumnController.header.columns, baseListView.reorderMode == ListViewReorderMode.Animated);
            PostInitRegistration(item);
        }

        internal override void InvokeBindItem(ReusableCollectionItem reusableItem, int index)
        {
            base.InvokeBindItem(reusableItem, index);
            if (reusableItem is ReusableListViewItem listItem)
                listItem.SetDragHandleEnabled(!(m_ColumnController.header.sortingEnabled && m_ColumnController.header.sortedColumnReadonly.Count > 0));
        }

        public override object GetItemForIndex(int index) => base.GetItemForIndex(columnController.GetSourceIndex(index));

        public override int GetIndexForId(int id) => columnController.GetSortedIndex(base.GetIndexForId(id));

        public override int GetIdForIndex(int index) => base.GetIdForIndex(columnController.GetSourceIndex(index));

        protected override VisualElement MakeItem() => m_ColumnController.MakeItem();

        protected override void BindItem(VisualElement element, int index) => m_ColumnController.BindItem(element, index, GetItemForIndex(index));

        protected override void UnbindItem(VisualElement element, int index) => m_ColumnController.UnbindItem(element, index);

        protected override void DestroyItem(VisualElement element) => m_ColumnController.DestroyItem(element);

        protected override void PrepareView()
        {
            m_ColumnController.PrepareView(view);
            baseListView.reorderModeChanged += UpdateReorderClassList;
        }

        public override void Dispose()
        {
            if (baseListView != null) baseListView.reorderModeChanged -= UpdateReorderClassList;
            m_ColumnController.Dispose();
            m_ColumnController = null;
            base.Dispose();
        }

        void UpdateReorderClassList() =>
            m_ColumnController.header.EnableInClassList(MultiColumnCollectionHeader.reorderableUssClassName, baseListView.reorderable && baseListView.reorderMode == ListViewReorderMode.Animated);
    }

    /// <summary>A multi-column list row: the row container; with animated reordering the primary
    /// column's cell holds the drag handle.</summary>
    internal partial class ReusableMultiColumnListViewItem : ReusableListViewItem
    {
        public override VisualElement rootElement => bindableElement;

        [Unbound.EditorFaithful("Unity's: multi-column rows are made with their columns")]
        public override void Init(VisualElement item) { }

        public void Init(VisualElement container, Columns columns, bool usesAnimatedDrag)
        {
            bindableElement = container;
            int cellIndex = 0;
            foreach (var column in columns.visibleColumns)
            {
                if (columns.IsPrimary(column))
                {
                    var cell = container[cellIndex];
                    UpdateHierarchy(cell, cell.GetProperty(MultiColumnController.bindableElementPropertyName) as VisualElement, usesAnimatedDrag);
                    break;
                }
                cellIndex++;
            }
        }
    }

    public partial class MultiColumnTreeView : BaseTreeView
    {
        readonly UaMultiColumnViewState m_State = new UaMultiColumnViewState();

        public new MultiColumnTreeViewController viewController => base.viewController as MultiColumnTreeViewController;

        public IEnumerable<SortColumnDescription> sortedColumns => m_State.sortedColumns;

        [CreateProperty]
        public Columns columns
        {
            get => m_State.columns;
            private set
            {
                if (value == null)
                {
                    m_State.columns.Clear();
                    return;
                }
                m_State.columns = value;
                if (value.Count > 0) GetOrCreateViewController();
            }
        }

        [CreateProperty]
        public SortColumnDescriptions sortColumnDescriptions
        {
            get => m_State.sortDescriptions;
            private set => m_State.SetSortDescriptions(value);
        }

        public bool sortingEnabled
        {
            get => sortingMode == ColumnSortingMode.Custom;
            set => sortingMode = value ? ColumnSortingMode.Custom : ColumnSortingMode.None;
        }

        [CreateProperty]
        public ColumnSortingMode sortingMode
        {
            get => m_State.sortingMode;
            set => m_State.SetSortingMode(value);
        }

        public event Action columnSortingChanged
        {
            add => m_State.sortingChanged += value;
            remove => m_State.sortingChanged -= value;
        }

        public MultiColumnTreeView() : this(new Columns()) { }

        public MultiColumnTreeView(Columns columns)
        {
            scrollView.viewDataKey = "unity-multi-column-scroll-view";
            this.columns = columns ?? new Columns();
        }

        internal override void SetRootItemsInternal<T>(IList<TreeViewItemData<T>> rootItems) =>
            TreeViewHelpers<T, DefaultMultiColumnTreeViewController<T>>.SetRootItems(this, rootItems,
                () => new DefaultMultiColumnTreeViewController<T>(columns, m_State.sortDescriptions, m_State.sortedColumns));

        private protected override IEnumerable<TreeViewItemData<T>> GetSelectedItemsInternal<T>() =>
            TreeViewHelpers<T, DefaultMultiColumnTreeViewController<T>>.GetSelectedItems(this);

        private protected override T GetItemDataForIndexInternal<T>(int index) =>
            TreeViewHelpers<T, DefaultMultiColumnTreeViewController<T>>.GetItemDataForIndex(this, index);

        private protected override T GetItemDataForIdInternal<T>(int id) =>
            TreeViewHelpers<T, DefaultMultiColumnTreeViewController<T>>.GetItemDataForId(this, id);

        private protected override void AddItemInternal<T>(TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree) =>
            TreeViewHelpers<T, DefaultMultiColumnTreeViewController<T>>.AddItem(this, item, parentId, childIndex, rebuildTree);

        protected override CollectionViewController CreateViewController() =>
            new DefaultMultiColumnTreeViewController<object>(columns, sortColumnDescriptions, m_State.sortedColumns);

        public override void SetViewController(CollectionViewController controller)
        {
            base.SetViewController(controller);
            m_State.Attach(viewController?.columnController);
        }

        private protected override void CreateVirtualizationController() => CreateVirtualizationController<ReusableMultiColumnTreeViewItem>();
    }

    public abstract partial class MultiColumnTreeViewController : BaseTreeViewController
    {
        MultiColumnController m_ColumnController;

        public MultiColumnController columnController => m_ColumnController;

        internal MultiColumnCollectionHeader header => m_ColumnController?.header;

        protected MultiColumnTreeViewController(Columns columns, SortColumnDescriptions sortDescriptions, List<SortColumnDescription> sortedColumns)
        {
            m_ColumnController = new MultiColumnController(columns, sortDescriptions, sortedColumns);
        }

        internal override void PreRefresh()
        {
            base.PreRefresh();
            m_ColumnController.SortIfNeeded();
        }

        internal override void InvokeMakeItem(ReusableCollectionItem reusableItem)
        {
            if (!(reusableItem is ReusableMultiColumnTreeViewItem item))
            {
                base.InvokeMakeItem(reusableItem);
                return;
            }
            item.Init(MakeItem(), m_ColumnController.header.columns);
            PostInitRegistration(item);
        }

        protected override VisualElement MakeItem() => m_ColumnController.MakeItem();

        protected override void BindItem(VisualElement element, int index) => m_ColumnController.BindItem(element, index, GetItemForIndex(index));

        protected override void UnbindItem(VisualElement element, int index) => m_ColumnController.UnbindItem(element, index);

        protected override void DestroyItem(VisualElement element) => m_ColumnController.DestroyItem(element);

        protected override void PrepareView() => m_ColumnController.PrepareView(view);

        public override void Dispose()
        {
            m_ColumnController.Dispose();
            m_ColumnController = null;
            base.Dispose();
        }
    }

    /// <summary>The default multi-column tree controller of data type T (the same tree data as
    /// DefaultTreeViewController&lt;T&gt;).</summary>
    public partial class DefaultMultiColumnTreeViewController<T> : MultiColumnTreeViewController, IDefaultTreeViewController<T>
    {
        readonly UaTreeItems<T> m_Items;

        public override IList itemsSource
        {
            get => base.itemsSource;
            set => m_Items.SetItemsSource(value);
        }

        public DefaultMultiColumnTreeViewController(Columns columns, SortColumnDescriptions sortDescriptions, List<SortColumnDescription> sortedColumns)
            : base(columns, sortDescriptions, sortedColumns)
        {
            m_Items = new UaTreeItems<T>(this);
        }

        public void SetRootItems(IList<TreeViewItemData<T>> items) => m_Items.SetRootItems(items);

        public virtual void AddItem(in TreeViewItemData<T> item, int parentId, int childIndex, bool rebuildTree = true) => m_Items.AddItem(item, parentId, childIndex, rebuildTree);

        public virtual TreeViewItemData<T> GetTreeViewItemDataForId(int id) => m_Items.GetForId(id);

        public virtual TreeViewItemData<T> GetTreeViewItemDataForIndex(int index) => m_Items.GetForId(GetIdForIndex(index));

        public override bool TryRemoveItem(int id, bool rebuildTree = true) => m_Items.TryRemoveItem(id, rebuildTree);

        public override object GetItemForId(int id) => GetTreeViewItemDataForId(id).data;

        public T GetDataForId(int id) => GetTreeViewItemDataForId(id).data;

        public T GetDataForIndex(int index) => GetTreeViewItemDataForIndex(index).data;

        public override object GetItemForIndex(int index) => GetDataForIndex(index);
    }

    /// <summary>A multi-column tree row: the row container; the primary column's cell holds the
    /// indent and toggle around its content.</summary>
    internal partial class ReusableMultiColumnTreeViewItem : ReusableTreeViewItem
    {
        public override VisualElement rootElement => bindableElement;

        [Unbound.EditorFaithful("Unity's: multi-column rows are made with their columns")]
        public override void Init(VisualElement item) { }

        public void Init(VisualElement container, Columns columns)
        {
            bindableElement = container;
            int cellIndex = 0;
            foreach (var column in columns.visibleColumns)
            {
                if (columns.IsPrimary(column))
                {
                    var cell = container[cellIndex];
                    InitExpandHierarchy(cell, cell.GetProperty(MultiColumnController.bindableElementPropertyName) as VisualElement);
                    break;
                }
                cellIndex++;
            }
        }
    }
}

namespace UnityEngine.UIElements.Internal
{
    /// <summary>A multi-column view's header: a column container with one header column per visible
    /// column and a resize handle container over it, laid out by the column layout; clicks on a
    /// sortable header change the sort descriptions.</summary>
    internal partial class MultiColumnCollectionHeader : VisualElement, IDisposable
    {
        internal partial class ColumnData
        {
            public MultiColumnHeaderColumn control { get; set; }
            public MultiColumnHeaderColumnResizeHandle resizeHandle { get; set; }
        }

        public static readonly string ussClassName = "unity-multi-column-header";
        public static readonly string columnContainerUssClassName = ussClassName + "__column-container";
        public static readonly string handleContainerUssClassName = ussClassName + "__resize-handle-container";
        public static readonly string reorderableUssClassName = ussClassName + "__header";
        // Layout passes past this one schedule the column layout instead of running it at once.
        const int kMaxStableLayoutPassCount = 2;

        bool m_SortingEnabled;
        readonly List<SortColumnDescription> m_SortedColumns;
        SortColumnDescriptions m_SortDescriptions;
        readonly List<(SortColumnDescription desc, SortDirection direction)> m_OldSortedColumnStates = new List<(SortColumnDescription, SortDirection)>();
        readonly List<(SortColumnDescription desc, SortDirection direction)> m_NewSortedColumnStates = new List<(SortColumnDescription, SortDirection)>();
        bool m_SortingUpdatesTemporarilyDisabled;
        bool m_DoLayoutScheduled;
        readonly Action m_DoLayout;

        public Dictionary<Column, ColumnData> columnDataMap { get; } = new Dictionary<Column, ColumnData>();

        public ColumnLayout columnLayout { get; }

        public VisualElement columnContainer { get; }

        public VisualElement resizeHandleContainer { get; }

        public IEnumerable<SortColumnDescription> sortedColumns => m_SortedColumns;

        internal IReadOnlyList<SortColumnDescription> sortedColumnReadonly => m_SortedColumns;

        public SortColumnDescriptions sortDescriptions
        {
            get => m_SortDescriptions;
            protected internal set
            {
                if (m_SortDescriptions != null) m_SortDescriptions.changed -= UpdateSortedColumns;
                m_SortDescriptions = value;
                m_SortDescriptions.changed += UpdateSortedColumns;
                UpdateSortedColumns();
            }
        }

        public Columns columns { get; }

        public bool sortingEnabled
        {
            get => m_SortingEnabled;
            set
            {
                if (m_SortingEnabled == value) return;
                m_SortingEnabled = value;
                UpdateSortingStatus();
                UpdateSortedColumns();
            }
        }

        public event Action<int, float> columnResized;

        public event Action columnSortingChanged;

        public MultiColumnCollectionHeader() : this(new Columns(), new SortColumnDescriptions(), new List<SortColumnDescription>()) { }

        public MultiColumnCollectionHeader(Columns columns, SortColumnDescriptions sortDescriptions, List<SortColumnDescription> sortedColumns)
        {
            AddToClassList(ussClassName);
            this.columns = columns;
            m_SortedColumns = sortedColumns;
            this.sortDescriptions = sortDescriptions;
            columnContainer = new VisualElement { pickingMode = PickingMode.Ignore };
            columnContainer.AddToClassList(columnContainerUssClassName);
            Add(columnContainer);
            resizeHandleContainer = new VisualElement { pickingMode = PickingMode.Ignore };
            resizeHandleContainer.AddToClassList(handleContainerUssClassName);
            resizeHandleContainer.StretchToParentSize();
            Add(resizeHandleContainer);
            columnLayout = new ColumnLayout(columns);
            m_DoLayout = DoLayout;
            columnLayout.layoutRequested += ScheduleDoLayout;
            foreach (var column in columns.visibleColumns) OnColumnAdded(column);
            columns.columnAdded += OnColumnAdded;
            columns.columnRemoved += OnColumnRemoved;
            columns.columnChanged += OnColumnChanged;
            columns.columnReordered += OnColumnReordered;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        void ScheduleDoLayout()
        {
            if (m_DoLayoutScheduled) return;
            schedule.Execute(m_DoLayout);
            m_DoLayoutScheduled = true;
        }

        /// <summary>Matches the sort descriptions to sortable columns and reports a change of the
        /// sorted columns or their directions.</summary>
        void UpdateSortedColumns()
        {
            if (m_SortingUpdatesTemporarilyDisabled) return;
            m_NewSortedColumnStates.Clear();
            if (sortingEnabled)
            {
                foreach (var description in sortDescriptions)
                {
                    Column column = null;
                    if (description.columnIndex != -1) column = columns[description.columnIndex];
                    else if (!string.IsNullOrEmpty(description.columnName)) column = columns[description.columnName];
                    if (column != null && column.sortable)
                    {
                        description.column = column;
                        m_NewSortedColumnStates.Add((description, description.direction));
                    }
                    else
                    {
                        description.column = null;
                    }
                }
            }
            if (SameStates(m_OldSortedColumnStates, m_NewSortedColumnStates)) return;
            m_SortedColumns.Clear();
            foreach (var state in m_NewSortedColumnStates) m_SortedColumns.Add(state.desc);
            m_OldSortedColumnStates.Clear();
            m_OldSortedColumnStates.AddRange(m_NewSortedColumnStates);
            ApplyColumnSorting();
            columnSortingChanged?.Invoke();
        }

        static bool SameStates(List<(SortColumnDescription desc, SortDirection direction)> a, List<(SortColumnDescription desc, SortDirection direction)> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].desc != b[i].desc || a[i].direction != b[i].direction) return false;
            return true;
        }

        void UpdateColumnControls()
        {
            bool anyStretchable = false;
            Column last = null;
            foreach (var column in columns.visibleColumns)
            {
                anyStretchable |= column.stretchable;
                if (columnDataMap.TryGetValue(column, out var data))
                {
                    data.control.style.minWidth = column.minWidth;
                    data.control.style.maxWidth = column.maxWidth;
                    data.resizeHandle.style.display = columns.resizable && column.resizable ? DisplayStyle.Flex : DisplayStyle.None;
                }
                last = column;
            }
            if (anyStretchable)
            {
                columnContainer.style.flexGrow = 1f;
                if (columns.stretchMode == Columns.StretchMode.GrowAndFill && last != null && columnDataMap.TryGetValue(last, out var lastData))
                    lastData.resizeHandle.style.display = DisplayStyle.None;
            }
            else
            {
                columnContainer.style.flexGrow = 0f;
            }
            UpdateSortingStatus();
        }

        void OnColumnAdded(Column column, int index) => OnColumnAdded(column);

        void OnColumnAdded(Column column)
        {
            if (columnDataMap.ContainsKey(column)) return;
            if (column.visible)
            {
                var control = new MultiColumnHeaderColumn(column);
                var handle = new MultiColumnHeaderColumnResizeHandle();
                control.RegisterCallback<GeometryChangedEvent>(OnColumnControlGeometryChanged);
                control.clickable.clickedWithEventInfo += OnColumnClicked;
                handle.dragArea.AddManipulator(new ColumnResizer(column));
                columnDataMap[column] = new ColumnData { control = control, resizeHandle = handle };
                columnContainer.Insert(column.visibleIndex, control);
                resizeHandleContainer.Insert(column.visibleIndex, handle);
            }
            UpdateColumnControls();
        }

        void OnColumnRemoved(Column column)
        {
            if (!columnDataMap.TryGetValue(column, out var data)) return;
            CleanupColumnData(data);
            columnDataMap.Remove(column);
            UpdateColumnControls();
        }

        void OnColumnChanged(Column column, ColumnDataType type)
        {
            if (type == ColumnDataType.Visibility)
            {
                // Adding or removing the header column updates the controls.
                if (column.visible) OnColumnAdded(column);
                else OnColumnRemoved(column);
                ApplyColumnSorting();
                return;
            }
            UpdateColumnControls();
        }

        void OnColumnReordered(Column column, int from, int to)
        {
            if (!column.visible || from == to || !columnDataMap.TryGetValue(column, out var data)) return;
            int at = column.visibleIndex;
            if (at == columns.visibleColumns.Count - 1)
            {
                data.control.BringToFront();
            }
            else
            {
                if (to > from) at++;
                data.control.PlaceBehind(columnContainer[at]);
                data.resizeHandle.PlaceBehind(resizeHandleContainer[at]);
            }
            UpdateColumnControls();
        }

        void OnGeometryChanged(GeometryChangedEvent e)
        {
            if (float.IsNaN(e.newRect.width) || float.IsNaN(e.newRect.height)) return;
            columnLayout.Dirty();
            if (e.layoutPass > kMaxStableLayoutPassCount) ScheduleDoLayout();
            else DoLayout();
        }

        void DoLayout()
        {
            columnLayout.DoLayout(layout.width);
            m_DoLayoutScheduled = false;
        }

        void OnColumnControlGeometryChanged(GeometryChangedEvent evt)
        {
            if (!(evt.target is MultiColumnHeaderColumn control)) return;
            columnDataMap[control.column].resizeHandle.style.left = control.layout.xMax;
            if (Math.Abs(evt.newRect.width - evt.oldRect.width) < float.Epsilon) return;
            int index = columnContainer.IndexOf(control);
            columnResized?.Invoke(index, columnContainer[index].resolvedStyle.width);
        }

        void OnColumnClicked(EventBase evt)
        {
            if (!sortingEnabled || !(evt.currentTarget is MultiColumnHeaderColumn control) || !control.column.sortable) return;
            EventModifiers modifiers;
            if (evt is IPointerEvent pointer) modifiers = pointer.modifiers;
            else if (evt is IMouseEvent mouse) modifiers = mouse.modifiers;
            else return;
            m_SortingUpdatesTemporarilyDisabled = true;
            try
            {
                UpdateSortColumnDescriptionsOnClick(control.column, modifiers);
            }
            finally
            {
                m_SortingUpdatesTemporarilyDisabled = false;
            }
            UpdateSortedColumns();
        }

        /// <summary>A click sorts by the column (again: the other direction); Shift removes it, the
        /// action key (Ctrl, Command on macOS) adds it to the sorted columns.</summary>
        void UpdateSortColumnDescriptionsOnClick(Column column, EventModifiers modifiers)
        {
            SortColumnDescription description = null;
            foreach (var d in sortDescriptions)
            {
                if (d.column == column || (!string.IsNullOrEmpty(column.name) && d.columnName == column.name) || d.columnIndex == column.index)
                {
                    description = d;
                    break;
                }
            }
            if (description != null)
            {
                if (modifiers == EventModifiers.Shift)
                {
                    sortDescriptions.Remove(description);
                    return;
                }
                description.direction = description.direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
            }
            else
            {
                description = string.IsNullOrEmpty(column.name)
                    ? new SortColumnDescription(column.index, SortDirection.Ascending)
                    : new SortColumnDescription(column.name, SortDirection.Ascending);
            }
            var actionKey = Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer
                ? EventModifiers.Command : EventModifiers.Control;
            if (modifiers != actionKey) sortDescriptions.Clear();
            if (!sortDescriptions.Contains(description)) sortDescriptions.Add(description);
        }

        public void ScrollHorizontally(float horizontalOffset) =>
            transform.position = new Vector3(-horizontalOffset, transform.position.y, transform.position.z);

        void ApplyColumnSorting()
        {
            foreach (var column in columns.visibleColumns)
            {
                if (!columnDataMap.TryGetValue(column, out var data)) continue;
                data.control.sortOrderLabel = "";
                data.control.RemoveFromClassList(MultiColumnHeaderColumn.sortedAscendingUssClassName);
                data.control.RemoveFromClassList(MultiColumnHeaderColumn.sortedDescendingUssClassName);
            }
            int sortedCount = 0;
            foreach (var sorted in m_SortedColumns)
            {
                if (sorted.column == null || !columnDataMap.TryGetValue(sorted.column, out var data)) continue;
                sortedCount++;
                data.control.AddToClassList(sorted.direction == SortDirection.Ascending
                    ? MultiColumnHeaderColumn.sortedAscendingUssClassName : MultiColumnHeaderColumn.sortedDescendingUssClassName);
            }
            if (sortedCount <= 1) return;
            // Several sorted columns show their order.
            int order = 0;
            foreach (var sorted in m_SortedColumns)
                if (sorted.column != null && columnDataMap.TryGetValue(sorted.column, out var data)) data.control.sortOrderLabel = (++order).ToString();
        }

        void UpdateSortingStatus()
        {
            bool sortable = false;
            foreach (var column in columns.visibleColumns)
                if (columnDataMap.ContainsKey(column) && sortingEnabled && column.sortable) sortable = true;
            foreach (var column in columns.visibleColumns)
                if (columnDataMap.TryGetValue(column, out var data)) data.control.EnableInClassList(MultiColumnHeaderColumn.sortableUssClassName, sortable);
        }

        void CleanupColumnData(ColumnData data)
        {
            data.control.UnregisterCallback<GeometryChangedEvent>(OnColumnControlGeometryChanged);
            data.control.clickable.clickedWithEventInfo -= OnColumnClicked;
            data.control.RemoveFromHierarchy();
            data.control.Dispose();
            data.resizeHandle.RemoveFromHierarchy();
        }

        public void Dispose()
        {
            sortDescriptions.changed -= UpdateSortedColumns;
            columnLayout.layoutRequested -= ScheduleDoLayout;
            columnLayout.Dispose();
            columns.columnAdded -= OnColumnAdded;
            columns.columnRemoved -= OnColumnRemoved;
            columns.columnChanged -= OnColumnChanged;
            columns.columnReordered -= OnColumnReordered;
            foreach (var data in columnDataMap.Values) CleanupColumnData(data);
            columnDataMap.Clear();
        }
    }

    /// <summary>A header column: sort indicator, then a content container holding the header content
    /// (the column's makeHeader, else an icon and title label bound from the column).</summary>
    internal partial class MultiColumnHeaderColumn : VisualElement
    {
        public static readonly string ussClassName = MultiColumnCollectionHeader.ussClassName + "__column";
        public static readonly string sortableUssClassName = ussClassName + "--sortable";
        public static readonly string sortedAscendingUssClassName = ussClassName + "--sorted-ascending";
        public static readonly string sortedDescendingUssClassName = ussClassName + "--sorted-descending";
        public static readonly string movingUssClassName = ussClassName + "--moving";
        public static readonly string contentContainerUssClassName = ussClassName + "__content-container";
        public static readonly string contentUssClassName = ussClassName + "__content";
        public static readonly string defaultContentUssClassName = ussClassName + "__default-content";
        public static readonly string hasIconUssClassName = contentUssClassName + "--has-icon";
        public static readonly string hasTitleUssClassName = contentUssClassName + "--has-title";
        public static readonly string titleUssClassName = ussClassName + "__title";
        public static readonly string iconElementName = "unity-multi-column-header-column-icon";
        public static readonly string titleElementName = "unity-multi-column-header-column-title";

        readonly VisualElement m_ContentContainer;
        VisualElement m_Content;
        readonly MultiColumnHeaderColumnSortIndicator m_SortIndicatorContainer;
        IVisualElementScheduledItem m_ScheduledHeaderTemplateUpdate;
        Action<VisualElement> m_BindContent;
        Action<VisualElement> m_UnbindContent;
        Action<VisualElement> m_DestroyContent;
        bool m_ContentBound;

        public Clickable clickable { get; private set; }

        public string sortOrderLabel
        {
            get => m_SortIndicatorContainer.sortOrderLabel;
            set => m_SortIndicatorContainer.sortOrderLabel = value;
        }

        public Column column { get; private set; }

        internal Label title => content?.Q<Label>(titleElementName);

        public VisualElement content
        {
            get => m_Content;
            set
            {
                if (m_Content != null)
                {
                    if (m_Content.parent == m_ContentContainer) m_Content.RemoveFromHierarchy();
                    DestroyHeaderContent();
                    m_Content = null;
                }
                m_Content = value;
                if (m_Content == null) return;
                m_Content.AddToClassList(contentUssClassName);
                m_ContentContainer.Add(m_Content);
            }
        }

        public MultiColumnHeaderColumn() : this(new Column()) { }

        public MultiColumnHeaderColumn(Column column)
        {
            this.column = column;
            column.changed += OnColumnChanged;
            column.resized += OnColumnResized;
            AddToClassList(ussClassName);
            style.marginLeft = 0f;
            style.marginTop = 0f;
            style.marginRight = 0f;
            style.marginBottom = 0f;
            style.paddingLeft = 0f;
            style.paddingTop = 0f;
            style.paddingRight = 0f;
            style.paddingBottom = 0f;
            Add(m_SortIndicatorContainer = new MultiColumnHeaderColumnSortIndicator());
            m_ContentContainer = new VisualElement();
            m_ContentContainer.style.flexGrow = 1f;
            m_ContentContainer.style.flexShrink = 1f;
            m_ContentContainer.AddToClassList(contentContainerUssClassName);
            Add(m_ContentContainer);
            UpdateHeaderTemplate();
            UpdateGeometryFromColumn();
            clickable = new Clickable((Action)null);
            this.AddManipulator(clickable);
            clickable.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Shift });
            var actionKey = Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer
                ? EventModifiers.Command : EventModifiers.Control;
            clickable.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = actionKey });
        }

        void OnColumnChanged(Column c, ColumnDataType role)
        {
            if (column != c) return;
            if (role == ColumnDataType.HeaderTemplate)
            {
                m_ScheduledHeaderTemplateUpdate?.Pause();
                m_ScheduledHeaderTemplateUpdate = schedule.Execute(UpdateHeaderTemplate);
            }
            else
            {
                UpdateDataFromColumn();
            }
        }

        void OnColumnResized(Column c) => UpdateGeometryFromColumn();

        void UpdateDataFromColumn()
        {
            if (column == null) return;
            name = column.name;
            UnbindHeaderContent();
            BindHeaderContent();
        }

        void BindHeaderContent()
        {
            if (m_ContentBound) return;
            m_BindContent?.Invoke(content);
            m_ContentBound = true;
        }

        void UnbindHeaderContent()
        {
            if (!m_ContentBound) return;
            m_UnbindContent?.Invoke(content);
            m_ContentBound = false;
        }

        void DestroyHeaderContent()
        {
            UnbindHeaderContent();
            var destroy = m_DestroyContent;
            m_BindContent = null;
            m_UnbindContent = null;
            m_DestroyContent = null;
            destroy?.Invoke(content);
        }

        VisualElement CreateDefaultHeaderContent()
        {
            var content = new VisualElement { pickingMode = PickingMode.Ignore };
            content.AddToClassList(defaultContentUssClassName);
            var icon = new MultiColumnHeaderColumnIcon { name = iconElementName, pickingMode = PickingMode.Ignore };
            var label = new Label { name = titleElementName, pickingMode = PickingMode.Ignore };
            label.AddToClassList(titleUssClassName);
            content.Add(icon);
            content.Add(label);
            return content;
        }

        void DefaultBindHeaderContent(VisualElement ve)
        {
            var label = ve.Q<Label>(titleElementName);
            var icon = ve.Q<MultiColumnHeaderColumnIcon>();
            ve.RemoveFromClassList(hasTitleUssClassName);
            if (label != null) label.text = column.title;
            if (!string.IsNullOrEmpty(column.title)) ve.AddToClassList(hasTitleUssClassName);
            if (icon == null) return;
            if (column.icon.texture != null || column.icon.sprite != null || column.icon.vectorImage != null)
            {
                icon.isImageInline = true;
                icon.image = column.icon.texture;
                icon.sprite = column.icon.sprite;
                icon.vectorImage = column.icon.vectorImage;
            }
            else if (icon.isImageInline)
            {
                icon.image = null;
                icon.sprite = null;
                icon.vectorImage = null;
            }
            icon.UpdateClassList();
        }

        void UpdateHeaderTemplate()
        {
            if (column == null) return;
            var make = column.makeHeader;
            var bind = column.bindHeader;
            var unbind = column.unbindHeader;
            var destroy = column.destroyHeader;
            if (make == null)
            {
                make = CreateDefaultHeaderContent;
                bind = DefaultBindHeaderContent;
                unbind = null;
                destroy = null;
            }
            content = make();
            m_BindContent = bind;
            m_UnbindContent = unbind;
            m_DestroyContent = destroy;
            m_ContentBound = false;
            m_ScheduledHeaderTemplateUpdate = null;
            UpdateDataFromColumn();
        }

        void UpdateGeometryFromColumn()
        {
            if (!float.IsNaN(column.desiredWidth)) style.width = column.desiredWidth;
        }

        public void Dispose()
        {
            column.changed -= OnColumnChanged;
            column.resized -= OnColumnResized;
            this.RemoveManipulator(clickable);
            column = null;
            content = null;
        }
    }

    internal partial class MultiColumnHeaderColumnResizeHandle : VisualElement
    {
        public static readonly string ussClassName = MultiColumnCollectionHeader.ussClassName + "__column-resize-handle";
        public static readonly string dragAreaUssClassName = ussClassName + "__drag-area";

        public VisualElement dragArea { get; }

        public MultiColumnHeaderColumnResizeHandle()
        {
            AddToClassList(ussClassName);
            dragArea = new VisualElement { focusable = true, tabIndex = -1 };
            dragArea.AddToClassList(dragAreaUssClassName);
            Add(dragArea);
        }
    }

    internal partial class MultiColumnHeaderColumnSortIndicator : VisualElement
    {
        public static readonly string ussClassName = MultiColumnHeaderColumn.ussClassName + "__sort-indicator";
        public static readonly string arrowUssClassName = ussClassName + "__arrow";
        public static readonly string indexLabelUssClassName = ussClassName + "__index-label";

        readonly Label m_IndexLabel;

        public string sortOrderLabel
        {
            get => m_IndexLabel.text;
            set => m_IndexLabel.text = value;
        }

        public MultiColumnHeaderColumnSortIndicator()
        {
            AddToClassList(ussClassName);
            pickingMode = PickingMode.Ignore;
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList(arrowUssClassName);
            Add(arrow);
            m_IndexLabel = new Label { pickingMode = PickingMode.Ignore };
            m_IndexLabel.AddToClassList(indexLabelUssClassName);
            Add(m_IndexLabel);
        }
    }

    internal partial class MultiColumnHeaderColumnIcon : Image
    {
        public new static readonly string ussClassName = MultiColumnHeaderColumn.ussClassName + "__icon";

        public bool isImageInline { get; set; }

        public MultiColumnHeaderColumnIcon()
        {
            AddToClassList(ussClassName);
            RegisterCallback<CustomStyleResolvedEvent>(_ => UpdateClassList());
        }

        /// <summary>The header content has the --has-icon class while an image is shown.</summary>
        public void UpdateClassList()
        {
            if (parent == null) return;
            parent.RemoveFromClassList(MultiColumnHeaderColumn.hasIconUssClassName);
            if (image != null || sprite != null || vectorImage != null) parent.AddToClassList(MultiColumnHeaderColumn.hasIconUssClassName);
        }
    }

    /// <summary>Resizes a column by dragging its resize handle (with a preview line when the
    /// columns ask for one; Escape cancels it).</summary>
    internal partial class ColumnResizer : PointerManipulator
    {
        Vector2 m_Start;
        protected bool m_Active;
        bool m_Resizing;
        MultiColumnCollectionHeader m_Header;
        readonly Column m_Column;
        VisualElement m_PreviewElement;

        public ColumnLayout columnLayout { get; set; }

        public bool preview { get; set; }

        public ColumnResizer(Column column)
        {
            m_Column = column;
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape && m_Resizing && preview) EndDragResize(true);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (m_Active)
            {
                e.StopImmediatePropagation();
                return;
            }
            if (!CanStartManipulation(e)) return;
            var current = e.currentTarget as VisualElement;
            m_Header = current.GetFirstAncestorOfType<MultiColumnCollectionHeader>();
            preview = m_Column.collection.resizePreview;
            if (preview)
            {
                if (m_PreviewElement == null)
                {
                    m_PreviewElement = new VisualElement { pickingMode = PickingMode.Ignore };
                    m_PreviewElement.AddToClassList(MultiColumnCollectionHeader.ussClassName + "__column-resize-preview");
                }
                var host = m_Header.GetFirstAncestorOfType<ScrollView>()?.parent ?? m_Header.parent;
                host.hierarchy.Add(m_PreviewElement);
            }
            columnLayout = m_Header.columnLayout;
            m_Start = current.ChangeCoordinatesTo(m_Header, e.localPosition);
            m_Resizing = true;
            columnLayout?.BeginDragResize(m_Column, m_Start.x, preview);
            if (preview) UpdatePreviewPosition();
            m_Active = true;
            target.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!m_Active || !target.HasPointerCapture(e.pointerId)) return;
            if (m_Resizing)
            {
                columnLayout?.DragResize(m_Column, ((VisualElement)e.currentTarget).ChangeCoordinatesTo(m_Header, e.localPosition).x);
                if (preview) UpdatePreviewPosition();
            }
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!m_Active || !target.HasPointerCapture(e.pointerId) || !CanStopManipulation(e)) return;
            EndDragResize(false);
            m_Active = false;
            target.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        void UpdatePreviewPosition() =>
            m_PreviewElement.style.left = columnLayout.GetDesiredPosition(m_Column) + columnLayout.GetDesiredWidth(m_Column);

        void EndDragResize(bool cancelled)
        {
            if (!m_Resizing) return;
            if (preview)
            {
                m_PreviewElement?.RemoveFromHierarchy();
                m_PreviewElement = null;
            }
            columnLayout?.EndDragResize(m_Column, cancelled);
            m_Resizing = false;
        }
    }
}
