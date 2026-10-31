# Continuation controls

`LifecycleOrder.cs` preserves ContactOrder's complete workload and optionally
tears down components before DestroyImmediate(GameObject). `KD_CLEANUP` selects
`original`, `body-first`, `collider-first` or `disable-first`. `CreationOrder.cs`
changes runtime box construction to add Rigidbody before Collider.

`continuation.py` prepares private copies under `KD_WORK/controls/`. Default
work lives in the per-probe run cache. Canonical Native is
`../Reference/unity-<release>/continuation/<control>.jsonl`.
`UNBOUND_PROBE_UNITY_VERSION` selects the release and Editor together.

Each control's two independent Native runs agree on all keys and values; row
order can vary. Original, collider-first and disable-first have 276 records.
Body-first has 277 and changes later contact manifolds. Creation-original has
278 and isolates Collider-before-Rigidbody's transient static actor. These
remain distinct source/configuration controls. The comparator preserves raw
point order and every numerical difference.

```bash
fixture=tests/projects/physicsscenesettings
python3 "$fixture/continuation.py" prepare --cleanup original
python3 "$fixture/continuation.py" native --cleanup original --repeat 1
python3 "$fixture/continuation.py" native --cleanup original --repeat 2
python3 "$fixture/continuation.py" engine --cleanup original --label final
```

Repeat for each cleanup selection. Use `--creation` for the creation control.
Install the complete agreeing Native set with `verify.py --record-continuation`;
compare selected engine output with `--continuation-output` and `--label`.
The runner wraps heavy jobs in `~/.cache/compat/with-heavy-job`.

`trace_native.py` compiles a private diagnostic shim using explicit matching
PhysX/NvCloth archives. Run it through the heavy-job wrapper and choose a cache
output directory. It logs real rigid IDs, element IDs, kinematic flags and callback
actor order. `native_ids.gdb` and `native_flags.gdb` inspect the exact Linux
Editor using matching local debug symbols. Their addresses and trace logs are
diagnostics, not extra oracle copies. No binary or diagnostic run output belongs
in Git, and these tools are not required for ordinary oracle comparison.
