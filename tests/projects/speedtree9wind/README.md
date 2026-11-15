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
is at rest (700: its local wind starts from rest). The earlier rows of the reference are unchanged.

    python3 tests/projects/speedtree9wind/make_trees.py              # rewrite the trees
    python3 tests/projects/speedtree9wind/run.py record --label A     # Editor capture (GPU)
    python3 tests/projects/speedtree9wind/run.py publish A B          # two identical captures
    python3 tests/projects/speedtree9wind/run.py gate                 # the engine against Reference/
