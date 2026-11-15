# Font pair queries

Unity 6000.0.71f1, measured on the NVIDIA RTX 4060. The reference records legacy KERN
queries followed by GPOS pair queries, so buffer reuse is tested across both API families.
The fixtures are renamed derivatives of the licensed layout font in `fontfeatures`.
`make_fonts.py` rebuilds them with fontTools.

The public [FontEngine API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.LowLevel.FontEngine.html)
does not document these internal pair-query overloads. The rules below are measured behavior,
not additional Unity guarantees. The [OpenType GPOS specification](https://learn.microsoft.com/en-us/typography/opentype/spec/gpos)
defines the pair-set, class-matrix, value-record and extension encodings.

- `PairOnly`, `KernOnly`, `Both` and `Neither` distinguish independent tables from fallback.
  `Both` deliberately gives the same pair different KERN and GPOS adjustments.
- GPOS arrays keep both glyphs and all eight value fields in font units. Pair sets retain
  explicit zero records. Class matrices omit all-zero cells. A second-side array definition
  omits zero entries; a range definition keeps an explicit zero-class range. Implicit class-zero
  members are omitted. All-record queries visit each second class, then each covered first glyph,
  then its second-glyph members. `ClassZero` and `ClassRange` exercise both encodings.
- Filtered GPOS arrays preserve lookup/subtable/input order and duplicate inputs. Zero terminates
  a list; a wider glyph ID does not alias its low sixteen bits. An all-record or unindexed
  scalar-glyph query returns an exact-sized array. List and indexed queries use the shared
  power-of-two buffer, with an empty terminator and null for no results.
- Legacy queries use KERN independently of GPOS. Values are unfitted 26.6 pixel coordinates
  converted to floats, as described by [FreeType](https://freetype.org/freetype2/docs/reference/ft2-glyph_retrieval.html#ft_kerning_mode).
  A scalar-glyph query includes incoming and outgoing pairs. A list/table query selects both
  endpoints and preserves duplicate combinations. New/all queries visit each new glyph against
  the all-glyph list, outgoing before incoming, and retain each pair once. `Reverse` adds a
  reverse pair and a self pair. Sizes 13 and 37, unloaded faces, zero/wide IDs, null inputs and
  buffer history are controlled.
- A GPOS scalar pair with only a first-glyph x advance scales that advance by the face size.
  Full multi-field scalar pair results vary across Editor repeats, including glyph IDs.
  Those cases remain unresolved; Unbound rejects that scalar context with a clear message.
  Record-array queries support the full fields.

The GPOS array capture writes glyph IDs and all eight value fields as float bits. Its
`featureLookupFlags` vary across Editor repeats, so those flag values are not asserted here;
Unbound initializes them to zero. Legacy records include their flags, which are zero in both
repeats. Existing `textcore` references and their raw flag discrepancies remain unchanged.
The exploratory raw captures are retained outside Git. No numeric tolerance is used by this
probe, and missing, extra or differing stable values fail the comparator.

The implementation is selected by `Compat.FontPairQueriesMeasured`. Other releases retain the
previous unmeasured pair transport; their indexed overloads continue to report unsupported use.
Only format-0 legacy KERN subtables are supported by the selected kerning path. Persistent
importer/cache output does not change; the parsed tables are per-face memory caches.

```bash
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontpairs/run.py native --label first
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontpairs/run.py native --label second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontpairs/run.py publish first second
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontpairs/run.py check
```
