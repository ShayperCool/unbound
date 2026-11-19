# URP strip shadows

Unity 6000.0.71f1, NVIDIA OpenGL Core only. The probe stages shadergraph's URP project,
replaces its scripts with these controls and adds a renderer feature to copy the shadow
atlas inside the render graph, while the texture is alive. Both independent Editor repeats
must have matching pre-launch sources and byte-identical records and images before publication.
The shared Editor runner's completed-shutdown cleanup policy applies.

The 145 records and 72 float images cover LineRenderer, TrailRenderer and a regular
MeshRenderer under directional, spot and point lights. Controls vary zero/width-scaled
bias, View/TransformZ alignment, orthographic/perspective viewing and a turned/scaled light.
Normal images and BakeMesh remain unbiased. The shadow shader writes world z into depth
and uses fixed clip coordinates, so drawing a point caster into an excluded cube face is
visible in the atlas. A regular mesh is the control outside the strip geometry path.
An unlit, non-casting receiver keeps ordinary-image checks independent of lit shading.

The default gate uses one directional cascade. Other releases and GPU
drivers are not established by this reference. The built-in pipeline has its own controls
in tests/projects/lines. Raw cube-depth readback is not part of this probe.

```
python3 tests/projects/stripshadows/run.py native --release 6000.0.71f1 --label native-L
python3 tests/projects/stripshadows/run.py publish --release 6000.0.71f1 --label native-L
UNBOUND_CLI=/path/to/immutable/runtime/unbound UNBOUND_MANAGED_DIR=/path/to/immutable/runtime/managed \
  python3 tests/projects/stripshadows/run.py check --release 6000.0.71f1 --label engine-L
```

Every missing, extra and failing value is kept in `comparison.json`. Numeric and image
comparisons reuse the line probe's unchanged tolerances. Capture validation also rejects
missing, extra or incorrectly sized images, duplicate or reordered records and nonfinite
image channels. No failure is allowed by the gate.

The [6000.0 shadowBias contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LineRenderer-shadowBias.html)
expresses bias as a proportion of each segment's width. The shadow images change across
zero and nonzero bias while ordinary geometry stays unchanged.
[CreateShadowRendererList](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.CreateShadowRendererList.html)
creates the selected light's shadow list, and
[CullShadowCasters](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.CullShadowCasters.html)
supplies its splits. The point controls distinguish the selected split from the other five
faces. Unbound retains the split planes independently of the caller's arrays, filters
ordinary renderers and queued meshes with them, and clears the stored splits when reusing
a culling result. Legacy shadow settings supply their own split planes when no retained
split exists. Array lifetime, exclusions, fallback planes, touching and unknown/nonfinite
bounds, and profile scope also have managed controls.

The separate `--cascades` diagnostic adds 96 records and 48 images for two, three and four
directional cascades, changed two/four split ratios, an orthographic four-cascade control, and
the three-cascade and wide four-cascade controls again with close-fit shadow projection
(`cascade3Close`, `cascade4WideClose`), for each strip type and the regular mesh. Two NVIDIA Editor repeats preserve every earlier
record and image byte. Publication stores only the new controls in `cascade.values.jsonl`;
the earlier reference files stay unchanged, and a later publication may only add controls to
it (every earlier record and image byte-identical, in order). Pass `--cascades` to each command
above to record, publish or compare this diagnostic. Comparisons compose the full 241-key
reference in the run cache and remain strict.

The diagnostic matches 217/217 keys (205/217 before the cascades' culling spheres were taken
into account: one additional atlas tile in `Directional.{line,trail,mesh}.{cascade3,cascade4Wide}`).
With stable-fit shadow projection a caster is drawn into a directional light's cascades in their
order until one cascade's culling sphere holds the caster's whole shadow volume (its bounding
sphere swept along the light until it leaves the camera's frustum cut at the shadow distance);
the later cascades leave it out. A cascade also takes a caster only when, in the light's frame,
the caster's bounding sphere reaches the cascade's sphere across the light and is not wholly
beyond it along the light; the cascades' culling planes then apply as before. The holding sphere's
radius is the culling sphere's times the split's blend culling factor (URP gives 1). The
close-fit controls draw exactly the atlases of their stable-fit controls in the Editor (two
NVIDIA repeats, every earlier record and image unchanged): `QualitySettings.shadowProjection`
does not change this culling (Unbound used to keep every cascade with close fit). Unmeasured:
blend culling factors other than 1, which URP never passes (it sets 1 for every cascade); only a
pipeline's own split data can. Both the
default gate and `--cascades` are strict gate commands.

SRP lists retain renderers whose coarse bounds are unmeasured, including non-rigid skins,
while still respecting split exclusions. Measured strip face selection and trusted mesh
bounds keep their split-plane filtering. A renderer-list regression control places a loaded
two-bone skin's fallback box outside the selected plane while its baked pose lies inside;
the skin draws, an outside regular mesh does not, and an excluded split rejects the skin.
This conservatively preserves previously drawn geometry without inventing tighter skin bounds.
