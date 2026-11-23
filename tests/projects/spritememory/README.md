# Runtime sprite geometry allocation

The Unity 6000.0.71f1 Editor attributes 1460 bytes to a script-created Sprite with ordinary
geometry. Making a copy or sampling the geometry through a particle Shape module raises the
source's size to 2040 bytes. Each copy starts at 1460 bytes and acquires its own reservations.
First camera use of the Sprite's geometry raises its size to 2132 bytes. Geometry changes
retain an acquired reservation.

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
without generated fallback physics or secondary textures. Imported Sprites, other release
families and storage needed only by shadow rendering remain unmeasured.
[Sprite.Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite.Create.html)

```sh
python3 tests/projects/spritememory/run.py record --release 6000.0.71f1 --label first
python3 tests/projects/spritememory/run.py record --release 6000.0.71f1 --label second
python3 tests/projects/spritememory/run.py publish --release 6000.0.71f1 first second
python3 tests/projects/spritememory/run.py gate --release 6000.0.71f1
```

Publishing checks two distinct sealed Editor runs. The gate runs in a private project and
compares each value exactly.
