This stage implements the public ContactEvent and ContactPairHeader, ContactPair,
ContactPairPoint APIs over actual PhysX reports. It does not complete Unity parity.
The branch starts at 00e1fd2285054c4de75604e7271adac5043f31c4. Main ABI5 and
shadercache13 stay unchanged; the private PhysX binding and matching managed guard
are version 9. No SDK algorithm, Rust, Rigidbody, 2D, generic runtime, historical
fixture or reference is edited. The game project was never accessed.

Native measurements preceded implementation. The first default-profile recording
crashed in GetContactPointFaceIndex(-1), after the unchecked negative accessor had
returned. Evidence/native1-p0-crash.json and its complete log/source retain that
failure. Acceptance recordings do not dereference negative face indices. Negative
struct accessor bounds remain unchecked in Native; high bounds throw. Copied
headers, pairs and ref returns contain borrowed pointers without safety handles.
Reading those pointers after the callback lifetime is undefined and is not tested.

The initial two default recordings agree byte-for-byte on 5,484 records, as do the
other three settings. A second series moves kinematic bodies, exercising the pair
settings. The final named series assigns a globally unique name to every fixture
GameObject. Each of its four profiles has two byte-identical independent Native
recordings, containing 6,658, 7,399, 7,415 and 8,156 rows. The recorder calls the same
public API on Native and engine. Reflection is used only for layout inspection.
Historical reflection-based supplemental fixtures remain untouched.

Raw object IDs remain in every capture. compare.py maps only identity rows and
raw header/pair IDs through identities explicitly registered from the fixture's
actual objects. Header and pair IDs are independently checked against the public
body/collider object's GetInstanceID. Static bodies and removed actors/shapes have
zero IDs where Native reports zero. No object IDs are fabricated or discarded.

Layouts are 40 bytes for ContactPairHeader, 40 for ContactPair and 48 for
ContactPairPoint. Every field offset is captured. Ref-readonly header, pair and
point access works. PhysicsScene has two 32-bit fields at offsets 0 and 4, size 8;
its zero value is the valid default scene. Its hash is process dependent, including
between Native recordings. Both raw hash rows and their differences remain in the
report. Within each process the default value and defaultPhysicsScene hashes agree.

The shim records each SDK header, pair flags/events, point position, separation,
normal, full reported impulse and face indices in SDK order. These buffers are
separate from the existing contact map and event lists. Pair impulse sums add the
SDK point impulses. The original geometry path is retained. Mesh face queries
translate SDK triangle indices through the cooking remap; non-mesh colliders,
including terrain, return the measured invalid mesh-face index.

Public reports require providesContacts or a collision-message receiver. The
implementation preserves real removal reports across simulations with no rigid
bodies, matching Native's deferred batch. Compound shapes group under their actor
header, while distinct actor pairs remain separate headers. Empty and paused
simulations do not deliver empty arrays. Sleeping and moved kinematic cases are
captured under all four settings.

Trigger messages precede ContactEvent, which precedes legacy collision messages.
ContactEvent still runs when invokeCollisionCallbacks is false. Legacy collision
messages use data captured before subscriber mutations. Disabling a collider,
deactivating its object, converting it to a trigger, moving it and unsubscribing
inside the event preserve the measured current-step behavior. A throwing handler
stops later handlers, is logged, and does not prevent legacy callbacks. Recursive
simulation is rejected without throwing to the subscriber.

The NativeArray.ReadOnly safety handle remains valid through legacy callbacks,
then is released before Simulate returns, including when a subscriber throws.
Length and IsCreated retain their values, but checked reads throw the measured
ObjectDisposedException. A previous event's view is already invalid in the next
event. Point copies own their destination data. Short copies truncate, long copies
leave the tail alone, default buffers accept a zero-length copy, and a disposed
nonempty destination throws. A completed IJob reading the borrowed headers works
in both runtimes. Jobs left outstanding across callback return, unsafe stale
pointers, arbitrary threads and manual release of simulation buffers are unmeasured.
No Collections or Jobs implementation files were changed.

The new API's four profile comparisons match 29,420 of 30,252 union keys, 29,172
exactly. All 832 differences are capsule cases. The frozen public stub baseline's
union comparisons resolve 29,348 differences and retain 208; 624 additional keys
are the extra real SDK capsule points. These introduced keys are retained as
failures, not excluded. Per-profile counts and every difference are in
Evidence/delivery-summary.json. These scores are not a full certification claim.

The capsule gap crosses ownership. Native AddComponent<CapsuleCollider> starts
with height 1; this checkout uses 2. Native also retains a tiny capsule segment at
height equal to diameter, while Collider.cs replaces it with a sphere. Explicit
height-2 recordings expose a point-order difference from the original orientation.
An orientation experiment corrected that case but introduced the accepted broad
query failure 3d.queries/capsule.h-1.point.y. It was rejected and reverted. Its exact
native source, verified against its build-input hash, all captures, the regression
report and build manifest are preserved. No contact points are reordered or
synthesized to hide these differences.

Native rejects DestroyImmediate during a contact callback with a logged error and
keeps the object alive. Core/Object.cs currently destroys it, losing one legacy
callback. The controls comparison retains all three resulting key differences.
The other two controls differences are the raw process hashes described above.
The geometry comparison retains four capsule/default rows; mesh and terrain
contact and face observations pass. outside-ownership.patch proposes the default,
degenerate capsule and destruction-guard changes without applying them. It passes
git apply --check but is not tested or authorized as an integrated fix. The owning
agent must validate it. It does not resolve the rejected orientation experiment.

All raw recordings, earlier candidates, rejected captures and exact sources are
preserved under Evidence. Sources is the final probe code; Assets is a new Unity
fixture authored through the Editor creation path. The append-only runners refuse
to overwrite projects, captures or logs. The comparator checks the union of keys.
Discrete values, identities, layout, counts, flags, order and strings compare
exactly. Only physical floating-point values use the fixed absolute 1e-5 bound,
chosen before implementation; no existing tolerance or comparator changed.

Evidence/Regression/summary.json records the fresh accepted regression replay.
The final integrated filter is the parent's six classes plus
UnityEngine.Tests.PhysicsContactEventTests, passing 378/378 without skips. The
standalone API checker uses the current checkout's no-stub assembly and installed
Unity engine/platform modules, passing zero errors across 2,274 handwritten types.
The parent should repeat the final integrated API check.

The supported scope is Unity 6000.0.71f1 Linux and its default 3D scene. Local
scenes, older releases, other scale/rotation combinations, mesh mutation during
callbacks and outstanding jobs remain unmeasured. Capsule and forbidden immediate
destruction differences are explicit open limits. There is no full-engine parity
claim.

The accepted replay preserves all 5,055 matrix observations, including the original
252 motion-rounding records; all 21 raw files are byte-identical to acceptance.
Focused inactive 1,050, sequences 4,668, active 20,700, clone placement 172, long
triggers 1,728, SDK flags 1,152, trigger pairs 1,440, ignore lifecycle 1,848 and
immediate setters 15 remain exact. Order 276, component 276 and barrier 227 pass
the unchanged comparator. Both serialized-loading orders pass 4,656/4,656 exactly,
9,312 total, against the preserved independent Native evidence.

Broad physics remains 11,495/11,626 with 7,885 exact values and 131 retained 2D
failures. Timing remains 18,275/18,491 and mixed order 5,560/5,720, retaining all
216 and 160 failures. All three raw datasets are identical to prior acceptance.
Defaults, controller, joints, rayhits, forces, Joint2D and effectors pass their
unchanged comparators. Graphics 80, Canvas 8,671 at 24 frames and 640x480, and
messages 316 pass. Lifecycle remains Native-byte-identical. All seven immutable
snapshot manifests verify unchanged. No accepted value newly fails.

The final geometry precision pass records mesh-face indices as uint values,
including 4294967295, without conversion through float. Earlier geometry captures
used a float array for these rows; they remain preserved, and their earlier report
is Evidence/final-summary.json. The final pair is geometry-exact1/exact2, recorded
in separate Native processes with the same public calls. The authoritative final
report is Evidence/delivery-summary.json. It retains every physical capsule
mismatch, every extra point key and every raw process-hash difference.

Reproduction and exact final build flags are in commands.sh. Evidence/builds keeps
the actual environment, all build/test/API logs, native compiler/link commands,
verified rejected native sources and capture sources. The private artifacts remain
under /mnt/storage-hdd/node-b-caches-20261023/contactevents3d-t3-f3abad48/. All
heavy jobs use the shared exclusive flock; SDK archives and snapshots are read-only
inputs, copied or reflinked rather than hardlinked. The first ccc initialization
succeeded, but indexing/search requests stalled; the final 25-second index attempt
also timed out. Its log is retained. Code review used rg and direct source reads.

Initial discovery recordings/builds used private project and build outputs with
the standard package/global cache defaults. The accepted replay and delivery use
explicit private UNBOUND_CACHE, XDG_CACHE_HOME, NUGET_PACKAGES and DOTNET_CLI_HOME.
All snapshot hashes are unchanged; shared caches were not cleaned or rewritten
manually. Further subscriber changes during trigger messages, outstanding jobs
and other callback mutations need additional Native controls before certification.
