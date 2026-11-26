# EditorGUI DrawPreviewTexture, DrawTextureAlpha and DrawTextureTransparent

The Unity 6000.0.71f1 Play mode capture (windowed Linux GPU Editor, two identical captures)
measures, in a game's `OnGUI`, the pixels the previews draw into 32x32 cells of the 320x180 game
view (over an opaque backdrop: the game view is not cleared under the GUI) and the whole view
(`previews.png`):

- `DrawPreviewTexture` of a 4x4 texture of four coloured quadrants with four alphas, of a texture
  with a colour per mip at mip 1, 2 and 0.5, with the colour masks Red and RGB, with exposure 1,
  ScaleToFit and ScaleAndCrop with other aspects, under a half-transparent `GUI.color`; of an
  Alpha8 texture and of an imported 4x4 normal map (`Assets/Resources/normal.png`, uncompressed);
- `DrawTextureAlpha` (also at mip 2) and `DrawTextureTransparent` (also ScaleAndCrop, the Alpha mask
  and exposure 1) over the transparency checker;
- a caller's material (Unlit/Transparent): the `_ColorMask`, `_Mip`, `_Exposure` and `_SNormFormat`
  the call sets on it, and what it draws;
- the error of `DrawTextureTransparent` without image or aspect, the exceptions outside `OnGUI`,
  and whether `Shader.Find` finds the preview shaders.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorpreviewtexture/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorpreviewtexture/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorpreviewtexture/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorpreviewtexture/run.py gate
```

The probe uses the imguiskin project's scene, packages and settings. Residual (`residuals.json`):
Unbound's `Shader.Find` finds the Editor's own shaders, which the Editor's Play mode does not.
`previews.png` is compared without a limit (the view outside the cells is the frame's backdrop).
