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
