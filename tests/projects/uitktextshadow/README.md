# UI Toolkit text shadow and outline

Stages the `uitk` fixture in a fresh cache directory and replaces its interaction run with
`Assets/Scripts/TextShadowProbe.cs`: twelve labels ("Shadow Ag", 32 points, white, the panel's
default font) on a runtime panel, one case per cell of 192 x 88 points on a green fill, rendered at
panel scale 1 and 1.25 (`shadow-1.png`, `shadow-1.25.png`). `shadow.jsonl` records each label's
resolved outline and layout.

Cases: `plain` and `outline-no-width` (an outline colour without width) are controls; `shadow`
(3, 4, no blur), `shadow-blur` (2.6, 3.9, blur 5.3, a translucent dark colour), `shadow-negative`
(-4, -3, blur 2), `shadow-far` (30, 30: beyond the atlas padding), `shadow-wide-blur` (blur 40);
`outline` (2 wide), `outline-shadow`; `face-transparent` (a transparent text colour with a
shadow); `small` (14 points) and `bold`, each with a shadow.

What it shows (Unity 6000.0.71f1): text-shadow and -unity-text-outline are drawn by the distance
field text shader, the shadow under the face and outline, in units of the font asset's atlas padding
at its point size: an offset or blur beyond one padding stops there (`shadow-far`,
`shadow-wide-blur`), so the shadow does not scale with the style past it; the panel scale does not
change them. A transparent face leaves the shadow outside the glyphs only.

`verify.py` compares each cell inside its edge exactly and the whole images within the recorded
residual (the cells' outer corner pixels, from the rectangle's anti-aliased edge). The `bold` label
is laid out 7.2 points narrower than in the Editor (its text measures 186.4 wide, 193.6 in the
Editor) while its glyphs are drawn at the same places: listed in `verify.py` KNOWN.

Other releases are unmeasured.

```sh
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/uitktextshadow"
python3 tests/projects/uitktextshadow/run.py native --release 6000.0.71f1 --output "$cache/native-a"
python3 tests/projects/uitktextshadow/run.py native --release 6000.0.71f1 --output "$cache/native-b"
python3 tests/projects/uitktextshadow/run.py publish --release 6000.0.71f1 --captures "$cache/native-a" "$cache/native-b"
python3 tests/projects/uitktextshadow/run.py engine --release 6000.0.71f1 --output "$cache/engine" \
  --cli /path/to/unbound --managed /path/to/managed
python3 tests/projects/uitktextshadow/verify.py --release 6000.0.71f1 --candidate "$cache/engine/capture"
```

## Reference

`Reference/unity-6000.0.71f1/metal`: two byte-identical macOS Editor captures (Metal).
