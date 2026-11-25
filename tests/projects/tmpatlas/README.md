# Texture and TextMeshPro font asset writes, and the quit callbacks

Which project files a Play mode session writes when a script changes texture assets and when
TextMeshPro's dynamic font assets fill, and the order of the callbacks as Play mode ends and the
Editor quits. Recorded with the Unity 6000.0.71f1 macOS Editor in batch mode (two identical
captures): none of this depends on the OS or the graphics device.

Each run's copy of the project gets TextMeshPro's Essential Resources (its shaders and settings)
from the ugui package of the Unity installation, as the Editor's importer window adds them;
nothing of them is committed, and the driver lists them only when a run changes them.

`run.py create` (once, its outputs are committed) has the Editor make `Assets/Tex`: a 4x4 RGBA32
texture asset per change below (`LinearReinitRGBA32` linear), and `RenamedMaterial.mat`; and
`Assets/Fonts`: three dynamic SDF font assets of `Probe.ttf` (the font of tests/projects/textcore),
`Dynamic SDF` with an empty 1x1 atlas, `Filled SDF` and `Kept SDF` saved with a character in a
256x256 atlas; both were made with "Clear Dynamic Data On Build" off, which the driver turns on
in `Filled SDF`'s file.

`Assets/Editor/TmpAtlasProbe.cs` (`-executeMethod TmpAtlasProbe.Record` / `unbound run`) enters
Play mode and changes each texture one way: `SetPixel`, `SetPixel` then `Apply`,
`LoadRawTextureData`, `filterMode`, `wrapMode`, `anisoLevel`, `mipMapBias`, a rename,
`Reinitialize` to another and to its own size, `Reinitialize` with the TextureFormats Alpha8,
RGB24, RFloat and R8 and the GraphicsFormats R8G8B8A8_UNorm and _SRGB, and RGBA32 on the linear
texture; it renames the material and adds characters to `Dynamic SDF`. It records which assets are
dirty, each texture's graphics format and whether its data is sRGB (`op.*`, `font.*`), leaves Play
mode and quits the Editor, recording the quit callbacks in their order (`quit.N.<callback>` with
whether Play mode was on). `run.py` adds `exit.<path>` for every file after the process ended
(tests/file_writes.py).

Measured: a script's `Apply`, `Reinitialize` (also to the texture's own size), a sampler setting
and a rename dirty an asset, a material's rename too, and they are written at the Editor's exit;
`SetPixel` alone and `LoadRawTextureData` do not. `Reinitialize` with a TextureFormat makes the
texture the format's linear variant (`RGB24` is not sRGB afterwards); with a GraphicsFormat it
takes that format's colour space; with only a size it keeps its own. TextMeshPro's first character
of an empty dynamic atlas reinitializes it, so the atlas is dirty (`font.Dynamic.after`). Leaving
Play mode raises ExitingPlayMode, `Application.quitting` (Play mode still on), EnteredEditMode;
the Editor's exit then raises `EditorApplication.quitting` and saves the dirty assets:
TextMeshPro's quit handler clears every dynamic font asset that clears its data and has a used
atlas, which reinitializes the atlas as a 1x1 linear Alpha8 texture, so `Dynamic SDF` and
`Filled SDF` are written with empty tables and that atlas, and `Kept SDF` is left as it is.
`EditorApplication.Exit` raises no `wantsToQuit`.

Residuals (`residuals.json`): a texture reinitialized in a float format holds the bytes 0xCD in
the Editor; Unbound's fill reads 205/255 in every format.

Unmeasured: a sampler setting or a rename to the value it has (Unbound leaves the asset clean), an
imported asset's (Unbound leaves it clean), other texture classes, the Editor quitting while Play
mode is on.

Create (once), record (macOS, holding the machine's heavy-job lock), publish and check:

    python3 tests/projects/tmpatlas/run.py create
    python3 tests/projects/tmpatlas/run.py native --label a
    python3 tests/projects/tmpatlas/run.py native --label b
    python3 tests/projects/tmpatlas/run.py publish a b
    python3 tests/projects/tmpatlas/run.py check
