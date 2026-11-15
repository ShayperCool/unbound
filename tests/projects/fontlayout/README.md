# Font feature tables and generated text

Unity 6000.0.71f1 TextCore font asset feature imports and text generation, using
neutral derivatives shared with `fontfeatures` and `fontmarks`. Their generators
and font licenses live with those probes. The reflection probe runs unchanged in
the Editor and Unbound. It records exact float bits for tables, preferred sizes,
characters and generated mesh positions, UVs and colors.

Controls cover fresh all-feature import, an import after seeding one ligature
group, dynamic additions with features enabled and disabled, lookup rebuilding,
clearing and repopulating an asset, and stable mark-table sorting. Three fonts
exercise independent `liga`, `mark` and `mkmk` switches, multiple component counts,
missing anchors, chained marks, rich text boundaries and size changes, line breaks,
wrapping and a long mark chain. Text is generated at 17 and 18 points. Additional
controls force ligatures into later atlas pages, apply LowerCase to source `FI`,
and combine `kern` with `liga` and ligatures with marks.

    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontlayout/run.py native --label reference-a
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontlayout/run.py native --label reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontlayout/run.py publish reference-a reference-b
    UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/fontlayout/run.py check

Publication requires two complete byte-identical GPU Editor captures. The
canonical reference was measured on the NVIDIA RTX 4060. The runner creates
private projects and captures immutable engine runtimes. Every missing, extra or
different value is reported; existing text probe references remain unchanged.
This measures generated geometry, not a universal pixel-rendering guarantee.

## Contracts and measured rules

| Source, version | Guarantee or observation | Control |
| --- | --- | --- |
| [TextCore font assets, Unity 6.0](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-font-asset.html) | Dynamic assets add characters from the source font on demand. | Fresh assets add the measured character set, clear it and add it again. |
| [FontFeatureTable, Unity 6.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.Text.FontFeatureTable.html) | A font asset stores its font feature data in a table. | Full record contents after all-feature and dynamic imports, and lookup rebuilding. |
| [Font features, uGUI 2.0](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/TextMeshPro/TMPObjects.html) | Ligatures substitute multiple glyphs; mark features position marks relative to a base or another mark. The switches are independent. | Same strings with all five switch combinations; distinct anchors make both attachment mechanisms change geometry. |
| Editor observation, 6000.0.71f1 | Asset mark anchors are scaled once by sampling point size divided by units per em; first duplicate pair wins. | Full signed anchor records before and after lookup rebuilding and repopulation. |
| Editor observation, 6000.0.71f1 | A duplicate ligature stops the current import batch; matching uses the first applicable record and character metadata uses the final consumed source element. | Seed-A then import-all, multi-component ligatures and rich text boundaries. These quirks use Compat; other releases skip duplicate imports and retain the starting source span, unmeasured. |
| [Multi-atlas font assets, Unity 6.0](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-font-asset.html) | A full dynamic atlas can allocate additional atlas textures. The measured release still selects the nominal glyph's texture for a ligature whose replacement occupies a later page. | Two 64x64 assets put `f` on page 0 and its ligature on page 1 or 2; record replacement page, selected material texture and generated vertices. This records the release's texture-selection limitation. |
| Editor observation, 6000.0.71f1 | Ligature lookahead uses following source code points, and pair/mark lookups retain nominal glyph IDs. | LowerCase `FI` stays two characters; `kern` plus `liga`, ligature-plus-mark strings and an explicit Unicode ligature control. |
| Editor observation, 6000.0.71f1 | Preferred-size measurement applies mark tables regardless of the switches and retains the generated horizontal origin for mark-to-mark attachment. | Cold and warm preferred values for every switch combination and chained marks. This uses Compat; other releases honor switches and use measuring-pass origins, unmeasured. |

Indexed pair queries and the older KERN query transport remain separate work.

## Recorded rounding residuals

The strict `check` and `compare` modes retain 16 failing values, all listed with
both exact bit patterns in `known-residuals.json`. They affect the x coordinates
of the third character and its vertices in `DejaVu/mode/4/{11,17,23}` and
`Marks/mode/4/17`. The largest absolute delta is 9.5367431640625e-7.
The signs vary; tables, counts, source spans, advances, preferred values and all
other fields match. Mark attachment followed by scaled quad arithmetic is the
suspected cause. These are recorded geometry rounding residuals, not a pixel
comparison or a guarantee for other fonts.

The `gate` mode prints the same complete strict failure report, then admits only
the exact expected/actual pairs in that file. It rejects every missing, extra or
newly different value. A resolved residual passes. No numeric tolerance changes.
