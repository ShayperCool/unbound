// The probe's native rendering plugin: it records what the low-level native plugin interface
// gives it (interfaces, device events, rendering callbacks, their thread and GL state) and draws
// with OpenGL into the textures the scripts hand it. The runner builds it into the project copy
// (Assets/Plugins/x86_64/libNativeGfxProbe.so); the scripts read the record back.
//
// Built with NATIVEGFX_PRELOAD="<word> " it is a variant no script calls (the preloaded plugin,
// and one named with the GfxPlugin prefix): it records only its load, device events and unload,
// each line prefixed with that word.
//
// Only the public plugin headers' declarations are used (repeated here so the build needs no
// Editor installation).
#include <dlfcn.h>
#include <pthread.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define EXPORT __attribute__((visibility("default")))

typedef struct { unsigned long long high, low; } Guid;
// UnityInterfaceGUID has a user-declared copy constructor in C++, so C++ passes it to
// GetInterface and RegisterInterface by reference (the Itanium C++ ABI's rule for classes that
// are not trivially copyable). Plugins are C++; this C plugin passes the same pointer.
typedef struct IUnityInterfaces {
    void* (*GetInterface)(const Guid* guid);
    void (*RegisterInterface)(const Guid* guid, void* ptr);
    void* (*GetInterfaceSplit)(unsigned long long high, unsigned long long low);
    void (*RegisterInterfaceSplit)(unsigned long long high, unsigned long long low, void* ptr);
} IUnityInterfaces;
typedef void (*DeviceEventCallback)(int eventType);
typedef struct IUnityGraphics {
    int (*GetRenderer)(void);
    void (*RegisterDeviceEventCallback)(DeviceEventCallback callback);
    void (*UnregisterDeviceEventCallback)(DeviceEventCallback callback);
    int (*ReserveEventIDRange)(int count);
} IUnityGraphics;
typedef struct IUnityLog {
    void (*Log)(int type, const char* message, const char* fileName, const int fileLine);
} IUnityLog;
typedef struct {
    void* texData;
    unsigned int userData;
    unsigned int textureID;
    int format;
    unsigned int width, height, bpp;
} TextureUpdateV1;
typedef struct {
    void* texData;
    intptr_t textureID;
    unsigned int userData;
    int format;
    unsigned int width, height, bpp;
} TextureUpdateV2;
typedef struct {
    unsigned int source;
    void* destination;
    unsigned int command, commandParam, commandFlags;
} CustomBlitParams;

enum { EventCustomBlit = 6, UpdateBeginV1 = 7, UpdateEndV1 = 8, UpdateBeginV2 = 9, UpdateEndV2 = 10 };

static const struct { const char* name; Guid guid; } kInterfaces[] = {
    {"IUnityGraphics", {0x7CBA0A9CA4DDB544ULL, 0x8C5AD4926EB17B11ULL}},
    {"IUnityLog", {0x9E7507fA5B444D5DULL, 0x92FB979515EA83FCULL}},
    {"IUnityProfiler", {0x2CE79ED8316A4833ULL, 0x87076B2013E1571FULL}},
    {"IUnityProfilerV2", {0xB957E0189CB6A30BULL, 0x83CE589AE85B9068ULL}},
    {"IUnityProfilerCallbacks", {0x572FDB38CE3C4B1FULL, 0xA6071A9A7C4F52D8ULL}},
    {"IUnityProfilerCallbacksV2", {0x5DEB59E88F2D4571ULL, 0x81E8583069A5E33CULL}},
    {"IUnityMemoryManager", {0xBAF9E57C61A811ECULL, 0xC5A7CC7861A811ECULL}},
    {"IUnityEventQueue", {0x9959C347F5AE374DULL, 0x9BADE6FC8EF49E7FULL}},
    {"IUnityGraphicsVulkan", {0x95355348d4ef4e11ULL, 0x9789313dfcffcc87ULL}},
    {"IUnityGraphicsVulkanV2", {0x329334C09DCA4787ULL, 0xB347DD92A0097FFCULL}},
    {"IUnityGraphicsMetal", {0x992C8EAEA95811E5ULL, 0x9A62C4B5B9876117ULL}},
    {"IUnityGraphicsMetalV1", {0x29F8F3D03833465EULL, 0x92138551C15D823DULL}},
    {"IUnityGraphicsMetalV2", {0xF58857784FEF46ECULL, 0x9DB7A8803B87DA3DULL}},
    {"IUnityGraphicsD3D11", {0xAAB37EF87A87D748ULL, 0xBF76967F07EFB177ULL}},
    {"IUnityGraphicsD3D12", {0xEF4CEC88A45F4C4CULL, 0xBD295B6F2A38D9DEULL}},
};

// ------------------------------------------------------------------ GL entry points

typedef unsigned int GLenum;
typedef unsigned int GLuint;
typedef int GLint;
typedef int GLsizei;
typedef float GLfloat;
typedef unsigned char GLboolean;
typedef unsigned int GLbitfield;
#define GL_VERSION 0x1F02
#define GL_MAJOR_VERSION 0x821B
#define GL_DRAW_FRAMEBUFFER_BINDING 0x8CA6
#define GL_DRAW_FRAMEBUFFER 0x8CA9
#define GL_COLOR_ATTACHMENT0 0x8CE0
#define GL_DEPTH_ATTACHMENT 0x8D00
#define GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE 0x8CD0
#define GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME 0x8CD1
#define GL_TEXTURE 0x1702
#define GL_RENDERBUFFER 0x8D41
#define GL_VIEWPORT 0x0BA2
#define GL_SCISSOR_TEST 0x0C11
#define GL_COLOR_BUFFER_BIT 0x4000
#define GL_TEXTURE_2D 0x0DE1
#define GL_TEXTURE_BINDING_2D 0x8069
#define GL_TEXTURE_WIDTH 0x1000
#define GL_TEXTURE_HEIGHT 0x1001
#define GL_TEXTURE_INTERNAL_FORMAT 0x1003
#define GL_RGBA 0x1908
#define GL_RGBA8 0x8058
#define GL_UNSIGNED_BYTE 0x1401
#define GL_UNPACK_ALIGNMENT 0x0CF5
#define GL_UNPACK_ROW_LENGTH 0x0CF2
#define GL_PIXEL_UNPACK_BUFFER_BINDING 0x88EF
#define GL_PIXEL_UNPACK_BUFFER 0x88EC
#define GL_TEXTURE_MIN_FILTER 0x2801
#define GL_TEXTURE_MAG_FILTER 0x2800
#define GL_NEAREST 0x2600
#define GL_FRAMEBUFFER_SRGB 0x8DB9

static struct {
    const unsigned char* (*GetString)(GLenum);
    void (*GetIntegerv)(GLenum, GLint*);
    GLenum (*GetError)(void);
    void (*GetFramebufferAttachmentParameteriv)(GLenum, GLenum, GLenum, GLint*);
    void (*Enable)(GLenum);
    void (*Disable)(GLenum);
    GLboolean (*IsEnabled)(GLenum);
    void (*Scissor)(GLint, GLint, GLsizei, GLsizei);
    void (*ClearColor)(GLfloat, GLfloat, GLfloat, GLfloat);
    void (*Clear)(GLbitfield);
    void (*ColorMask)(GLboolean, GLboolean, GLboolean, GLboolean);
    void (*BindTexture)(GLenum, GLuint);
    void (*GenTextures)(GLsizei, GLuint*);
    void (*TexImage2D)(GLenum, GLint, GLint, GLsizei, GLsizei, GLint, GLenum, GLenum, const void*);
    void (*TexSubImage2D)(GLenum, GLint, GLint, GLint, GLsizei, GLsizei, GLenum, GLenum, const void*);
    void (*TexParameteri)(GLenum, GLenum, GLint);
    GLboolean (*IsTexture)(GLuint);
    GLboolean (*IsRenderbuffer)(GLuint);
    void (*GetTexLevelParameteriv)(GLenum, GLint, GLenum, GLint*);
    void (*PixelStorei)(GLenum, GLint);
    void (*BindBuffer)(GLenum, GLuint);
    void (*Finish)(void);
} gl;
static int gl_loaded;

static void load_gl(void)
{
    if (gl_loaded) return;
    gl_loaded = 1;
    void* lib = dlopen("libOpenGL.so.0", RTLD_NOW | RTLD_GLOBAL);
    if (!lib) lib = dlopen("libGL.so.1", RTLD_NOW | RTLD_GLOBAL);
    if (!lib) return;
#define L(name) *(void**)&gl.name = dlsym(lib, "gl" #name)
    L(GetString); L(GetIntegerv); L(GetError); L(GetFramebufferAttachmentParameteriv); L(Enable);
    L(Disable); L(IsEnabled); L(Scissor); L(ClearColor); L(Clear); L(ColorMask); L(BindTexture);
    L(GenTextures); L(TexImage2D); L(TexSubImage2D); L(TexParameteri); L(IsTexture);
    L(IsRenderbuffer); L(GetTexLevelParameteriv); L(PixelStorei); L(BindBuffer); L(Finish);
#undef L
}

/// Whether a GL context is current on this thread: GL_VERSION is null without one.
static int gl_current(void)
{
    load_gl();
    return gl.GetString && gl.GetString(GL_VERSION) != NULL;
}

// ------------------------------------------------------------------ record

static pthread_mutex_t s_lock = PTHREAD_MUTEX_INITIALIZER;
static char* s_log;
static size_t s_length, s_capacity;
static FILE* s_file;
static pthread_t s_main;
static int s_main_known;

#ifdef NATIVEGFX_PRELOAD
#define RECORD_PREFIX NATIVEGFX_PRELOAD
#else
#define RECORD_PREFIX ""
#endif

static void record(const char* format, ...)
{
    char line[512];
    va_list args;
    va_start(args, format);
    int prefix = snprintf(line, sizeof line, "%s", RECORD_PREFIX);
    int n = vsnprintf(line + prefix, sizeof line - 1 - prefix, format, args);
    if (n >= 0) n += prefix;
    va_end(args);
    if (n < 0) return;
    if (n > (int)sizeof line - 2) n = sizeof line - 2;
    line[n++] = '\n';
    line[n] = 0;
    pthread_mutex_lock(&s_lock);
    if (s_length + n + 1 > s_capacity) {
        s_capacity = (s_length + n + 1) * 2;
        s_log = realloc(s_log, s_capacity);
    }
    memcpy(s_log + s_length, line, n + 1);
    s_length += n;
    if (!s_file) {
        const char* path = getenv("NATIVEGFX_PLUGIN_LOG");
        if (path) s_file = fopen(path, "a");
    }
    if (s_file) {
        fputs(line, s_file);
        fflush(s_file);
    }
    pthread_mutex_unlock(&s_lock);
}

static const char* thread_name(void)
{
    if (!s_main_known) return "unknown";
    return pthread_equal(pthread_self(), s_main) ? "main" : "other";
}

// ------------------------------------------------------------------ state

static IUnityInterfaces* s_interfaces;
static IUnityGraphics* s_graphics;
static IUnityLog* s_unity_log;
static int s_event_base = -1;
/// Native pointers the scripts hand over (see `kNameLabels`).
#define NAME_COUNT 12
static uintptr_t s_names[NAME_COUNT];
static const char* kNameLabels[NAME_COUNT] = {"camera", "blit", "written", "updated", "rtdepth",
                                              "external", "plain", "created", "nodepth", "blitsource",
                                              "order", "aliased"};
static uintptr_t s_render_buffers[4];
static GLuint s_created[2];

static void on_device_event(int type)
{
    record("device event=%d renderer=%d thread=%s gl=%d", type,
           s_graphics ? s_graphics->GetRenderer() : -1, thread_name(), gl_current());
}

EXPORT void UnityPluginLoad(IUnityInterfaces* interfaces)
{
    // The scripts run on the main thread; UnityPluginLoad is called from the first call into the
    // plugin (a script's), so this is the main thread.
    s_main = pthread_self();
    s_main_known = 1;
    s_interfaces = interfaces;
    record("load thread=%s gl=%d", thread_name(), gl_current());
#ifdef NATIVEGFX_PRELOAD
    s_graphics = (IUnityGraphics*)interfaces->GetInterface(&kInterfaces[0].guid);
    if (s_graphics) s_graphics->RegisterDeviceEventCallback(on_device_event);
    return;
#endif
    for (size_t i = 0; i < sizeof kInterfaces / sizeof kInterfaces[0]; i++) {
        void* by_struct = interfaces->GetInterface(&kInterfaces[i].guid);
        void* split = interfaces->GetInterfaceSplit(kInterfaces[i].guid.high, kInterfaces[i].guid.low);
        record("interface %s=%d split=%s", kInterfaces[i].name, by_struct != NULL,
               split == by_struct ? "same" : "different");
    }
    // An interface this plugin registers is found by its GUID (by either entry point); an
    // unknown GUID is null.
    static int s_own_interface = 42;
    Guid own = {0x1122334455667788ULL, 0x99AABBCCDDEEFF00ULL};
    record("own.before=%d", interfaces->GetInterface(&own) != NULL);
    interfaces->RegisterInterface(&own, &s_own_interface);
    record("own.after=%d split=%d", interfaces->GetInterface(&own) == &s_own_interface,
           interfaces->GetInterfaceSplit(own.high, own.low) == &s_own_interface);
    interfaces->RegisterInterfaceSplit(own.high, own.low, NULL);
    record("own.cleared=%d", interfaces->GetInterface(&own) == NULL);
    s_graphics = (IUnityGraphics*)interfaces->GetInterface(&kInterfaces[0].guid);
    s_unity_log = (IUnityLog*)interfaces->GetInterface(&kInterfaces[1].guid);
    if (s_graphics) {
        record("renderer=%d", s_graphics->GetRenderer());
        s_event_base = s_graphics->ReserveEventIDRange(4);
        int next = s_graphics->ReserveEventIDRange(4);
        record("reserve base=%d next=%d", s_event_base, next);
        s_graphics->RegisterDeviceEventCallback(on_device_event);
    }
    if (s_unity_log) {
        s_unity_log->Log(3, "nativegfx: plugin loaded", "nativegfx_probe.c", 1);
        s_unity_log->Log(2, "nativegfx: a warning", "nativegfx_probe.c", 2);
        s_unity_log->Log(0, "nativegfx: an error", "nativegfx_probe.c", 3);
    }
    record("load done");
}

EXPORT void UnityPluginUnload(void)
{
    record("unload thread=%s gl=%d", thread_name(), gl_current());
    if (s_graphics) s_graphics->UnregisterDeviceEventCallback(on_device_event);
}

// ------------------------------------------------------------------ GL helpers

static int name_index(uintptr_t name)
{
    for (int i = 0; i < NAME_COUNT; i++)
        if (name != 0 && s_names[i] == name) return i;
    return -1;
}

/// The bound draw framebuffer: "tex<i>" when its color is a texture the scripts named (with its
/// depth attachment's kind and the viewport), "unnamed" for any other framebuffer (the default
/// one, another texture or none: what Unity binds outside a target is its own), "none" without a
/// context.
static void describe_target(char* out, size_t size)
{
    if (!gl_current()) {
        snprintf(out, size, "none");
        return;
    }
    GLint fbo = 0;
    gl.GetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &fbo);
    GLint type = 0, name = 0, depth_type = 0;
    if (fbo != 0) {
        gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE, &type);
        if (type != 0)
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME, &name);
        gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE, &depth_type);
        // A framebuffer of Unity's own may refuse these queries: their errors are not the
        // callback's.
        while (gl.GetError() != 0) {}
    }
    int index = type == GL_TEXTURE ? name_index((uintptr_t)name) : -1;
    if (index < 0) {
        snprintf(out, size, "unnamed");
        return;
    }
    GLint viewport[4] = {0};
    gl.GetIntegerv(GL_VIEWPORT, viewport);
    const char* depth = depth_type == GL_TEXTURE ? "texture" : depth_type == GL_RENDERBUFFER ? "renderbuffer" : "none";
    snprintf(out, size, "%s depth=%s vp=%d,%d,%d,%d", kNameLabels[index], depth, viewport[0], viewport[1], viewport[2], viewport[3]);
}

/// Records the GL errors a callback left (none is expected), so that Unity's own check after the
/// callback finds none.
static void drain_errors(const char* where)
{
    if (!gl_current()) return;
    for (int i = 0; i < 8; i++) {
        GLenum error = gl.GetError();
        if (error == 0) break;
        record("glerror %s 0x%x", where, error);
    }
}

/// Clears a rectangle of the bound draw framebuffer to a color, as plugins draw into the target
/// Unity has bound.
static void fill_bound(int x, int y, int w, int h, float r, float g, float b)
{
    if (!gl_current()) return;
    GLboolean scissor = gl.IsEnabled(GL_SCISSOR_TEST);
    gl.Enable(GL_SCISSOR_TEST);
    gl.Scissor(x, y, w, h);
    gl.ColorMask(1, 1, 1, 1);
    gl.ClearColor(r, g, b, 1.0f);
    gl.Clear(GL_COLOR_BUFFER_BIT);
    if (!scissor) gl.Disable(GL_SCISSOR_TEST);
}

static void unpack_defaults(void)
{
    gl.BindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
    gl.PixelStorei(GL_UNPACK_ALIGNMENT, 4);
    gl.PixelStorei(GL_UNPACK_ROW_LENGTH, 0);
}

/// Writes a 4x2 block of opaque red at (1, 2) of the texture named `name` (glTexSubImage2D).
static void write_texture(uintptr_t name)
{
    if (!gl_current() || !name) return;
    GLint bound = 0;
    gl.GetIntegerv(GL_TEXTURE_BINDING_2D, &bound);
    gl.BindTexture(GL_TEXTURE_2D, (GLuint)name);
    unpack_defaults();
    unsigned char texels[4 * 2 * 4];
    for (int i = 0; i < 8; i++) {
        texels[i * 4 + 0] = 255;
        texels[i * 4 + 1] = (unsigned char)(i * 30);
        texels[i * 4 + 2] = 0;
        texels[i * 4 + 3] = 255;
    }
    gl.TexSubImage2D(GL_TEXTURE_2D, 0, 1, 2, 4, 2, GL_RGBA, GL_UNSIGNED_BYTE, texels);
    gl.BindTexture(GL_TEXTURE_2D, (GLuint)bound);
}

/// A 4x4 RGBA8 texture of the plugin's own: texel (x, y) = (60x+seed, 60y, seed, 255).
static GLuint create_texture(int seed)
{
    if (!gl_current()) return 0;
    GLint bound = 0;
    gl.GetIntegerv(GL_TEXTURE_BINDING_2D, &bound);
    GLuint name = 0;
    gl.GenTextures(1, &name);
    gl.BindTexture(GL_TEXTURE_2D, name);
    unpack_defaults();
    unsigned char texels[4 * 4 * 4];
    for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++) {
            unsigned char* t = texels + (y * 4 + x) * 4;
            t[0] = (unsigned char)(x * 60 + seed);
            t[1] = (unsigned char)(y * 60);
            t[2] = (unsigned char)seed;
            t[3] = 255;
        }
    gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, 4, 4, 0, GL_RGBA, GL_UNSIGNED_BYTE, texels);
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    gl.BindTexture(GL_TEXTURE_2D, (GLuint)bound);
    return name;
}

/// What GL says of a native texture pointer: whether it names a texture, its size and internal
/// format (bound briefly), or a renderbuffer.
static void describe_name(const char* label, uintptr_t name)
{
    if (!gl_current()) {
        record("name %s gl=0", label);
        return;
    }
    if (name == 0) {
        record("name %s zero", label);
        return;
    }
    if (name > 0xffffffffu) {
        record("name %s pointer", label);
        return;
    }
    if (gl.IsTexture((GLuint)name)) {
        GLint bound = 0, w = 0, h = 0, format = 0;
        while (gl.GetError() != 0) {}
        gl.GetIntegerv(GL_TEXTURE_BINDING_2D, &bound);
        gl.BindTexture(GL_TEXTURE_2D, (GLuint)name);
        // A texture of another target fails to bind as 2D: GL_INVALID_OPERATION.
        if (gl.GetError() == 0) {
            gl.GetTexLevelParameteriv(GL_TEXTURE_2D, 0, GL_TEXTURE_WIDTH, &w);
            gl.GetTexLevelParameteriv(GL_TEXTURE_2D, 0, GL_TEXTURE_HEIGHT, &h);
            gl.GetTexLevelParameteriv(GL_TEXTURE_2D, 0, GL_TEXTURE_INTERNAL_FORMAT, &format);
            record("name %s texture %dx%d format=0x%x", label, w, h, format);
        } else {
            record("name %s texture not2d", label);
        }
        gl.BindTexture(GL_TEXTURE_2D, (GLuint)bound);
        while (gl.GetError() != 0) {}
    } else if (gl.IsRenderbuffer((GLuint)name)) {
        record("name %s renderbuffer", label);
    } else {
        record("name %s unknown", label);
    }
}

// ------------------------------------------------------------------ rendering callbacks

/// Events relative to the reserved base: 0 fills a rectangle of the bound target red, 1 writes
/// the texture named "written", 2 creates the plugin's first texture, 3 its second, 4 describes
/// the named textures, 5 fills another rectangle of the bound target yellow, 6 leaves a GL error
/// (GL_INVALID_ENUM) for Unity's check after the callback, 7 fills a column of the bound target
/// blue (between two draws of an executed buffer).
static void on_render_event(int id)
{
    char target[160];
    describe_target(target, sizeof target);
    int rel = id - s_event_base;
    record("event id=base+%d thread=%s gl=%d target=%s", rel, thread_name(), gl_current(), target);
    switch (rel) {
    case 0: fill_bound(4, 8, 16, 8, 1.0f, 0.0f, 0.0f); break;
    case 1: write_texture(s_names[2]); break;
    case 2: if (!s_created[0]) s_created[0] = create_texture(10); break;
    case 3: if (!s_created[1]) s_created[1] = create_texture(130); break;
    case 5: fill_bound(40, 4, 8, 8, 1.0f, 1.0f, 0.0f); break;
    case 7: fill_bound(4, 0, 8, 16, 0.0f, 0.0f, 1.0f); break;
    case 4:
        for (int i = 0; i < NAME_COUNT; i++)
            if (s_names[i]) describe_name(kNameLabels[i], s_names[i]);
        break;
    case 6:
        gl.Enable(0x1234);
        return;
    default: break;
    }
    drain_errors("event");
}

static void on_render_event_data(int id, void* data)
{
    record("data id=base+%d value=%d thread=%s gl=%d", id - s_event_base, data ? *(int*)data : -1,
           thread_name(), gl_current());
}

static unsigned char s_update_texels[16 * 16 * 4];

static void on_texture_update(int event, void* data)
{
    char target[160];
    describe_target(target, sizeof target);
    if (event == UpdateBeginV1 || event == UpdateEndV1) {
        TextureUpdateV1* p = data;
        int index = name_index(p->textureID);
        record("update event=%d v1 user=%u id=%s format=%d size=%ux%u bpp=%u data=%d thread=%s gl=%d target=%s",
               event, p->userData, index >= 0 ? (index == 3 ? "native" : "other-named") : "internal",
               p->format, p->width, p->height, p->bpp, p->texData != NULL, thread_name(), gl_current(), target);
        if (event == UpdateBeginV1) {
            for (unsigned i = 0; i < p->width * p->height && i < 256; i++) {
                unsigned char texel[4] = {(unsigned char)(i * 16), (unsigned char)(255 - i * 16), (unsigned char)p->userData, 255};
                memcpy(s_update_texels + i * p->bpp, texel, p->bpp < 4 ? p->bpp : 4);
            }
            p->texData = s_update_texels;
        }
    } else if (event == UpdateBeginV2 || event == UpdateEndV2) {
        TextureUpdateV2* p = data;
        int index = name_index((uintptr_t)p->textureID);
        record("update event=%d v2 user=%u id=%s format=%d size=%ux%u bpp=%u data=%d thread=%s gl=%d target=%s",
               event, p->userData, index >= 0 ? (index == 3 ? "native" : "other-named") : "internal",
               p->format, p->width, p->height, p->bpp, p->texData != NULL, thread_name(), gl_current(), target);
        if (event == UpdateBeginV2) {
            for (unsigned i = 0; i < p->width * p->height && i < 256; i++) {
                unsigned char texel[4] = {(unsigned char)(255 - i * 16), (unsigned char)p->userData, (unsigned char)(i * 16), 255};
                memcpy(s_update_texels + i * p->bpp, texel, p->bpp < 4 ? p->bpp : 4);
            }
            p->texData = s_update_texels;
        }
    } else {
        record("update event=%d unexpected", event);
    }
    drain_errors("update");
}

static void on_custom_blit(int event, void* data)
{
    char target[160];
    describe_target(target, sizeof target);
    if (event != EventCustomBlit) {
        record("blit event=%d unexpected", event);
        return;
    }
    CustomBlitParams* p = data;
    int source = name_index(p->source);
    const char* destination = "other";
    for (int i = 0; i < 4; i++)
        if (s_render_buffers[i] && (uintptr_t)p->destination == s_render_buffers[i]) destination = i == 0 ? "rb.blit.color" : i == 1 ? "rb.blit.depth" : "rb.other";
    if (!p->destination) destination = "null";
    record("blit command=%u param=%u flags=%u source=%s destination=%s thread=%s gl=%d target=%s",
           p->command, p->commandParam, p->commandFlags, source >= 0 ? kNameLabels[source] : (p->source ? "unnamed" : "zero"),
           destination, thread_name(), gl_current(), target);
    fill_bound(2, 1, 4, 2, 0.0f, 1.0f, 0.0f);
    drain_errors("blit");
}

// ------------------------------------------------------------------ script entry points

EXPORT void* NativeGfx_RenderEvent(void) { return (void*)on_render_event; }
EXPORT void* NativeGfx_RenderEventAndData(void) { return (void*)on_render_event_data; }
EXPORT void* NativeGfx_TextureUpdate(void) { return (void*)on_texture_update; }
EXPORT void* NativeGfx_CustomBlit(void) { return (void*)on_custom_blit; }
EXPORT int NativeGfx_EventBase(void) { return s_event_base; }

EXPORT void NativeGfx_SetName(int index, void* name)
{
    if (index >= 0 && index < NAME_COUNT) s_names[index] = (uintptr_t)name;
}

EXPORT void NativeGfx_SetRenderBuffer(int index, void* buffer)
{
    if (index >= 0 && index < 4) s_render_buffers[index] = (uintptr_t)buffer;
}

EXPORT void* NativeGfx_Created(int index) { return index >= 0 && index < 2 ? (void*)(uintptr_t)s_created[index] : NULL; }

/// A script's marker in the record: what the scripts did between the callbacks.
EXPORT void NativeGfx_Mark(const char* text) { record("mark %s thread=%s", text, thread_name()); }

/// Copies the record into `buffer` (at most `capacity` bytes with the terminator); returns the
/// record's length.
EXPORT int NativeGfx_Record(char* buffer, int capacity)
{
    pthread_mutex_lock(&s_lock);
    int length = (int)s_length;
    if (buffer && capacity > 0) {
        int n = length < capacity - 1 ? length : capacity - 1;
        if (n > 0) memcpy(buffer, s_log, n);
        buffer[n] = 0;
    }
    pthread_mutex_unlock(&s_lock);
    return length;
}
