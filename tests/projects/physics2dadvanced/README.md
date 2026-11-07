# Advanced 2D physics

Unity 6000.0.71f1 Play-mode controls for edge adjacency and edge radius, explicit
simulation in each simulation mode, sub-stepping with and without contact updates,
simulation layer masks and pending body moves/forces, and every SlideMovement option.
The capture records body poses, velocities, contact and callback counts after each
step, and every SlideResults hit field. The API control also records constructor
defaults and the serialized movement structure. It checks whether a body-level
simulationLayers property exists rather than assuming one exists.

`Reference/unity-6000.0.71f1.json` comes from two independent, byte-identical
captures using the supervised GPU Editor runner. Repeats and reports stay in the
Unbound cache. The reference has 172 scenarios. The comparator preserves every
missing, extra and failing value using the physics probe's relative tolerance of
0.001. Callback counts, booleans and strings compare exactly.

Run `run.py prepare`, then `run.py native` with `--release 6000.0.71f1`, a cache
directory in `--work`, and independent `--label` values to record new controls.
Use `run.py engine --runtime <directory-containing-unbound> --project <label>`
to replay a prepared project. `run.py compare` requires `--candidate` and
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

These captures establish explicit Simulate rejection in FixedUpdate and Update
modes, the absence of Rigidbody2D.simulationLayers, and the exposed defaults for
this release. They do not establish automatic player-loop timing or behavior in
other releases. Flat edge controls cover box and circle bodies, four connected
colliders with and without adjacent points, duplicate points, and two radii.
Curved seams and joint/effector layer interactions need additional controls.
