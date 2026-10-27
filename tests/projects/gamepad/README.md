# Gamepads and joysticks

`GamepadProbe` plugs a virtual Xbox 360 pad into the machine (`/dev/uinput`, bus USB, vendor
045e, product 028e, version 0x110), moves its buttons, sticks, triggers and hat, then unplugs it,
and records per frame the Input System's devices and gamepad, and the legacy Input Manager's
joystick names, joystick buttons and axes ("Horizontal", "Vertical", "Fire1", "Jump", "Submit",
"Cancel"). Frames take at least 16 ms so the hotplug lands on nearly the same frames.

Measured (see `crates/unbound-runtime/src/gamepads.rs`, `managed/UnityEngine/Input/Gamepads.cs`,
`NativeInput.cs`): the Editor reads pads through its embedded SDL, with its own controller
database (`data/profiles/sdl-gamecontrollerdb-6000.0.txt`, the mappings in the Editor's binary);
a pad SDL maps is a `Gamepad` of the "SDL" interface, its product the mapping's name, its
manufacturer udev's vendor name, its version the USB version in decimal. Its state is the
`GPAD` format (SDL's axes over 32768, y up). The legacy Input Manager reads the raw joystick:
SDL's button order and axes (over 32767, the dead zone cut out and the rest stretched).
Unbound loads the system's SDL2 (`libSDL2-2.0.so.0`) and gives it Unity's mappings.

The machine needs write access to `/dev/uinput` (a udev rule giving it to the `input` group, and
the user in it) and SDL2.

Make the probe scene (once; it is committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -nographics -projectPath tests/projects/gamepad \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/gamepad-create.log

Record Unity's reference: a windowed Editor on an X server of its own, which opens the scene and
enters Play mode:

    Xvfb :78 -screen 0 1920x1080x24 &
    DISPLAY=:78 GAMEPAD_PROBE_OUT=$PWD/tests/projects/gamepad/Reference sg input -c \
        "~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -projectPath tests/projects/gamepad -logFile ~/.cache/gamepad.log"

Run Unbound and compare:

    sg input -c "GAMEPAD_PROBE_OUT=/tmp/gamepad unbound run -p tests/projects/gamepad --headless 300000 --fixed-fps 60"
    python3 tests/projects/gamepad/compare.py tests/projects/gamepad/Reference/gamepad.jsonl /tmp/gamepad/gamepad.jsonl

Every record matches but one expected difference: the Editor's SDL holds back a trigger's first
move until the pad's next event (the right trigger pulled in frame 104 shows only when it is
released in frame 108), the system SDL reports it at once.
