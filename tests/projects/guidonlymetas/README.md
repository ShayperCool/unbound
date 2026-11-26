# Assets whose .meta holds the GUID alone

What the Editor imports, and writes, for textures, sounds and a model whose `.meta` holds the
format version and GUID and nothing else (no importer section). Unity 6000.0.71f1, recorded with
the macOS Editor in batch mode (two identical captures): nothing here depends on the OS or the
graphics device; the installation's platform modules' texture entries are left out of each text
(`.platforms` says whether those left out were the installed modules).

`Assets/Guid` holds PNGs of 16x16 (`Square`, `TexSaved`), 32x16 (`Wide`) and 96x16 (`Strip`), a
`Aspect<w>x<h>` images of aspects between and beyond the AutoCubemap layouts' (1:16, 1:4, 0.68,
1.13, 1.22, 1.4, 1.53, 2.85, 6:1, 8:1, 16:1, 256:1), a mono WAV twice (`Beep`, `BeepSaved`) and an OBJ triangle with a material from its MTL file (`Tri`,
the MTL with a DefaultImporter `.meta` of its own), each `.meta` the GUID alone.

`Assets/Scripts/GuidOnlyMetasProbe.cs` records in Play mode into values.jsonl in
`$GUIDONLYMETAS_OUT` (tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:

* `open/files`, `open/<path>`: the folder's files and each `.meta` as the project opened.
* `texture/<name>`, `.../importer`: the imported texture (class, dimension, size, mips, sampler,
  format) and its importer's settings; `sound/<name>`, `.../importer`: the clip and its importer's;
  `model/Tri`, `.../renderer`, `.../importer`: the model's objects, its renderer's materials and its
  importer's settings; `aspect/<name>`: each aspect image as imported.
* `save/TexSaved`, `save/BeepSaved`, `.../meta`: `isReadable` and `forceToMono` set and saved by
  `SaveAndReimport`, the import then and the file; `end/files`.

What it shows: opening the project writes none of these `.meta` files. A texture imports with the
texture importer's oldest settings: a cube map (from the 16x16 image 8x8 faces, from the others
16x16) made automatically, uncompressed (RGB24), with mip maps, gamma decoding and Clamp, every
aspect a cube map (the face sizes give the layout: the corner square for the long, tall and 0.68,
1.22, 2.85 images, a sphere map at 1.13, a horizontal cross at 1.4; 1.53 is a corner square or a
panorama, the same size: Unbound imports such an unmeasured aspect as a 2D texture with a
warning); a sound
compressed in memory (Vorbis, quality 1), not preloaded. A save writes the importer's whole section
with those settings and the change (a texture's installed platforms listed). A model imports
legacy-style: its materials written into a `Materials` folder as Standard materials as the project
opens, a Legacy animation, Read/Write on (Unbound does not: `residuals.json`).

Not covered: the pixels of the cube faces, other image and sound formats, a `.meta` with labels but no section (Unbound reads it
as one with the GUID alone), one with other lines, the other importers.

Record and gate:

    python3 tests/projects/guidonlymetas/run.py record --label a   # twice, then
    python3 tests/projects/guidonlymetas/run.py publish a b
    python3 tests/projects/guidonlymetas/run.py gate
