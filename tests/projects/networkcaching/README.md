# AssetBundle cache, Caching API and audio downloads

Unity 6000.0.71f1 macOS Editor oracle (`Reference/unity-6000.0.71f1.jsonl`, 1040 compared
records; two independent captures were identical). A loopback server (`server.py`) serves
`Content/` on 127.0.0.1 and logs every request path, so the probe counts transfers; a path under
`/gated/` sends its first 16384 bytes and waits for the probe to release the rest. Nothing goes to
the internet.

`Content/bundles` holds three bundles the Editor built once (`ProbeRunner.Create`, one text asset
each): `alpha` uncompressed, `beta.bundle` chunk-based LZ4, `gamma.unity3d` LZMA, with their CRCs.
`Content/audio` holds generated tones: 16-bit and 24-bit PCM WAV, IMA ADPCM WAV, stereo Ogg Vorbis,
MP3 (CBR with ID3 and Info headers, CBR without either, stereo, VBR with a Xing header, MPEG-2),
a two-second WAV for partial reads, and a text file named `.wav`.

`NetworkCachingProbe` records:

- Caching and Cache: the default cache, AddCache (missing, empty, duplicate, trailing-slash and
  read-only directories), GetCacheAt/GetCacheByPath, Move/Remove, currentCacheForWriting,
  expirationDelay (and its 150-day limit), maximumAvailableStorageSpace, spaceOccupied/spaceFree,
  invalid caches and the exception texts.
- GetAssetBundle with a hash, a version (`Hash128(0,0,0,v)`), CachedAssetBundle (empty name too)
  and none: transfers counted, the result, response code, bytes, progress, the handler's error and
  the bundle; cache hits (code 0, no bytes), names from URLs (query and extension dropped), the
  cached files and their sizes (rewritten LZ4 or uncompressed archives per `compressionEnabled`),
  CRC checks of downloads and of cached versions, data that is no bundle, 404s, autoLoad,
  lookups across caches, MarkAsUsed, ClearCachedVersion/ClearOther/ClearAll/ClearCache with and
  without a version in use, ClearCache(expiration) and use times, an expiration delay of 1 s and a
  cache added again, the room a download needs and least-recently-used eviction.
- GetAudioClip for each file with no flag, `compressed` and `streamAudio`: the result, data and
  text, the clip's properties, GetData (16-bit precision summaries), errors for other containers,
  unsupported data and 404s, the flag warnings, and clips read while only part of a file arrived.

Waits use the system clock (cache times follow it). Paths are written relative to the run
(`<out>`, `<url>`). Keys starting with `os.` (the platform and the default cache's path) are
listed but not compared: the Editor's default cache is in the user's cache directory, Unbound's
in the project's `Library/Unbound/Caching`. Nothing else depends on the operating system.

Record the reference (macOS; on Linux pass `--editor` or use the default Hub path):

    python3 tests/projects/networkcaching/run.py editor --out ~/.cache/unbound/networkcaching-editor

Run Unbound and compare (the land gate, `gate.json`):

    UNBOUND=target/release/unbound python3 tests/projects/networkcaching/run.py check

Rebuild the served bundles (only when the bundled texts change; then record again):

    Unity -batchmode -quit -projectPath tests/projects/networkcaching -executeMethod ProbeRunner.Create

Unbound matches every compared record. Residual, below the recorded precision: MP3 samples can
differ by up to 3/32768 and a few Vorbis samples by 1/32768 from the Editor's decoders.
