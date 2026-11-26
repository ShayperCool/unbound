# A texture importer's per-platform settings and PNG gamma

What `TextureImporter`'s per-platform settings API reads, stores and clears, the `.meta` files it
saves, and how a PNG's gamma (`ignorePngGamma`) changes the imported pixels. Unity 6000.0.71f1,
recorded with the macOS Editor in batch mode (two identical captures): the values depend on
neither the OS nor the graphics device. The installation's platform modules do: the recording
Editor has Android's, and every `.meta` here lists iOS (and all but a few Android) so that an
installation with either adds nothing when an importer loads; only Android and Standalone are
missing on purpose.

`Assets/Textures` holds 16x16 PNGs (a 64x64 one for the size) whose `.meta` files are a new
texture's settings with these platform entries:

| texture | entries |
| --- | --- |
| `Get` | the default changed (4096, Bilinear, high quality, 70, crunched, alpha split, ETC2 fallback 16-bit), Standalone not overridden (1024, format 4), Android overridden (512, ETC2 RGBA8, 80), WebGL overridden (256, format 12, no module installed), no iOS |
| `Set`, `SetOff`, `Legacy` | the four platforms at their defaults (`SetOff`: Android overridden; `Legacy`: no Android) |
| `Clear` | the default 1024, every other platform overridden, WebGL too |
| `ClearDefault`, `DefaultProps`, `LoadCopy` | no Android (the default 1024 and quality 20; at its defaults; 512, 60, crunched) |
| `LoadOrder`, `LoadNoDefault` | no Android and no Standalone; no default |
| `Size` | Standalone not overridden at 32 |
| `Gamma*` | uncompressed, readable, no mip maps: a ramp (every 8-bit value once in red), a gAMA chunk of 1.0, 0.45455, 0.5, 2.2, 0.47, 0.43, 0 or 0.0001, an sRGB chunk with 1.0, `ignorePngGamma` set, sRGB off, RGB, grey, palette (a gAMA chunk before and after the palette) and 16-bit images |

`Assets/Scripts/TexturePlatformsProbe.cs` records in Play mode into values.jsonl in
`$TEXTUREPLATFORMS_OUT` (tests/value_probe.py), each `.meta` with its GUID line read `guid: (guid)`:

* `get/<platform>`: `GetPlatformTextureSettings` of names, aliases, other letter case, `""` and
  null; `get/default`, the default platform's importer properties, the `out` overloads, a copy
  changed and read again, the default changed through a property, whether reads dirty the importer.
* `set/<name>`: `SetPlatformTextureSettings` of an object naming each platform (aliases, one not
  installed, an unknown name, `""`, null, overridden or not), then null; the argument's name after
  the call, the read, dirtiness; `set/meta` after `SaveAndReimport`, `set/after/*` the importer
  then. `setoff/*`: overridden entries stored not overridden. `legacy/*`: the obsolete overloads.
* `clear/<name>`: `ClearPlatformTextureSettings` of each platform, a missing one, `""`, null, the
  default, then a read and the saved `.meta`.
* `props/*`: the default's `allowAlphaSplitting`, `androidETC2FallbackOverride`,
  `crunchedCompression`; `load/*`: what the importer added on loading, read before and after the
  default changes, and saved.
* `size/*`: the imported size with Standalone (the active platform) not overridden at 32,
  overridden, then cleared.
* `gamma/<texture>`: `ignorePngGamma` and the imported pixels; `gamma/Gamma100000Toggle/set` after
  setting it and saving, `.../settings` read through `TextureImporterSettings` and set back off.

What it shows: a read gives a copy of the platform's entry under the platform's serialization name
(`iPhone` reads `iOS`, every desktop target `Standalone`); a platform without one reads the
default's settings, not overridden. When an importer loads, the platforms it imports for that its
`.meta` lacks get an entry (Standalone, then the installed modules, each the default entry as it
was then, which later changes of the default do not reach; a missing default at its defaults
after the others), and so does such a platform when a read asks for one (a platform without a
module does not). Set stores every field of the object, overridden or not and whatever the name,
in the entry of that name or after the others, and makes the importer dirty; it renames the
argument; null throws. Clear removes the entry and makes the importer dirty, also when there is
none. Reads do not make it dirty. A save writes the entries as the importer holds them; one it
added on loading alone is no change. The imported size follows the active platform's entry only
when that is overridden. A PNG's gamma corrects its colour for a 2.2 screen (alpha left), with
libpng's fixed-point exponent and table, unless the correction is within 5% of none, the gamma is
one libpng rejects (0, 0.0001), the gAMA chunk follows the palette, an sRGB chunk is present or
`ignorePngGamma` is set (which `TextureImporterSettings` reads and sets too); 16-bit samples are
corrected before they are narrowed.

Not covered: a default and another platform both missing on load (Unbound adds the default
last), a read adding an entry after the default changed (Unbound copies the default as it is
then), a save after reads alone added entries or after only an added entry was cleared (Unbound
writes nothing), a save after an entry the importer added was edited (a property of an added default, a SerializedObject edit: Unbound writes the list with it), values a platform
validates (sizes, formats, qualities out of range), the format quality a platform reads for BC6H
and BC7 (the sprite atlas importer's rule is not applied), files of older serialized versions,
PNGs with an iCCP chunk, gAMA values above libpng's range, an sRGB chunk after the palette, the rounding of 16-bit samples between the 256 measured ones, and the
runtime's `ImageConversion.LoadImage` (Unbound keeps a PNG's samples there).

Record and gate:

    python3 tests/projects/textureplatforms/run.py record --label a   # twice, then
    python3 tests/projects/textureplatforms/run.py publish a b
    python3 tests/projects/textureplatforms/run.py gate
