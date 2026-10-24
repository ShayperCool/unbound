using Unbound.UIElements.Layout;
using Xunit;
using Xunit.Abstractions;

namespace UnityEngine.Tests
{
    /// <summary>UI Toolkit's layout measured against the Editor: a toggle's input (flex: 1 0 0) in a
    /// row sized to content contributes its checkmark's width (the toggle is 180 wide in the probe).</summary>
    public class UIElementsLayoutTests
    {
        readonly ITestOutputHelper m_Out;
        public UIElementsLayoutTests(ITestOutputHelper output) { m_Out = output; }

        [Fact]
        public void PercentBasisInAutoRow()
        {
            var config = new YogaConfig { PercentFlexBasisNeedsExactContainer = true };
            var root = new YogaNode(config) { FlexDirection = YogaFlexDirection.Row, Width = YogaValue.Point(389) };
            var toggle = new YogaNode(config) { FlexDirection = YogaFlexDirection.Row };
            var label = new YogaNode(config) { Width = YogaValue.Point(150) };
            var input = new YogaNode(config) { FlexBasis = YogaValue.Percent(0), FlexGrow = 1, FlexShrink = 0, FlexDirection = YogaFlexDirection.Row };
            var check = new YogaNode(config) { Width = YogaValue.Point(24), Height = YogaValue.Point(24) };
            input.Insert(0, check);
            toggle.Insert(0, label);
            toggle.Insert(1, input);
            root.Insert(0, toggle);
            root.CalculateLayout();
            m_Out.WriteLine($"toggle {toggle.LayoutWidth} input {input.LayoutX} {input.LayoutWidth} basis {input.ComputedFlexBasis} {input.FlexBasis} cfg {input.Config.PercentFlexBasisNeedsExactContainer}");
            Assert.Equal(174f, toggle.LayoutWidth);
            Assert.Equal(150f, input.LayoutX);
            Assert.Equal(24f, input.LayoutWidth);
        }
    }
}
