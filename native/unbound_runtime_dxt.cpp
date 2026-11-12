#include <cstdint>
#include <cstring>
#define STB_DXT_IMPLEMENTATION
#define STB_DXT_USE_ROUNDING_BIAS
#include "stb_dxt/stb_dxt.h"

extern "C" void unbound_runtime_dxt_init() {
    stb__InitDXT();
}

extern "C" void unbound_runtime_dxt_colour(const uint8_t* pixels, uint8_t* block,
                                          bool high_quality) {
    // The colour routine reads whole pixels. Give it aligned local storage.
    uint32_t aligned[16];
    std::memcpy(aligned, pixels, sizeof(aligned));
    stb__CompressColorBlock(block, reinterpret_cast<unsigned char*>(aligned),
                           high_quality ? STB_DXT_DITHER : STB_DXT_NORMAL);
}
