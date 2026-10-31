# Combined 2D query and captured skin integration

This stage combines the previously rejected query candidate with the Native-backed
BoxCollider2D skin fix. A query-only correction recognizes an unrounded box by
its authored edgeRadius, even when its captured base skin exceeds the new global
query skin. Four regression cases retain the fine-angle Native midpoint values.

The original query branch had 53 introduced stress observations. The root skin
controls exposed another 14. Merely merging skin removed those failures but
introduced 23 different contact-selection observations. That intermediate output
is preserved. The final correction has zero introduced failures in every query
suite against both the actual frozen accepted runtime and the starting query
candidate. All tolerances and historical observations remain unchanged.

| Suite | Final matched / union | Retained failures |
| --- | ---: | ---: |
| Original broad physics | 11,581 / 11,626 | 45 |
| Main query controls | 52,059 / 55,251 | 3,192 |
| Support geometry | 66 / 86 | 20 |
| Selection | 1,439 / 1,440 | 1 |
| Fine selection | 1,722 / 1,728 | 6 |
| Owner boundary | 64 / 64 | 0 |
| Prior isolated controls | 48 / 48 | 0 |
| Box skin lifecycle | 644 / 644 | 0 |
| Initial-overlap threshold | 6,256 / 6,256 | 0 |
| New frame/center ties | 72,439 / 75,168 | 2,729 |

All nine query-control Native pairs agree byte for byte. Support geometry has
72 Native keys and 86 union keys because engine edge-shape grouping differs;
all extra keys remain failures. The main suite's 3,192 failures concern initial
contact points/normals and repeat across directions. The expanded motion suite
has 69 scenarios and 3,362,360 observations exactly unchanged from the prior
runtime. Only the three original query scenarios change. Their complete values
and the unchanged scenarios remain in the lossless motion capture.

The 76 original broad query failures resolve. Bounce9, stack27 and kinematic9
remain. The rejected motion experiments are retained in physics2dmotion and no
contact-phase or solver-order candidate was merged. Broader measured motion
differences remain in that fixture's full report; the table above does not imply
full 2D or full engine parity.

Fresh combined validation passes 494/494 managed tests and API-check with zero
errors across 2,266 handwritten types. Matrix5055 retains 4,803 exact and 252
original passing rounding values; all 21 files are byte-identical to SDK8.
Loading9312, contact lifecycle25356 and receiver1152 are exact. Timing18491,
order5720 and every focused active/inactive/clone/trigger/SDK control pass with
their unchanged comparator. Defaults, controller, rayhits, joints, Joint2D,
effectors and forces retain their accepted results. Graphics80, Canvas8671 and
messages316 pass; lifecycle is Native-byte-identical. ABI5, shim8 and shader
cache13 remain unchanged. All eight older frozen snapshots verify unchanged.

Generated projects, output and builds are private to
`/mnt/storage-hdd/node-b-caches-20261023/query2d-parent-combined-20261031/`.
The build uses matching frozen CLI/native8 copies and a fresh managed rebuild,
disabled build servers, one MSBuild worker and the exclusive heavy-job lock.
The complete report retains artifact hashes, all rejected query integration
outputs, passing roundoff and every missing/extra/failing key. The validation
driver's argument-name collision stopped it after the completed matrix; its
failed log is kept and only the remaining lifecycle/loading gates were resumed.

```bash
# Set QUERY_WORK, UNBOUND_MANAGED_DIR, SCENE_WORK, SCENE_PARENT_WORK,
# TRIGGER_WORK, UNBOUND_CACHE and matching library paths to fresh private outputs.
python3 tests/projects/query2dskinmerge/validate.py
python3 tests/projects/query2dskinmerge/check_gates.py
python3 tests/projects/query2dskinmerge/report.py
python3 tests/projects/query2dskinmerge/verify.py
```

The report writers refuse to replace their archived output directories. The
historical parent replay used separately completed 494 tests, then validate.py
--skip-tests and --resume-lifecycle after the retained driver failure. Its TRX
and both logs are archived. Other collider skin types, GetShapes with a changed
global offset, further hierarchy/scale transitions and older Unity releases
remain unmeasured. Public 3D ContactEvent/shim9 is a separate unaccepted branch.
