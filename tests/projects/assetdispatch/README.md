# Asset type dispatch

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 95 rows. The values are managed asset state (CPU
copies and descriptors), so they do not depend on the OS; the render texture's depth bits, which do
depend on the graphics device, are not recorded. The gate compares every missing, extra or unequal
row exactly.

- `interfaces.*`: the interfaces 25 asset types declare, none in the reference.
- `<asset>.*`: Object.Instantiate of a runtime-made asset in a nondefault state: the copy's type and
  `(Clone)` name, that it is a distinct object, its state, its state after a change, and the
  original's state after that change. The assets are a Mesh (vertices and triangles), a Material
  (color, render queue, shader), a Texture2D (pixels, filter and wrap modes), a Cubemap (a face
  pixel), a RenderTexture (size, format, wrap mode), a CustomRenderTexture (update mode,
  initialization color, double buffering), a legacy AnimationClip (frame rate, length, wrap mode as a
  number), LightingSettings and a ShaderVariantCollection. A Sprite and a default
  AnimatorOverrideController are cloned without a later change. A cloned Shader keeps its name.

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [Object.Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html) | Each `*.changedCopy` against `*.original`. |

Not compared: Object.Instantiate of the types Unbound only reads, the mixer group and snapshot
included (unsupported, it fails loudly, while the reference allows it), and other releases.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/assetdispatch/run.py native --label reference-a
python3 tests/projects/assetdispatch/run.py native --label reference-b
python3 tests/projects/assetdispatch/run.py publish reference-a reference-b
python3 tests/projects/assetdispatch/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
