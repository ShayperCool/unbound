# USS values outside their property's syntax

Unity 6000.0.71f1, Linux Editor in batch mode, a runtime panel (a UIDocument made in Play mode
drawing into a 400x300 target). The values depend on neither the OS nor the graphics device; the gate
compares every value exactly.

`Assets/Resources/Invalid.uss` gives one rule per case a value its property's syntax does not take
(a length without a unit, a word, too many values, an override by an invalid declaration, colors,
enum and keyword values, an image, times, transition properties and easing, variables, an unknown
property, an integer, a number, a font style, too many border colors, `inherit` and `unset`) beside
valid controls (`flex`, whose syntax names other properties').
`Broken.uss` holds a value the parser cannot read beside a valid rule, `Errors.uss` values the
importer reports as errors. `Assets/Editor/UssValidationRunner.cs` re-imports the sheets while
recording the importer's logs; `Assets/Scripts/UssValidationProbe.cs` adds them (with what loading
the sheets logs) as `import.logs`, then adds one element per rule to the panel, one at a time, and
records the rule's property as resolved and what the style pass logged (`style.<class>[.logs]`).

What the Editor does:

- The importer checks each declaration against its property's syntax and warns ("Expected (syntax)
  but found 'value'", or "Expected end of value but found ..." for extra values, "Unknown property
  'x' (did you mean 'y'?)", with the declaration and a hint for a number without a unit or an
  unknown color) but keeps the declaration; `inherit` and `unset` are warned about too. A unit it does not know or a variable without `--` is
  an error: the whole sheet then has no rules (`style.errors-*`), as one the parser cannot read
  (`style.broken-*`).
- The style pass reads what was kept: a number without a unit as pixels or seconds, the first of
  too many values, a word for a length as 0 with an error, an unknown color word as clear, an
  enum or keyword it does not know as the default; a dimension for `transition-property` gives
  no property; a dimension for `background-image` the Editor's warning icon with a warning.

Other releases are unmeasured.
