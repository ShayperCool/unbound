# Numeric fields and ExpressionEvaluator

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 646 rows (managed values and text in a runtime
panel, independent of the OS and the graphics device). The gate compares every row exactly. Float
and double values are recorded as round-trip text and bits.

- `eval.*`: `ExpressionEvaluator.Evaluate` of 66 expressions as float, int, double, long and ulong:
  operators and precedence (`2^-1` does not evaluate: the negation is read as an operator before
  the exponent), functions, unbalanced parentheses, trailing operators, `pi`, a comma as the
  decimal point, `f`/`d`/`l` suffixes, expressions that need a value (`x`, `+=`, `L`, `R`, `#`)
  and literals beyond a type's range. Intermediate results keep the digits the Editor's runtime
  prints (15 significant digits: `1/3*3` is 0.999999999999999 as a double), and a literal beyond
  the double range or a division by zero does not evaluate. C# leaves the conversion of a double
  outside an integer type's range unspecified, so those integer results are recorded as
  `<unspecified>` and not compared.
- Per field (`int`, `long`, `float`, `double`, `uint`, `ulong`): default state, classes, `value`
  and `SetValueWithoutNotify` (change events), `formatString` (and null), typed text committed with
  Return (`*.typeN.typed` is the state after typing: the value follows every keystroke while the
  text stays as typed; `.committed` after Return), rejected characters, values beyond the type
  (saturated), "inf"/"infinity"/"nan" in real fields, `+=` and `*=` relative to the text the edit
  started from, a delayed field, input device deltas at three speeds, a drag on the label, a
  read-only field (no label dragger, typing rejected), mixed values, and `ClearValue`.
- `plain`, `short`: a field without a label and one with a maximum length of 3.
- `uxml.*`: the fields from UXML: `value`, `label`, `is-delayed`, `readonly`, `max-length`; the
  numeric fields take no `format-string`; text inputs read `readonly`, `password` and
  `mask-character`, not their property names (`is-read-only`, `is-password-field`, `mask-char`).
- `binding.*`: a two-way `DataBinding` of an IntegerField's value to a plain object's property.

Other releases are unmeasured.
