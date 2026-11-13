# 3D contact modification probe

Measures `Physics.ContactModifyEvent`, `Physics.ContactModifyEventCCD`, `ModifiableContactPair`,
`ModifiableMassProperties` and `Collider.hasModifiableContacts`.
`Assets/Scripts/ContactModifyProbe.cs` builds every case at runtime, steps it with
`Physics.Simulate(0.02)` and writes one JSON line per record:

- `pair/<case>/...`: a dynamic box resting on a static ground, with the flag on the box, the ground,
  both or neither, a trigger ground, a kinematic box on a static ground, a kinematic pusher, a
  compound child sphere, a MeshCollider ground, and the flag changed on a live contact (with and
  without a collision message receiver, and with the body moved). Values: event calls and pairs per
  step, the pairs seen, the third step's pair data (ids, poses, velocities, mass properties, every
  contact's point, normal, separation, target velocity, max impulse, bounciness, frictions, face
  index), the body's pose.
- `sleep/...`: pairs per step until the body sleeps.
- `batch/...`: 300 flagged boxes: calls and pairs in one step.
- `edit/<kind>/...`, `ball/...`: a sliding, falling box (or a dropped ball) with one edit applied to
  every contact. Values: its trace, final pose and velocities, pair counts.
- `mass/<case>/...`: two frictionless spheres colliding with edited mass scales.
- `ccd/<case>/...`: a fast Continuous ball against a thin wall: both events, their data and edits.
- `order/events`: the event against the same step's collision messages.
- `clone`: an `Instantiate` copy of a flagged collider: its flag and its reported pairs.
- `unsubscribed`, `default`: the flag without a subscriber, the default flag.
- `control/...`: requests from collision receivers (including disabled, parameterless and exit-only
  methods, on either collider), `providesContacts`, event subscriptions and both callback reuse
  settings; adding/removing a receiver or request alongside a live flag change; changes before
  first contact and from its collision callback; separation and recontact; CCD; repeated compound
  actor replacement; controller flag reads, moves, simulation contacts and identities;
  reuse enabled from a compound callback and the compound receiver destinations.
  New identity controls keep the collider and its body together and canonicalize the two sides,
  since which collider is first is unspecified.

The canonical Native oracle is `Reference/unity-6000.0.71f1.jsonl` (412 records, Unity 6000.0.71f1
on Linux, GPU Editor). Two independent captures were byte-identical. Only one copy is kept here.

Measured (6000.0.71f1):

- Pairs with a flagged collider on either side reach the event; triggers do not, and neither do
  kinematic/static pairs that the default contact pairs mode suppresses. A sleeping body's pairs stop.
- 300 pairs arrive in 3 calls (128, 128, 44). A swept CCD contact arrives once, through the CCD event
  only, with the normal inverted. Ignoring it lets the body through.
- The default mass properties are 1; a static body's instance ID is 0; face indices are mesh
  triangles for a MeshCollider and 4294967295 otherwise.
- The event runs within `Physics.Simulate`, before the step's `OnCollisionEnter`/`Stay`.
- An `Instantiate` clone starts with `hasModifiableContacts` false.
- A live pair refreshes its modification state when the need to retain contact points changes,
  or when the overlap is re-created. Modification itself, a collision receiver on the owning Rigidbody or static Collider
  GameObject, or `providesContacts` requests retention. A flag change with neither other request therefore
  applies on the next simulation; with another request keeping retention enabled, it waits for
  pair recreation. Adding/removing a request and changing the flag together follows the same rule.
  This is measured behaviour, not a documented timing guarantee.
- `ContactEvent` subscription and `reuseCollisionCallbacks` do not request point retention.
  Exit-only receivers request enter/exit reports, without stay reports; parameterless and disabled
  receivers still request points. A `ContactModifyEvent` subscriber alone receives no unflagged
  pairs. Callback reuse true shares one `Collision` object across both sides and later steps;
  false gives distinct callback objects, with the same contact data. Enabling reuse during a
  callback preserves collision data retained while reuse was false. Compound collision messages
  go to the Rigidbody GameObject; collider-only children do not request contact reports or
  receive duplicate messages. Controls test a child-only receiver independently of a body receiver.
- `CharacterController.hasModifiableContacts` defaults to false and reads back either setter
  value. Its simulated contacts with a dynamic body reach modification; its `Move` sweeps do not.
  Its body ID is zero. Static colliders also have zero body ID; each compound child retains its own
  collider ID and resolves to the parent Rigidbody ID, including after repeated destruction and
  recreation. ArticulationBody has no implemented simulation backend yet and is outside this
  capture; this change does not represent its actors as rigidbodies.

Not compared: the calling thread and the order of pairs in a buffer (Unity leaves both unspecified;
pairs are recorded sorted), and which collider is first in a pair (unspecified; the mass cases use
frictionless spheres, whose motion does not depend on it).

All 412 rows are required. The original 343 oracle rows and numeric tolerances are unchanged;
there are no known differences. `publish --extend-reference` admits only new keys and requires
both complete GPU captures to agree byte for byte, including every existing reference row.

The point-retention mechanism uses the PhysX 4.1.2 pair state already present in the backend;
there are no vendored changes. Requests are cached per GameObject for each step and per script
Type for method discovery. Actor and shape IDs use lifecycle-maintained dictionaries; callback
identity reads do not scan bodies or allocate.

The measured request rule is selected through Compat for exactly 6000.0.71f1. Other releases
retain unconditional point requests and the previous collider-plus-body message delivery until
measured; see `docs/COMPATIBILITY_GAPS.md`.

Unity 6000.0 contracts checked for this work:

| API | Documented guarantee / limitation |
| --- | --- |
| [Collider.hasModifiableContacts](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-hasModifiableContacts.html) | Flagged pairs are available for modification while bodies are awake; sleeping pairs stop reporting. |
| [Physics.ContactModifyEvent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ContactModifyEvent.html) | Only flagged pairs; arbitrary calling thread and multiple batches; CCD contacts use the separate event. |
| [Physics.ContactModifyEventCCD](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ContactModifyEventCCD.html) | CCD contacts, with inverted normals. |
| [Collider.providesContacts](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-providesContacts.html) and [Physics.ContactEvent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.ContactEvent.html) | Report requests depend on providesContacts and collision method phases; no trigger reports. |
| [MonoBehaviour.OnCollisionEnter](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnCollisionEnter.html) and [compound colliders](https://docs.unity3d.com/6000.0/Documentation/Manual/compound-colliders-introduction.html) | Collision data is supplied to receiver methods; disabled methods still receive messages. Compound shapes belong to one Rigidbody. The measured destination is its GameObject; the pages do not prescribe this routing in detail. |
| [Physics.reuseCollisionCallbacks](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics-reuseCollisionCallbacks.html) | True reuses one Collision object; false permits retaining callback objects. |
| [ModifiableContactPair](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModifiableContactPair.html) and [CharacterController](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.html) | Pair proxy exposes collider/body identities; the controller inherits the collider flag. Neither page promises live flag-change timing or controller sweep delivery. |

```bash
python3 tests/projects/contactmodify3d/run.py native --label native1
python3 tests/projects/contactmodify3d/run.py native --label native2
python3 tests/projects/contactmodify3d/run.py publish native1 native2   # refuses to overwrite
python3 tests/projects/contactmodify3d/run.py engine --label final      # UNBOUND_CLI selects the CLI
python3 tests/projects/contactmodify3d/run.py verify                    # or --candidate PATH
python3 tests/projects/contactmodify3d/run.py check                     # engine run + verify (gate)
```

Run output stays under `${XDG_CACHE_HOME:-~/.cache}/unbound/validation/runs/contactmodify3d/`.
`UNBOUND_PROBE_UNITY_VERSION` selects the release.

## macOS arm64

The Unity 6000.0.71f1 macOS arm64 Editor (batch mode, `-nographics`, macOS 26)
records 409/412 against the committed Linux reference. `edit/bounciness/final`,
`edit/separation/final` and `edit/separation/trace` differ beyond the
tolerances, and 98 values differ in raw form. Unbound built for arm64 matches
that macOS capture 412/412, with those three records' values equal to it. So
the three rows that fail against the reference on macOS are the Editor's own
arm64 results. The capture is not committed; the reference stays the Linux
Editor's.
