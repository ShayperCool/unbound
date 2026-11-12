# BC7 encoder

The scalar BC7 encoder is from [Basis Universal](https://github.com/BinomialLLC/basis_universal/tree/073eb6a0a34a23d77e75e54f41dcd9653a52da83/encoder),
revision `073eb6a0a34a23d77e75e54f41dcd9653a52da83`, under Apache-2.0.
`basisu_bc7e_scalar.cpp` and `.h` are unchanged upstream sources. The original bc7e encoder
is copyright 2018-2020 Binomial LLC. See LICENSE and the source notices.

`../unbound_bc7e.cpp` supplies the C interface, tiles small levels and arranges image rows into
blocks. Initialization runs once before parallel calls. The build disables fused products.
The settings are measured against Unity 6000.0.71f1 in tests/projects/texturecompression.
