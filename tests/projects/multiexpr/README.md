# Multi-object expression probe

Records Unity 6000.0.71f1 numeric fields (`IntegerField`, `FloatField`, `LongField`, `DoubleField`,
`UnsignedIntegerField`) bound to one property of two objects (and an `IntegerField` of three)
when their text is committed as an expression: `L(0,10)`, `+=5`, `*=2`, `R(5,5)`, `L(1,1)` (with
per-object variables) and `2+3` (without). For each, both objects' values before and after, the
field's text and mixed state, and the undo record's name.

The text is set and committed as Return does (`ITextEdition.UpdateText`, then the field's
`UpdateValueFromText`), without focusing the field: a focused field of several objects commits the
value it shows when it loses focus, and key events sent to a runtime panel's text input in batch
mode insert each character at the start.

## Usage

```bash
python3 tests/projects/multiexpr/run.py record --label run-a   # Editor, batch mode
python3 tests/projects/multiexpr/run.py publish run-a run-b
python3 tests/projects/multiexpr/run.py gate
```

The values depend on neither the OS nor the graphics device (`macos_batch`).

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical captures.
