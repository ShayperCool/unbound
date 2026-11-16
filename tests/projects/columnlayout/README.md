# Multi-column layout lifetime

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 11 rows (header column widths, laid out in a
runtime panel whose scale is `96 / Screen.dpi`, so a point is a pixel on every display). The gate
compares every row exactly.

The release's header does not dispose its column layout when it is disposed (a replaced view
controller disposes its header): the layout stays subscribed to the columns. Unbound detached it,
through a `ColumnLayout.Dispose` the release does not declare.

- A MultiColumnListView 400 points wide in Grow mode with two stretchable 150-point columns and a
  hidden 120-point column: the third column is shown and hidden before and after
  `SetViewController` replaces the controller (`start` .. `hide2`). The widths are the same either
  way.
- `stale.*` tells the two apart. The two stretchable columns are limited to 150 points and the
  hidden column is 200; the controller is replaced at 400 points and the view is then widened to
  600. Showing the column, the replaced controller's layout, still following the columns with the
  width it last laid out (400), makes room for it: both stretchable columns shrink to 100
  (`stale.show`), though the current layout has room. Detaching gave 150, 150, 200.

Other releases are unmeasured.
