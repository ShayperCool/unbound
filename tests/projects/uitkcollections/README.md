# UI Toolkit collection controls

Stages the `uitk` fixture in a fresh cache directory and replaces its interaction run with
`Assets/Scripts/CollectionsProbe.cs`, which builds on the runtime panel (800x600):

- `fixed`: a ListView of 100 rows (fixed height 20): scrolling, ScrollToItem, RefreshItem,
  RefreshItems, Rebuild, items added and removed while scrolled, item height and view size
  changes, clicks (with Ctrl and Shift, double click), navigation and keys, the selection API,
  selection types, a new items source, a new makeItem, a null source, detaching and attaching;
- `reorder` and `animated`: reorderable ListViews (simple and animated mode) dragged by pointer:
  one row down and up, a move short of a drag, a selection of two rows, a drag to the end (in
  animated mode each drag starts once the rows' 500 ms gap animations have settled, so where the
  pointer lands does not depend on the machine's speed);
- `footer`: a ListView with the foldout header, size field and add/remove footer: "+", "-" with
  and without a selection, a size typed in the field, collapsing the header, onAdd/onRemove,
  removing the footer and header, an empty source;
- `dynamic`: a ListView with dynamic height rows (16 to 48 points): scrolling and ScrollToItem
  (each followed by a second update), a click, RefreshItems, Rebuild;
- `tree`: a TreeView of 3x3x2 items: ExpandItem (with children), toggle presses (with Alt),
  ExpandAll, CollapseAll, selection by id, navigation right/left, new root items;
- `columns` and `column-tree`: MultiColumnListView and MultiColumnTreeView: scrolling, column
  visibility and width, a click, columns added and removed, sorting (sortingMode Default with a
  sort description, header clicks plain, again, with Ctrl and with Shift; only the sorting mode
  changed to None and Custom; a tree of uneven shape sorted among siblings, with each row's indent
  and toggle), a resize handle dragged and the widths restored, a Grow-mode view resized, and the
  primary column named;
- `tabs`: a TabView of three tabs (the last closeable): header presses, selectedTabIndex, the
  close button, a tab added later, activeTab, removing the active tab.
- `absolute`: one absolutely positioned child per case in a padded, bordered parent (a tab
  header's underline is such a child): insets, percentage width/height/offsets/margins/min/max,
  no offsets under each justify-content, align-items and direction;
- `animations`: transition animations (`experimental.animation`) of 1 ms with a pause before the
  panel update, so each completes on its first tick: values, completion callbacks, recycling,
  KeepAlive, StyleValues read from the element, the flexShrink setter, an animation started on an
  element outside a panel; every Easing curve over a grid of progress values and an animation's
  defaults.

Every step records the item callbacks it caused (makeItem/bindItem/unbindItem/destroyItem, with
the element: elements are numbered per control), the events the controls raised, and the state:
visible rows (element, text, layout, selected, shown), scroll offset, content rect, scroller
range, selection, items order, element trees. The panel is rendered after building
(`collections-built.png`) and at the end (`collections-final.png`, with the animated list hidden:
its rows animate in real time, so that section records no row layout).

```sh
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/uitkcollections"
python3 tests/projects/uitkcollections/run.py native --release 6000.0.71f1 --output "$cache/native-a"
python3 tests/projects/uitkcollections/run.py native --release 6000.0.71f1 --output "$cache/native-b"
cmp "$cache/native-a/capture/collections.jsonl" "$cache/native-b/capture/collections.jsonl"
python3 tests/projects/uitkcollections/run.py engine --release 6000.0.71f1 --output "$cache/engine" \
  --cli /path/to/unbound --managed /path/to/managed
python3 tests/projects/uitkcollections/verify.py --release 6000.0.71f1 --candidate "$cache/engine/capture"
```

The gate (`gate.json`) runs `run.py gate`: the engine capture into a fresh cache run, then
`verify.py`, which compares every line (numbers within 1e-3) and prints image measurements.
Sections listed in its `OPEN` table (none at present) are reported with their counts but not
gated. The published reference is one of two byte-identical Editor recordings.
