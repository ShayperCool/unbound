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

    python3 tests/projects/particlerender/run.py native --label native1   # Editor capture (GPU, job pool)
    python3 tests/projects/particlerender/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particlerender/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1/` (`values.jsonl`, 12 images); both captures were
identical. Values compare with particletrails' comparator (0.0002 absolute plus 0.0002 relative);
images go through `particles/compare_images.py` (report only). Run output stays under
`$XDG_CACHE_HOME/unbound/validation/runs/particlerender/`.

## Documented contract (Unity 6000.0 Scripting API)

| Source | What it says |
| --- | --- |
| [ParticleSystemRenderer.BakeTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemRenderer.BakeTexture.html) | "Creates a snapshot of ParticleSystemRenderer and stores it in a Texture2D"; returns "the number of indices used by the Particle System". Nothing about the layout or format. |
| [MainModule.cullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-cullingMode.html), [ParticleSystemCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemCullingMode.html) | Automatic: looping effects pause offscreen, one-shot ones keep playing; Pause; PauseAndCatchup "perform[s] an extra simulation when the system comes back onscreen"; AlwaysSimulate. |
| [ParticleSystem.proceduralSimulationSupported](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem-proceduralSimulationSupported.html) | Limit Velocity over Lifetime and "modifying any properties from script while the system is playing" disable procedural mode. |
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
| `freemove/*`, `freeplay/*`, `camvel/*/freeform` | Freeform: a quad of the particle's size facing the camera's position (right across the camera's up and the direction to the camera), turned by the particle's rotation plus, with rotateWithStretchDirection, the stretch direction's angle in that plane, moved by pivot.x sizes along its right, pivot.y along its up and pivot.z away from the camera, then scaled along the stretch direction by length over size.y; normal towards the camera (unstretched quads). A particle at rest bakes NaN corners with the view direction as normal; BakeMesh is empty when the last baked vertex is NaN (the at-rest particle drawn last), not when an earlier one is. |
| `firstvis/*` | Culling bounds are the particles' (the emitter point without one). Frames with zero frame time keep the last bounds: a seen system stays seen after an Emit or Play (shown_*), one whose particles are all out of view stays unseen though its emitter is in view (stale). A system that never had bounds (played or emitted by script, never updated while time moved, never rendered) has nothing to draw after a zero-time update: invisible at once and until an update whose frame time is positive (at any simulation speed) or a Simulate. Play On Awake and a render before any update give bounds (tests/projects/resources mask_particles). A playback's two grace updates are updates that move time. Play restarts a playing system whose emission module is off, keeping its particles (shown_play; an emitting one plays on, cull/catchup_replay). |
| `subcull/*` | A sub-emitter updates while its parent updates or its own renderer is seen, whatever its own culling mode: an unseen AlwaysSimulate child freezes with its culled parent; a seen child runs on after its parent left the view. |

## Known differences (`known.json`)

- `catchup-step-births`: a birth due exactly at a step's end inside the catch-up advance falls one
  step apart.
- `bake-scratch-memory`: a texture passed in shows the Editor's scratch memory past the data.
- `mesh-bake-layout`: a non-uniformly scaled emitter's mesh particle vertices (scaled after the
  turn in the Editor) and the mesh-mode index texture (16-bit indices read as 32-bit).
- `freeform-stretching`: stretched freeform normals lean from the camera direction by an unresolved
  rule; freeform quads of a rotated, non-uniformly scaled emitter.
- `stretched-normals`: side-view normals differ by up to 1e-3, the normal of a collapsed particle in
  a sorted system, and BakeRotationAndScale with a rotated non-uniformly scaled emitter.
- `birth-subemitter-start`: a Birth sub-emitter child under a played parent starts playing in the
  parent's first update in the Editor, and its first births from a rate that reaches a whole
  particle exactly at a step's end come one update later; the culling rule matches.

Unmeasured: other releases; orthographic cameras for stretched particles; negative pivots; a
system without a renderer under a culling mode (Unbound keeps simulating, reported); the bounds
the Editor culls particles by (Unbound culls by conservative bounds from the live particles and the
emitter, and keeps drawing systems with trails, a minimum particle size or camera-velocity
stretching whenever their layer is drawn); mesh particles with uv3 or more channels; trail bakes
past 256 indices; freeform stretching with an orthographic camera.
