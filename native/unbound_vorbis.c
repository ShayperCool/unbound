/* The flat API `crates/unbound-assets/src/fsb5.rs` calls over libvorbis (`native/vorbis`): the
 * headers libvorbis' encoder writes for a channel count, rate and quality (FSB5 keeps only the CRC
 * of the setup header), and a decoder fed those headers and then raw audio packets. */
#include <stdlib.h>
#include <string.h>
#include <vorbis/codec.h>
#include <vorbis/vorbisenc.h>
/* libvorbis' own: the encoder's private state, for writing the headers without an analyser. */
#include "codec_internal.h"

/* The identification and setup headers the encoder writes at `quality` (VBR), copied into the
 * caller's buffers; their lengths go to `*ident_len` and `*setup_len`. Returns 0, or -1 when the
 * encoder takes no such mode, -2 when a buffer is too small. The headers depend on the encoder's
 * setup alone: they are written from a DSP state holding only the private state they are kept in,
 * not an analyser (whose tables make most of an encoder's start-up). */
int uav_headers(int channels, long rate, float quality, unsigned char *ident, int ident_cap, int *ident_len,
                unsigned char *setup, int setup_cap, int *setup_len) {
    vorbis_info vi;
    vorbis_comment vc;
    vorbis_dsp_state vd;
    ogg_packet id, comment, code;
    int result = 0;
    vorbis_info_init(&vi);
    if (vorbis_encode_init_vbr(&vi, channels, rate, quality) != 0) {
        vorbis_info_clear(&vi);
        return -1;
    }
    vorbis_comment_init(&vc);
    memset(&vd, 0, sizeof vd);
    vd.vi = &vi;
    vd.backend_state = calloc(1, sizeof(private_state));
    if (!vd.backend_state) {
        vorbis_comment_clear(&vc);
        vorbis_info_clear(&vi);
        return -1;
    }
    if (vorbis_analysis_headerout(&vd, &vc, &id, &comment, &code) != 0) {
        result = -1;
    } else if (id.bytes > ident_cap || code.bytes > setup_cap) {
        result = -2;
    } else {
        memcpy(ident, id.packet, id.bytes);
        memcpy(setup, code.packet, code.bytes);
        *ident_len = (int)id.bytes;
        *setup_len = (int)code.bytes;
    }
    {
        private_state *b = vd.backend_state;
        free(b->header);
        free(b->header1);
        free(b->header2);
        free(b);
    }
    vorbis_comment_clear(&vc);
    vorbis_info_clear(&vi);
    return result;
}

typedef struct {
    vorbis_info vi;
    vorbis_comment vc;
    vorbis_dsp_state vd;
    vorbis_block vb;
    ogg_int64_t packetno;
} uav_decoder;

static int header_in(uav_decoder *d, const unsigned char *data, int len, int bos) {
    ogg_packet p;
    memset(&p, 0, sizeof p);
    p.packet = (unsigned char *)data;
    p.bytes = len;
    p.b_o_s = bos;
    p.packetno = d->packetno++;
    return vorbis_synthesis_headerin(&d->vi, &d->vc, &p);
}

/* A decoder for the stream the headers describe (an empty comment header goes between them), or
 * NULL when libvorbis rejects them. */
void *uav_decoder_new(const unsigned char *ident, int ident_len, const unsigned char *setup, int setup_len) {
    uav_decoder *d = calloc(1, sizeof *d);
    vorbis_comment empty;
    ogg_packet comment;
    int ok;
    if (!d) return NULL;
    vorbis_info_init(&d->vi);
    vorbis_comment_init(&d->vc);
    vorbis_comment_init(&empty);
    ok = header_in(d, ident, ident_len, 1) == 0 && vorbis_commentheader_out(&empty, &comment) == 0;
    if (ok) {
        ok = header_in(d, comment.packet, (int)comment.bytes, 0) == 0;
        ogg_packet_clear(&comment);
    }
    vorbis_comment_clear(&empty);
    ok = ok && header_in(d, setup, setup_len, 0) == 0;
    ok = ok && vorbis_synthesis_init(&d->vd, &d->vi) == 0;
    if (!ok) {
        vorbis_comment_clear(&d->vc);
        vorbis_info_clear(&d->vi);
        free(d);
        return NULL;
    }
    vorbis_block_init(&d->vd, &d->vb);
    return d;
}

/* Decodes an audio packet, writing up to `cap` frames of interleaved samples to `out`. Returns the
 * frames written (0 for the first packet, which only primes the overlap), or -1 when the packet
 * does not decode. */
int uav_decode(void *decoder, const unsigned char *data, int len, float *out, int cap) {
    uav_decoder *d = decoder;
    ogg_packet p;
    float **pcm;
    int frames, written = 0, channels = d->vi.channels;
    memset(&p, 0, sizeof p);
    p.packet = (unsigned char *)data;
    p.bytes = len;
    p.granulepos = -1;
    p.packetno = d->packetno++;
    if (vorbis_synthesis(&d->vb, &p) != 0 || vorbis_synthesis_blockin(&d->vd, &d->vb) != 0) return -1;
    while ((frames = vorbis_synthesis_pcmout(&d->vd, &pcm)) > 0) {
        int take = frames < cap - written ? frames : cap - written;
        for (int i = 0; i < take; i++)
            for (int c = 0; c < channels; c++) out[(written + i) * channels + c] = pcm[c][i];
        written += take;
        vorbis_synthesis_read(&d->vd, frames);
        if (written == cap) break;
    }
    return written;
}

void uav_decoder_free(void *decoder) {
    uav_decoder *d = decoder;
    if (!d) return;
    vorbis_block_clear(&d->vb);
    vorbis_dsp_clear(&d->vd);
    vorbis_comment_clear(&d->vc);
    vorbis_info_clear(&d->vi);
    free(d);
}
