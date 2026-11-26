# SpeedTree 8 wind

The wind constants SpeedTree 8 trees draw with, frame by frame, in the built-in render pipeline.
The tree is `Broadleaf_Desktop_Field.st`, one of the SpeedTree 8 sample trees of Unity's Graphics
test projects (`tests/projects/speedtree8` imports them). The samples are not part of the
repository: `run.py` stages `v8/` from `$UNBOUND_SPEEDTREE_SAMPLES`, else
`$XDG_CACHE_HOME/unbound/speedtree-samples` (`~/.cache` by default), into each run's private
project copy, and stops when they are not there (`gate --if-staged` prints a SKIPPED line instead).

`Assets/Scripts/SpeedTree8WindProbe.cs` plays at a captured 30 frames per second. Each probe tree
is a copy of the asset's first level (its Tree keeps the asset's wind asset) drawing a quad with
`Assets/Shaders/Wind8Dump.shader`, which writes the constant buffers `SpeedTreeWind` (the 16
`_ST_Wind*` vectors the SpeedTree 8 shaders declare, `_ST_WindAnimation` last) and
`SpeedTreeWindHistory` (each name with `History` after it) into a 32x1 float target; one orthographic camera per tree renders it every frame. `FieldA` draws camera
motion vectors (its current constants are recorded), `FieldB` (turned and scaled) per-object
motion vectors (its history too). The scene has no wind at first; then a directional zone comes
(frame 10), its main wind rises above 1 (50), it turns around (75), a second directional zone
under a mirrored, turned parent comes (105), a spherical zone far from every tree comes (120), and
all zones go (130); the wind eases to rest (the tree's responses are 14 seconds: its strength
reaches 0 by frame 400, its direction stops by frame 550) and stays calm; long after, a directional
zone comes again (900), and a tree of the same asset first draws in it (`FieldLate`, 1000,
per-object motion vectors: until it draws its own wind it reads what an earlier draw left, so the
probe records whether its wind vector is `FieldB`'s, and its constants only when it is). That
zone goes once its direction and strength have arrived (1250): the strength comes to rest last,
at frame 1565, so the frames around it show the last windy frame and the first calm one. At 94
frames between 1 and 1650 the probe records Time.time and each tree's constants (floats as the hex of their bits; values compare exactly),
and once `Tree.hasSpeedTreeWind` of the asset's first level.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, OpenGL Core, NVIDIA driver):
each wind asset keeps one wind its trees draw with. Its direction and strength ease toward the
scene's wind over the asset's responses, as a SpeedTree 9 wind's do; the strength picks a point
on the asset's 10-sample curves, and ten phases (the global wind, two branch levels, each leaf
group's ripple, tumble and twitch, the fronds' ripple) advance by their oscillation curves times
the time since the previous update. The constants hold each part's phase before this frame's
step, its curves' values at the strength and the asset's settings; the branch anchor is the
asset's anchor moved along the wind by the first branch level's length. The history constants
take the previous frame's strength and direction with the phases after this frame's step. Once
the wind is at rest its state stays as it is: the calm frames draw the last windy frame's
constants, and a history at strength 0 (the strength is reset with the timers). When a wind comes back the timers have started over: its first step is over the whole
time since play started (a jump of every phase by its curve at strength 0 times that time).
`_ST_WindAnimation` is never written.

    python3 tests/projects/speedtree8wind/run.py record --label A   # Editor capture (Linux, GPU)
    python3 tests/projects/speedtree8wind/run.py publish A B        # two identical captures (--profile first: the same for the first-frame scene)
    python3 tests/projects/speedtree8wind/run.py gate               # the engine against Reference/

Further trees (Q769): `FieldNear`, which a spherical zone (frame 1010, wind 1.2 toward the tree,
radius 10) reaches until it is moved away (1100); `FieldBillboard`, a copy of the asset's billboard
(last) level; `FieldDirectional`, a copy of a staged variant of the tree (`Field_Directional.st`,
the sample's bytes with the wind options that make both branch levels' adherence directional
set; no sample sets them); `Calm9`, a SpeedTree 9 tree (`Calm.st9` and its dump, staged from
tests/projects/speedtree9wind; its unwritten first branch level is not recorded) and `Calm9Late`,
one that first draws (330) while only the SpeedTree 8 wind still eases. A global
`_ST_WindAnimation` is set at frame 2 and the dump material's at 1299: before a tree draws with
wind its raw value is recorded, afterwards only whether it is the global or material value. The
profile `first` (`--profile first`) plays `Assets/Scenes/FirstWind.unity`, whose directional zone
blows from the first frame, with FieldA and FieldB (frames 1 to 450). The profile `zones`
(`--profile zones`, `Assets/Scenes/ZonesWind.unity`, frames 1 to 1450) has FieldA, FieldB,
`FieldCalm` and `FieldLong`: a directional zone blows from 10 to 40 and the asset's wind eases to
rest; a spherical zone reaches FieldCalm from 520 to 640; a weak directional zone from 700 and a
strong spherical zone beside FieldLong from 710 to 1110, after which its phases return.

Measured besides (same Editor, two identical captures per profile): a SpeedTree 8 wind starts
along +x (a wind from the first frame turns from +x toward the scene's; the first update of a
windless scene rests it at once); the billboard level draws the same constants as the first; the
directional options multiply both branch levels' adherence by the first level's length (in the
imported units; the staged copy also gives the second level an adherence curve, the sample's
first level's halved); a tree that a spherical zone reaches gets a wind of its own, as a SpeedTree 9
tree does, whose phases return to its asset's wind's out of reach (each phase on its own: one
whose return would take over 15 seconds jumps, the others ease); a tree that a spherical zone
reaches while its asset's wind rests starts from rest (+x, its phases at zero); a SpeedTree 8 wind still easing
keeps the world windy for a SpeedTree 9 tree that first draws; `_ST_WindAnimation` reads the
global value until the tree draws with wind and then neither the global nor the material value
(the engine's buffer holds something else there, not repeatable). The imported variant's children
come in name order in the Editor (its collision objects first), as tests/projects/speedtree8 measures.

Not measured: gusts (the release never turns them on), the directional options set one at a time
(the staged copy sets both; Unbound scales each option's own level), palm wind (no sample uses it; its
turbulence reads `_ST_WindAnimation.x` as its time, which Unbound binds as zero once a tree draws
with wind), a local wind becoming its asset's wind again (no capture reaches it: Unbound gives it
up when every phase, current and previous, is within 5e-4 of the asset's), the GPU Resident Drawer's SpeedTree 8
parameters (Unbound gives it the 15 constants in the SpeedTree 8 shaders' order and says so once).
