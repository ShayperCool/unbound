# Animation from an AssetBundle

An `anim` bundle the Editor built for StandaloneLinux64 (`ProbeRunner.Create`, committed under
`Reference/bundles`): clips in player form — `Move` (position, Euler rotation, scale, script fields
of every kind, a material's color, float and texture scale-offset channels, a GameObject's
activity, two events), `Spin` (quaternion rotation, looping), the legacy `Bob` — the `Rig`
controller (Idle at a speed parameter, Spin on a trigger, then after an exit time a 1D blend tree)
and the prefabs that play them. `AnimProbe` records the clips' properties, each clip sampled onto
the rig at chosen times (SampleAnimation), the rig's Animator frame by frame through its
transitions and events, and the legacy Animation, at a frame per fixed step.

Measured (see `managed/UnityEngine/Animation/PlayerClip.cs` and `CompiledController.cs`): a
player clip is a muscle clip — streamed curves as cubic segments between keys, dense curves as
samples, constant curves — bound by CRC32 hashes of paths and attributes (Transforms by code;
a material property's name hash in the low 28 bits and its channel above: x y z w r g b a, 8 for a
float); a compiled controller names everything by hash through its table of strings. Also: an
animated integer rounds half up, a blended bool is true while any curve keeps it from 0, material
curves write each channel into the renderer's property block (a channel's default is the
material's value; channels no curve drives stay 0 in the block), Euler curves drive the rotation as
a quaternion, isLooping is the clip's loop time even for legacy clips, a legacy state's first
update samples its start, and a transition leaving at an exit time starts where the exit time was
crossed (its destination and progress already as far as the update ran past it).

Make the bundle (once; it is committed):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/bundleanim \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/bundleanim-create.log

Record Unity's reference (the Editor needs a display):

    ANIM_PROBE_OUT=$PWD/tests/projects/bundleanim/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/bundleanim \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/bundleanim.log

Run Unbound and compare:

    ANIM_PROBE_OUT=/tmp/anim unbound run -p tests/projects/bundleanim --headless 300000
    python3 tests/projects/bundleanim/compare.py tests/projects/bundleanim/Reference /tmp/anim

Every record matches. The probe keeps its keys, steps and exit times off the frames' times: Unity's
animator time gathers rounding from frame to frame (a 0.02 s step advances 0.0199999865), so a tie
exactly on a frame resolves by the last bits of that sum.
