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
* `frame/*`: a ConfigurableJoint on a body turned 90 degrees about y, free to slide 0.3 m along
  its x axis, pushed along world x and z: with `configuredInWorldSpace` the axes are world
  directions (it slides along world x), without it the body's own (along world z); `swapBodies`
  (anchor kinematic) moves the body as the unswapped joint does here.

Unbound passes projection to PhysX's own (the joint's projection flag, the distance and angle
as its linear and angular tolerances) and builds a world-space joint's frame from the axes as
world directions. Tolerance 1e-3. The strained chains are chaotic: their rotations differ from
the Editor's by about 1e-9 from the first step and the difference grows past the tolerance late
in the run (`residuals.json`, 49 values); the frame scenarios match exactly.

Not measured: drive targets with `configuredInWorldSpace` (the Scripting API says targets are
then in world space), what `swapBodies` changes (forces, drives, mass scales with two dynamic
bodies), projection on HingeJoint/FixedJoint (no such API).

    python3 tests/projects/jointprojection/run.py record --label L     # twice, then publish
    python3 tests/projects/jointprojection/run.py publish L1 L2
    UNBOUND=<binary> python3 tests/projects/jointprojection/run.py gate
