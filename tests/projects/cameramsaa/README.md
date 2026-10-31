# Camera and RenderTexture MSAA

Unity 6000.0.71f1 Linux Editor, built-in RP, NVIDIA RTX 4060, driver 580.178.04,
OpenGL Core 4.5 on DISPLAY=:0. Unbound uses the same GPU through Vulkan.
This fixture measures camera sample selection separately from format capability.

Before changing runtime sources, two independent real Editor processes recorded
`~/.cache/compat/cameramsaa-oracle1` and `cameramsaa-oracle2`. All 886 observations,
181 complete float images and 11862016 channels match byte for byte. The private
starting engine was built from f2063fce into `cameramsaa-before-managed`, with
`cameramsaa-before-build/` intermediates, then captured in `cameramsaa-oracle-before`.
Earlier exploratory recordings remain under `cameramsaa-native*` and `focused*`.
The focused screen attempt was black and the corresponding engine recording threw
on an unimplemented Editor API. Those attempts are not the oracle. The final screen
path uses a fresh camera and two player-loop frames before reading a real Game view.

The constant-color triangle has three slopes and subpixel vertices. All 128x128 RGBA
float channels, including alpha and missing format channels, are compared with the
fixed absolute/relative maximum rule of 2e-4 copied from uirectclipping. There are no
expected failures, exclusions or per-case overrides. Every capture has a PNG companion.

The matrix covers ARGB32/Default, ARGBHalf/DefaultHDR, ARGBFloat, RFloat/RGFloat,
RHalf/RGHalf; target samples 1/2/4 and quality 0/2/4; allowMSAA off, HDR on/off,
explicit targets, render buffers, partial viewports, copy image effects, the actual
screen, live quality/allowMSAA changes, release/recreation and explicit resolve.
Descriptor/API readbacks are recorded before Create, after Create and after rendering.
Sample capability requests 0/1/2/3/4/8 include invalid/non-power-of-two queries,
recorded as API observations rather than rendered invalid targets.

Measured policy: all tested color formats support explicit 2x and 4x targets. A
single-sample explicit target stays single-sample, independent of quality, unless
the camera needs an HDR intermediate because allowHDR is true and its target is LDR.
That intermediate uses quality samples, even when the final target requests two.
An already-HDR target uses its own samples with either allowHDR value. Explicit
buffer bindings bypass the HDR/quality intermediate. Screen rendering uses quality
samples with HDR on or off. Image effects preserve target samples when no HDR
conversion is needed. Capability reporting alone therefore cannot select camera AA.
