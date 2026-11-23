using System;
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

        // A column container of the given height holding a 20-wide child with a 40% flex basis that
        // neither grows nor shrinks.
        static (YogaNode container, YogaNode child) PercentBasisColumn(YogaConfig config, float height)
        {
            var container = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(height) };
            var child = new YogaNode(config) { Width = YogaValue.Point(20), FlexBasis = YogaValue.Percent(40), FlexShrink = 0 };
            container.Insert(0, child);
            container.CalculateLayout();
            return (container, child);
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent): a percentage flex basis is
        /// resolved again each time, after its container's height changes, after a display: none
        /// container is shown again, and inside an element that grows (not at the size its content was
        /// measured at earlier in the pass); plain Yoga keeps the earlier basis.</summary>
        [Fact]
        public void APercentFlexBasisIsResolvedEachTime()
        {
            var (container, child) = PercentBasisColumn(YogaConfig.UIToolkit(), 400);
            Assert.Equal(160f, child.LayoutHeight);
            container.Height = YogaValue.Point(200);
            container.CalculateLayout();
            Assert.Equal(80f, child.LayoutHeight);
            Assert.Equal(80f, child.ComputedFlexBasis);

            // Hidden at its first layout (its child's basis is zeroed), then shown.
            var config = YogaConfig.UIToolkit();
            var root = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(400) };
            var hidden = new YogaNode(config) { Height = YogaValue.Point(400), FlexShrink = 0, Display = YogaDisplay.None };
            var deep = new YogaNode(config) { Width = YogaValue.Point(20), FlexBasis = YogaValue.Percent(30), FlexShrink = 0 };
            hidden.Insert(0, deep);
            root.Insert(0, hidden);
            root.CalculateLayout();
            Assert.Equal(0f, deep.LayoutHeight);
            Assert.Equal(0f, deep.ComputedFlexBasis);
            hidden.Display = YogaDisplay.Flex;
            root.CalculateLayout();
            Assert.Equal(120f, deep.LayoutHeight);

            // Owner resized by a sibling: the owner itself is not changed.
            var outer = new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(400) };
            var fixedSibling = new YogaNode(config) { Height = YogaValue.Point(100), FlexShrink = 0 };
            var grow = new YogaNode(config) { FlexGrow = 1, FlexShrink = 0 };
            var percent = new YogaNode(config) { Width = YogaValue.Point(20), FlexBasis = YogaValue.Percent(50), FlexShrink = 0 };
            grow.Insert(0, percent);
            outer.Insert(0, fixedSibling);
            outer.Insert(1, grow);
            outer.CalculateLayout();
            Assert.Equal(150f, percent.LayoutHeight);
            fixedSibling.Height = YogaValue.Point(200);
            outer.CalculateLayout();
            Assert.Equal(100f, percent.LayoutHeight);

            // Control: without the flag, the basis of the first pass stays.
            var (plainContainer, plainChild) = PercentBasisColumn(new YogaConfig(), 400);
            plainContainer.Height = YogaValue.Point(200);
            plainContainer.CalculateLayout();
            Assert.Equal(160f, plainChild.LayoutHeight);
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent): the percentage margins and
        /// paddings of an element in a 100 wide, 400 high column take the width (10% is 10), also for
        /// the space its children are laid out in: a 50% basis of a child is half of what is left.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PercentMarginsAndPaddingsOfAColumnTakeTheWidth(bool padding)
        {
            var config = YogaConfig.UIToolkit();
            var outer = new YogaNode(config) { Width = YogaValue.Point(100), Height = YogaValue.Point(400) };
            var grow = new YogaNode(config) { Width = YogaValue.Point(100), FlexGrow = 1, FlexShrink = 0 };
            if (padding)
            {
                grow.SetPadding(YogaEdge.Top, YogaValue.Percent(10));
                grow.SetPadding(YogaEdge.Bottom, YogaValue.Percent(10));
            }
            else
            {
                grow.SetMargin(YogaEdge.Top, YogaValue.Percent(10));
                grow.SetMargin(YogaEdge.Bottom, YogaValue.Percent(10));
            }
            var percent = new YogaNode(config) { Width = YogaValue.Point(20), FlexBasis = YogaValue.Percent(50), FlexShrink = 0 };
            grow.Insert(0, percent);
            outer.Insert(0, new YogaNode(config) { Height = YogaValue.Point(100), FlexShrink = 0 });
            outer.Insert(1, grow);
            outer.CalculateLayout();
            Assert.Equal(padding ? 100f : 110f, grow.LayoutY);
            Assert.Equal(padding ? 300f : 280f, grow.LayoutHeight);
            Assert.Equal(padding ? 10f : 0f, percent.LayoutY);
            Assert.Equal(140f, percent.LayoutHeight);
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent relativeIn*): percentage offsets of
        /// a relatively placed element take the owner's width for left and right and its height for
        /// top and bottom, whatever the element's own flex direction.</summary>
        [Fact]
        public void PercentOffsetsTakeTheOwnersSizeAlongTheirAxis()
        {
            var config = YogaConfig.UIToolkit();
            var column = new YogaNode(config) { Width = YogaValue.Point(200), Height = YogaValue.Point(400) };
            var row = new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(50), FlexShrink = 0, FlexDirection = YogaFlexDirection.Row };
            row.SetPosition(YogaEdge.Left, YogaValue.Percent(10));
            row.SetPosition(YogaEdge.Top, YogaValue.Percent(5));
            var end = new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(50), FlexShrink = 0 };
            end.SetPosition(YogaEdge.Right, YogaValue.Percent(10));
            end.SetPosition(YogaEdge.Bottom, YogaValue.Percent(5));
            column.Insert(0, row);
            column.Insert(1, end);
            column.CalculateLayout();
            Assert.Equal((20f, 20f), (row.LayoutX, row.LayoutY));
            Assert.Equal((-20f, 30f), (end.LayoutX, end.LayoutY));

            var rowOwner = new YogaNode(config) { Width = YogaValue.Point(400), Height = YogaValue.Point(200), FlexDirection = YogaFlexDirection.Row };
            var columnChild = new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(50), FlexShrink = 0 };
            columnChild.SetPosition(YogaEdge.Left, YogaValue.Percent(10));
            columnChild.SetPosition(YogaEdge.Top, YogaValue.Percent(5));
            rowOwner.Insert(0, columnChild);
            rowOwner.CalculateLayout();
            Assert.Equal((40f, 10f), (columnChild.LayoutX, columnChild.LayoutY));
        }

        // A 25x20 box (column, or row) holding a middle node over a 50x50 leaf; neither shrinks.
        static YogaNode BoxedMiddle(YogaConfig config, YogaOverflow boxOverflow, YogaOverflow middleOverflow, YogaFlexDirection boxDirection = YogaFlexDirection.Column)
        {
            var box = new YogaNode(config) { Width = YogaValue.Point(25), Height = YogaValue.Point(20), Overflow = boxOverflow, FlexDirection = boxDirection };
            var middle = new YogaNode(config) { Overflow = middleOverflow, FlexShrink = 0 };
            middle.Insert(0, new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(50), FlexShrink = 0 });
            box.Insert(0, middle);
            box.CalculateLayout();
            return middle;
        }

        /// <summary>Measured in the Editor (tests/projects/uitkcollections): a scroll node keeps its
        /// content's main size inside a hidden or visible box; plain Yoga clamps it to the box.</summary>
        [Fact]
        public void ScrollNodesKeepTheirMainAxisContent()
        {
            var config = new YogaConfig { ScrollKeepsMainAxisContent = true };
            Assert.Equal(50f, BoxedMiddle(config, YogaOverflow.Hidden, YogaOverflow.Scroll).LayoutHeight);
            Assert.Equal(50f, BoxedMiddle(config, YogaOverflow.Visible, YogaOverflow.Scroll).LayoutHeight);
            Assert.Equal(50f, BoxedMiddle(config, YogaOverflow.Hidden, YogaOverflow.Visible).LayoutHeight);
            Assert.Equal(20f, BoxedMiddle(new YogaConfig(), YogaOverflow.Hidden, YogaOverflow.Scroll).LayoutHeight);
            // The cross axis still clamps: in a row box the scroll node is as wide as the box.
            Assert.Equal(25f, BoxedMiddle(config, YogaOverflow.Hidden, YogaOverflow.Scroll, YogaFlexDirection.Row).ComputedFlexBasis);
        }

        /// <summary>A hidden box measures its items within its size (a label in a 20-high cell is
        /// measured at most 14 high once its margins and padding are taken).</summary>
        [Fact]
        public void HiddenBoxesMeasureItemsWithinTheirSize()
        {
            var config = new YogaConfig { ScrollKeepsMainAxisContent = true };
            var box = new YogaNode(config) { Width = YogaValue.Point(25), Height = YogaValue.Point(20), Overflow = YogaOverflow.Hidden };
            YogaMeasureMode seen = YogaMeasureMode.Exactly;
            var text = new YogaNode(config) { FlexShrink = 0 };
            text.MeasureFunction = (n, w, wm, h, hm) =>
            {
                seen = hm;
                return new YogaSize(10, hm == YogaMeasureMode.Undefined ? 30 : Math.Min(30, h));
            };
            box.Insert(0, text);
            box.CalculateLayout();
            Assert.Equal(YogaMeasureMode.AtMost, seen);
            Assert.Equal(20f, text.LayoutHeight);
        }

        static YogaConfig ScaledConfig() => new YogaConfig
        {
            PointScaleFactor = 1.25f, RoundEdgesToPixelGrid = true, PercentFlexBasisNeedsExactContainer = true,
            ScrollKeepsMainAxisContent = true, RoundPositionsFromAbsolute = true, MeasuredNodesRoundAsOthers = true,
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
