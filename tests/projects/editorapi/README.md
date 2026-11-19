# Editor APIs in Play mode

The Unity 6000.0.71f1 reference contains 253 exact rows from two independent windowed
GPU Editor captures. Both exited normally on the NVIDIA GeForce RTX 4060. The probe
records all requested event subscriptions, 30 idle frames, queries, constructors,
disconnected messaging, thread checks, reflected client requirements and an active Undo
control. No crash is requested. Messaging controls stop if a peer is connected.

The comparison retains every missing, extra and differing row. Three exact differences
are named in `Reference/unity-6000.0.71f1/known-gaps.json`: two rows describe the missing
toolbar UI, and one records missing Undo change-stream publication.
A changed actual value, changed reference value, missing row, extra row or improved queued
row fails until its gap is reviewed. `report.json` keeps the full raw comparison alongside
queued, unexpected and stale entries. Nothing is removed from the reference comparison.

## Controls and scope

- All 19 events accept duplicate subscriptions and removal without diagnostics and stay
  quiet during the recorded idle frames. The active control changes a transform after
  `Undo.RecordObject`, flushes the record and receives one `ObjectChangeEvents` callback
  in the Editor. Unbound retains real subscribers and explicitly reports unsupported Undo
  payload production when a listener is present. It does not synthesize an empty stream.
  The opaque type retains the measured 96-byte storage and eight-byte alignment. A complete producer, payload lifetime and accessor implementation remain a follow-up.
- Editability uses package metadata. The built-in package file, its loaded TextAsset,
  directory and metadata file are non-editable; an embedded package is editable. A
  read-only scene file, missing files and paths outside asset folders remain editable
  without version control. Bulk queries clear stale results and validate null arguments.
  Nonempty worker-thread queries fail before processing package state; empty paths return
  true. Active version-control providers remain explicitly unsupported.
- Menu checkmarks are stored independently of Play state. Existing project commands and
  Play/Pause/Step entries accept changes; unknown, null and empty paths stay unchecked.
  A project command registered with a hotkey also executes through its ordinary menu path.
  Get/Set and command execution reject worker calls without changing state.
  The registry now also owns dynamic commands, validation and parent menu entries.
  Removing a parent removes its descendants; removing a leaf retains its parents.
  Duplicate dynamic registration updates the checkmark and retains the original callbacks.
  Script validators bind to the full attribute registration string, including shortcut text.
  A fresh throwing validator reports its exception and returns disabled. Other built-in
  Editor commands and context commands remain outside the supported command registry.
- Case-sensitive type-name lookup reuses the complete 333-class table already recorded by
  `tests/projects/classids`, with an exact release selection through Compat. Names and
  persistent IDs remain available for classes without an importer. Unknown names return
  null and null input throws. Other metadata queries remain explicit stubs.
- Importer and settings constructors produce uncreated wrappers unless supplied with a
  backing object through the existing construction path. Inspector repaint has no drawing
  work in this host. The sprite callback setter retains its previous callback for null.
- PlayerConnection keeps a singleton, disconnected subscriptions and UnityEvent listener
  semantics. Registration of a new message ID checks the main thread before storing it;
  adding a listener to an existing ID stays managed and is allowed on a worker. A null
  listener added there changes the later unregister result. Connected-state queries,
  sends, unregister transitions and disconnect reject worker calls with the measured
  validation order. Empty-event null removal is also checked directly. Attached-peer
  transport and receive polling remain unsupported.
- EditorConnection reports an empty connected-player list without a player. Its subscriptions
  use persistent UnityEvents, so ordinary CLR instance targets are rejected while Unity
  objects and static methods work. Duplicate message callbacks remain persistent after one
  unregister, and disappear when the message membership count reaches zero. Controls invoke
  the incoming message entry with a payload and player ID. Existing-player replay is checked
  by changing the public list before registration. Runtime callbacks use the same message
  membership helper with ordinary listeners, and retain their effective removal behavior.
  The shared persistent event controls exercise static calls, missing stored target-type
  recovery, destroyed targets, and RuntimeOnly versus EditorAndRuntime in Edit mode.
  Sends, registration transitions and initialization retain their measured thread checks.
  Attached-peer transport and receive polling remain unsupported.
- Memory snapshot metadata stores its description, and `CreatingMetadata` retains
  subscribers. Snapshot production remains unsupported. Developer queries answer for the
  measured retail Editor profile.

## Contracts and observations

The release's [editability documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.IsOpenForEdit.html)
distinguishes non-local packages, version-controlled paths and paths outside asset folders.
The package, permission, bulk-list and outside-path controls change the results where those
rules apply. [Menu.SetChecked](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Menu.SetChecked.html)
sets a menu checkmark; the custom command controls write both states and execute the command.
[PlayerConnection](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Networking.PlayerConnection.PlayerConnection.html)
describes the singleton and message subscriptions. The no-peer and worker-thread details
here are measured observations. The documented
[ObjectChangeEvents callback](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ObjectChangeEvents-changesPublished.html)
publishes undoable changes once per frame and lends the stream only for callback duration.
The active divergent-state control (`event.undo.published`) now matches: an undo publishes one stream.
[CreatingMetadata](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.Memory.MemoryProfiler.CreatingMetadata.html)
allows subscribers to supply a description when a snapshot is taken; this probe tests storage
and subscription, and does not request a snapshot.

The client controls confirm that the Editor has `CustomEditorAttributes.instance`, `Rebuild`,
`m_Cache`, its typed nested cache/storage entries and `CustomEditor.m_EditorForChildClasses`.
The toolbar has an `m_Root` VisualElement with `ToolbarZoneLeftAlign` and
`ToolbarZoneRightAlign`. The registry owns inspector selection, accepts reflected insertions and resets them on rebuild.
Seventeen added controls cover direct and inherited editors, regular editors before fallbacks,
multi-object editing, constructed generic targets, null/unregistered targets, dynamic
registration that changes `Editor.CreateEditor`, and rebuild removing that registration.
Additional controls exercise a fallback without a regular match, its exclusion from multi-object editing,
unsupported render-pipeline inspectors under the built-in pipeline, non-inherited pipeline restrictions,
and competing script inspectors.

Toolbar UI lifecycle remains a follow-up. The existing game-window view has no Editor
panel drawing or IMGUI container callback dispatch. A root with zone names alone would
accept registrations without running their callbacks. Panel ownership, attach/detach,
layout and IMGUI drawing must be integrated together.

From the repository root:

```sh
python3 tests/projects/editorapi/run.py editor --release 6000.0.71f1 --label native
python3 tests/projects/editorapi/run.py publish --release 6000.0.71f1 --label native
python3 tests/projects/editorapi/run.py unbound --release 6000.0.71f1 --label engine
python3 -m unittest discover -s tests/projects/editorapi -p test_compare.py
```

The shared runner stages authored files in the validation cache, validates GPU completion
and independent repeat equality before publishing, and captures the engine with an immutable
runtime snapshot. Exception records keep argument exceptions' whole messages (in the
Editor's layout, the parameter name on its own line) and parameter names, and record a null
reference by its type alone. Other releases need their own controls and exact class tables.

The [CustomEditor contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CustomEditor.html)
selects the inspected type and tries regular editors before fallback editors. The inherited
regular versus exact fallback control makes that guarantee affect the result.
[Editor.CreateEditor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Editor.CreateEditor.html)
uses the matching registration when no explicit editor type is supplied. The dynamic
registration control starts from an unregistered target and verifies the resulting editor.

[Menu.GetEnabled](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Menu.GetEnabled.html)
reports command availability. The validator controls toggle the result and verify that disabled
commands do not execute. [UnityEventCallState](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Events.UnityEventCallState.html)
distinguishes RuntimeOnly from EditorAndRuntime. The Edit-mode control invokes the same listener
before and after changing that state, and records `0:5`.

The Play/Pause/Step availability controls cover ordinary Play mode on 6000.0.71f1.
`Compat.EditorPlayMenusMeasured` selects that context. Other releases and availability
queries outside Play mode remain explicitly unsupported. Broader menu construction
phases and notifications caused by pause changes remain follow-ups.

Throwing script and dynamic commands log their exceptions and retain a successful
execution result. Open generic commands and validators are ignored; they do not prevent
other commands from being discovered. Non-void script commands execute with an ignored
return value. The command cache becomes current after construction completes.
