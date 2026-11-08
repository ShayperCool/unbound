# FlareLayer camera dependency

Unity 6000.0.71f1, Linux, Built-in Render Pipeline, OpenGLCore. Two independent Editor
captures agree on the camera checks and both repair warnings. Raw repeats remain in the cache.

The public [FlareLayer API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/FlareLayer.html)
says it is a camera component. The [lens flare instructions](https://docs.unity3d.com/6000.0/Documentation/Manual/create-lens-flare.html)
require a FlareLayer on the camera's GameObject. [RequireComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RequireComponent.html)
documents adding dependencies during AddComponent, and explicitly does not promise repair of
already serialized missing dependencies.

The reference measures that extra load behavior. An explicitly authored prefab and scene
contain FlareLayer without Camera. Importing the prefab and opening the scene each creates one
Camera and warns `Creating missing Camera component for FlareLayer in <GameObject name>.`.
AddComponent also creates Camera, without a warning. An existing disabled Camera is retained
and remains disabled. A FlareLayer on another GameObject does not enable flare drawing on the
probe camera. Disabling the camera prevents automatic drawing; explicit Camera.Render can
still draw flares. Disabling FlareLayer suppresses the flare. These are measured contexts,
not a claim about other pipelines or releases.

## Shader startup observations

The original `Reference/unity-6000.0.71f1/values.txt` is retained unchanged. It records a dark first
explicit draw with default Editor shader compilation. The five-render readings varied between
independent repeats and remain in their `init-trace.txt` files. That observation does not establish
a Camera initialization delay.

Two further captures record `ShaderUtil.anythingCompiling` as true after the first dark draw.
Setting `ShaderUtil.allowAsyncCompilation = false` immediately around Camera.Render makes the
first draw visible in both independent repeats, with compilation no longer pending afterward.
The [compilation control API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil-allowAsyncCompilation.html)
applies at the current rendering call site, and
[anythingCompiling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderUtil-anythingCompiling.html)
reports outstanding asynchronous compilation. This distinguishes shader startup from camera
lifecycle behavior. No fixed frame delay is imposed by the engine.

`Reference/unity-6000.0.71f1/sync-shaders/` is the camera-dependency gate's controlled profile.
It records the first draw, the next five explicit renders, the steady draw, automatic rendering,
and controls involving enabled and previously rendered cameras. `shader-trace.txt` retains each
Editor rendering call's compilation state. Unbound's asynchronous Editor shader-startup timing
is unimplemented and remains a rendering follow-up; the original observation is still available
with `--profile async-observation` and its differences are not suppressed by the comparator.

`run.py` copies the probe into the cache for every run. It starts no Editor in `check` mode.
The flare and UV texture are shared inputs copied from `tests/projects/flares`.

```sh
python3 tests/projects/flarelayerdependency/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/flarelayerdependency/run.py publish --release 6000.0.71f1 --label <same-label>
python3 tests/projects/flarelayerdependency/run.py check --release 6000.0.71f1 --cli /path/to/unbound
```

Doctor's importer and project-settings reports use the same leaf-path accounting as serialized
assets, including JSON package settings stored with an `.asset` extension. Rust runs the ordinary
metadata and startup configuration readers on private tracked trees, without decoding source
content. Managed physics and navigation settings use declarations generated from their actual
readers. Unknown importer and settings fields remain visible, including empty and zero values.
`importerFields` and `projectSettingsFields` contain complete JSON lists and reasoned metadata
exceptions; the human output gives totals and examples. The active target is included in the
JSON. Fields not read for that target remain conservatively visible. Reading a field records
consumption, not a promise of complete feature parity or Editor API coverage. Values retained
only for inspection while their import effect is not applied, such as audio compression quality,
are marked by their reader and still appear as "may change behaviour".
