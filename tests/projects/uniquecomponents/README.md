# uniquecomponents

Which engine component types a GameObject holds only one of, in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/UniqueComponentsProbe.cs`; two byte-identical macOS batch Editor captures; the
project has every engine module). For every public, concrete component class of the engine's
modules (scripts and Transforms aside) a new GameObject gets it twice through
`GameObject.AddComponent`; the row says whether the first add worked, whether the second came
back, and what the second logged (the GameObject's name as NAME).

Measured: 61 types (cameras, lights, renderers, rigidbodies, animators, ...) refuse a second
component with the log "Can't add component 'X' to NAME because such a component is already added
to the game object!"; a second 2D effector logs "... because it conflicts with the existing 'X'
derived component!"; colliders, joints, AudioSource, VideoPlayer, Skybox and OffMeshLink take
several; base classes and the audio filters are not added at all in this setup.

```sh
python3 tests/projects/uniquecomponents/run.py record --label a
python3 tests/projects/uniquecomponents/run.py record --label b
python3 tests/projects/uniquecomponents/run.py publish a b
python3 tests/projects/uniquecomponents/run.py gate
```
