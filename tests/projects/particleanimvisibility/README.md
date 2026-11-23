# Animation culling with particle renderers

Unity 6000.0.71f1, Built-In Render Pipeline. Two independent windowed Linux Editor runs
on NVIDIA matched across all 798 rows. The runner closes Scene views and uses a 320×180
Game view. The gate compares every visibility, animation clock and pose sample exactly.

An Animator with CullCompletely freezes while its only ParticleSystemRenderer is invisible.
A legacy Animation with BasedOnRenderers freezes while invisible and catches up when visible
again. The control rig uses a plain billboard. Other rigs use minimum screen size, trails,
or camera-velocity stretch. The camera and emitter move independently, the camera drops the
layer, the renderer is disabled, and the camera is turned off. A world-space particle starts
away from its emitter, and the trail and stretch controls include moving particles.
Two Pause-mode controls record the simulation clock as the camera drops an off-screen
system and brings it back. The clock stops for both empty and live systems with trails.
A longer diagnostic found a one-step clock difference after re-enabling the renderer and
a different near-frustum result for an empty Pause-mode system. Those cases remain open.
PauseAndCatchup holds and takes a larger step on return. Its absolute time remains one
step ahead in Unbound per return in the wider diagnostic, with the same values before
and after the bounds change. The Automatic controls check the procedural flag and clock
for a system that emits over time and a nonprocedural trail system. In the separate manual
Emit control, Unity simulated off-screen and both Unbound builds paused.

Near the edge of the view, a small empty system becomes invisible earlier than a large
empty system. A large emitted particle stays visible farther out even with a small
configured start size, as does a small emitted particle with a large configured start size.
Separate Editor controls with constant sizes 0.1, 0.5 and 4 recorded world-space extents of
0.071, 0.355 and 2.84 for stationary rigs. This supports a 0.71-times-size margin around
particle positions or the emitter when empty. The measured moving trail includes its
earlier points and the moving stretched billboard extends behind its particle. Public
particle bounds and sizes outside these cases remain open. SRP, mesh particles with
approximate bounds, ribbons, freeform stretch, custom simulation space, wider or
size-independent trails and mixed world-space trails while paused are unmeasured.

Unity documents the effect of each culling mode, the visibility contribution of shadow
rendering and Scene view cameras, and minimum particle size as a screen fraction.
The bounds estimates and exact timing above are observations of this setup.
[AnimatorCullingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorCullingMode.html),
[AnimationCullingType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationCullingType.html),
[Renderer.isVisible](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-isVisible.html),
[Particle simulation Pause](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemCullingMode.Pause.html),
[Particle renderer Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysRendererModule.html).

```sh
python3 tests/projects/particleanimvisibility/run.py record --release 6000.0.71f1 --label native1
python3 tests/projects/particleanimvisibility/run.py record --release 6000.0.71f1 --label native2
python3 tests/projects/particleanimvisibility/run.py publish --release 6000.0.71f1 native1 native2
python3 tests/projects/particleanimvisibility/run.py gate --release 6000.0.71f1
```

The runner uses private project copies under the validation cache.
