# Advanced 2D physics

Unity 6000.0.71f1 Play-mode controls for edge adjacency and edge radius, explicit
simulation in each simulation mode, sub-stepping with and without contact updates,
simulation layer masks and pending body moves/forces, and every SlideMovement option.
The capture records body poses, velocities, contact and callback counts after each
step, and every SlideResults hit field. The API control also records constructor
defaults and the serialized movement structure. It checks whether a body-level
simulationLayers property exists rather than assuming one exists.

`Reference/unity-6000.0.71f1/main.json` and `controls.json` each come from two
independent, byte-identical captures using the supervised GPU Editor runner. Repeats and reports stay in the
Unbound cache. The references have 257 scenarios and 24713 values. The comparator preserves every
missing, extra and failing value using the physics probe's relative tolerance of
0.001. The integer callback counts in these controls, booleans and strings compare exactly.

Run `run.py prepare`, then `run.py native` with `--release 6000.0.71f1`, a cache
directory in `--work`, and independent `--label` values to record new controls.
After recording independent Native labels, use `run.py publish --repeat-label <second>`
with the first label to verify and publish them. It checks successful GPU runs,
completion and byte-identical outputs and refuses to replace a reference.
Use `run.py engine --runtime <directory-containing-unbound> --project <label>`
to replay a prepared project. Add `--controls` to prepare, record, replay and
compare the supplementary controls. `run.py compare` requires `--candidate` and
`--output`; the full failure report goes to the latter cache file.

The engine-only gate is `python3 tests/projects/physics2dadvanced/gate.py`.
It selects the release through `UNBOUND_PROBE_UNITY_VERSION` and the built CLI
through `UNBOUND`. It never invokes the Editor.

## Contracts and observations

The release-specific API pages used for this probe are:

- [EdgeCollider2D](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EdgeCollider2D.html): adjacent points determine endpoint collision normals; connected points define the edges.
- [Physics2D.Simulate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics2D.Simulate.html): the mask selects bodies, collider contacts, joints and effectors. Calls during physics callbacks fail; explicit simulation does not invoke FixedUpdate.
- [Physics2D.useSubStepping](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics2D-useSubStepping.html): applies in Update and Script modes. minSubStepFPS determines the minimum simulation frequency; maxSubStepCount caps the work and excess time is lost.
- [Physics2D.useSubStepContacts](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics2D-useSubStepContacts.html): controls whether contacts update on every sub-step or only the first.
- [Rigidbody2D.Slide](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody2D.Slide.html): movement, surface anchoring and gravity precede immediate or deferred movement. A selected collider overrides attached-trigger selection. Any body type is valid; Dynamic bodies can also move through their ordinary forces and collision response.

The references establish explicit Simulate rejection in FixedUpdate and Update
modes and the absence of Rigidbody2D.simulationLayers in this release. Automatic
mode controls sample Update, FixedUpdate and LateUpdate calls, poses, callback
counts and sub-step metadata over six frames for every mode/contact-refresh
combination. Callback counts are per outer simulation call; these captures do
not promise a universal ordering between different colliders' callbacks.

Flat and bent seam controls cover box and circle bodies, connected colliders
with and without adjacent points, duplicate points and two radii. Supplementary
controls check exact sub-step boundaries, lost time, selected joints and both
connected body layers, held bodies with child colliders on included layers,
volume forces, pending moves, damping, and every serialized SlideResults field.
Joints are first simulated with stationary bodies before querying their reaction
state on omitted layers. An earlier uncaptured setup queried a joint before its
first participating solve; independent runs returned zero or NaN. Those raw
captures remain outside Git and are not used as an oracle.

## Causes and limits

| Mechanism | Reference observation | Implementation |
| --- | --- | --- |
| Rounded edges | Radius 0.1 changes the resting height; boxes traverse rounded bent seams with the recorded velocity. | Preserve radius when copying fixtures. Use one endpoint/adjacency rule for every two-sided segment. |
| Sub-steps | Force/damping motion is unchanged by the contact-refresh switch, while sensor callbacks differ. | Keep forces through the outer step and optionally omit contact refresh after the first sub-step. |
| Layer masks | A child collider waits for its body's layer; a joint waits for both bodies. Omitted joints retain the last reaction force. | Filter contacts using collider and body membership; omit excluded constraints without destroying their stored state. |
| Pending movement | Commanded position/rotation reaches the recorded pose, bypasses that component's damping, then resumes its old velocity. | Temporarily replace only the commanded component and restore its velocity and damping after simulation. |
| Slide | Free movement uses one iteration; a limited slide reports unused distance along the normalized remaining direction. Contact movement retains the projected direction's magnitude and a 0.01 gap behind the cast hit. | One movement loop handles free movement, contacts and iteration limits. Anchoring and gravity use the same casts and contact gap. These arithmetic details are observations, not documented universal guarantees. |
| Slide serialization | Movement and the three scalar/vector result properties use compiler backing fields. Hit properties are not serialized. | Serialized auto-properties reproduce the captured names. |
| Fixed clock | Equal captured frame and fixed intervals produce one fixed call per frame. The previous float scheduler skipped a call after clock drift accumulated. | Advancing fixed time in the frame clock's whole-tick domain is the implementation inference that resolves the observation. It is selected through Compat. |

The fixed-clock contract comes from
[Time.fixedDeltaTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-fixedDeltaTime.html)
and [Time.captureDeltaTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-captureDeltaTime.html):
fixed intervals follow game time; capture time advances game time at the selected
rate, scaled by timeScale, independently of frame duration. Capture time does not
control unscaledTime. The documentation does not specify the clock rounding.

Both reference sets pass at the original physics tolerance. Other Unity releases,
local 2D worlds, malformed adjacent points and nonfinite Slide arguments are not
validated by these captures. The regression suite retains its established
geometry-frame, dynamics, query-geometry and input-frame residuals; it reports no
changes against the existing baseline. No importer or persistent cache format is
changed by these runtime fixes.
