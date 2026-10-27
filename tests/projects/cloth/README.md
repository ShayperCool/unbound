# Cloth probe

How Cloth moves: `ClothProbe` records the defaults and Unity's virtual particle weights, then
scenarios of a 1 m cloth of 9 x 9 vertices (hanging from its top row, or lying flat and free) at a
frame per fixed step: stiffnesses, damping, external acceleration, solver and stiffness
frequencies, tethers, max distance and collision sphere distance coefficients, sphere, sphere pair
and capsule colliders, draping on a sphere (friction, collision mass scale, virtual particles,
self collision), a moving, rotating, scaled and teleported transform, world velocity and
acceleration scales, enabling, fading, sleeping, a fast collider, a mesh with a seam, a mesh
skinned by a moving bone and the renderer's visibility settings — recording the cloth's vertices at
chosen frames and its normals at the last one.

Measured on NvCloth 1.1.6 (Unbound runs the same, see `managed/UnityEngine/Cloth/Cloth.cs`): the
cloth steps by the fixed time step once per physics step of the frame, after the scripts; the
particles are the mesh's vertices welded by position, in the transform's frame without its scale
(the root bone's for a mesh skinned by bones), max distance 0 fixes them; the fabric is cooked
with geodesic tethers; stretching stiffness drives the vertical and horizontal phases, bending
stiffness the bending ones, shearing stays at 1; world acceleration scale is the frame's inertia
(linear, angular, centrifugal), world velocity scale changes nothing measured; max distances are
motion constraints around the skinned pose, collision sphere distances separation spheres behind
it as large as the mesh's half diagonal; one virtual particle per triangle at its centre;
colliders in the cloth's frame (sphere pairs as cones); fading blends the vertices with the
skinned pose while the simulation goes on, and a cloth enabled again starts over; a cloth that does
not update offscreen waits for its first culling; normals are the normalized sums of the
triangles' unit normals.

Record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/cloth \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/cloth-create.log
    CLOTH_PROBE_OUT=$PWD/tests/projects/cloth/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/cloth \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/cloth.log

Run Unbound and compare:

    CLOTH_PROBE_OUT=/tmp/cloth unbound run -p tests/projects/cloth --headless 300000
    python3 tests/projects/cloth/compare.py tests/projects/cloth/Reference /tmp/cloth

Every record matches within 5 mm (most exactly) but three that swing far (a capsule, a fast sphere,
a moving bone), which match for their first 20 frames and end within 7 cm. A hanging cloth is at
its tethers' limits, so differences in the last bits of the arithmetic decide which tethers act and
grow from there; the probe's vertices sit on multiples of 1/8 so that the runtime's float rounding
(Mono computes float expressions in double, CoreCLR in float) cannot differ.
