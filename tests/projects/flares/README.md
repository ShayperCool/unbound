# Halos and lens flares

Unity 6000.0.71f1, built-in pipeline, Linear color space (`Assets/Scripts/FlareProbe*.cs`). A
camera at z -5 looking along +z (field of view 60, near 0.3) draws into a 128x128 ARGBFloat
target (no HDR, no MSAA, black clear with zero alpha) with a FlareLayer; frames are fixed at
1/30 s (`Time.captureDeltaTime`), and each case is read at the end of a frame.

`ProbeRunner.Create` (batchmode) made the assets: `Uv.png` (256x256, point filtered, red and green
the texel's x and y, blue and alpha 1, so every drawn pixel tells which texel it sampled),
`HaloRamp.png`, single-element flares (`S*` sizes, `P*` positions, `Rot*`, `Zoom20`, `Fade20`,
`Light20`, `Color20`, `L<layout>_<image>` for every image of the six texture layouts), `Quad4`
(plain, light-colored, fading, light-colored and fading elements side by side), `Combo`, and the
scenes: `Probe.unity` (Halo components, lights with a halo, LensFlare objects, one directional and
one ignoring layer 8) and `HaloTexture.unity` (its RenderSettings halo texture is `HaloRamp`, set in
the scene file: there is no scripting API for it).

* **Halos** (`h.*`, `t.*`): a Halo component moved, scaled, under haloStrength 1, orthographic,
  with a rolled camera, without FlareLayer, over a colored background, behind a wall, in fog; a
  colored Halo; a point light's halo by range, intensity, strength, color, disabled; spot and
  directional lights; the scene halo texture. `start.haloFalloff`, `h.falloff`, `t.haloFalloff*`
  read the global `_HaloFalloff` texture back.
* **Order** (`o.*`, `ho.*`): a command buffer clearing the target at AfterForwardAlpha,
  BeforeHaloAndLensFlares and AfterHaloAndLensFlares, with a halo and a flare, then with a halo
  alone; a halo with a flare; a Halo added by script (its defaults), also reactivated.
* **Flare geometry** (`f.S*`, `f.P*`, `f.Rot*`, `f.L*`): sizes, fields of view, target aspect,
  orthographic, depth, rolled camera, positions along the line, rotation, layouts.
* **Flare settings** (`f.*`, `q.*`): element switches while fading in, LensFlare color and
  brightness, flareStrength, fog, no FlareLayer, light flares (color, intensity, directional),
  directional LensFlares, the four element kinds under each setting.
* **Fading** (`f.fade.*`, `f.speed.*`, `f.occ.*`, `q.light.*`): fade in and out speeds, colliders
  (triggers, the Ignore Raycast layer, ignored layers), renderers without colliders, off screen,
  behind the camera, disabling.

## Recording

```
python3 tests/projects/flares/run.py create --release 6000.0.71f1 --label <fresh-label>   # assets (already in Git)
python3 tests/projects/flares/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/flares/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/flares/run.py unbound --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/flares/compare.py tests/projects/flares/Reference/unity-6000.0.71f1 \
    ~/.cache/unbound/validation/runs/flares/<label>/unbound --report <cache>/report.json
```

Each Editor runs windowed on a private Xvfb with the GPU through VirtualGL. A guard ends an Editor
that logs its shutdown and is still alive 30 s later (its own process group) and records the
forced exit; the Hub an Editor starts for its project copy is killed. `publish` requires two
byte-identical complete captures and refuses to overwrite a reference. The comparator keeps the
whole key union and image manifest with the float tolerance of the render texture probes
(absolute and relative 2e-4).

`Reference/unity-6000.0.71f1/` holds the values and 212 images of two byte-identical captures
(OpenGLCore, RTX 4060).

## What the Editor does (6000.0.71f1, Linux, OpenGL Core, Linear)

* `_HaloFalloff` is a global texture from the start: `UnityHalo`, 64x64 alpha, bilinear, clamped,
  alpha `round(255 * max(0, 1 - r²))` with r the texel center's distance from the middle over 32.
  A scene's halo texture replaces it when the scene loads (sampled with its own filter).
* A halo is a square fan of four triangles in the camera's right/up plane, its corners `size` away
  along each axis, its center vertex `size / 3` nearer the camera; it draws with the Halo shader
  (depth tested, fogged towards black, `OneMinusDstColor One`) with or without a FlareLayer. A
  Halo component's size is its own (not scaled by its transform, not by haloStrength); a light's
  is its range times haloStrength, for every light type, while the light is enabled.
* Colors reach the shaders as 8-bit sRGB vertex colors decoded to linear and stored in 8 bits
  again. A Halo component's color is its own; a light halo's is the light's color times its
  intensity times the sRGB encoding of haloStrength.
* Halos and flares draw between BeforeHaloAndLensFlares and AfterHaloAndLensFlares, halos
  first. A camera with any command buffer draws no halos (its flares it does). A Halo added by
  script draws nothing until it is given a size and color (it has no scripting API).
* Lens flares need an enabled FlareLayer. An element is a quad at `position` along the line from
  the flare's point on screen through the screen center, half `size * 0.0100354` of the
  projection's vertical scale tall (constant on screen with a perspective camera, independent of
  the target's width; an orthographic camera scales it by the flare's depth). `Rotate` turns it
  into a diamond whose corners are 1.4 half sizes away, its top-right corner pointing away from
  the center. `Zoom` scales it by the flare's visibility.
* Texture layouts: 1 Large 4 Small (the top half, then a 2x2 grid in the bottom half), 1 Large 2
  Medium 8 Small (the top half, two quarter images on the left of the bottom half, a 2x4 grid on
  its right), 1 Texture, 2x2, 3x3 and 4x4 grids from the top left; each image's texture rectangle
  is inset by half a texel times the part of the texture outside it (the large and medium images
  of the second layout reach about 0.47 texel further left and up). Images past a layout's count
  continue its pattern (clamped by the texture).
* An element's sRGB color is its color, times the light color when `Use Light Color` is set (a
  LensFlare's color, a light's color times its intensity), times the sRGB encoding of brightness
  times visibility unless it uses the light color without fading. The brightness is the
  LensFlare's; a light's flare takes `RenderSettings.flareStrength`, which a LensFlare ignores.
* Visibility fades in at the fade speed per second (a LensFlare's `fadeSpeed`, a light's
  `RenderSettings.flareFadeSpeed`) while the flare's point is on screen and no collider is on the
  ray from the camera (triggers and the Ignore Raycast layer count, renderers without colliders
  do not, nor the LensFlare's ignored layers), and out at half that speed; disabling restarts it
  at zero. Directional flares are infinitely far opposite their forward axis. Fog and `useFog`
  change nothing measured here.
