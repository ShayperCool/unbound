# Contact creation and traversal controls

These controls measure Unity 6000.0.71f1 Editor contact ordering. Frozen dynamic
H touches static L/R across separate simulation barriers in both orders, or
together with reversed movement order. Other cases recreate a noninteracting
proxy or remove and recreate a middle contact while retaining its bodies.

`Reference/unity-6000.0.71f1/` is the canonical release set. Two independent
recordings agreed exactly before each profile was published. Public arrays
remain indexed. Diagnostic identity renaming is bijective, preserves null,
and retains numeric IDs, reuse and record order. Comparisons never sort by
names, coordinates or solver output.

```sh
WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/pair-order-local"
python3 tests/projects/solverpairorder/run.py prepare --release 6000.0.71f1 --work "$WORK" --label native1
python3 tests/projects/solverpairorder/run.py prepare --release 6000.0.71f1 --work "$WORK" --label native2
python3 tests/projects/solverpairorder/run.py native --release 6000.0.71f1 --work "$WORK" --label native1
python3 tests/projects/solverpairorder/run.py native --release 6000.0.71f1 --work "$WORK" --label native2
python3 tests/projects/solverpairorder/public.py --release 6000.0.71f1 --exact --reference "$WORK/traces/native1.json" --candidate "$WORK/traces/native2.json" --output "$WORK/agreement.json"
```

The runner uses the shared job pool for each Editor or engine process. Do not
wrap the driver again. Prepared projects, recordings and reports stay in cache.
Engine mode takes an explicit private `--runtime` and prepared `--project`.
`public.py --baseline` reports the complete union of resolved, retained and
introduced failures with the original 1e-3 tolerance. `--exact` compares
independent recordings without numeric tolerance.

The default reference has 3,228 public fields, 218 pair/edge events and 48
complete islands. `verify_proxies.py` checks every live proxy and complete star
graph at all 48 barriers. `verify_order.py` checks oriented constraints and all
island bodies, retaining extra records. Input poses bind logical identities;
they do not order bodies or constraints. The existing body lifetime controls
separately cover 102 islands.

`--enumeration` covers body/collider and static/component getters, point and
collider results, array capacities 0/1/2/3/5/16, prefilled lists, layer/depth/normal
filters and trigger filtering. Its 95,074 reference fields distinguish
first/middle/last removal, recreation, delayed touching, compound views and
duplicate opposing colliders. An unrelated-body case distinguishes a shared
scene sequence from separate body lists. Removal can replace a slot with an
unrelated pair that the getter then filters out.

`--velocities` covers moving and rotating contacts, a four-body stack, mixed
ordinary/continuous contacts and live continuity-mode changes. Its 18,129
public fields distinguish cached points, separations and velocities from
updated normals and solved impulses, including getters after velocity edits
without another simulation. The reference also contains 200 contact updates
and 85 complete shared pair sequences.

`--lifetimes` creates and ends touching pairs in one simulation barrier,
including a trigger exit and simultaneous additions and losses. Every
notification reads public contacts. The 3,499 reference fields distinguish
appending new pairs before retiring an ended pair, replacing its slot with the
tail, and visiting the replacement after the exit notification. Getters inside
the exit already observe the compacted sequence.

`--motion-witness` preserves the existing motion probe's full prefix and first
bounce/stack cases. Its independent recordings agree on 144,026 public fields,
1,621 islands and 4,976 contact updates. `--motion-through solver/0.04/2/7`
preserves the longer prefix through that declared case and teardown. Its
recordings agree on 2,093,365 public fields, 4,227 islands and 23,011 updates.
Both use the existing motion oracle. The canonical release set retains the
2,400 measured pair sequences for the longer prefix and 304 ordered polygon
contact inputs. `verify_witness_order.py` includes empty islands after sleep.

`--rotation-inputs` covers 52 transform and setter cases, including tilted
transforms and rotations crossing 180 degrees. Its 988 exact fields distinguish
the signed Z/W quaternion twist from the rotated right-axis projection.

`--unattached-statics` selects `Sources/UnattachedStatics/ProbeEntry.cs`. The
793 public fields and corresponding pair, update and island records distinguish
unattached colliders sharing one static scene body at `(0, 0)` from explicit
static rigidbodies. Moving, rotating, resizing, disabling or recreating one
collider preserves the other collider and exercises proxy lifetime.

`--contact-inputs` preserves all 75,168 indexed fields of the existing query tie
controls, reusing that oracle. The selected source markers cover translated
capsules and boxes in static and rigidbody frames. Independent measurements
agree on 162 complete contact inputs. Capsule/circle contacts use a two-sided
edge. Polygon/capsule contacts put the polygon first; equal shape kinds retain
the query-first convention. Markers change no simulation barrier or query input.

Add the selected profile flag to both prepare and capture commands. Compare
its recordings with `public.py` using the same flag. For engine diagnostics,
`--managed-trace` records the runtime's actual proxy IDs, linked neighbour order,
body rotations and shared pair sequence through read-only managed reflection.

`--capsule-geometry` selects `Sources/CapsuleGeometry/ProbeEntry.cs`. Two
independent Unity 6000.0.71f1 Editor recordings agree exactly on 276 fields
across 36 controls. These measure transformed tips, translated rotations,
nearly circular segments and the minimum accepted radius in both directions.
The canonical `capsule-geometry.json` retains zero-length and reversed core
segments as observed, with every indexed vertex and shape error state.

`--transform-inputs` selects `Sources/TransformInputs/ProbeEntry.cs`. Its single
canonical profile contains 135,896 fields from 1,548 snapshots after two exact
independent Editor recordings. All 144 original contact-frame records retain
their measured public inputs. A further 160 parenting cases record the parent,
original child, attachment, repeated cached reads, parent-scale edits and
detachment. They cover both `worldPositionStays` values, nested transforms,
uniform, nonuniform and negative parent scales, arbitrary planar angles and
tilted rotations. Further controls cover raw quaternion setters, collapsed and tiny
scales, and public point transforms in both directions. Circle and box frames
also retain every local shape vertex. Circle controls include tilted, negative
and nested scales, direct planar and tilted rotation edits, scale edits and
detachment after allocation. `public.py --transform-inputs --exact --baseline <starting>`
retains every resolved, retained and introduced indexed field. The existing
query tie oracle separately checks all 75,168 query fields.

Unattached static controls also record movement across synchronization and the
following step, including a second sleeping body on the shared scene body. The
canonical profile retains touching flags, point and collider views, and both
bodies' sleep state for solid and trigger pairs. Two independent Native
recordings agree on all 793 fields, including the original 417.

`--manifold-controls` records 216 near-parallel controls with complete physical
contact arrays and cast results. Four arbitrary angles, small positive and
negative angular differences, three offsets and three centers retain every
indexed point, normal and separation. Body basis vectors and side rays separate
pose arithmetic from clipping. Two Native recordings agree on all 28,512 fields.

`--review-controls` records capsule tips whose core intervals do not overlap,
collapsed cores against rotated boxes in both fixture orders, coincident and
exactly touching core boundaries, and callback-driven removal of an earlier
scene pair. Solid and trigger cases retain enter/stay order with both values of
`callbacksOnDisable`, including the following simulation step. Two independent
Editor recordings agree byte-for-byte on all 3,670 fields in 66 cases. The
first 2,752 measured fields remain unchanged in the expanded reference.

`public.py` reports newly missing observations separately from retained numeric
failures. A baseline failure cannot make the loss of its field acceptable. Run
the CLI regression controls with `python3 -m unittest discover -s
tests/projects/solverpairorder -p test_public.py`.
