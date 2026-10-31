# Collider skin capture proposal

Strict stage acceptance is blocked. The query candidate resolves the 76 assigned
broad observations and retains the original 55 motion/contact failures. New
stress controls still contain introduced failures, so this branch is not ready
for integration under the no-new-failing-values rule. No failure is masked.

`Evidence/boundary-native1.json` and `boundary-native2.json` agree byte for byte
on 64 observations. `Sources/OwnerBoundary.cs` uses the same geometry in every
case. Both processes measure the following Unity 6000.0.71f1 Linux behavior:

* Offset 0.001 before creating and sizing the box: BoxCast misses.
* Create and size at offset 0.01, then set 0.001: BoxCast hits at distance 3.095,
  centroid y 0.9049999, point y 0.506999969.
* Explicit sync before changing the offset gives the same result.
* Change size after setting 0.001: BoxCast misses again.
* At offset 0.1, creation before/after the change and a subsequent size edit
  likewise distinguish the captured collider skin from the temporary query skin.

This supports capturing a polygon collider's skin when its geometry is authored
or registered. It does not establish behavior for every enable, clone, material,
trigger, composite or deserialization transition. Older releases are unmeasured.

The ownership boundary is `managed/UnityEngine/Physics2D/Collider2D.cs` and
`Physics2DWorld.cs`, both owned by LIFECYCLE. They were read but not edited.
The current `Collider2D.ShapeChanged` marks a collider dirty, and
`Physics2DWorld.Rebuild` later invokes `BuildShapes`. Polygon constructors then
read the current global `Settings.PolygonRadius`, losing the earlier skin.

Proposed owner patch:

1. Store the geometry's contact-offset value on the collider when it is registered
   and when a geometry setter changes it. Keep this separate from query temporary
   shape construction and from subsequent global tuning.
2. In `BoxCollider2D.BuildShapes`, polygon shape construction and edge/chain
   construction, use that captured value as the base skin, plus authored extrusion.
   Do not change circle/capsule authored radii, shared GJK, TOI, solver epsilon or
   the global solver settings as a shortcut.
3. Keep the captured value when only `Physics2D.defaultContactOffset` changes.
   Refresh it after the measured size edit. Additional lifecycle transitions need
   their own two independent Native recordings before broadening the rule.
4. Replay all six new Native suites, the original broad fixture and the existing
   timing, contact, joints, effectors and forces gates before accepting integration.

The query candidate separately uses fixed query TOI precision, validates an actual
surface contact at the returned time, clips polygon/capsule features without
altering common contacts, and measures contact averaging over the core overlap.
Those changes belong to QUERIES. The proposal above does not authorize or conceal
any change to the other owner's files.
