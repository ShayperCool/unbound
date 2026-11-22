# Keyboard configuration cause control

The process-local signal control supplements inputframes. Two independent
Unity 6000.0.71f1 Editor runs on the NVIDIA display agree on all 16 values.

```bash
python3 tests/projects/inputframes/capture_configuration.py editor --release 6000.0.71f1 --label fresh-label
python3 tests/projects/inputframes/capture_configuration.py publish --release 6000.0.71f1 --label fresh-label
```

[InputDevice.OnConfigurationChanged](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.InputDevice.html#UnityEngine_InputSystem_InputDevice_OnConfigurationChanged)
describes configuration events invalidating cached device information, including
key display names after layout changes. [ConfigurationChanged](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.InputDeviceChange.html#UnityEngine_InputSystem_InputDeviceChange_ConfigurationChanged)
reports device configuration changes. Neither documents an event on the first key
press or on a layout/name query.

The control first reads keyboardLayout and the A and B display names. All observed
configuration-event and callback counts remain zero. It then emits the public,
process-local [GDK keys-changed signal](https://docs.gtk.org/gdk3/signal.Keymap.keys-changed.html)
twice without changing the OS map. Each signal advances both counts by one. This
establishes a notification route even when the layout text is unchanged; it does
not reproduce the original first-key trigger or test refreshed key names.

## X11 host control

The host profile starts its own X server. It reads the layout and Y/Z labels,
delivers two taps through XTEST, loads a German map and remaps one physical
key with the layout name still German. It then switches between two active
groups and replaces the map during paused Play mode. Two independent Unity 6000.0.71f1
captures agree on the event sequences, configuration callback counts and
labels.

```bash
python3 tests/projects/inputframes/capture_configuration.py editor --host --release 6000.0.71f1 --label fresh-label
python3 tests/projects/inputframes/capture_configuration.py publish --host --release 6000.0.71f1 --label fresh-label
python3 tests/projects/inputframes/capture_configuration.py unbound --host --release 6000.0.71f1
```

The initial English (US) queries produce zero events. The XTEST device's
first tap generates one configuration event before the key state. The
next tap generates key and text events. Changing the map to German gives
three notifications and exchanges Y/Z labels. Remapping the physical Y
position gives four notifications and changes its label back while the
layout name stays German. Both captures record one configuration callback
per event.
[X Keyboard Extension](https://xorg.freedesktop.org/archive/current/doc/libX11/XKB/xkblib.html),
[GDK X11 event handling](https://github.com/GNOME/gtk/blob/gtk-3-24/gdk/x11/gdkdisplay-x11.c).

The host delivers queued notifications before the frame's key changes.
Switching the active XKB group alone preserves the cached layout and labels
and generates zero notifications. Each switch verifies the actual group on
the X server. Replacing the map while Play mode is paused refreshes the
layout and labels during the pause and generates three callbacks before play
resumes. The Linux landing gate runs the host replay with empty Wayland
environment variables.

Changes that fall between multiple X11 key reports in one frame, Wayland
keyboards and other release families are outside this control.

The process-local GDK signal control runs in the Editor. The X11 host
profile uses actual input on its own display in both engines.
