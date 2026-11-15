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

The separate `--cascades` diagnostic adds 72 records and 36 images for two, three and four
directional cascades, changed two/four split ratios, and an orthographic four-cascade control,
for each strip type and the regular mesh. Two NVIDIA Editor repeats preserve every earlier
record and image byte. Publication stores only the new controls in `cascade.values.jsonl`;
the earlier reference files stay unchanged. Pass `--cascades` to each command above to
record, publish or compare this diagnostic. Comparisons compose the full 217-key reference
in the run cache and remain strict.

The diagnostic currently matches 205/217 keys. All 12 failures remain in `comparison.json`:
`Directional.{line,trail,mesh}.{cascade3,cascade4Wide}.map.{covered,pixels}`. Unbound draws
one additional atlas tile in each case. Two cascades, the other four-cascade split, and the
orthographic control match. Changing strip geometry or shadow bias does not explain the
regular-mesh failures. Receiver-volume containment across cascades remains open; follow-up
controls must vary the receiver frustum, sphere shrink/blending and culling flags before
changing the shared caster selection. These failures are outside the default one-cascade
gate, which retains all 145 strict checks without exceptions.
