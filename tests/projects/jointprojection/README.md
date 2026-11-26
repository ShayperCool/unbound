# jointprojection

Joint projection and the configurable joint's frame options, measured in the Unity 6000.0.71f1
Editor (`Assets/Scripts/JointProjectionProbe.cs`; two byte-identical Linux batch Editor captures
in `Reference/unity-6000.0.71f1/`). Every fifth physics step for 2 s (0.02 s steps), each body's
position and rotation:

* `configurable/*`, `character/*`: a kinematic anchor and four links 1 m apart along +x, the
  last 200 times as heavy, each jointed to the one before at its near end, swinging down from the
  horizontal. ConfigurableJoints (a hinge about z: angular x free, the rest locked) with
  `projectionMode` None, PositionAndRotation at the defaults (0.1 m, 180 degrees) and tight
  (0.01 m, 5 degrees); CharacterJoints with `enableProjection` off, on at the defaults and tight.
  Without projection the heavy link pulls its joint apart (5.98 m between the last two links at
  2 s in the Editor); with it the chain holds (1.09 m, and 1.00 m tight).
* `configurable/distanceonly`, `configurable/angleonly`: the configurable chain projected by
  distance alone (0.01 m, 180 degrees) and by angle alone (1000 m, 1 degree).
* `frame/*`: a ConfigurableJoint on a body turned 90 degrees about y, free to slide 0.3 m along
  its x axis, pushed along world x and z: with `configuredInWorldSpace` the axis and secondary
  axis are world directions (`frame/world` slides along world x, `frame/worldaxis`, axis along z,
  along world z; `frame/worldturnedanchor`, the connected body turned instead, along world x),
  without it the body's own (`frame/local*`); drives toward a target position (`frame/*drive`)
  and rotation (`frame/*rotdrive`) act in that frame. `frame/pair` and `frame/pairswapped`: a
  joint between two dynamic bodies with a drive and a limit, and the same with `swapBodies`:
  swapped, the solver takes the connected body as the joint's first body, which moves the pair
  slightly otherwise (1e-3 by step 20).

Unbound passes projection to PhysX's own (the joint's projection flag, the distance and the
angle in radians as its linear and angular tolerances), builds a world-space joint's frame from
the axes as world directions, and for `swapBodies` makes the connected body the native joint's
first actor (frames and mass scales with it). Tolerance 1e-3. The strained chains are chaotic:
the Editor's own chains change by 1e-16 to 1e-9 from the first steps when other bodies are added
to the scene, and Unbound's differ from the Editor's by about 1e-9 in the rotations from the
first step, growing past the tolerance late in the run (`residuals.json`, 59 values); every
frame scenario matches.

Not measured: `currentForce`/`currentTorque` of a swapped joint, the obsolete
`JointProjectionMode.PositionOnly` (reported, not projected), projection on HingeJoint and
FixedJoint (no such API).

    python3 tests/projects/jointprojection/run.py record --label L     # twice, then publish
    python3 tests/projects/jointprojection/run.py publish L1 L2
    UNBOUND=<binary> python3 tests/projects/jointprojection/run.py gate
