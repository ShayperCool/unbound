# UI Toolkit runtime controls

Unity 6000.0.71f1 controls for runtime panel creation and sorting, selectable-object
component assignment, IMGUI event conversion, navigation filtering, wheel-event
pooling and the shared click detector's configurable double-click interval.
The panel component control uses a separate `UnityEngine.UI` assembly, as the
runtime panel component interface is internal to the UI integration.

The [runtime event system manual](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-Runtime-Event-System.html)
describes runtime input and coordination with uGUI. The
[WheelEvent API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.WheelEvent.html)
requires pooled events to be disposed and describes their propagation and target.
The internal static members' defaults, conversion choices and notification counts
are observations from this release, rather than documented cross-release guarantees.
The wheel controls initialize nonzero pointer data and then inspect a fresh pooled
wheel to distinguish copying from reset behavior. Chord and window-exit controls
exercise button state separately from the event-enumeration pass.

Record twice and publish only matching GPU captures:

```sh
python3 tests/projects/uitkruntime/run.py native --release 6000.0.71f1 --label native-a
python3 tests/projects/uitkruntime/run.py native --release 6000.0.71f1 --label native-b
python3 tests/projects/uitkruntime/run.py publish native-a native-b --release 6000.0.71f1
python3 tests/projects/uitkruntime/run.py check --release 6000.0.71f1
```

The gate reads the committed release reference, runs only the engine and compares
every key exactly, including missing and extra keys. GPU captures were recorded
on the NVIDIA driver. These controls do not claim that other devices were measured.

The defaultstate inventory adds these event types with two independent captures.
Existing reference rows remain byte-identical. New creation timestamps retain
their raw values and use the existing numeric timestamp migration policy.
Absolute event-registration numbers are context observations; the defaultstate
relational control checks that one type keeps its ID and different types differ.
