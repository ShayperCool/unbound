# freetype-sys patch ledger

This is `freetype-sys` **0.23.0** as published on crates.io (archive SHA-256
`eab537ce43cab850c64b4cdc390ce7e4f47f877485ddc323208e268280c308ae`, the checksum Cargo.lock
recorded for the registry package), without its bundled `freetype2/` and `libpng/` source
trees. `Cargo.toml`, `src/`, `LICENSE` and `README.md` are the published files, unchanged. The
workspace's `[patch.crates-io]` entry replaces the registry package with this directory; no
dependency version changes.

`build.rs` is the only changed file. Upstream copies `libpng/scripts/pnglibconf.h.prebuilt` to
`libpng/pnglibconf.h` inside the crate's own directory on every build, which is the shared Cargo
registry: concurrent builds rewrite a header other builds are compiling, and a read-only Cargo home
fails. Here the bundled sources come from `native/freetype/source.py`, which extracts them from
the same published archive (checked against the SHA-256 above) into Unbound's dependency cache once
and makes that header there. The compiled file list, include directories, defines and link
libraries are upstream's, except for upstream's `libz-sys/src/zlib` include directory, which the
crate does not contain (zlib's header comes from the system).

The non-`bundled` (pkg-config) path is unchanged. `UNBOUND_FREETYPE_ARCHIVE` supplies the archive
instead of a download; `UNBOUND_OFFLINE` or `CARGO_NET_OFFLINE` forbid downloading.
