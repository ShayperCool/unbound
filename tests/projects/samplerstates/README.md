# samplerstates

Which sampler state a graphics program samples a texture through on the OpenGL Core device.

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

Reference (`Reference/unity-6000.0.71f1.jsonl`, the Unity 6000.0.71f1 Editor on OpenGL Core,
NVIDIA, two identical captures): a texture is sampled through its own sampler state whatever
sampler the program names, another texture's too, in either stage and for every texture shape.
Comparisons compare. The Vulkan Editor is not recorded.

Unbound: `run.py check` compares an Unbound capture (`--force-glcore`) with the reference.
`residuals.json` lists the cases that differ with their cause: a texture sampled with and
without comparisons does not compile yet (it draws with the error shader).

    python3 tests/projects/samplerstates/run.py native --label a   # the Editor, twice
    python3 tests/projects/samplerstates/run.py native --label b
    python3 tests/projects/samplerstates/run.py publish a b
    python3 tests/projects/samplerstates/run.py check
