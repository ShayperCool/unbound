# Main-thread probe

Which engine API members Unity lets scripts call off the main thread, and what the others throw.
`MainThreadProbe` calls about 120 representative members (Object, GameObject, Component,
Behaviour, Transform, Time, Debug, math, Random, Application, Screen, SystemInfo, Input, Camera,
PlayerPrefs, Resources, TextAsset, Texture2D, Mesh, Shader, Hash128, JsonUtility, SceneManager,
UnityWebRequest) in five contexts, each with its own objects made on the main thread first:

* `main`: the main thread, in Start;
* `thread`: a `System.Threading.Thread`;
* `task`: a `Task.Run` thread-pool thread;
* `job`: an `IJob` scheduled and run by a job worker (the probe waits for `IsCompleted` before
  `Complete`, so the main thread does not run it);
* `job.Run`: the same job through `IJob.Run`, on the main thread.

Each call's outcome is a JSON line: `ok` (with `true`/`false`/`null` for booleans and null
answers) or `throws <type>: <message>`. The first line of each context says whether it ran on the
main thread.

Unity 6000.0.71f1 (the reference, recorded twice with byte-identical results):

* `thread`, `task` and `job` give the same answers. Members that are not thread-safe throw
  `UnityException: <native binding> can only be called from the main thread.` followed by Unity's
  two lines about constructors and field initializers; the name is the internal binding's
  (`GetName`, `get_transform`, `Internal_CloneSingle`, `RandomRangeInt`, ...), not the member's.
* `job.Run` runs on the main thread, yet throws the same exceptions: Unity treats a job being
  executed as off the main thread. Only `Object.GetInstanceID` and comparing a destroyed object
  with null differ: off the main thread they throw `InvalidOperationException: EnsureRunningOnMainThread
  can only be called from the main thread` (a check of the thread itself), in `job.Run` they work.
* Allowed everywhere: Object `GetHashCode`/`Equals`/`==` with a live object/implicit bool,
  `Debug.Log`, `Debug.DrawLine`, Mathf, Vector3, Quaternion, Matrix4x4, Color, `Shader.PropertyToID`,
  `Hash128.Compute`, `JsonUtility`, `Screen.width`, `Application.isEditor`/`platform`/
  `unityVersion`/`dataPath`/`streamingAssetsPath`.

Record the reference (two repeats, then publish one if they are identical; GPU Editor through
`tests/editor_runner.py`):

    python3 tests/projects/mainthread/run.py editor --repeat native1
    python3 tests/projects/mainthread/run.py editor --repeat native2
    python3 tests/projects/mainthread/run.py publish

Run Unbound and compare:

    python3 tests/projects/mainthread/run.py unbound --out ~/.cache/unbound/mainthread.jsonl
    python3 tests/projects/mainthread/compare.py \
        tests/projects/mainthread/Reference/unity-6000.0.71f1.jsonl ~/.cache/unbound/mainthread.jsonl
