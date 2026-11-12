#include "bc7e/basisu_bc7e_scalar.h"
#include <cstdint>
#include <cstring>

extern "C" void unbound_bc7e_init() {
    bc7e_scalar::bc7e_compress_block_init();
}

// `pixels` contains whole 4x4 RGBA blocks, with no image-row padding.
extern "C" void unbound_bc7e_blocks(const uint32_t* pixels, uint64_t* blocks,
                                   uint32_t count, int quality, bool perceptual) {
    bc7e_scalar::bc7e_compress_block_params settings{};
    if (quality >= 100)
        bc7e_scalar::bc7e_compress_block_params_init_slow(&settings, perceptual);
    else if (quality >= 50)
        bc7e_scalar::bc7e_compress_block_params_init_basic(&settings, perceptual);
    else
        bc7e_scalar::bc7e_compress_block_params_init_ultrafast(&settings, perceptual);
    bc7e_scalar::bc7e_compress_blocks(count, blocks, pixels, &settings);
}
