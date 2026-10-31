# Sprite import probe

What sprite import gives a game: for every sprite of `Assets/Sprites`, `SpriteImportProbe` records
its rect, pivot, pixels per unit, bounds, `textureRect` and `textureRectOffset`, mesh (vertices,
uvs, triangles) and physics shapes (`GetPhysicsShape`). The textures (`make_textures.py`) cover
shapes with holes and islands, extrude 0 to 32, tessellation detail -1 to 1, alpha tolerance
(alpha 100 is in the mesh but not the physics shape), rects under 32 pixels, pivots and pixels per
unit, a soft edge raw and DXT-compressed, a texture downscaled by the max size, custom outlines and
physics shapes in Single, Polygon and Multiple modes, Full Rect with an outline, and fractional
sprite rects.

    python3 tests/projects/spriteimport/make_textures.py      # after changing a case
    python3 tests/projects/spriteimport/run.py create --label <L>   # remakes Assets/Scenes/Probe.unity
    python3 tests/projects/spriteimport/run.py editor --label <L>   # two Editor captures
    python3 tests/projects/spriteimport/run.py publish --label <L>  # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> python3 tests/projects/spriteimport/run.py unbound --label <L>
    python3 tests/projects/spriteimport/compare.py \
        tests/projects/spriteimport/Reference/unity-6000.0.71f1.jsonl \
        ~/.cache/unbound/validation/runs/spriteimport/<L>/unbound/sprites.jsonl

Values compare exactly (as 32-bit floats). What Unity 6 does is described in
`crates/unbound-assets/src/sprite_mesh.rs` and `sprite.rs` (`build_geometry`): a Tight mesh traced
from alpha above 0 for rects of at least 32 by 32 pixels (smaller ones, and Full Rect, get the
quad), or triangulated from the custom outline; the default physics shape traced at detail 0.25
from alpha above 200 (extrude does not matter); `textureRect` is the traced outline's bounds;
`bounds` is the rect's, also for Tight meshes.

What still differs (515/520 values): the `downscaled` sprite (4 values), because Unbound's
max-size downscale gives alpha up to 4/255 off the Editor's (28 pixels cross the alpha 0
threshold); and the default physics shape of a Polygon-mode sprite, which Unity takes from its
mesh's boundary edges and Unbound does not make (it warns).
