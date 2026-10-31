# Contact creation and traversal controls

The only dynamic seed is frozen H. Static L/R contacts are created across separate
simulation barriers in both orders, or together with reversed movement order.
Other controls delete/recreate a noninteracting dummy proxy. The final control
removes the middle H-R contact with IgnoreCollision, then recreates it without
changing bodies or proxies. A third static neighbour M distinguishes middle
removal from head removal.

`Reference/unity-6000.0.71f1/` is one canonical Linux Editor reference set.
`public.json` retains every public observation. `pairs.jsonl` retains numeric
proxy IDs, creation/destruction, AddPair calls, contact head/prev/next and island
input order. `islands.jsonl` retains solver input bodies and constraints. Pointer
identities in the diagnostic files are renamed bijectively in first-observation
order. Null remains null. Numeric IDs, reuse and record/list order are unchanged.
Two independent Editor processes agreed exactly on all 3,228 public values,
218 pair/edge events and 48 island records before this set was published.

The hooks are specific to the hash-checked Linux 6000.0.71f1 binary. BroadPhase
CreateProxy returns before its caller initializes FixtureProxy fields, so the
create event records the returned numeric ID and proxy identity. Live proxy
records are resolved at island entry. This avoids reading uninitialized data.
No comparison sorts by names, coordinates or solver output.

```bash
RUN="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/pair-order-local"
python3 tests/projects/solverpairorder/run.py prepare --release 6000.0.71f1 --work "$RUN" --label native1
python3 tests/projects/solverpairorder/run.py prepare --release 6000.0.71f1 --work "$RUN" --label native2
python3 tests/projects/solverpairorder/run.py native --release 6000.0.71f1 --work "$RUN" --label native1
python3 tests/projects/solverpairorder/run.py native --release 6000.0.71f1 --work "$RUN" --label native2
python3 tests/projects/solverpairorder/verify.py --release 6000.0.71f1 --reference "$RUN/traces/native1-pairs.jsonl" --candidate "$RUN/traces/native2-pairs.jsonl" --output "$RUN/pair-agreement.json"
python3 tests/projects/physics2ddynamics/trace.py compare "$RUN/traces/native1.jsonl" "$RUN/traces/native2.jsonl" --output "$RUN/island-agreement.json"
python3 tests/projects/solverpairorder/public.py --release 6000.0.71f1 --exact --reference "$RUN/traces/native1.json" --candidate "$RUN/traces/native2.json" --output "$RUN/public-agreement.json"
```

The Native driver wraps the Editor with the shared heavy-job runner. Do not wrap
the driver again. Projects and all outputs stay in cache. The engine mode accepts
an explicit private `--runtime` and prepared `--project`; `public.py --baseline`
reports every resolved, retained and introduced failure with the original 1e-3
tolerance and complete missing/extra-key union. Existing public differences are
not an accepted implementation of Native ordering.

`--enumeration` selects separate public getter controls. Their canonical
`enumeration.json.gz` contains 95,074 values after exact independent recordings.
The cases distinguish first/middle/last removal, recreation, delayed touching,
unrelated bodies, compound collider views and duplicate opposing colliders.
They cover body/collider and static/component APIs, point/collider results,
array capacities 0/1/2/3/5/16, prefilled lists, layer/depth/normal filters and
trigger filtering. Arrays remain indexed throughout comparison.

The unrelated-body control distinguishes a shared scene sequence from separate
per-body lists: removal can fill a slot with an unrelated pair, which is then
filtered out by a body/collider getter. Contact pairs append when they begin
touching, independently of when their broadphase contacts were allocated.

Prepare two new labels and run Native mode with `--enumeration`, then compare
the public recordings with `public.py --exact --reference <first> --candidate
<second>`. For engine comparisons use `public.py --enumeration --baseline
<baseline> --candidate <candidate>`. Both modes preserve the complete union.

Engine mode's optional `--managed-trace` records actual numeric proxy IDs and
linked neighbour order through read-only reflection. `verify_proxies.py` checks
every live proxy and the complete star graph at all 48 simulation barriers.
`verify_order.py` checks every island body's type and logical identity, every
constraint's oriented body pair and point count, and all extra records. Explicit
input poses bind identities; they do not order bodies or constraints. The
existing lifetime probe's `verify_order.py` separately checks its 102 islands.

`--velocities` covers a moving, rotating head-on contact, a four-body stack,
mixed ordinary/continuous contacts and live continuity-mode changes. The
canonical `velocities.json` has 18,129 indexed public values, independently
recorded twice with exact agreement. `contact-updates.jsonl` has 200 exact
pointer-normalized records of Native's actual update arrays and visits.
These controls distinguish cached pre-solve points, separations and point
velocities from updated normals and solved impulses, including getters called
after editing body velocities without another simulation step. They also
distinguish ordinary/sensor storage from the continuous-contact array and
record contact removal/recreation without changing the oracle to fit an engine.

Prepare mode's `--motion-witness` derives a private project from the existing
motion probe, preserving its complete broad prefix and first bounce/stack
cases. It uses the existing motion oracle rather than publishing a duplicate.
Two Native recordings agreed on all 144,026 prefix values, 1,621 island inputs
and 4,976 update records; the public prefix also agreed exactly with the existing
motion oracle. `verify_witness_order.py` checks every one of the final 300 stack
steps, including matching empty island lists after sleep. It derives the declared
case boundary from the full Native update trace, retains every record in that
interval and rejects extra records after the case. Native's one default static
ground body and managed's static ground body are bound by their unique source
role; their different body origins do not sort or relabel dynamic bodies.

The optional Native `trace.py capture --queries --query-case mixed-continuous`
hook records the actual scene pair vector read by body getters. Set
`SOLVER_PAIR_VELOCITIES=1` and `DYNAMICS_TRACE_STORAGE=1` when using it with a
prepared velocity project. `pair-vector.jsonl` contains all 85 records from the
declared mixed case after two exact, bijective pointer-normalized recordings.
The entire preceding scene history still runs. Source barriers temporarily set
velocity iterations to 63/62 to delimit recording; no simulation runs at those
values, and all 18,129 public observations remain exactly identical to the
unmarked canonical capture. This avoids treating nondeterministic C++ component
address reuse between destroyed test cases as a stable cross-case identity.
The complete unmarked captures and their failed agreement report stay in cache.

`--lifetimes` creates and ends touching pairs in the same simulation barrier,
including a trigger exit, two new pairs and two losses. Its `lifetimes.json`
contains 3,499 indexed values after two exact independent Native recordings.
Every notification reads the public contact list. New pairs append before an
ended pair releases its slot for the tail replacement. The exit notification
precedes visiting that replacement; getters inside the exit already see the
compacted touching sequence. This distinguishes removal at EndContact from
removal during notification without changing bodies or proxies.

`--motion-through solver/0.04/2/7` preserves the existing motion probe's full
prefix through that declared case, including teardown, and suppresses later
cases. It reads the existing motion oracle. Two independent Native recordings
agreed exactly on all 2,093,365 public prefix values, 4,227 island inputs and
23,011 contact-update records. All public values also match the existing oracle
exactly. Selecting `SOLVER_PAIR_QUERY_CASE=solver/0.04/2/0` records the actual
pair vector for that case while retaining its full prefix. The canonical
`motion-pair-vector.jsonl` retains all 2,400 records after exact independent
agreement with bijective pointer renaming only. Optional managed tracing reads
the scene pair sequence at each public snapshot without changing the engine.
