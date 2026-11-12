# Keyboard configuration cause control

This is a Linux Editor supplement to inputframes, not an engine parity fixture.
Two independent Unity 6000.0.71f1 Editor runs on the existing NVIDIA display agree
on all 16 values. The parent 3786-record reference and its strict comparator remain
unchanged.

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

The host currently supplies no OS keymap notifications, and the engine layout
command still reports US. The follow-up must carry real notifications through the
host, refresh layout/key names and preserve event ordering. Synthesizing one event
at the first key, on every metadata query, or only when the layout text differs
would not implement the measured mechanism.

Existing-display physical-key attempts delivered zero keyboard state events and
are not reference evidence. This supplement has no engine mode because its local
GDK signal belongs to the Editor process. It does not stand in for the missing
host integration.
