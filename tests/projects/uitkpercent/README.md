# UI Toolkit percentages across layout passes

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (a 640x480 screen) on a screen panel
at constant pixel size, scale 1: a UIDocument made in Play mode. The layout does not depend on the OS
or the graphics device. The gate compares every value exactly.

`Assets/Scripts/PercentProbe.cs` builds one absolutely placed case per region, lays it out (changes
it and lays it out again where named) and records each element's layout rect and
`resolvedStyle.flexBasis`.

Flex basis:

- `resize.*`: a 400 high column container whose children have a 40% basis, a 50 point basis and an
  automatic basis (from a 30 high child), made 200 and then 300 high.
- `resizeRow.*`: a 400 wide row whose child has a 25% basis, made 200 wide.
- `shrink.*`: two shrinking children of 40% in a 400 high container made 100 high.
- `sibling.*`: an element that grows next to a 100 high sibling, whose child has a 50% basis; the
  sibling is made 200 high (the grown element itself is not changed).
- `shown.*`, `hiddenParent.*`: a container that is `display: none` at its first layout, then shown;
  its child (40%) and grandchild (30% of a 200 high element) are measured hidden and shown.
- `reshown.*`: a container laid out visible, then hidden, then shown again.

A percentage basis is resolved again each time, against the container's current size whatever
changed it, also within one layout pass (`sibling.first`: a 50% basis inside a grown element takes
the grown height); inside a hidden element the layout and the basis are 0. A basis in points and an
automatic basis are controls.

Margins, paddings, offsets, minimum and maximum:

- `marginPercent`, `paddingPercent`, `marginRow`: 10% margins (or paddings) of an element that grows
  next to a fixed sibling, with a 50% basis child: they take the owner's width on both axes, also for
  the space the element's children are laid out in. `measuredMargin`: the same for an element laid out
  by its text (a label), which is as large as a plain element with the same styles;
  `measuredPadding`: a 300 wide label with wrapping text and 5% paddings in a 1000 wide row takes 50
  of padding on each side and measures its text in what is left.
- `relativeInColumn`, `relativeInRow`: relatively placed elements with `left`/`top` (and
  `right`/`bottom`) in percent: left and right take the owner's width, top and bottom its height,
  whatever the element's own flex direction.
- `minOverMax`: a minimum size wins over a smaller maximum (also for an absolutely placed element sized
  in percent); `percentUnderMax`: width, min-width and max-width of 85% give 85% of the owner.

Flexible lengths (`shrink*`, `grow*`, `tabsAuto`): whether a line's items grow or shrink is decided
from the space they consume with percentage margins taken as 0, the space then shared takes the
margins whole: a row whose overflow is only its items' 2% margin does not shrink (`shrinkAbsolute`,
`shrinkRow`, `shrinkBasis`, `shrinkPercentMargin`, `shrinkRowLastMargin`, `shrinkRowLeftMargin`), one
that overflows anyway shrinks by the overflow with the margin (`shrinkRowMore`), and a growing item
gives back an overflow its margin makes (`growOverflow`). Flex factors summing under 1 share that part
of the free space (`growFraction`); an item held by its max is frozen there (`growMax`); `tabsAuto` is a
row between 70% and 90% wide of growing tabs, one at least 12% wide. Point margins, a column and point
widths are controls (`shrinkPoints`, `shrinkPercentWidth`, `shrinkColumnPercentMargin`).

Percentages of a size not known yet (`autoColumn*`): a column with an automatic height in a row holds a
60% basis and a 40% basis with a 37% height: laid out once (400 wide, neither growing nor shrinking)
its children take their content's sizes; when its own size may follow its owner's (it shrinks, grows
or has a percentage basis) it is laid out again at the height found and the percentages take it. A
child placed 10% from the top, absolutely or relatively, also lays the unchanged column out again
(`autoColumn.OffsetAbsolute`, `autoColumn.OffsetRelative`).

Wrapping (`wrapCards.*`): in a 1000 wide wrapping row, three cards 32% wide with 1% side margins stay
on one line (lines break with percentage margins as 0); with 10 point margins the third wraps.

Containers without an exact main size (`centeredRow.*`): in a 984 wide column centring its items, a
row of an item around content 982.8 (or 300) wide and a fixed 24 wide one is as long as its items'
bases plus its padding (1006.8, centred at -11.4), while its items grow or shrink to the 984 it was
given when it and they grow (`grown`, `grownSmall`, `grownPadded`; `fixedHeight*` and `autoHeight` do
not grow, `stretched` is exact); a vertical ScrollView in such a column keeps its content's height
(`scrollView`). `percentMinInAutoRow.*`: in a row sized by its content, an item 95% wide with a 95%
min and max from a 0 basis gives the row no basis (`auto`); in a 1000 wide row (`fixed`) its
percentages resolve.

Other releases are unmeasured.

## Usage

```bash
python3 tests/projects/uitkpercent/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/uitkpercent/run.py publish run-a run-b
python3 tests/projects/uitkpercent/run.py gate
```

The project takes its scene, panel settings, packages and settings from `tests/projects/uitkpanelutils`.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical macOS batch captures.
