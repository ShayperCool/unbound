# USS cursors on runtime panels

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode on a runtime panel (a UIDocument made
in Play mode drawing into an 800x600 target). The values are the panel's cursor state and log text;
they depend on neither the OS nor the graphics device. The gate compares every value exactly.

`Assets/Scripts/CursorsProbe.cs` moves the pointer (pointer move events at element centres) over
elements whose USS `cursor` is each keyword (`Resources/Cursors.uss`), a texture with and without a
hotspot, an inline texture cursor, a parent with a texture cursor and its child without one, and
empty space; with the mouse captured by one element and released; and onto an element whose cursor
class is added while the pointer rests on it. After each step it records whether the panel's cursor
manager has a texture cursor set (internal state, read by reflection) and the logs since the last step.

- Entering an element under the mouse takes its cursor: a texture becomes the cursor; a keyword other
  than `arrow` is reported ("Runtime cursors other than the default cursor need to be defined using a
  texture.") and, as `arrow` and no cursor, restores the default; a cursor is not inherited.
- While an element holds the mouse capture, entering others changes nothing; releasing the capture
  takes the cursor of the element under the mouse.
- When the rules of the element under the resting pointer change (a class added, a `:hover` rule),
  the style pass sets its cursor, also while another element holds the capture (`restyle.*`,
  `pseudo.*`, `captureRestyle.*`); an inline cursor set on it applies at once, its removal waits for
  the next entering (`inline.*`).
- `custom-cursor` and an unknown keyword are refused by the importer (`import.logs`: the probe imports
  the sheet again in Play mode, `AssetDatabase.ImportAsset` forced, which both runtimes log in the
  call: tests/projects/importlogs).

Other releases are unmeasured.
