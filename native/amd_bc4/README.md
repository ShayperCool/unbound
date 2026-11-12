# Scalar channel encoder

Adapted from the scalar channel routines of AMD Compressonator's
[compressonatorxcodec.cpp](https://github.com/GPUOpen-Tools/compressonator/blob/095a143ad8cfbf9ed5afa4ea63c735bfd0fd04dd/cmp_compressonatorlib/ati/compressonatorxcodec.cpp),
revision `095a143ad8cfbf9ed5afa4ea63c735bfd0fd04dd`, retrieved 2026-11-12. Its MIT licence notice is retained in encode.cpp.
The adapter retains only unsigned byte channels, packs BC4 blocks and handles constant blocks
without fitting. BC5 uses the same routine for each channel.
