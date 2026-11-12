# Crunch

The build uses Unity Technologies' public [Crunch fork](https://github.com/Unity-Technologies/crunch/tree/8708900eca8ec609d279270e72936258f81ddfb7), revision `8708900eca8ec609d279270e72936258f81ddfb7`, library version 1.04. Its zlib license is copied in `LICENSE.txt`. The fork's ETC encoder is included; no ETCPACK implementation is used.

`source.py` downloads the exact source archive, checks SHA-256, and prepares it under `$XDG_CACHE_HOME/unbound/dependencies/crunch`. `UNBOUND_CRUNCH_ARCHIVE` can supply that same archive locally. `UNBOUND_OFFLINE` and Cargo's offline setting prohibit downloads. The source URL, revision and archive checksum are fixed in the script. An interprocess lock and atomic directory publication allow concurrent checkouts to share the source cache.

Prepared source carries modification notices for portability patches:

- `crn_vector.cpp` selects the wide integer overloads explicitly.
- `crn_platform.h` uses portable traps and the standard file API on Apple platforms.
- `crn_mem.cpp` uses Apple's allocation-size API there.
- `crn_console.h` includes the terminal headers at file scope, outside `namespace crnlib`; the macOS SDK otherwise leaves the global `uuid_t` undeclared.
- `crn_timer.cpp` includes the portable time declarations.
- `crn_jpge.cpp` and `inc/crn_decomp.h` use Apple's allocation declarations there.

The build disables fast-math and fused multiply-add contraction. The portable ANSI path uses no helper threads. Archive and transcoded-block comparisons are recorded by `tests/projects/texturecompression` against Unity 6000.0.71f1.
