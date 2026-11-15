# OpenType font feature queries

Unity 6000.0.71f1 FontEngine ligature records, including all query overloads,
measured by reflection because these APIs and their record type are internal. The same
script runs in Unbound. The controls cover all records, the first component alone,
reversed and duplicate input lists, a zero terminator, empty lists, absent glyphs,
32-bit glyph indices, lookup indices -2 through 13, unloaded faces, two point sizes,
and record equality. Capacity and null results are compared along with every record.

The two real fonts are renamed subsets of Liberation Sans and DejaVu Sans. Their
licenses are alongside this file. `make_fonts.py` uses fontTools to regenerate them
from the stated sources. The controlled derivative adds three GSUB lookups: a single
substitution control, normal ligatures, and extension ligatures with a different
replacement for the same inputs. Gates use the committed fonts directly.

    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontfeatures/run.py native --label reference-a
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontfeatures/run.py native --label reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontfeatures/run.py publish reference-a reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontfeatures/run.py check

The runner gives each capture its own project and output directory. It records the
Editor on the NVIDIA GPU and refuses to publish unequal or incomplete repeats.
Engine replays use `UNBOUND_CLI` or `UNBOUND` from the suite's immutable runtime.

Documented context: [FontEngine](https://docs.unity3d.com/6000.0/ScriptReference/TextCore.LowLevel.FontEngine.html)
reads font files, glyphs and metrics. The internal query overloads have no public
Scripting API contract for ordering or lookup filtering. These controls record the
selected release's behavior: unfiltered results follow lookup, subtable and coverage
order; filtered results follow lookup, subtable and input-glyph order. Point size
does not change the records. -1 selects all lookups; the other tested explicit indices
return null in this release. Other releases use indexed table queries and are unmeasured.

This slice does not apply substitutions during text layout or populate TextCore font
asset feature tables. Mark attachments and single-adjustment queries are separate work.
