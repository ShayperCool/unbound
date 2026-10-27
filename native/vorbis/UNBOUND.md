# libvorbis 1.3.7 and libogg 1.3.6 (vendored)

[libvorbis](https://xiph.org/vorbis/) 1.3.7 (`libvorbis-1.3.7.tar.xz`, sha256
`b33cc4934322bcbf6efcbacf49e3ca01aadbea4114ec9589d1b1e9d20f72954b`) and
[libogg](https://xiph.org/ogg/) 1.3.6 (`libogg-1.3.6.tar.xz`, sha256
`5c8253428e181840cd20d41f3ca16557a9cc04bad4a3d04cce84808677fa1061`), both BSD-3 licensed
(`COPYING`, `ogg/COPYING`). Kept: libvorbis' `include/vorbis/{codec,vorbisenc}.h` and `lib/`
without its tools (`barkmel.c`, `psytune.c`, `tone.c`), `vorbisfile.c` and build files; libogg's
`include/ogg/{ogg,os_types}.h` and `src/`. `ogg/include/ogg/config_types.h` is ours (configure's
output, for `stdint.h` platforms). Otherwise unmodified.

Why: the Vorbis clips of an AssetBundle (FMOD's FSB5) keep only their audio packets and the CRC32 of
the setup header FMOD's encoder (libvorbis) wrote. `crates/unbound-assets` (`build.rs`, `fsb5.rs`)
compiles this into the engine, makes that header again by encoding nothing at the qualities FMOD
uses until the CRC matches, and decodes the packets. Every header FMOD writes at 32 kHz and above
is libvorbis 1.3.7's (measured over rates, channels and qualities); below 32 kHz FMOD's headers are
its own, and such clips fail with a message naming them.

To update: copy the same files from a newer release and update the versions and hashes above; the
`fsb5` tests check the headers still match the Editor's bundles.
