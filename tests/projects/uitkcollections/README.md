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
  defaults;
- `binding`: runtime data binding over two frames per step (a panel update, then a repaint, which
  is when bindings update): labels, a text field and a slider bound to plain, notifying and
  view-hash sources and through the hierarchy, each binding mode and update trigger, local and
  global converters, UI changes written to the source, a custom binding's calls (activation, data
  source changes, updates), results, a binding cleared, an element removed and re-added, a list
  whose rows get their data sources automatically, a tree with makeItem and no bindItem.

After the final image, on a cover above everything else (so the lines and images before them do
not depend on them):

- `sibling-order`: PlaceBehind, PlaceInFront, BringToFront and SendToBack on five children of a
  row, forward and backward, with where layout puts them;
- `menu-api`: a DropdownMenu on its own: which separators are kept, inserted items, statuses
  before and after PrepareForDisplay, the event info, Execute;
- `manipulator`: a ContextualMenuManipulator on the runtime panel (which has no contextual menu
  manager): a right click, the Menu key, then a ContextualMenuPopulateEvent the probe sends itself;
- `header-menu`: the menu a MultiColumnListView header fills for such an event (also on a
  MultiColumnTreeView): its entries and their statuses over a column, past the columns, with a
  hidden, a non-optional, an unnamed and a primary column, stretchable columns in both stretch
  modes, no visible column, a game's own entry; what "Resize To Fit" and the column entries do;
- `header-drag`: a header column dragged step by step (press, a move short of a drag, the move
  preview and drop line, either half of the target column, release), drops past the columns and
  left of the view, onto the column's own and its neighbours' halves, with sorting on (a click,
  a short move and a drag), Escape, a lost pointer capture, columns that are not reorderable, a
  hidden column, `Columns.ReorderDisplay`, a column added afterwards, a column hidden or removed
  while its header column is pressed, and a tree's primary column;
- `tree-data`: a TreeView of 3x2x1 items changed through its controller: Move (to another parent,
  among siblings, to the root, with indices past the end and below -1, into itself, under its own
  descendant, with unknown ids, without rebuild, of the selected item, into a collapsed item),
  AddItem (at the root, at a child index, with children, without rebuild, under an unknown
  parent, with an id already in the tree) and TryRemoveItem (a leaf, an item with children, an
  unknown id, the selected item, without rebuild), each with the hierarchy the controller answers
  and the children each TreeViewItemData holds;
- `tree-drag`: a reorderable TreeView dragged by pointer in one go: between rows, onto a row, to
  the top of the first row, outside the view, onto a collapsed row, and with `reorderable` off;
- `animated-clock` and `tree-drag-positions` run on a clock the probe moves (the panels' time
  function is replaced for the section), so animations and timers are at a known time:
  an animated ListView drag step by step (the gap's padding and height while it opens, a release
  and a second drag while gaps still animate, a control after they settled); tree drags step by
  step (the parent and child index every pointer position gives `dragAndDropUpdate` and
  `handleDrop`, the hover bar and markers, the depth picked by the pointer's x, drops below the
  rows, three items at once with an item's own child among them, an item onto its own
  descendant, a collapsed row opening under a hovering drag and closing when the drag ends,
  `canStartDrag` false and `handleDrop` Rejected);
- `tree-controls` (same clock): single rules of tree data and drags: an item added at a child
  index and a move among siblings to an index below -1, both without a rebuild; a parent whose
  data is null; root items that give an id twice; a drag resting over a row that has opened
  while item 0 is collapsed.

Every step records the item callbacks it caused (makeItem/bindItem/unbindItem/destroyItem, with
the element: elements are numbered per control), the events the controls raised, and the state:
visible rows (element, text, layout, selected, shown), scroll offset, content rect, scroller
range, selection, items order, element trees. The panel is rendered after building
(`collections-built.png`) and at the end (`collections-final.png`, with the animated list hidden:
its rows animate in real time, so that section records no row layout).

Reference history: the commit that added the binding `switch-*` steps also re-recorded the three
steps after them (`clear`, `remove-custom`, `readd-custom`), although its `Oracle-Change` note
says existing lines were unchanged.

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
