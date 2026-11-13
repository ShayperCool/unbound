# samplerstates

Which sampler state a graphics program samples a texture through.

Each case is a shader `Probe/SamplerStates/<case>` drawn into one float texel with
`Graphics.Blit`. `_A`, `_C`, the cube and the volume are point filtered, `_B` is bilinear, and
every sample lies between two texels, so its value tells the state it was sampled through.

| Case | What it samples |
| --- | --- |
| One | each texture through its own sampler (control) |
| Two | `_A` through `sampler_B` and `_B` through `sampler_A` |
| Foreign | `_C`, which the program samples through other textures' samplers only |
| Main | `_MainTex` through another texture's sampler |
| Load | beside a `Load` of the texture |
| Legacy | beside a `sampler2D` |
| Shapes | a cube and a volume through a 2D texture's sampler |
| Vertex | in the vertex stage |
| CmpOnly | a comparison (control) |
| CmpTwo | one depth texture through two comparison samplers |
| CmpOwn, CmpInline, CmpOther | one texture with and without comparisons |
| Inline | textures through inline samplers of another filter and wrap mode |
| DepthPlain | a depth texture without comparisons: its own, an inline and another texture's sampler |
| DepthChannels | the four channels of a depth texture's sample |
| InlineAxes | inline samplers with wrap modes of one axis (`ClampU_RepeatV`, `RepeatU_ClampV`, `Repeat_ClampU`) and `Mirror` |
| InlineMirrorOnce | an inline `MirrorOnce` sampler, outside 0..1 on both sides |
| InlineVolume | a volume of two slices through inline samplers, outside 0..1 along W |
| InlineAniso | a texture of constant mip levels through `Trilinear`, `Aniso8` and `Aniso2` inline samplers, with gradients eight times longer along U |
| UsedLoad, UsedVertex | a sampler whose texture the program only loads, or uses in the vertex stage only |
| InlineV | a texture of four texels through `ClampU_RepeatV` outside 0..1 along V and along U, through `ClampUV`, and through `MirrorOnceU` along V |
| InlineWords | whole-sampler words together: `MirrorOnceRepeat`, and `Mirror_RepeatU` |
| BadFilters, BadClampRepeat, BadClampMirror, BadAniso, BadAnisoTail, BadBare | wrongly spelt inline samplers: two filters, `Clamp` with `Repeat` or with `Mirror`, the levels `Aniso3` and `Aniso8_2`, and one whose name does not start with `sampler` |

`_Shadow` is a point filtered `RenderTextureFormat.Depth` target cleared to the far plane. The
cases that sample it are drawn again with four depth targets of two texels, 0.25 and 0.75
(`Fill` writes `SV_Depth`): `<case>.split` and `.splitLinear` are `RenderTextureFormat.Depth`,
point and bilinear; `.map` and `.mapLinear` are `RenderTextureFormat.Shadowmap`. Their values
are recorded in twentieths (`Twentieths` rounds them), so that the bits a depth buffer stores do
not tell: 5 is the first texel, 8 both texels filtered, 0 and 20 a comparison with 0.5, 6 and
14 a filtered one. `DepthChannels.<target>.raw` is each target as sampled.

The programs that fill the depth targets are also drawn where there is no depth buffer:
`Fill.color` is the color of the program that writes a color and the depth, `DepthOnly.color` a
cleared target after the program that writes only the depth, and `DepthOnly.depth` a depth target
that program filled.

Reference, the Unity 6000.0.71f1 Editor on NVIDIA, two identical captures for each device.

OpenGL Core (`Reference/unity-6000.0.71f1.jsonl`): a texture is sampled through its own sampler
state whatever sampler the program names, an inline one or another texture's, in either stage,
for every texture shape, and with comparisons too. A texture's own state compares when the
texture is a `Shadowmap` (less or equal, through the texture's filter): a comparison of a `Depth`
target reads the stored depth instead, as its other samples do.

Vulkan (`Reference/unity-6000.0.71f1/vulkan.jsonl`, `--device vulkan`): a texture is sampled
through the sampler the program names: an inline sampler is the state its name spells, another
texture's sampler that texture's state, with comparisons too. An inline name spells one filter,
wrap modes for the whole sampler or for the axes their `U`, `V`, `W` letters name, along a
volume's W too, and an anisotropic level; a wrongly spelt one is an error ("Unrecognized inline
sampler name ...") and the program draws with the error shader. An inline `...Compare` sampler
compares (greater or equal: depth is reversed, and the far plane is stored as 0); a texture's
own sampler compares when the texture is a `Shadowmap`, and reads the stored depth when it is a
`Depth` target. A program with a sampler of a texture it does not use (`Shapes`: `sampler_B`
without `_B`) is an error there ("Unrecognized sampler 'sampler_b' - does not match any texture
and is not a recognized inline name") and draws with the error shader; a texture the program
loads, or uses in the other stage, is used.

On both devices a program's depth output is the depth stored; without a depth buffer its colors
are drawn alone, and a program without colors leaves the target as it was.

Unbound: `run.py check` compares an Unbound capture (`--force-glcore`, or `--force-vulkan` with
`--device vulkan`) with the device's reference. `residuals.json` and `residuals-vulkan.json`
list the remaining cases and their causes. Texture-owned non-comparing depth state now reads
stored depth on the measured NVIDIA devices. Vulkan has no remaining scalar residuals; OpenGL
Core retains three compute comparisons, four unsupported comparison-plus-Load cases, and three
MirrorOnce controls. D16 uses a normalized 16-bit
color copy so its linear filtering keeps the measured depth precision. OpenGL Core retains the existing `MirrorOnce` approximation as `Mirror`; Vulkan uses
native mirror-clamp-to-edge where the device enables it. A depth buffer of 16 bits is stored as 16 bits
without stencil, as in the Editor.

`Stencil.<target>.*` (last): a target of two texels cleared, its left texel drawn with stencil 1,
then drawn red where the stencil is 1: with 16 depth bits both texels are red (no stencil
buffer), with 24 bits and with `D16_UNorm_S8_UInt` only the left one; `.format` is the
target's `depthStencilFormat` after Create (the latter reads `D24_UNorm_S8_UInt` on both
devices).

`StencilFaces.<culling>.<draw>` (recorded last): a two-texel target with a stencil buffer gets a
quad over its left texel and the same quad through a mirrored matrix (negative determinant) over
its right one, with a two-sided stencil state writing 1 for front faces and 255 for back faces,
then red where the stencil is 1 and green where it is 255; once plain, once with
`GL.invertCulling`; then culling back faces (`cullBack`), and both through a command buffer's
DrawMesh (`commandBuffer`, `commandBufferCullBack`). On both devices the plain quad takes the
front state and the mirrored one the back state, with inverted culling the other way round, and
culling back faces culls the mirrored quad: DrawMeshNow and CommandBuffer.DrawMesh draw a
mirrored matrix as it is (its faces turned round), while inverted culling turns which faces
count as front. Unbound retains this winding for both mesh-call paths. `RendererFaces.*` adds MeshRenderer,
SkinnedMeshRenderer and CommandBuffer.DrawRenderer, each under a reflected parent. Those paths
correct the model's determinant for both culling and stencil. Controls include X/Y/Z reflections,
two negative axes, a collapsed X axis, both culling modes and GL.invertCulling in both states,
and the same transforms through DrawMeshNow, CommandBuffer.DrawMesh and DrawMultipleMeshes.
Skinned controls cover both world-space CPU skinning and local-space vertex-buffer output,
for camera draws and CommandBuffer.DrawRenderer. A reflection baked into submitted vertices
retains its renderer winding separately from the submitted model matrix. The two independent
captures per device keep all earlier rows unchanged. These observations are for Unity
6000.0.71f1 on OpenGL Core and Vulkan, measured on the NVIDIA driver only.

Unity 6000.0 contracts checked for these cases:

| Contract | Control |
|---|---|
| [DrawMeshNow](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.DrawMeshNow.html) uses the current material pass and supplied transform, without normal lighting/shadows | Immediate rows select the stencil pass and use the same transforms as renderer rows. Determinant handling is a measured detail, not a documented guarantee. |
| [CommandBuffer.DrawMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.DrawMesh.html) records the supplied transform/pass; lighting data is undefined | Command mesh rows use an unlit shader. |
| [GL.invertCulling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GL-invertCulling.html) reverses the culling direction | Each path is rendered with inversion off/on; Cull Back changes the visible texels. Its effect on two-sided stencil is measured. |
| [Cull](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-Cull.html) Back discards back faces, Off retains both | Two-sided and Cull Back passes observe the same reflected geometry. |

`Compute.<kernel>.<target>` (after those): compute kernels (`DepthCompute.compute`) reading each
depth target: `Compare` compares both texels against 0.5 through the texture's own comparison
sampler, `CompareLoad` compares the first and loads both, `LoadOnly` loads both. On Vulkan a
`Shadowmap` target compares (greater or equal) and a `Depth` target reads its stored depth, as
in graphics programs; on OpenGL Core the comparison of the four targets follows no one rule
(recorded as residuals). Loads read the stored 16-bit depth on both devices; Unbound refuses a
kernel that compares and loads a texture on the OpenGL device.

    python3 tests/projects/samplerstates/run.py native --label a   # the Editor, twice (a-1, a-2)
    python3 tests/projects/samplerstates/run.py publish a-1 a-2
    python3 tests/projects/samplerstates/run.py check
    python3 tests/projects/samplerstates/run.py check --device vulkan

`MirrorWrap.*` samples a volume along U, V and W at positive and negative coordinates
inside and outside the mirrored interval. Texture-owned Clamp and Mirror controls distinguish
edge clamping from repeated reflection. The inline PointMirrorOnce sampler uses the same
coordinates with a texture whose own mode is Clamp. On Vulkan the inline and texture-owned
MirrorOnce states reflect around zero and clamp at either edge. OpenGL Core ignores the
inline state and uses the texture's own state; its three texture-owned MirrorOnce rows retain
the existing declared approximation. Both 489-row references preserve all 477 earlier rows,
from two identical captures per device on Unity 6000.0.71f1, NVIDIA RTX 4060 only.

Contract: [TextureWrapMode.MirrorOnce](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureWrapMode.MirrorOnce.html)
defines reflection around zero followed by edge clamping and requires checking device support.
The native Vulkan opt-in requires the enabled mirror-clamp extension; other backend creation
rejects that option. The zero-option path and ordinary samplers retain their validation.

`D16Filter.*` compares D16 depth, R16 UNorm holding the same stored integers, and float
textures holding either the quantized endpoints or the original endpoints. Nine coordinates
include texel centers and seven interpolation positions. The normalized formats agree on both
measured devices; the float copies differ between the texel centers. A D16 color copy therefore
uses R16 UNorm where the device can render and filter it, with the existing float fallback and
a warning elsewhere. Depth-copy pipelines distinguish normalized storage and multisampling.
The 525-row references retain all 489 earlier records unchanged, with two matching captures
per device on Unity 6000.0.71f1, NVIDIA only. Other drivers remain unmeasured.

The Unity 6000.0 [RenderTexture.depth contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-depth.html)
lists 16-bit depth precision; the existing Stencil controls check the absence of stencil.
[Texture.filterMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture-filterMode.html)
selects the sampling filter. The interpolation precision above is a measured device result.

`DepthState.*` adds 24 controls per device, preserving every earlier 525 reference row. D16,
D24-with-stencil and D32 targets use point and bilinear filters. One material alternates
None, CompareDepths, the first None target again, and RawDepth. Its four channels read through
the target's own sampler, another target's comparing sampler, an inline comparing sampler,
and that other target's own sampler. The same bound texture therefore distinguishes stored
depth from a comparison, and changing bindings checks reused sampler and bind-group state.
All controls match: OpenGL Core 547/557 with ten declared residuals; Vulkan 557/557.

Unity 6000.0 documents [shadowSamplingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTextureDescriptor-shadowSamplingMode.html),
[RawDepth](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ShadowSamplingMode.RawDepth.html)
and [CompareDepths](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ShadowSamplingMode.CompareDepths.html)
as selecting depth sampling or comparison sampling. The result of a comparison expression
through non-comparing state is observed behavior in these captures, not a portable shader
guarantee. The native sampler opt-in is limited to NVIDIA OpenGL and Vulkan; other drivers
are rejected. The release profile carries explicit texture state; unmeasured releases retain
the legacy comparison path. OpenGL compute histories remain separate and unresolved.

Eight `DepthState.pool.*` controls repeat the sampling-state changes through
[GetTemporary](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.GetTemporary.html)
and ReleaseTemporary. Each depth lease is fully filled before sampling, so None and
CompareDepths give different results. The binding controls request false, true, false and
true for bindMS and record each returned descriptor. Temporary pool identity preserves both
settings. These controls extend each reference from 549 to 557 rows without changing earlier
rows; both devices have two identical NVIDIA captures.
