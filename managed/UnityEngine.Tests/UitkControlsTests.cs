using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>HelpBox, ToggleButtonGroup and its state, TwoPaneSplitView (Unity 6000.0.71f1,
    /// tests/projects/uitkcontrols). Controls outside the probe: other enum widths and lengths, a
    /// group made with fewer options than buttons, value changes without notification, and a split
    /// view set up before and after layout.</summary>
    public sealed class UitkControlsTests : IDisposable
    {
        readonly Panel m_Panel = new Panel(null, ContextType.Player, EventDispatcher.CreateDefault(), null);

        public void Dispose() => m_Panel.Dispose();

        [Flags] enum ShortFlags : short { A = 1, Top = unchecked((short)0x8000) }
        [Flags] enum ULongFlags : ulong { A = 1, Top = 1UL << 63 }
        [Flags] enum IntFlags { A = 1, Top = int.MinValue }
        [Flags] enum SByteFlags : sbyte { A = 1, Top = sbyte.MinValue }
        [Flags] enum UShortFlags : ushort { A = 1, Top = 0x8000 }

        // An enum value is read as a 32-bit integer, widened as its underlying type is, then
        // sign-extended (the probe measures byte, short, int and long enums): signed and unsigned
        // widths here, the wide ones cut to their low 32 bits.
        [Fact]
        public void EnumFlagsReadTheLow32BitsSignExtended()
        {
            var signedNarrow = ToggleButtonGroupState.FromEnumFlags(SByteFlags.A | SByteFlags.Top, 64);
            Assert.True(signedNarrow[0] && signedNarrow[7] && signedNarrow[40]);
            var unsignedNarrow = ToggleButtonGroupState.FromEnumFlags(UShortFlags.A | UShortFlags.Top, 64);
            Assert.True(unsignedNarrow[0] && unsignedNarrow[15]);
            Assert.False(unsignedNarrow[16]);
            Assert.True(ToggleButtonGroupState.Compare(new ToggleButtonGroupState(0x8001, 16), UShortFlags.A | UShortFlags.Top));
            Assert.False(ToggleButtonGroupState.Compare(new ToggleButtonGroupState(0x81, 8), SByteFlags.A | SByteFlags.Top));
            var narrow = ToggleButtonGroupState.FromEnumFlags(ShortFlags.A | ShortFlags.Top);
            Assert.Equal(16, narrow.length);
            Assert.True(narrow[0] && narrow[15]);
            var wide = ToggleButtonGroupState.FromEnumFlags(ULongFlags.A | ULongFlags.Top);
            Assert.Equal(64, wide.length);
            Assert.True(wide[0]);
            Assert.False(wide[63]);
            var extended = ToggleButtonGroupState.FromEnumFlags(IntFlags.Top, 64);
            Assert.False(extended[30]);
            Assert.True(extended[31] && extended[40] && extended[63]);
            Assert.True(ToggleButtonGroupState.Compare(extended, IntFlags.Top));
            // Back to the enum, the options are parsed as an unsigned number: a negative short's
            // options do not fit.
            Assert.Throws<OverflowException>(() => ToggleButtonGroupState.ToEnumFlags<ShortFlags>(narrow, false));
            Assert.Equal(ShortFlags.A, ToggleButtonGroupState.ToEnumFlags<ShortFlags>(new ToggleButtonGroupState(1, 16), false));
        }

        [Fact]
        public void TheLastOfSixtyFourOptionsAndOneOptionPrint()
        {
            var full = new ToggleButtonGroupState(0, 64);
            full[63] = true;
            Assert.Equal("1" + new string('0', 63), full.ToString());
            Assert.Equal("1", new ToggleButtonGroupState(1, 1).ToString());
            Assert.Equal("0", new ToggleButtonGroupState(0b10, 1).ToString());
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => default(ToggleButtonGroupState)[0]);
            Assert.Equal("index", ex.ParamName);
            var span = Assert.Throws<ArgumentException>(() => full.GetInactiveOptions(new int[63]));
            Assert.StartsWith("indices' length (63) should be equal to or greater than the ToggleButtonGroupState's length (64).", span.Message);
        }

        static string Checked(ToggleButtonGroup group) =>
            string.Concat(group.Query<Button>().ToList().Select(b => (b.pseudoStates & PseudoStates.Checked) != 0 ? '1' : '0'));

        // A group made with fewer options than it gets buttons takes the button count once the
        // buttons reach it; the value keeps its options until then.
        [Fact]
        public void AShortStateGrowsWithTheButtons()
        {
            var group = new ToggleButtonGroup(new ToggleButtonGroupState(0b10, 2));
            int changes = 0;
            group.RegisterValueChangedCallback(_ => changes++);
            group.Add(new Button());
            Assert.Equal(2, group.value.length);
            group.Add(new Button());
            Assert.Equal(2, group.value.length);
            Assert.Equal(0, changes);
            group.Insert(0, new Button { name = "first" });
            Assert.Equal(3, group.value.length);
            Assert.Equal("first", group.Query<Button>().First().name);
            Assert.Contains(ToggleButtonGroup.buttonLeftClassName, group.Q<Button>("first").GetClasses());
            Assert.Equal("010", Checked(group));
        }

        [Fact]
        public void SetValueWithoutNotifyUpdatesTheButtonsOnly()
        {
            var group = new ToggleButtonGroup { allowEmptySelection = true };
            for (int i = 0; i < 4; i++) group.Add(new Button());
            m_Panel.visualTree.Add(group);
            int changes = 0;
            group.RegisterValueChangedCallback(_ => changes++);
            group.SetValueWithoutNotify(new ToggleButtonGroupState(0b1010, 4));
            Assert.Equal("0101", Checked(group));
            Assert.Equal(0, changes);
            group.value = new ToggleButtonGroupState(0b0001, 4);
            Assert.Equal("1000", Checked(group));
            Assert.Equal(1, changes);
        }

        // Without buttons, disallowing an empty selection selects nothing.
        [Fact]
        public void AGroupWithoutButtonsKeepsAnEmptySelection()
        {
            var group = new ToggleButtonGroup { allowEmptySelection = true };
            group.allowEmptySelection = false;
            Assert.Equal(0, group.value.GetActiveOptions(stackalloc int[64]).Length);
            group.isMultipleSelection = true;
            group.SetValueWithoutNotify(new ToggleButtonGroupState(0b11, 2));
            group.isMultipleSelection = false;
            Assert.Equal("11", group.value.ToString());
        }

        [Fact]
        public void AHelpBoxKeepsItsIconFirst()
        {
            var box = new HelpBox("a", HelpBoxMessageType.None);
            box.Insert(0, new VisualElement { name = "game" });
            box.messageType = HelpBoxMessageType.Error;
            Assert.Contains(HelpBox.iconErrorUssClassName, box.hierarchy[0].GetClasses());
            box.messageType = HelpBoxMessageType.Error;
            Assert.Equal(3, box.hierarchy.childCount);
            box.text = "b";
            Assert.Equal("b", box.Q<Label>().text);
        }

        // With both panes present, a property change sets the panes up at once, without layout.
        [Fact]
        public void APropertyChangeWithBothPanesSetsThemUp()
        {
            var view = new TwoPaneSplitView();
            var first = new VisualElement();
            var second = new VisualElement();
            view.Add(first);
            view.Add(second);
            Assert.Null(view.fixedPane);
            view.orientation = TwoPaneSplitViewOrientation.Vertical;
            Assert.Same(first, view.fixedPane);
            Assert.Equal(100f, first.style.height.value.value);
            view.fixedPaneIndex = 1;
            Assert.Same(second, view.fixedPane);
            Assert.Same(first, view.flexedPane);
            Assert.Equal(1f, first.style.flexGrow.value);
            Assert.Equal(100f, second.style.height.value.value);
        }

        // A collapse asked for before layout happens on the first layout; restoring shows both.
        [Fact]
        public void APendingCollapseHappensOnLayout()
        {
            var view = new TwoPaneSplitView(1, 40, TwoPaneSplitViewOrientation.Horizontal);
            view.style.width = 200;
            view.style.height = 100;
            var first = new VisualElement();
            var second = new VisualElement();
            view.Add(first);
            view.Add(second);
            // The runtime theme lays the panes out in a row; this panel has no theme.
            view.Q("unity-content-container").style.flexDirection = FlexDirection.Row;
            view.CollapseChild(0);
            m_Panel.visualTree.style.width = 400;
            m_Panel.visualTree.style.height = 300;
            m_Panel.visualTree.Add(view);
            m_Panel.ValidateLayout();
            Assert.Equal(DisplayStyle.None, first.resolvedStyle.display);
            Assert.Equal(200f, second.layout.width);
            view.UnCollapse();
            m_Panel.ValidateLayout();
            Assert.Equal(DisplayStyle.Flex, first.resolvedStyle.display);
            Assert.Equal(40f, second.layout.width);
            Assert.Equal(160f, first.layout.width);
        }
    }
}
