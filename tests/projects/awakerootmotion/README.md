# An exception in Awake; Animator.hasRootMotion

Unity 6000.0.71f1, recorded in Play mode with the macOS Editor in batch mode (two byte-identical
captures; nothing depends on the OS or the graphics device). The rigs and controllers come from
`tests/projects/modelroot` (Walker with root node Hips, WalkerNoNode, WalkerNoAvatar, Mover,
MoverAvatar) and `tests/projects/humanoid` (HumanA); `run.py` copies them in.

Documented (Scripting API 6000.0,
[MonoBehaviour.Awake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Awake.html)):
"If a script component's Awake throws an exception, Unity disables the component." The probe's
scripts (`Assets/Scripts/Callbacks.cs`) log every callback. Cases: a failing script added to an
active object beside a neighbour, then enabled again by a script; added to an inactive object that
is activated, deactivated and activated again; disabled before its object first activates, then
enabled; a copy of an object holding it; the script removed afterwards; and, as controls,
exceptions in OnEnable and Start (the component stays enabled). Each case records the component's
`enabled` and `isActiveAndEnabled`, the callbacks of the frame and the next frames, and the logged
exceptions.

Measured: the exception is logged and the script is disabled, which sends it OnDisable right after
its Awake (no OnEnable, Start or Update); its neighbours are unaffected; enabled again it gets
OnEnable, Start and Update and no second Awake; a script disabled before its first activation gets
only Awake; an object activated again does not enable it.

[Animator.hasRootMotion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-hasRootMotion.html)
("Returns true if the current rig has root motion") for each rig: after instantiation, with root
motion off and on, with its own controller, after `Update(0)` and a frame, with another rig's
controller (whose clips have no curves of its root), without a controller, disabled, with
another rig's avatar (and rebound), without an avatar, and with avatars `AvatarBuilder` builds for
the walker (root motion transform Hips, Leg, none, one that does not exist; and a humanoid one from
HumanA's description). Measured: it answers whether the Animator's avatar is a valid, non-human
avatar with a root motion transform, whatever the controller, `applyRootMotion` or the Animator's
state: true only for generic avatars built with a root motion transform found in the hierarchy;
false for the imported generic avatars (with or without a root node: the importer bakes a root
node's motion into the clips), humanoid avatars, avatars without a root motion transform, invalid
avatars and no avatar.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/awakerootmotion/run.py record --label native-a
python3 tests/projects/awakerootmotion/run.py record --label native-b
python3 tests/projects/awakerootmotion/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/awakerootmotion/run.py gate
```

Values compare exactly (tolerance 0).
