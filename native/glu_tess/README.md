# Polygon tessellator

The SGI GLU tessellator from Mesa GLU 9.0.3, obtained from
https://archive.mesa3d.org/glu/glu-9.0.3.tar.xz. License B is retained in every source file.
Only `src/libtess`, `include/GL/glu.h`, and `src/include/gluos.h` are used.

Local changes: a minimal GL scalar header removes the need for system graphics headers;
the deletion sentinel uses decimal notation. The tessellator itself is unchanged.
The bounded, per-call adapter is `../unbound_model_tess.c`.
