# Trail autodestruction phase control

This bounded public API diagnostic moves two automatic trails for three Update
calls, then leaves them still. Their lifetimes are 0.05 and 0.11 seconds at a
50 fps capture rate. A passive trail distinguishes point expiry from destruction.
Three manual controls call `Object.Destroy` from Update, LateUpdate, and after
`TriggerEndOfFrameCallbacks` at step 6. PlayerLoop callbacks observe public
point counts and object existence before and after renderer and destruction
flush phases. `OnDisable`, `OnDestroy`, request and return events retain raw
frame, time, phase and probe step. No frame offsets are applied.

The corrected fixture gives all six trails separate lanes in an orthographic
camera, uses the line probe's explicit vertex-color material and renders to a
128 by 128 target. An explicit `Camera.Render` after `UpdateAllRenderers`
ensures batchmode rendering without a Game view. Snapshots surround that call;
per-lane nonblack pixel counts at step 2 prove each trail rendered. Fifty Update
steps provide one second of simulated time. A 20 ms fixture delay per step also
bounds real elapsed time below by one second, even if batchmode ignores the
requested frame rate. The output-progress deadline includes cold import.

Run from the repository root, with a fresh label in the Unbound cache:

```sh
python3 tests/projects/trailautodestruct/run.py native --release 6000.0.71f1 \
  --work ~/.cache/unbound/validation/runs/trail-autodestruct --label pair
python3 tests/projects/trailautodestruct/run.py engine --release 6000.0.71f1 \
  --work ~/.cache/unbound/validation/runs/trail-autodestruct --label candidate \
  --cli /path/to/unbound --runtime /path/to/managed
```

The Native path uses `tests/editor_runner.py`, two independent graphical
batchmode Editors without `-nographics`, one installed lease each, a 600 s hard
deadline and a 300 s output-progress deadline. Each engine command takes its own
installed lease and a 180 s deadline. Do not wrap either driver in another lease.
Projects, manifests, logs and raw events stay in the selected cache directory.

The first pair on 2026-11-01 used the prior camera-free nographics fixture.
It completed with byte-identical 859-record captures, but neither automatic
trail expired. Both retained three points through step 14. That pair remains
rejected, outside Git, and cannot publish a reference. Its manual controls
recorded immediate OnDisable on request and later OnDestroy. The prior engine
trace had 863 records and expired its trails. Those observations do not identify
Native automatic-destruction request timing.

The corrected capture has one separately authorized independent pair, with no
retry or sweep. The `publish` command requires every declared phase, rendered
lanes, populated trails, expiry with an empty-live observation, automatic
destruction, passive expiry and complete manual observations. It also requires
complete byte-identical independent repeats and strict successful GPU Editor
results. It preserves raw order and never overwrites a canonical reference.
Do not infer a global destruction rule from this diagnostic alone.

The corrective pair on 2026-11-01 passed with 3,557 byte-identical records per
repeat and 128 nonblack pixels in every lane. Its canonical reference is
`Reference/unity-6000.0.71f1.jsonl`. The short trail first becomes empty during
the explicit render at step 5, frame 6; the long trail at step 8, frame 9.
Both remain alive through that frame's final EOF observation. Their OnDisable
and OnDestroy callbacks carry the next frame's raw clock and precede its Update.
The callback phase string is the last snapshot label, not the internal callback
node. These observations bracket destruction but do not expose the automatic
request phase. A pre-ageing decision at the next UpdateAllRenderers would occur
after the Update where Native already reports the object dead. No production
timing change is justified by this pair alone.

The report and both complete repeats remain outside Git at
`/mnt/data/unbound-cache-20261031/unbound-validation/runs/trail-autodestruct-cause-20261101-t3-3141edbd/REPORT.md`.
