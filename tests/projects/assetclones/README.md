# Asset instantiation

Runtime-created and imported Texture2D, RenderTexture and TextAsset clones, with Material,
ScriptableObject and GameObject controls. The probe records types, names, independent identities,
hide flags, persistence, AssetDatabase paths, sampler settings, formats, color space, readability,
every CPU mip, raw bytes, GPU mip samples, independent writes and destruction. TextAsset byte
array writes and NativeArray destruction are checked separately. Overloads include parent and
position arguments and null inputs.

RenderTexture captures descriptor and creation state before touching the clone's GPU storage.
It initializes each clone before reading it, then releases, recreates and destroys it while
checking the initialized original. Initial RenderTexture clone pixels are undefined and are
not certified. Texture2D inputs have explicitly initialized mip levels, including an unapplied
CPU edit control. AudioClip, Font, Sprite and other asset clone families are outside this probe.

```
python3 tests/projects/assetclones/run.py editor --release 6000.0.71f1 --label native-v1
python3 tests/projects/assetclones/run.py publish --release 6000.0.71f1 --label native-v1
UNBOUND_CLI=/path/to/private/unbound python3 tests/projects/assetclones/run.py unbound --release 6000.0.71f1 --label before-v1
python3 tests/projects/assetclones/compare.py tests/projects/assetclones/Reference/unity-6000.0.71f1/Main.jsonl /path/to/capture.jsonl --report /path/to/cache/union.json
```

Runs use fresh cache-only project copies and the installed common heavy-job launcher. The Editor
makes the minimal scene in each copy. The committed scene is the same scene used by Unbound.
Publishing requires byte-identical independent Native captures and verified NVIDIA renderers.
The comparator keeps the full key union and every failure, with fixed CPU/GPU color tolerances;
metadata and raw bytes compare exactly. Only Unity 6000.0.71f1 is measured.

## Measured behavior in 6000.0.71f1

`Reference/unity-6000.0.71f1/` is one release reference set. `Main.jsonl` has
14,340 values from two byte-identical GPU captures. `ColorGamma.jsonl` and
`ColorLinear.jsonl` each have 109 values from two independent agreeing captures.
The color controls distinguish the stored data color space from the active graphics format,
including explicit GraphicsFormat constructors, floating formats and imported textures.

```
python3 tests/projects/assetclones/color.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/assetclones/color.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/private/unbound python3 tests/projects/assetclones/color.py unbound --release 6000.0.71f1 --label <fresh-label>
```

Clones have new identities, `(Clone)` names, no asset path or persistence, and hide flags zero.
Parent and position overloads accept these assets. RenderTexture clones copy the descriptor and
sampler but start uncreated. Releasing an active RenderTexture clears the active target.

Texture2D cloning uploads an independent retained CPU snapshot, including unapplied edits,
even when `isReadable` is false. A RenderTexture-to-Texture2D GPU copy can therefore leave the
original green on the GPU and red in its retained CPU storage; the clone is red on the GPU.
Texture2D-to-Texture2D copies propagate retained storage even for unreadable textures. These
controls prevent substituting either an aliased GPU allocation or a stale CPU-only clone.
An engine texture represented only by GPU storage without a retained snapshot is explicitly
rejected. That remaining representation is outside the certified paths.

TextAsset preserves its bytes for imported `.txt` and `.bytes`, string and byte constructors,
and clones. Text detects valid UTF-8/16/32 BOMs. A failed strict decode falls back to UTF-8
replacement over the entire original byte array, including its BOM. The malformed BOM control
and valid BOM controls intentionally have different text results. The existing valid-BOM unit
expectation remains unchanged.

## Latest matching validation, 2026-11-03

The bound comparison passes 14,338/14,340 main values, up from 14,332 on the frozen
pass-count runtime, with no lost matches or missing/extra values. Exactly six dirty-source GPU
samples now match: `texture.dirty.{source,source_after_write,source_after_destroy}.gpu.m0.{0,1}`.
The two remaining differences are imported **source** hide flags (Native 8, engine 0); clone
flags still match zero. All failures and original tolerances remain in the full union.

Once a Texture2D has GPU storage, CPU writes leave those GPU texels intact until Apply or a GPU
copy. A clone independently uploads the retained CPU snapshot, including unapplied edits.
SetPixel here and SetPixels in samplerbias provide independent Native evidence. The same upload
rule covers SetPixels32, SetPixelData, LoadRawTextureData and ReadPixels, whose GPU visibility is
not independently measured. Five writer forms and cloning have managed regressions; first-use
and Reinitialize upload behavior is preserved, without a new Native claim for those paths.

Both color profiles pass 109/109 in captures bound to the complete runtime, including the earlier
Alpha8 metadata correction (unnamed enum value 54). This is not a new general utility or readback
conversion claim. The samplerbias cache control remains 171/171; textureraw remains 205/207
(the two old unreadable-storage lifetime answers); screencapture keeps 187 values and all 39
before/after image files byte-identical, with six existing image differences from Native.

AudioClip, Font and Cubemap instantiation remain unsupported by the unchanged dispatch. Sprite
cloning already has its own implementation and is outside this task. Material, ScriptableObject
and GameObject controls remain passing. Initial RenderTexture pixels, unmeasured texture formats,
other Unity releases and full engine or full clone parity are not certified here.

The matching suite passes 1661/1661, including eight new texture cases and four Start-phase
cases. An owned old-Core red control fails six texture cases and passes the two preservation
controls. Public/protected metadata is unchanged (65,228 rows), so API 0/2319 is reused; all 108
generated files are unchanged. Candidate manifest `d4078b07`, Core `fb2a7180`, reused CLI
`ce742394` and PhysX `487d450b` are recorded in
`validation/runs/start-texture-matching-validation-20261103-5/`; Root admission is in
`validation/runs/start-texture-root-admission-20261103/`. This batch adds no claim about earlier
graphics, resource or texture-import controls that it did not rerun. Raw captures and full unions
stay outside Git; canonical references are unchanged.
