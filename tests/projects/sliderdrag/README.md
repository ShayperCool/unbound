# Slider label drags

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 160 rows. The values are managed slider state and
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
- `IValueField` calls made directly (`direct.*`) add to the current value.
- SliderInt's own state: defaults, classes, whole page sizes, page and Home/End keys, a reversed
  range and clamping (`int.default`, `int.classes`, `int.pageSize.*`, `int.key.*`, `int.narrow.*`,
  `int.reversed.*`). Arrow keys do nothing without navigation (`int.key.right*`).

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [IValueField](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.IValueField_1.html) | `slider.valueField`, `direct.*` |
| [FieldMouseDragger](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.FieldMouseDragger_1.html) | `down`, `up`, `escape`, `right.button` |
| [SliderInt](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.SliderInt.html) | `int.*` |

The probe formats floats itself: a first capture concatenated generic values, which the Editor's
Mono prints with 7 significant digits while Unbound printed the shortest round-trip string (a call
through a `constrained.` type parameter is not redirected to the Mono number format). Other
releases are unmeasured.
