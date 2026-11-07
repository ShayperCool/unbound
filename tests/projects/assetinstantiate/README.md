# Asset object instantiation

The Play Mode probe covers AudioClip, Font, Cubemap, Texture2D, RenderTexture, Material,
Mesh, AnimationClip, ScriptableObject, Sprite, TextAsset, PhysicsMaterial,
RuntimeAnimatorController, AnimatorOverrideController, ComputeShader and Shader.
It records identities, names, flags, data ownership, load state, readability, overloads,
diagnostics and immediate/deferred destruction. It includes a stereo runtime audio control,
shared audio resource lifetime controls, valid/invalid Sprite geometry, custom-font override
controls, audible cloned imported audio and rendered cloned bitmap Font/Cubemap/Material controls.
The controller fixture has no layers or animation clips; graph cloning is not certified.
Dynamic Font cloning logs an error in the Editor, returns an object and shares its material.
Its rendering is explicitly unsupported by that diagnostic; only bitmap Font rendering is certified.

Every run uses a fresh project copy under the validation cache. The Editor path uses
`tests/editor_runner.py` on the GPU. Publication requires two byte-identical independent
complete captures. The engine-only gate uses the committed release reference and writes
its full key union, all missing/extra values and all failures into the cache. Numeric values
use an absolute tolerance of 0.00001; metadata, diagnostics and rendered equality controls
compare exactly. Rendered controls compare each clone with its original within the same run,
not pixel-for-pixel against the other engine. Audio checks for a nonzero captured signal.

```sh
python3 tests/projects/assetinstantiate/run.py editor --release 6000.0.71f1 --label native
python3 tests/projects/assetinstantiate/run.py publish --release 6000.0.71f1 --label native
UNBOUND_CLI=/path/to/this/checkout/build/unbound python3 tests/projects/assetinstantiate/run.py check --release 6000.0.71f1
```

## Contract and cause table

The documented rules below were checked for Unity 6000.0. The reference records observations
in the 6000.0.71f1 Linux Editor, not guarantees for other releases.

| Area | Documented rule | Observation in this reference |
| --- | --- | --- |
| [Object.Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html) | Copies the object. GameObject/Component cloning includes child objects and component state. Active state and callback restrictions apply. Deep hierarchy recursion is bounded; no prefab connection is created. | Asset clones have distinct identities and zero hide flags. Parent overloads accept these assets. Shader keeps its name; other measured classes append `(Clone)`, including repeated and empty names. The position-only ScriptableObject overload throws; the position-plus-parent overload accepts it. |
| [Serialization rules](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-rules.html) | Serialized fields copy by value; UnityEngine.Object fields hold object references. Inline managed classes do not preserve shared managed identity. | ScriptableObject self-references remap to its clone. Managed arrays/classes copy independently, and a null serialized AnimationCurve becomes empty. |
| [AudioClip.GetData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.GetData.html) | Samples are interleaved by channel; compressed/streamed data has access restrictions. | Runtime clip clones have no samples or callbacks. Imported clips share sample writes while retaining separate object identities. Destroying the imported original leaves a reloadable clone. Compressed diagnostics identify the shared resource. |
| [Font](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Font.html) | Material displays the font; dynamic glyph requests populate its texture. | Custom character arrays copy independently; material references stay shared. Dynamic clones report an error and begin with no cached characters. |
| [Mesh.isReadable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-isReadable.html) | UploadMeshData can remove CPU readability while retaining renderable data. | Cloning preserves unreadability and vertex count after UploadMeshData(true). The vertices getter logs an error and returns an empty array. Imported model CPU-array access remains unmeasured and preserves previous behavior. |
| [Sprite.OverrideGeometry](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite.OverrideGeometry.html) | Replaces geometry with positions and triangle indices; texture UVs follow the sprite rectangle. | Clone geometry replacement preserves the original. Out-of-rectangle positions log an error and preserve previous geometry. |

Cubemap CPU faces/mips are independent; unreadable clones keep unreadability and report an
error at instantiation. The retained CPU representation is an implementation choice. GPU-only
Cubemaps without retained storage still fail explicitly. Texture2D/RenderTexture/TextAsset
have more extensive controls in `tests/projects/assetclones`, which remains a regression probe.
ComputeShader clones retain the source and kernels but reset runtime parameter values, keywords and bindings.
Full controller graph mutation, imported bitmap fonts, additional audio import configurations,
GPU-only Cubemaps and other Unity releases remain outside this capture.
