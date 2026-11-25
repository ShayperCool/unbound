# SpeedTree 8 wind

The wind constants SpeedTree 8 trees draw with, frame by frame, in the built-in render pipeline.
The tree is `Broadleaf_Desktop_Field.st`, one of the SpeedTree 8 sample trees of Unity's Graphics
test projects (`tests/projects/speedtree8` imports them). The samples are not part of the
repository: `run.py` stages `v8/` from `$UNBOUND_SPEEDTREE_SAMPLES`, else
`$XDG_CACHE_HOME/unbound/speedtree-samples` (`~/.cache` by default), into each run's private
project copy, and stops when they are not there (`gate --if-staged` prints a SKIPPED line instead).

`Assets/Scripts/SpeedTree8WindProbe.cs` plays at a captured 30 frames per second. Each probe tree
is a copy of the asset's first level (its Tree keeps the asset's wind asset) drawing a quad with
`Assets/Shaders/Wind8Dump.shader`, which writes the constant buffers `SpeedTreeWind` (the 15
`_ST_Wind*` vectors) and `SpeedTreeWindHistory` (each name with `History` after it) into a 30x1
float target; one orthographic camera per tree renders it every frame. `FieldA` draws camera
motion vectors (its current constants are recorded), `FieldB` (turned and scaled) per-object
motion vectors (its history too). The scene has no wind at first; then a directional zone comes
(frame 10), its main wind rises above 1 (50), it turns around (75), a second directional zone
under a mirrored, turned parent comes (105), a spherical zone far from every tree comes (120), and
all zones go (130); once the wind has settled a directional zone comes again (260), and a tree of
the same asset first draws in it (`FieldLate`, 300, per-object motion vectors: until it draws its
own wind it reads what an earlier draw left, so the probe records whether its wind vector is
`FieldB`'s, and its constants only when it is). At 53 frames between 1 and 360 the probe records
Time.time and each tree's constants (floats as the hex of their bits; values compare exactly),
and once `Tree.hasSpeedTreeWind` of the asset's first level.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, OpenGL Core, NVIDIA driver):
each wind asset keeps one wind its trees draw with. Its direction and strength ease toward the
scene's wind over the asset's responses, as a SpeedTree 9 wind's do; the strength picks a point
on the asset's 10-sample curves, and ten phases (the global wind, two branch levels, each leaf
group's ripple, tumble and twitch, the fronds' ripple) advance by their oscillation curves times
the time since the previous update. The constants hold each part's phase before this frame's
step, its curves' values at the strength and the asset's settings; the branch anchor is the
asset's anchor moved along the wind by the first branch level's length. The history constants are
the previous frame's, with the phases after this frame's step. `_ST_WindAnimation` is never
written.

    python3 tests/projects/speedtree8wind/run.py record --label A   # Editor capture (Linux, GPU)
    python3 tests/projects/speedtree8wind/run.py publish A B        # two identical captures
    python3 tests/projects/speedtree8wind/run.py gate               # the engine against Reference/

Not measured: spherical zones reaching a SpeedTree 8 tree (Unbound keeps the asset's wind for it
and says so once), gusts (the release never turns them on), the GPU Resident Drawer's SpeedTree 8
parameters (Unbound gives it the 15 constants in the SpeedTree 8 shaders' order and says so once).
