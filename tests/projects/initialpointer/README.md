# The Input System mouse at the start of Play mode

Unity 6000.0.71f1 with the Input System package (the input probe's packages and settings), in a
windowed Editor (a batch-mode Editor has no pointer): `Assets/Scripts/InitialPointerProbe.cs`
records, in each of the first five frames of Play mode, whether a mouse exists, whether its
position is still (0, 0), its delta, and whether the position is as in the frame before. Nothing
moves the system pointer; where it is depends on where the window opened, so its coordinates are
not recorded.

The host OS decides (AGENTS.md Platforms: input stays as measured on Linux unless measured on
macOS):

- `Reference/unity-6000.0.71f1/values.jsonl`, the Linux Editor (its own Xvfb display, the pointer
  where the X server put it): the mouse stays at (0, 0) in every frame, the pointer never having
  moved;
- `Reference/unity-6000.0.71f1/metal/values.jsonl`, the macOS Editor (the gate's reference on
  macOS arm64): (0, 0) in the first frame, then from the second frame on where the system cursor
  is, also outside the Game view, with a delta of 0, unchanged after.

Two independent, byte-identical captures each. The Game view has the focus in the captures; the
engine's window needs it too on macOS (launched from a background process it may open without:
the Input System's background behaviour then drops the pointer's events and the mouse stays at
(0, 0)). Other releases are unmeasured.

## Usage

```bash
python3 tests/projects/initialpointer/run.py record --label run-a   # Linux GPU Editor (or macOS)
python3 tests/projects/initialpointer/run.py publish run-a run-b
python3 tests/projects/initialpointer/run.py gate
```
