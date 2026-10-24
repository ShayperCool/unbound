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

        static YogaConfig ScaledConfig() => new YogaConfig
        {
            PointScaleFactor = 1.25f, RoundEdgesToPixelGrid = true, PercentFlexBasisNeedsExactContainer = true,
            OverflowHiddenMeasuresContent = true, RoundPositionsFromAbsolute = true, MeasuredNodesRoundAsOthers = true,
        };

        /// <summary>At 1.25 pixels per point positions are differences of rounded absolute edges: the
        /// Editor reports a label at 10.4 in a box at 8 as lying at 10.4f - 8f (2.39999962).</summary>
        [Fact]
        public void ScaledPositionsAreDifferencesOfAbsoluteEdges()
        {
            var config = ScaledConfig();
            var root = new YogaNode(config) { Width = YogaValue.Point(400), Height = YogaValue.Point(300) };
            var box = new YogaNode(config) { Width = YogaValue.Point(200), Height = YogaValue.Point(100) };
            box.SetMargin(YogaEdge.Left, YogaValue.Point(8));
            var label = new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(20) };
            label.SetMargin(YogaEdge.Left, YogaValue.Point(2.4f));
            box.Insert(0, label);
            root.Insert(0, box);
            root.CalculateLayout();
            Assert.Equal(8f, box.LayoutX);
            Assert.Equal(10.4f - 8f, label.LayoutX);
        }

        /// <summary>Measured (text) nodes round like the others: a 77.6pt label centered in 300.8pt lies at
        /// 111.6pt (139.5 pixels), rounded up to 112, not floored to 111.2 (the Editor's progress bar title).</summary>
        [Fact]
        public void ScaledMeasuredNodesAreNotFloored()
        {
            var config = ScaledConfig();
            var root = new YogaNode(config) { Width = YogaValue.Point(300.8f), Height = YogaValue.Point(40), AlignItems = YogaAlign.Center };
            var text = new YogaNode(config) { MeasureFunction = (n, w, wm, h, hm) => new YogaSize(77.6f, 22.4f) };
            root.Insert(0, text);
            root.CalculateLayout();
            Assert.Equal(112f, text.LayoutX);
        }

        /// <summary>A long column keeps its items on the half-pixel positions they compute to: the 20th
        /// item of 24.4pt steps lies at 463.6pt (579.5 pixels), rounded up to 464 (in float the running
        /// sum drifts to 463.59991 and rounds down).</summary>
        [Fact]
        public void ScaledLongColumnsDoNotDrift()
        {
            var config = ScaledConfig();
            var root = new YogaNode(config) { Width = YogaValue.Point(200) };
            YogaNode last = null;
            for (int i = 0; i < 20; i++)
            {
                last = new YogaNode(config) { Height = YogaValue.Point(18) };
                last.SetMargin(YogaEdge.Bottom, YogaValue.Point(6.4f));
                root.Insert(i, last);
            }
            root.CalculateLayout();
            Assert.Equal(464f, last.LayoutY);
        }
    }
}
