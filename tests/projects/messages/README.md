# Messages probe

Engine messages and player-loop behaviour, frame by frame, for one scripted input sequence and a
series of scripted changes (`Assets/Scripts/MessagesProbe.cs`; the Input Manager is the active
input handling):

* `OnMouseEnter/Over/Exit/Down/Drag/Up/UpAsButton`: a collider clicked, dragged off and released
  elsewhere; a trigger with `Physics.queriesHitTriggers` off and on; a collider under a rigidbody
  (the body's object gets them); Ignore Raycast; a layer outside the camera's `eventMask`; 2D
  colliders (under a Rigidbody2D too), a 3D and a 2D collider at the same place (both get them);
  an object only an overlay camera of a higher depth sees; a render-texture camera turned on (the
  Editor skips it); the hovered object deactivated (an inactive object gets no message), its
  collider turned off while pressed.
* `OnApplicationPause(false)` and `OnApplicationFocus(true)` when Play mode starts (after the
  scene's Awake/OnEnable, before the AfterSceneLoad initializers, disabled behaviours too), and
  `OnApplicationFocus` when another window takes the focus and gives it back (between frames; the
  order of the objects: those made at run time newest first, then the scene's).
* The cursor: `Cursor.lockState`/`visible` and what `Input.mousePosition` and the `Mouse X/Y`
  axes report while locked, for relative pointer motion.
* Visibility: `isVisible` and `OnBecameVisible/Invisible` (during the camera's culling; early in
  the frame after the last one that rendered it; at once when the renderer or its object is turned
  off) for an object moving in and out of view, one behind the camera, one on a layer no camera
  sees, a `ShadowsOnly` caster, casters above the view with and without shadows, layer cull
  distances (planar, then spherical), a render-texture camera.
* `OnBeforeTransformParentChanged` (the whole subtree, inactive objects too, before the relinking),
  `OnTransformParentChanged`, `OnTransformChildrenChanged`.
* Fixed steps: the first frames, a 0.7 s frame (`maximumDeltaTime` clamps the frame, nothing caps
  the steps), a 0.3 s frame under `maximumDeltaTime` 0.1.
* `StartCoroutine` on a disabled behaviour (runs), an inactive object's (refused, logged); a
  coroutine whose behaviour is disabled (goes on).
* `SendMessage` to an inactive object and a disabled behaviour, `BroadcastMessage` from an
  inactive root.
* A Single scene load: roots with `HideFlags.DontSaveInEditor` (`DontSave`, `HideAndDontSave`)
  stay, in no scene; the others go.
* `Debug.Break` (pauses after the frame; `Editor/ProbeRunner.cs` unpauses after ten Editor
  updates) and `Application.Quit` (ignored in Play mode: no wantsToQuit, no quitting).

The scenes are made by the Editor: `Unity -batchmode -projectPath tests/projects/messages
-executeMethod ProbeRunner.Create -quit`.

## The Editor's reference

The input is played as real X11 input into a windowed Editor on an X server of its own (Xvfb, the
GPU through VirtualGL, no window manager), as tests/projects/input does: `ProbeRunner` opens the
scene at a 960x540 game view and enters Play mode, `ProbeDriver` plays `MessagesProbe.Steps` with
`xdotool` (calibrating the game view with the first two moves); for the focus step an `xmessage`
window titled `probe-focus` takes the focus for half a second.

    Xvfb :77 -screen 0 1920x1080x24 -nolisten tcp &
    DISPLAY=:77 xset r off
    DISPLAY=:77 xmessage -title probe-focus -geometry +1500+900 focus &
    DISPLAY=:77 MESSAGES_PROBE_DRIVE=1 MESSAGES_PROBE_OUT=$PWD/tests/projects/messages/Reference/unity-6000.0.71f1.jsonl \
        vglrun -d egl ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -projectPath tests/projects/messages -logFile /tmp/unity.log

## Unbound

`compare.py args REFERENCE` prints the `unbound run` options that play the same input (`--move`,
`--press`, `--release`, `--motion`, and `--focus` at the frames the Editor reported its focus
changes); headless frames keep the wall clock (the slow frames are real sleeps):

    MESSAGES_PROBE_OUT=/tmp/messages.jsonl unbound run -p tests/projects/messages --headless 445 --size 960x540 \
        --no-watch $(python3 tests/projects/messages/compare.py args tests/projects/messages/Reference/unity-6000.0.71f1.jsonl)
    python3 tests/projects/messages/compare.py tests/projects/messages/Reference/unity-6000.0.71f1.jsonl /tmp/messages.jsonl

Every record is compared exactly, in order within its frame, except the fixed-step records, whose
clocks follow each run's frame times (checked: the fixed clock within a step behind the game clock,
a slow frame's delta `maximumDeltaTime`, as many steps as the delta covers).

## What the Editor does (Linux, 6000.0.71f1)

* While the cursor is locked, `Input.mousePosition` follows the pointer and the `Mouse X/Y` axes
  report its motion as when unlocked (on this X server the Editor does not hold the pointer);
  `Cursor.visible` reads back what was set.
* `Camera.allCameras` lists the cameras by depth.
* No `OnApplicationPause` but at the start: neither losing the focus nor `Debug.Break` sends one.
