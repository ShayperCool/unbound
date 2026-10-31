# Input frames probe

What every input path reports when keys and mouse buttons go down and up between two frames:
once, several times, released and pressed again, next to presses in adjacent frames, in frames
with five fixed steps or none, and while the Input System processes its events in fixed updates.
Recorded per frame: the Input Manager (`Input.GetKey*`, `GetMouseButton*`, `anyKey*`,
`inputString`) in FixedUpdate and Update, the Input System (controls, its event stream, text,
actions with and without interactions, polled and called back, its updates), IMGUI events, a uGUI
button under the StandaloneInputModule, and OnMouse* messages. Player Settings' active input
handling is Both. Steps and their frames: `InputFramesProbe.Steps` and `Captured`.

## Running

    run.py create                 makes the scene with the Editor (batchmode)
    run.py editor --label L       two independent Editor captures, each on a private Xvfb
    run.py publish --label L      checks that they agree, copies one to Reference/
    run.py unbound --label L      an Unbound capture (UNBOUND_CLI, UNBOUND_MANAGED_DIR) compared
                                  with Reference/unity-6000.0.71f1.jsonl

Captures, logs and comparisons go to `$XDG_CACHE_HOME/unbound/validation/runs/inputframes/L`.
The Editor runs windowed on an X server of its own (Xvfb, no window manager, key auto-repeat
off) through VirtualGL; `Editor/ProbeRunner.cs` sizes the window, opens a 960x540 game view,
enters Play mode and plays each step's tokens with one `xdotool` run, so all of them reach the
Editor before its next frame. In Unbound, `compare.py args` turns the steps into `--move` and
`--keys FRAME:+KEY,-KEY,...` options (`unbound run` counts frames from 0).

`compare.py` compares every record of frames 8 on (frames 1-6 hold the driver's calibration)
exactly and in order within its frame, minus wall-clock fields (event and action times, the
fixed steps' unscaled time); event times are checked against frames instead. Every missing and
extra record is printed.

As of this writing Unbound matches 3785 of the reference's 3786 records, with nothing extra and
no time problems. The one missing record is a keyboard `DCFG` (device configuration) event the
Editor queues just before the first key's state event. It appeared in every capture of this probe
(also with the keyboard layout read in Start, and with only the Input System active) but in no
capture of tests/projects/input, so what makes the Editor send it is not known; Unbound does not
send it.

## What the Editor does (Linux, 6000.0.71f1)

* Input Manager: every press and release since the last frame counts. A key or button pressed
  and released between two frames is down, up and held for that frame (FixedUpdate sees the same);
  one released and pressed again is down, up and held. `inputString` has every character typed
  (Return types `\r`).
* IMGUI: the events go out at `PreUpdate.IMGUISendQueuedEvents`, after the frame's fixed steps
  and before the Input System's update and Update, in the order they happened (a click is a
  `mouseDown` and a `mouseUp`, a second click within the frame has click count 2, a key typed
  during a click lies between them). Mouse events carry the button's key code (`Mouse0`). Each
  event has the modifiers held after it (Shift's press has Shift, its release none); Shift,
  Control and Alt are function keys.
* Input System: one state event per key or button change, in order, and a text event after a
  press that types; actions see every press and release (a click within a frame starts,
  performs and cancels). Processing events in fixed updates, a fixed step takes the events stamped
  up to its unscaled time, which is the frame's unscaled time less the step's lag behind the game
  clock: events that arrive with a frame go to the next frame's first step.
* Time: with `captureFramerate` set in Awake, the first frame's time and deltaTime are the
  capture delta (its fixed step at 0).
* OnMouse* and uGUI follow from the Input Manager: OnMouseDown in a click's frame,
  OnMouseUp/UpAsButton in the next; the button gets down, up and click in the click's frame.

## Driver limits

`xdotool` presses Shift_L with Shift_R (and likewise for Control and Alt), so right modifiers are
not played. Key names go to `xdotool` as X keysyms (`ProbeDriver.KeySym`), which are case
sensitive: a key without an entry there must be a lower-case keysym.
