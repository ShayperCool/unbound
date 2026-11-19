# SpeedTree 9 wind

The wind constants SpeedTree 9 trees draw with, frame by frame, under the Universal Render
Pipeline 17 (the pipeline asset and renderer of `tests/projects/speedtree9urp`). Two trees written
by `make_trees.py` with `tests/projects/speedtree9`'s writer: `Gusty` turns every wind part on and
asks for a gust on almost every frame; `Calm` keeps the shared bend, the second branch level and
the ripple, with other curves, imported at scale 2. Their .meta files set other wind responses
and randomness.

`Assets/Scripts/SpeedTreeWindProbe.cs` plays at a captured 30 frames per second. Each probe tree
is a copy of an asset's first level (its Tree keeps the asset's wind asset) drawing a quad with
`Assets/Shaders/WindDump.shader`, which writes the constant buffers `SpeedTreeWind` and
`SpeedTreeWindHistory` into a 22x1 float target; one orthographic camera per tree renders it every
frame. `GustyA` draws camera motion vectors, `GustyB` (the same asset, turned and scaled) and
`Calm` per-object motion vectors. The scene has no wind at first; then a directional zone comes
(frame 10), its main wind rises above 1 (50), it turns around (75), a second directional zone
under a mirrored, turned parent comes (105), a spherical zone far from every tree comes (120),
and all zones go (130); once the winds have settled the spherical zone comes back alone (340),
then a directional zone without wind (350), then a directional wind again (360); from frame 400
the same wind is drawn through the built-in render pipeline. Trees also first appear late: a
`Gusty` one once the winds have settled (300), another just before the spherical zone comes
alone (335), a `Calm` one under the zone without wind (351) (the second draws after the first,
of the same asset, so the first decides the spherical zone's case); until such a tree draws its own
wind it reads what an earlier draw left, so for it the probe records whether its extents are
those of an earlier tree of its asset, and its constants only when they are. At 49 frames
between 1 and 420 the probe records Time.time, the pipeline and each present
tree's constants (floats as the hex of their bits; values compare exactly), and once each
`Tree.hasSpeedTreeWind` of the asset's two levels and of an added Tree.

Measured in the Unity 6000.0.71f1 Editor (two identical captures, OpenGL Core, NVIDIA driver): the
Editor binds the constants as constant buffers per tree (a renderer's property block shows no
wind vectors), first when the tree draws while the world has wind (a directional wind, a
spherical zone or a wind still moving; not a zone without wind), and keeps them bound; `SpeedTreeWindHistory` only for a renderer drawing per-object motion vectors (for
`GustyA` it holds what an earlier draw left, so its history is not recorded); the constants of a
wind part the asset turns off are never written (they held other memory in a built-in pipeline
capture), so `Calm`'s first branch level is not recorded.

From frame 425 spherical zones reach trees (frames 425 to 780, still through the built-in
pipeline): one reaches `GustyA` (frame 425) and is moved out of its reach (490), another reaches
`GustyB` (500), then the directional zone goes (540), so `GustyB`'s wind is the spherical zone's
alone, and both spherical zones go (580). A tree in reach gets a wind of its own (a local wind),
which starts from its asset's wind at its first draw while the zones reach it and eases toward
the directional wind plus the spherical zones' wind at the tree; out of reach its noise returns
to its asset's wind's and it takes its asset's wind again. The draw takes the zones active when it
draws, as long as a spherical zone was active at the frame's wind update (`GustyB` gets its local
wind in the frame its zone is made). Controls: a whole tree (`GroupLevel1`, its LOD group held at
its second level) under a zone from frame 440 (the draw starts the local wind on the group's first
level, and the drawn level joins it at its next draw), the zone out of reach (470), back in reach
while the trees return to the asset's wind (474) and out again (480); a tree that first draws
(650) while no zone is active but trees still hold winds of their own (it gets its constants: those
winds count as wind in the world); a spherical zone reaching `LateSettled` while its asset's wind
is at rest (700: its local wind starts from rest); a whole tree drawing its first level with its
second level's renderer moved 6 units along x (`SplitGroup`, frame 790) and a spherical zone that
reaches only that second level's bounds (800): the drawn level gets a local wind, so a group's trees
are reached by their levels' bounds together, levels it does not draw included; beside it a tree
(`Cancelled`, 790) that two spherical zones at one place with main winds 1 and -1 reach (810): their
winds cancel exactly at the tree and it gets no local wind (it keeps its asset's wind).

From frame 870 (to 1100): `Opposed`, a tree that a directional zone (875, main wind 1 along +z) and
a spherical zone 5 units along +z of it (880, main wind 1) reach, so the spherical wind cancels
the directional one exactly at the tree, with its twin `OpposedTwin` (another tree of its asset,
out of reach) as the control, and `Windless`, a tree whose wind asset is set to none. `Opposed`
gets a local wind with no direction: its wind vector is NaN (and the NaN reaches the noise
positions) while its strength eases as for any local wind. The zone then moves 3 units along x
(950), still reaching the tree, so the winds no longer cancel, and goes (1000): the local wind's
strength follows, but its direction and noise positions stay NaN, and it never becomes the
asset's wind again (to frame 1100). `Windless` binds no wind: its draw reads what the draw before
it left (here exactly `Opposed`'s constants); Unbound draws it without wind constants (zeros), a
recorded residual (`residuals.json`). Two identical captures (OpenGL Core, NVIDIA driver, 25195
rows); the earlier 16252 rows are unchanged. On arm64 a NaN that
Unbound computes keeps the default NaN's positive sign (`7fc00000`, x86 gives `ffc00000`), so
on the Mac the gate also lists `Opposed`'s NaN rows.

Not measured: the order in which the trees of a local wind that has become its asset's wind
again leave it (the recorded constants are the same whichever leaves first), and the constants
outside Play mode (the probe records Play mode only), and a local wind whose winds come to
cancel exactly after it has started with a direction (`Opposed` cancels from its start).

    python3 tests/projects/speedtree9wind/make_trees.py              # rewrite the trees
    python3 tests/projects/speedtree9wind/run.py record --label A     # Editor capture (GPU)
    python3 tests/projects/speedtree9wind/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree9wind/run.py gate                 # the engine against Reference/
