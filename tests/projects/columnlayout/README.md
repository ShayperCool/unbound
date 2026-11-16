# Multi-column layout lifetime

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 10 rows (header column widths, laid out in a
runtime panel whose scale is `96 / Screen.dpi`, so a point is a pixel on every display). The gate
compares every row exactly.

A MultiColumnListView 400 points wide in Grow mode has two stretchable 150-point columns and a
hidden 120-point column. The probe shows and hides the third column, replaces the view controller
(`SetViewController` with a new `MultiColumnListViewController`, which disposes the old header),
then shows and hides it again, and records the header columns' widths after each step.

The release's header does not dispose its column layout, which stays subscribed to the columns;
Unbound did, through a `ColumnLayout.Dispose` the release does not declare. These controls give the
same widths either way (a lingering layout only marks itself dirty; the current header's layout sets
the widths), on the runtime before and after the change. They guard the widths around the
controller replacement; they do not tell the two lifetimes apart, which no control found here does.

Other releases are unmeasured.
