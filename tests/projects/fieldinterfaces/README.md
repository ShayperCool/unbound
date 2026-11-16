# Field interfaces

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 36 rows (managed field state and notifications in a
runtime panel, independent of the OS and the graphics device). The gate compares every row exactly.

- `interfaces.*`: every BaseField (TextField, Toggle, Slider, SliderInt, MinMaxSlider, EnumField,
  RadioButtonGroup, DropdownField) implements IMixedValueSupport and the internal IPrefixLabel and
  IEditableElement; Label and Foldout implement none of them.
- `mixed.*`: setting showMixedValue through IMixedValueSupport is the field's own property (a text
  field shows the mixed-value dash, then its text again).
- `prefix.*`: IPrefixLabel answers the field's label and label element, following a renamed label;
  a field made without a label answers an empty label and its (empty) label element.
- `editable.initial`, `text.*`, `toggle.*`, `slider.*`, `range.*`: IEditableElement's
  editingStarted and editingEnded (set through reflection) are null at first. A text field raises
  editingStarted when it gains focus and editingEnded when it loses it; a toggle starts on a release
  over it, not on focus, and ends when it loses focus; a slider starts on a press on its label or
  drag area and ends on a release over the drag area, not on focus; a min-max slider starts on a
  press on its input and ends on the release.
- `detached`, `reattached.*`: a field removed from the panel and added back twice raises one
  notification per focus change (its callbacks are not registered twice).
- `focusedDetach`: removing the focused field from the panel leaves nothing focused and raises no
  editingEnded.
- `replaced`, `cleared`: a replaced handler alone is told; a cleared one stops the notifications.

Other releases are unmeasured.
