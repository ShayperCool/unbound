# Serialized 3D body loading

This fixture measures Unity 6000.0.71f1 on Linux. Its 4,656 JSONL records cover
192 bodies with scene objects, nested prefab overrides, Resources prefab assets
and runtime instances. Cases vary activity, body type, hierarchy and six
first-read sequences. Reads retain setters, Transform edits, SyncTransforms,
Move/Publish, activation, simulation and deactivation. The comparison checks
the union of all keys exactly, with no rounding tolerance or exclusions.

`Sources/LoadingEditor.cs` authors the scene and prefabs through Unity APIs.
`Sources/LoadingProbe.cs` is the recorder. The checked-in Assets contain those
sources and authored inputs. `run.py` creates and prepares private copies. It
reorders complete YAML documents while preserving component lists and references,
so Transform-first and Rigidbody-first exercise distinct loader orders.

Both orders and both independent Native recordings produce the same bytes.
One canonical copy lives at `Reference/unity-6000.0.71f1/loading.jsonl`, and both
order controls compare with that file. `UNBOUND_PROBE_UNITY_VERSION` selects the
release directory and matching Editor. `UNBOUND_PROBE_UNITY` selects an explicit
Editor path when needed. No missing release falls back to an old
oracle. Repeats, engine captures, logs, manifests and reports stay in the run
cache or the private `LOADING_WORK`, never in Git.

These checks need no private binaries, Unity installation or historical archive:

```bash
python3 tests/projects/inactive3dloading/verify.py
python3 tests/projects/inactive3dloading/verify.py --captures /path/to/cached/captures --label final
```

The frozen loading baseline passes 4,558/4,656 Transform-first records and
4,574/4,656 Rigidbody-first records. The snapshot-only candidate passes
4,614/4,656 in each order and retains 42 kinematic-deactivation failures.
The final loading capture passes all 4,656 records exactly in both orders.
`--label baseline` or `--label candidate1` reports the earlier failures and exits
nonzero. The canonical oracle does not encode engine acceptance status.

To record a release, use a fresh private directory:

```bash
export LOADING_WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/inactive3dloading/loading"
fixture=tests/projects/inactive3dloading
python3 "$fixture/run.py" create
for order in transform-first rigidbody-first; do
  for label in native1 native2; do
    python3 "$fixture/run.py" prepare --order "$order" --label "$label"
    python3 "$fixture/run.py" native --order "$order" --label "$label"
  done
done
python3 "$fixture/verify.py" --record-reference "$LOADING_WORK"
```

Set `UNBOUND_PROBE_UNITY_VERSION` before preparing a new release. Recording wraps
Unity and engine execution in `~/.cache/compat/with-heavy-job`. Native run state
stays in the private projects, including deliberate runtime prefab edits.

`report.py --label <stage>` compares baseline and selected candidate against
canonical Native and writes full differences under the run cache.
`validate-stage.sh` and `validate.py` replay existing focused, matrix, graphics,
canvas, messages and physics controls. `verify.py --full` invokes the preserved
loading-stage build/artifact gate; `parent_verify.py` preserves the subsequent
parent gate. Those historical audits require explicitly selected private
artifacts and cached engine baselines. `archive.py` copies their logs and writes
hash manifests into the run cache. They are optional for oracle verification and
standalone comparison.

Inactive scene bodies and prefab assets retain completed serialized world pose
through later Transform edits and Sync. Active scene bodies retain physics pose
until Sync. Activation uses current Transform. Nested overrides and parent fields
must finish before the inactive snapshot. Prefab assets never join scene physics.
Kinematic deactivation clears motion from the previous simulation; dynamic
deactivation retains its actual motion. Older releases, non-unit hierarchy scale
and full engine parity remain unmeasured by this fixture.
