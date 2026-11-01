# Trail automatic destruction phases

The public control is [trailautodestruct](../trailautodestruct/README.md). Its
[Unity 6000.0.71f1 reference](../trailautodestruct/Reference/unity-6000.0.71f1.jsonl)
contains 3,557 records across 50 steps and six lanes. It observes movement, point
counts, explicit camera renders, automatic expiry and manual destruction at
Update, LateUpdate and end of frame.

## Measured observations

- A trail moved before its initial position was observed records the new position
  alone. The separate line control observes the initial position before moving,
  and retains that initial position. Clear also establishes the next anchor.
- Camera collection can empty an expired stationary trail. Its GameObject remains
  alive through the end-of-frame observation and is destroyed later.
- Manual Destroy has separate callback-phase residuals. The automatic path does
  not establish a change to global destruction timing.

## Implementation and evidence limits

The trail renderer requests automatic destruction during actual render
collection, before returning for an empty strip. The trail-owned queue keeps the
target alive and enabled until a snapshot is drained. It does not re-evaluate
expiry after a request. BakeMesh and bounds queries do not request destruction.

The queue drains before existing callbacks at FixedUpdate.ScriptRunDelayedFixedFrameRate,
Update.ScriptRunDelayedDynamicFrameRate and PostLateUpdate.ScriptRunDelayedDynamicFrameRate.
These consuming nodes follow the saved phase analysis; they were not independently
identified by a dynamic Native node trace. End-of-frame nodes do not drain it.

The matching validation compiled source `6ef53d16`. It closed the introduced
first-point records and exercised all six lanes through expiry and destruction.
Eighteen manual-Destroy timing rows remain failing. Render-only first observation,
an initial move shorter than minVertexDistance, re-enabling an empty trail and
AddPositions after an unobserved move remain unmeasured.

Detailed phase research, both Native captures and complete engine comparisons
remain in the validation cache. Root's integration receipt is under
`validation/runs/line-root-integration-20261101b/`; the matching runtime receipt is
under `validation/runs/trail-recording-runtime-20261101-t3-fcf65aff/`.
