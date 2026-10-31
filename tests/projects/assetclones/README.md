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

## Strict residuals

The matching CLI/Core comparison preserves all 6,281 previously passing main values and resolves
8,006, with no introduced differences. It passes 14,287/14,340, with no missing or extra values.
All 53 differences remain in the full union: 45 sampler-bias GPU values, two imported **source**
hide flags (Native 8, engine 0; clone flags match zero), and six unapplied **source** CPU-edit GPU
samples. The sampler follow-up belongs to shared rendering work. These are not tolerance waivers.

Each color profile passes 105/109, with four retained Alpha8 graphics-format differences:
Native reports unnamed enum value 54 while the existing engine format utility reports R8_UNorm.
Both original and clone values remain in each union. This does not establish a general utility or
readback conversion for the unnamed format.

AudioClip, Font and Cubemap instantiation remain unsupported by the unchanged dispatch. Sprite
cloning already has its own implementation and is outside this task. Material, ScriptableObject
and GameObject controls remain passing. Initial RenderTexture pixels, unmeasured texture formats,
other Unity releases and full engine or full clone parity are not certified here.
