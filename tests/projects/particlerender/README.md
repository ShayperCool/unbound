# particlerender

What a particle system's renderer bakes and how its culling mode updates it, in Unity 6000.0.71f1.
Systems built in code; each record is one JSON line `{"key", "value": [numbers]}`:

- `proc/*`: `proceduralSimulationSupported` with one setting changed at a time, before and after
  `Play` and after a step, and after a property set while playing, a stop and a new `Play`.
- `bake/*`: `BakeTexture` and `BakeTrailsTexture` for vertex streams, options, render modes, an empty
  system and textures passed in: the return value, whether the texture passed in is kept, and each
  texture's size, format and texels (`GetPixels`; for trails only the baked part, the rest is the
  Editor's scratch memory). `bakesize/*`: the sizes for several capacities, counts and meshes.
- `stretch/*`: the mesh `BakeMesh` builds for Stretched Billboard particles (length and velocity
  scales, a negative length, freeform stretching, rotation with the stretch, pivot, 3D size, minimum
  and maximum size, flip, a rotated non-uniformly scaled emitter, world space, sort modes) from a
  front and a side camera (aspect fixed at 4:3), and images.
- `camvel/*`: Stretched Billboard bakes from a camera moving at a constant velocity, and its
  `Camera.velocity`.
- `cull/*`, `hidden/*`: systems played over frames at a fixed capture delta for every culling mode
  (looping, one-shot, non-procedural), while one disabled camera rendered by the probe (none for
  frames 0-4) looks at them for frames 5-14, away for 15-39 and back; `hidden/*` sit on a layer the
  camera never draws; one system is paused by script while culled, one played again.
- `sheet/*`: Texture Sheet Animation in random modes (frame over time two constants and two curves,
  random start frame, random row), read from the baked quads' texture coordinates.
- `meshrot/*`, `meshattr/*`: mesh particles with a 2D and a 3D rotation for every render alignment
  in a rotated emitter, baked (BakeMesh and BakeTexture) from the front, side and a rolled camera;
  BakeTexture without an indices texture; the vertex attributes and texels of default-stream bakes
  of a cube and a quad.
- `bakecap/*`: BakeTexture sizes for 17 to 300 particles, and a one-particle bake after a
  300-particle one on the same and on a new system.
- `freemove/*`, `freeplay/*`: every freeform stretch case with all particles moving and with a
  particle at rest emitted first or second (Simulate), and played at simulation speed 0.
- `firstvis/*`: per frame, after the particle update, whether systems just played, emitted into,
  instantiated (Play On Awake), simulated, paused by script, at simulation speed 0, empty in and
  out of view, or seen and then emitted into or played again are visible, starting in a frame that
  moves time and in frames at timeScale 0.
- `subcull/*`: parents out of view (or seen, then moved out of view) with a Birth sub-emitter whose
  world-space particles fly into view, for parent and child culling modes.
- `birthstart/*` (`ParticleRenderProbe.Controls.cs`): per frame, each system's count, time, isPlaying,
  isEmitting, isPaused and sorted particle ages, also right after creation and after Play: Birth
  children of played parents at frames of 0.02 and 0.05 s, a child outside the parent's hierarchy or
  made before it, a delayed parent, a parent without particles of its own (emitted into later),
  child bursts, a stopped parent, Play On Awake, prewarm, Simulate, parents dying with a birth due
  exactly at, inside and one step before their life's end, and Death, Collision, Trigger and Manual
  children (alone, together, beside a Birth child, under Collision or Trigger modules, looping).
- `freenorm/*`: one freeform stretched particle per system (lengths, directions, rotations with and
  without rotateWithStretchDirection, 3D size, a pivot, an off-axis particle; plain stretching as a
  control) and two in transformed emitters (rotated, uniformly and non-uniformly scaled, each scaling
  mode, world space): the particles, and BakeMesh positions, normals and tangents (a Tangent
  stream) from the front and side cameras, and with BakeRotationAndScale.
- `meshscale/*`: mesh particles (an asymmetric mesh, 3D rotations and sizes) of a rotated,
  non-uniformly scaled emitter under a scaled parent, for each scaling mode, simulation space and
  View, World and Local alignment, baked plain and with BakeRotationAndScale.
- `meshindex/*`: BakeMesh and BakeTexture of mesh particles below and above 65535 baked vertices with
  16- and 32-bit particle meshes: index format, counts and index digests.
- `prewarm/*` (`ParticleRenderProbe.Prewarm.cs`): prewarmed looping parents (one particle a loop moving
  at 1 unit/s, or a rate) without and with sub-emitters, right after Play and after two frames of
  0.05 s: every particle's age, position and seed; parent lifetime and duration, child lifetimes
  (constant, two constants, 6 s), a grandchild, a Death child, child bursts, a looping child.
- `birthend/*`: Birth children, played and simulated at 0.02 s, of a non-looping child whose duration
  the parent outlives (rate × duration whole) and of children with a disabled Emission module.
- `inherit/*`: Birth (constant and curve parent lifetimes), Death, Collision and Trigger children
  inheriting colour, size, rotation, lifetime or duration from parents with colour, size and rotation
  over lifetime, children with a start size curve, a three-level chain and a looping parent: each
  child's start colour, size, rotation, lifetimes and position by seed.
- `collide/*` (`ParticleRenderProbe.Collision.cs`): Collision module contacts and bounces at 0.02 s,
  every particle each frame (position, velocity, total velocity, remaining lifetime). World: a sloped
  box (size over lifetime, radius scale, bounce, dampen, lifetime loss, gravity) at High, Medium and
  Low quality, a sphere, a plane under particles with an animated velocity and a speed modifier,
  particles starting inside a box (with their collision events), uniformly and non-uniformly scaled
  Local systems, kill speeds with a speed modifier. Planes: particles starting behind a plane moving
  towards or away from it, two planes reached in one step, a sloped plane, scaled Local systems.
  `collide/trigger/size`: a Trigger module killing fast-shrinking particles on Enter.

    python3 tests/projects/particlerender/run.py native --label native1   # Editor capture (GPU, job pool)
    python3 tests/projects/particlerender/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particlerender/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1/` (`values.jsonl`, 12 images); both captures were
identical (re-recorded with the `birthstart`, `freenorm`, `meshscale` and `meshindex` controls, then
with `prewarm`, `birthend` and `inherit`, then with `collide`: every earlier record and image
unchanged). Values compare with particletrails' comparator (0.0002 absolute plus 0.0002 relative);
images go through `particles/compare_images.py` (report only). Run output stays under
`$XDG_CACHE_HOME/unbound/validation/runs/particlerender/`.

## Documented contract (Unity 6000.0 Scripting API)

| Source | What it says |
| --- | --- |
| [ParticleSystemRenderer.BakeTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemRenderer.BakeTexture.html) | "Creates a snapshot of ParticleSystemRenderer and stores it in a Texture2D"; returns "the number of indices used by the Particle System". Nothing about the layout or format. |
| [MainModule.cullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-cullingMode.html), [ParticleSystemCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemCullingMode.html) | Automatic: looping effects pause offscreen, one-shot ones keep playing; Pause; PauseAndCatchup "perform[s] an extra simulation when the system comes back onscreen"; AlwaysSimulate. |
| [ParticleSystem.proceduralSimulationSupported](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-proceduralSimulationSupported.html) | Limit Velocity over Lifetime and "modifying any properties from script while the system is playing" disable procedural mode. |
| [MainModule.prewarm](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-prewarm.html) | A looping system starts "as though it had already emitted particles from a previous cycle"; nothing about how. |
| [Sub Emitters module](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysSubEmitModule.html) | Birth children come "throughout the parent particle's lifetime"; the Inherit properties pass the parent's colour, size, rotation, lifetime or duration; nothing about timing or start values. |
| [Camera.velocity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-velocity.html) | "Get the world-space speed of the camera". |
| [ParticleSystemRenderMode.Stretch](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemRenderMode.Stretch.html) | "Stretch particles in the direction of motion." |

Everything below is measured in this release, not documented.

## Measured behaviour

| Records | 6000.0.71f1 |
| --- | --- |
| `proc/*` | True until a playback starts; then false with World or Custom simulation space, a rate over distance, a non-constant gravity modifier, orbital or radial velocity, Limit Velocity, randomized Force over Lifetime, Rotation by Speed, External Forces, Noise, Collision, Trigger, Sub Emitters or Trails enabled, or a stop action; false after a property set while playing until a new playback. |
| `cull/*`, `hidden/*` | Offscreen means the renderer is not visible (a camera's culling makes it visible at once; it turns invisible early in the frame after the first frame no camera took it). A playback's first two updates simulate whatever the visibility. Automatic culls a looping procedural system as Pause and simulates the others; Pause and PauseAndCatchup cull non-procedural and stretched systems alike. A culled update holds time, particles, isPlaying and isPaused. PauseAndCatchup catches up the missed time (and the second update's) in its first visible update as one advance with its own time; a Play call (also a resume, or on a playing system) drops the time missed before it. |
| `bake/*`, `bakesize/*`, `bakecap/*`, `meshattr/*` | The vertices texture is RFloat: the vertex buffer, the active streams packed in order (position, normal, tangent, color as four bytes in one texel, the other streams), every vertex (PerVertex) or one record per particle (its first vertex; per trail point; a mesh particle's 3×3 transform, centre and color). Mesh particles without custom streams take the mesh's layout: its tangents and its second uv as a four-float TexCoord1 join the record (BakeMesh keeps the same attributes). The indices texture holds the triangle list as floats; with IncludeParticleIndices (particles) it is RGFloat with each particle's own indices (0 1 2 2 3 0) and its index; PerParticle without it holds one particle's indices. Sizes leave room for the records, at least as many as the most particles the system has held (a later smaller bake on it keeps that room), rounded up to a multiple of 64, plus one (indices: multiples of 128), rows of whole records. A texture passed in is kept in its format and reinitialized only when too small. Nothing to bake returns 0. Mesh particles without an indices texture throw. |
| `meshrot/*` | Mesh particles with only a z rotation turn about their axis of rotation (forward for births; a system switched to Mesh mode gives the particles it holds up, as in bake/mesh), those with 3D angles by Euler angles, for every alignment and camera, drawn and baked alike. |
| `stretch/*`, `camvel/*` | The quad trails back from the particle along its velocity less the camera's velocity × cameraVelocityScale, length size.y × lengthScale × the emitter's x scale + speed × velocityScale (negative: forwards), size.x wide across the stretch's direction on screen in the camera's plane (scaled along that axis); corners head and tail on one side, then tail and head on the other, u from 0 at the head; the normal is the quad's, towards the camera; at rest the quad collapses onto the particle. The pivot moves it by pivot.x sizes along the stretch and pivot.y twice the size across. Min and max particle size clamp the width only, as a fraction of the viewport's width. Oldest/youngest in front sort by the fraction of the life lived. `Camera.velocity` is the movement since the camera last rendered over the frame's delta time. |
| `freemove/*`, `freeplay/*`, `camvel/*/freeform` | Freeform: a quad of the particle's size facing the camera's position (right across the camera's up and the direction to the camera), turned by the particle's rotation plus, with rotateWithStretchDirection, the stretch direction's angle in that plane, moved by pivot.x sizes along its right, pivot.y along its up and pivot.z away from the camera, then scaled along the stretch direction by length over size.y; normal and tangent as in `freenorm/*`. A particle at rest bakes NaN corners with a +Z normal; BakeMesh is empty when the last baked vertex is NaN (the at-rest particle drawn last), not when an earlier one is. |
| `firstvis/*` | Culling bounds are the particles' (the emitter point without one). Frames with zero frame time keep the last bounds: a seen system stays seen after an Emit or Play (shown_*), one whose particles are all out of view stays unseen though its emitter is in view (stale). A system that never had bounds (played or emitted by script, never updated while time moved, never rendered) has nothing to draw after a zero-time update: invisible at once and until an update whose frame time is positive (at any simulation speed) or a Simulate. Play On Awake and a render before any update give bounds (tests/projects/resources mask_particles). A playback's two grace updates are updates that move time. Play restarts a playing system whose emission module is off, keeping its particles (shown_play; an emitting one plays on, cull/catchup_replay). |
| `subcull/*` | A sub-emitter updates while its parent updates or its own renderer is seen, whatever its own culling mode: an unseen AlwaysSimulate child freezes with its culled parent; a seen child runs on after its parent left the view. |
| `birthstart/*`, `subcull/*` | Every sub-emitter (Birth, Death, Collision, Trigger, Manual) plays, without emitting, after each update of its playing parent, from the parent's first update (not at Play), with a start delay and without parent particles too, whatever the update order; it is not playing again before the next update (tests/projects/subemitters played/*) and keeps its time; a culled parent no longer starts it. Birth children of played frames are as old at the frame's end as they were due before the end of the parent's internal step (a later internal step of the frame does not age them); a rate reaching a whole particle exactly at a step's end comes in the next step, as in Simulate; a parent emits no Birth child in the step its life ends. Collision and Trigger children, like Death children, are emitted after every system has updated (age 0 at the frame's end). |
| `prewarm/*` | One simulation call of the longest start lifetime plus the longest lifetime down each sub-emitter chain, in equal steps of at most Time.maximumParticleDeltaTime, its clock starting at the duration less that span's remainder so it ends on a loop's end, where the reported time stays (the duration, or just past the loop's start). Sub-emitter children (Birth, Death) are kept only when the call time left at their step's start is below the child's longest start lifetime, and are not aged by the rest of the call; all sub-emitters then play without emitting, the direct ones' time advanced by the span (held at a non-looping child's duration), the further ones' not. A system in procedural mode is prewarmed differently (exact burst ages). |
| `birthend/*` | A Birth child's disabled Emission module does not matter; a non-looping child's rate ends exclusively at its duration (no birth at it), in Play and Simulate. |
| `inherit/*` | Children of every sub-emitter type sample their start curves at the parent particle's normalized age (after its step; a dying parent's: 1), with or without InheritDuration, which shows no other effect. InheritColor multiplies by the parent's current colour, InheritSize by its current size, InheritRotation adds its rotation after the step (Collision and Trigger too), InheritLifetime multiplies by its remaining seconds; Death, Collision and Trigger children it leaves at zero life are counted at the frame's end and gone after the child's next update. |
| `collide/*`, `inherit/collision/*` | A particle's collision radius is half its largest size axis times the radius scale, its size taken where its previous step ended (a newborn's where its first step ends); a particle with less than 1e-6 s in the step collides with nothing. Planes are half-spaces tested where the step ended: the first plane of the list whose distance is at most the radius, unless the particle moves along it (cosine below 1e-4), particles behind it included; the contact is on the line of its velocity where the sphere touches the plane. World collisions sweep from the end less the step's movement to the end with a sphere 15.1% of the radius smaller and that much longer; the contact is that much before where it touches (head-on the full sphere's contact, deeper at a slant); a sphere starting inside a collider is put where it touches the collider's nearest face from outside, the normal (the collision event's) pointing in. Uniformly scaled Local systems collide in simulation space alike, as do non-uniformly scaled ones with World collisions (the hit normal carried in like a direction and normalized, `world/scaled_y`); a non-uniformly scaled system's collision planes have their up carried in alike (the bounced velocities of `planes/scaled_y`), but another offset (open). The Trigger module sees the same size as collisions (`collide/trigger/size`). Unmeasured: the parallel cut-off's exact cosine (1e-4). From the contact the rest of the movement and the whole velocity (with the animated part and the speed modifier) are reflected, scaled by 1 − dampen and their normal part by the bounce; the particle's own velocity takes the difference. The lifetime loss is added, then a particle beyond its life or outside the kill speeds (the speed it arrived with, speed modifier included: `world/killspeed`) dies, bounced. |
| `freenorm/*` | A freeform quad's normal is the cross product of the directions from the particle to its first two corners (pivot included; shorter than one when stretched), its tangent the direction from the first corner to the second (w -1); plain stretched quads take the head-to-tail tangent. The emitter scale scales the turned quad along the camera's right and up, the stretch follows the output-space velocity; a particle at rest has a +Z normal. |
| `meshscale/*` | A mesh particle's vertices: the mesh scaled by the particle's size, turned by its rotation and the alignment, then scaled by the emitter along the output axes; with Local alignment turned and scaled by the emitter's frame (Hierarchy scaling: the transform's whole matrix, skew included, the identity in a plain bake of a local simulation). Normals by the inverse transpose. |
| `meshindex/*` | BakeMesh's index format is 32-bit for more than 65535 vertices or a 32-bit particle mesh. A mesh particles' BakeTexture whose bake has 16-bit indices holds its index buffer read as 32-bit words (two indices a texel, the first in the low half); with 32-bit indices one index a texel. |

## Known differences (`known.json`)

- `catchup-step-births`: a birth due exactly at a step's end inside the catch-up advance falls one
  step apart.
- `bake-scratch-memory`: a texture passed in shows the Editor's scratch memory past the data; so do
  mesh particles' indices textures past their index pairs and with PerParticle.
- `freeform-stretching`: with rotateWithStretchDirection on an emitter whose y scale is not 1, a
  particle moving partly along the view turns by another, unresolved stretch angle.
- `stretched-normals`: side-view normals differ by up to 1e-3, the normal of a collapsed particle in
  a sorted system, and BakeRotationAndScale with a rotated non-uniformly scaled emitter.
- `prewarm-procedural`: a system in procedural mode is prewarmed without the stepped simulation
  (exact burst ages; mid-loop bursts 0.01 s older); unmodelled.
- `prewarm-long-child-catchup`: children emitted more than about four seconds before the prewarm's
  end (a 6 s child): the longer catch-up steps of long calls are not modelled.
- `prewarm-end-tie`, `prewarm-equal-age-order`: float ties at the prewarm's loop end and in the
  age order of equal-age children.
- `collision-quality-cache`: Low-quality world collisions, which the Editor resolves against planes
  it caches per voxel; Unbound collides as at High (Medium matches in the control).
- `collision-scaled-planes`: Planes collisions of a non-uniformly scaled Local system: the Editor's
  plane offset in simulation space is unresolved.

Unmeasured: other releases; orthographic cameras for stretched particles; negative pivots; a
system without a renderer under a culling mode (Unbound keeps simulating, reported); the bounds
the Editor culls particles by (Unbound culls by conservative bounds from the live particles and the
emitter, and keeps drawing systems with trails, a minimum particle size or camera-velocity
stretching whenever their layer is drawn); mesh particles with uv3 or more channels; trail bakes
past 256 indices; freeform stretching with an orthographic camera.
