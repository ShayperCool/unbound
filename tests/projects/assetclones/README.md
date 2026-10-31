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
