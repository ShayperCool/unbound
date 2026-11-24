using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PercentProbe
{
    /// <summary>
    /// Percentages in UI Toolkit layout across layout passes: a percentage flex basis after its
    /// container changes size (directly, through a sibling, or through display: none and back), a
    /// basis in points and an automatic one as controls, and the resolved flexBasis of each; the
    /// percentage margins and paddings of a column element, which take its owner's width.
    /// </summary>
    public class PercentProbeBehaviour : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UITK_PERCENT_OUT"))) return;
            var go = new GameObject("probe");
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = Resources.Load<PanelSettings>("ProbePanelSettings");
            go.AddComponent<PercentProbeBehaviour>();
        }

        static VisualElement Box(VisualElement parent, string name, float width = float.NaN, float height = float.NaN)
        {
            var ve = new VisualElement { name = name };
            ve.style.flexShrink = 0;
            if (!float.IsNaN(width)) ve.style.width = width;
            if (!float.IsNaN(height)) ve.style.height = height;
            parent?.Add(ve);
            return ve;
        }

        static VisualElement Case(VisualElement root, string name, float left)
        {
            var ve = Box(root, name);
            ve.style.position = Position.Absolute;
            ve.style.left = left;
            ve.style.top = 0;
            return ve;
        }

        static string State(VisualElement ve) =>
            "rect=" + F(ve.layout.x) + "," + F(ve.layout.y) + "," + F(ve.layout.width) + "," + F(ve.layout.height) + "|basis=" + F(ve.resolvedStyle.flexBasis.value);

        static IEnumerator Frames()
        {
            yield return null;
            yield return null;
        }

        IEnumerator Start()
        {
            var dir = Environment.GetEnvironmentVariable("UITK_PERCENT_OUT");
            var root = GetComponent<UIDocument>().rootVisualElement;
            yield return Frames();
            Put("root", State(root));

            // resize: a column container of exact height whose height changes; a child with a
            // percentage basis (no grow, no shrink), one with a basis in points and one sized by its
            // content (controls).
            var resize = Case(root, "resize", 0);
            var rContainer = Box(resize, "container", 100, 400);
            var rPercent = Box(rContainer, "percent", 20); rPercent.style.flexBasis = Length.Percent(40);
            var rPoints = Box(rContainer, "points", 20); rPoints.style.flexBasis = 50;
            var rAuto = Box(rContainer, "auto", 20); Box(rAuto, "content", 20, 30);
            // resizeRow: the same along a row.
            var resizeRow = Case(root, "resizeRow", 120);
            var rrContainer = Box(resizeRow, "container", 400, 50); rrContainer.style.flexDirection = FlexDirection.Row;
            var rrPercent = Box(rrContainer, "percent", float.NaN, 20); rrPercent.style.flexBasis = Length.Percent(25);
            // shrink: two children of 40% that shrink once the container is too small for both.
            var shrink = Case(root, "shrink", 540);
            var sContainer = Box(shrink, "container", 50, 400);
            var sA = Box(sContainer, "a", 20); sA.style.flexBasis = Length.Percent(40); sA.style.flexShrink = 1;
            var sB = Box(sContainer, "b", 20); sB.style.flexBasis = Length.Percent(40); sB.style.flexShrink = 1;
            // sibling: the container's height comes from growing next to a sibling whose height
            // changes; its child has a percentage basis (the container itself is not changed).
            var sibling = Case(root, "sibling", 600);
            var sibOuter = Box(sibling, "outer", 50, 400);
            var sibFixed = Box(sibOuter, "fixed", 50, 100);
            var sibGrow = Box(sibOuter, "grow", 50); sibGrow.style.flexGrow = 1;
            var sibPercent = Box(sibGrow, "percent", 20); sibPercent.style.flexBasis = Length.Percent(50);
            // shown: a container hidden (display: none) at its first layout, then shown; its child
            // and grandchild have percentage bases.
            var shown = Case(root, "shown", 660);
            var shContainer = Box(shown, "container", 50, 400);
            shContainer.style.display = DisplayStyle.None;
            var shPercent = Box(shContainer, "percent", 40); shPercent.style.flexBasis = Length.Percent(40);
            var shInner = Box(shContainer, "inner", 40, 200);
            var shDeep = Box(shInner, "deep", 20); shDeep.style.flexBasis = Length.Percent(30);
            // reshown: laid out visible, then hidden, then shown again.
            var reshown = Case(root, "reshown", 720);
            var reContainer = Box(reshown, "container", 50, 400);
            var rePercent = Box(reContainer, "percent", 40); rePercent.style.flexBasis = Length.Percent(40);
            // hiddenParent: the hidden element is the case's own top element (the container's parent).
            var hiddenParent = Case(root, "hiddenParent", 780);
            hiddenParent.style.display = DisplayStyle.None;
            var hpContainer = Box(hiddenParent, "container", 50, 400);
            var hpPercent = Box(hpContainer, "percent", 40); hpPercent.style.flexBasis = Length.Percent(40);

            // marginPercent / paddingPercent: an element that grows in a 100 wide, 400 high column
            // next to a 100 high sibling, with 10% margins (or paddings) at its top and bottom; its
            // child has a 50% basis. marginRow: the same along a 400 wide, 100 high row.
            var marginPercent = Case(root, "marginPercent", 840);
            var mpOuter = Box(marginPercent, "outer", 100, 400);
            Box(mpOuter, "fixed", 100, 100);
            var mpGrow = Box(mpOuter, "grow", 100); mpGrow.style.flexGrow = 1;
            mpGrow.style.marginTop = Length.Percent(10); mpGrow.style.marginBottom = Length.Percent(10);
            var mpPercent = Box(mpGrow, "percent", 20); mpPercent.style.flexBasis = Length.Percent(50);
            var paddingPercent = Case(root, "paddingPercent", 950);
            var ppOuter = Box(paddingPercent, "outer", 100, 400);
            Box(ppOuter, "fixed", 100, 100);
            var ppGrow = Box(ppOuter, "grow", 100); ppGrow.style.flexGrow = 1;
            ppGrow.style.paddingTop = Length.Percent(10); ppGrow.style.paddingBottom = Length.Percent(10);
            var ppPercent = Box(ppGrow, "percent", 20); ppPercent.style.flexBasis = Length.Percent(50);
            var marginRow = Case(root, "marginRow", 1060);
            var mrOuter = Box(marginRow, "outer", 400, 100); mrOuter.style.flexDirection = FlexDirection.Row;
            Box(mrOuter, "fixed", 100, 100);
            var mrGrow = Box(mrOuter, "grow", float.NaN, 100); mrGrow.style.flexGrow = 1; mrGrow.style.flexDirection = FlexDirection.Row;
            mrGrow.style.marginLeft = Length.Percent(10); mrGrow.style.marginRight = Length.Percent(10);
            var mrPercent = Box(mrGrow, "percent", float.NaN, 20); mrPercent.style.flexBasis = Length.Percent(50);

            // relativeInColumn / relativeInRow: relatively placed elements with left: 10% and top: 5%
            // (a row in a 200 wide, 400 high column; a column in a 400 wide, 200 high row), and one with
            // right: 10% and bottom: 5% in the column.
            var relativeInColumn = Case(root, "relativeInColumn", 1170);
            var rcOuter = Box(relativeInColumn, "outer", 200, 400);
            var rcRow = Box(rcOuter, "row", 50, 50); rcRow.style.flexDirection = FlexDirection.Row;
            rcRow.style.left = Length.Percent(10); rcRow.style.top = Length.Percent(5);
            var rcEnd = Box(rcOuter, "end", 50, 50);
            rcEnd.style.right = Length.Percent(10); rcEnd.style.bottom = Length.Percent(5);
            var relativeInRow = Case(root, "relativeInRow", 1380);
            var rrOuter = Box(relativeInRow, "outer", 400, 200); rrOuter.style.flexDirection = FlexDirection.Row;
            var rrColumn = Box(rrOuter, "column", 50, 50);
            rrColumn.style.left = Length.Percent(10); rrColumn.style.top = Length.Percent(5);

            // minOverMax: in a 200 wide, 400 high column, an element 150x300 with max 50x100 and min
            // 80x200; minOverMaxAbsolute: an absolutely placed one 38% high, at most 20% and at least
            // 120 high. percentUnderMax: in a 960 wide, 540 high column centering its children, an
            // element whose width, min-width and max-width are 85%.
            var minOverMax = Case(root, "minOverMax", 1490);
            var mmOuter = Box(minOverMax, "outer", 200, 400);
            var mmBox = Box(mmOuter, "box", 150, 300);
            mmBox.style.maxWidth = 50; mmBox.style.maxHeight = 100; mmBox.style.minWidth = 80; mmBox.style.minHeight = 200;
            var mmAbsolute = Box(mmOuter, "absolute", 50);
            mmAbsolute.style.position = Position.Absolute;
            mmAbsolute.style.height = Length.Percent(38); mmAbsolute.style.maxHeight = Length.Percent(20); mmAbsolute.style.minHeight = 120;
            var percentUnderMax = Case(root, "percentUnderMax", 1700);
            var pmOuter = Box(percentUnderMax, "outer", 960, 540); pmOuter.style.alignItems = Align.Center;
            var pmBox = Box(pmOuter, "box", float.NaN, 50);
            pmBox.style.width = Length.Percent(85); pmBox.style.minWidth = Length.Percent(85); pmBox.style.maxWidth = Length.Percent(85);

            // shrinkAbsolute: in an absolutely placed 1632x876 column, an absolutely placed row 100% wide
            // and high whose children are 32% wide with a 2% right margin and 67% wide (together wider
            // than the row) and may shrink; shrinkRow: the same children in a plain 1632 wide row.
            var shrinkAbsolute = Case(root, "shrinkAbsolute", 0);
            shrinkAbsolute.style.top = 500;
            var saOuter = Box(shrinkAbsolute, "outer", 1632, 876);
            var saRow = Box(saOuter, "row");
            saRow.style.position = Position.Absolute; saRow.style.width = Length.Percent(100); saRow.style.height = Length.Percent(100);
            saRow.style.flexDirection = FlexDirection.Row; saRow.style.flexShrink = 1;
            var saA = Box(saRow, "a"); saA.style.width = Length.Percent(32); saA.style.height = Length.Percent(96); saA.style.marginRight = Length.Percent(2); saA.style.flexShrink = 1;
            var saB = Box(saRow, "b"); saB.style.width = Length.Percent(67); saB.style.height = Length.Percent(100); saB.style.flexShrink = 1;
            var shrinkRow = Case(root, "shrinkRow", 0);
            shrinkRow.style.top = 1400;
            var srRow = Box(shrinkRow, "row", 1632, 876); srRow.style.flexDirection = FlexDirection.Row;
            var srA = Box(srRow, "a"); srA.style.width = Length.Percent(32); srA.style.height = Length.Percent(96); srA.style.marginRight = Length.Percent(2); srA.style.flexShrink = 1;
            var srB = Box(srRow, "b"); srB.style.width = Length.Percent(67); srB.style.height = Length.Percent(100); srB.style.flexShrink = 1;

            // Controls: the same overflow with widths in points (522 and 1093, a 33 margin), and with
            // percentage bases (32% and 67%) in place of widths.
            var shrinkPoints = Case(root, "shrinkPoints", 0);
            shrinkPoints.style.top = 2300;
            var spRow = Box(shrinkPoints, "row", 1632, 100); spRow.style.flexDirection = FlexDirection.Row;
            var spA = Box(spRow, "a", 522, 50); spA.style.marginRight = 33; spA.style.flexShrink = 1;
            var spB = Box(spRow, "b", 1093, 50); spB.style.flexShrink = 1;
            var shrinkBasis = Case(root, "shrinkBasis", 0);
            shrinkBasis.style.top = 2400;
            var sbRow = Box(shrinkBasis, "row", 1632, 100); sbRow.style.flexDirection = FlexDirection.Row;
            var sbA = Box(sbRow, "a", float.NaN, 50); sbA.style.flexBasis = Length.Percent(32); sbA.style.marginRight = Length.Percent(2); sbA.style.flexShrink = 1;
            var sbB = Box(sbRow, "b", float.NaN, 50); sbB.style.flexBasis = Length.Percent(67); sbB.style.flexShrink = 1;

            // Which part keeps them from shrinking: percentage widths with a margin in points, widths in
            // points with a percentage margin, and 50% and 60% widths without a margin.
            var shrinkPercentWidth = Case(root, "shrinkPercentWidth", 0);
            shrinkPercentWidth.style.top = 2500;
            var spwRow = Box(shrinkPercentWidth, "row", 1632, 100); spwRow.style.flexDirection = FlexDirection.Row;
            var spwA = Box(spwRow, "a", float.NaN, 50); spwA.style.width = Length.Percent(32); spwA.style.marginRight = 33; spwA.style.flexShrink = 1;
            var spwB = Box(spwRow, "b", float.NaN, 50); spwB.style.width = Length.Percent(67); spwB.style.flexShrink = 1;
            var shrinkPercentMargin = Case(root, "shrinkPercentMargin", 0);
            shrinkPercentMargin.style.top = 2600;
            var spmRow = Box(shrinkPercentMargin, "row", 1632, 100); spmRow.style.flexDirection = FlexDirection.Row;
            var spmA = Box(spmRow, "a", 522, 50); spmA.style.marginRight = Length.Percent(2); spmA.style.flexShrink = 1;
            var spmB = Box(spmRow, "b", 1093, 50); spmB.style.flexShrink = 1;
            var shrinkNoMargin = Case(root, "shrinkNoMargin", 0);
            shrinkNoMargin.style.top = 2700;
            var snmRow = Box(shrinkNoMargin, "row", 1000, 100); snmRow.style.flexDirection = FlexDirection.Row;
            var snmA = Box(snmRow, "a", float.NaN, 50); snmA.style.width = Length.Percent(50); snmA.style.flexShrink = 1;
            var snmB = Box(snmRow, "b", float.NaN, 50); snmB.style.width = Length.Percent(60); snmB.style.flexShrink = 1;

            // growPercentMargin: a 1000 wide row with a 100 wide growing child with a 10% right margin
            // and a 100 wide one; shrinkColumnPercentMargin: a 1000 wide, 400 high column whose 300 and
            // 120 high children (the first with a 2% bottom margin) may shrink.
            var growPercentMargin = Case(root, "growPercentMargin", 0);
            growPercentMargin.style.top = 2800;
            var gpmRow = Box(growPercentMargin, "row", 1000, 100); gpmRow.style.flexDirection = FlexDirection.Row;
            var gpmA = Box(gpmRow, "a", 100, 50); gpmA.style.marginRight = Length.Percent(10); gpmA.style.flexGrow = 1;
            var gpmB = Box(gpmRow, "b", 100, 50);
            var shrinkColumnPercentMargin = Case(root, "shrinkColumnPercentMargin", 1100);
            shrinkColumnPercentMargin.style.top = 2800;
            var scpColumn = Box(shrinkColumnPercentMargin, "column", 1000, 400);
            var scpA = Box(scpColumn, "a", 50, 300); scpA.style.marginBottom = Length.Percent(2); scpA.style.flexShrink = 1;
            var scpB = Box(scpColumn, "b", 50, 120); scpB.style.flexShrink = 1;

            // shrinkRowMore: a 1632 wide row with 800 and 900 wide children, the first with a 2% right
            // margin; shrinkRowLastMargin: 522 and 1093 wide children, the last with a 2% right margin;
            // shrinkRowLeftMargin: the same with a 2% left margin on the first.
            var shrinkRowMore = Case(root, "shrinkRowMore", 0);
            shrinkRowMore.style.top = 3300;
            var srmRow = Box(shrinkRowMore, "row", 1632, 100); srmRow.style.flexDirection = FlexDirection.Row;
            var srmA = Box(srmRow, "a", 800, 50); srmA.style.marginRight = Length.Percent(2); srmA.style.flexShrink = 1;
            var srmB = Box(srmRow, "b", 900, 50); srmB.style.flexShrink = 1;
            var shrinkRowLastMargin = Case(root, "shrinkRowLastMargin", 0);
            shrinkRowLastMargin.style.top = 3400;
            var srlRow = Box(shrinkRowLastMargin, "row", 1632, 100); srlRow.style.flexDirection = FlexDirection.Row;
            var srlA = Box(srlRow, "a", 522, 50); srlA.style.flexShrink = 1;
            var srlB = Box(srlRow, "b", 1093, 50); srlB.style.marginRight = Length.Percent(2); srlB.style.flexShrink = 1;
            var shrinkRowLeftMargin = Case(root, "shrinkRowLeftMargin", 0);
            shrinkRowLeftMargin.style.top = 3500;
            var srfRow = Box(shrinkRowLeftMargin, "row", 1632, 100); srfRow.style.flexDirection = FlexDirection.Row;
            var srfA = Box(srfRow, "a", 522, 50); srfA.style.marginLeft = Length.Percent(2); srfA.style.flexShrink = 1;
            var srfB = Box(srfRow, "b", 1093, 50); srfB.style.flexShrink = 1;

            // measuredMargin: in a 1000 wide, 200 high row with a 50 bottom padding, centering its
            // children, a label (laid out by its text) and a plain element, each with a 25% basis, a
            // 100% height, 2% top and 0.5% side and bottom margins, a 2% bottom padding and a 10 top
            // padding.
            var measuredMargin = Case(root, "measuredMargin", 0);
            measuredMargin.style.top = 3600;
            var mgRow = Box(measuredMargin, "row", 1000, 200); mgRow.style.flexDirection = FlexDirection.Row;
            mgRow.style.alignItems = Align.Center; mgRow.style.paddingBottom = 50;
            var mgLabel = new Label("Ab") { name = "label" };
            var mgPlain = new VisualElement { name = "plain" };
            foreach (var ve in new[] { mgLabel, mgPlain })
            {
                ve.style.flexBasis = Length.Percent(25); ve.style.flexShrink = 0; ve.style.height = Length.Percent(100);
                ve.style.marginTop = Length.Percent(2); ve.style.marginLeft = Length.Percent(0.5f);
                ve.style.marginRight = Length.Percent(0.5f); ve.style.marginBottom = Length.Percent(0.5f);
                ve.style.paddingBottom = Length.Percent(2); ve.style.paddingTop = 10;
                mgRow.Add(ve);
            }

            // measuredPadding: in a 1000 wide, 400 high row aligning its children to the start, a label
            // 300 wide with wrapping text, an automatic height and 5% top, bottom and left paddings
            // (its text is measured at the width the paddings leave).
            var measuredPadding = Case(root, "measuredPadding", 0);
            measuredPadding.style.top = 3900;
            var mpRow = Box(measuredPadding, "row", 1000, 400); mpRow.style.flexDirection = FlexDirection.Row;
            mpRow.style.alignItems = Align.FlexStart;
            var mpLabel = new Label("Percentage paddings of a label decide the width its text wraps in") { name = "label" };
            mpLabel.style.width = 300; mpLabel.style.flexShrink = 0; mpLabel.style.whiteSpace = WhiteSpace.Normal;
            mpLabel.style.paddingTop = Length.Percent(5); mpLabel.style.paddingBottom = Length.Percent(5); mpLabel.style.paddingLeft = Length.Percent(5);
            mpRow.Add(mpLabel);

            // autoColumn*: in a 1000 wide, 400 high row centering its children, a column with an
            // automatic height holding a label of wrapping text with a 60% basis and an element with a
            // 40% basis, a 37% height and a 140 maximum height around a 54 high child: 400 wide and
            // neither growing nor shrinking (unchanged), or next to a 600 wide sibling with a 53% basis
            // (shrunk), a 30% basis growing (grown), a 40% basis (fits) or a 530 width (width shrunk).
            var autoColumns = new System.Collections.Generic.List<(string, VisualElement)>();
            void AutoColumn(string name, float top, Action<VisualElement, VisualElement> place)
            {
                var c = Case(root, "autoColumn" + name, 0);
                c.style.top = top;
                var row = Box(c, "row", 1000, 400); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
                var column = Box(row, "column"); column.style.alignItems = Align.Center;
                place(row, column);
                var label = new Label("A label of wrapping text in a column of automatic height") { name = "label" };
                label.style.width = 380; label.style.fontSize = 40; label.style.whiteSpace = WhiteSpace.Normal;
                label.style.flexBasis = Length.Percent(60); label.style.flexShrink = 0;
                column.Add(label);
                var box = Box(column, "box", 300); box.style.flexBasis = Length.Percent(40);
                box.style.height = Length.Percent(37); box.style.maxHeight = 140;
                Box(box, "inner", 50, 54);
                autoColumns.Add((name, column));
            }
            AutoColumn("Unchanged", 4400, (row, column) => column.style.width = 400);
            // The unchanged column with a 10x10 child placed 10% from the top, absolutely or relatively.
            AutoColumn("OffsetAbsolute", 9400, (row, column) =>
            {
                column.style.width = 400;
                var badge = Box(column, "badge", 10, 10); badge.style.position = Position.Absolute; badge.style.top = Length.Percent(10);
            });
            AutoColumn("OffsetRelative", 9900, (row, column) =>
            {
                column.style.width = 400;
                var badge = Box(column, "badge", 10, 10); badge.style.top = Length.Percent(10);
            });
            AutoColumn("Shrunk", 4900, (row, column) => { Box(row, "sibling", 600, 50).SendToBack(); column.style.flexBasis = Length.Percent(53); column.style.flexShrink = 1; });
            AutoColumn("Grown", 5400, (row, column) => { Box(row, "sibling", 600, 50).SendToBack(); column.style.flexBasis = Length.Percent(30); column.style.flexGrow = 1; });
            AutoColumn("Fits", 5900, (row, column) => { Box(row, "sibling", 600, 50).SendToBack(); column.style.flexBasis = Length.Percent(40); column.style.flexShrink = 1; });
            AutoColumn("WidthShrunk", 6400, (row, column) => { Box(row, "sibling", 600, 50).SendToBack(); column.style.width = 530; column.style.flexShrink = 1; });

            // Flexible lengths: growOverflow, a 1000 wide row with a growing 500 wide child with a 10%
            // right margin and a 450 wide one (the margins overflow it); growFraction, two 200 bases
            // growing by 0.25 each; growMax, three 0 bases growing in a 600 wide row, the first at
            // most 100 wide.
            var growOverflow = Case(root, "growOverflow", 0);
            growOverflow.style.top = 6900;
            var goRow = Box(growOverflow, "row", 1000, 100); goRow.style.flexDirection = FlexDirection.Row;
            var goA = Box(goRow, "a", 500, 50); goA.style.flexGrow = 1; goA.style.marginRight = Length.Percent(10);
            var goB = Box(goRow, "b", 450, 50);
            var growFraction = Case(root, "growFraction", 0);
            growFraction.style.top = 7000;
            var gfRow = Box(growFraction, "row", 1000, 100); gfRow.style.flexDirection = FlexDirection.Row;
            var gfA = Box(gfRow, "a", 200, 50); gfA.style.flexGrow = 0.25f;
            var gfB = Box(gfRow, "b", 200, 50); gfB.style.flexGrow = 0.25f;
            var growMax = Case(root, "growMax", 0);
            growMax.style.top = 7100;
            var gmRow = Box(growMax, "row", 600, 100); gmRow.style.flexDirection = FlexDirection.Row;
            var gmA = Box(gmRow, "a", float.NaN, 50); gmA.style.flexBasis = 0; gmA.style.flexGrow = 1; gmA.style.maxWidth = 100;
            var gmB = Box(gmRow, "b", float.NaN, 50); gmB.style.flexBasis = 0; gmB.style.flexGrow = 1;
            var gmC = Box(gmRow, "c", float.NaN, 50); gmC.style.flexBasis = 0; gmC.style.flexGrow = 1;
            // tabsAuto: in a 1592 wide, 300 high container, an absolutely placed row 14% high, between
            // 70% and 90% wide, growing, of three growing tabs, the first at least 12% wide.
            var tabsAuto = Case(root, "tabsAuto", 0);
            tabsAuto.style.top = 7200;
            var taOuter = Box(tabsAuto, "outer", 1592, 300);
            var taRow = Box(taOuter, "row"); taRow.style.position = Position.Absolute; taRow.style.flexDirection = FlexDirection.Row;
            taRow.style.height = Length.Percent(14); taRow.style.minWidth = Length.Percent(70); taRow.style.maxWidth = Length.Percent(90);
            taRow.style.flexGrow = 1; taRow.style.flexShrink = 1; taRow.style.left = Length.Percent(15);
            var taTabs = new VisualElement[3];
            for (int t = 0; t < 3; t++) { taTabs[t] = Box(taRow, "tab" + t); taTabs[t].style.flexGrow = 1; taTabs[t].style.flexShrink = 1; }
            taTabs[0].style.minWidth = Length.Percent(12);

            // centeredRow: in a column centring its items (984 wide, 415 high), a row growing from a
            // 0 basis of an item growing and shrinking around content 982.8 wide and a fixed 24 wide
            // one; the row with a fixed height instead (fixedHeight), stretched (stretched), sized by
            // its content (autoHeight), and a vertical ScrollView with a 19.5 right padding in such a
            // column (scrollView).
            var centeredRows = new System.Collections.Generic.List<(string, VisualElement, VisualElement, VisualElement)>();
            float crLeft = 0;
            foreach (var name in new[] { "grown", "fixedHeight", "stretched", "autoHeight", "grownSmall", "fixedHeightSmall", "grownPadded" })
            {
                var crCase = Case(root, "centeredRow." + name, crLeft);
                crCase.style.top = 7600;
                crLeft += 1100;
                var crOuter = Box(crCase, "outer", 984, 415);
                crOuter.style.alignItems = name == "stretched" ? Align.Stretch : Align.Center;
                var crRow = Box(crOuter, "row"); crRow.style.flexDirection = FlexDirection.Row; crRow.style.flexShrink = 1;
                if (name.StartsWith("fixedHeight")) crRow.style.height = 415;
                else if (name != "autoHeight") { crRow.style.flexGrow = 1; crRow.style.flexBasis = 0; }
                var crA = Box(crRow, "a"); crA.style.flexGrow = 1; crA.style.flexShrink = 1;
                Box(crA, "content", name.EndsWith("Small") ? 300 : 982.8f, 50);
                if (name == "grownPadded") { crRow.style.paddingLeft = 10; crRow.style.paddingRight = 6; }
                var crB = Box(crRow, "b", 24);
                centeredRows.Add((name, crRow, crA, crB));
            }
            // percentMinInAutoRow: in a 1423.5 wide row, a growing row sized by its content (auto) or
            // 1000 wide (fixed) holding an item growing and shrinking from a 0 basis, 95% wide with a
            // 95% min and max.
            var percentMinRows = new System.Collections.Generic.List<(string, VisualElement, VisualElement)>();
            float pmrLeft = 0;
            foreach (var name in new[] { "auto", "fixed" })
            {
                var pmrCase = Case(root, "percentMinInAutoRow." + name, pmrLeft);
                pmrCase.style.top = 8800;
                pmrLeft += 1600;
                var pmiOuter = Box(pmrCase, "outer", 1423.5f, 90); pmiOuter.style.flexDirection = FlexDirection.Row;
                var pmrInput = Box(pmiOuter, "input"); pmrInput.style.flexDirection = FlexDirection.Row; pmrInput.style.flexGrow = 1;
                if (name == "fixed") pmrInput.style.width = 1000;
                var pmrDrag = Box(pmrInput, "drag"); pmrDrag.style.flexGrow = 1; pmrDrag.style.flexShrink = 1; pmrDrag.style.flexBasis = 0; pmrDrag.style.height = 80;
                pmrDrag.style.width = Length.Percent(95); pmrDrag.style.minWidth = Length.Percent(95); pmrDrag.style.maxWidth = Length.Percent(95);
                percentMinRows.Add((name, pmrInput, pmrDrag));
            }
            // wrapCards: a 1000 wide wrapping row of three shrinking 50 high cards 32% wide with 1% (or
            // 10 point) side margins.
            var wrapCards = new System.Collections.Generic.List<(string, VisualElement[])>();
            float wcLeft = 0;
            foreach (var name in new[] { "percent", "points" })
            {
                var wcCase = Case(root, "wrapCards." + name, wcLeft);
                wcCase.style.top = 10400;
                wcLeft += 1100;
                var wcRow = Box(wcCase, "row", 1000, 200); wcRow.style.flexDirection = FlexDirection.Row; wcRow.style.flexWrap = Wrap.Wrap;
                var cards = new VisualElement[3];
                for (int c = 0; c < 3; c++)
                {
                    cards[c] = Box(wcRow, "card" + c, float.NaN, 50); cards[c].style.width = Length.Percent(32); cards[c].style.flexShrink = 1;
                    var margin = name == "percent" ? Length.Percent(1) : new Length(10);
                    cards[c].style.marginLeft = margin; cards[c].style.marginRight = margin;
                }
                wrapCards.Add((name, cards));
            }
            // absoluteText: a 192 x 88 container (a column, or a row in row*) holding an absolutely
            // placed 40 point label ("Shadow Ag", about 219 wide) at left 12 (default, nowrap,
            // wrapping, with right 12, with max-width in points/percent, with width > max-width,
            // with max-height in points/percent), at left 0, and an absolutely placed box around a
            // 250 wide child; inFlow, wrapInFlow and inFlowMaxPercent are controls.
            var absoluteTexts = new System.Collections.Generic.List<(string, VisualElement)>();
            float atLeft = 0;
            foreach (var name in new[] {
                "left", "noWrap", "leftRight", "noLeft", "rowLeft", "box", "inFlow",
                "wrap", "wrapInFlow", "wrapMaxWidth", "wrapMaxPercent", "wrapWidthOverMax",
                "wrapMaxHeight", "wrapMaxHeightPercent", "inFlowMaxPercent" })
            {
                var atCase = Case(root, "absoluteText." + name, atLeft);
                atCase.style.top = 10700;
                atLeft += 300;
                var atOuter = Box(atCase, "outer", 192, 88);
                if (name.StartsWith("row")) atOuter.style.flexDirection = FlexDirection.Row;
                VisualElement item;
                if (name == "box")
                {
                    item = Box(atOuter, "item");
                    Box(item, "child", 250, 20);
                }
                else
                {
                    var label = new Label("Shadow Ag") { name = "item" };
                    label.style.fontSize = 40;
                    if (name == "noWrap") label.style.whiteSpace = WhiteSpace.NoWrap;
                    else if (name.StartsWith("wrap") || name == "inFlowMaxPercent") label.style.whiteSpace = WhiteSpace.Normal;
                    if (name == "wrapMaxWidth" || name == "wrapMaxHeight") label.style.maxWidth = 150;
                    if (name == "wrapMaxPercent" || name == "inFlowMaxPercent") label.style.maxWidth = Length.Percent(75);
                    if (name == "wrapWidthOverMax") { label.style.width = 180; label.style.maxWidth = 120; }
                    if (name == "wrapMaxHeight") label.style.maxHeight = 40;
                    if (name == "wrapMaxHeightPercent") { label.style.height = 80; label.style.maxHeight = Length.Percent(50); }
                    atOuter.Add(label);
                    item = label;
                }
                if (name != "inFlow" && name != "wrapInFlow" && name != "inFlowMaxPercent")
                {
                    item.style.position = Position.Absolute;
                    item.style.top = 20;
                    item.style.left = name == "noLeft" ? 0 : 12;
                    if (name == "leftRight") item.style.right = 12;
                }
                absoluteTexts.Add((name, item));
            }
            var svCase = Case(root, "centeredRow.scrollView", 0);
            svCase.style.top = 8100;
            var svOuter = Box(svCase, "outer", 1003.5f, 535.5f);
            svOuter.style.alignItems = Align.Center;
            var scrollView = new ScrollView(ScrollViewMode.Vertical) { name = "scroll" };
            scrollView.style.flexGrow = 1; scrollView.style.flexShrink = 0; scrollView.style.paddingRight = 19.5f;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            svOuter.Add(scrollView);
            Box(scrollView, "content", 982.8f, 555);

            yield return Frames();
            foreach (var (name, column) in autoColumns)
                Put("autoColumn." + name, "column " + State(column) + "|label " + State(column.Q<Label>("label")) + "|box " + State(column.Q("box"))
                    + (column.Q("badge") is VisualElement badge ? "|badge " + State(badge) : ""));
            Put("growOverflow", "a " + State(goA) + "|b " + State(goB));
            Put("growFraction", "a " + State(gfA) + "|b " + State(gfB));
            Put("growMax", "a " + State(gmA) + "|b " + State(gmB) + "|c " + State(gmC));
            foreach (var (name, input, drag) in percentMinRows)
                Put("percentMinInAutoRow." + name, "input " + State(input) + "|drag " + State(drag));
            foreach (var (name, item) in absoluteTexts)
                Put("absoluteText." + name, State(item));
            foreach (var (name, cards) in wrapCards)
                Put("wrapCards." + name, string.Join("|", cards.Select((e, i) => "card" + i + " " + State(e))));
            foreach (var (name, row, a, b) in centeredRows)
                Put("centeredRow." + name, "row " + State(row) + "|a " + State(a) + "|b " + State(b));
            Put("centeredRow.scrollView", "scroll " + State(scrollView) + "|row " + State(scrollView.Q("unity-content-and-vertical-scroll-container"))
                + "|viewport " + State(scrollView.contentViewport) + "|scroller " + State(scrollView.verticalScroller));
            Put("tabsAuto", "row " + State(taRow) + "|" + string.Join("|", taTabs.Select((e, i) => "tab" + i + " " + State(e))));
            Put("measuredPadding", State(mpLabel) + "|content " + F(mpLabel.contentRect.x) + "," + F(mpLabel.contentRect.y) + "," + F(mpLabel.contentRect.width) + "," + F(mpLabel.contentRect.height));
            Put("measuredMargin", "label " + State(mgLabel) + "|plain " + State(mgPlain));
            Put("shrinkRowMore", "a " + State(srmA) + "|b " + State(srmB));
            Put("shrinkRowLastMargin", "a " + State(srlA) + "|b " + State(srlB));
            Put("shrinkRowLeftMargin", "a " + State(srfA) + "|b " + State(srfB));
            Put("growPercentMargin", "a " + State(gpmA) + "|b " + State(gpmB));
            Put("shrinkColumnPercentMargin", "a " + State(scpA) + "|b " + State(scpB));
            Put("shrinkPercentWidth", "a " + State(spwA) + "|b " + State(spwB));
            Put("shrinkPercentMargin", "a " + State(spmA) + "|b " + State(spmB));
            Put("shrinkNoMargin", "a " + State(snmA) + "|b " + State(snmB));
            Put("shrinkPoints", "a " + State(spA) + "|b " + State(spB));
            Put("shrinkBasis", "a " + State(sbA) + "|b " + State(sbB));
            Put("shrinkAbsolute", "row " + State(saRow) + "|a " + State(saA) + "|b " + State(saB));
            Put("shrinkRow", "a " + State(srA) + "|b " + State(srB));
            Put("minOverMax", "box " + State(mmBox) + "|absolute " + State(mmAbsolute));
            Put("percentUnderMax", State(pmBox));
            Put("relativeInColumn", "row " + State(rcRow) + "|end " + State(rcEnd));
            Put("relativeInRow", State(rrColumn));
            Put("marginPercent", "grow " + State(mpGrow) + "|percent " + State(mpPercent));
            Put("paddingPercent", "grow " + State(ppGrow) + "|percent " + State(ppPercent));
            Put("marginRow", "grow " + State(mrGrow) + "|percent " + State(mrPercent));
            Put("resize.first", "percent " + State(rPercent) + "|points " + State(rPoints) + "|auto " + State(rAuto));
            Put("resizeRow.first", State(rrPercent));
            Put("shrink.first", "a " + State(sA) + "|b " + State(sB));
            Put("sibling.first", "grow " + State(sibGrow) + "|percent " + State(sibPercent));
            Put("shown.hidden", "percent " + State(shPercent) + "|deep " + State(shDeep));
            Put("reshown.first", State(rePercent));
            Put("hiddenParent.hidden", State(hpPercent));

            rContainer.style.height = 200;
            rrContainer.style.width = 200;
            sContainer.style.height = 100;
            sibFixed.style.height = 200;
            shContainer.style.display = DisplayStyle.Flex;
            reContainer.style.display = DisplayStyle.None;
            hiddenParent.style.display = DisplayStyle.Flex;
            yield return Frames();
            Put("resize.smaller", "percent " + State(rPercent) + "|points " + State(rPoints) + "|auto " + State(rAuto));
            Put("resizeRow.smaller", State(rrPercent));
            Put("shrink.smaller", "a " + State(sA) + "|b " + State(sB));
            Put("sibling.taller", "grow " + State(sibGrow) + "|percent " + State(sibPercent));
            Put("shown.shown", "percent " + State(shPercent) + "|deep " + State(shDeep));
            Put("reshown.hidden", State(rePercent));
            Put("hiddenParent.shown", State(hpPercent));

            rContainer.style.height = 300;
            reContainer.style.display = DisplayStyle.Flex;
            yield return Frames();
            Put("resize.larger", "percent " + State(rPercent) + "|points " + State(rPoints) + "|auto " + State(rAuto));
            Put("reshown.shown", State(rePercent));

            // pixelRound: single-precision pixel-grid rounding boundaries at scale 1 and scale 1.5
            // (positive and negative half-ties straddling the 0.001f threshold).
            var prRoot = Case(root, "pixelRound", 0);
            prRoot.style.top = 12000;
            var prNodes = new List<(string name, VisualElement ve)>();
            foreach (var (name, left, width, height) in new (string, float, float, float)[]
            {
                ("pos_10_4989", 10.4989f, 10.4989f, 10f),
                ("pos_10_4990", 10.4990f, 10.4990f, 10f),
                ("pos_10_499023", 10.499023f, 10.499023f, 10f),
                ("pos_564_4989", 564.4989f, 564.4989f, 10f),
                ("pos_564_499023", 564.499023f, 564.499023f, 10f),
                ("neg_10_5009", -10.5009f, 10f, 10f),
                ("neg_10_5010", -10.5010f, 10f, 10f),
            })
            {
                var ve = Box(prRoot, name, width, height);
                ve.style.position = Position.Absolute;
                ve.style.left = left;
                ve.style.top = 0;
                prNodes.Add((name, ve));
            }
            yield return Frames();
            foreach (var (name, ve) in prNodes)
                Put("pixelRound.s1." + name, State(ve));

            var ps = GetComponent<UIDocument>().panelSettings;
            ps.scale = 1.5f;
            var pr15Root = Case(root, "pixelRound15", 0);
            pr15Root.style.top = 0;
            var pr15Nodes = new List<(string name, VisualElement ve)>();
            foreach (var (name, height) in new (string, float)[]
            {
                ("h_564_4989", 564.4989f / 1.5f),
                ("h_564_499023", 564.499023f / 1.5f),
                ("h_10_4989", 10.4989f / 1.5f),
                ("h_10_4995", 10.4995f / 1.5f),
            })
            {
                var ve = Box(pr15Root, name, 10f, height);
                ve.style.position = Position.Absolute;
                ve.style.left = 0;
                ve.style.top = 0;
                pr15Nodes.Add((name, ve));
            }
            yield return Frames();
            foreach (var (name, ve) in pr15Nodes)
                Put("pixelRound.s1_5." + name, State(ve));
            ps.scale = 1f;

            Put("complete", true);
            File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            EditorApplication.Exit(0);
        }

        static string F(float f) => f.ToString("0.###", Inv);

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(Convert.ToString(value, Inv))).Append("}\n");
        }

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
