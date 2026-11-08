# IMGUI GameSkin (Q18)

Records, in Play mode of the Unity 6000.0.71f1 Editor, the skin `GUI.skin` answers inside the first
`OnGUI` call: its name, flags, font and settings; every built-in and custom style's states (background
texture name, text colour), offsets, font, text and layout properties and line height; the styles the
skin enumerates and `FindStyle` finds; and every background texture's class, size, sampler, format and
pixels (copied through a RenderTexture, since the skin's textures are not readable). The capture is
windowed (on the private GPU display): a batch-mode Editor calls no `OnGUI`.

Contract: the Scripting API [GUISkin](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUISkin.html)
and [GUI.skin](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUI-skin.html) ("The global
skin to use"; the default when none is set) document the members; the values are this capture's.

```sh
python3 tests/projects/imguiskin/run.py record --label native-a
python3 tests/projects/imguiskin/run.py record --label native-b
python3 tests/projects/imguiskin/run.py publish native-a native-b
python3 tests/projects/imguiskin/run.py gate
```

Numbers compare with an absolute tolerance of 1e-6. The platform cursor flash speed (2) is the Linux
Editor's; macOS is unmeasured.
