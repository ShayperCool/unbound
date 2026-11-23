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
            var config = new YogaConfig { ResolvesPercentagesOfExactSizesOnly = true };
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
            m_Out.WriteLine($"toggle {toggle.LayoutWidth} input {input.LayoutX} {input.LayoutWidth} basis {input.ComputedFlexBasis} {input.FlexBasis} cfg {input.Config.ResolvesPercentagesOfExactSizesOnly}");
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

        /// <summary>Measured in the Editor (tests/projects/uitkpercent measuredMargin, measuredPadding): a
        /// node laid out by a measure function (a text element) takes its percentage margins and
        /// paddings from the owner's width like any other node: in a 1000 wide row it is as large as a
        /// plain node, and its content is measured at the width its paddings leave.</summary>
        [Fact]
        public void AMeasuredNodesPercentMarginsAndPaddingsTakeTheOwnersWidth()
        {
            var config = YogaConfig.UIToolkit();
            var row = new YogaNode(config) { Width = YogaValue.Point(1000), Height = YogaValue.Point(200), FlexDirection = YogaFlexDirection.Row, AlignItems = YogaAlign.Center };
            row.SetPadding(YogaEdge.Bottom, YogaValue.Point(50));
            YogaNode Item(bool measured)
            {
                var node = new YogaNode(config) { FlexBasis = YogaValue.Percent(25), FlexShrink = 0, Height = YogaValue.Percent(100) };
                node.SetMargin(YogaEdge.Top, YogaValue.Percent(2));
                node.SetMargin(YogaEdge.Left, YogaValue.Percent(0.5f));
                node.SetMargin(YogaEdge.Right, YogaValue.Percent(0.5f));
                node.SetMargin(YogaEdge.Bottom, YogaValue.Percent(0.5f));
                node.SetPadding(YogaEdge.Bottom, YogaValue.Percent(2));
                node.SetPadding(YogaEdge.Top, YogaValue.Point(10));
                if (measured) node.MeasureFunction = (n, w, wm, h, hm) => new YogaSize { Width = 20, Height = 15 };
                return node;
            }
            var label = Item(true);
            var plain = Item(false);
            row.Insert(0, label);
            row.Insert(1, plain);
            row.CalculateLayout();
            Assert.Equal((5f, 8f, 250f, 150f), (label.LayoutX, label.LayoutY, label.LayoutWidth, label.LayoutHeight));
            Assert.Equal((265f, 8f, 250f, 150f), (plain.LayoutX, plain.LayoutY, plain.LayoutWidth, plain.LayoutHeight));

            // Measured: 300 wide with an automatic height and 5% top, bottom and left paddings; the
            // content (taller when narrower) is measured 250 wide and the paddings add 100.
            var wrapRow = new YogaNode(config) { Width = YogaValue.Point(1000), Height = YogaValue.Point(400), FlexDirection = YogaFlexDirection.Row, AlignItems = YogaAlign.FlexStart };
            var text = new YogaNode(config) { Width = YogaValue.Point(300), FlexShrink = 0 };
            text.SetPadding(YogaEdge.Top, YogaValue.Percent(5));
            text.SetPadding(YogaEdge.Bottom, YogaValue.Percent(5));
            text.SetPadding(YogaEdge.Left, YogaValue.Percent(5));
            float measuredWidth = float.NaN;
            text.MeasureFunction = (n, w, wm, h, hm) => { measuredWidth = w; return new YogaSize { Width = w, Height = 3000f / w }; };
            wrapRow.Insert(0, text);
            wrapRow.CalculateLayout();
            Assert.Equal(250f, measuredWidth);
            Assert.Equal(112f, text.LayoutHeight);
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent minOverMax): a minimum size wins
        /// over a smaller maximum (150x300 with max 50x100 and min 80x200 is 80x200), also for an
        /// absolutely placed element sized in percentages; a size between them stays.</summary>
        [Fact]
        public void AMinimumWinsOverASmallerMaximum()
        {
            var config = YogaConfig.UIToolkit();
            var outer = new YogaNode(config) { Width = YogaValue.Point(200), Height = YogaValue.Point(400) };
            var box = new YogaNode(config)
            {
                Width = YogaValue.Point(150), Height = YogaValue.Point(300), FlexShrink = 0,
                MaxWidth = YogaValue.Point(50), MaxHeight = YogaValue.Point(100), MinWidth = YogaValue.Point(80), MinHeight = YogaValue.Point(200),
            };
            var absolute = new YogaNode(config)
            {
                PositionType = YogaPositionType.Absolute, Width = YogaValue.Point(50),
                Height = YogaValue.Percent(38), MaxHeight = YogaValue.Percent(20), MinHeight = YogaValue.Point(120),
            };
            var between = new YogaNode(config) { Width = YogaValue.Point(60), Height = YogaValue.Point(150), FlexShrink = 0, MaxHeight = YogaValue.Point(170), MinHeight = YogaValue.Point(120) };
            outer.Insert(0, box);
            outer.Insert(1, absolute);
            outer.Insert(2, between);
            outer.CalculateLayout();
            Assert.Equal(80f, box.LayoutWidth);
            Assert.Equal(200f, box.LayoutHeight);
            Assert.Equal(120f, absolute.LayoutHeight);
            Assert.Equal(150f, between.LayoutHeight);
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

        static (YogaNode row, YogaNode[] items) Row(float width, params System.Action<YogaNode>[] items)
        {
            var config = YogaConfig.UIToolkit();
            var row = new YogaNode(config) { Width = YogaValue.Point(width), Height = YogaValue.Point(100), FlexDirection = YogaFlexDirection.Row };
            var nodes = new YogaNode[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                nodes[i] = new YogaNode(config) { Height = YogaValue.Point(50), FlexShrink = 0 };
                items[i](nodes[i]);
                row.Insert(i, nodes[i]);
            }
            row.CalculateLayout();
            return (row, nodes);
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent shrink*, grow*): whether a line's
        /// items grow or shrink is decided with percentage margins taken as 0, the space shared takes
        /// them whole; factors summing under 1 share that part only; an item held by its max is frozen
        /// there and the rest share what is left.</summary>
        [Fact]
        public void FlexibleLengthsAreResolvedAsUIToolkit()
        {
            // 522 + 1093 fit 1632 without the 2% margin: no shrinking, the margin overflows.
            var (_, fits) = Row(1632, a => { a.Width = YogaValue.Point(522); a.FlexShrink = 1; a.SetMargin(YogaEdge.Right, YogaValue.Percent(2)); },
                b => { b.Width = YogaValue.Point(1093); b.FlexShrink = 1; });
            Assert.Equal((522f, 1093f, 555f), (fits[0].LayoutWidth, fits[1].LayoutWidth, fits[1].LayoutX));
            // The same with a 33 point margin shrinks.
            var (_, points) = Row(1632, a => { a.Width = YogaValue.Point(522); a.FlexShrink = 1; a.SetMargin(YogaEdge.Right, YogaValue.Point(33)); },
                b => { b.Width = YogaValue.Point(1093); b.FlexShrink = 1; });
            Assert.Equal((517f, 1082f), (points[0].LayoutWidth, points[1].LayoutWidth));
            // 800 + 900 overflow 1632 anyway: the shrink takes the margin too.
            var (_, more) = Row(1632, a => { a.Width = YogaValue.Point(800); a.FlexShrink = 1; a.SetMargin(YogaEdge.Right, YogaValue.Percent(2)); },
                b => { b.Width = YogaValue.Point(900); b.FlexShrink = 1; });
            Assert.Equal((752f, 847f, 785f), (more[0].LayoutWidth, more[1].LayoutWidth, more[1].LayoutX));
            // Growing although the 10% margin overflows: the growing item gives the overflow back.
            var (_, overflow) = Row(1000, a => { a.Width = YogaValue.Point(500); a.FlexGrow = 1; a.SetMargin(YogaEdge.Right, YogaValue.Percent(10)); },
                b => b.Width = YogaValue.Point(450));
            Assert.Equal((450f, 450f), (overflow[0].LayoutWidth, overflow[1].LayoutWidth));
            // Factors 0.25 + 0.25 share half of the 600 left.
            var (_, fraction) = Row(1000, a => { a.Width = YogaValue.Point(200); a.FlexGrow = 0.25f; }, b => { b.Width = YogaValue.Point(200); b.FlexGrow = 0.25f; });
            Assert.Equal((350f, 350f), (fraction[0].LayoutWidth, fraction[1].LayoutWidth));
            // A max of 100 freezes the first of three equal growers.
            var (_, max) = Row(600, a => { a.FlexBasis = YogaValue.Point(0); a.FlexGrow = 1; a.MaxWidth = YogaValue.Point(100); },
                b => { b.FlexBasis = YogaValue.Point(0); b.FlexGrow = 1; }, c => { c.FlexBasis = YogaValue.Point(0); c.FlexGrow = 1; });
            Assert.Equal((100f, 250f, 250f), (max[0].LayoutWidth, max[1].LayoutWidth, max[2].LayoutWidth));
        }

        // A column with an automatic height in a 1000x400 row centering its children, holding a child
        // with a 60% basis around a 154 high one and a child with a 40% basis, 37% height and 140 max
        // around a 54 high one.
        static YogaNode[] AutoColumn(System.Action<YogaNode, YogaNode> place)
        {
            var config = YogaConfig.UIToolkit();
            var row = new YogaNode(config) { Width = YogaValue.Point(1000), Height = YogaValue.Point(400), FlexDirection = YogaFlexDirection.Row, AlignItems = YogaAlign.Center };
            var column = new YogaNode(config) { FlexShrink = 0, AlignItems = YogaAlign.Center };
            var a = new YogaNode(config) { Width = YogaValue.Point(380), FlexBasis = YogaValue.Percent(60), FlexShrink = 0 };
            a.Insert(0, new YogaNode(config) { Height = YogaValue.Point(154), FlexShrink = 0 });
            var b = new YogaNode(config) { Width = YogaValue.Point(300), FlexBasis = YogaValue.Percent(40), Height = YogaValue.Percent(37), MaxHeight = YogaValue.Point(140) };
            b.Insert(0, new YogaNode(config) { Width = YogaValue.Point(50), Height = YogaValue.Point(54), FlexShrink = 0 });
            column.Insert(0, a);
            column.Insert(1, b);
            place(row, column);
            row.CalculateLayout();
            return new[] { column, a, b };
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent autoColumn*): percentages of a size
        /// a node is not laid out exactly in stay unresolved (its children take their content's), and
        /// a node whose own size may follow its owner's is laid out again at the size found, which
        /// resolves them.</summary>
        [Fact]
        public void PercentagesOfANonExactSizeWaitForTheFinalSize()
        {
            // Neither growing nor shrinking, 400 wide: laid out once, the children keep their content's.
            var once = AutoColumn((row, column) => { column.Width = YogaValue.Point(400); row.Insert(0, column); });
            Assert.Equal((208f, 154f, 54f), (once[0].LayoutHeight, once[1].LayoutHeight, once[2].LayoutHeight));
            // Shrinking next to a sibling: laid out again 208 high, the bases take 60% and 40% of it.
            var again = AutoColumn((row, column) =>
            {
                var config = row.Config;
                row.Insert(0, new YogaNode(config) { Width = YogaValue.Point(600), Height = YogaValue.Point(50), FlexShrink = 0 });
                column.FlexBasis = YogaValue.Percent(53);
                column.FlexShrink = 1;
                row.Insert(1, column);
            });
            Assert.Equal((208f, 125f, 83f), (again[0].LayoutHeight, again[1].LayoutHeight, again[2].LayoutHeight));
        }

        // In a 984x415 column centring its items, a row growing from a 0 basis of an item growing and
        // shrinking around content `content` wide and a fixed 24 wide one (plus the row's padding).
        static (YogaNode row, YogaNode a) CenteredRow(YogaConfig config, float content, float padding = 0)
        {
            var column = new YogaNode(config) { Width = YogaValue.Point(984), Height = YogaValue.Point(415), AlignItems = YogaAlign.Center };
            var row = new YogaNode(config) { FlexDirection = YogaFlexDirection.Row, FlexGrow = 1, FlexShrink = 1, FlexBasis = YogaValue.Point(0) };
            row.SetPadding(YogaEdge.Left, YogaValue.Point(padding));
            row.SetPadding(YogaEdge.Right, YogaValue.Point(padding));
            var a = new YogaNode(config) { FlexGrow = 1, FlexShrink = 1 };
            a.Insert(0, new YogaNode(config) { Width = YogaValue.Point(content), Height = YogaValue.Point(50), FlexShrink = 0 });
            row.Insert(0, a);
            row.Insert(1, new YogaNode(config) { Width = YogaValue.Point(24), FlexShrink = 0 });
            column.Insert(0, row);
            column.CalculateLayout();
            return (row, a);
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent centeredRow): a container without
        /// an exact main size is as long as its items' bases, while they grow or shrink to the space
        /// it was given.</summary>
        [Fact]
        public void ContainersAreAsLongAsTheirItemsBases()
        {
            var (row, a) = CenteredRow(YogaConfig.UIToolkit(), 982.8f);
            Assert.Equal((-11f, 1006f, 960f), (row.LayoutX, row.LayoutWidth, a.LayoutWidth));
            (row, a) = CenteredRow(YogaConfig.UIToolkit(), 300);
            Assert.Equal((330f, 324f, 960f), (row.LayoutX, row.LayoutWidth, a.LayoutWidth));
            (row, a) = CenteredRow(YogaConfig.UIToolkit(), 982.8f, 8);
            Assert.Equal((-19f, 1022f, 944f), (row.LayoutX, row.LayoutWidth, a.LayoutWidth));
            // Control: as long as its items after they grow or shrink.
            var config = YogaConfig.UIToolkit();
            config.MeasuresLinesByTheirBases = false;
            (row, a) = CenteredRow(config, 982.8f);
            Assert.Equal((0f, 984f, 960f), (row.LayoutX, row.LayoutWidth, a.LayoutWidth));
        }

        /// <summary>Measured in the Editor (tests/projects/uitkpercent percentMinInAutoRow): the bases a
        /// row sized by its content collects are bounded by percentage min and max sizes of its own
        /// width only when that width is exact, so an item 95% wide at least gives the row no basis.</summary>
        [Fact]
        public void CollectedBasesTakePercentagesOfTheNodesExactSizeOnly()
        {
            var config = YogaConfig.UIToolkit();
            var outer = new YogaNode(config) { Width = YogaValue.Point(1423.5f), Height = YogaValue.Point(90), FlexDirection = YogaFlexDirection.Row };
            var input = new YogaNode(config) { FlexDirection = YogaFlexDirection.Row, FlexGrow = 1 };
            var drag = new YogaNode(config)
            {
                FlexGrow = 1, FlexShrink = 1, FlexBasis = YogaValue.Point(0), Height = YogaValue.Point(80),
                Width = YogaValue.Percent(95), MinWidth = YogaValue.Percent(95), MaxWidth = YogaValue.Percent(95),
            };
            input.Insert(0, drag);
            outer.Insert(0, input);
            outer.CalculateLayout();
            Assert.Equal((0f, 1424f, 1352f), (input.ComputedFlexBasis, input.LayoutWidth, drag.LayoutWidth));
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
            PointScaleFactor = 1.25f, RoundEdgesToPixelGrid = true, ResolvesPercentagesOfExactSizesOnly = true,
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
