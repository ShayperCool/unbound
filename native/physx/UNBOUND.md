# PhysX 4.1.2 (vendored)

[NVIDIA PhysX SDK](https://github.com/NVIDIAGameWorks/PhysX) 4.1.2 (`version.txt`), BSD-3 licensed
(`LICENSE.md`), branch `4.1` at commit `a2c0428acab643e60618c681b501e86f7fd558cc` — the release the
Unity 6000.0 Editor runs. Kept: `physx/include` as `include/`, `physx/source` as `source/` without
its CMake files (`compiler/`) and Windows-only directories, `pxshared/include`. Unmodified but for
three patches (marked `Unbound:`); the first two match what Unity's PhysX does:

- a broken constraint keeps the force that broke it, so `PxConstraint::getForce` reads it in
  `onConstraintBreak` (Unity's `OnJointBreak` passes it) — `ScConstraintCore.h/.cpp` (the unused
  `mAppliedForce`/`mAppliedTorque`) and `ScConstraintSim.cpp`;
- a character controller's walk experiment (the second try after a non-walkable hit, which skips
  the up pass) does not undo a step offset in its down pass — `CctCharacterController.cpp`
  (tests/projects/controller);
- the NEON splats (`PsUnixNeonInlineAoS.h`, arm64) pass `index & 1` as the low-half lane: clang
  checks the lane constant also in the branches of indices 2 and 3, which never take it.
`crates/unbound-host/build.rs` compiles the sources into a static archive linked with
`native/unbound_physx.cpp`, the flat API the managed 3D physics calls.

To update: copy the same directories from a newer 4.1 checkout, reapply the patches and update the
commit above.
