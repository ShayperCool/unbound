# Input probe

The Input System package 1.19.0 (Player Settings' active input handling: Input System package)
with uGUI: what the native layer reports and queues, what actions, PlayerInputs (Send Messages,
Unity Events, C# events) and an `InputSystemUIInputModule`-driven uGUI button, slider and scroll
target, and the InputForUI events the package's ForUI provider gives UI Toolkit, do for one scripted input sequence (pointer moves, clicks, a drag, wheel turns, held keys, a
hold interaction, typing, navigation, submit, cancel), frame by frame. Also recorded: the devices
and their descriptions, device command answers, key names, which legacy `Input` members refuse,
the scroll scale, where Play mode counts as entered, the default player loop.

The scene, the project-wide actions (Unity 6's template), the settings asset and the PlayerInput
prefabs are authored by the Editor (`Assets/Editor/ProbeSetup.cs`):

    Unity -batchmode -projectPath tests/projects/input -executeMethod ProbeSetup.Build -quit

## The Editor's reference

The sequence is played as real X11 input into a windowed Editor on an X server of its own (Xvfb,
no window manager, key auto-repeat off), so the Editor's native input layer handles it as it
handles a person's: `Assets/Editor/ProbeRunner.cs` sizes the window, opens the scene at a 960x540
game view, enters Play mode, and `ProbeDriver` plays `InputProbe.Steps` with `xdotool` (input
played during frame F arrives with frame F+1, which is the step's frame), calibrating the
game-view-to-screen mapping with the first two pointer moves. The Editor quits when the record is
written.

    Xvfb :77 -screen 0 1920x1080x24 -nolisten tcp &
    xset -display :77 r off
    DISPLAY=:77 INPUT_PROBE_DRIVE=1 INPUT_PROBE_OUT=Reference/unity-6000.0.71f1.jsonl \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -projectPath tests/projects/input -logFile /tmp/unity.log

`Reference/integertime-6000.0.71f1.jsonl` (`-executeMethod ProbeSetup.MeasureIntegerTime`) holds
RationalTime's native arithmetic, `Reference/fileids-6000.0.71f1.jsonl`
(`-executeMethod ProbeSetup.MeasureFileIds`) the file ids of the `.inputactions` importer's objects;
the managed tests (`managed/UnityEngine.Tests/InputTests.cs`) check Unbound against both.

## Unbound

`compare.py args REFERENCE` prints the `unbound run` options that play the same steps (`--move`,
`--press`, `--release`, `--scroll`, `--type`; frames counted from 0):

    INPUT_PROBE_OUT=/tmp/unbound.jsonl unbound run -p tests/projects/input --headless 310 --size 960x540 \
        --fixed-fps 60 --no-watch $(python3 tests/projects/input/compare.py args \
        tests/projects/input/Reference/unity-6000.0.71f1.jsonl)
    python3 tests/projects/input/compare.py tests/projects/input/Reference/unity-6000.0.71f1.jsonl /tmp/unbound.jsonl

(`run-unbound.sh REFERENCE OUT [UNBOUND]` does both steps of the run.) The same works in a window
(`--frames 310` instead of `--headless 310`). The comparison checks every record of frames 4 on
(frames 1-3 hold the Editor driver's calibration) exactly — raw events (types, devices, state
bytes, characters), action phases, values and controls, PlayerInput notifications, device state,
uGUI events, values and selection — plus the start-up records and the order of the first two
frames; times are checked against frames (an event's time lies between the previous frame's start
and its own frame's start; an action's time is its event's; a hold performs in the frame its time
is due). As of this writing: 420/420 records match, headless and in a window.

## What the Editor does (Linux, 6000.0.71f1)

* Devices: `Mouse` id 1 and `Keyboard` id 2, interface `Gtk`, no product data; the keyboard
  layout `English (US)`; key names from the layout (letters upper case, `Left Shift`, control keys
  their character), scan codes the `Key` values (F19 and F24 their X keysyms). Every other device
  command fails (-1), the enable/sync/reset ones included.
* Events: per key change a `KEYS` state event of 160 bits, per typed character a `TEXT` event
  after it (Return, Escape, Tab, Backspace, Delete type their control characters; repeats type
  without a state event); per pointer move, button change and wheel notch a 32-byte `MOUS` state
  event (delta since the last event, one unit per notch, click count 0). Events are stamped when the
  window system delivered them, before the frame that processes them.
* Updates: a dynamic update in `PreUpdate.NewInputUpdate`, then the Editor's own update after the
  frame (fixed and before-render updates only when the settings or devices ask). Play mode counts as
  entered after the first frame's LateUpdate; the package then resets its devices (sync fails).
* Times: event and action times are on the real time since startup, which the unscaled time
  follows from the second frame on (Unbound's input clock is its unscaled time, the frame clock).
  The Editor's frames on Xvfb take about 34 ms, so what depends on durations (a hold, the
  navigation repeat, double clicks: 500 ms) is compared by time, and the steps keep presses of
  a button half a second apart.
* With only the Input System active, the legacy `Input` members that read input throw
  `InvalidOperationException`; touch, sensor and settings members answer.
