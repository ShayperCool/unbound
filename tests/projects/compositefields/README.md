# Composite fields and Hash128Field

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 140 rows (managed values, texts and element trees in
a runtime panel, independent of the OS and the graphics device). The gate compares every row
exactly.

- Per composite field (`v2`, `v3`, `v4`, `v2i`, `v3i`, `rect`, `recti`, `bounds`,
  `boundsi`): the element tree (sub-field names, classes, spacers and two-line groups, the inner
  input types), the default value, `value` and `SetValueWithoutNotify` (change events and the
  sub-fields' texts), text typed into the first and the last numeric sub-field and committed with
  Return (an expression keeps its typed text while the whole value follows), a drag on the first
  sub-field's label, and mixed values reaching the sub-fields.
- `plain.tree`: a Vector3Field without a label.
- `hash.*`: Hash128Field: the default and a set value, typed hexadecimal (other characters
  rejected; one digit reads as that number; longer text reads its first 32 digits), upper case, and an
  emptied field leaving focus.
- `uxml.*`: the `value` attribute of every type (bounds take a center and a size) and the
  older per-component attributes (`x`, `y`, `z` set those components; the others stay 0).
- `binding.*`: a two-way `DataBinding` of a Vector3Field's value, including a value typed into a
  sub-field reaching the source.

Other releases are unmeasured.
