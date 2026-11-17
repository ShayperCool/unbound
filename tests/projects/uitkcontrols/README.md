# Runtime UI Toolkit controls

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode: managed element state and the
layout of a runtime panel (a UIDocument made in Play mode, drawing into an 800x600 target), which
depend on neither the OS nor the graphics device. No row measures text. The gate compares every value
exactly (tolerance 0). `Assets/Scripts/ControlsProbe.cs` records:

- `help.*`: HelpBox's hierarchy and USS classes for each message type (the icon is inserted before the
  label, and leaves the hierarchy for None and undefined values), a null text, a game child;
- `popup.*`: PopupWindow's content container, title and children;
- `state.*`: ToggleButtonGroupState: the constructor's masking of bits past the length, ToString,
  the indexer and its exceptions (type, parameter and message; the runtime's own parameter suffix
  is not compared), active and inactive options, set/toggle/reset all, CreateFromOptions,
  FromEnumFlags/ToEnumFlags/Compare with byte, short, int and long flag enums (also at the full
  length of 64, where a short's sign extension shows) and a non-flag enum,
  equality, CompareTo, JsonUtility, and ToggleButtonGroupStatePropertiesAttribute's defaults;
- `group.*`: ToggleButtonGroup: its hierarchy with and without a label and with no options (the
  empty label), buttons added one by one (their classes, the first option selected when the
  selection may not be empty), a non-button child (moved to the group), submitted clicks
  (NavigationSubmitEvent) under single and multiple selection with and without empty selection,
  each ChangeEvent, switching those modes, a longer value, mixed values, removing buttons, more
  than 64 buttons (the warning);
- `split.*`: TwoPaneSplitView: its hierarchy and classes per orientation; after layout, the panes'
  layout, the drag line anchor and the content padding for a fixed first pane, a fixed second pane
  (with a flexed minimum), a vertical view (with a fixed minimum) and a collapse requested before
  layout; the view shrunk, shrunk past both minimums and grown again; pointer drags of the line
  (also past the minimum and the maximum); a view with a view-data key, which keeps its dragged size
  when a property change sets it up again (one without a key goes back to the initial dimension);
  collapsing, restoring and an invalid index; changing the fixed pane index, the orientation and the
  initial dimension; one child only;
- `uxml.*`: the four controls from `Resources/Controls.uxml` through the UXML serialization path and
  through game subclasses whose deprecated UxmlFactory uses the built-in UxmlTraits
  (`fixed-pane-initial-dimension` is read as an integer there, so `80.5` gives the default 100).

`split.dragLogs` is a known residual (residuals.json): the Editor warns that a runtime keyword
cursor needs a texture when the dragged pointer is over the drag line; Unbound's runtime panels do
not apply USS cursors yet. Other releases are unmeasured.
