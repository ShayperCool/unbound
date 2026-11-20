# EditorGUI.DrawRect

Two independent Unity 6000.0.71f1 Editor runs per color space capture pixels from a 320×180
Play mode Game view on Linux. The probe shares the scene, packages and project settings from
`imguiskin` and runs in Gamma and Linear. The comparator checks each sampled RGB channel exactly.

The documented API draws a filled rectangle at the supplied coordinates. The controls record
an opaque fill, component-wise GUI tint and alpha blending, the color and enabled state after
the call, group clipping, a translated and scaled GUI matrix, and rectangles with zero and
negative width. A disabled GUI at full alpha still draws opaque white. Calls in Layout have
no visible effect, and the call outside OnGUI records the exception type.
[Unity 6000.0 `EditorGUI.DrawRect`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorGUI.DrawRect.html)

The direct texture controls sample draws inside a disabled group and an opaque draw after
closing it. They cover tint alpha, source alpha and the `alphaBlend` setting, cropped UVs,
and a rounded rectangle. Each texture draw keeps the supplied alpha while the group is disabled.
[Unity 6000.0 `GUI.DrawTexture`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUI.DrawTexture.html)
[Unity 6000.0 `GUI.DrawTextureWithTexCoords`](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUI.DrawTextureWithTexCoords.html)

```sh
for space in Gamma Linear; do
    python3 tests/projects/editordrawrect/run.py record --space "$space" --label "native-$space-a"
    python3 tests/projects/editordrawrect/run.py record --space "$space" --label "native-$space-b"
    python3 tests/projects/editordrawrect/run.py publish --space "$space" "native-$space-a" "native-$space-b"
done
python3 tests/projects/editordrawrect/run.py gate
```
