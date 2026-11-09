using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.UIElements;
using Unbound.UIElements;
using Xunit;

// TreeView, BaseTreeView and TreeViewItemData<T>. Each test names its source:
//   [doc]    a statement of the Unity 6000.0 Scripting API pages of these types and their members
//            (TreeViewItemData, BaseTreeView, TreeView, BaseTreeViewController.GetIndexForId/GetParentId,
//            TreeViewExpansionChangedArgs and its id, isExpanded and isAppliedToAllChildren) or of the
//            Manual page "TreeView" (UXML attributes, USS
//            class table, bindItem receives the index);
//   [choice] Unbound's choice where those pages say nothing (default expansion, when the id/index
//            mapping updates, which calls send itemExpandedChanged, the row element structure). These
//            check Unbound's implementation, not Unity behaviour.
// No reference capture covers TreeView; nothing here is a measured Unity value.
// Red base: bc93e0fc, where the three types are generated stubs: their members warn, do nothing and
// answer defaults (ids 0, null class names, an empty ArrayList as itemsSource, a null viewController
// and virtualization controller). The file uses only members that exist there (the row's depth is
// read by reflection), so it is expected to compile on that base (unexecuted). The itemExpandedChanged
// payload facts are red on 54087224 instead, where TreeViewExpansionChangedArgs is still a generated
// stub: its setters store nothing and its getters answer 0 and false.

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkTreeViewTests
    {
        static Panel NewPanel() => new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        static VisualElement Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UitkTreeViewProbe";
            UxmlImporter.Import("Assets/UI/UitkTreeViewProbe.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return root;
        }

        // A (1): A1 (11), A2 (12): A2a (121); B (2); C (3): C1 (31). Seven items, three roots.
        static List<TreeViewItemData<string>> Sample() => new List<TreeViewItemData<string>>
        {
            new TreeViewItemData<string>(1, "A", new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(11, "A1"),
                new TreeViewItemData<string>(12, "A2", new List<TreeViewItemData<string>> { new TreeViewItemData<string>(121, "A2a") }),
            }),
            new TreeViewItemData<string>(2, "B"),
            new TreeViewItemData<string>(3, "C", new List<TreeViewItemData<string>> { new TreeViewItemData<string>(31, "C1") }),
        };

        static TreeView NewTree()
        {
            TreeView view = null;
            view = new TreeView(() => new Label(), (e, i) => ((Label)e).text = view.GetItemDataForIndex<string>(i));
            view.SetRootItems(Sample());
            return view;
        }

        static int[] VisibleIds(TreeView view) => Enumerable.Range(0, view.itemsSource.Count).Select(view.GetIdForIndex).ToArray();

        static int Depth(object row) => ((ReusableTreeViewItem)row).m_Depth;

        // ------------------------------------------------------------------ item data

        // [doc] TreeViewItemData: id "Id of the item", data "Data for this item", children "Children of
        // this tree item", hasChildren "Whether this item has children or not".
        // [choice] No children (null) reads as an empty sequence.
        // Red on bc93e0fc: the stub's id answers 0 (first Assert.Equal fails).
        [Fact]
        public void TreeViewItemDataHoldsIdDataAndChildren()
        {
            var leaf = new TreeViewItemData<string>(7, "leaf");
            var parent = new TreeViewItemData<string>(5, "parent", new List<TreeViewItemData<string>> { leaf });
            Assert.Equal(5, parent.id);
            Assert.Equal("parent", parent.data);
            Assert.True(parent.hasChildren);
            Assert.Equal(new[] { 7 }, parent.children.Select(c => c.id));
            Assert.Equal("leaf", parent.children.Single().data);
            Assert.False(leaf.hasChildren);
            Assert.Empty(leaf.children);
            Assert.False(new TreeViewItemData<string>(8, "empty", new List<TreeViewItemData<string>>()).hasChildren);
        }

        // [doc] SetRootItems "Sets the root items"; GetTreeCount is "the TreeView's total number of
        // items"; GetRootIds "the root item identifiers"; the controller's itemsSource "Contains items
        // that are expanded in the tree"; GetItemDataForIndex/GetItemDataForId give the item data.
        // [choice] Items start collapsed (autoExpand is false).
        // Red on bc93e0fc: the stub's itemsSource is an empty ArrayList (Count 0, not 3).
        [Fact]
        public void SetRootItemsShowsTheCollapsedRoots()
        {
            var view = NewTree();
            Assert.Equal(3, view.itemsSource.Count);
            Assert.Equal(7, view.GetTreeCount());
            Assert.Equal(new[] { 1, 2, 3 }, view.GetRootIds());
            Assert.Equal(new[] { 1, 2, 3 }, VisibleIds(view));
            Assert.Equal("B", view.GetItemDataForIndex<string>(1));
            Assert.Equal("A2a", view.GetItemDataForId<string>(121));
            Assert.False(view.IsExpanded(1));
            Assert.IsAssignableFrom<DefaultTreeViewController<string>>(view.viewController);
        }

        // ------------------------------------------------------------------ expand and collapse

        // [doc] ExpandItem makes the children visible, CollapseItem hides them, IsExpanded reports it.
        // [choice] A collapsed parent keeps its children's expanded state, shown again when reopened.
        // Red on bc93e0fc: the stub's itemsSource Count is 0, not 3 (first Assert.Equal fails).
        [Fact]
        public void ExpandAndCollapseChangeTheVisibleRows()
        {
            var view = NewTree();
            Assert.Equal(3, view.itemsSource.Count);
            view.ExpandItem(1);
            Assert.True(view.IsExpanded(1));
            Assert.Equal(new[] { 1, 11, 12, 2, 3 }, VisibleIds(view));
            view.ExpandItem(12);
            Assert.Equal(new[] { 1, 11, 12, 121, 2, 3 }, VisibleIds(view));
            view.CollapseItem(1);
            Assert.False(view.IsExpanded(1));
            Assert.True(view.IsExpanded(12));
            Assert.Equal(new[] { 1, 2, 3 }, VisibleIds(view));
            view.ExpandItem(1);
            Assert.Equal(6, view.itemsSource.Count);
            view.CollapseItem(1, collapseAllChildren: true);
            Assert.False(view.IsExpanded(12));
            view.ExpandItem(1, expandAllChildren: true);
            Assert.True(view.IsExpanded(12));
            Assert.Equal(6, view.itemsSource.Count);
            // [choice] refresh false still updates the rows and the id/index mapping at once.
            view.CollapseItem(1, refresh: false);
            Assert.Equal(3, view.itemsSource.Count);
        }

        // [doc] ExpandAll/CollapseAll: all items "including children"; ExpandRootItems: the root items.
        // Red on bc93e0fc: the stub's itemsSource Count is 0, not 7 (first Assert.Equal fails).
        [Fact]
        public void ExpandAllCollapseAllAndExpandRootItems()
        {
            var view = NewTree();
            view.ExpandAll();
            Assert.Equal(7, view.itemsSource.Count);
            Assert.Equal(new[] { 1, 11, 12, 121, 2, 3, 31 }, VisibleIds(view));
            view.CollapseAll();
            Assert.Equal(new[] { 1, 2, 3 }, VisibleIds(view));
            Assert.False(view.IsExpanded(12));
            view.ExpandRootItems();
            Assert.Equal(new[] { 1, 11, 12, 2, 3, 31 }, VisibleIds(view));
            Assert.False(view.IsExpanded(12));
        }

        // [doc] autoExpand: "items are automatically expanded when added to the TreeView".
        // Red on bc93e0fc: the stub's autoExpand stores nothing and answers false.
        [Fact]
        public void AutoExpandExpandsAddedItems()
        {
            var view = new TreeView { autoExpand = true };
            Assert.True(view.autoExpand);
            view.SetRootItems(Sample());
            Assert.Equal(7, view.itemsSource.Count);
            Assert.True(view.IsExpanded(12));
        }

        // ------------------------------------------------------------------ id and index

        // [doc] BaseTreeViewController.GetIndexForId: "Returns -1 if the item is not visible";
        // GetParentId: "-1 if the item is at the root"; GetIdForIndex, GetParentIdForIndex and
        // GetChildrenIdsForIndex by visible index.
        // Red on bc93e0fc: the stub's viewController is null, so viewController.GetIndexForId throws.
        [Fact]
        public void IdAndIndexLookupsFollowTheVisibleRows()
        {
            var view = NewTree();
            Assert.Equal(-1, view.viewController.GetIndexForId(121));
            Assert.Equal(2, view.viewController.GetIndexForId(3));
            view.ExpandItem(1, expandAllChildren: true);
            Assert.Equal(3, view.viewController.GetIndexForId(121));
            Assert.Equal(5, view.viewController.GetIndexForId(3));
            Assert.Equal(121, view.GetIdForIndex(3));
            Assert.Equal(12, view.GetParentIdForIndex(3));
            Assert.Equal(-1, view.GetParentIdForIndex(0));
            Assert.Equal(new[] { 11, 12 }, view.GetChildrenIdsForIndex(0));
            Assert.Empty(view.GetChildrenIdsForIndex(4));
            Assert.Equal(1, view.viewController.GetParentId(12));
            Assert.Equal(-1, view.viewController.GetParentId(2));
            Assert.Equal(7, view.viewController.GetTreeItemsCount());
            Assert.Equal("A2a", view.GetItemDataForIndex<string>(3));
        }

        // ------------------------------------------------------------------ selection by id

        // [doc] SetSelectionById, AddToSelectionById, RemoveFromSelectionById and
        // SetSelectionByIdWithoutNotify ("without triggering a selection change callback");
        // GetSelectedItems "Gets tree data for the selected item indices".
        // [choice] Selecting a hidden id expands its ancestors; the collection view's selected item is
        // the item's data.
        // Red on bc93e0fc: the stub's SetSelectionById does nothing, so selectedIndex stays -1.
        [Fact]
        public void SelectionById()
        {
            var view = NewTree();
            view.selectionType = SelectionType.Multiple;
            int changes = 0;
            List<object> last = null;
            view.selectionChanged += items =>
            {
                changes++;
                last = items.ToList();
            };
            view.SetSelectionById(121);
            Assert.Equal(3, view.selectedIndex);
            Assert.True(view.IsExpanded(1));
            Assert.True(view.IsExpanded(12));
            Assert.Equal(new[] { 121 }, view.selectedIds);
            Assert.Equal(1, changes);
            Assert.Equal(new object[] { "A2a" }, last);
            Assert.Equal(121, view.GetSelectedItems<string>().Single().id);

            view.AddToSelectionById(2);
            Assert.Equal(new[] { 3, 4 }, view.selectedIndices);
            view.RemoveFromSelectionById(121);
            Assert.Equal(new[] { 2 }, view.selectedIds);

            int before = changes;
            view.SetSelectionByIdWithoutNotify(new[] { 3 });
            Assert.Equal(before, changes);
            Assert.Equal(5, view.selectedIndex);
            Assert.Equal("C", view.selectedItem);
        }

        // [measured] tests/projects/uitkcollections tree/select-id-parent-10, select-id-child-14,
        // add-to-selection-id-17, select-without-notify-id-21: selection by id expands the item's
        // collapsed ancestors (each reported), not the item itself, though the Remarks of these
        // methods say it "will also expand the selected item(s) if not expanded already".

        [Fact]
        public void SetSelectionByIdKeepsTheSelectedItemCollapsed()
        {
            var view = NewTree();
            view.SetSelectionById(1);
            Assert.False(view.IsExpanded(1));
            Assert.Equal(new[] { 1, 2, 3 }, VisibleIds(view));
            Assert.Equal(0, view.selectedIndex);
            Assert.Equal(new[] { 1 }, view.selectedIds);
        }

        [Fact]
        public void SetSelectionByIdsExpandsOnlyTheAncestors()
        {
            var view = NewTree();
            view.selectionType = SelectionType.Multiple;
            view.SetSelectionById(new[] { 12, 3 });
            Assert.True(view.IsExpanded(1));
            Assert.False(view.IsExpanded(12));
            Assert.False(view.IsExpanded(3));
            Assert.Equal(new[] { 1, 11, 12, 2, 3 }, VisibleIds(view));
            Assert.Equal(new[] { 2, 4 }, view.selectedIndices);
        }

        [Fact]
        public void AddToSelectionByIdKeepsTheSelectedItemCollapsed()
        {
            var view = NewTree();
            view.AddToSelectionById(1);
            Assert.False(view.IsExpanded(1));
            Assert.Equal(new[] { 1, 2, 3 }, VisibleIds(view));
            Assert.Equal(0, view.selectedIndex);
        }

        [Fact]
        public void SetSelectionByIdWithoutNotifyExpandsAncestorsQuietly()
        {
            var view = NewTree();
            int changes = 0;
            view.selectionChanged += _ => changes++;
            view.SetSelectionByIdWithoutNotify(new[] { 121 });
            Assert.True(view.IsExpanded(1));
            Assert.True(view.IsExpanded(12));
            Assert.Equal(new[] { 1, 11, 12, 121, 2, 3 }, VisibleIds(view));
            Assert.Equal(3, view.selectedIndex);
            Assert.Equal(0, changes);
        }

        // Red on b6ee73db: expanding item 1 moves the selected id 2 from row 1 to row 3 and the refresh
        // reports it (Assert.Equal(0, changes) fails with 1).
        [Fact]
        public void SetSelectionByIdWithoutNotifyDoesNotReportAMovedSelection()
        {
            var view = NewTree();
            view.SetSelection(1);
            int changes = 0, indexChanges = 0;
            view.selectionChanged += _ => changes++;
            view.selectedIndicesChanged += _ => indexChanges++;
            view.SetSelectionByIdWithoutNotify(new[] { 1 });
            Assert.Equal(0, changes);
            Assert.Equal(0, indexChanges);
            Assert.Equal(new[] { 1 }, view.selectedIds);
            Assert.Equal(0, view.selectedIndex);
        }

        // Red on b6ee73db: a stale report of the moved id 2 at row 3 comes first (Assert.Single(reports)
        // fails with 2).
        [Fact]
        public void SetSelectionByIdReportsOnlyTheNewSelection()
        {
            var view = NewTree();
            view.SetSelection(1);
            var reports = new List<int[]>();
            view.selectedIndicesChanged += indices => reports.Add(indices.ToArray());
            view.SetSelectionById(1);
            Assert.Single(reports);
            Assert.Equal(new[] { 0 }, reports[0]);
            Assert.Equal(new[] { 1 }, view.selectedIds);
        }

        // Guard (not a red/green control; expected to pass on 46596641 and on this head): a selected
        // leaf has nothing to expand; its ancestors are expanded [choice] and it stays not expanded.
        [Fact]
        public void SelectingALeafByIdExpandsOnlyItsAncestors()
        {
            var view = NewTree();
            view.SetSelectionById(121);
            Assert.True(view.IsExpanded(1));
            Assert.True(view.IsExpanded(12));
            Assert.False(view.IsExpanded(121));
            Assert.False(view.IsExpanded(3));
            Assert.Equal(new[] { 1, 11, 12, 121, 2, 3 }, VisibleIds(view));
        }

        // [measured] Each ancestor that selection by id expands sends itemExpandedChanged after the rows
        // are refreshed; a root item has none.
        [Fact]
        public void SelectionByIdExpansionSendsItemExpandedChanged()
        {
            var view = NewTree();
            int calls = 0;
            int rowsDuring = 0;
            view.itemExpandedChanged += _ =>
            {
                calls++;
                rowsDuring = view.itemsSource.Count;
            };
            view.SetSelectionById(121);
            Assert.Equal(2, calls);
            Assert.Equal(6, rowsDuring);
            view.SetSelectionByIdWithoutNotify(new[] { 3 });
            view.SetSelectionById(3);
            Assert.Equal(2, calls);
        }

        // ------------------------------------------------------------------ virtualization

        // [doc] TreeView "creates VisualElements for the visible items" and recycles them; bindItem
        // receives the index (Manual note).
        // [choice] The shared fixed-height virtualization: rows for the viewport plus two.
        // Red on bc93e0fc: the stub's CreateVirtualizationController leaves none, so
        // view.virtualizationController is null and Resize throws NullReferenceException.
        [Fact]
        public void OnlyRowsInTheViewportAreBoundAndUnbound()
        {
            var bound = new List<int>();
            var unbound = new List<int>();
            var view = new TreeView(() => new Label(), (e, i) => bound.Add(i)) { fixedItemHeight = 20f };
            view.unbindItem = (e, i) => unbound.Add(i);
            var roots = Enumerable.Range(0, 50).Select(i => new TreeViewItemData<int>(i, i)).ToList();
            view.SetRootItems(roots);
            view.virtualizationController.Resize(new Vector2(100f, 100f));
            Assert.Equal(Enumerable.Range(0, 7), bound);
            Assert.Empty(unbound);
            Assert.Equal(7, view.activeItems.Count());

            // [measured] tree/set-root-items: new root items report itemsSourceChanged; the rows follow
            // at the next refresh.
            view.SetRootItems(roots.Take(3).ToList());
            Assert.Empty(unbound);
            view.RefreshItems();
            Assert.Equal(Enumerable.Range(0, 7), unbound.OrderBy(i => i));
            Assert.Equal(3, view.activeItems.Count());
            Assert.Equal(new[] { 0, 1, 2 }, view.activeItems.Select(r => r.index).OrderBy(i => i));
        }

        // ------------------------------------------------------------------ item template without bindItem

        /// <summary>The Stubs member key under which unbound tree template rows are reported.</summary>
        const string UnboundTemplateMember = "TreeView.itemTemplate";

        /// <summary>The once-per-process warning Stubs.Ignored logs for that member (a stack trace
        /// follows it when stub tracing is on).</summary>
        const string UnboundTemplateNotice =
            "Unbound: TreeView.itemTemplate rows are shown unbound: runtime data binding (bindItem omitted with an item template) not implemented";

        /// <summary>How often the member has been reported in this process (every call counts).</summary>
        static int Hits(string member) => Unbound.Runtime.Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;

        /// <summary>Forwards everything to the original handler, keeping the warnings that name the
        /// unbound-template member and counting exceptions. "Unbound: " diagnostics never reach
        /// Application.logMessageReceived, so they are observed here, at the logger's handler.</summary>
        sealed class NoticeCapture : ILogHandler
        {
            readonly ILogHandler m_Inner;
            internal readonly List<string> Notices = new List<string>();
            internal int Exceptions;
            internal NoticeCapture(ILogHandler inner) { m_Inner = inner; }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = args == null || args.Length == 0
                    ? format
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                if (type == LogType.Warning && message != null && message.Contains("Unbound: " + UnboundTemplateMember + " "))
                    lock (Notices) Notices.Add(message);
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(Exception exception, Object context)
            {
                System.Threading.Interlocked.Increment(ref Exceptions);
                m_Inner.LogException(exception, context);
            }
        }

        /// <summary>Runs <paramref name="body"/> with the capture installed at the logger Stubs writes
        /// to; the logger's handler, enabled flag and filter are restored after.</summary>
        static NoticeCapture Captured(Action body)
        {
            var logger = Debug.unityLogger;
            var handler = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var capture = new NoticeCapture(handler);
            logger.logHandler = capture;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                body();
            }
            finally
            {
                logger.logHandler = handler;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
            }
            return capture;
        }

        // [doc] A tree with an item template and no bindItem is not valid (makeItem without
        // bindItem): it shows no rows.
        [Fact]
        public void TemplateRowsWithoutBindItemAreNotShown()
        {
            var template = ScriptableObject.CreateInstance<VisualTreeAsset>();
            template.name = "UitkTreeViewRow";
            UxmlImporter.Import("Assets/UI/UitkTreeViewRow.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label name=\"row-label\" /></ui:UXML>", template);
            var view = new TreeView { fixedItemHeight = 20f };
            view.itemTemplate = template;
            view.SetRootItems(new List<TreeViewItemData<string>> { new TreeViewItemData<string>(1, "a") });
            Assert.Null(view.bindItem);
            Assert.False(view.HasValidDataAndBindings());
        }

        /// <summary>A tree whose rows come from a one-Label item template set in C#, with 20-pixel rows,
        /// three root items and no callback set by the test.</summary>
        static TreeView TemplatedTree(out VisualTreeAsset template)
        {
            template = ScriptableObject.CreateInstance<VisualTreeAsset>();
            template.name = "UitkTreeViewRow";
            UxmlImporter.Import("Assets/UI/UitkTreeViewRow.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:Label name=\"row-label\" /></ui:UXML>", template);
            var view = new TreeView { fixedItemHeight = 20f };
            view.itemTemplate = template;
            view.SetRootItems(new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(1, "a"),
                new TreeViewItemData<string>(2, "b"),
                new TreeViewItemData<string>(3, "c"),
            });
            return view;
        }

        // [interim] Control: a game makeItem without bindItem is still rejected, also on a tree that
        // had a template: replacing the template's makeItem ends the exemption, and binding a row
        // throws as before, with no unbound-template report.
        // Passes at 411b77c2, at 1764758a and at head (not a per-case red). Red under a mutation
        // dropping the "makeItem is the template's factory" term: the tree is valid and the row is
        // reported instead of rejected.
        [Fact]
        public void AGameMakeItemWithoutBindItemIsStillRejectedOnATemplatedTree()
        {
            var view = TemplatedTree(out var template);
            view.makeItem = () => new Label();
            Assert.Same(template, view.itemTemplate);
            Assert.Null(view.bindItem);
            Assert.False(view.HasValidDataAndBindings());

            int before = Hits(UnboundTemplateMember);
            var error = Assert.Throws<NotImplementedException>(() => view.virtualizationController.Resize(new Vector2(100f, 100f)));
            Assert.Equal("You must specify bindItem if makeItem is specified.", error.Message);
            Assert.Equal(before, Hits(UnboundTemplateMember));
        }


        // ------------------------------------------------------------------ USS classes and rows

        // [doc] The Manual's USS class table of TreeView and BaseTreeView's static fields.
        // Red on bc93e0fc: the stub's ussClassName is null (first Assert.Equal fails).
        [Fact]
        public void TreeViewHasTheDocumentedUssClasses()
        {
            Assert.Equal("unity-tree-view", BaseTreeView.ussClassName);
            Assert.Equal("unity-tree-view__item", BaseTreeView.itemUssClassName);
            Assert.Equal("unity-tree-view__item-toggle", BaseTreeView.itemToggleUssClassName);
#pragma warning disable CS0618
            Assert.Equal("unity-tree-view__item-indents", BaseTreeView.itemIndentsContainerUssClassName);
#pragma warning restore CS0618
            Assert.Equal("unity-tree-view__item-indent", BaseTreeView.itemIndentUssClassName);
            Assert.Equal("unity-tree-view__item-content", BaseTreeView.itemContentContainerUssClassName);
            var view = new TreeView();
            Assert.True(view.ClassListContains("unity-tree-view"));
            Assert.True(view.ClassListContains("unity-collection-view"));
        }

        // [doc] Each item element has the __item class, a toggle (__item-toggle), an indent
        // (__item-indent) and a content container (__item-content) holding the made element.
        // [measured] Element order indent, toggle, content, each named after its class (the indent
        // carries its class only when indented); leaves hide their toggle; depth by tree level.
        // Red on bc93e0fc: view.virtualizationController is null, so Resize throws.
        [Fact]
        public void RowsHaveTheIndentToggleAndContent()
        {
            var view = NewTree();
            view.fixedItemHeight = 20f;
            view.ExpandItem(1);
            view.virtualizationController.Resize(new Vector2(100f, 200f));
            var rows = view.activeItems.OrderBy(r => r.index).ToList();
            Assert.Equal(5, rows.Count);

            var root = rows[0].rootElement;
            Assert.True(root.ClassListContains("unity-tree-view__item"));
            Assert.True(root.ClassListContains("unity-collection-view__item"));
            var parts = root.hierarchy.Children().ToList();
            Assert.Equal(3, parts.Count);
            Assert.Equal("unity-tree-view__item-indent", parts[0].name);
            Assert.False(parts[0].ClassListContains("unity-tree-view__item-indent"));
            var toggle = Assert.IsType<Toggle>(parts[1]);
            Assert.True(toggle.ClassListContains("unity-tree-view__item-toggle"));
            Assert.True(parts[2].ClassListContains("unity-tree-view__item-content"));
            Assert.Equal("A", Assert.IsType<Label>(parts[2].hierarchy[0]).text);
            Assert.True(toggle.value);
            Assert.Equal(Visibility.Visible, toggle.style.visibility.value);
            Assert.Equal(0, Depth(rows[0]));

            var leaf = rows[1];
            Assert.Equal(1, Depth(leaf));
            Assert.True(leaf.rootElement.hierarchy[0].ClassListContains("unity-tree-view__item-indent"));
            var leafToggle = (Toggle)leaf.rootElement.Q(null, "unity-tree-view__item-toggle");
            Assert.Equal(Visibility.Hidden, leafToggle.style.visibility.value);
            Assert.Equal("A1", ((Label)leaf.bindableElement).text);

            var collapsedParent = (Toggle)rows[2].rootElement.Q(null, "unity-tree-view__item-toggle");
            Assert.False(collapsedParent.value);
            Assert.Equal(Visibility.Visible, collapsedParent.style.visibility.value);
        }

        // [doc] The toggle expands or collapses its item (Manual: "Expand/collapse toggle of each item").
        // Red on bc93e0fc: view.virtualizationController is null, so Resize throws.
        [Fact]
        public void TheRowToggleExpandsItsItem()
        {
            var panel = NewPanel();
            var view = new TreeView { fixedItemHeight = 20f };
            panel.visualTree.Add(view);
            view.SetRootItems(Sample());
            view.virtualizationController.Resize(new Vector2(100f, 200f));
            var first = view.activeItems.Single(r => r.index == 0);
            var toggle = (Toggle)first.rootElement.Q(null, "unity-tree-view__item-toggle");
            toggle.value = true;
            Assert.True(view.IsExpanded(1));
            Assert.Equal(5, view.itemsSource.Count);
        }

        // [doc] SetRootItems "will force the use of a DefaultTreeViewController<T>", so root items of
        // another data type replace the controller; the toggle still expands its item.
        // [rule] Pooled rows are kept; the controller that rebinds a row takes its toggle over (a
        // replaced controller ignores it instead of failing).
        [Fact]
        public void TheRowToggleWorksAfterRootItemsOfAnotherType()
        {
            var panel = NewPanel();
            var view = new TreeView { fixedItemHeight = 20f };
            panel.visualTree.Add(view);
            view.SetRootItems(Sample());
            view.virtualizationController.Resize(new Vector2(100f, 200f));
            var first = view.activeItems.Single(r => r.index == 0);
            view.SetRootItems(new List<TreeViewItemData<int>>
            {
                new TreeViewItemData<int>(100, 10, new List<TreeViewItemData<int>> { new TreeViewItemData<int>(101, 11) }),
                new TreeViewItemData<int>(200, 20),
                new TreeViewItemData<int>(300, 30),
            });
            Assert.Same(first, view.activeItems.Single(r => r.index == 0));
            var toggle = (Toggle)first.rootElement.Q(null, "unity-tree-view__item-toggle");
            toggle.value = true;
            Assert.False(view.IsExpanded(100));
            view.RefreshItems();
            toggle.value = true;
            Assert.True(view.IsExpanded(100));
            Assert.Equal(new[] { 100, 101, 200, 300 }, VisibleIds(view));
        }

        // ------------------------------------------------------------------ itemExpandedChanged

        // [doc] itemExpandedChanged is "Raised when an item is expanded or collapsed"; ExpandAll and
        // CollapseAll raise it (its id "Returns -1 when expandAll() or collapseAll() is being called").
        // [measured] Sent after the rows are refreshed, also when the state did not change (tree/
        // collapse-10); a leaf or an unknown id sends nothing (tree/expand-leaf-2).
        // The event arguments' values are checked by the payload facts below.
        // Red on bc93e0fc: the stub's event add stores nothing (calls stays 0, not 1).
        [Fact]
        public void ItemExpandedChangedIsRaised()
        {
            var view = NewTree();
            int calls = 0;
            bool expandedDuring = false;
            int rowsDuring = 0;
            view.itemExpandedChanged += args =>
            {
                calls++;
                expandedDuring = view.IsExpanded(1);
                rowsDuring = view.itemsSource.Count;
            };
            view.ExpandItem(1);
            Assert.Equal(1, calls);
            Assert.True(expandedDuring);
            Assert.Equal(5, rowsDuring);
            view.ExpandItem(1);
            Assert.Equal(2, calls);
            view.ExpandItem(2);
            view.ExpandItem(999);
            Assert.Equal(2, calls);
            view.CollapseItem(1);
            Assert.Equal(3, calls);
            Assert.False(expandedDuring);
            view.ExpandAll();
            Assert.Equal(4, calls);
            view.CollapseAll();
            Assert.Equal(5, calls);
        }

        // The payloads itemExpandedChanged handlers receive, as (id, isExpanded, isAppliedToAllChildren),
        // read inside the handler.
        static List<(int id, bool expanded, bool allChildren)> RecordExpansions(BaseTreeView view)
        {
            var payloads = new List<(int id, bool expanded, bool allChildren)>();
            view.itemExpandedChanged += args => payloads.Add((args.id, args.isExpanded, args.isAppliedToAllChildren));
            return payloads;
        }

        // [doc] TreeViewExpansionChangedArgs: id "The id of the item being expanded or collapsed",
        // isExpanded "Indicates whether the item is expanded (true) or collapsed (false)",
        // isAppliedToAllChildren "Indicates whether the expandAllChildren or collapsedAllChildren is
        // applied when expanding the item".
        // [choice] One event for one item whose state changed (as ItemExpandedChangedIsRaised).
        // Red on 54087224: the stub args answer (0, false, false), so Assert.Equal((1, true, false), ...)
        // fails.
        [Fact]
        public void ExpandItemSendsTheItemIdAndExpanded()
        {
            var view = NewTree();
            var payloads = RecordExpansions(view);
            view.ExpandItem(1);
            Assert.Equal((1, true, false), Assert.Single(payloads));
        }

        // [doc] as ExpandItemSendsTheItemIdAndExpanded, for a collapse.
        // Red on 54087224: the stub args answer (0, false, false), so the id differs (1 expected).
        [Fact]
        public void CollapseItemSendsTheItemIdAndCollapsed()
        {
            var view = NewTree();
            view.ExpandItem(1);
            var payloads = RecordExpansions(view);
            view.CollapseItem(1);
            Assert.Equal((1, false, false), Assert.Single(payloads));
        }

        // [doc] isAppliedToAllChildren tells whether expandAllChildren or collapseAllChildren is applied.
        // Red on 54087224: the stub args answer (0, false, false) for both calls.
        [Fact]
        public void ExpandingOrCollapsingAllChildrenSendsAppliedToAllChildren()
        {
            var view = NewTree();
            var payloads = RecordExpansions(view);
            view.ExpandItem(1, expandAllChildren: true);
            Assert.Equal((1, true, true), Assert.Single(payloads));
            payloads.Clear();
            view.CollapseItem(1, collapseAllChildren: true);
            Assert.Equal((1, false, true), Assert.Single(payloads));
        }

        // [doc] id "Returns -1 when expandAll() or collapseAll() is being called".
        // [choice] (Root R-B1 grant) isAppliedToAllChildren is true there, as the expansion applies
        // to every item and its children.
        // [choice] One event per call (as ItemExpandedChangedIsRaised).
        // Red on 54087224: the stub args answer (0, false, false), so the id differs (-1 expected).
        [Fact]
        public void ExpandAllSendsTheAggregateId()
        {
            var view = NewTree();
            var payloads = RecordExpansions(view);
            view.ExpandAll();
            Assert.Equal((-1, true, true), Assert.Single(payloads));
        }

        // [doc] as ExpandAllSendsTheAggregateId, for CollapseAll.
        // Red on 54087224: the stub args answer (0, false, false), so the id differs (-1 expected).
        [Fact]
        public void CollapseAllSendsTheAggregateId()
        {
            var view = NewTree();
            view.ExpandAll();
            var payloads = RecordExpansions(view);
            view.CollapseAll();
            Assert.Equal((-1, false, true), Assert.Single(payloads));
        }

        // [doc] id is the expanded item's id; 0 is a valid item id (it is not the -1 aggregate id).
        // The handler finds the item's data by that id.
        // Red on 54087224: the stub args answer (0, false, false); the id matches by chance, but
        // isExpanded is false, so Assert.Equal((0, true, false), ...) fails.
        [Fact]
        public void ExpandingTheItemWithIdZeroSendsIdZero()
        {
            TreeView view = null;
            view = new TreeView(() => new Label(), (e, i) => ((Label)e).text = view.GetItemDataForIndex<string>(i));
            view.SetRootItems(new List<TreeViewItemData<string>>
            {
                new TreeViewItemData<string>(0, "zero", new List<TreeViewItemData<string>> { new TreeViewItemData<string>(5, "five") }),
                new TreeViewItemData<string>(1, "one"),
            });
            var payloads = RecordExpansions(view);
            string dataDuring = null;
            view.itemExpandedChanged += args => dataDuring = view.GetItemDataForId<string>(args.id);
            view.ExpandItem(0);
            Assert.Equal((0, true, false), Assert.Single(payloads));
            Assert.Equal("zero", dataDuring);
        }

        // [measured] The expanded ancestors send their ids; a selected item with children stays
        // collapsed and sends nothing.
        [Fact]
        public void SelectionByIdExpansionSendsTheExpandedItemIds()
        {
            var view = NewTree();
            var payloads = RecordExpansions(view);
            view.SetSelectionById(121);
            // Ancestors are expanded from the nearest outwards (one ancestor is measured; the order
            // for several follows the same walk and is not measured).
            Assert.Equal(new[] { (12, true, false), (1, true, false) }, payloads);
            payloads.Clear();
            view.SetSelectionByIdWithoutNotify(new[] { 3 });
            Assert.Empty(payloads);
            view.CollapseAll();
            payloads.Clear();
            view.AddToSelectionById(3);
            Assert.Empty(payloads);
        }

        // ------------------------------------------------------------------ structure changes

        // [rule] SetRootItems reads the given list again when it is the same list, changed.
        [Fact]
        public void TheSameRootListIsReadAgain()
        {
            var view = NewTree();
            var roots = Sample();
            view.SetRootItems(roots);
            roots.Add(new TreeViewItemData<string>(4, "D"));
            view.SetRootItems(roots);
            Assert.Equal(new[] { 1, 2, 3, 4 }, VisibleIds(view));
            Assert.Equal("D", view.GetItemDataForId<string>(4));
        }

        // [rule] AddItem at the root, under a parent at a child index, with autoExpand; an unknown
        // parent or a repeated id adds nothing (logged); TryRemoveItem removes the descendants too.
        [Fact]
        public void AddAndRemoveItems()
        {
            var view = NewTree();
            view.AddItem(new TreeViewItemData<string>(4, "D"));
            Assert.Equal(new[] { 1, 2, 3, 4 }, VisibleIds(view));
            view.AddItem(new TreeViewItemData<string>(13, "A0"), 1, 0);
            Assert.Equal(new[] { 13, 11, 12 }, view.viewController.GetChildrenIds(1).ToArray());
            view.autoExpand = true;
            view.AddItem(new TreeViewItemData<string>(311, "C1a"), 31);
            Assert.True(view.IsExpanded(3) && view.IsExpanded(31));
            int before = view.GetTreeCount();
            var errors = new List<string>();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = new ErrorCapture(errors, previous);
            try
            {
                view.AddItem(new TreeViewItemData<string>(999, "orphan"), 12345);
                view.AddItem(new TreeViewItemData<string>(2, "B again"), 1);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            Assert.Equal(2, errors.Count);
            Assert.Equal(before, view.GetTreeCount());
            var controller = (DefaultTreeViewController<string>)view.viewController;
            Assert.Equal(new[] { 13, 11, 12 }, controller.GetTreeViewItemDataForId(1).children.Select(c => c.id).ToArray());
            Assert.True(view.TryRemoveItem(3));
            Assert.False(view.viewController.Exists(31));
            Assert.False(view.viewController.Exists(311));
            Assert.False(view.TryRemoveItem(3));
        }

        sealed class ErrorCapture : ILogHandler
        {
            readonly List<string> m_Errors;
            readonly ILogHandler m_Inner;
            public ErrorCapture(List<string> errors, ILogHandler inner) { m_Errors = errors; m_Inner = inner; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Error) m_Errors.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
                m_Inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => m_Inner.LogException(exception, context);
        }

        // [rule] --unity-tree-view-indent sets the indent per level; a changed value rebinds the rows,
        // an unchanged one does not.
        [Fact]
        public void TheCustomIndentPropertySetsTheIndent()
        {
            var panel = NewPanel();
            panel.visualTree.style.width = 300;
            panel.visualTree.style.height = 300;
            var binds = 0;
            TreeView view = null;
            view = new TreeView(() => new Label(), (e, i) => { binds++; ((Label)e).text = view.GetItemDataForIndex<string>(i); }) { fixedItemHeight = 20f };
            view.style.height = 200;
            view.SetRootItems(Sample());
            view.ExpandItem(1);
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            new UssImporter("tree-indent.uss").Import(sheet, ".unity-tree-view { --unity-tree-view-indent: 13px; } .wide { --unity-tree-view-indent: 21px; }");
            view.styleSheets.Add(sheet);
            panel.visualTree.Add(view);
            for (int i = 0; i < 3; i++) { panel.ApplyStyles(); panel.ValidateLayout(); }
            var child = view.activeItems.Single(r => r.index == 1);
            Assert.Equal(13f, ((ReusableTreeViewItem)child).m_IndentElement.style.width.value.value);

            int before = binds;
            view.AddToClassList("unrelated");
            for (int i = 0; i < 3; i++) { panel.ApplyStyles(); panel.ValidateLayout(); }
            Assert.Equal(before, binds);

            view.AddToClassList("wide");
            for (int i = 0; i < 3; i++) { panel.ApplyStyles(); panel.ValidateLayout(); }
            Assert.True(binds > before);
            Assert.Equal(21f, ((ReusableTreeViewItem)view.activeItems.Single(r => r.index == 1)).m_IndentElement.style.width.value.value);
        }

        // [rule] Move within a parent and to another parent.
        [Fact]
        public void MoveItems()
        {
            var view = NewTree();
            var controller = view.viewController;
            controller.Move(11, 1, 2);
            Assert.Equal(new[] { 12, 11 }, controller.GetChildrenIds(1).ToArray());
            controller.Move(2, 3, 0);
            Assert.Equal(new[] { 2, 31 }, controller.GetChildrenIds(3).ToArray());
            Assert.Equal(3, controller.GetParentId(2));
            controller.Move(1, 12);
            Assert.Equal(-1, controller.GetParentId(1));
        }

        // ------------------------------------------------------------------ UXML

        // [doc] Manual "TreeView": the <TreeView> element with its inherited attributes auto-expand,
        // fixed-item-height and selection-type.
        // Red on bc93e0fc: the element is created, but the stub constructor adds no unity-tree-view class.
        [Fact]
        public void UxmlCreatesTreeView()
        {
            var root = Clone("<ui:TreeView auto-expand=\"true\" fixed-item-height=\"20\" selection-type=\"Multiple\" />");
            var view = root.Q<TreeView>();
            Assert.NotNull(view);
            Assert.True(view.ClassListContains("unity-tree-view"));
            Assert.True(view.autoExpand);
            Assert.Equal(20f, view.fixedItemHeight);
            Assert.Equal(SelectionType.Multiple, view.selectionType);
            view.SetRootItems(Sample());
            Assert.Equal(7, view.itemsSource.Count);
        }

        // ------------------------------------------------------------------ out of scope stays loud

        // Guard (not a red/green control): the tree's drag and drop controller refuses drags and says
        // so when reordering is asked for.
        [Fact]
        public void TreeReorderingIsRefused()
        {
            var tree = new TreeView();
            Assert.IsType<UaUnsupportedReorderController>(tree.dragger.dragAndDropController);
            Assert.False(tree.dragger.dragAndDropController.CanStartDrag(new[] { 0 }));
        }
    }
}
