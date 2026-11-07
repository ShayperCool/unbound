/* Bounded, CPU-only polygon triangulation; each call owns its callback state. */
#include <GL/glu.h>
#include <stdint.h>
#include <stddef.h>
#include <limits.h>

struct output {
    uint32_t *indices;
    size_t count, capacity;
    int failed;
};

static void emit(void *vertex, void *data) {
    struct output *out = data;
    if (out->failed) return;
    if (out->count >= out->capacity || !vertex) { out->failed = -1; return; }
    out->indices[out->count++] = (uint32_t)((uintptr_t)vertex - 1);
}

static void edge(GLboolean boundary) { (void)boundary; }
static void error(GLenum code, void *data) {
    struct output *out = data;
    if (code == GLU_OUT_OF_MEMORY) out->failed = -2;
    else if (!out->failed) out->failed = -1;
}
static void combine(GLdouble coords[3], void *vertices[4], GLfloat weights[4],
                    void **result, void *data) {
    (void)coords; (void)weights;
    /* Corners at one position are merged with only two source vertices; the first keeps its
     * corner, as the sweep does without a callback. A crossing has four source vertices and
     * would need a new vertex with interpolated attributes: refuse it explicitly. */
    if (!vertices[2] && !vertices[3]) {
        *result = vertices[0];
        return;
    }
    struct output *out = data;
    if (!out->failed) out->failed = -3;
    *result = NULL;
}

/* Returns index count, or -3 when edges cross, -2 for allocation failure, -1 otherwise. */
int unbound_model_tess(const float *positions, uint32_t count, uint32_t *indices, size_t capacity) {
    if (count < 3 || capacity > INT_MAX) return -1;
    GLUtesselator *tess = gluNewTess();
    if (!tess) return -2;
    struct output out = { indices, 0, capacity, 0 };
    gluTessCallback(tess, GLU_TESS_VERTEX_DATA, (_GLUfuncptr)emit);
    gluTessCallback(tess, GLU_TESS_EDGE_FLAG, (_GLUfuncptr)edge);
    gluTessCallback(tess, GLU_TESS_ERROR_DATA, (_GLUfuncptr)error);
    gluTessCallback(tess, GLU_TESS_COMBINE_DATA, (_GLUfuncptr)combine);
    gluTessBeginPolygon(tess, &out);
    gluTessBeginContour(tess);
    for (uint32_t i = 0; i < count; ++i) {
        GLdouble p[3] = { positions[3*(size_t)i], positions[3*(size_t)i+1], positions[3*(size_t)i+2] };
        gluTessVertex(tess, p, (void *)(uintptr_t)(i + 1));
    }
    gluTessEndContour(tess);
    gluTessEndPolygon(tess);
    gluDeleteTess(tess);
    return out.failed ? out.failed : out.count % 3 ? -1 : (int)out.count;
}
