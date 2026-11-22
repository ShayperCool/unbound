# The Editor's GUI skins, style catalog and fonts

The Unity 6000.0.71f1 Play mode capture (Linux, dark theme) records, from a game `OnGUI`:

- `gui/*`: which skin `GUI.skin` is there (the game's) and the Editor's theme.
- `skin/<Game|Inspector|Scene>/*`: each skin `EditorGUIUtility.GetBuiltinSkin` answers: its name,
  font and settings, and for every built-in and custom style its layout and text fields, the
  eight states' background names, text colours and scaled background counts, its line height and
  the size of "Sample Text". `Scene` is the dark skin, the skin of the capture's theme: its rows
  hold the values the Editor's style catalog gives the styles (and no backgrounds for those).
- `font/<font>/*`: the fonts of the Editor's resources its skins' styles name: the font, the
  line heights and the widths of texts with and without kerning pairs in normal and bold style,
  the font asset IMGUI text of it is made with (face, atlas, which earlier font shares the
  asset, the faces of its weight table) and the asset's feature record counts afterwards (not
  for the Inter fonts: the Editor draws its own windows with them, so their assets' records
  count that text too).
- `load/*`: the order `EditorGUIUtility.Load` looks in: the project has a font under
  `Assets/Editor Default Resources` at the path of one of the Editor's fonts (it is the one
  loaded), none at the path of another (the Editor's is loaded), and neither has the third.
- `osAsset/<family>/*`: a font asset made of an OS family by name and the feature records it has
  once the characters of a text with kerning pairs are added.

Unity 6000.0 documentation:
- [`EditorGUIUtility.GetBuiltinSkin`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUIUtility.GetBuiltinSkin.html)
  ("Get one of the built-in GUI skins, which can be the game view, inspector or scene view skin").
- [`EditorGUIUtility.Load`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUIUtility.Load.html)
  (looks in `Assets/Editor Default Resources`, then in the Editor's built-in resources).
- [`EditorSkin`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorSkin.html)
- [`FontAsset.CreateFontAsset`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextCore.Text.FontAsset.CreateFontAsset.html)

The style catalog and the font families are not documented: the rows are observations of this
release on Linux with the dark theme. The OS families are the ones installed on the capture
machine (`osAsset/DejaVu Sans` has no "Regular" style there); a machine without Liberation Sans
draws the system fonts differently, in the Editor too.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorskin/run.py record --label native-a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorskin/run.py record --label native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorskin/run.py publish native-a native-b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/editorskin/run.py gate
```

The project takes its scene, packages and settings from `tests/projects/imguiskin`. Its font
under `Assets/Editor Default Resources` is that probe's `ProbeSerif.ttf` (`FONT-LICENSE.txt`). Numbers
compare within 1e-6.

## Residuals

`residuals.json` names two:

- Font feature records of OS font assets (31 rows): the Editor takes the pair adjustment and
  mark records of some OS faces and none of others; Unbound gives no OS font asset any, so text
  with a kerning pair is wider in the Editor's system fonts.
- The atlas population mode of the Inter and Roboto Mono family assets (2 rows): `DynamicOS` in
  the Editor, `Dynamic` in Unbound, which makes them of the fonts of the Editor's resources.

## Reference

`Reference/unity-6000.0.71f1/values.jsonl`: two independent, byte-identical Editor captures.
