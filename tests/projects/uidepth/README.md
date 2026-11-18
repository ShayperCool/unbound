# Canvas renderer depths

`CanvasRenderer.absoluteDepth`, `CanvasRenderer.relativeDepth` and uGUI's `Graphic.depth` as the
Unity 6000.0.71f1 Editor numbers them, and the order and depths of `EventSystem.RaycastAll` at
the screen's centre for each GraphicRaycaster. Scripting API 6000.0:
[absoluteDepth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CanvasRenderer-absoluteDepth.html)
is the "Depth of the renderer relative to the root canvas",
[relativeDepth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CanvasRenderer-relativeDepth.html)
the depth "relative to the parent canvas"; uGUI 2.0's
[Graphic.depth](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/api/UnityEngine.UI.Graphic.html)
is the "Absolute depth of the graphic, used by rendering and events -- lowest to highest". The
pages say nothing about several root canvases, overriding canvases, disabled content or when the
numbers change; those are what the probe measures.

`UiDepthProbe` builds, in Play mode, overlay root canvases at equal and different sorting
orders, one on Display 2, a disabled one, Screen Space Camera canvases with and without a
camera, world canvases in front of, behind and culled by the camera and one without a camera;
nested canvases with and without `overrideSorting`, disabled, inactive, empty, holding only an
empty or overriding canvas, with a renderer on their own object; renderers that are culled
(`cull`, RectMask2D), transparent (colour, `SetAlpha`, CanvasGroup), disabled, inactive, empty
or without a Graphic. Each key `<phase>.<renderer>` is `[absoluteDepth, relativeDepth,
Graphic.depth]` (`null` without a Graphic); `<phase>.raycast.<canvas>` lists `name:depth` in
RaycastAll's order for the hits of one canvas's GraphicRaycaster, which its depths decide; the
order between raycasters (their canvases' `sortingOrder` and `renderOrder`) is not recorded
here, except in `<phase>.raycastRoot.OR`: the hits under one root raycaster, a root and an
overriding canvas at the same sorting order, which RaycastAll orders by depth. Phases: `made`
(same frame as built), `initial`, then reparenting, enabling and disabling, `overrideSorting`
changes, a canvas made late, roots made nested and nested made roots, a canvas added and one
removed, and `Canvas.ForceUpdateCanvases`. `<phase>.now` keys are read right after the change,
`reparent.next` one frame later, the others two frames later.

## Measured behaviour

- Each canvas that sorts on its own (a root canvas, or a nested one with `overrideSorting`)
  numbers its renderers from 0, whatever its render mode, display, camera or culling.
  `absoluteDepth` counts the renderers of active objects in hierarchy order through the nested
  canvases drawn with it; a disabled graphic, culled, transparent and empty renderers and a
  renderer without a Graphic are numbered too. `Graphic.depth` equals `absoluteDepth`.
- `relativeDepth` counts within the nearest canvas, a nested canvas drawn with it taking one
  number when it numbers at least one renderer (an overriding canvas inside it counts). A
  nested canvas's own renderer belongs to it.
- A disabled canvas numbers nothing below it (-1), enabled nested canvases drawn with it
  included; enabled overriding canvases below it number themselves.
- A renderer whose canvas changes (reparenting into another canvas, a canvas enabled, disabled,
  deactivated, added or removed above it, `overrideSorting` changed) is -1 at once; a
  renderer whose own object is deactivated keeps its numbers until the next canvas update. A
  new renderer is -1 until then. `Canvas.ForceUpdateCanvases` numbers nothing; the frame's
  canvas update does, and a change made in Update is numbered when the next frame starts.

Unmeasured: releases other than 6000.0.71f1, sorting layers, a canvas with two Canvas
components, reading inside `Canvas.willRenderCanvases`, and a renderer moved out of a canvas and
back within one frame (Unbound: -1 until the next update).

## Reproduce

```sh
python3 tests/projects/uidepth/run.py record  --release 6000.0.71f1 --label rec
python3 tests/projects/uidepth/run.py publish --release 6000.0.71f1 --label rec
UNBOUND=<build>/unbound python3 tests/projects/uidepth/run.py gate --release 6000.0.71f1 --label gate-1
```

`record` runs two batchmode Editors (the numbers do not depend on the graphics device; the
reference was recorded with the macOS Editor); `publish` requires both captures to be identical.
The gate compares every value exactly and lists every missing, extra or differing key.
`ProbeRunner.Create` regenerates `Assets/Scenes/Probe.unity`.
