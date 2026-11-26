# Editor shortcut value types and attributes

What the Editor's shortcut value types and attributes answer, as packages' editor code builds and
reads them: `UnityEditor.ShortcutManagement.KeyCombination`, `ShortcutBinding`, `ShortcutAttribute`,
`ClutchShortcutAttribute`, `ReserveModifiersAttribute`, the internal `FormerlyPrefKeyAsAttribute`
and `Identifier`, `ShortcutArguments` and the event arguments. Unity 6000.0.71f1, recorded with the
macOS Editor in batch mode (two identical captures). Only values that depend on neither the OS nor
the graphics device are recorded: the Editor names keys and modifiers by OS (glyphs on macOS,
"Ctrl+Alt+Shift+" and key words elsewhere) and compares the action and control modifiers as one
outside macOS, so the probe leaves out text with modifiers or with the keys those name tables
hold, and equality of that pair.

`Assets/Scripts/ShortcutTypesProbe.cs` records in Play mode into values.jsonl in
`$SHORTCUTTYPES_OUT` (tests/value_probe.py); values compare exactly:

* `key.<key>.keyCode`, `.text`, `.menu.<m>`, `.hash.<m>`: a combination's key code, its text
  without modifiers, its menu hotkey string and hash with each of the 16 modifier sets, for
  letters, digits, punctuation, navigation and function keys (F16 and F24 too), keypad, mouse and
  modifier keys, keys the shortcut system does not support (joystick buttons, an undefined code,
  -1). `flags.*`, `default.*`: the modifier flags, the default combination.
* `equals.*`: equality and hashes of pairs without the action/control pair.
* `parse.<text>`: menu hotkey strings parsed back (internal `TryParseMenuItemBindingString`).
* `input.<modifiers>`: keyboard input without control and command.
* `binding.*`: the empty binding, a one-key binding, a three-key sequence (internal constructor),
  their text, hashes, equality and menu strings, a null sequence.
* `attribute.<method>.<i>.*`: the attributes each constructor leaves (identifier, display name,
  context, tag, priority, default binding, clutch context), read by reflection as packages with
  internal access read them; `usage.*` their attribute usage; the global context's priority.
* `arguments.*`, `changed*`, `profile`, `modifiers`, `stages`, `identifier`: the value types.

What it shows: a key the shortcut system does not support (above the last mouse button, except
F16 to F24) reads as None and has no menu string; the menu string is "&" Alt, "#" Shift, "%"
action, "^" Control, or "_" without modifiers, then the key's menu name or its character (keypad
and modifier keys have none); a one-key binding's text is its key's; a three-key sequence has no
menu string (the Editor throws); a priority left at its default is 1000000 for a global shortcut
(no context, or the global context) and 1000 in a context; the display name starts as the id.
The shortcut registry (which shortcuts exist, their bindings, profiles) is not recorded here.

Record and gate:

    python3 tests/projects/shortcuttypes/run.py record --label a   # twice, then
    python3 tests/projects/shortcuttypes/run.py publish a b
    python3 tests/projects/shortcuttypes/run.py gate
