# First Play frame and end-of-frame completion

The probe records the windowed Linux Unity 6000.0.71f1 Editor. Two independent captures must
exit normally and agree byte for byte before `publish` creates a reference.

```bash
python3 tests/projects/eofstartup/run.py editor --release 6000.0.71f1 --label startup
python3 tests/projects/eofstartup/run.py publish --release 6000.0.71f1 --label startup
python3 tests/projects/eofstartup/run.py unbound --release 6000.0.71f1 --label engine
```

The default opens a saved scene in Single mode and enters Play in the same Editor update.
`--entry settled` opens the same scene before the wait in the Editor runner.
`--entry edit-immediate` executes the same-update entry from an Edit Mode session in
Unbound. `--entry restore` restores the saved scene setup then enters Play in the same
update. The restore control uses one saved, loaded and active scene as required by the Unity
6.0 API. Both reuse the immediate reference. [OpenScene API][open] · [RestoreSceneManagerSetup API][restore]

Unbound opens Edit Mode for these three
entries with `unbound test --graphics`. The probe's private copy omits package test
runners so its recorder owns the Play transition. Every entry counts frames from Update.

The built-in pipeline renders both the screen and the render-texture camera in frame 0.
Explicit `Camera.Render()` sends its callback before returning. With same-update entry,
the first end-of-frame waits resume in frame 1. With an intervening Editor update, they
resume in frame 0. Controls measured the same result with one, two and thirty intervening
updates. Opening an additional scene or creating an empty scene and entering immediately
also allowed frame 0 in separate Editor controls.

The probe covers coroutines started in Awake, Start and the first Update, and
`Awaitable.EndOfFrameAsync` started in Start. Each entry has the same first resume frame
for objects made in BeforeSceneLoad, AfterSceneLoad, Awake, Start and Update. The object
made in Update of frame 2 starts and resumes in that frame. The `before.*` counters
check that every resume follows the first Update. An additive load during Play delivers
the new scene's waits after its first Update and camera post-render callback.

`--pipeline urp` uses the project settings, packages and pipeline assets of
`tests/projects/stpmotion`. Its end-of-frame values match the built-in pipeline.
The legacy `Camera.onPostRender` counters are zero under URP. References for same-update
entry are `unity-<release>.jsonl` and `unity-<release>.urp.jsonl`. The settled
references add `.settled` before the pipeline suffix.

The Unity 6.0 [execution-order Manual][order] and [WaitForEndOfFrame API][wait]
place waits after camera and GUI rendering. The Scripting API describes Game view
suspension and batch-mode Editor limits. The additive post-render counter records the
camera-before-wait guarantee in the built-in pipeline. Frame 0 timing here is measured
Editor behavior. Other pipelines, multiple Game views and other releases are unmeasured. The windowed
macOS Editor has a separate first-frame difference recorded in the destroycontract README.

The comparator checks every integer counter exactly. Existing same-update captures
retain their frame 0 records alongside the settled references.

[order]: https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html
[wait]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForEndOfFrame.html
[open]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.EditorSceneManager.OpenScene.html
[restore]: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.EditorSceneManager.RestoreSceneManagerSetup.html
