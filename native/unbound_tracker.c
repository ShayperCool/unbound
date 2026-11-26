/* Flat C wrapper over libxmp-lite for Unbound's tracker module importer and downloader.
 * Decodes .mod, .xm, .s3m and .it modules into stereo interleaved f32 PCM samples. */
#include <limits.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "xmp.h"

enum {
    UNBOUND_TRACKER_ANY = 0,
    UNBOUND_TRACKER_MOD = 1,
    UNBOUND_TRACKER_XM  = 2,
    UNBOUND_TRACKER_S3M = 3,
    UNBOUND_TRACKER_IT  = 4,
};

/* Cap rendered output at one hour to protect against malformed or endlessly looping modules. */
#define UNBOUND_TRACKER_MAX_SECONDS 3600

static int format_from_type_name(const char *type) {
    if (type == NULL) return 0;
    if (strcmp(type, "Protracker") == 0) return UNBOUND_TRACKER_MOD;
    if (strcmp(type, "Fast Tracker II") == 0) return UNBOUND_TRACKER_XM;
    if (strcmp(type, "Scream Tracker 3") == 0) return UNBOUND_TRACKER_S3M;
    if (strcmp(type, "Impulse Tracker") == 0) return UNBOUND_TRACKER_IT;
    return 0;
}

int unbound_tracker_detect(const uint8_t *data, size_t len) {
    if (data == NULL || len == 0 || len > (size_t)LONG_MAX) {
        return 0;
    }
    struct xmp_test_info info;
    memset(&info, 0, sizeof(info));
    if (xmp_test_module_from_memory(data, (long)len, &info) != 0) {
        return 0;
    }
    return format_from_type_name(info.type);
}

int unbound_tracker_decode(
    const uint8_t *data,
    size_t len,
    int sample_rate,
    int expected_format,
    float **out_samples,
    size_t *out_frames,
    int *out_format
) {
    if (out_samples) *out_samples = NULL;
    if (out_frames) *out_frames = 0;
    if (out_format) *out_format = 0;
    if (data == NULL || len == 0 || len > (size_t)LONG_MAX) {
        return -1;
    }
    if (sample_rate < XMP_MIN_SRATE || sample_rate > XMP_MAX_SRATE) {
        return -2;
    }
    int actual = unbound_tracker_detect(data, len);
    if (actual == 0) {
        return -3;
    }
    if (expected_format != UNBOUND_TRACKER_ANY && actual != expected_format) {
        return -4;
    }

    xmp_context ctx = xmp_create_context();
    if (ctx == NULL) {
        return -5;
    }

    /* Amiga MOD files have no panning commands; 70% default separation avoids hard-L/R silence on
     * headphones while XM/S3M/IT files keep their explicit channel panning. */
    if (actual == UNBOUND_TRACKER_MOD) {
        xmp_set_player(ctx, XMP_PLAYER_DEFPAN, 70);
    }

    if (xmp_load_module_from_memory(ctx, data, (long)len) != 0) {
        xmp_free_context(ctx);
        return -6;
    }

    if (xmp_start_player(ctx, sample_rate, 0) != 0) {
        xmp_release_module(ctx);
        xmp_free_context(ctx);
        return -7;
    }

    xmp_set_player(ctx, XMP_PLAYER_INTERP, XMP_INTERP_LINEAR);
    /* Keep channel panning unattenuated (100% stereo separation); MOD's default 70% separation is
     * already applied via XMP_PLAYER_DEFPAN before loading. */
    xmp_set_player(ctx, XMP_PLAYER_MIX, 100);

    size_t max_frames = (size_t)sample_rate * UNBOUND_TRACKER_MAX_SECONDS;
    size_t capacity = (size_t)sample_rate * 2; /* initial 2 s stereo */
    size_t frames = 0;
    float *samples = (float *)malloc(capacity * 2 * sizeof(float));
    if (samples == NULL) {
        xmp_end_player(ctx);
        xmp_release_module(ctx);
        xmp_free_context(ctx);
        return -8;
    }

    struct xmp_frame_info fi;
    while (xmp_play_frame(ctx) == 0) {
        xmp_get_frame_info(ctx, &fi);
        if (fi.loop_count > 0) {
            break;
        }
        if (fi.buffer == NULL || fi.buffer_size <= 0) {
            continue;
        }
        size_t chunk_frames = (size_t)fi.buffer_size / (2 * sizeof(int16_t));
        if (chunk_frames == 0) {
            continue;
        }
        if (frames >= max_frames) {
            break;
        }
        if (chunk_frames > max_frames - frames) {
            chunk_frames = max_frames - frames;
        }
        if (frames + chunk_frames > capacity) {
            size_t next = capacity * 2;
            while (next < frames + chunk_frames) {
                next *= 2;
            }
            float *grown = (float *)realloc(samples, next * 2 * sizeof(float));
            if (grown == NULL) {
                free(samples);
                xmp_end_player(ctx);
                xmp_release_module(ctx);
                xmp_free_context(ctx);
                return -8;
            }
            samples = grown;
            capacity = next;
        }
        const int16_t *pcm = (const int16_t *)fi.buffer;
        float *dst = samples + frames * 2;
        size_t count = chunk_frames * 2;
        for (size_t i = 0; i < count; i++) {
            dst[i] = (float)pcm[i] / 32768.0f;
        }
        frames += chunk_frames;
    }

    xmp_end_player(ctx);
    xmp_release_module(ctx);
    xmp_free_context(ctx);

    if (out_samples) *out_samples = samples;
    else free(samples);
    if (out_frames) *out_frames = frames;
    if (out_format) *out_format = actual;
    return 0;
}

void unbound_tracker_free(float *samples) {
    free(samples);
}
