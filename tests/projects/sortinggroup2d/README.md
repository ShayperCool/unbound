# Sorting groups across the 2D renderer's layer batches

The 2D renderer (URP 17, the `urp2d` probe's pipeline and settings, its renderer data sorting along
the custom axis Y) draws one list per batch of sorting layers that share their 2D lights.
`Assets/Editor/SortingGroup2DProbe.cs` records in Play mode which of two overlapping unit squares
is drawn on top: A (red) inside a SortingGroup whose sorting layer differs from A's own, B
(green) outside it, on the sorting layers `Default` and `Objects`, with one global 2D light per
layer (separate batches) or one for both; the group in front of or behind B along Y; nested
groups (outer and inner on different layers); and controls without a group. Each case is left
two frames, which render its camera into a render texture; the colour at the overlap gives
`A` or `B`. Values compare exactly.

```sh
python3 tests/projects/sortinggroup2d/run.py record --label A     # twice, A and B
python3 tests/projects/sortinggroup2d/run.py publish A B
python3 tests/projects/sortinggroup2d/run.py gate
```
