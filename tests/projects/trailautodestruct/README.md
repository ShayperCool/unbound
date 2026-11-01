# Trail autodestruction phase control

This bounded public-API diagnostic moves two automatic trails for three Update
calls, then leaves them still. Their lifetimes are 0.05 and 0.11 seconds at a
50 fps capture rate. A passive trail distinguishes point expiry from destruction.
Three manual controls call `Object.Destroy` from Update, LateUpdate, and after
`TriggerEndOfFrameCallbacks` at step 6. PlayerLoop callbacks observe public
point counts and object existence before and after renderer and destruction
phases. `OnDisable`, `OnDestroy`, request and return events retain raw frame,
time, phase and probe step. No frame offsets are applied.

Run from the repository root, with a fresh label in the Unbound cache:

```sh
python3 tests/projects/trailautodestruct/run.py native --release 6000.0.71f1 \
  --work ~/.cache/unbound/validation/runs/trail-autodestruct --label pair
python3 tests/projects/trailautodestruct/run.py engine --release 6000.0.71f1 \
  --work ~/.cache/unbound/validation/runs/trail-autodestruct --label candidate \
  --cli /path/to/unbound --runtime /path/to/managed
```

The Native path uses `tests/editor_runner.py`, two independent batchmode
`-nographics` Editors, one installed lease each, a 600 s hard deadline and a
300 s output-progress deadline. Each engine command takes its own installed
lease and a 180 s deadline. Do not wrap either driver in another lease.
Projects, manifests, logs and raw events stay in the selected cache directory.

The first pair on 2026-11-01 completed with byte-identical 859-record captures,
but neither automatic trail expired. Both retained three points through step
14. This camera-free, nographics setup therefore fails the causal exercise
guard and cannot publish a reference for automatic destruction. It does record
manual controls, including immediate OnDisable on request and later OnDestroy.
The matching engine diagnostic has 863 records and expires its trails. These
observations do not identify Native automatic-destruction request timing.

No canonical reference has been published. A corrective graphics setup with a
camera and material needs a separately authorized Native pair. Until then,
do not use this diagnostic to justify an engine scheduling change. The `publish`
command refuses captures that fail to exercise expiry and automatic destruction,
and otherwise requires complete, identical independent repeats and successful
Editor results. It never overwrites a canonical reference.
