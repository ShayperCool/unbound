# Font single-adjustment queries

Unity 6000.0.71f1 `FontEngine.GetSingleAdjustmentRecords` for an indexed glyph
and indexed glyph list. The same reflection probe runs in the Editor and Unbound;
floats are compared as their exact bits, including all four placement/advance axes.

The controlled OFL font has per-glyph values, shared extension values, an explicitly
zero-valued record, and a pair lookup as a wrong-type control. Two real font subsets
are shared with `fontfeatures`. Controls include loaded sizes 13/37, reversed and
duplicate lists, zero/empty/null lists, full-width invalid indices and unloaded faces.
`make_font.py` rebuilds the neutral `UnboundLayoutSingles` derivative with fontTools;
`FONT-LICENSE.txt` carries its attribution and license.

    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontsingles/run.py native --label reference-a
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontsingles/run.py native --label reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontsingles/run.py publish reference-a reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontsingles/run.py check

The runner creates private projects and uses immutable engine snapshots. Native
publication requires two complete byte-identical captures on the NVIDIA GPU.
The internal methods have no public ordering/units contract in the
[FontEngine API documentation](https://docs.unity3d.com/6000.0/ScriptReference/TextCore.LowLevel.FontEngine.html).
Measured results retain font units and visit glyphs in input order within subtables;
zero terminates a list, duplicates repeat records, empty results are null, and nonempty
results reuse a power-of-two array with a zero sentinel. Invalid lookup indices are
unmeasured. Indexed pair queries and the older KERN transport are separate work.
