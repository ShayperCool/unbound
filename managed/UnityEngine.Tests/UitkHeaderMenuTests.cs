using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Internal;
using Unbound.UIElements;
using Xunit;

// Contextual menus, sibling order and the multi-column header's menu and column dragging. The
// probe (tests/projects/uitkcollections: sibling-order, menu-api, manipulator, header-menu and
// header-drag sections) records the Editor's behaviour on a runtime panel; these tests check the
// same rules outside its setup: a panel that has a contextual menu manager, other column widths
// with a stretchable column, other sibling counts.
namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkHeaderMenuTests
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
            for (int i = 0; i < 3; i++)
            {
                panel.uaScheduler.UpdateScheduledEvents();
                panel.ApplyStyles();
                panel.ValidateLayout();
            }
        }

        static void Pointer(Panel panel, EventType type, Vector2 position, int button = 0)
        {
            var imgui = new Event { type = type, mousePosition = position, button = button, clickCount = type == EventType.MouseMove ? 0 : 1 };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui)
                : type == EventType.MouseUp ? PointerUpEvent.GetPooled(imgui)
                : PointerMoveEvent.GetPooled(imgui);
            using (e) panel.visualTree.SendEvent(e);
            Layout(panel);
        }

        static void Drag(Panel panel, Vector2 from, Vector2 to)
        {
            Pointer(panel, EventType.MouseMove, from);
            Pointer(panel, EventType.MouseDown, from);
            Pointer(panel, EventType.MouseMove, Vector2.Lerp(from, to, 0.5f));
            Pointer(panel, EventType.MouseMove, to);
            Pointer(panel, EventType.MouseUp, to);
        }

        static string Names(VisualElement parent) => string.Join("", parent.Children().Select(c => c.name));

        static VisualElement Siblings(string names)
        {
            var parent = new VisualElement();
            foreach (char c in names) parent.Add(new VisualElement { name = c.ToString() });
            return parent;
        }

        [Fact]
        public void PlaceBehindPutsTheElementRightBeforeItsSibling()
        {
            var p = Siblings("abcd");
            p[0].PlaceBehind(p[2]); // forward: the sibling's index shifts as the element leaves
            Assert.Equal("bacd", Names(p));
            p[3].PlaceBehind(p[0]); // backward
            Assert.Equal("dbac", Names(p));
            p[0].PlaceBehind(p[3]); // to just before the last
            Assert.Equal("badc", Names(p));
            Assert.Throws<ArgumentException>(() => p[0].PlaceBehind(new VisualElement()));
        }

        [Fact]
        public void PlaceInFrontPutsTheElementRightAfterItsSibling()
        {
            var p = Siblings("abcd");
            p[0].PlaceInFront(p[2]);
            Assert.Equal("bcad", Names(p));
            p[3].PlaceInFront(p[0]);
            Assert.Equal("bdca", Names(p));
            p[0].PlaceInFront(p[3]); // after the last: last
            Assert.Equal("dcab", Names(p));
            p[1].BringToFront();
            Assert.Equal("dabc", Names(p));
            p[2].SendToBack();
            Assert.Equal("bdac", Names(p));
        }

        [Fact]
        public void MovedSiblingsAreLaidOutInTheirNewOrder()
        {
            var panel = NewPanel();
            var p = Siblings("abc");
            p.style.flexDirection = FlexDirection.Row;
            foreach (var c in p.Children()) c.style.width = 10;
            panel.visualTree.Add(p);
            Layout(panel);
            p[0].PlaceInFront(p[1]);
            Layout(panel);
            Assert.Equal(new[] { 0f, 10f, 20f }, new[] { p.Q("b").layout.x, p.Q("a").layout.x, p.Q("c").layout.x });
        }

        static string Items(DropdownMenu menu) => string.Join(" ", menu.MenuItems().Select(i =>
            i is DropdownMenuAction a ? a.name + ":" + (int)a.status : "--" + ((DropdownMenuSeparator)i).subMenuPath));

        [Fact]
        public void SeparatorsNeedAnActionBeforeThemAndAreNotDoubled()
        {
            var menu = new DropdownMenu();
            menu.AppendSeparator();
            Assert.Empty(menu.MenuItems());
            menu.AppendAction("Edit/Copy", null);
            menu.AppendSeparator("File/"); // no action of that sub-menu
            menu.AppendSeparator("Edit/");
            menu.AppendSeparator("Edit/");
            menu.AppendSeparator(); // another sub-menu path: kept
            Assert.Equal("Edit/Copy:0 --Edit/ --", Items(menu));
            menu.InsertSeparator("x", 1); // after an action
            menu.InsertSeparator("y", 2); // after a separator: refused
            menu.InsertSeparator("z", 0);
            Assert.Equal("Edit/Copy:0 --x --Edit/ --", Items(menu));
        }

        [Fact]
        public void PrepareForDisplayAsksEveryStatusAndDropsATrailingSeparator()
        {
            var menu = new DropdownMenu();
            bool on = false;
            int executed = 0;
            menu.AppendAction("Toggle", a => { on = !on; executed++; }, a => on ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal, "data");
            menu.AppendAction("Off", null, DropdownMenuAction.Status.Disabled);
            menu.AppendAction("NoStatus", null, (Func<DropdownMenuAction, DropdownMenuAction.Status>)null);
            menu.AppendSeparator();
            menu.PrepareForDisplay(null);
            Assert.Equal("Toggle:1 Off:2 NoStatus:8", Items(menu));
            var toggle = (DropdownMenuAction)menu.MenuItems()[0];
            Assert.Equal("data", toggle.userData);
            Assert.Null(toggle.eventInfo);
            toggle.Execute();
            ((DropdownMenuAction)menu.MenuItems()[2]).Execute(); // no callback: nothing
            Assert.Equal(1, executed);
            // The status is what the last preparation saw, with that event's data.
            Assert.Equal(DropdownMenuAction.Status.Normal, toggle.status);
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = new Vector2(7, 9), button = 1, modifiers = EventModifiers.Alt }))
                menu.PrepareForDisplay(up);
            Assert.Equal(DropdownMenuAction.Status.Checked, toggle.status);
            Assert.Equal(EventModifiers.Alt, toggle.eventInfo.modifiers);
            Assert.Equal(new Vector2(7, 9), toggle.eventInfo.mousePosition);
        }

        sealed class RecordingMenuManager : ContextualMenuManager
        {
            public readonly List<string> Shown = new List<string>();

            public override void DisplayMenuIfEventMatches(EventBase evt, IEventHandler eventHandler) { }

            protected internal override void DoDisplayMenu(DropdownMenu menu, EventBase triggerEvent) =>
                Shown.Add(Items(menu) + " by " + triggerEvent.GetType().Name);
        }

        static (Panel panel, VisualElement box, List<string> log) MenuBox(ContextualMenuManager manager)
        {
            var panel = NewPanel();
            panel.contextualMenuManager = manager;
            var log = new List<string>();
            var box = new VisualElement { name = "box", focusable = true };
            box.style.width = 100;
            box.style.height = 50;
            box.AddManipulator(new ContextualMenuManipulator(e =>
            {
                log.Add("populate button " + e.button + " at " + e.mousePosition);
                e.menu.AppendAction("Hello", null);
                e.menu.AppendSeparator();
            }));
            panel.visualTree.RegisterCallback<PointerUpEvent>(e => log.Add("root up " + e.button));
            panel.visualTree.Add(box);
            Layout(panel);
            return (panel, box, log);
        }

        [Fact]
        public void ARightReleaseShowsThePopulatedMenuThroughThePanelsManager()
        {
            var manager = new RecordingMenuManager();
            var (panel, box, log) = MenuBox(manager);
            Pointer(panel, EventType.MouseDown, new Vector2(20, 10), 1);
            Assert.Empty(log);
            Pointer(panel, EventType.MouseUp, new Vector2(20, 10), 1);
            // The release is consumed; the menu is prepared (statuses asked, trailing separator dropped).
            Assert.Equal(new[] { "populate button 1 at (20.00, 10.00)" }, log);
            Assert.Equal(new[] { "Hello:1 by PointerUpEvent" }, manager.Shown);
            // The left button does not ask for a menu.
            log.Clear();
            Pointer(panel, EventType.MouseDown, new Vector2(20, 10));
            Pointer(panel, EventType.MouseUp, new Vector2(20, 10));
            Assert.Equal(new[] { "root up 0" }, log);
            Assert.Single(manager.Shown);
        }

        [Fact]
        public void TheMenuKeyAsksForAMenuAndAnEmptyMenuIsNotShown()
        {
            var manager = new RecordingMenuManager();
            var (panel, box, log) = MenuBox(manager);
            using (var up = KeyUpEvent.GetPooled('\0', KeyCode.Menu, EventModifiers.None)) { up.target = box; box.SendEvent(up); }
            Assert.Equal(new[] { "Hello:1 by KeyUpEvent" }, manager.Shown);
            var empty = new VisualElement();
            empty.AddManipulator(new ContextualMenuManipulator(null));
            panel.visualTree.Add(empty);
            manager.DisplayMenu(null, empty);
            Assert.Single(manager.Shown);
        }

        [Fact]
        public void WithoutAManagerNothingIsAskedAndTheReleaseGoesOn()
        {
            var (panel, box, log) = MenuBox(null);
            Pointer(panel, EventType.MouseDown, new Vector2(20, 10), 1);
            Pointer(panel, EventType.MouseUp, new Vector2(20, 10), 1);
            Assert.Equal(new[] { "root up 1" }, log);
        }

        sealed class HeaderFixture
        {
            public readonly Panel Panel = NewPanel();
            public readonly MultiColumnListView View;
            public readonly List<string> Log = new List<string>();

            public HeaderFixture(ContextualMenuManager manager = null)
            {
                Panel.contextualMenuManager = manager;
                var columns = new Columns();
                columns.Add(new Column { name = "name", title = "Name", width = 80, stretchable = true });
                columns.Add(new Column { name = "value", width = 60 });
                columns.Add(new Column { name = "flag", title = "Flag", width = 40, optional = false });
                View = new MultiColumnListView(columns) { fixedItemHeight = 20, itemsSource = Enumerable.Range(0, 5).ToList() };
                foreach (var column in columns)
                {
                    string tag = column.name;
                    column.makeCell = () => new Label();
                    column.bindCell = (e, i) => ((Label)e).text = tag + i;
                    column.comparison = (a, b) => b.CompareTo(a);
                }
                View.style.position = Position.Absolute;
                View.style.width = 280;
                View.style.height = 120;
                View.columnSortingChanged += () => Log.Add("sorting");
                // The header's layout, which the theme gives it on a runtime panel.
                var sheet = ScriptableObject.CreateInstance<StyleSheet>();
                new UssImporter("header.uss").Import(sheet,
                    ".unity-multi-column-header { flex-direction: row; height: 20px; } " +
                    ".unity-multi-column-header__column-container { flex-direction: row; } " +
                    ".unity-multi-column-header__column { height: 20px; } " +
                    ".unity-multi-column-header__column__move-preview { position: absolute; } " +
                    ".unity-multi-column-header__column__move-location-preview { position: absolute; }");
                View.styleSheets.Add(sheet);
                Panel.visualTree.Add(View);
                Layout(Panel);
            }

            public VisualElement Header => View.Q(className: MultiColumnCollectionHeader.ussClassName);

            public VisualElement Column(string name) => View.Q(name, MultiColumnHeaderColumn.ussClassName);

            public string Order => string.Join(",", Header.Query(className: MultiColumnHeaderColumn.ussClassName).ToList().Select(c => c.name));

            public string Cells => string.Join(",", View.GetRootElementForIndex(0).Query<Label>().ToList().Select(l => l.text));
        }

        [Fact]
        public void DraggingAHeaderColumnMovesItWhereThePointerIs()
        {
            var f = new HeaderFixture();
            // name stretches to 180: the flag column dragged into its left half goes first.
            Drag(f.Panel, f.Column("flag").worldBound.center, new Vector2(f.Column("name").worldBound.xMin + 20, 10));
            Assert.Equal("flag,name,value", f.Order);
            Assert.Equal("flag0,name0,value0", f.Cells);
            // Into the right half of the last column: after it.
            Drag(f.Panel, f.Column("flag").worldBound.center, new Vector2(f.Column("value").worldBound.xMax - 5, 10));
            Assert.Equal("name,value,flag", f.Order);
            // The header columns keep their widths and the handles follow them.
            Assert.Equal(new[] { 180f, 60f, 40f }, f.View.columns.visibleColumns.Select(c => c.desiredWidth));
            Assert.Null(f.View.Q(className: MultiColumnHeaderColumnMovePreview.ussClassName));
            Assert.Null(f.View.Q(className: MultiColumnHeaderColumnMoveLocationPreview.ussClassName));
        }

        [Fact]
        public void AReleaseAfterADragIsNotAClickAndAShortMoveIs()
        {
            var f = new HeaderFixture();
            f.View.sortingMode = ColumnSortingMode.Default;
            Layout(f.Panel);
            var from = f.Column("value").worldBound.center;
            Drag(f.Panel, from, from + new Vector2(5, 0)); // within the start distance: a click
            Assert.Equal(new[] { "sorting" }, f.Log);
            Assert.Equal("name,value,flag", f.Order);
            f.Log.Clear();
            from = f.Column("value").worldBound.center;
            Drag(f.Panel, from, from + new Vector2(6, 0)); // a drag that ends where it began
            Assert.Empty(f.Log);
            Assert.Equal("name,value,flag", f.Order);
        }

        [Fact]
        public void EscapeEndsADragWithoutMovingAndColumnsThatAreNotReorderableStay()
        {
            var f = new HeaderFixture();
            var column = f.Column("flag");
            var from = column.worldBound.center;
            Pointer(f.Panel, EventType.MouseMove, from);
            Pointer(f.Panel, EventType.MouseDown, from);
            Pointer(f.Panel, EventType.MouseMove, new Vector2(10, 10));
            Assert.True(column.ClassListContains(MultiColumnHeaderColumn.movingUssClassName));
            Assert.NotNull(f.View.Q(className: MultiColumnHeaderColumnMoveLocationPreview.ussClassName));
            using (var down = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) { down.target = column; column.SendEvent(down); }
            Assert.False(column.ClassListContains(MultiColumnHeaderColumn.movingUssClassName));
            Pointer(f.Panel, EventType.MouseMove, new Vector2(20, 10));
            Pointer(f.Panel, EventType.MouseUp, new Vector2(20, 10));
            Assert.Equal("name,value,flag", f.Order);

            f.View.columns.reorderable = false;
            Drag(f.Panel, f.Column("flag").worldBound.center, new Vector2(10, 10));
            Assert.Equal("name,value,flag", f.Order);
            Assert.Null(f.Panel.GetCapturingElement(PointerId.mousePointerId));
        }

        [Fact]
        public void AHiddenColumnKeepsItsPlaceWhenOthersMoveAroundIt()
        {
            var f = new HeaderFixture();
            f.View.columns["value"].visible = false;
            Layout(f.Panel);
            Drag(f.Panel, f.Column("name").worldBound.center, new Vector2(f.Column("flag").worldBound.xMax - 5, 10));
            Assert.Equal("flag,name", f.Order);
            f.View.columns["value"].visible = true;
            Layout(f.Panel);
            Assert.Equal("value,flag,name", f.Order);
        }

        [Fact]
        public void TheHeaderFillsItsMenuAndTheViewsHandlersSeeTheColumnUnderThePointer()
        {
            var manager = new RecordingMenuManager();
            var f = new HeaderFixture(manager);
            f.View.headerContextMenuPopulateEvent += (e, column) =>
            {
                f.Log.Add("column " + (column?.name ?? "none"));
                e.menu.AppendAction("Mine", null);
            };
            var at = f.Column("value").worldBound.center;
            Pointer(f.Panel, EventType.MouseDown, at, 1);
            Pointer(f.Panel, EventType.MouseUp, at, 1);
            Assert.Equal(new[] { "column value" }, f.Log);
            // A stretchable column in GrowAndFill mode already fits; a column without a title
            // shows its name; one that is not optional cannot be hidden.
            Assert.Equal(new[] { "Resize To Fit:2 -- Name:4 value:4 Flag:2 Mine:1 by PointerUpEvent" }, manager.Shown);
        }

        [Fact]
        public void MenuEntriesShowAndHideColumnsAndResizeToFit()
        {
            var f = new HeaderFixture();
            f.View.columns["name"].stretchable = false;
            Layout(f.Panel);
            var menu = new DropdownMenu();
            using (var e = ContextualMenuPopulateEvent.GetPooled(null, menu, f.Header, null)) f.Header.SendEvent(e);
            menu.PrepareForDisplay(null);
            Assert.Equal("Resize To Fit:1 -- Name:4 value:4 Flag:2", Items(menu));
            ((DropdownMenuAction)menu.MenuItems()[3]).Execute();
            Layout(f.Panel);
            Assert.Equal("name,flag", f.Order);
            menu.PrepareForDisplay(null);
            Assert.Equal(DropdownMenuAction.Status.Normal, ((DropdownMenuAction)menu.MenuItems()[3]).status);
            // 80 + 40 in a header of 280: fitting grows the columns to fill it.
            ((DropdownMenuAction)menu.MenuItems()[0]).Execute();
            Layout(f.Panel);
            Assert.Equal(f.Header.layout.width, f.View.columns.visibleColumns.Sum(c => c.desiredWidth), 3);
            // The named primary column cannot be hidden.
            f.View.columns.primaryColumnName = "name";
            menu.PrepareForDisplay(null);
            Assert.Equal(DropdownMenuAction.Status.Disabled, ((DropdownMenuAction)menu.MenuItems()[2]).status);
        }
    }
}
