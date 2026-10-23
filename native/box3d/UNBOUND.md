# Box3D (vendored)

[Box3D](https://github.com/erincatto/box3d) by Erin Catto, MIT licensed (`LICENSE`), at commit
`f36d061ba26998c87cb7dc593a4065aaa03fb7eb` (2026-10-20). Only `include/` and `src/` are kept, unmodified;
its CMake files are replaced by `crates/unbound-host/build.rs`, which compiles these sources with
`native/unbound_box3d.c` into the `unbound_box3d` shared library that the managed 3D physics calls.

To update: copy `include/` and `src/` from a newer checkout, drop `src/CMakeLists.txt`,
`src/box3d.natvis` and `src/box3d.pc.in`, update the commit above, and adjust the shim if the API moved.
