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
texture's sampler that texture's state, with comparisons too. An inline `...Compare` sampler
compares (greater or equal: depth is reversed, and the far plane is stored as 0); a texture's
own sampler compares when the texture is a `Shadowmap`, and reads the stored depth when it is a
`Depth` target. A program with a sampler of a texture it does not use (`Shapes`: `sampler_B`
without `_B`) is an error there ("Unrecognized sampler 'sampler_b' - does not match any texture
and is not a recognized inline name") and draws with the error shader.

On both devices a program's depth output is the depth stored; without a depth buffer its colors
are drawn alone, and a program without colors leaves the target as it was.

Unbound: `run.py check` compares an Unbound capture (`--force-glcore`, or `--force-vulkan` with
`--device vulkan`) with the device's reference. `residuals.json` and `residuals-vulkan.json`
list the cases that differ with their cause: Unbound compares where the Editor's comparison
reads the depth of a texture whose state does not compare, and stores 24 bits where the
Editor's depth buffer has 16.

    python3 tests/projects/samplerstates/run.py native --label a   # the Editor, twice (a-1, a-2)
    python3 tests/projects/samplerstates/run.py publish a-1 a-2
    python3 tests/projects/samplerstates/run.py check
    python3 tests/projects/samplerstates/run.py check --device vulkan
