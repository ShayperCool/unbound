# Internal runtime roots

The Unity 6000.0.71f1 control renders a realtime reflection probe, waits for its
render id to finish, and runs live particles with the Lights module. Before and
after those operations it records the active scene's root count, a new root's
sibling index, and root enumeration length. An ordinary scene root is the control.
The completed captures have 15 exact values each.

The Editor uses disabled domain reload for the Play-to-Edit transition. Its
reflection camera remains alive and appears in Resources.FindObjectsOfTypeAll
after stopping Play. Unbound therefore keeps internal detached roots registered
in Edit mode and destroys them at final process shutdown. Particle light holders
are also destroyed by their owning particle system. Other reload options are not
covered by this capture.

Unity 6000.0 documents [Scene.rootCount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.Scene-rootCount.html)
as the scene's number of root transforms and
[Transform.GetSiblingIndex](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform.GetSiblingIndex.html)
as the sibling position. [ReflectionProbe.RenderProbe](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ReflectionProbe.RenderProbe.html)
returns an id that can be checked for completion. Those APIs do not specify the
ownership or Play-exit lifetime of internal rendering helpers; these are measured
observations for this release and context.

A separate diagnostic sidecar retains the ordinary scripted HideAndDontSave
root's scene validity and lifetime. That public flag transition is an open gap
in COMPATIBILITY_GAPS, distinct from the internal creation paths checked here.
No prior reference or tolerance is changed.

```
python3 tests/projects/hiddenroots/run.py editor --release 6000.0.71f1 --label fresh
python3 tests/projects/hiddenroots/run.py publish --release 6000.0.71f1 --label fresh
UNBOUND_CLI=/path/to/immutable/unbound python3 tests/projects/hiddenroots/run.py check --release 6000.0.71f1
```

The runner creates private projects, enables the built-in Particle System module,
and records two independent normal-exit GPU Editor captures before publication.
Gate runs use the committed reference and start only Unbound.
