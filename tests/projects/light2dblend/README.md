# 2D light blend styles

The 2D renderer (URP 17, the `urp2d` probe's pipeline and settings, whose
`Assets/Settings/Probe2DRenderer.asset` defines four light blend styles: 0 Multiply, mask channel
R; 1 Additive, no mask; 2 Multiply with Mask, R; 3 Additive with Mask, R) renders each blend
style's lights into its own light texture. With several blend styles active in a frame (a global
light counts) the light pass draws into one target per active style, and a light writes only the
target of its style's place among the active styles: with styles 0 and 3 active, a style 3 light
writes the second target; with 0, 1, 2 and 3, the fourth.
`Assets/Editor/Light2DBlendProbe.cs` records in Play mode the colour Sprite-Lit squares get: a
white one from a point light of each blend style under a dim global light (style 0), without a
mask texture and with a black mask texture (secondary texture `_MaskTex`), from the point light
alone (its style the only active one), and from the global light alone; and a grey one from
point lights of styles 1 and 3, and of styles 1, 2 and 3, at separate places under the global
light (style 3's light writing the third and the fourth target). Each case is left three frames,
which render its camera into a render texture; the colour at each light's centre (and for the
single lights halfway to its edge) is recorded (0-255). Values compare within 1, by which the
Editor on different GPUs rounds them apart.

```sh
python3 tests/projects/light2dblend/run.py record --label A     # twice, A and B
python3 tests/projects/light2dblend/run.py publish A B
python3 tests/projects/light2dblend/run.py gate
```
