using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// TabView and Tab. Each test names its source:
//   [doc]    a statement or example of the Unity 6000.0 Manual pages "TabView" and "Tab" (UI Toolkit
//            control reference, with their UXML attribute and USS class tables), the Scripting API
//            pages of TabView, Tab and their members, or the class names of Unity's shipped default
//            theme (TabView.headerContainerClassName, whose value the Manual table omits);
//   [choice] Unbound's choice where those pages say nothing (initial selection, event order,
//            selection after closing, element structure). These check Unbound's implementation, not
//            Unity behaviour.
// No reference capture covers these controls; nothing here is a measured Unity value.
// Red base: 59ec037a, where both types are generated stubs (their members do nothing and answer
// defaults; contentContainer answers null, so Add throws InvalidOperationException). The file uses
// only members that exist there, so it is expected to compile on that base (unexecuted).

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

        static void Click(VisualElement target)
        {
            using var e = ClickEvent.GetPooled();
            e.target = target;
            target.SendEvent(e);
        }

        static bool IsChecked(VisualElement e) => (e.pseudoStates & PseudoStates.Checked) == PseudoStates.Checked;

        static bool IsHidden(VisualElement e) => e.style.display.keyword == StyleKeyword.Undefined && e.style.display.value == DisplayStyle.None;

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

        // [doc] The --empty and --standalone class descriptions: the icon "when the value is null",
        // the icon "when the label is empty or null", the label "when the value is empty or null".
        // [choice] The icon Image and the label are always present.
        // Red on 59ec037a: the stub's tabHeader is null, so the first query on it throws.
        [Fact]
        public void HeaderClassesFollowLabelAndIcon()
        {
            var tab = new Tab("Text");
            var image = tab.tabHeader.Q(null, "unity-tab__header-image");
            var label = tab.tabHeader.Q(null, "unity-tab__header-label");
            Assert.True(image.ClassListContains("unity-tab__header-image--empty"));
            Assert.False(image.ClassListContains("unity-tab__header-image--standalone"));
            Assert.False(label.ClassListContains("unity-tab__header-label--empty"));
            tab.label = "";
            Assert.True(image.ClassListContains("unity-tab__header-image--standalone"));
            Assert.True(label.ClassListContains("unity-tab__header-label--empty"));
            tab.label = "Again";
            Assert.Equal("Again", tab.label);
            Assert.False(label.ClassListContains("unity-tab__header-label--empty"));
            Assert.True(new Tab().tabHeader.Q(null, "unity-tab__header-label").ClassListContains("unity-tab__header-label--empty"));
        }

        // ------------------------------------------------------------------ adding tabs

        // [doc] Manual C# example: tabs are added with TabView.Add, their content with Tab.Add.
        // [choice] Each header moves, in tab order, into the header container; the Tab elements stay
        // in the content container; GetTab/GetTabHeader answer them by index.
        // Red on 59ec037a: the stub Tab's contentContainer is null, so the first call, Tab.Add of the
        // tab's content, throws InvalidOperationException.
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

            var headerRow = view.Q(null, "unity-tab-view__header-container");
            Assert.Equal(new[] { one.tabHeader, two.tabHeader }, headerRow.hierarchy.Children().ToArray());
            Assert.Equal(new VisualElement[] { one, two }, view.contentContainer.hierarchy.Children().ToArray());
            Assert.Same(one.contentContainer, oneContent.hierarchy.parent);
            Assert.Same(one, view.GetTab(0));
            Assert.Same(two, view.GetTab(1));
            Assert.Same(one.tabHeader, view.GetTabHeader(0));
            Assert.Same(two.tabHeader, view.GetTabHeader(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.GetTab(2));
        }

        // [choice] The first tab added becomes active (header :checked, displayed); the others are
        // hidden with an inline display: none; a non-Tab child is not a tab.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void FirstAddedTabIsActiveAndTheOthersAreHidden()
        {
            var (view, a, b, c) = ThreeTabs();
            view.Add(new Label("not a tab"));
            Assert.Same(a, view.activeTab);
            Assert.Equal(0, view.selectedTabIndex);
            Assert.True(IsChecked(a.tabHeader));
            Assert.False(IsChecked(b.tabHeader));
            Assert.False(IsHidden(a));
            Assert.True(IsHidden(b));
            Assert.True(IsHidden(c));
            Assert.Equal(3, view.tabs.Count);
        }

        // [choice] Inserting a tab places its header at the tab's index among the tabs.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
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

        // [doc] activeTab: "Setting a valid tab will also deselect the previous active tab before
        // setting the newly assigned tab to active"; activeTabChanged receives the previously
        // selected tab then the newly selected tab; Tab.selected is called when a tab is selected.
        // [choice] selected is sent before activeTabChanged; setting the active tab again sends
        // nothing.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void SettingActiveTabDeselectsThePreviousAndSendsEvents()
        {
            var (view, a, b, _) = ThreeTabs();
            var log = new List<string>();
            a.selected += t => log.Add("selected " + t.label);
            b.selected += t => log.Add("selected " + t.label);
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");

            view.activeTab = b;
            Assert.Equal(new[] { "selected B", "changed A>B" }, log);
            Assert.Same(b, view.activeTab);
            Assert.Equal(1, view.selectedTabIndex);
            Assert.False(IsChecked(a.tabHeader));
            Assert.True(IsChecked(b.tabHeader));
            Assert.True(IsHidden(a));
            Assert.False(IsHidden(b));

            view.activeTab = b;
            Assert.Equal(2, log.Count);
        }

        // [doc] selectedTabIndex: "Setting a valid index will set the Tab at the given index to
        // active". [choice] An index out of range, null or a foreign tab changes nothing.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void SelectedTabIndexSelectsAndInvalidValuesAreIgnored()
        {
            var (view, a, _, c) = ThreeTabs();
            var changes = 0;
            view.activeTabChanged += (_, __) => changes++;
            view.selectedTabIndex = 2;
            Assert.Same(c, view.activeTab);
            view.selectedTabIndex = 3;
            view.selectedTabIndex = -1;
            view.activeTab = null;
            view.activeTab = new Tab("foreign");
            Assert.Same(c, view.activeTab);
            Assert.Equal(1, changes);
            view.activeTab = a;
            Assert.Equal(0, view.selectedTabIndex);
        }

        // [doc] "A Tab has a header that can respond to pointer and mouse events"; the TabView
        // displays the active tab. [choice] A ClickEvent on a header selects its tab.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void ClickingAHeaderSelectsItsTab()
        {
            using var panel = NewPanel();
            var (view, a, b, _) = ThreeTabs();
            panel.visualTree.Add(view);
            var changes = new List<(Tab, Tab)>();
            view.activeTabChanged += (previous, next) => changes.Add((previous, next));
            Click(b.tabHeader);
            Assert.Same(b, view.activeTab);
            Assert.Equal(new[] { (a, b) }, changes);
        }

        // ------------------------------------------------------------------ closing

        // [doc] closeable: "a close icon appears on the Tab. If a user selects the close icon, the
        // Tab closes"; closed is called when a tab has been closed; tabClosed passes the closed tab
        // and its index; closeableUssClassName and closeButtonUssClassName.
        // [choice] The close button is in the header, which gets the closeable class; closing the
        // active tab activates the tab now at its index (activeTabChanged before closed), then
        // closed, then tabClosed; a click on the close button does not select its tab.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void CloseableTabClosesWithItsEvents()
        {
            using var panel = NewPanel();
            var (view, a, b, c) = ThreeTabs();
            panel.visualTree.Add(view);
            b.closeable = true;
            Assert.True(b.closeable);
            Assert.True(b.tabHeader.ClassListContains("unity-tab__header__closeable"));
            var close = b.tabHeader.Q(null, "unity-tab__close-button");
            Assert.NotNull(close);
            Assert.Null(a.tabHeader.Q(null, "unity-tab__close-button"));

            var log = new List<string>();
            b.closing += () => { log.Add("closing"); return true; };
            b.closed += t => log.Add("closed " + t.label);
            view.tabClosed += (t, index) => log.Add($"tabClosed {t.label} {index}");
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");

            Click(close);
            Assert.Equal(new[] { "closing", "closed B", "tabClosed B 1" }, log);
            Assert.Same(a, view.activeTab);
            Assert.Null(b.parent);
            Assert.Equal(new[] { a, c }, view.tabs);
            Assert.Equal(new[] { a.tabHeader, c.tabHeader }, view.Q(null, "unity-tab-view__header-container").hierarchy.Children().ToArray());
            Assert.Same(b, b.tabHeader.hierarchy.parent);

            log.Clear();
            a.closeable = true;
            a.closed += t => log.Add("closed " + t.label);
            Click(a.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.Equal(new[] { "changed A>C", "closed A", "tabClosed A 0" }, log);
            Assert.Same(c, view.activeTab);

            c.closeable = false;
            Assert.Null(c.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.False(c.tabHeader.ClassListContains("unity-tab__header__closeable"));
        }

        // [doc] closing: "Return true if the tab can be closed. In the case where more than one
        // event are assigned, all of them will be executed synchronously but only the last value
        // will be taken into account."
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
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
            Click(b.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.Equal(new[] { "yes", "no" }, calls);
            Assert.Equal(0, closedCount);
            Assert.Contains(b, view.tabs);

            b.closing -= yes;
            b.closing += yes;
            calls.Clear();
            Click(b.tabHeader.Q(null, "unity-tab__close-button"));
            Assert.Equal(new[] { "no", "yes" }, calls);
            Assert.Equal(1, closedCount);
            Assert.DoesNotContain(b, view.tabs);
        }

        // [choice] Removing the active tab by code activates the next tab without closing events,
        // and gives the tab its header back.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void RemovingTheActiveTabByCodeSelectsTheNextOne()
        {
            var (view, a, b, _) = ThreeTabs();
            var closed = 0;
            view.tabClosed += (_, __) => closed++;
            a.RemoveFromHierarchy();
            Assert.Same(b, view.activeTab);
            Assert.Equal(0, view.selectedTabIndex);
            Assert.Equal(0, closed);
            Assert.Same(a, a.tabHeader.hierarchy.parent);
            Assert.False(IsHidden(a));
            Assert.False(IsChecked(a.tabHeader));
        }

        // [choice] Only a tab still in the TabView can become active. Clear() removes every tab at
        // once, so it sends one activeTabChanged (active tab, null) and selects no removed tab.
        // Red on 85fcc53e (own red base, not 59ec037a): Clear re-activated each removed tab in turn,
        // so the log is ["selected B", "changed A>B", "selected C", "changed B>C", "changed C>"]
        // (the first Assert.Equal fails).
        [Fact]
        public void ClearSendsOneChangeAndSelectsNoRemovedTab()
        {
            var (view, a, b, c) = ThreeTabs();
            var log = new List<string>();
            b.selected += t => log.Add("selected " + t.label);
            c.selected += t => log.Add("selected " + t.label);
            view.activeTabChanged += (previous, next) => log.Add($"changed {previous?.label}>{next?.label}");

            view.Clear();
            Assert.Equal(new[] { "changed A>" }, log);
            Assert.Null(view.activeTab);
            Assert.Equal(-1, view.selectedTabIndex);
            Assert.Empty(view.tabs);
            Assert.Empty(view.Q(null, "unity-tab-view__header-container").hierarchy.Children());
            Assert.Empty(view.contentContainer.hierarchy.Children());
            foreach (var tab in new[] { a, b, c })
            {
                Assert.Same(tab, tab.tabHeader.hierarchy.parent);
                Assert.False(IsChecked(tab.tabHeader));
                Assert.False(IsHidden(tab));
            }
        }

        // ------------------------------------------------------------------ reordering

        // [doc] ReorderTab "Moves the tab from the specified index to a new index"; tabReordered
        // passes the source then the destination index; reorderable "adds dragging support to tabs"
        // and the reorderable classes. [choice] The tab ends at index `to` with its header; the
        // reorderable header holds a drag handle with two bars; invalid indices change nothing.
        // Red on 59ec037a: TabView.Add throws InvalidOperationException (null contentContainer).
        [Fact]
        public void ReorderTabMovesTheTabAndItsHeader()
        {
            var (view, a, b, c) = ThreeTabs();
            var moves = new List<(int, int)>();
            view.tabReordered += (from, to) => moves.Add((from, to));
            view.ReorderTab(0, 2);
            Assert.Equal(new[] { b, c, a }, view.tabs);
            Assert.Equal(new[] { b.tabHeader, c.tabHeader, a.tabHeader }, view.Q(null, "unity-tab-view__header-container").hierarchy.Children().ToArray());
            Assert.Equal(new VisualElement[] { b, c, a }, view.contentContainer.hierarchy.Children().ToArray());
            Assert.Equal(new[] { (0, 2) }, moves);
            Assert.Same(a, view.activeTab);
            Assert.Equal(2, view.selectedTabIndex);
            view.ReorderTab(1, 1);
            view.ReorderTab(0, 3);
            Assert.Single(moves);

            view.reorderable = true;
            Assert.True(view.reorderable);
            Assert.True(view.ClassListContains("unity-tab-view__reorderable"));
            var handle = a.tabHeader.Q(null, "unity-tab__reorderable-handle");
            Assert.NotNull(handle);
            Assert.Equal(2, handle.hierarchy.Children().Count(e => e.ClassListContains("unity-tab__reorderable-handle-bar")));
            Assert.True(a.tabHeader.ClassListContains("unity-tab__reorderable"));
            view.reorderable = false;
            Assert.Null(a.tabHeader.Q(null, "unity-tab__reorderable-handle"));
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
