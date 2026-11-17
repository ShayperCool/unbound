# Text field command and IME events

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 66 rows (managed text state and event propagation
only, independent of the OS and the graphics device). The gate compares every row exactly.

Four TextFields (plain, read-only, multiline and one that is focused elsewhere or disabled) sit in a
container that counts the command and IME events passing it on the way down (`trickled`) and up
(`bubbled`); the panel root counts the command events that reach it (`reached`). Command events are
sent to a field's text element, or to the panel without a target (`panel.*`). Each row after an
`execute` also records the field's text, value, cursor, selection, the clipboard and the value
changes it caused. Selections are applied again until they hold: the indices are clamped to the
laid out text, which a batch-mode Editor sometimes lays out a frame later.

- `validate.*`: a focused editable field takes (stops) Paste with text on the clipboard, Cut and
  Copy with a selection, Delete, SelectAll, UndoRedoPerformed and every command it does not know
  (`Duplicate`, `Find`, `OnLostFocus`); Copy and Cut without a selection and Paste with an empty
  clipboard pass on. Unfocused (`unfocused.*`), read-only (`locked.*`: Copy and SelectAll only) and
  disabled fields (`disabled.*`; disabling a focused field blurs it) are covered too.
- `execute.*`: Copy, Cut, Paste (line breaks become spaces in a single-line field, `multi.*` keeps
  them), SelectAll, OnLostFocus (taken, no change) and Delete, which deletes the selection or the
  character after the cursor without copying (`execute.delete.noSelection`); Cut without a selection
  and Paste with an empty clipboard are taken without a change; unknown commands pass on.
- `panel.*`: an event sent to the panel without a target reaches the focused field.
- `reuse.*`, `imgui.*`: a pooled event comes back reset (no command name, propagation not
  stopped); a command event made from an IMGUI event reads that event's command name.
- `ime.*`: focusing an editable field sets `Input.imeCompositionMode` to On, its blur back to Auto;
  focusing a read-only field leaves a mode set before (`ime.lockedFocused`, Off: neither On nor
  Auto).
- `imeEvent.*`: the internal IMEEvent (reflection) is pooled with its composition string,
  trickles down and bubbles without being stopped, still passes enabled ancestors of a disabled
  target, and goes nowhere when sent to the panel without a target. With no input method composing,
  it leaves the text unchanged. The composition preview itself (a composition replaces the
  selection and shows at the cursor) needs a composing input method, which batch mode lacks; the
  managed tests cover it with a stand-in input method.

The probe puts the clipboard text it found back when it ends (after its last row), so the desktop
clipboard that other programs and probes read is left as it was; a non-text clipboard comes back
empty.

Other releases are unmeasured.
