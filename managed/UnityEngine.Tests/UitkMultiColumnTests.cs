using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Internal;
using Xunit;

// MultiColumnListView, MultiColumnTreeView and their column layout. The probe
// (tests/projects/uitkcollections, columns and column-tree sections) records the Editor's behaviour;
// these tests check the same rules on cases outside its setup: other widths, the Grow mode, tree
// data added and removed, sorting by several columns.
namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkMultiColumnTests
    {
        static Panel NewPanel()
        {
            var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            panel.visualTree.style.width = 400;
            panel.visualTree.style.height = 300;
            return panel;
        }

        static void Layout(Panel panel)
        {
            // Scheduled items (the header's layout, rebuilds), styles and layout.
            for (int i = 0; i < 3; i++)
            {
                panel.uaScheduler.UpdateScheduledEvents();
                panel.ApplyStyles();
                panel.ValidateLayout();
            }
        }

        static Columns ThreeColumns(Columns.StretchMode mode = Columns.StretchMode.GrowAndFill)
        {
            var columns = new Columns { stretchMode = mode };
            columns.Add(new Column { name = "name", width = 80, stretchable = true });
            columns.Add(new Column { name = "value", width = 60 });
            columns.Add(new Column { name = "flag", width = 40 });
            return columns;
        }

        static float[] Widths(Columns columns) => columns.visibleColumns.Select(c => c.desiredWidth).ToArray();

        [Fact]
        public void StretchableColumnsFillTheHeader()
        {
            var columns = ThreeColumns();
            var layout = new ColumnLayout(columns);
            layout.DoLayout(256);
            Assert.Equal(new[] { 156f, 60f, 40f }, Widths(columns));
            // A wider header grows the stretchable column only.
            layout.DoLayout(300);
            Assert.Equal(new[] { 200f, 60f, 40f }, Widths(columns));
            // Narrower than the fixed columns: the stretchable one stops at its minimum width.
            layout.DoLayout(120);
            Assert.Equal(Column.kDefaultMinWidth, columns["name"].desiredWidth);
        }

        [Fact]
        public void GrowModeKeepsPreferredWidthsUntilTheHeaderGrows()
        {
            var columns = ThreeColumns(Columns.StretchMode.Grow);
            var layout = new ColumnLayout(columns);
            layout.DoLayout(256);
            Assert.Equal(new[] { 80f, 60f, 40f }, Widths(columns));
            layout.DoLayout(300);
            Assert.Equal(new[] { 124f, 60f, 40f }, Widths(columns));
        }

        [Fact]
        public void HiddenColumnsTakeNoWidthAndANewWidthIsLaidOutAgain()
        {
            var columns = ThreeColumns();
            var layout = new ColumnLayout(columns);
            layout.DoLayout(256);
            columns["value"].visible = false;
            Assert.True(layout.isDirty);
            layout.DoLayout(256);
            Assert.Equal(new[] { 216f, 40f }, Widths(columns));
            columns["flag"].width = 70;
            Assert.True(float.IsNaN(columns["flag"].desiredWidth));
            layout.DoLayout(256);
            Assert.Equal(new[] { 186f, 70f }, Widths(columns));
        }

        sealed class ListFixture
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<int> Items = Enumerable.Range(0, 20).ToList();
            public readonly Panel Panel = NewPanel();
            public readonly MultiColumnListView View;

            public ListFixture()
            {
                View = new MultiColumnListView(ThreeColumns()) { fixedItemHeight = 20 };
                View.style.position = Position.Absolute;
                View.style.width = 280;
                View.style.height = 120;
                foreach (var column in View.columns)
                {
                    string tag = column.name;
                    column.makeCell = () => { Log.Add(tag + ":make"); return new Label(); };
                    column.bindCell = (e, i) => { Log.Add(tag + ":bind " + i); ((Label)e).text = tag + i; };
                    column.unbindCell = (e, i) => Log.Add(tag + ":unbind " + i);
                }
                View.columns["value"].comparison = (a, b) => Items[a].CompareTo(Items[b]);
                View.columns["flag"].comparison = (a, b) => (Items[a] % 2).CompareTo(Items[b] % 2);
                View.itemsSource = Items;
                Panel.visualTree.Add(View);
                Layout(Panel);
            }

            public VisualElement Row(int index) => View.GetRootElementForIndex(index);

            public string FirstCellText(int index) => ((Label)Row(index)[0][0]).text;
        }

        [Fact]
        public void RowsHoldACellPerVisibleColumnMadeThenBound()
        {
            var f = new ListFixture();
            var row = f.Row(0);
            Assert.Equal(MultiColumnController.rowContainerUssClassName, row.name);
            Assert.Equal(3, row.childCount);
            Assert.All(row.Children(), cell => Assert.Contains(MultiColumnController.cellUssClassName, cell.GetClasses()));
            Assert.Equal(new[] { "name:make", "value:make", "flag:make", "name:bind 0", "value:bind 0", "flag:bind 0" }, f.Log.Take(6));
            // Hiding a column rebuilds the rows with one cell less.
            f.View.columns["value"].visible = false;
            Layout(f.Panel);
            Assert.Equal(2, f.Row(0).childCount);
            Assert.Equal("name0", f.FirstCellText(0));
        }

        [Fact]
        public void DefaultSortingOrdersRowsThroughTheComparisons()
        {
            var f = new ListFixture();
            f.View.sortingMode = ColumnSortingMode.Default;
            int changes = 0;
            f.View.columnSortingChanged += () => changes++;
            f.View.sortColumnDescriptions.Add(new SortColumnDescription("value", SortDirection.Descending));
            Layout(f.Panel);
            Assert.Equal(1, changes);
            Assert.Equal("name19", f.FirstCellText(0));
            Assert.Equal(19, f.View.viewController.columnController.GetSourceIndex(0));
            Assert.Equal(0, f.View.viewController.columnController.GetSortedIndex(19));
            // Two sorted columns: flag first (even before odd), then value descending among equals.
            f.View.sortColumnDescriptions.Insert(0, new SortColumnDescription("flag", SortDirection.Ascending));
            Layout(f.Panel);
            Assert.Equal(2, changes);
            Assert.Equal(new[] { "name18", "name16", "name14" }, Enumerable.Range(0, 3).Select(f.FirstCellText));
            var header = f.View.viewController.header;
            Assert.Equal("1", header.columnDataMap[f.View.columns["flag"]].control.sortOrderLabel);
            Assert.Equal("2", header.columnDataMap[f.View.columns["value"]].control.sortOrderLabel);
            // Sorting off: source order again, and no description is matched.
            f.View.sortingMode = ColumnSortingMode.None;
            Layout(f.Panel);
            Assert.Equal(3, changes);
            Assert.Empty(f.View.sortedColumns);
        }

        [Fact]
        public void CustomSortingLeavesTheOrderToTheGame()
        {
            var f = new ListFixture();
            f.View.sortingMode = ColumnSortingMode.Custom;
            f.View.sortColumnDescriptions.Add(new SortColumnDescription("value", SortDirection.Descending));
            Layout(f.Panel);
            Assert.Single(f.View.sortedColumns);
            Assert.Equal("name0", f.FirstCellText(0));
        }

        static MultiColumnTreeView NewTree(Panel panel)
        {
            var columns = new Columns();
            columns.Add(new Column { name = "name", width = 100 });
            MultiColumnTreeView view = null;
            columns["name"].makeCell = () => new Label();
            columns["name"].bindCell = (e, i) => ((Label)e).text = view.GetItemDataForIndex<string>(i);
            view = new MultiColumnTreeView(columns) { fixedItemHeight = 20 };
            view.style.position = Position.Absolute;
            view.style.width = 200;
            view.style.height = 200;
            view.SetRootItems(new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(1, "A", new List<TreeViewItemData<string>> { new TreeViewItemData<string>(11, "A1"), new TreeViewItemData<string>(12, "A2") }),
                new TreeViewItemData<string>(2, "B"),
            });
            panel.visualTree.Add(view);
            Layout(panel);
            return view;
        }

        [Fact]
        public void TreesSortItemsAmongTheirSiblings()
        {
            var panel = NewPanel();
            var view = NewTree(panel);
            view.ExpandAll();
            view.columns["name"].comparison = (a, b) => view.GetIdForIndex(a).CompareTo(view.GetIdForIndex(b));
            view.sortingMode = ColumnSortingMode.Default;
            view.sortColumnDescriptions.Add(new SortColumnDescription("name", SortDirection.Descending));
            Layout(panel);
            var texts = Enumerable.Range(0, 4).Select(i => ((Label)view.GetRootElementForIndex(i).Q<Label>()).text).ToArray();
            Assert.Equal(new[] { "B", "A", "A2", "A1" }, texts);
        }

        [Fact]
        public void TreeDataIsAddedAndRemovedAsInTheSingleColumnTree()
        {
            var panel = NewPanel();
            var view = NewTree(panel);
            view.AddItem(new TreeViewItemData<string>(3, "C"), -1, -1, true);
            view.AddItem(new TreeViewItemData<string>(21, "B1"), 2, -1, true);
            Assert.Equal(2, view.viewController.GetParentId(21));
            Assert.Equal("B1", view.GetItemDataForId<string>(21));
            Assert.True(view.viewController.TryRemoveItem(1));
            Assert.Equal(new[] { 2, 3 }, view.viewController.GetRootItemIds());
            Assert.Equal(-1, view.viewController.GetIndexForId(11));
        }
    }
}
