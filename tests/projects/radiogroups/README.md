# Radio button groups and GroupBox

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 32 rows (managed element state and change events
in a runtime panel, independent of the OS and the graphics device). The gate compares every row
exactly. Each row lists the checked buttons and the change events since the previous row, in order.

- `panel.*`: buttons under no GroupBox form one group for the panel whatever their parents;
  checking one sends its own change event first, then unchecks the checked one; unchecking leaves
  none checked; `SetValueWithoutNotify(true)` checks without unchecking the others.
- `groupbox.*`: each GroupBox, nested ones too, is its own group; `SetSelected`; a button moved
  into another group joins it (its group then holds two checked buttons until the next selection,
  which unchecks the others in the order they joined); GroupBox's title label, made from the text
  and removed when it is cleared.
- `detached.*`: buttons not in a panel are in no group (both stay checked); after attaching they
  join the panel's group, which unchecks them on the next selection; after detaching they leave it.
- `order.events`: the others are unchecked in the order they joined the group (attached), not in
  hierarchy order.
- `rbg.*`, `rbg2.*`: RadioButtonGroup: choices, value, a checked option, an added button (its
  index after the choices), value 0 and -1, and a checked button added to a group that has a value
  (unchecked when the group next reconciles its buttons); `rbg3.*`: `SetValueWithoutNotify` on a
  group checks and unchecks its radios through their value (they send change events), and a group
  with a value leaving its panel reads -1 (its checked radio left the group; no event outside the
  panel) and takes the value back silently when it returns.

Other releases are unmeasured.
