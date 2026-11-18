# What scene loads unload; asset loads in the scene queue

Unity 6000.0.71f1, Linux GPU Editor (OpenGL Core), Play mode entered with domain reload disabled,
EditorSettings.forceAssetUnloadAndGCOnSceneLoad on (off for one case). Our own content: Resources
assets (ScriptableObjects `ResData`, `ResPlain`, `ResHeld` (kept in a static field), `ResQueue1`,
`ResQueue2`, materials `ResMat`, `ResPlainMat`, a text asset), assets only scene `A` references
(`SceneData`, `SceneMat`), an empty scene `B`, and the StandaloneLinux64 bundle `things` that
tests/projects/assetbundles commits (copied into StreamingAssets). Two windowed captures were
byte-identical.

`SceneUnloadProbe` (a DontDestroyOnLoad object) opens `A`, loads the Resources assets, edits
`ResData`, `ResMat`, `SceneData` and `SceneMat` (`ResPlain`, `ResPlainMat` and the text asset are only
loaded), drops every reference and then, per case, loads `B` (LoadScene Single, LoadSceneAsync
Single, LoadSceneAsync Additive, Single with the forced unload off) or calls
Resources.UnloadUnusedAssets, recording which of them Resources.FindObjectsOfTypeAll still lists
(with their values) at sceneLoaded, at the operation's completion and on the next three frames, and
what Resources.Load gives back. The queue cases hold a LoadSceneAsync with allowSceneActivation
false and request Resources.LoadAsync, AssetBundle.LoadFromFileAsync and AssetBundle.LoadAssetAsync
after it (or the Resources load before it), recording whether each completes while the scene is
held or after its release (how many frames after depends on the machine's load and is not recorded).

| Source (6000.0) | Documented | Measured here |
|---|---|---|
| [EditorSettings.forceAssetUnloadAndGCOnSceneLoad](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorSettings-forceAssetUnloadAndGCOnSceneLoad.html) | A Single load unloads unused assets and collects the managed heap. | Unused ScriptableObjects, text assets and materials scripts did not change are gone the frame after the load (not yet at sceneLoaded or when the load reports done); materials scripts changed stay, with their changes; what a static field holds stays; reloading gives the file's values under the same instance ID. Off: nothing is unloaded. Additive loads unload nothing. |
| [Resources.UnloadUnusedAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Resources.UnloadUnusedAssets.html) | Unloads assets nothing reaches. | The same kinds as above, at the operation's completion. |
| [AsyncOperation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation.html), [allowSceneActivation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html) | A scene load held at 0.9 stalls the queue of asynchronous operations. | Resources.LoadAsync, AssetBundle.LoadFromFileAsync and AssetBundle.LoadAssetAsync requested after a held scene load complete only after its release; a Resources load requested before it completes while it is held. |

Unbound: 216/255 with 39 recorded residuals (`residuals.json`): unused ScriptableObjects and
unchanged materials stay loaded in Unbound (196/255 before Single loads ran the unused-asset walk
and asset loads joined the queue). Unmeasured: other asset kinds, other releases.

```sh
python3 tests/projects/sceneunload/run.py editor --release 6000.0.71f1 --label L
python3 tests/projects/sceneunload/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/sceneunload/run.py check --release 6000.0.71f1
```
