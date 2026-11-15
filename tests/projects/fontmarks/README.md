# Font mark attachment queries

Unity 6000.0.71f1 FontEngine mark-to-base and mark-to-mark records, read by the same
reflection probe in the Editor and Unbound. It checks every record, order, array
capacity and null result. Floats are stored as their exact IEEE 754 bits.

Controls cover two licensed font subsets shared with `fontfeatures`, a controlled
attachment font, point sizes 13/37, individual bases, reversed and duplicate lists,
zero/empty lists, absent and wide glyph indices, valid lookup indices, mismatched
lookup types, individual pairs, unloaded faces and extension lookups. `Marks.ttf`
is an OFL derivative of the shared Sans subset; `make_font.py` rebuilds it with
fontTools. It has two mark classes, signed anchors, a missing base-class anchor,
and overlapping lookups with different anchors. License: `FONT-LICENSE.txt`.

    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontmarks/run.py native --label reference-a
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontmarks/run.py native --label reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontmarks/run.py publish reference-a reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontmarks/run.py check

The runner takes an immutable runtime and creates a private project per run.
The Editor must use the NVIDIA GPU; publication requires identical complete repeats.
The shared font files are copied from the committed `fontfeatures` fixture.

[FontEngine](https://docs.unity3d.com/6000.0/ScriptReference/TextCore.LowLevel.FontEngine.html)
accesses font data. The internal attachment query overloads have no public contract
for ordering or units. Measured here: array queries retain font units, single-pair
queries scale to the loaded point size, all-record order is lookup/subtable/mark/base,
and filtered lists visit requested bases within each subtable. Missing matrix anchors
retain two signed coordinates from the matrix header in this release, selected through
Compat; other releases skip absent anchors and remain unmeasured. Invalid lookup
indices are not part of these captures. This slice covers queries and record storage;
font asset table generation and text layout are separate controls.
