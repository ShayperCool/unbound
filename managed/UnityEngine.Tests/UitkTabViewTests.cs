using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// TabView and Tab. Each test names its source:
//   [doc]      a statement or example of the Unity 6000.0 Manual pages "TabView" and "Tab", the
//              Scripting API pages of TabView, Tab and their members (USS class tables included);
//   [measured] the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (tabs section): structure
//              and names, selection by header press, selected/activeTabChanged order, closing,
//              removing the active tab, a tab added later;
//   [rule]     the same rules applied to a case the capture does not press (Clear, ReorderTab).

namespace UnityEngine.Tests
{
    [Collection("UXML custom controls")]
    public class UitkTabViewTests
    {
        static Panel NewPanel() => new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        static VisualElement Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "UitkTabViewProbe";
            UxmlImporter.Import("Assets/UI/UitkTabViewProbe.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return root;
        }

        // A pointer press on an element, as a mouse press sends it.
        static void Press(VisualElement target)
        {
            using var e = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, clickCount = 1 });
            e.target = target;
            target.SendEvent(e);
        }

        static bool IsChecked(VisualElement e) => (e.pseudoStates & PseudoStates.Checked) == PseudoStates.Checked;

        static (TabView View, Tab A, Tab B, Tab C) ThreeTabs()
        {
            var view = new TabView();
            var a = new Tab("A");
            var b = new Tab("B");
            var c = new Tab("C");
            view.Add(a);
            view.Add(b);
            view.Add(c);
            return (view, a, b, c);
        }

        // ------------------------------------------------------------------ USS classes

        // [doc] TabView USS class table (unity-tab-view, __content-container, __reorderable,
        // __vertical) and the theme's header container class (unity-tab-view__header-container).
        // Red on 59ec037a: the stub's ussClassName is null (first Assert.Equal fails).
        [Fact]
        public void TabViewHasTheDocumentedUssClasses()
        {
            Assert.Equal("unity-tab-view", TabView.ussClassName);
            Assert.Equal("unity-tab-view__header-container", TabView.headerContainerClassName);
            Assert.Equal("unity-tab-view__content-container", TabView.contentContainerUssClassName);
            Assert.Equal("unity-tab-view__reorderable", TabView.reorderableUssClassName);
            Assert.Equal("unity-tab-view__vertical", TabView.verticalUssClassName);
            var view = new TabView();
            Assert.True(view.ClassListContains("unity-tab-view"));
            Assert.NotNull(view.Q(null, "unity-tab-view__header-container"));
            Assert.True(view.contentContainer.ClassListContains("unity-tab-view__content-container"));
            Assert.False(view.ClassListContains("unity-tab-view__reorderable"));
        }

        // [doc] Tab USS class table (unity-tab, __header, __header-image(--empty, --standalone),
        // __header-label(--empty), __header-underline, __content-container, --dragging,
        // __reorderable, __reorderable-handle, __reorderable-handle-bar, __header__closeable,
        // __close-button); the underline is a direct child of the header (Manual USS example
        // `.unity-tab__header:checked > .unity-tab__header-underline`).
        // Red on 59ec037a: the stub's ussClassName is null (first Assert.Equal fails).
        [Fact]
        public void TabHasTheDocumentedUssClasses()
        {
            Assert.Equal("unity-tab", Tab.ussClassName);
            Assert.Equal("unity-tab__header", Tab.tabHeaderUssClassName);
            Assert.Equal("unity-tab__header-image", Tab.tabHeaderImageUssClassName);
            Assert.Equal("unity-tab__header-image--empty", Tab.tabHeaderEmptyImageUssClassName);
            Assert.Equal("unity-tab__header-image--standalone", Tab.tabHeaderStandaloneImageUssClassName);
            Assert.Equal("unity-tab__header-label", Tab.tabHeaderLabelUssClassName);
            Assert.Equal("unity-tab__header-label--empty", Tab.tabHeaderEmptyLabeUssClassName);
            Assert.Equal("unity-tab__header-underline", Tab.tabHeaderUnderlineUssClassName);
            Assert.Equal("unity-tab__content-container", Tab.contentUssClassName);
            Assert.Equal("unity-tab--dragging", Tab.draggingUssClassName);
            Assert.Equal("unity-tab__reorderable", Tab.reorderableUssClassName);
            Assert.Equal("unity-tab__reorderable-handle", Tab.reorderableItemHandleUssClassName);
            Assert.Equal("unity-tab__reorderable-handle-bar", Tab.reorderableItemHandleBarUssClassName);
            Assert.Equal("unity-tab__header__closeable", Tab.closeableUssClassName);
            Assert.Equal("unity-tab__close-button", Tab.closeButtonUssClassName);

            var tab = new Tab("Label");
            Assert.True(tab.ClassListContains("unity-tab"));
            Assert.True(tab.tabHeader.ClassListContains("unity-tab__header"));
            Assert.True(tab.contentContainer.ClassListContains("unity-tab__content-container"));
            Assert.NotNull(tab.tabHeader.Q(null, "unity-tab__header-image"));
            var label = Assert.IsType<Label>(tab.tabHeader.Q(null, "unity-tab__header-label"));
            Assert.Equal("Label", label.text);
            Assert.Contains(tab.tabHeader.hierarchy.Children(), e => e.ClassListContains("unity-tab__header-underline"));
        }

        // [doc] The --empty and --standalone class descriptions. [measured] The icon Image and label
        // are always present; a tab constructed without a label keeps no --empty class until its
        // label is set to an empty string.
        [Fact]
        public void HeaderClassesFollowLabelAndIcon()
        {
            var tab = new Tab("Text");
            var image = tab.tabHeader.Q(null, "unity-tab__header-image");
            var label = tab.tabHeader.Q(null, "unity-tab__header-label");
            Assert.Equal("unity-tab__header-image", image.name);
            Assert.Equal("unity-tab__header-label", label.name);
            Assert.True(image.ClassListContains("unity-tab__header-image--empty"));
            Assert.False(image.ClassListContains("unity-tab__header-image--standalone"));
            Assert.False(label.ClassListContains("unity-tab__header-label--empty"));
            tab.label = "";
            Assert.True(image.ClassListContains("unity-tab__header-image--standalone"));
            Assert.True(label.ClassListContains("unity-tab__header-label--empty"));
            tab.label = "Again";
            Assert.Equal("Again", tab.label);
            Assert.False(label.ClassListContains("unity-tab__header-label--empty"));
            Assert.False(new Tab().tabHeader.Q(null, "unity-tab__header-label").ClassListContains("unity-tab__header-label--empty"));
        }

        // ------------------------------------------------------------------ adding tabs

        // [doc] Manual C# example: tabs are added with TabView.Add, their content with Tab.Add.
        // [measured] Each header moves, in tab order, into the header container (named after its
        // class); the Tab elements stay in the content container; GetTab/GetTabHeader answer null out
        // of range.
        [Fact]
        public void AddingTabsBuildsTheHeaderRowAndTheContent()
        {
            var view = new TabView();
            var one = new Tab("One");
            var oneContent = new Label("first");
            one.Add(oneContent);
            var two = new Tab("Two");
            view.Add(one);
            view.Add(two);

            var headerRow = view.Q("unity-tab-view__header-container");
            Assert.True(headerRow.ClassListContains("unity-tab-view__header-container"));
            Assert.Equal(new[] { one.tabHeader, two.tabHeader }, headerRow.hierarchy.Children().ToArray());
            Assert.Equal(new VisualElement[] { one, two }, view.contentContainer.hierarchy.Children().ToArray());
            Assert.Equal("unity-tab-view__content-container", view.contentContainer.name);
            Assert.Same(one.contentContainer, oneContent.hierarchy.parent);
            Assert.Same(one, view.GetTab(0));
            Assert.Same(two.tabHeader, view.GetTabHeader(1));
            Assert.Null(view.GetTab(2));
            Assert.Null(view.GetTabHeader(-1));
        }

        // [measured] The first tab added becomes active: it and its header are :checked (the theme
        // shows only the checked tab); a non-Tab child is not a tab.
        [Fact]
        public void FirstAddedTabIsActiveAndTheOthersAreUnchecked()
        {
            var (view, a, b, c) = ThreeTabs();
            view.Add(new Label("not a tab"));
            Assert.Same(a, view.activeTab);
            Assert.Equal(0, view.selectedTabIndex);
            Assert.True(IsChecked(a) && IsChecked(a.tabHeader));
            Assert.False(IsChecked(b) || IsChecked(b.tabHeader));
            Assert.False(IsChecked(c));
            Assert.Equal(3, view.tabs.Count);
        }

        // [rule] Inserting a tab places its header at the tab's index among the tabs.
        [Fact]
        public void InsertedTabTakesItsIndex()
        {
            var (view, a, b, _) = ThreeTabs();
            var inserted = new Tab("Inserted");
            view.Insert(1, inserted);
            Assert.Same(inserted, view.GetTab(1));
            Assert.Same(inserted.tabHeader, view.GetTabHeader(1));
            Assert.Same(b, view.GetTab(2));
            Assert.Same(a, view.activeTab);
        }

        // ------------------------------------------------------------------ selection

        // [doc] activeTab deselects the previous tab; activeTabChanged receives (previous, new).
        // [measured] Setting activeTab or selectedTabIndex in code sends activeTabChanged only (no
        // Tab.selected); setting the active tab again sends nothing.
        [Fact]
        public void SettingActiveTabDeselectsThePreviousAndSendsEvents()
        {
            var (view, a, b, _) = ThreeTabs();
            var log = new List<string>();
            a.selected += t => log.Add("selected " + t.label);
            b.selected += t => log.Add("selected " + t.label);
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");

            view.activeTab = b;
            Assert.Equal(new[] { "changed A>B" }, log);
            Assert.Same(b, view.activeTab);
            Assert.Equal(1, view.selectedTabIndex);
            Assert.False(IsChecked(a.tabHeader));
            Assert.True(IsChecked(b.tabHeader));

            view.activeTab = b;
            view.selectedTabIndex = 1;
            Assert.Single(log);
        }

        // [doc] selectedTabIndex: "Setting a valid index will set the Tab at the given index to
        // active". [measured rule] Out-of-range indices are ignored; activeTab = null with tabs throws
        // NullReferenceException and a tab of another view throws.
        [Fact]
        public void SelectedTabIndexSelectsAndInvalidActiveTabsThrow()
        {
            var (view, a, _, c) = ThreeTabs();
            var changes = 0;
            view.activeTabChanged += (_, __) => changes++;
            view.selectedTabIndex = 2;
            Assert.Same(c, view.activeTab);
            view.selectedTabIndex = 3;
            view.selectedTabIndex = -1;
            Assert.Throws<NullReferenceException>(() => view.activeTab = null);
            Assert.Throws<Exception>(() => view.activeTab = new Tab("foreign"));
            Assert.Same(c, view.activeTab);
            Assert.Equal(1, changes);
            view.activeTab = a;
            Assert.Equal(0, view.selectedTabIndex);
        }

        // [measured] A pointer press on a header sends Tab.selected (also for the active tab), then
        // the TabView activates it (activeTabChanged only on a change).
        [Fact]
        public void PressingAHeaderSelectsItsTab()
        {
            using var panel = NewPanel();
            var (view, a, b, _) = ThreeTabs();
            panel.visualTree.Add(view);
            var log = new List<string>();
            b.selected += t => log.Add("selected " + t.label);
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");
            Press(b.tabHeader);
            Assert.Same(b, view.activeTab);
            Press(b.tabHeader);
            // The TabView subscribed to selected when the tab was added, before this test did, so it
            // activates the tab first (the probe subscribes before adding: selected comes first there).
            Assert.Equal(new[] { "changed A>B", "selected B", "selected B" }, log);
        }

        // ------------------------------------------------------------------ closing

        // [doc] closeable shows a close icon; closed and tabClosed (tab, index) follow closing.
        // [measured] The close button (a press on it) asks closing, removes the tab, then sends
        // closed and tabClosed; removing the active tab activates the first remaining one
        // (activeTabChanged before closed); the press does not select the tab.
        [Fact]
        public void CloseableTabClosesWithItsEvents()
        {
            using var panel = NewPanel();
            var (view, a, b, c) = ThreeTabs();
            panel.visualTree.Add(view);
            b.closeable = true;
            Assert.True(b.tabHeader.ClassListContains("unity-tab__header__closeable"));
            var close = b.tabHeader.Q("unity-tab__close-button");
            Assert.NotNull(close);
            Assert.Null(a.tabHeader.Q(null, "unity-tab__close-button"));

            var log = new List<string>();
            b.selected += t => log.Add("selected " + t.label);
            b.closing += () => { log.Add("closing"); return true; };
            b.closed += t => log.Add("closed " + t.label);
            view.tabClosed += (t, index) => log.Add($"tabClosed {t.label} {index}");
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");

            Press(close);
            // closed handlers run in subscription order: the TabView's (tabClosed) came first here.
            Assert.Equal(new[] { "closing", "tabClosed B 1", "closed B" }, log);
            Assert.Same(a, view.activeTab);
            Assert.Null(b.parent);
            Assert.Equal(new[] { a, c }, view.tabs);
            Assert.Same(b, b.tabHeader.hierarchy.parent);

            log.Clear();
            view.activeTab = c;
            log.Clear();
            c.closeable = true;
            c.closed += t => log.Add("closed " + t.label);
            Press(c.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.Equal(new[] { "changed C>A", "tabClosed C 1", "closed C" }, log);
            Assert.Same(a, view.activeTab);

            a.closeable = true;
            a.closeable = false;
            Assert.Null(a.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.False(a.tabHeader.ClassListContains("unity-tab__header__closeable"));
        }

        // [doc] closing: "all of them will be executed synchronously but only the last value will be
        // taken into account."
        [Fact]
        public void OnlyTheLastClosingAnswerCounts()
        {
            using var panel = NewPanel();
            var (view, _, b, _) = ThreeTabs();
            panel.visualTree.Add(view);
            b.closeable = true;
            var calls = new List<string>();
            var closedCount = 0;
            b.closed += _ => closedCount++;
            Func<bool> yes = () => { calls.Add("yes"); return true; };
            Func<bool> no = () => { calls.Add("no"); return false; };
            b.closing += yes;
            b.closing += no;
            Press(b.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.Equal(new[] { "yes", "no" }, calls);
            Assert.Equal(0, closedCount);
            Assert.Contains(b, view.tabs);

            b.closing -= yes;
            b.closing += yes;
            calls.Clear();
            Press(b.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.Equal(new[] { "no", "yes" }, calls);
            Assert.Equal(1, closedCount);
            Assert.DoesNotContain(b, view.tabs);
        }

        // [measured] Removing the active tab by code activates the first remaining tab without
        // closing events and gives the tab its header back, unchecked.
        [Fact]
        public void RemovingTheActiveTabByCodeSelectsTheFirstRemaining()
        {
            var (view, a, b, c) = ThreeTabs();
            view.activeTab = b;
            var closed = 0;
            view.tabClosed += (_, __) => closed++;
            b.RemoveFromHierarchy();
            Assert.Same(a, view.activeTab);
            Assert.Equal(0, closed);
            Assert.Same(b, b.tabHeader.hierarchy.parent);
            Assert.False(IsChecked(b) || IsChecked(b.tabHeader));
            Assert.Same(c, view.GetTab(1));
        }

        // [rule] Clear removes the tabs one by one: each removal of the active tab activates the
        // first remaining one; the last removal leaves no active tab without an event.
        [Fact]
        public void ClearActivatesEachRemainingTabInTurn()
        {
            var (view, a, b, c) = ThreeTabs();
            var log = new List<string>();
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");

            view.Clear();
            Assert.Equal(new[] { "changed A>B", "changed B>C" }, log);
            Assert.Null(view.activeTab);
            Assert.Equal(-1, view.selectedTabIndex);
            Assert.Empty(view.tabs);
            Assert.Empty(view.Q("unity-tab-view__header-container").hierarchy.Children());
            foreach (var tab in new[] { a, b, c })
            {
                Assert.Same(tab, tab.tabHeader.hierarchy.parent);
                Assert.False(IsChecked(tab.tabHeader));
            }
        }

        // ------------------------------------------------------------------ reordering

        // [doc] ReorderTab moves a tab; tabReordered passes (from, to); reorderable adds dragging.
        // [rule] Only a reorderable TabView reorders; the tab ends at index `to` with its header;
        // the drag handle (first in the header) holds two bars, the first marked --left.
        [Fact]
        public void ReorderTabMovesTheTabAndItsHeader()
        {
            var (view, a, b, c) = ThreeTabs();
            var moves = new List<(int, int)>();
            view.tabReordered += (from, to) => moves.Add((from, to));
            view.ReorderTab(0, 2);
            Assert.Empty(moves);

            view.reorderable = true;
            Assert.True(view.ClassListContains("unity-tab-view__reorderable"));
            view.ReorderTab(0, 2);
            Assert.Equal(new[] { b, c, a }, view.tabs);
            Assert.Equal(new[] { b.tabHeader, c.tabHeader, a.tabHeader }, view.Q("unity-tab-view__header-container").hierarchy.Children().ToArray());
            Assert.Equal(new VisualElement[] { b, c, a }, view.contentContainer.hierarchy.Children().ToArray());
            Assert.Equal(new[] { (0, 2) }, moves);
            Assert.Same(a, view.activeTab);
            Assert.Equal(2, view.selectedTabIndex);
            view.ReorderTab(1, 1);
            Assert.Single(moves);

            var handle = a.tabHeader.hierarchy[0];
            Assert.Equal("unity-tab__reorderable-handle", handle.name);
            var bars = handle.hierarchy.Children().ToArray();
            Assert.Equal(2, bars.Length);
            Assert.True(bars[0].ClassListContains("unity-tab__reorderable-handle-bar--left"));
            Assert.False(bars[1].ClassListContains("unity-tab__reorderable-handle-bar--left"));
            view.reorderable = false;
            Assert.Null(a.tabHeader.Q("unity-tab__reorderable-handle"));
        }

        // ------------------------------------------------------------------ UXML

        // [doc] The Manual's UXML example: <TabView> with <Tab label=...> children holding Labels;
        // the documented attributes reorderable (TabView), label and closeable (Tab).
        // [choice] The first tab is active.
        // Red on 59ec037a: the builder adds each Tab's Label child before adding the Tab to the
        // TabView, so the stub Tab (null contentContainer) throws InvalidOperationException out of
        // CloneTree.
        [Fact]
        public void UxmlCreatesTabViewAndTabs()
        {
            var root = Clone(
                "<ui:TabView reorderable=\"true\">"
                + "<ui:Tab label=\"UXML Tab A\"><ui:Label text=\"first\" /></ui:Tab>"
                + "<ui:Tab label=\"UXML Tab B\" closeable=\"true\"><ui:Label text=\"second\" /></ui:Tab>"
                + "</ui:TabView>");
            var view = Assert.IsType<TabView>(root[0]);
            Assert.True(view.reorderable);
            Assert.Equal(2, view.tabs.Count);
            var a = view.GetTab(0);
            var b = view.GetTab(1);
            Assert.Equal("UXML Tab A", a.label);
            Assert.Equal("UXML Tab B", b.label);
            Assert.False(a.closeable);
            Assert.True(b.closeable);
            Assert.Equal("first", Assert.IsType<Label>(a.contentContainer[0]).text);
            Assert.Equal("second", Assert.IsType<Label>(b.contentContainer[0]).text);
            Assert.Same(a.tabHeader, view.GetTabHeader(0));
            Assert.Same(a, view.activeTab);
            Assert.NotNull(b.tabHeader.Q(null, "unity-tab__close-button"));
        }
    }
}
