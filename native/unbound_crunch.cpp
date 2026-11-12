// C interface to the zlib-licensed public Crunch fork; see crunch/README.md.
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <initializer_list>
#include <mutex>
#include "crn_core.h"
#include "crn_console.h"
#include "crnlib.h"
#include "crn_defs.h"

namespace {
// UnityEngine.TextureFormat values, also mapped by Rust's CrunchFormat.
constexpr unsigned Dxt1Crunched = 28, Dxt5Crunched = 29;
constexpr unsigned Etc1Crunched = 64, Etc2RgbaCrunched = 65;
// The archive variant recorded by texturecompression's 6000.0.71f1 references.
constexpr unsigned ArchiveVariant = 1;
crn_format codec_format(unsigned format) {
    switch (format) {
        case Dxt1Crunched: return cCRNFmtDXT1;
        case Dxt5Crunched: return cCRNFmtDXT5;
        case Etc1Crunched: return cCRNFmtETC1;
        case Etc2RgbaCrunched: return cCRNFmtETC2A;
        default: return cCRNFmtInvalid;
    }
}
unsigned texture_format(crn_format format) {
    switch (format) {
        case cCRNFmtDXT1: return Dxt1Crunched;
        case cCRNFmtDXT5: return Dxt5Crunched;
        case cCRNFmtETC1: return Etc1Crunched;
        case cCRNFmtETC2A: return Etc2RgbaCrunched;
        default: return 0;
    }
}

bool archive_info(const uint8_t* bytes, size_t size, crnd::crn_texture_info& info) {
    if (!bytes || size < sizeof(crnd::crn_header) || size > UINT32_MAX) return false;
    const auto& header = *reinterpret_cast<const crnd::crn_header*>(bytes);
    const size_t header_size = header.m_header_size, data_size = header.m_data_size;
    const unsigned levels = header.m_levels;
    if (!levels || levels > cCRNMaxLevels || header_size > data_size || data_size > size
        || header_size < offsetof(crnd::crn_header, m_level_ofs) + levels * sizeof(uint32_t)
        || header.m_flags != 0 || header.m_userdata0 != ArchiveVariant) return false;
    const auto range = [=](size_t offset, size_t length) {
        return offset <= data_size && length <= data_size - offset
            && (!length || offset >= header_size);
    };
    for (const auto* palette : {&header.m_color_endpoints, &header.m_color_selectors,
                                &header.m_alpha_endpoints, &header.m_alpha_selectors}) {
        if (palette->m_num > cCRNMaxPaletteSize || !range(palette->m_ofs, palette->m_size)) return false;
    }
    if (!range(header.m_tables_ofs, header.m_tables_size)) return false;
    size_t previous = header_size;
    for (unsigned mip = 0; mip < levels; ++mip) {
        // The file has a variable-length offset array within the checked header extent.
        const auto* offsets = bytes + offsetof(crnd::crn_header, m_level_ofs);
        const size_t offset = (size_t(offsets[mip * 4]) << 24)
            | (size_t(offsets[mip * 4 + 1]) << 16)
            | (size_t(offsets[mip * 4 + 2]) << 8) | offsets[mip * 4 + 3];
        if (offset < previous || offset >= data_size) return false;
        previous = offset;
    }
    return crnd::crnd_validate_file(bytes, static_cast<uint32_t>(size), nullptr)
        && crnd::crnd_get_texture_info(bytes, static_cast<uint32_t>(size), &info)
        && info.m_faces == 1 && texture_format(info.m_format) != 0;
}
}

extern "C" {
int unbound_crunch_encode(const uint32_t* pixels, size_t count, unsigned width, unsigned height,
                         unsigned levels, unsigned format, unsigned quality, bool perceptual,
                         void** output, size_t* length) {
    if (!output || !length) return 0;
    *output = nullptr;
    *length = 0;
    if (!pixels || !width || !height || width > cCRNMaxLevelResolution
        || height > cCRNMaxLevelResolution || !levels || levels > cCRNMaxLevels
        || codec_format(format) == cCRNFmtInvalid) return 0;
    try {
        // The codec has shared initialization and output state. Imports serialize these calls.
        static std::mutex mutex;
        std::lock_guard<std::mutex> guard(mutex);
        crnlib::console::disable_output();
        crn_comp_params params;
        params.m_width = width;
        params.m_height = height;
        params.m_levels = levels;
        params.m_format = codec_format(format);
        params.m_userdata0 = ArchiveVariant;
        params.m_quality_level = static_cast<unsigned>((quality > 100 ? 100 : quality) * 0.01 * 255);
        params.set_flag(cCRNCompFlagPerceptual, perceptual);
        // The portable library path has no worker threads; imports use the engine's job pool.
        params.m_num_helper_threads = 0;
        size_t at = 0;
        for (unsigned mip = 0; mip < levels; ++mip) {
            const size_t size = size_t(width) * height;
            if (at > count || size > count - at) return 0;
            params.m_pImages[0][mip] = pixels + at;
            at += size;
            width = width > 1 ? width / 2 : 1;
            height = height > 1 ? height / 2 : 1;
        }
        if (at != count) return 0;
        crn_uint32 bytes = 0;
        *output = crn_compress(params, bytes);
        *length = bytes;
        return *output != nullptr;
    } catch (...) { return 0; }
}
void unbound_crunch_free(void* archive) { crn_free_block(archive); }

int unbound_crunch_info(const uint8_t* archive, size_t size, uint32_t* metadata) {
    if (!metadata) return 0;
    try {
        crnd::crn_texture_info info;
        if (!archive_info(archive, size, info)) return 0;
        metadata[0] = texture_format(info.m_format);
        metadata[1] = info.m_width;
        metadata[2] = info.m_height;
        metadata[3] = info.m_levels;
        return 1;
    } catch (...) { return 0; }
}
int unbound_crunch_unpack(const uint8_t* archive, size_t size, uint8_t* output, size_t capacity) {
    if (!output) return 0;
    try {
        crnd::crn_texture_info info;
        if (!archive_info(archive, size, info)) return 0;
        struct Context {
            crnd::crnd_unpack_context value;
            ~Context() { if (value) crnd::crnd_unpack_end(value); }
        } context {crnd::crnd_unpack_begin(archive, static_cast<uint32_t>(size))};
        if (!context.value) return 0;
        size_t at = 0;
        unsigned width = info.m_width, height = info.m_height;
        bool success = true;
        for (unsigned mip = 0; mip < info.m_levels; ++mip) {
            const unsigned pitch = ((width + 3) / 4) * info.m_bytes_per_block;
            const size_t bytes = size_t(pitch) * ((height + 3) / 4);
            if (at > capacity || bytes > capacity - at) { success = false; break; }
            void* destination = output + at;
            if (!crnd::crnd_unpack_level(context.value, &destination, bytes, pitch, mip)) { success = false; break; }
            at += bytes;
            width = width > 1 ? width / 2 : 1;
            height = height > 1 ? height / 2 : 1;
        }
        return success && at == capacity;
    } catch (...) { return 0; }
}
}
