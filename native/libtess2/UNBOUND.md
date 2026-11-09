# libtess2 1.0.3 (vendored)

[libtess2](https://github.com/memononen/libtess2), the GitHub `master` archive of 2025-10-15
(`master.tar.gz`, sha256 `e21abbf4a3d6c396c28d2d986bbbd4a649fb5b962a4775903cace62fe38465af`),
SGI Free Software License B 2.0 (`LICENSE.txt`). Kept: `Include/` and `Source/`, unmodified.

Why: Unity triangulates Tight sprite meshes with libtess2 (the Editor links `tessNewTess`,
`tessTesselate`, `tessSetOption` and `tessMeshRefineDelaunay`, the same API): nonzero winding,
triangles, no normal given. `crates/unbound-assets` (`build.rs`, `sprite_mesh.rs`) compiles it with
floating-point contraction off (the Editor's build has no fused multiply-adds) and calls it the same
way, so the triangles come out in the Editor's order.

To update: copy `Include/` and `Source/` from a newer archive and update the date and hash above;
the sprite import probe (`tests/projects/spriteimport`) checks the triangles still match.

Two builds use it: `crates/unbound-assets` links it into the engine binary for sprite meshes, and
`crates/unbound-host/build.rs` links it with `native/unbound_tess.c` into the `unbound_physx` library
next to the managed engine, where UI Toolkit's Painter2D fills call `ua_tess_triangulate`
(tests/projects/uitkpainter). The managed engine and its tests load that library, not the engine
binary, so one build cannot serve both; both compile the same sources without floating-point
contraction.
