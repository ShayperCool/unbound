# Runtime sprite geometry allocation

The Unity 6000.0.71f1 Editor attributes 1460 bytes to a script-created Sprite with a quad.
The size consists of a 1368-byte base, source vertices at 20 bytes each with four extra bytes
for each vertex in the final incomplete group of four, and two bytes per reserved index.
Geometry replacement keeps the largest source index reservation. A copy starts with the
source's current vertices and indices and acquires its own reservations.

Making a copy, replacing geometry or sampling it through a particle Shape module adds a
580-byte reservation once. First camera use also adds 20 bytes for each vertex and two bytes
for each index in the camera's geometry. This allocation reflects the geometry last prepared for
a camera. Replacing the source geometry updates its own cost immediately. Its camera allocation
changes on the next use.

The camera controls cover a Simple SpriteRenderer and a visible Sprite-mode SpriteMask,
including a mask without a receiver and a renderer with an empty material list. A renderer
that is disabled, forced off, outside the view or excluded by layer leaves the reservation
unchanged. The captures show the initial size in the Built-In visibility callbacks and the
increased size after the camera renders. In the scriptable pipeline, a completed cull raises the
size before any renderer draws. The probe separately checks the target before and after drawing.
The manual and API describe the render modes and the distinction between culling and drawing.
[Sprite Renderer manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-SpriteRenderer.html) · [Cull](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.Cull.html) · [SpriteRenderer.drawMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SpriteRenderer-drawMode.html)

Sliced and Tiled renderers use their own generated geometry and leave the Sprite's size at its
previous value. This profile measures Sprites created from a texture through `Sprite.Create`
without generated fallback physics or secondary textures. Imported Sprites, low-level geometry
data writes, other release families and storage needed only by shadow rendering remain unmeasured.
[Sprite.Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite.Create.html)

```sh
python3 tests/projects/spritememory/run.py record --release 6000.0.71f1 --label first
python3 tests/projects/spritememory/run.py record --release 6000.0.71f1 --label second
python3 tests/projects/spritememory/run.py publish --release 6000.0.71f1 first second
python3 tests/projects/spritememory/run.py gate --release 6000.0.71f1
```

Publishing checks two distinct sealed Editor runs. The gate runs in a private project and
compares each value exactly.
