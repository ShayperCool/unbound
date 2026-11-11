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
14 a filtered one. `DepthChannels.split.raw` is one of them as sampled.

Reference (`Reference/unity-6000.0.71f1.jsonl`, the Unity 6000.0.71f1 Editor on OpenGL Core,
NVIDIA, two identical captures): a texture is sampled through its own sampler state whatever
sampler the program names, an inline one or another texture's, in either stage, for every
texture shape, and with comparisons too. A texture's own state compares when the texture is a
`Shadowmap` (less or equal, through the texture's filter): a comparison of a `Depth` target
reads the stored depth instead, as its other samples do. The Vulkan Editor is not recorded
here yet.

Unbound: `run.py check` compares an Unbound capture (`--force-glcore`) with the reference.
`residuals.json` lists the cases that differ with their cause: Unbound compares where the
Editor's comparison reads the depth of a texture whose state does not compare, and stores 24
bits where the Editor's depth buffer has 16.

    python3 tests/projects/samplerstates/run.py native --label a   # the Editor, twice (a-1, a-2)
    python3 tests/projects/samplerstates/run.py publish a-1 a-2
    python3 tests/projects/samplerstates/run.py check
