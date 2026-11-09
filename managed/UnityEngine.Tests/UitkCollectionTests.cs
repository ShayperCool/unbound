using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements.Layout;
using Unbound.UIElements;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using Xunit;

// Collection controls and the layout rules they measured. Each test names its source:
//   [measured] the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (the step named);
//   [rule]     the same rule applied to a case the capture does not exercise.

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkCollectionTests
    {
        const float RowHeight = 20f;

        static Panel NewPanel(float width = 400, float height = 300)
        {
            var panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);
            panel.visualTree.style.width = width;
            panel.visualTree.style.height = height;
            return panel;
        }

        static void Layout(Panel panel)
        {
            for (int i = 0; i < 3; i++)
            {
                panel.uaValueAnimations.Update();
                panel.ApplyStyles();
                panel.ValidateLayout();
            }
        }

        static void Pointer(Panel panel, EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None)
        {
            var imgui = new Event { type = type, mousePosition = position, button = 0, modifiers = modifiers, clickCount = type == EventType.MouseMove ? 0 : 1 };
            EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui)
                : type == EventType.MouseUp ? PointerUpEvent.GetPooled(imgui)
                : PointerMoveEvent.GetPooled(imgui);
            using (e) panel.visualTree.SendEvent(e);
            Layout(panel);
        }

        static void Click(Panel panel, VisualElement target)
        {
            var at = target.worldBound.center;
            Pointer(panel, EventType.MouseMove, at);
            Pointer(panel, EventType.MouseDown, at);
            Pointer(panel, EventType.MouseUp, at);
        }

        static void Drag(Panel panel, Vector2 from, Vector2 to, int steps)
        {
            Pointer(panel, EventType.MouseMove, from);
            Pointer(panel, EventType.MouseDown, from);
            for (int s = 1; s <= steps; s++) Pointer(panel, EventType.MouseMove, Vector2.Lerp(from, to, s / (float)steps));
            Pointer(panel, EventType.MouseUp, to);
        }

        sealed class Recorder
        {
            public readonly List<string> Log = new List<string>();
            int m_Next;

            public ListView List(List<string> items, Action<ListView> setup = null)
            {
                var list = new ListView { fixedItemHeight = RowHeight };
                list.style.position = Position.Absolute;
                list.style.width = 150;
                list.style.height = 200;
                setup?.Invoke(list);
                list.makeItem = () => { var l = new Label { userData = ++m_Next }; Log.Add("make e" + m_Next); return l; };
                list.bindItem = (e, i) => { Log.Add($"bind e{e.userData} {i}"); ((Label)e).text = items[i]; };
                list.unbindItem = (e, i) => Log.Add($"unbind e{e.userData} {i}");
                list.destroyItem = e => Log.Add($"destroy e{e.userData}");
                list.itemIndexChanged += (a, b) => Log.Add($"itemIndexChanged {a} {b}");
                list.itemsAdded += added => Log.Add("itemsAdded " + string.Join(",", added));
                list.itemsRemoved += removed => Log.Add("itemsRemoved " + string.Join(",", removed));
                list.itemsSource = items;
                return list;
            }
        }

        static Vector2 RowCenter(BaseVerticalCollectionView view, int index) => view.GetRootElementForIndex(index).worldBound.center;

        static List<string> Items(int count) => Enumerable.Range(0, count).Select(i => "Row " + i).ToList();

        // ------------------------------------------------------------------ ObjectPool

        // [measured] fixed/rebuild destroys the rows released first-to-last except the first, which
        // is destroyed last: the pool keeps the first released object in its own slot.
        // [rule] Get returns that slot first, then the stack top; Clear destroys the stack bottom to
        // top, then the slot.
        [Fact]
        public void ObjectPoolKeepsTheFreshlyReleasedObjectInItsOwnSlot()
        {
            int next = 0;
            var destroyed = new List<string>();
            var pool = new UnityEngine.Pool.ObjectPool<string>(() => "n" + next++, actionOnDestroy: s => destroyed.Add(s));
            var a = pool.Get(); var b = pool.Get(); var c = pool.Get();
            pool.Release(a); pool.Release(b); pool.Release(c);
            Assert.Equal(3, pool.CountInactive);
            Assert.Throws<InvalidOperationException>(() => { pool.Release(a); });
            Assert.Same(a, pool.Get());
            Assert.Same(c, pool.Get());
            pool.Release(a);
            pool.Release(c);
            Assert.Equal(3, pool.CountInactive);
            pool.Clear();
            Assert.Equal(new List<string> { b, c, a }, destroyed);
            Assert.Equal(0, pool.CountInactive);
        }

        // [measured] fixed/rebuild: unbind in row order, then destroy every row except the first,
        // which is destroyed last, then make and bind new rows.
        [Fact]
        public void RebuildDestroysTheFirstReleasedRowLast()
        {
            using var panel = NewPanel();
            var r = new Recorder();
            var list = r.List(Items(3));
            panel.visualTree.Add(list);
            Layout(panel);
            r.Log.Clear();
            list.Rebuild();
            Assert.Equal(new[] { "unbind e1 0", "unbind e2 1", "unbind e3 2", "destroy e2", "destroy e3", "destroy e1",
                "make e4", "bind e4 0", "make e5", "bind e5 1", "make e6", "bind e6 2" }, r.Log);
        }

        // ------------------------------------------------------------------ footer and header

        // [measured] footer/click-add, select-0-click-remove, click-remove-no-selection: "+" adds
        // one item (itemsAdded), selects it; "-" removes the selection (then none is selected), or
        // the last item without a selection.
        [Fact]
        public void FooterButtonsAddAndRemoveItems()
        {
            using var panel = NewPanel();
            var items = new List<string> { "One", "Two", "Three" };
            var r = new Recorder();
            var list = r.List(items, l => { l.showAddRemoveFooter = true; });
            panel.visualTree.Add(list);
            Layout(panel);
            var footer = list.Q(BaseListView.footerUssClassName);
            Assert.NotNull(footer);
            Assert.True(list.ClassListContains(BaseListView.listViewWithFooterUssClassName));
            var add = list.Q<Button>(BaseListView.footerAddButtonName);
            var remove = list.Q<Button>(BaseListView.footerRemoveButtonName);
            Assert.Equal("+", add.text);
            Assert.Equal("-", remove.text);

            // Without the runtime theme the buttons have no height: give them one to press them.
            add.style.height = 16;
            remove.style.height = 16;
            Layout(panel);
            r.Log.Clear();
            Click(panel, add);
            Assert.Equal(4, items.Count);
            Assert.Null(items[3]);
            Assert.Contains("itemsAdded 3", r.Log);
            Assert.Equal(new[] { 3 }, list.selectedIndices);

            list.SetSelection(0);
            Layout(panel);
            Click(panel, remove);
            Assert.Equal(new[] { "Two", "Three", null }, items);
            Assert.Empty(list.selectedIndices);

            Click(panel, remove);
            Assert.Equal(new[] { "Two", "Three" }, items);

            list.showAddRemoveFooter = false;
            Assert.Null(list.Q(BaseListView.footerUssClassName));
            Assert.False(list.showAddRemoveFooter);
        }

        // [measured] footer/attach, collapse-header, size-field-5: the foldout header (named after
        // its class, titled headerTitle) holds the scroll view and footer; the size field beside it
        // shows the count; collapsing it unbinds the rows; entering a larger size adds items.
        [Fact]
        public void FoldoutHeaderHoldsTheListAndTheSizeField()
        {
            using var panel = NewPanel();
            var items = new List<string> { "One", "Two" };
            var r = new Recorder();
            var list = r.List(items, l => { l.showFoldoutHeader = true; l.headerTitle = "Things"; l.showAddRemoveFooter = true; });
            panel.visualTree.Add(list);
            Layout(panel);
            var foldout = list.Q<Foldout>(BaseListView.foldoutHeaderUssClassName);
            Assert.Equal("Things", foldout.text);
            Assert.Same(foldout.contentContainer, list.Q<ScrollView>().parent);
            Assert.Same(foldout.contentContainer, list.Q(BaseListView.footerUssClassName).parent);
            var size = list.Q<TextField>(BaseListView.arraySizeFieldUssClassName);
            Assert.Equal("2", size.value);
            Assert.True(size.ClassListContains(BaseListView.arraySizeFieldWithHeaderUssClassName));

            r.Log.Clear();
            foldout.value = false;
            Layout(panel);
            Assert.Equal(new[] { "unbind e2 1", "unbind e1 0" }, r.Log.Where(l => l.StartsWith("unbind")).ToArray());

            foldout.value = true;
            Layout(panel);
            r.Log.Clear();
            size.value = "4";
            Assert.Equal(4, items.Count);
            Assert.Contains("itemsAdded 2,3", r.Log);
            Layout(panel);
            Assert.Equal("4", size.value);

            list.showFoldoutHeader = false;
            Assert.Null(list.Q<Foldout>());
            Assert.Same(list, list.Q<ScrollView>().hierarchy.parent);
            Assert.Null(size.parent);
        }

        // ------------------------------------------------------------------ reordering

        // [measured] reorder/drag-1-to-5, drag-6-to-0, small-move-no-drag: a drag of more than 10
        // points moves the pressed row (selected first) to the drop position between rows and sends
        // itemIndexChanged; a shorter move is a click.
        [Fact]
        public void DraggingARowReordersTheList()
        {
            using var panel = NewPanel();
            var items = Items(8);
            var r = new Recorder();
            var list = r.List(items, l => { l.reorderable = true; });
            panel.visualTree.Add(list);
            Layout(panel);
            Assert.True(list.reorderable);

            r.Log.Clear();
            Drag(panel, RowCenter(list, 1), RowCenter(list, 4) + new Vector2(0, 6), 5);
            Assert.Contains("itemIndexChanged 1 4", r.Log);
            Assert.Equal(new[] { "Row 0", "Row 2", "Row 3", "Row 4", "Row 1", "Row 5", "Row 6", "Row 7" }, items);
            Assert.Equal(new[] { 4 }, list.selectedIndices);

            r.Log.Clear();
            var start = RowCenter(list, 2);
            Drag(panel, start, start + new Vector2(0, 2), 2);
            Assert.DoesNotContain(r.Log, l => l.StartsWith("itemIndexChanged"));
            Assert.Equal(new[] { 2 }, list.selectedIndices);
        }

        // [measured] reorder/drag-selection-to-9: a dragged selection moves as a block, last item
        // first, and stays selected (without a notification) at its new indices.
        // [rule] The same block dragged up.
        [Fact]
        public void DroppingASelectionMovesItAsABlock()
        {
            var items = Items(8);
            var list = new ListView { reorderable = true, selectionType = SelectionType.Multiple, itemsSource = items };
            var moves = new List<string>();
            int selectionEvents = 0;
            list.itemIndexChanged += (a, b) => moves.Add($"{a}>{b}");
            list.selectionChanged += _ => selectionEvents++;
            list.SetSelection(new[] { 2, 3 });
            selectionEvents = 0;
            var controller = (ListViewReorderableDragAndDropController)list.dragger.dragAndDropController;
            controller.SetupDragAndDrop(list.selectedIds);
            controller.OnDrop(new DragAndDropArgs { insertAtIndex = 6, dragAndDropPosition = DragAndDropPosition.BetweenItems });
            Assert.Equal(new[] { "Row 0", "Row 1", "Row 4", "Row 5", "Row 2", "Row 3", "Row 6", "Row 7" }, items);
            Assert.Equal(new[] { "3>5", "2>4" }, moves);
            Assert.Equal(new[] { 4, 5 }, list.selectedIndices);
            Assert.Equal(0, selectionEvents);

            moves.Clear();
            controller.SetupDragAndDrop(list.selectedIds);
            controller.OnDrop(new DragAndDropArgs { insertAtIndex = 1, dragAndDropPosition = DragAndDropPosition.BetweenItems });
            Assert.Equal(new[] { "Row 0", "Row 2", "Row 3", "Row 1", "Row 4", "Row 5", "Row 6", "Row 7" }, items);
            // Each move is from the item's index after the block's earlier moves (both from 5 here).
            Assert.Equal(new[] { "5>1", "5>1" }, moves);
            Assert.Equal(new[] { 1, 2 }, list.selectedIndices);
        }

        // [rule] Without reordering a drag does not move anything; a drop over a row is refused.
        [Fact]
        public void NoReorderingWithoutReorderableOrOverARow()
        {
            using var panel = NewPanel();
            var items = Items(6);
            var r = new Recorder();
            var list = r.List(items);
            panel.visualTree.Add(list);
            Layout(panel);
            Drag(panel, RowCenter(list, 1), RowCenter(list, 4) + new Vector2(0, 6), 5);
            Assert.Equal(Items(6), items);

            list.reorderable = true;
            Layout(panel);
            var controller = list.dragger.dragAndDropController;
            Assert.Equal(DragVisualMode.Rejected, controller.HandleDragAndDrop(new DragAndDropArgs { dragAndDropPosition = DragAndDropPosition.OverItem }));
        }

        // [measured] animated/attach: with animated reordering a row is a container holding a drag
        // handle (two bars) and an item container around the made element.
        [Fact]
        public void AnimatedReorderRowsHaveADragHandle()
        {
            using var panel = NewPanel();
            var r = new Recorder();
            var list = r.List(Items(3), l => { l.reorderable = true; l.reorderMode = ListViewReorderMode.Animated; });
            panel.visualTree.Add(list);
            Layout(panel);
            var row = list.GetRootElementForIndex(0);
            Assert.Equal(BaseListView.reorderableItemUssClassName, row.name);
            Assert.Equal(new[] { BaseListView.reorderableItemHandleUssClassName, BaseListView.reorderableItemContainerUssClassName },
                row.hierarchy.Children().Select(c => c.name).ToArray());
            Assert.Equal(2, row.hierarchy[0].hierarchy.childCount);
            Assert.IsType<Label>(row.hierarchy[1].hierarchy[0]);
        }

        // [rule] Animated reordering (tests/projects/uitkcollections animated section records the
        // fixed-height, unscrolled case): a drag of the last row (no row after it to open a gap) and a
        // drop in a dynamic-height list keep the rows and their heights.
        static ListView AnimatedList(Recorder r, List<string> items, Action<ListView> setup = null) =>
            r.List(items, l => { l.reorderable = true; l.reorderMode = ListViewReorderMode.Animated; setup?.Invoke(l); });

        [Fact]
        public void AnimatedDragOfTheLastRowMovesItUp()
        {
            using var panel = NewPanel();
            var items = Items(5);
            var r = new Recorder();
            var list = AnimatedList(r, items);
            panel.visualTree.Add(list);
            Layout(panel);
            Drag(panel, RowCenter(list, 4), RowCenter(list, 1) + new Vector2(0, -6), 6);
            Assert.Equal(new[] { "Row 0", "Row 4", "Row 1", "Row 2", "Row 3" }, items);
            Assert.Equal(new[] { 1 }, list.selectedIndices);
            // Once the gaps have closed (500 ms) the rows stand at their places again.
            System.Threading.Thread.Sleep(600);
            Settle(panel);
            Assert.All(Enumerable.Range(0, 5), i => Assert.Equal(i * RowHeight, list.GetRootElementForIndex(i).layout.y));
        }

        [Fact]
        public void AnimatedDropInADynamicHeightListKeepsTheRowHeights()
        {
            using var panel = NewPanel();
            var items = Items(6);
            var heights = new Dictionary<string, float> { ["Row 0"] = 16, ["Row 1"] = 24, ["Row 2"] = 32, ["Row 3"] = 16, ["Row 4"] = 24, ["Row 5"] = 32 };
            var r = new Recorder();
            var list = AnimatedList(r, items, l => l.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight);
            list.bindItem = (e, i) => { ((Label)e).text = items[i]; e.style.height = heights[items[i]]; };
            panel.visualTree.Add(list);
            Layout(panel);
            Layout(panel);
            Drag(panel, RowCenter(list, 0), RowCenter(list, 3) + new Vector2(0, 4), 6);
            // The gaps close over 500 ms; their rows report their heights again as they do.
            System.Threading.Thread.Sleep(600);
            Settle(panel);
            // Row 0 moved down past rows 1 and 2 (where exactly depends on how far the gaps opened
            // while dragging); the other rows keep their order.
            Assert.True(items.IndexOf("Row 0") >= 2);
            Assert.Equal(new[] { "Row 1", "Row 2", "Row 3", "Row 4", "Row 5" }, items.Where(i => i != "Row 0"));
            float expected = 0f;
            foreach (var item in items) expected += heights[item];
            Assert.Equal(expected, list.virtualizationController.GetExpectedContentHeight());
            for (int i = 0; i < items.Count; i++) Assert.Equal(heights[items[i]], list.virtualizationController.GetExpectedItemHeight(i));
        }

        // ------------------------------------------------------------------ dynamic height

        // [measured] dynamic/attach: each laid-out row registers its height; the smallest one becomes
        // the default for rows not laid out yet, which brings in more rows.
        [Fact]
        public void DynamicHeightRowsRegisterTheirHeights()
        {
            using var panel = NewPanel(400, 400);
            var items = Enumerable.Range(0, 60).ToList();
            var list = new ListView { virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight };
            list.style.position = Position.Absolute;
            list.style.width = 180;
            list.style.height = 150;
            list.makeItem = () => new Label();
            list.bindItem = (e, i) => { e.style.height = 16 + (items[i] % 5) * 8; };
            list.itemsSource = items;
            panel.visualTree.Add(list);
            for (int i = 0; i < 6; i++) Layout(panel);
            var controller = (DynamicHeightVirtualizationController<ReusableListViewItem>)list.virtualizationController;
            Assert.Equal(16f, controller.itemHeightCache[0]);
            Assert.Equal(24f, controller.itemHeightCache[1]);
            Assert.Equal(12, list.activeItems.Count());
        }

        static ListView DynamicList(List<int> items, Func<int, float> height)
        {
            var list = new ListView { virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight };
            list.style.position = Position.Absolute;
            list.style.width = 180;
            list.style.height = 150;
            list.makeItem = () => new Label();
            list.bindItem = (e, i) =>
            {
                float h = height(items[i]);
                if (h <= 0) e.style.display = DisplayStyle.None;
                else
                {
                    e.style.display = DisplayStyle.Flex;
                    e.style.height = h;
                }
            };
            list.itemsSource = items;
            return list;
        }

        static void Settle(Panel panel)
        {
            for (int i = 0; i < 6; i++) Layout(panel);
        }

        // [rule] Items removed while scrolled to the end, a null source and back, and a resize keep the
        // dynamic list consistent: no exception, no more shown rows than items, rows bound only to
        // existing items. (Where the scroll offset settles after the shrink is not measured.)
        [Fact]
        public void DynamicHeightSurvivesSourceChangesAndResizes()
        {
            using var panel = NewPanel(400, 400);
            var items = Enumerable.Range(0, 200).ToList();
            var list = DynamicList(items, i => 16 + (i % 5) * 8);
            panel.visualTree.Add(list);
            Settle(panel);
            list.ScrollToItem(-1);
            Settle(panel);
            var scroll = list.Q<ScrollView>();
            Assert.True(scroll.scrollOffset.y > 1000);

            items.RemoveRange(10, items.Count - 10);
            list.RefreshItems();
            Settle(panel);
            int shown = list.activeItems.Count(r => r.rootElement.resolvedStyle.display == DisplayStyle.Flex && r.index >= 0);
            Assert.InRange(shown, 1, 10);
            Assert.All(list.activeItems, r => Assert.True(r.index < 10));
            var cache = ((DynamicHeightVirtualizationController<ReusableListViewItem>)list.virtualizationController).itemHeightCache;
            Assert.All(cache.Keys, k => Assert.True(k < 10));
            float expected = 0;
            for (int i = 0; i < 10; i++) expected += cache.TryGetValue(i, out float h) ? h : list.virtualizationController.GetExpectedItemHeight(i);
            Assert.Equal(expected, list.virtualizationController.GetExpectedContentHeight());

            list.itemsSource = null;
            Settle(panel);
            Assert.Empty(list.activeItems.Where(r => r.index >= 0));
            list.itemsSource = items;
            Settle(panel);
            Assert.Equal(0, list.activeItems.First().index);

            list.style.height = 60;
            Settle(panel);
            int few = list.activeItems.Count(r => r.index >= 0 && r.rootElement.resolvedStyle.display == DisplayStyle.Flex);
            list.style.height = 300;
            Settle(panel);
            int many = list.activeItems.Count(r => r.index >= 0 && r.rootElement.resolvedStyle.display == DisplayStyle.Flex);
            Assert.True(few < many, $"{few} rows at 60, {many} at 300");
            Assert.Equal(10, many);
        }

        // [rule] A row bound hidden (no height) never reports a layout and keeps no registered height;
        // the rows around it register theirs and are shown.
        [Fact]
        public void DynamicHeightToleratesRowsWithoutHeight()
        {
            using var panel = NewPanel(400, 400);
            var items = Enumerable.Range(0, 30).ToList();
            var list = DynamicList(items, i => i == 2 ? 0 : 20);
            panel.visualTree.Add(list);
            Settle(panel);
            var controller = (DynamicHeightVirtualizationController<ReusableListViewItem>)list.virtualizationController;
            Assert.False(controller.itemHeightCache.ContainsKey(2));
            Assert.Equal(20f, controller.itemHeightCache[3]);
            Assert.Equal(40f, list.GetRootElementForIndex(3).layout.y);
        }

        // [rule] A TreeView with dynamic height rows uses the same virtualization: rows stack at their
        // own heights.
        [Fact]
        public void TreeViewWithDynamicHeightLaysOutItsRows()
        {
            using var panel = NewPanel(400, 400);
            var tree = new TreeView { virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight };
            tree.style.position = Position.Absolute;
            tree.style.width = 180;
            tree.style.height = 150;
            // The runtime theme keeps rows from shrinking; the test panel has no theme.
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssImporter("tree-rows.uss").Import(sheet, ".unity-tree-view__item { flex-shrink: 0; }");
            tree.styleSheets.Add(sheet);
            var roots = Enumerable.Range(0, 20).Select(i => new TreeViewItemData<int>(i, i)).ToList();
            tree.SetRootItems(roots);
            tree.makeItem = () => new Label();
            tree.bindItem = (e, i) => e.style.height = 18 + (i % 3) * 6;
            panel.visualTree.Add(tree);
            Settle(panel);
            float Top(int i) => tree.GetRootElementForIndex(i).worldBound.y;
            Assert.Equal(18f, Top(1) - Top(0));
            Assert.Equal(24f, Top(2) - Top(1));
            Assert.Equal(30f, Top(3) - Top(2));
        }

        // ------------------------------------------------------------------ layout events

        // [measured] fixed/new-makeitem, footer/no-footer: a parent's GeometryChangedEvent follows
        // its children's; a scroll view's content gets one when its children changed while its own
        // size did not.
        [Fact]
        public void GeometryEventsFollowChildrenAndReportContentChanges()
        {
            using var panel = NewPanel();
            var parent = new VisualElement();
            parent.style.width = 100;
            var child = new VisualElement();
            child.style.height = 10;
            parent.Add(child);
            var scroll = new ScrollView();
            scroll.style.height = 50;
            panel.visualTree.Add(parent);
            panel.visualTree.Add(scroll);
            Layout(panel);
            var order = new List<string>();
            parent.RegisterCallback<GeometryChangedEvent>(_ => order.Add("parent"));
            child.RegisterCallback<GeometryChangedEvent>(_ => order.Add("child"));
            scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(e => order.Add("content " + e.oldRect.size + ">" + e.newRect.size));
            parent.style.width = 120;
            Layout(panel);
            Assert.Equal(new[] { "child", "parent" }, order);

            var fixedSize = new VisualElement();
            fixedSize.style.position = Position.Absolute;
            fixedSize.style.width = 10;
            fixedSize.style.height = 10;
            scroll.contentContainer.style.height = 30;
            Layout(panel);
            order.Clear();
            scroll.Add(fixedSize);
            Layout(panel);
            Assert.Single(order);
            Assert.StartsWith("content (0.00, 0.00)>", order[0]);
        }
    }

    // [measured] tests/projects/uitkcollections absolute section (a 79x40 parent with padding
    // 20/20/4/6 and borders 3/2) and tabs/attach (the header underline): an absolute child's
    // percentage size, min/max and offsets resolve against its parent's padding box; its position
    // adds margins of the content width; without offsets it starts at the parent's border and
    // padding plus the line's justify offset (centered and end-justified children excepted).
    public class YogaAbsoluteChildRulesTests
    {
        static (YogaNode Root, YogaNode Child) Padded(Action<YogaNode> setupChild, YogaFlexDirection direction = YogaFlexDirection.Column,
            YogaJustify justify = YogaJustify.FlexStart)
        {
            var config = new YogaConfig();
            var root = new YogaNode(config) { Width = YogaValue.Point(79), Height = YogaValue.Point(40), FlexDirection = direction, JustifyContent = justify };
            root.SetPadding(YogaEdge.Left, YogaValue.Point(20));
            root.SetPadding(YogaEdge.Right, YogaValue.Point(20));
            root.SetPadding(YogaEdge.Top, YogaValue.Point(4));
            root.SetPadding(YogaEdge.Bottom, YogaValue.Point(6));
            root.SetBorder(YogaEdge.Left, 3);
            root.SetBorder(YogaEdge.Top, 2);
            var child = new YogaNode(config) { PositionType = YogaPositionType.Absolute, Height = YogaValue.Point(2) };
            setupChild(child);
            root.Insert(0, child);
            root.CalculateLayout();
            return (root, child);
        }

        [Fact]
        public void InsetsSpanThePaddingBox()
        {
            var (_, c) = Padded(n => { n.SetPosition(YogaEdge.Left, YogaValue.Point(0)); n.SetPosition(YogaEdge.Right, YogaValue.Point(0)); });
            Assert.Equal(3f, c.LayoutX);
            Assert.Equal(76f, c.LayoutWidth);
        }

        [Fact]
        public void PercentSizesAndOffsetsUseThePaddingBox()
        {
            var (_, w) = Padded(n => n.Width = YogaValue.Percent(100));
            Assert.Equal(23f, w.LayoutX);
            Assert.Equal(6f, w.LayoutY);
            Assert.Equal(76f, w.LayoutWidth);
            var (_, h) = Padded(n => { n.Width = YogaValue.Point(10); n.Height = YogaValue.Percent(50); });
            Assert.Equal(19f, h.LayoutHeight);
            var (_, l) = Padded(n => { n.Width = YogaValue.Point(10); n.SetPosition(YogaEdge.Left, YogaValue.Percent(10)); });
            Assert.Equal(11f, l.LayoutX);
        }

        [Fact]
        public void PercentMarginsUseTheContentWidth()
        {
            var (_, m) = Padded(n => { n.Width = YogaValue.Point(10); n.SetMargin(YogaEdge.Left, YogaValue.Percent(10)); });
            Assert.Equal(27f, m.LayoutX);
            Assert.Equal(10f, m.LayoutWidth);
        }

        [Fact]
        public void MinMaxAndTrailingOffsetsUseThePaddingBox()
        {
            var (_, min) = Padded(n => { n.Width = YogaValue.Point(10); n.MinWidth = YogaValue.Percent(50); });
            Assert.Equal(38f, min.LayoutWidth);
            var (_, max) = Padded(n => { n.Width = YogaValue.Point(40); n.MaxWidth = YogaValue.Percent(10); });
            Assert.Equal(8f, max.LayoutWidth);
            var (_, right) = Padded(n => { n.Width = YogaValue.Point(10); n.SetPosition(YogaEdge.Right, YogaValue.Percent(10)); });
            Assert.Equal(61f, right.LayoutX);
        }

        [Fact]
        public void StaticPositionAddsTheJustifyOffset()
        {
            var (_, evenly) = Padded(n => n.Width = YogaValue.Point(10), justify: YogaJustify.SpaceEvenly);
            Assert.Equal(34f, evenly.LayoutY);
            var (_, center) = Padded(n => n.Width = YogaValue.Point(10), justify: YogaJustify.Center);
            Assert.Equal(19f, center.LayoutY);
            var (_, end) = Padded(n => n.Width = YogaValue.Point(10), justify: YogaJustify.FlexEnd);
            Assert.Equal(38f, end.LayoutY);
            // No relative items: the space-around offset divides by zero, as in the Editor.
            var (_, around) = Padded(n => n.Width = YogaValue.Point(10), justify: YogaJustify.SpaceAround);
            Assert.Equal(float.PositiveInfinity, around.LayoutY);
            Assert.True(float.IsNaN(around.LayoutHeight));
        }

        // [rule] Laying out a panel again rounds the cached layout of an unchanged subtree once more;
        // a child at an infinite offset keeps its undefined size (no -Infinity).
        [Fact]
        public void AnInfiniteOffsetKeepsAnUndefinedSizeWhenLaidOutAgain()
        {
            var config = new YogaConfig { RoundEdgesToPixelGrid = true, RoundPositionsFromAbsolute = true };
            var panel = new YogaNode(config) { Width = YogaValue.Point(800), Height = YogaValue.Point(600) };
            var host = new YogaNode(config) { PositionType = YogaPositionType.Absolute, Width = YogaValue.Point(79), Height = YogaValue.Point(40), JustifyContent = YogaJustify.SpaceAround };
            var child = new YogaNode(config) { PositionType = YogaPositionType.Absolute, Width = YogaValue.Point(10), Height = YogaValue.Point(2) };
            var sibling = new YogaNode(config) { Height = YogaValue.Point(10) };
            host.Insert(0, child);
            panel.Insert(0, host);
            panel.Insert(1, sibling);
            panel.CalculateLayout();
            Assert.True(float.IsNaN(child.LayoutHeight));
            sibling.Height = YogaValue.Point(11);
            panel.CalculateLayout();
            Assert.Equal(float.PositiveInfinity, child.LayoutY);
            Assert.True(float.IsNaN(child.LayoutHeight));
        }

        // [rule] A parent sized by its content: offsets and sizes use its final padding box.
        [Fact]
        public void OffsetsUseTheFinalSizeOfAContentSizedParent()
        {
            var config = new YogaConfig();
            var row = new YogaNode(config) { Width = YogaValue.Point(200), Height = YogaValue.Point(100), FlexDirection = YogaFlexDirection.Row, AlignItems = YogaAlign.FlexStart };
            var column = new YogaNode(config) { Width = YogaValue.Point(50) };
            var content = new YogaNode(config) { Height = YogaValue.Point(20) };
            var top = new YogaNode(config) { PositionType = YogaPositionType.Absolute, Width = YogaValue.Point(4), Height = YogaValue.Point(2) };
            top.SetPosition(YogaEdge.Top, YogaValue.Percent(50));
            var bottom = new YogaNode(config) { PositionType = YogaPositionType.Absolute, Width = YogaValue.Point(4), Height = YogaValue.Point(2) };
            bottom.SetPosition(YogaEdge.Bottom, YogaValue.Percent(50));
            column.Insert(0, content);
            column.Insert(1, top);
            column.Insert(2, bottom);
            row.Insert(0, column);
            row.CalculateLayout();
            Assert.Equal(20f, column.LayoutHeight);
            Assert.Equal(10f, top.LayoutY);
            Assert.Equal(8f, bottom.LayoutY);
        }

        [Fact]
        public void HiddenAbsoluteChildKeepsAZeroLayout()
        {
            var (_, hidden) = Padded(n => { n.Width = YogaValue.Point(10); n.SetPosition(YogaEdge.Left, YogaValue.Point(30)); n.Display = YogaDisplay.None; });
            Assert.Equal(0f, hidden.LayoutX);
            Assert.Equal(0f, hidden.LayoutY);
            Assert.Equal(0f, hidden.LayoutWidth);
            Assert.Equal(0f, hidden.LayoutHeight);
        }

        // A column of exact height: a shrinkable 41-high header, a growing and shrinking body whose
        // content is 400 high, then optionally a shrinkable trailing child.
        static (YogaNode Header, YogaNode Body) HeaderAndBody(bool trailingFlexibleChild)
        {
            var config = new YogaConfig();
            var root = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(160) };
            var header = new YogaNode(config) { FlexShrink = 1 };
            header.Insert(0, new YogaNode(config) { Height = YogaValue.Point(41) });
            var body = new YogaNode(config) { FlexGrow = 1, FlexShrink = 1 };
            body.Insert(0, new YogaNode(config) { Height = YogaValue.Point(400) });
            root.Insert(0, header);
            root.Insert(1, body);
            if (trailingFlexibleChild)
                root.Insert(2, new YogaNode(config) { FlexShrink = 1, Height = YogaValue.Point(10) });
            root.CalculateLayout();
            return (header, body);
        }

        [Fact]
        public void TheFirstGrowingAndShrinkingChildTakesTheRemainingSpace()
        {
            // The header before it does not grow, so the body alone flexes from a zero basis.
            var (header, body) = HeaderAndBody(false);
            Assert.Equal(41f, header.LayoutHeight);
            Assert.Equal(0f, body.ComputedFlexBasis);
            Assert.Equal(119f, body.LayoutHeight);
            // A flexible child after it: every child is measured and the overflow shrinks them all.
            var (shrunkHeader, measuredBody) = HeaderAndBody(true);
            Assert.True(shrunkHeader.LayoutHeight < 41f);
            Assert.Equal(400f, measuredBody.ComputedFlexBasis);
        }

        [Fact]
        public void StaticPositionFollowsTheAxisDirection()
        {
            var (_, rev) = Padded(n => n.Width = YogaValue.Point(10), YogaFlexDirection.ColumnReverse);
            Assert.Equal(32f, rev.LayoutY);
            Assert.Equal(23f, rev.LayoutX);
            var (_, rowRev) = Padded(n => n.Width = YogaValue.Point(10), YogaFlexDirection.RowReverse);
            Assert.Equal(49f, rowRev.LayoutX);
            Assert.Equal(6f, rowRev.LayoutY);
        }
    }
}
