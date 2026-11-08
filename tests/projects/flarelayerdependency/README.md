# FlareLayer camera dependency

Unity 6000.0.71f1, Linux, Built-in Render Pipeline, OpenGLCore. Two independent Editor
captures agree on every value and both repair warnings. Raw repeats remain in the cache.

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
still draw flares. The first explicit render after enabling FlareLayer on a disabled Camera is dark;
repeated renders in later frames show it. An already visible flare stays visible under
explicit rendering of the disabled camera.
The steady explicit-render check uses 60 frames. A five-render reading varied between Editor
repeats and is retained in each run's `init-trace.txt`; it does not establish a frame-count
contract. Delayed visibility readiness is an unresolved timing hypothesis.
Enabling FlareLayer while its Camera is enabled initializes visibility immediately, even
if that Camera is disabled before its first render. A new source on a previously initialized
disabled Camera is visible on its first explicit render. Disabling FlareLayer suppresses the
flare. These are measured contexts, not a claim about other pipelines or releases.

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
content. Managed physics settings use the declarations generated
from their actual readers. Unknown importer and settings fields remain visible, including empty
and zero values. `importerFields` and `projectSettingsFields` contain complete JSON lists and
reasoned metadata exceptions; the human output gives totals and examples. The active target is
included in the JSON. Fields not read for that target remain conservatively visible. Reading a
field records consumption, not a promise of complete feature parity or Editor API coverage.
Values retained only for inspection while their import effect is not applied, such as audio
compression quality, are marked by their reader and still appear as "may change behaviour".
