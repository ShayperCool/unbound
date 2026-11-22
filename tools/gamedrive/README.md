# Game drive: one script for the Unity Editor and Unbound

A debug package that plays a game by a script, the same way in the Unity Editor's Play mode and in
Unbound, so the two runs can be compared screen by screen and log by log. It is for checks on real
projects; it changes nothing in a project and nothing here is specific to one.

Put a copy of `Packages/com.unbound.gamedrive` into the `Packages/` folder of a *copy* of the
project (never the project itself), set the variables below and start the run:

```
# the Editor (windowed: a batch-mode Editor draws no Game view to capture)
GAMEDRIVE_OUT=<dir> GAMEDRIVE_SCRIPT_FILE=<steps.txt> GAMEDRIVE_SCENE=Assets/Scenes/Start.unity GAMEDRIVE_SIZE=960x540 \
  <Unity> -projectPath <copy> -executeMethod Unbound.GameDrive.GameDriveRun.Run -logFile <dir>/editor.log
# Unbound
GAMEDRIVE_OUT=<dir> GAMEDRIVE_SCRIPT_FILE=<steps.txt> unbound run -p <copy> --size 960x540 --no-watch
```

The run ends when the script does: the Editor exits (Unbound's run with it) with code 0, a `quit`
step's code, 3 when a step waited longer than the timeout, or 4 when a step could not be read.

| Variable | Meaning |
|---|---|
| `GAMEDRIVE_OUT` | the directory of `drive.log` and the screenshots; the driver starts only when it is set |
| `GAMEDRIVE_SCRIPT`, `GAMEDRIVE_SCRIPT_FILE` | the steps, separated by `;` or line ends (`#` starts a comment line) |
| `GAMEDRIVE_CLOCK` | `frames`: count time in frames of 1/`GAMEDRIVE_FPS` s (default 60) and set `Time.captureFramerate`; otherwise the wall clock |
| `GAMEDRIVE_TIMEOUT` | seconds a step may wait for its target (120) |
| `GAMEDRIVE_SETTLE` | seconds a target must have been ready before a step acts on it (0.5) |
| `GAMEDRIVE_SEED` | `UnityEngine.Random`'s seed at the start |
| `GAMEDRIVE_SCENE`, `GAMEDRIVE_SIZE` | Editor only: the scene to open and the Game view's size |

## Steps

A target is an active object's name, or the end of its path when the name has a `/`
(`Popup/Buttons/Ok`). Screen points are fractions of the screen from its bottom left.

| Step | What it does |
|---|---|
| `wait:SECONDS` | waits |
| `until:TARGET[:SECONDS]` | waits until the object is there and has been for the time (the settle time by default) |
| `gone:TARGET` | waits until no such object is active |
| `click:TARGET` | presses and releases the target at its centre: an interactable Selectable of that name, else a component that handles pointer downs, clicks or drags. The press goes to the target even when something is drawn over it |
| `press:TARGET:SECONDS` | the same, held for the time |
| `tap:X,Y` | presses and releases whatever is under the point |
| `swipe:X1,Y1:X2,Y2[:SECONDS]` | presses at the first point, moves to the second over the time (0.2) and releases |
| `stick:TARGET:DX,DY[:SECONDS]` | presses the target's centre, pushes it by DX,DY within a tenth of a second, holds for the time (0.5) and releases. DX,DY are in halves of the target's smaller side: `1,0` is its right edge |
| `drag:TARGET:DX,DY[:SECONDS]` | the same movement over the time, released at once |
| `shot:NAME` | writes `NAME.png` with `ScreenCapture.CaptureScreenshot` |
| `hide:TARGET` | keeps the object inactive from now on (an overlay that covers the screen) |
| `dump` | logs what could be pressed now |
| `seed:N`, `log:TEXT`, `quit[:CODE]` | sets the random seed; writes a line; ends the run |

## Time

Games run parts of their flow on the wall clock (tweens on unscaled or real time, timeouts,
network waits), and a load takes as long as it takes: a script that acts at frame numbers meets a
different game in every run. So a step waits for its target, and times are seconds on the wall
clock, in both the Editor and Unbound, which then play in real time: do not pass `--fixed-fps` to
Unbound for such a run. The two runs then go through the same states, not through the same frames;
compare them at `shot` steps and by their logs.

`GAMEDRIVE_CLOCK=frames` is for content that depends on game time only: every wait is a whole
number of frames and a frame is 1/FPS of game time (`Time.captureFramerate`; run Unbound with
`--fixed-fps` of the same rate), so a run repeats frame for frame.

## Input

Gestures are UI pointer events, sent with `ExecuteEvents` in the order an input module sends them
for one touch: enter, down, (begin drag once the pointer has moved past the EventSystem's drag
threshold, the press handler's up, drags,) up, click when nothing was dragged and the pointer is
still over the click handler, drop, end drag, exit. They need an EventSystem and reach uGUI
handlers of any kind: buttons, sliders, scroll views, on-screen sticks and a game's own
`IPointerDownHandler` and `IDragHandler` components. They do not depend on the window's focus or
on the input backend.

Not driven: code that polls `Input.GetMouseButton`, `Input.touches` or Input System devices
directly, without a UI handler. Such input cannot be given to the Editor from a script; in Unbound
alone, `unbound run --tap/--move/--press` send it at frame numbers.

## Log

`drive.log` has a line per event, `SECONDS fFRAME TEXT`: `start`, each step as it acts (`step N
<step> down <object>`, `up`, `held`, `timeout`), `hidden <object>`, `scene <path> <mode>`, the
game's warnings, errors and exceptions (`game Warning: ...`), and `done CODE`.

The probe `tests/projects/gamedrive` runs the driver on a small UI in the Editor and in Unbound and
compares what every step logged and which events the UI got.
