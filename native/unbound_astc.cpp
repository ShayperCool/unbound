// C interface to the Apache-2.0 licensed Arm astc-encoder 4.3.0.
#include "astcenc/astcenc.h"
#include <cstddef>
#include <cstdint>
#include <mutex>

extern "C" {
void* unbound_astc_context(unsigned side, bool hdr, float quality, bool decode) {
    // Initialization of the codec's CPU feature cache is serialized across import jobs.
    static std::mutex creation_mutex;
    std::lock_guard<std::mutex> guard(creation_mutex);
    astcenc_config config {};
    const auto profile = hdr ? ASTCENC_PRF_HDR : ASTCENC_PRF_LDR;
    const unsigned flags = decode ? ASTCENC_FLG_DECOMPRESS_ONLY : ASTCENC_FLG_SELF_DECOMPRESS_ONLY;
    if (astcenc_config_init(profile, side, side, 1, quality, flags, &config) != ASTCENC_SUCCESS)
        return nullptr;
    astcenc_context* context = nullptr;
    if (astcenc_context_alloc(&config, 1, &context) != ASTCENC_SUCCESS)
        return nullptr;
    return context;
}
void unbound_astc_free(void* context) {
    astcenc_context_free(static_cast<astcenc_context*>(context));
}
int unbound_astc_encode(void* context, void* pixels, bool hdr, unsigned width, unsigned height,
                        unsigned channel, bool opaque, uint8_t* output, size_t size) {
    astcenc_image image { width, height, 1, hdr ? ASTCENC_TYPE_F16 : ASTCENC_TYPE_U8, &pixels };
    astcenc_swizzle swizzle { ASTCENC_SWZ_R, ASTCENC_SWZ_G, ASTCENC_SWZ_B, opaque ? ASTCENC_SWZ_1 : ASTCENC_SWZ_A };
    if (channel == 1)
        swizzle = { ASTCENC_SWZ_R, ASTCENC_SWZ_R, ASTCENC_SWZ_R, ASTCENC_SWZ_1 };
    else if (channel == 2)
        swizzle = { ASTCENC_SWZ_0, ASTCENC_SWZ_0, ASTCENC_SWZ_0, ASTCENC_SWZ_A };
    auto* ctx = static_cast<astcenc_context*>(context);
    const auto status = astcenc_compress_image(ctx, &image, &swizzle, output, size, 0);
    astcenc_compress_reset(ctx);
    return status;
}
int unbound_astc_decode(void* context, const uint8_t* blocks, size_t size,
                        unsigned width, unsigned height, bool floats, void* output) {
    astcenc_image image { width, height, 1, floats ? ASTCENC_TYPE_F32 : ASTCENC_TYPE_U8, &output };
    const astcenc_swizzle swizzle { ASTCENC_SWZ_R, ASTCENC_SWZ_G, ASTCENC_SWZ_B, ASTCENC_SWZ_A };
    auto* ctx = static_cast<astcenc_context*>(context);
    const auto status = astcenc_decompress_image(ctx, blocks, size, &image, &swizzle, 0);
    astcenc_decompress_reset(ctx);
    return status;
}
}
