# 2D contact observation phases

This probe retains the complete original `Probe2D.RunAll()` and `Probe3D.RunAll()`
sequence before 34 contact controls. The controls vary circle/box geometry,
rotation, approach speed and discrete/continuous collision detection. Each records
body state and `GetContacts` before and after each simulation, plus the collision
payload and a separate `GetContacts` read inside callbacks. Real world history is
preserved; the probe does not add dummy bodies or alter allocation state.

The Unity 6000.0.71f1 Linux reference has 27,676 values across 63 scenarios.
Two independent project copies produced byte-identical Native captures, SHA-256
`61cf170339bd79a8fa463482f63fe55c4b9ed08f417101aa5479728cc071b529`.
Only the agreed capture is retained in `Reference/unity-6000.0.71f1.json`.

For example, `phase/False/0/-12/False` enters on step 2. Both its collision
payload and callback query report point `(0.02, -0.066962)`, separation
`-0.153923988` and relative velocity `(-1.12539244, 12.1962)`.
The body has already been solved when its callback runs. Public contact data
therefore cannot all be reconstructed from that body's current pose and velocity.
The rotating-box and continuous controls remain separate observations; agreement
in this circle example does not establish a solver ordering rule.

```bash
export DYNAMICS_WORK="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/contact-phases"
python3 tests/projects/physics2ddynamics/run.py prepare --label native1
python3 tests/projects/physics2ddynamics/run.py prepare --label native2
python3 tests/projects/physics2ddynamics/run.py native --label native1
python3 tests/projects/physics2ddynamics/run.py native --label native2
cmp "$DYNAMICS_WORK/native1.json" "$DYNAMICS_WORK/native2.json"
python3 tests/projects/physics2ddynamics/verify.py --suite dynamics --candidate "$DYNAMICS_WORK/native2.json"

# Select a private managed runtime and matching CLI explicitly.
export UNBOUND_CLI=/path/to/private/unbound
export UNBOUND_MANAGED_DIR=/path/to/private/managed
python3 tests/projects/physics2ddynamics/run.py engine --label baseline
python3 tests/projects/physics2ddynamics/run.py engine --label final
python3 tests/projects/physics2ddynamics/verify.py --suite dynamics \
    --candidate "$DYNAMICS_WORK/final.json" --baseline "$DYNAMICS_WORK/baseline.json"
```

`UNBOUND_PROBE_UNITY_VERSION` selects the release. `UNBOUND_PROBE_UNITY` or
`UNITY_EDITOR` selects the Editor executable. Every capture uses the shared
heavy-job launcher. Independent Native captures can run in parallel because
preparation creates separate project copies. Engine jobs are exclusive.

The verifier uses the existing union-key comparator and unchanged 1e-3 relative
numeric tolerance. All missing, extra and failing observations remain in external
reports. Without a candidate it checks reference structure only. This is a
measurement probe, not a claim that current dynamics match Unity. Older releases
remain unmeasured. Runtime experiments, Native repeats, traces and comparisons
belong in the validation cache, never this source directory.

Detailed diagnostic tools and run outputs stay outside Git. Public controls
preserve body creation, deletion, type changes and contact order without adding
dummy bodies or changing allocation state. Their observations constrain solver
ordering, but do not establish a complete storage policy for every lifetime
operation. Other releases require their own measurements.

The six appended dynamic-ground controls use a supporting body with mass 3.
They cover circle/box impacts in both collision modes and two circle controls
with gravity and the locked translation axis rotated by 90 degrees. All original scenario rows
remain unchanged. Both independent reference captures used the GPU renderer.

Unity 6000.0 documents [normal impulses](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ContactPoint2D-normalImpulse.html)
along the contact normal and [tangent impulses](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ContactPoint2D-tangentImpulse.html)
perpendicular to it. [Collision2D.relativeVelocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision2D-relativeVelocity.html)
is relative linear velocity; its exact sampling phase is not specified.
The [dynamic body reference](https://docs.unity3d.com/6000.0/Documentation/Manual/2d-physics/rigidbody/body-types/dynamic/dynamic-body-type-reference.html)
describes continuous detection at the first impact and its higher CPU cost.
The recorded contact phases and impulse accumulation are release-specific
observations, not additional ordering guarantees from these pages.

The dynamic-ground controls also exercise the documented
[FreezePositionX](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RigidbodyConstraints2D.FreezePositionX.html)
and [FreezePositionY](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RigidbodyConstraints2D.FreezePositionY.html)
constraints during oblique impacts. A tangential impulse would move the supporting
body along its locked axis unless the solve itself respects that constraint.

Four sub-step controls hold a resting circle against the ground with four solves
per simulation call, with both collision modes and contact-refresh settings.
The public normal impulse is 0.1962 per call in every case, including subsequent
calls, rather than the 0.04905 impulse of the last sub-step.
The measured contact policy uses the cached Physics2D simulation compatibility
flag. Unmeasured releases report a diagnostic, expose the discrete warm-start
impulses and retain the first contact observation for entering collision velocity.
Their continuous impulses and contact sampling remain an explicit gap.
