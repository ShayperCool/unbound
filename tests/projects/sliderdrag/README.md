# Slider label drags

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 240 rows. The values are managed slider state and
change notifications in a runtime panel, so they do not depend on the OS or the graphics device.
The gate compares every missing, extra or unequal row exactly.

The probe sends pointer and key events to slider labels and records, after every step, the value,
the change notification and whether the label holds the mouse capture:

- `Slider` and `SliderInt` are `IValueField`s; the label of every slider (also one without a label
  text) is a drag zone with the `--with-dragger` class.
- A left press on the label captures the mouse; each move changes the value by its length signed by
  the dominant axis (`right10`, `up6`, `diagonal`, `near.horizontal`), with shift four times and
  alt a quarter as much (`shift5`, `alt8`), clamped to the range (`past.*`). Release ends the drag
  (`move.after.up`); Escape restores the start value and ends it (`escape*`); the right button does
  not start one (`right.button`).
- The sensitivity is 0.03 % of the range per pixel (`wide.*`); an integer slider rounds each move
  half to even, at least one step per pixel (`int.*`).
- While a move's two axes are within a tenth of each other, the axis of the move before gives its
  sign, also when that move was on another field (`axis.*`, `int.axis.shared`).
- A touch press on the label does not start a drag in a runtime panel (`touch.*`).
- `IValueField` calls made directly (`direct.*`) add to the current value.
- SliderInt's own state: defaults, classes, whole page sizes, page and Home/End keys, a reversed
  range and clamping (`int.default`, `int.classes`, `int.pageSize.*`, `int.key.*`, `int.narrow.*`,
  `int.reversed.*`). Arrow keys do nothing without navigation (`int.key.right*`); navigation moves
  step once a submit has made the dragger movable (`int.narrow.nav.*`, `int.keyed.nav.*`).

- Tracker clicks and dragger drags of SliderInt (`track*.click.*`, `drag.*`): without a page size a
  click jumps the dragger there and a drag moves it, the value rounded to a whole number; with one, a
  click beside the dragger steps by the page size towards the click's side of the value range. The
  step is negated when the range is reversed, when `inverted` is set on an increasing range, or for
  a vertical slider that is not inverted (any one of them, not each in turn: `track6`, vertical and
  reversed, steps down at the bottom); on a horizontal slider `inverted` does not change which side
  of the screen steps up (`track2` equals `track1`), on a vertical one it does (`track5`). The
  trackers' labels have no width, so their size does not depend on text layout. Each press is preceded by a pointer move to the same place: a press
  acts only when the element under the pointer, which the panel takes from the pointer events
  themselves, is inside the tracker.

The panel's scale is set to `96 / Screen.dpi`, so a panel point is a screen pixel on every display
(the macOS Editor's batch-mode screen is 640x480 at 255 dpi; without this its panel is 241 points
wide and clicks at the far end of a 300-point slider fall outside it). The engine runs at 640x480,
the Editor's batch-mode screen size.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [IValueField](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.IValueField_1.html) | `slider.valueField`, `direct.*` |
| [FieldMouseDragger](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.FieldMouseDragger_1.html) | `down`, `up`, `escape`, `right.button` |
| [SliderInt](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.SliderInt.html) | `int.*` |

The probe formats floats itself: a first capture concatenated generic values, which the Editor's
Mono prints with 7 significant digits while Unbound printed the shortest round-trip string (a call
through a `constrained.` type parameter is not redirected to the Mono number format; its gap row
is `Mono number format of generic values`). Other releases are unmeasured.
