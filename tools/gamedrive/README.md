# Game drive: one script for the Unity Editor and Unbound

A debug package that runs the same scripted UI actions in the Unity Editor's Play mode and in
Unbound. Compare the two runs using screenshots and step logs.

Embed `Packages/com.unbound.gamedrive` in the `Packages/` folder of a disposable project copy.
Create the output directory, then start the run with the variables below.

```
# the Editor (windowed: a batch-mode Editor draws no Game view to capture)
GAMEDRIVE_OUT=<dir> GAMEDRIVE_SCRIPT_FILE=<steps.txt> GAMEDRIVE_SCENE=Assets/Scenes/Start.unity GAMEDRIVE_SIZE=960x540 \
  <Unity> -projectPath <copy> -executeMethod Unbound.GameDrive.GameDriveRun.Run -logFile <dir>/editor.log
# Unbound
GAMEDRIVE_OUT=<dir> GAMEDRIVE_SCRIPT_FILE=<steps.txt> unbound run -p <copy> --size 960x540 --no-watch
```

The final `done CODE` line records 0 when the script finishes, the code supplied by a `quit`
step, 3 when a target or screenshot times out, or 4 when a step fails. The
Editor also returns that status. Use `drive.log` for the script result in Unbound. Its windowed
runner returns status 0 on a normal shutdown. Startup errors go to the engine or Editor log.

| Variable | Meaning |
|---|---|
| `GAMEDRIVE_OUT` | the directory of `drive.log` and the screenshots; the driver starts only when it is set |
| `GAMEDRIVE_SCRIPT`, `GAMEDRIVE_SCRIPT_FILE` | the steps, separated by `;` or line ends (`#` starts a comment line) |
| `GAMEDRIVE_CLOCK` | `frames`: count time in frames of 1/`GAMEDRIVE_FPS` s (default 60) and set `Time.captureFramerate`; otherwise the wall clock |
| `GAMEDRIVE_TIMEOUT` | seconds a step may wait for a target or screenshot (120) |
| `GAMEDRIVE_SETTLE` | seconds a target must have been ready before a step acts on it (0.5) |
| `GAMEDRIVE_SEED` | `UnityEngine.Random`'s seed at the start |
| `GAMEDRIVE_SCENE`, `GAMEDRIVE_SIZE` | Editor only: the scene to open and the Game view's size |

## Steps

A target is an active object's name, or a path suffix when the name contains a `/`
(`Popup/Buttons/Ok`). If several eligible objects match, the driver chooses the first in scene
and hierarchy order and logs the ambiguity once per target. Screen points are fractions of the
screen from its bottom left.

| Step | What it does |
|---|---|
| `wait:SECONDS` | waits |
| `until:TARGET[:SECONDS]` | waits until the object is there and has been for the time (the settle time by default) |
| `gone:TARGET` | waits until no such object is active |
| `click:TARGET` | presses and releases the target at its centre: an interactable Selectable of that name, else a component that handles pointer downs, clicks or drags. The press goes to the target even when something is drawn over it |
| `press:TARGET:SECONDS` | the same, held for the time |
| `tap:X,Y` | presses and releases whatever is under the point. A miss is logged as `down (nothing)` and the script continues |
| `swipe:X1,Y1:X2,Y2[:SECONDS]` | presses at the first point, moves to the second over the time (0.2) and releases. A miss at the first point behaves like a tap miss |
| `stick:TARGET:DX,DY[:SECONDS]` | presses the target's centre, pushes it by DX,DY within a tenth of a second, holds for the time (0.5) and releases. DX,DY are in halves of the target's smaller side: `1,0` is its right edge |
| `drag:TARGET:DX,DY[:SECONDS]` | the same movement over the time, released at once |
| `shot:NAME` | captures `NAME.png` with `ScreenCapture.CaptureScreenshot` before advancing the script. Requires a rendering Game view |
| `hide:TARGET` | deactivates the object immediately and keeps it inactive if it reappears |
| `dump` | logs what could be pressed now |
| `seed:N`, `log:TEXT`, `quit[:CODE]` | sets the random seed; writes a line; ends the run |

## Time

Games run parts of their flow on the wall clock (tweens on unscaled or real time, timeouts,
network waits), and a load takes as long as it takes: a script that acts at frame numbers meets a
different game in every run. So a step waits for its target, and times are seconds on the wall
clock, in both the Editor and Unbound. Run Unbound at its normal real-time speed for these checks.
Compare the game states at `shot` steps and by their logs. Frame numbers can vary between runs.

`GAMEDRIVE_CLOCK=frames` counts waits in whole frames and sets `Time.captureFramerate` to
`GAMEDRIVE_FPS` in both runtimes. Use it for checks that depend on game time. Unbound needs no
additional frame-rate flag.

## Input

Gestures send uGUI pointer events through `ExecuteEvents`. They require an EventSystem and
follow the single-touch event order of uGUI 2.0's Standalone Input Module with parent hover
enabled. They reach buttons, sliders, scroll views, on-screen sticks, and game components that
implement handlers such as `IPointerDownHandler` and `IDragHandler`. Dispatch works independently
of window focus and of the input backend.

The driver sends events for one pressed pointer. Unpressed hover, scrolling, multiple touches,
keyboard and navigation events, UI Toolkit events, and device state read through `Input` or the
Input System are outside its scope. Unbound's `run --tap/--move/--press` can send device input
at frame numbers.

## Log

`drive.log` has a line per event, `SECONDS fFRAME TEXT`: `start`, each step as it acts (`step N
<step> down <object>`, `up`, `held`, `timeout`), `hidden <object>`, `scene <path> <mode>`, the
game's warnings, errors and exceptions (`game Warning: ...`), and `done CODE`.

The probe `tests/projects/gamedrive` runs the driver on a small UI in the Editor and in Unbound and
compares what every step logged and which events the UI got.
