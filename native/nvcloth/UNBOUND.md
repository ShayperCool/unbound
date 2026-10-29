# NvCloth 1.1.6 (vendored)

[NVIDIA NvCloth](https://github.com/NVIDIAGameWorks/NvCloth) 1.1.6 (`ReleaseNotes.txt` of the
repository), under the NVIDIA Source Code License (`LICENSE.txt`, the repository's `license.txt`),
branch `master` at commit `6e86b1838ae185c527744d475d34777ff69c1b8a` — the release the Unity 6000.0
Editor runs (its symbols have 1.1.6's `setSpheres(start, target)` and `teleportToLocation`). Kept:
`NvCloth/include`, `NvCloth/src` without the CUDA, DirectX and NEON solvers, and
`NvCloth/extensions` (the fabric and tether cookers). Unmodified. `crates/unbound-host/build.rs`
compiles it (the CPU solver: SSE2, or scalar off x86 — the NEON solver needs Android's cpu-features) on PhysX's PxShared into a static archive
linked with `native/unbound_cloth.cpp`, the flat API the managed Cloth calls, into the
`unbound_physx` library.

To update: copy the same directories from a newer checkout and update the commit above.
