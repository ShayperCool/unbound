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
| `bake/*` | The vertices texture is RFloat: the vertex buffer, the active streams packed in order (position, normal, tangent, color as four bytes in one texel, the other streams), every vertex (PerVertex) or one record per particle (its first vertex; per trail point; a mesh particle's 3×3 transform, centre and color). The indices texture holds the triangle list as floats; with IncludeParticleIndices (particles) it is RGFloat with each particle's own indices (0 1 2 2 3 0) and its index; PerParticle without it holds one particle's indices. Sizes leave room for max(64, next power of two) + 1 records (indices: 128, trails 256), rows of whole records. A texture passed in is kept in its format and reinitialized only when too small. Nothing to bake returns 0. |
| `stretch/*`, `camvel/*` | The quad trails back from the particle along its velocity less the camera's velocity × cameraVelocityScale, length size.y × lengthScale × the emitter's x scale + speed × velocityScale (negative: forwards), size.x wide across the stretch's direction on screen in the camera's plane (scaled along that axis); corners head and tail on one side, then tail and head on the other, u from 0 at the head; the normal is the quad's, towards the camera; at rest the quad collapses onto the particle. The pivot moves it by pivot.x sizes along the stretch and pivot.y twice the size across. Min and max particle size clamp the width only, as a fraction of the viewport's width. Oldest/youngest in front sort by the fraction of the life lived. `Camera.velocity` is the movement since the camera last rendered over the frame's delta time. |

## Known differences (`known.json`)

- `catchup-step-births`: a birth due exactly at a step's end inside the catch-up advance falls one
  step apart.
- `bake-buffer-history`: the Editor keeps and grows its bake buffers between bakes, so its texture
  sizes depend on earlier bakes (and mesh attributes); a texture passed in shows stale memory past
  the data.
- `mesh-bake-layout`: mesh particles' 2D rotation axis and the mesh-mode index texture.
- `freeform-stretching`: BakeMesh of a Simulate-stepped freeform system is empty in the Editor;
  Unbound's freeform geometry is approximate.
- `stretched-normals`: side-view normals differ by up to 1e-3, the normal of a collapsed particle in
  a sorted system, and BakeRotationAndScale with a rotated non-uniformly scaled emitter.

Unmeasured: other releases; orthographic cameras for stretched particles; negative pivots; a
system without a renderer under a culling mode (Unbound keeps simulating, reported); the bounds
the Editor culls particles by (Unbound culls by conservative bounds from the live particles and the
emitter, and keeps drawing systems with trails, a minimum particle size or camera-velocity
stretching whenever their layer is drawn).
