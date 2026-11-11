# First Play frame and end-of-frame completion

Unity 6000.0.71f1, windowed Linux GPU Editor, built-in render pipeline. The runner
opens Game view on the existing GPU display. Two independent captures must exit
normally and agree byte for byte before `publish` creates the canonical reference.

```bash
python3 tests/projects/eofstartup/run.py editor --release 6000.0.71f1 --label startup
python3 tests/projects/eofstartup/run.py publish --release 6000.0.71f1 --label startup
python3 tests/projects/eofstartup/run.py unbound --release 6000.0.71f1 --label engine
```

The first Update is frame 0 in the record. Screen and render-texture cameras render
in that frame, and explicit `Camera.Render()` sends its callback before returning.
The frame has no EOF completion: coroutines started in Awake, Start and the first
Update, and `Awaitable.EndOfFrameAsync` started in Start, first resume at frame 1.
The control started in Update of frame 2 resumes in frame 2. After an additive
scene load, the scene's Awake and Start waiters resume after its first Update and
camera post-render callback; loading a scene does not repeat Play startup.
The counters check every frame through frame 9, so skipping more than startup or
adding a delay to every newly started coroutine fails the comparison.

This is a measured Editor startup rule. The Unity 6000.0
[execution-order Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/ExecutionOrder.html)
and [WaitForEndOfFrame API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForEndOfFrame.html)
place EOF resumes after camera and GUI rendering. The API also documents that
switching away from Game view freezes these coroutines and that batch-mode Editor
execution does not resume them. Neither page guarantees a resume in the first
Play frame. The additive post-render counter tests the documented camera-before-EOF
guarantee with values that change before the waiter resumes. Receiver order is
not compared. SRP, multiple Game views and other releases are unmeasured here.

All values are small integer counters. The shared JSONL comparator's numerical
comparison rejects any unequal counter. Existing destroycontract references and
comparators are unchanged.
