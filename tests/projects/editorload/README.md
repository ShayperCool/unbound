# Editor API package editor code calls while it loads

Package editor code (2D Animation, Sprite Shape, collaboration, purchasing, analytics and
ad SDKs) registers callbacks and reads Editor state when it loads, which in Unbound is when Play
mode starts. `Assets/Editor/EditorLoadProbe.cs` (an editor script run when Play mode starts)
writes values.jsonl in `$EDITORLOAD_OUT` (tests/value_probe.py): for every action, what it
returns or throws, the events it raises (`.events`) and the log messages it writes (`.logs`).

* `subscribe.*`, `register.*`: the events and handlers subscribed when Play mode starts: an
  editor's header drawn, package imports, compilations, prefab reverts, the analytics build
  check, the tool context, the scene tools and the Build Settings window's build handler;
  `later.events` lists which of them fired from their subscription to 30 editor updates later
  (the probe's own tool queries included).
* `drop.*`: DragAndDrop drop handlers: the views' ids, the handlers registered at the start
  (`dropHandlers` by view), adding, adding again, removing, null, and HasHandler for each view.
* `tools.*`: ToolManager's tool and context types read first, then Tools.current, pivotMode and
  pivotRotation with the tool and context types, and the events each change raises, for every
  Tool value.
* `player.*`, `unity_type.*`: PlayerSettings.productGUID, and the case-insensitive class lookup.

Measured in the Unity 6000.0.71f1 Editor on Linux with an NVIDIA GPU, windowed (two
byte-identical captures). `run.py record` / `publish` / `gate` (tests/value_probe.py).

Recorded residuals (`residuals.json`): the Editor registers drop handlers of its own for the
project browser, the hierarchy and the inspector, which Unbound has no views for.
