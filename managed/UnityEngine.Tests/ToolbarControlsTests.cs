using System;
using System.Linq;
using Unbound.Runtime;
using Unbound.UIElements;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The editor toolbar controls (tests/projects/toolbarcontrols records them in the
    /// Editor): the search field's cancel button and clearing, the menu's report, breadcrumbs, and
    /// the UXML attributes their data declares.</summary>
    public class ToolbarControlsTests
    {
        static Panel NewPanel() => new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        static VisualElement Clone(string body)
        {
            var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
            asset.name = "ToolbarControls";
            UxmlImporter.Import("Assets/UI/ToolbarControls.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\">" + body + "</ui:UXML>", asset);
            var root = new VisualElement();
            asset.CloneTree(root);
            return root;
        }

        static bool CancelOff(ToolbarSearchField field) =>
            field.Q<Button>("unity-cancel").ClassListContains(ToolbarSearchField.cancelButtonOffVariantUssClassName);

        [Fact]
        public void TheSearchFieldsCancelButtonFollowsItsTextAndClearsIt()
        {
            var panel = NewPanel();
            var field = new ToolbarSearchField();
            panel.visualTree.Add(field);
            var changes = new System.Collections.Generic.List<string>();
            field.RegisterValueChangedCallback(evt => changes.Add(evt.previousValue + ">" + evt.newValue));
            Assert.True(CancelOff(field));

            field.value = "abc";
            Assert.False(CancelOff(field));
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                var cancel = field.Q<Button>("unity-cancel");
                submit.target = cancel;
                cancel.SendEvent(submit);
            }
            Assert.Equal(string.Empty, field.value);
            Assert.True(CancelOff(field));

            field.SetValueWithoutNotify("quiet");
            Assert.False(CancelOff(field));
            var input = field.Q(TextInputBaseField<string>.textInputUssName);
            using (var escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                escape.target = input;
                input.SendEvent(escape);
            }
            Assert.Equal(string.Empty, field.value);
            Assert.Equal(new[] { ">abc", "abc>", "quiet>" }, changes);
        }

        [Fact]
        public void AMenuWithItemsIsReportedNotShown()
        {
            const string member = "UnityEditor.UIElements.ToolbarMenuElementExtensions.ShowMenu (the Editor's menu)";
            int Hits() => Stubs.HitReport().FirstOrDefault(h => h.Key == member).Value;
            var menu = new ToolbarMenu();
            int before = Hits();
            menu.ShowMenu();
            Assert.Equal(before, Hits());
            menu.menu.AppendAction("Open", _ => { });
            menu.ShowMenu();
            Assert.Equal(before + 1, Hits());
        }

        [Fact]
        public void BreadcrumbsMarkTheFirstItemAndPopOnlyItems()
        {
            var crumbs = new ToolbarBreadcrumbs();
            crumbs.PushItem("Root");
            crumbs.PushItem("Child");
            Assert.True(crumbs[0].ClassListContains(ToolbarBreadcrumbs.firstItemClassName));
            Assert.False(crumbs[1].ClassListContains(ToolbarBreadcrumbs.firstItemClassName));
            Assert.All(crumbs.Children(), c => Assert.True(c.ClassListContains(ToolbarBreadcrumbs.itemClassName)));
            Assert.All(crumbs.Children(), c => Assert.False(c.ClassListContains(ToolbarButton.ussClassName)));
            crumbs.PopItem();
            Assert.Equal(1, crumbs.childCount);
            crumbs.Add(new Label("other"));
            var error = Assert.Throws<InvalidOperationException>(() => crumbs.PopItem());
            Assert.Equal("Last child isn't a BreadcrumbItem", error.Message);
        }

        [Fact]
        public void UxmlSetsOnlyTheAttributesTheControlsDataDeclares()
        {
            var root = Clone("<uie:ToolbarMenu name=\"menu\" text=\"File\" variant=\"Popup\" />"
                + "<uie:ToolbarSpacer name=\"spacer\" flex=\"true\" />"
                + "<uie:ToolbarSearchField name=\"search\" placeholder-text=\"Find\" value=\"abc\" />");
            var menu = root.Q<ToolbarMenu>("menu");
            Assert.Equal("File", menu.text);
            Assert.Equal(ToolbarMenu.Variant.Default, menu.variant);
            Assert.False(root.Q<ToolbarSpacer>("spacer").flex);
            var search = root.Q<ToolbarSearchField>("search");
            Assert.Equal("Find", search.placeholderText);
            Assert.Equal(string.Empty, search.value);
        }
    }
}
