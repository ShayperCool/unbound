// The probe's native rendering plugin: it records what the low-level native plugin interface
// gives it (interfaces, device events, rendering callbacks, their thread and GL state) and draws
// with OpenGL into the textures the scripts hand it. The runner builds it into the project copy
// (Assets/Plugins/x86_64/libNativeGfxProbe.so); the scripts read the record back.
//
// Built with NATIVEGFX_PRELOAD="<word> " it is a variant no script calls (the preloaded plugin,
// and one named with the GfxPlugin prefix): it records only its load, device events and unload,
// each line prefixed with that word.
//
// A second record, the notes, holds what the profiler callbacks, the event queue
// (nativegfx_events.cpp) and the rendering extension exports observe, with the scripts' marks
// between: the first record's lines stay what they were. The variants write their rendering
// extension calls as lines that start with "ext ".
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
    void (*GetTexParameteriv)(GLenum, GLenum, GLint*);
    void (*TexSubImage3D)(GLenum, GLint, GLint, GLint, GLint, GLsizei, GLsizei, GLsizei, GLenum, GLenum, const void*);
    void (*CompressedTexImage2D)(GLenum, GLint, GLenum, GLsizei, GLsizei, GLint, GLsizei, const void*);
    void (*BindRenderbuffer)(GLenum, GLuint);
    void (*GetRenderbufferParameteriv)(GLenum, GLenum, GLint*);
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
    L(GetTexParameteriv); L(TexSubImage3D); L(CompressedTexImage2D); L(BindRenderbuffer);
    L(GetRenderbufferParameteriv);
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

// ------------------------------------------------------------------ notes

static char* s_notes;
static size_t s_notes_length, s_notes_capacity;

static void note(const char* format, ...)
{
    char line[512];
    va_list args;
    va_start(args, format);
    int n = vsnprintf(line, sizeof line - 1, format, args);
    va_end(args);
    if (n < 0) return;
    if (n > (int)sizeof line - 2) n = sizeof line - 2;
    line[n++] = '\n';
    line[n] = 0;
    pthread_mutex_lock(&s_lock);
    if (s_notes_length + n + 1 > s_notes_capacity) {
        s_notes_capacity = (s_notes_length + n + 1) * 2;
        s_notes = realloc(s_notes, s_notes_capacity);
    }
    memcpy(s_notes + s_notes_length, line, n + 1);
    s_notes_length += n;
    pthread_mutex_unlock(&s_lock);
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

// ------------------------------------------------------------------ rendering extensions

static int gl_current(void);

/// UnityRenderingExtQuery and UnityRenderingExtEvent (IUnityRenderingExtensions.h): what the
/// Editor asks and tells a loaded plugin of its own accord.
EXPORT int UnityRenderingExtQuery(int query)
{
#ifdef NATIVEGFX_PRELOAD
    pthread_mutex_lock(&s_lock);
    if (!s_file) {
        const char* path = getenv("NATIVEGFX_PLUGIN_LOG");
        if (path) s_file = fopen(path, "a");
    }
    if (s_file) {
        fprintf(s_file, "ext %squery flags=%d thread=%s gl=%d\n", RECORD_PREFIX, query, thread_name(), gl_current());
        fflush(s_file);
    }
    pthread_mutex_unlock(&s_lock);
    // A variant says that it overrides what is asked; the plugin the scripts call says no.
    return 1;
#else
    note("ext.query flags=%d thread=%s gl=%d", query, thread_name(), gl_current());
    return 0;
#endif
}

EXPORT void UnityRenderingExtEvent(int event, void* data)
{
#ifndef NATIVEGFX_PRELOAD
    note("ext.event type=%d data=%d thread=%s gl=%d", event, data != NULL, thread_name(), gl_current());
#else
    (void)event;
    (void)data;
#endif
}

#ifndef NATIVEGFX_PRELOAD
// ------------------------------------------------------------------ profiler callbacks

typedef struct { uint16_t id, reserved0; uint32_t rgbaColor; const char* name; } CategoryDesc;
typedef struct { const void* callback; uint32_t id; uint16_t flags, categoryId; const char* name; const void* metaDataDesc; } MarkerDesc;
typedef struct { uint64_t threadId; const char* groupName; const char* name; } ThreadDesc;
typedef struct IUnityProfilerCallbacksV2 {
    int (*RegisterCreateCategoryCallback)(void (*callback)(const CategoryDesc*, void*), void* user);
    int (*UnregisterCreateCategoryCallback)(void (*callback)(const CategoryDesc*, void*), void* user);
    int (*RegisterCreateMarkerCallback)(void (*callback)(const MarkerDesc*, void*), void* user);
    int (*UnregisterCreateMarkerCallback)(void (*callback)(const MarkerDesc*, void*), void* user);
    int (*RegisterMarkerEventCallback)(const MarkerDesc* desc, void (*callback)(const MarkerDesc*, uint16_t, uint16_t, const void*, void*), void* user);
    int (*UnregisterMarkerEventCallback)(const MarkerDesc* desc, void (*callback)(const MarkerDesc*, uint16_t, uint16_t, const void*, void*), void* user);
    int (*RegisterFrameCallback)(void (*callback)(void*), void* user);
    int (*UnregisterFrameCallback)(void (*callback)(void*), void* user);
    int (*RegisterCreateThreadCallback)(void (*callback)(const ThreadDesc*, void*), void* user);
    int (*UnregisterCreateThreadCallback)(void (*callback)(const ThreadDesc*, void*), void* user);
    // IUnityProfilerCallbacks ends here.
    int (*RegisterFlowEventCallback)(void (*callback)(uint8_t, uint32_t, void*), void* user);
    int (*UnregisterFlowEventCallback)(void (*callback)(uint8_t, uint32_t, void*), void* user);
} IUnityProfilerCallbacksV2;
typedef struct IUnityProfilerV2 {
    void (*EmitEvent)(const MarkerDesc* desc, uint16_t type, uint16_t count, const void* data);
    int (*IsEnabled)(void);
    int (*IsAvailable)(void);
    int (*CreateMarker)(const MarkerDesc** desc, const char* name, uint16_t category, uint16_t flags, int dataCount);
    int (*SetMarkerMetadataName)(const MarkerDesc* desc, int index, const char* name, uint8_t type, uint8_t unit);
    int (*CreateCategory)(uint16_t* category, const char* name, uint32_t unused);
    int (*RegisterThread)(uint64_t* id, const char* group, const char* name);
    int (*UnregisterThread)(uint64_t id);
} IUnityProfilerV2;

int nativegfx_events_load(IUnityInterfaces* interfaces, void (*note)(const char*, ...));
void nativegfx_events_frame(int frame);

static IUnityProfilerCallbacksV2* s_callbacks;
static IUnityProfilerV2* s_profiler;
static const MarkerDesc* s_marker;
/// What the callbacks heard: categories (all, and the plugin's own), markers, threads, frames,
/// flows, marker events.
static int s_categories, s_own_categories, s_markers, s_own_markers, s_threads, s_own_threads, s_frames, s_flows, s_events;
static int s_listing;
static uint32_t s_own_color;
static char s_last_mark[96] = "(none)";
static char s_first_thread[96];

static int own(const char* name) { return name && strncmp(name, "NativeGfx", 9) == 0; }

/// The scripts' frame count since they began (a managed function), -1 before they give it.
static int (*s_clock)(void);
int nativegfx_clock(void) { return s_clock ? s_clock() : -1; }

static void on_category(const CategoryDesc* desc, void* user)
{
    s_categories++;
    if (own(desc->name)) {
        s_own_categories++;
        s_own_color = desc->rgbaColor;
    }
    // The categories that exist when the first callback registers.
    if (s_listing) note("category %u %08x %s user=%ld thread=%s", desc->id, desc->rgbaColor, desc->name, (long)user, thread_name());
}

static void on_marker_event(const MarkerDesc* desc, uint16_t type, uint16_t count, const void* data, void* user);

static void on_marker(const MarkerDesc* desc, void* user)
{
    s_markers++;
    if (!own(desc->name)) return;
    s_own_markers++;
    note("marker name=%s flags=%u category.own=%d callback=%d user=%ld thread=%s", desc->name, desc->flags, desc->categoryId >= 39, desc->callback != NULL, (long)user, thread_name());
    // A marker a script makes: its events too.
    if (strcmp(desc->name, "NativeGfx.Script") == 0)
        note("register.event.script=%d (NativeGfx.Script)", s_callbacks->RegisterMarkerEventCallback(desc, on_marker_event, (void*)8));
}

static void on_thread(const ThreadDesc* desc, void* user)
{
    // A thread's ID is the thread's own handle (the first heard of is the registering one, the
    // main thread; the plugin's is heard of on itself).
    int self = desc->threadId == (uint64_t)pthread_self();
    if (s_threads++ == 0)
        snprintf(s_first_thread, sizeof s_first_thread, "group=%s name=%s id=%s user=%ld self=%d", desc->groupName ? desc->groupName : "(null)",
                 desc->name ? desc->name : "(null)", desc->threadId ? "nonzero" : "zero", (long)user, self);
    if (!own(desc->name)) return;
    s_own_threads++;
    note("thread group=%s name=%s id=%s user=%ld thread=%s self=%d", desc->groupName, desc->name, desc->threadId ? "nonzero" : "zero", (long)user, thread_name(), self);
}

static void on_frame(void* user)
{
    s_frames++;
    note("frame n=%d user=%ld thread=%s after=%s clock=%d", s_frames, (long)user, thread_name(), s_last_mark, nativegfx_clock());
}

static void on_flow(uint8_t type, uint32_t id, void* user)
{
    (void)type; (void)id; (void)user;
    s_flows++;
}

static void on_marker_event(const MarkerDesc* desc, uint16_t type, uint16_t count, const void* data, void* user)
{
    s_events++;
    note("event marker=%s type=%u count=%u data=%d user=%ld thread=%s", desc->name, type, count, data != NULL, (long)user, thread_name());
}

// Five callbacks of each kind that do nothing: the header allows four of a kind.
#define FIVE(name, ...) \
    static void name##0(__VA_ARGS__) {} static void name##1(__VA_ARGS__) {} static void name##2(__VA_ARGS__) {} \
    static void name##3(__VA_ARGS__) {} static void name##4(__VA_ARGS__) {}
FIVE(idle_category, const CategoryDesc* desc, void* user)
FIVE(idle_marker, const MarkerDesc* desc, void* user)
FIVE(idle_thread, const ThreadDesc* desc, void* user)
FIVE(idle_frame, void* user)
FIVE(idle_flow, uint8_t type, uint32_t id, void* user)
#define REGISTER_FIVE(kind, Register, Unregister, name) do { \
    int r[5] = {cb->Register(name##0, NULL), cb->Register(name##1, NULL), cb->Register(name##2, NULL), cb->Register(name##3, NULL), cb->Register(name##4, NULL)}; \
    int u[5] = {cb->Unregister(name##0, NULL), cb->Unregister(name##1, NULL), cb->Unregister(name##2, NULL), cb->Unregister(name##3, NULL), cb->Unregister(name##4, NULL)}; \
    note("limit." kind " register=%d,%d,%d,%d,%d unregister=%d,%d,%d,%d,%d", r[0], r[1], r[2], r[3], r[4], u[0], u[1], u[2], u[3], u[4]); } while (0)

static void never_registered(const MarkerDesc* desc, void* user) { (void)desc; (void)user; }
static void never_registered_thread(const ThreadDesc* desc, void* user) { (void)desc; (void)user; }
static void never_registered_frame(void* user) { (void)user; }
static void never_registered_flow(uint8_t type, uint32_t id, void* user) { (void)type; (void)id; (void)user; }

/// A thread of the plugin's that registers with the profiler and unregisters.
static void* registered_thread(void* unused)
{
    (void)unused;
    uint64_t id = 0;
    int registered = s_profiler->RegisterThread(&id, "NativeGfxGroup", "NativeGfxThread");
    int unregistered = s_profiler->UnregisterThread(id);
    note("register.thread.own=%d id=%s self=%d unregister=%d", registered, id ? "nonzero" : "zero", id == (uint64_t)pthread_self(), unregistered);
    return NULL;
}

/// What the profiler callbacks answer and call, recorded while the plugin loads.
static void profiler_load(IUnityInterfaces* interfaces)
{
    // By their place in kInterfaces.
    void* profiler = interfaces->GetInterface(&kInterfaces[2].guid);
    s_profiler = interfaces->GetInterface(&kInterfaces[3].guid);
    void* callbacks = interfaces->GetInterface(&kInterfaces[4].guid);
    s_callbacks = interfaces->GetInterface(&kInterfaces[5].guid);
    void* vulkan = interfaces->GetInterface(&kInterfaces[8].guid);
    void* vulkan2 = interfaces->GetInterface(&kInterfaces[9].guid);
    // Each version of an interface is an object of its own.
    note("distinct profiler=%d callbacks=%d vulkan=%d", profiler && profiler != (void*)s_profiler, callbacks && callbacks != (void*)s_callbacks,
         vulkan && vulkan != vulkan2);
    if (!s_callbacks || !s_profiler) return;
    IUnityProfilerCallbacksV2* cb = s_callbacks;
    note("profiler enabled=%d available=%d", s_profiler->IsEnabled(), s_profiler->IsAvailable());

    s_listing = 1;
    int r = cb->RegisterCreateCategoryCallback(on_category, (void*)1);
    s_listing = 0;
    note("register.category=%d heard=%d", r, s_categories);
    r = cb->RegisterCreateMarkerCallback(on_marker, (void*)2);
    note("register.marker=%d", r);
    note("editor markers=%d", s_markers > 0);
    r = cb->RegisterCreateThreadCallback(on_thread, (void*)3);
    note("register.thread=%d first %s", r, s_first_thread);
    note("editor threads=%d", s_threads > 1);
    note("register.frame=%d", cb->RegisterFrameCallback(on_frame, (void*)4));
    note("register.flow=%d", cb->RegisterFlowEventCallback(on_flow, (void*)6));

    // A category of the plugin's: the first ID after the built-in ones, heard of once; its
    // name again is the same category; a built-in category's name.
    uint16_t category = 0xffff, again = 0xffff, builtin = 0xffff;
    r = s_profiler->CreateCategory(&category, "NativeGfxCategory", 0);
    note("create.category=%d id=%u heard=%d color=%08x", r, category, s_own_categories, s_own_color);
    int heard = s_categories;
    r = s_profiler->CreateCategory(&again, "NativeGfxCategory", 0);
    note("create.category.again=%d same=%d heard=%d", r, again == category, s_categories - heard);
    r = s_profiler->CreateCategory(&builtin, "Render", 0);
    note("create.category.builtin=%d id=%u heard=%d", r, builtin, s_categories - heard);
    // Registered twice, a callback is called twice and unregistered twice.
    r = cb->RegisterCreateCategoryCallback(on_category, (void*)1);
    heard = s_own_categories;
    s_profiler->CreateCategory(&again, "NativeGfxCategory2", 0);
    note("register.category.twice=%d heard=%d", r, s_own_categories - heard);
    int first = cb->UnregisterCreateCategoryCallback(on_category, (void*)1);
    int second = cb->UnregisterCreateCategoryCallback(on_category, (void*)1);
    note("unregister.category=%d,%d,%d", first, second, cb->UnregisterCreateCategoryCallback(on_category, (void*)1));

    // A marker of the plugin's, and its events.
    r = s_profiler->CreateMarker(&s_marker, "NativeGfx.Marker", category, 0, 0);
    note("create.marker=%d heard=%d", r, s_own_markers);
    const MarkerDesc* same = NULL;
    r = s_profiler->CreateMarker(&same, "NativeGfx.Marker", category, 0, 0);
    note("create.marker.again=%d same=%d heard=%d", r, same == s_marker, s_own_markers);
    s_profiler->EmitEvent(s_marker, 0, 0, NULL);
    note("emit.unregistered events=%d", s_events);
    r = cb->RegisterMarkerEventCallback(s_marker, on_marker_event, (void*)5);
    note("register.event=%d marker.callback=%d", r, s_marker->callback != NULL);
    s_profiler->EmitEvent(s_marker, 0, 0, NULL);
    s_profiler->EmitEvent(s_marker, 1, 0, NULL);
    s_profiler->EmitEvent(s_marker, 2, 0, NULL);
    r = cb->UnregisterMarkerEventCallback(s_marker, on_marker_event, (void*)5);
    note("unregister.event=%d marker.callback=%d", r, s_marker->callback != NULL);
    s_profiler->EmitEvent(s_marker, 0, 0, NULL);
    note("emit.after events=%d unregister.event.again=%d", s_events, cb->UnregisterMarkerEventCallback(s_marker, on_marker_event, (void*)5));

    // Callbacks that were never registered.
    note("unregister.unknown marker=%d thread=%d frame=%d flow=%d", cb->UnregisterCreateMarkerCallback(never_registered, NULL),
         cb->UnregisterCreateThreadCallback(never_registered_thread, NULL), cb->UnregisterFrameCallback(never_registered_frame, NULL),
         cb->UnregisterFlowEventCallback(never_registered_flow, NULL));

    pthread_t thread;
    if (pthread_create(&thread, NULL, registered_thread, NULL) == 0) pthread_join(thread, NULL);
    note("thread.heard=%d", s_own_threads);

    // The Vulkan interfaces on a device that is not Vulkan: what can be asked without the
    // device (the rest ends the Editor).
    typedef struct {
        unsigned char (*InterceptInitialization)(void* callback, void* user);
        void* (*InterceptVulkanAPI)(const char* name, void* function);
        void* needDevice[10];
        unsigned char (*ConfigureSwapchain)(const void* config);
        void* accessTextureByID;
        unsigned char (*AddInterceptInitialization)(void* callback, void* user, int priority);
        unsigned char (*RemoveInterceptInitialization)(void* callback);
    } VulkanElsewhere;
    VulkanElsewhere* elsewhere = vulkan2;
    if (elsewhere && s_graphics && s_graphics->GetRenderer() != 21) {
        int config[4] = {0, 0, 0, 0};
        note("vulkan.elsewhere intercept=%d add=%d remove=%d api=%d swapchain=%d", elsewhere->InterceptInitialization((void*)never_registered_frame, NULL),
             elsewhere->AddInterceptInitialization((void*)never_registered_frame, NULL, 0), elsewhere->RemoveInterceptInitialization((void*)never_registered_frame),
             elsewhere->InterceptVulkanAPI("vkCreateImage", (void*)never_registered_frame) != NULL, elsewhere->ConfigureSwapchain(config));
    }

    // A fifth callback of a kind, beside the ones registered above (one marker, one thread, one
    // frame and one flow callback; no category callback).
    REGISTER_FIVE("category", RegisterCreateCategoryCallback, UnregisterCreateCategoryCallback, idle_category);
    REGISTER_FIVE("marker", RegisterCreateMarkerCallback, UnregisterCreateMarkerCallback, idle_marker);
    REGISTER_FIVE("thread", RegisterCreateThreadCallback, UnregisterCreateThreadCallback, idle_thread);
    REGISTER_FIVE("frame", RegisterFrameCallback, UnregisterFrameCallback, idle_frame);
    REGISTER_FIVE("flow", RegisterFlowEventCallback, UnregisterFlowEventCallback, idle_flow);

    // Marker event callbacks unregistered without naming the marker: by callback and user data,
    // then by callback alone.
    const MarkerDesc* other = NULL;
    s_profiler->CreateMarker(&other, "NativeGfx.Marker2", category, 0, 0);
    cb->RegisterMarkerEventCallback(s_marker, on_marker_event, (void*)5);
    cb->RegisterMarkerEventCallback(other, on_marker_event, (void*)5);
    cb->RegisterMarkerEventCallback(other, on_marker_event, (void*)7);
    r = cb->UnregisterMarkerEventCallback(NULL, on_marker_event, (void*)5);
    note("unregister.event.user=%d marker.callback=%d second.callback=%d", r, s_marker->callback != NULL, other->callback != NULL);
    s_profiler->EmitEvent(s_marker, 2, 0, NULL);
    s_profiler->EmitEvent(other, 2, 0, NULL);
    r = cb->UnregisterMarkerEventCallback(NULL, on_marker_event, NULL);
    note("unregister.event.all=%d second.callback=%d again=%d", r, other->callback != NULL, cb->UnregisterMarkerEventCallback(NULL, on_marker_event, NULL));
    s_profiler->EmitEvent(other, 2, 0, NULL);
    note("emit.none events=%d", s_events);
}

EXPORT void NativeGfx_SetClock(int (*clock)(void)) { s_clock = clock; }

/// The end of the scripts' run: every callback unregistered, and what was heard meanwhile.
EXPORT void NativeGfx_Finish(void)
{
    if (!s_callbacks) return;
    IUnityProfilerCallbacksV2* cb = s_callbacks;
    note("finish frame=%d marker=%d thread=%d flow=%d frames=%d", cb->UnregisterFrameCallback(on_frame, (void*)4),
         cb->UnregisterCreateMarkerCallback(on_marker, (void*)2), cb->UnregisterCreateThreadCallback(on_thread, (void*)3),
         cb->UnregisterFlowEventCallback(on_flow, (void*)6), s_frames);
    note("editor flows=%d", s_flows > 0);
}

/// Copies the notes into `buffer` (at most `capacity` bytes with the terminator); returns
/// their length.
EXPORT int NativeGfx_Notes(char* buffer, int capacity)
{
    pthread_mutex_lock(&s_lock);
    int length = (int)s_notes_length;
    if (buffer && capacity > 0) {
        int n = length < capacity - 1 ? length : capacity - 1;
        if (n > 0) memcpy(buffer, s_notes, n);
        buffer[n] = 0;
    }
    pthread_mutex_unlock(&s_lock);
    return length;
}
#endif

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
#ifndef NATIVEGFX_PRELOAD
    profiler_load(interfaces);
    note("queue=%d", nativegfx_events_load(interfaces, note));
#endif
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
#ifndef NATIVEGFX_PRELOAD
    // The event a script issues with no render texture active: what is bound then.
    if (rel == 1 && gl_current()) {
        GLint fbo = 0, color = 0, depth = 0, viewport[4] = {0};
        gl.GetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &fbo);
        if (fbo != 0) {
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE, &color);
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE, &depth);
            while (gl.GetError() != 0) {}
        }
        gl.GetIntegerv(GL_VIEWPORT, viewport);
        note("screen framebuffer=%s color=%s depth=%s viewport=%d,%d,%d,%d", fbo ? "own" : "default",
             color == GL_TEXTURE ? "texture" : color == GL_RENDERBUFFER ? "renderbuffer" : "none",
             depth == GL_TEXTURE ? "texture" : depth == GL_RENDERBUFFER ? "renderbuffer" : "none", viewport[0], viewport[1], viewport[2], viewport[3]);
    }
#endif
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

#ifndef NATIVEGFX_PRELOAD
// ------------------------------------------------------------------ textures of every shape

#define GL_TEXTURE_3D 0x806F
#define GL_TEXTURE_CUBE_MAP 0x8513
#define GL_TEXTURE_CUBE_MAP_POSITIVE_X 0x8515
#define GL_TEXTURE_2D_ARRAY 0x8C1A
#define GL_TEXTURE_CUBE_MAP_ARRAY 0x9009
#define GL_TEXTURE_2D_MULTISAMPLE 0x9100
#define GL_TEXTURE_2D_MULTISAMPLE_ARRAY 0x9102
#define GL_TEXTURE_BINDING_3D 0x806A
#define GL_TEXTURE_BINDING_CUBE_MAP 0x8514
#define GL_TEXTURE_BINDING_2D_ARRAY 0x8C1D
#define GL_TEXTURE_BINDING_CUBE_MAP_ARRAY 0x900A
#define GL_TEXTURE_BINDING_2D_MULTISAMPLE 0x9104
#define GL_TEXTURE_BINDING_2D_MULTISAMPLE_ARRAY 0x9105
#define GL_TEXTURE_DEPTH 0x8071
#define GL_TEXTURE_SAMPLES 0x9106
#define GL_TEXTURE_IMMUTABLE_FORMAT 0x912F
#define GL_TEXTURE_IMMUTABLE_LEVELS 0x82DF
#define GL_RENDERBUFFER_BINDING 0x8CA7
#define GL_RENDERBUFFER_WIDTH 0x8D42
#define GL_RENDERBUFFER_HEIGHT 0x8D43
#define GL_RENDERBUFFER_INTERNAL_FORMAT 0x8D44
#define GL_RENDERBUFFER_SAMPLES 0x8CAB
#define GL_RED 0x1903
#define GL_RG 0x8227
#define GL_RGB 0x1907
#define GL_FLOAT 0x1406
#define GL_R8 0x8229
#define GL_RG8 0x822B
#define GL_RGB8 0x8051
#define GL_SRGB8_ALPHA8 0x8C43
#define GL_RGBA16F 0x881A
#define GL_RGBA32F 0x8814
#define GL_COMPRESSED_RGBA_S3TC_DXT5_EXT 0x83F3
#define GL_NEAREST_MIPMAP_NEAREST 0x2700
#define GL_TEXTURE_MAX_LEVEL 0x813D
#define GL_FRAMEBUFFER_ATTACHMENT_TEXTURE_LAYER 0x8CD4
#define GL_FRAMEBUFFER_ATTACHMENT_TEXTURE_CUBE_MAP_FACE 0x8CD3
#define GL_FRAMEBUFFER_ATTACHMENT_LAYERED 0x8DA7

/// The second record: what the texture callback found, read by the scripts line by line.
static char s_texture_notes[8192];
static size_t s_texture_notes_length;

static void texture_note(const char* format, ...)
{
    va_list args;
    va_start(args, format);
    size_t left = sizeof s_texture_notes - s_texture_notes_length;
    int n = vsnprintf(s_texture_notes + s_texture_notes_length, left, format, args);
    va_end(args);
    if (n < 0 || (size_t)n + 2 > left) return;
    s_texture_notes_length += n;
    s_texture_notes[s_texture_notes_length++] = '\n';
    s_texture_notes[s_texture_notes_length] = 0;
}

/// Native pointers of textures of the other shapes, by the scripts' labels.
#define SHAPE_COUNT 32
static struct { char label[32]; uintptr_t name; } s_shapes[SHAPE_COUNT];

static uintptr_t shape_named(const char* label)
{
    for (int i = 0; i < SHAPE_COUNT; i++)
        if (strcmp(s_shapes[i].label, label) == 0) return s_shapes[i].name;
    return 0;
}

static const struct { GLenum target, binding; const char* kind; } kTargets[] = {
    {GL_TEXTURE_2D, GL_TEXTURE_BINDING_2D, "2d"},
    {GL_TEXTURE_CUBE_MAP, GL_TEXTURE_BINDING_CUBE_MAP, "cube"},
    {GL_TEXTURE_2D_ARRAY, GL_TEXTURE_BINDING_2D_ARRAY, "array"},
    {GL_TEXTURE_3D, GL_TEXTURE_BINDING_3D, "3d"},
    {GL_TEXTURE_CUBE_MAP_ARRAY, GL_TEXTURE_BINDING_CUBE_MAP_ARRAY, "cubearray"},
    {GL_TEXTURE_2D_MULTISAMPLE, GL_TEXTURE_BINDING_2D_MULTISAMPLE, "multisample"},
    {GL_TEXTURE_2D_MULTISAMPLE_ARRAY, GL_TEXTURE_BINDING_2D_MULTISAMPLE_ARRAY, "multisamplearray"},
};

/// What GL says of a name, in `out`: a texture's target (the one it binds to), level 0's size
/// and internal format, its sample count and its number of levels; a renderbuffer's size, format
/// and sample count.
static void describe_shape(uintptr_t name, char* out, size_t size)
{
    if (!gl_current()) { snprintf(out, size, "gl=0"); return; }
    if (name == 0) { snprintf(out, size, "zero"); return; }
    if (name > 0xffffffffu) { snprintf(out, size, "pointer"); return; }
    while (gl.GetError() != 0) {}
    if (gl.IsTexture((GLuint)name)) {
        for (size_t i = 0; i < sizeof kTargets / sizeof kTargets[0]; i++) {
            GLint bound = 0;
            gl.GetIntegerv(kTargets[i].binding, &bound);
            gl.BindTexture(kTargets[i].target, (GLuint)name);
            // A texture binds to its own target only: GL_INVALID_OPERATION to another.
            if (gl.GetError() != 0) continue;
            GLenum target = kTargets[i].target;
            GLenum level_target = target == GL_TEXTURE_CUBE_MAP ? GL_TEXTURE_CUBE_MAP_POSITIVE_X : target;
            GLint w = 0, h = 0, d = 0, format = 0, samples = 0, immutable = 0, levels = 0;
            gl.GetTexLevelParameteriv(level_target, 0, GL_TEXTURE_WIDTH, &w);
            gl.GetTexLevelParameteriv(level_target, 0, GL_TEXTURE_HEIGHT, &h);
            gl.GetTexLevelParameteriv(level_target, 0, GL_TEXTURE_DEPTH, &d);
            gl.GetTexLevelParameteriv(level_target, 0, GL_TEXTURE_INTERNAL_FORMAT, &format);
            gl.GetTexLevelParameteriv(level_target, 0, GL_TEXTURE_SAMPLES, &samples);
            gl.GetTexParameteriv(target, GL_TEXTURE_IMMUTABLE_FORMAT, &immutable);
            if (immutable) {
                gl.GetTexParameteriv(target, GL_TEXTURE_IMMUTABLE_LEVELS, &levels);
            } else {
                // The levels that have texels.
                for (; levels < 16; levels++) {
                    GLint lw = 0;
                    gl.GetTexLevelParameteriv(level_target, levels, GL_TEXTURE_WIDTH, &lw);
                    if (gl.GetError() != 0 || lw == 0) break;
                }
            }
            while (gl.GetError() != 0) {}
            gl.BindTexture(target, (GLuint)bound);
            snprintf(out, size, "%s %dx%dx%d format=0x%x levels=%d samples=%d", kTargets[i].kind, w, h, d, format, levels, samples);
            return;
        }
        snprintf(out, size, "texture of an unknown target");
    } else if (gl.IsRenderbuffer((GLuint)name)) {
        GLint bound = 0, w = 0, h = 0, format = 0, samples = 0;
        gl.GetIntegerv(GL_RENDERBUFFER_BINDING, &bound);
        gl.BindRenderbuffer(GL_RENDERBUFFER, (GLuint)name);
        gl.GetRenderbufferParameteriv(GL_RENDERBUFFER, GL_RENDERBUFFER_WIDTH, &w);
        gl.GetRenderbufferParameteriv(GL_RENDERBUFFER, GL_RENDERBUFFER_HEIGHT, &h);
        gl.GetRenderbufferParameteriv(GL_RENDERBUFFER, GL_RENDERBUFFER_INTERNAL_FORMAT, &format);
        gl.GetRenderbufferParameteriv(GL_RENDERBUFFER, GL_RENDERBUFFER_SAMPLES, &samples);
        gl.BindRenderbuffer(GL_RENDERBUFFER, (GLuint)bound);
        snprintf(out, size, "renderbuffer %dx%d format=0x%x samples=%d", w, h, format, samples);
    } else {
        snprintf(out, size, "unknown");
    }
    while (gl.GetError() != 0) {}
}

/// The plugin's own textures for external textures, by index (see `make_textures`).
#define MADE_COUNT 13
static GLuint s_made[MADE_COUNT];

/// Texel (x, y) of the plugin's textures: (60x+10, 60y, 10, 255) as bytes.
static void pattern(unsigned char* texels, int width, int height, int channels)
{
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) {
            unsigned char full[4] = {(unsigned char)(x * 60 + 10), (unsigned char)(y * 60), 10, 255};
            memcpy(texels + (y * width + x) * channels, full, channels);
        }
}

static GLuint begin_texture(void)
{
    GLuint name = 0;
    gl.GenTextures(1, &name);
    gl.BindTexture(GL_TEXTURE_2D, name);
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, 0);
    return name;
}

/// An 8x8 RGBA8 texture with all four levels, each of one color: (40+60l, 200-50l, 0, 255).
static GLuint make_mips(void)
{
    GLuint name = begin_texture();
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST_MIPMAP_NEAREST);
    gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, 3);
    unsigned char texels[8 * 8 * 4];
    for (int level = 0; level < 4; level++) {
        int side = 8 >> level;
        for (int i = 0; i < side * side; i++) {
            texels[i * 4 + 0] = (unsigned char)(40 + 60 * level);
            texels[i * 4 + 1] = (unsigned char)(200 - 50 * level);
            texels[i * 4 + 2] = 0;
            texels[i * 4 + 3] = 255;
        }
        gl.TexImage2D(GL_TEXTURE_2D, level, GL_RGBA8, side, side, 0, GL_RGBA, GL_UNSIGNED_BYTE, texels);
    }
    return name;
}

static GLuint make_bytes(GLenum internal, GLenum format, int channels)
{
    GLuint name = begin_texture();
    unsigned char texels[4 * 4 * 4];
    pattern(texels, 4, 4, channels);
    gl.PixelStorei(GL_UNPACK_ALIGNMENT, 1);
    gl.TexImage2D(GL_TEXTURE_2D, 0, internal, 4, 4, 0, format, GL_UNSIGNED_BYTE, texels);
    gl.PixelStorei(GL_UNPACK_ALIGNMENT, 4);
    return name;
}

/// Texel (x, y) = (x/4, y/4, 0.5, 1) as floats.
static GLuint make_floats(GLenum internal)
{
    GLuint name = begin_texture();
    float texels[4 * 4 * 4];
    for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++) {
            float* t = texels + (y * 4 + x) * 4;
            t[0] = x / 4.0f; t[1] = y / 4.0f; t[2] = 0.5f; t[3] = 1.0f;
        }
    gl.TexImage2D(GL_TEXTURE_2D, 0, internal, 4, 4, 0, GL_RGBA, GL_FLOAT, texels);
    return name;
}

/// One DXT5 block: opaque, red and blue columns with their two blends between.
static GLuint make_dxt5(void)
{
    GLuint name = begin_texture();
    static const unsigned char block[16] = {
        255, 255, 0, 0, 0, 0, 0, 0,   // alpha: both ends 255
        0x00, 0xF8, 0x1F, 0x00,       // color0 = red (565), color1 = blue
        0xB4, 0xB4, 0xB4, 0xB4,       // each row: 0, 1, 3, 2 = red, blue, 1/3 red, 2/3 red
    };
    gl.CompressedTexImage2D(GL_TEXTURE_2D, 0, GL_COMPRESSED_RGBA_S3TC_DXT5_EXT, 4, 4, 0, 16, block);
    return name;
}

/// Records the GL errors left since the last check, by what was done.
static void texture_errors(const char* what)
{
    for (int i = 0; i < 8; i++) {
        GLenum error = gl.GetError();
        if (error == 0) break;
        texture_note("glerror %s 0x%x", what, error);
    }
}

static void make_textures(void)
{
    if (s_made[0] || !gl_current()) return;
    GLint bound = 0;
    gl.GetIntegerv(GL_TEXTURE_BINDING_2D, &bound);
    unpack_defaults();
    texture_errors("before");
    s_made[0] = make_mips();
    s_made[1] = make_mips();
    texture_errors("mips");
    s_made[2] = make_bytes(GL_SRGB8_ALPHA8, GL_RGBA, 4);
    s_made[3] = make_bytes(GL_SRGB8_ALPHA8, GL_RGBA, 4);
    s_made[4] = make_bytes(GL_RGBA8, GL_RGBA, 4);
    texture_errors("rgba");
    s_made[5] = make_bytes(GL_R8, GL_RED, 1);
    s_made[6] = make_bytes(GL_RG8, GL_RG, 2);
    s_made[7] = make_bytes(GL_RGB8, GL_RGB, 3);
    texture_errors("bytes");
    s_made[8] = make_floats(GL_RGBA16F);
    s_made[9] = make_floats(GL_RGBA32F);
    texture_errors("floats");
    s_made[10] = make_dxt5();
    texture_errors("dxt5");
    s_made[11] = make_bytes(GL_R8, GL_RED, 1);
    // An unsized internal format, as glTexImage2D takes one: what the driver makes of it.
    s_made[12] = make_bytes(GL_RGBA, GL_RGBA, 4);
    gl.BindTexture(GL_TEXTURE_2D, (GLuint)bound);
    texture_errors("made");
}

/// Writes a 2x2 block (red, green / blue, white) at (1, 1) of one image of the shapes "cube"
/// (its +X face), "array" (its layer 1) and "volume" (its slice 2).
static void write_shapes(void)
{
    static const unsigned char texels[16] = {255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255};
    unpack_defaults();
    GLint bound = 0;
    GLuint name = (GLuint)shape_named("cube");
    if (name) {
        gl.GetIntegerv(GL_TEXTURE_BINDING_CUBE_MAP, &bound);
        gl.BindTexture(GL_TEXTURE_CUBE_MAP, name);
        gl.TexSubImage2D(GL_TEXTURE_CUBE_MAP_POSITIVE_X, 0, 1, 1, 2, 2, GL_RGBA, GL_UNSIGNED_BYTE, texels);
        gl.BindTexture(GL_TEXTURE_CUBE_MAP, (GLuint)bound);
    }
    name = (GLuint)shape_named("array");
    if (name) {
        gl.GetIntegerv(GL_TEXTURE_BINDING_2D_ARRAY, &bound);
        gl.BindTexture(GL_TEXTURE_2D_ARRAY, name);
        gl.TexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 1, 1, 1, 2, 2, 1, GL_RGBA, GL_UNSIGNED_BYTE, texels);
        gl.BindTexture(GL_TEXTURE_2D_ARRAY, (GLuint)bound);
    }
    name = (GLuint)shape_named("volume");
    if (name) {
        gl.GetIntegerv(GL_TEXTURE_BINDING_3D, &bound);
        gl.BindTexture(GL_TEXTURE_3D, name);
        gl.TexSubImage3D(GL_TEXTURE_3D, 0, 1, 1, 2, 2, 2, 1, GL_RGBA, GL_UNSIGNED_BYTE, texels);
        gl.BindTexture(GL_TEXTURE_3D, (GLuint)bound);
    }
    texture_errors("write");
}

/// What is bound as the target: the framebuffer and the viewport, then its color and its depth
/// attachment, a line each (a texture's shape label when the scripts named it); then a 4x4
/// square at (2, 2): green, or (`grey`) half grey written with GL_FRAMEBUFFER_SRGB enabled, which
/// an sRGB attachment stores encoded.
static void describe_bound(int grey)
{
    GLint fbo = 0, viewport[4] = {0};
    gl.GetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &fbo);
    gl.GetIntegerv(GL_VIEWPORT, viewport);
    char parts[2][200] = {"none", "none"};
    static const GLenum kAttachments[2] = {GL_COLOR_ATTACHMENT0, GL_DEPTH_ATTACHMENT};
    for (int i = 0; i < 2 && fbo != 0; i++) {
        GLint type = 0, name = 0;
        gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, kAttachments[i], GL_FRAMEBUFFER_ATTACHMENT_OBJECT_TYPE, &type);
        if (type != 0)
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, kAttachments[i], GL_FRAMEBUFFER_ATTACHMENT_OBJECT_NAME, &name);
        while (gl.GetError() != 0) {}
        if (type == 0) continue;
        const char* label = "unnamed";
        // The first label the scripts gave the name (a pointer may be given twice).
        for (int s = SHAPE_COUNT - 1; s >= 0; s--)
            if (type == GL_TEXTURE && s_shapes[s].name == (uintptr_t)name && name != 0) label = s_shapes[s].label;
        char shape[120];
        describe_shape((uintptr_t)name, shape, sizeof shape);
        // A renderbuffer and a texture may share a number: the attachment's type decides.
        if (type == GL_RENDERBUFFER && strncmp(shape, "renderbuffer", 12) != 0) snprintf(shape, sizeof shape, "renderbuffer");
        // Which image of a cube, an array or a 3D texture is attached (a cube's face counts
        // from +X), or that all of them are.
        char image[32] = "";
        if (type == GL_TEXTURE && strncmp(shape, "2d", 2) != 0 && strncmp(shape, "multisample ", 12) != 0) {
            GLint layered = 0, layer = 0, face = 0;
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, kAttachments[i], GL_FRAMEBUFFER_ATTACHMENT_LAYERED, &layered);
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, kAttachments[i], GL_FRAMEBUFFER_ATTACHMENT_TEXTURE_LAYER, &layer);
            gl.GetFramebufferAttachmentParameteriv(GL_DRAW_FRAMEBUFFER, kAttachments[i], GL_FRAMEBUFFER_ATTACHMENT_TEXTURE_CUBE_MAP_FACE, &face);
            while (gl.GetError() != 0) {}
            if (layered) snprintf(image, sizeof image, " every image");
            else snprintf(image, sizeof image, " image=%d", face ? face - GL_TEXTURE_CUBE_MAP_POSITIVE_X : layer);
        }
        snprintf(parts[i], sizeof parts[i], "%.31s(%.119s)%s", label, shape, image);
    }
    texture_note("bound framebuffer=%s viewport=%d,%d,%d,%d", fbo ? "own" : "default", viewport[0], viewport[1], viewport[2], viewport[3]);
    texture_note("color %s", parts[0]);
    texture_note("depth %s", parts[1]);
    if (grey) {
        GLboolean encoding = gl.IsEnabled(GL_FRAMEBUFFER_SRGB);
        gl.Enable(GL_FRAMEBUFFER_SRGB);
        fill_bound(2, 2, 4, 4, 0.5f, 0.5f, 0.5f);
        if (!encoding) gl.Disable(GL_FRAMEBUFFER_SRGB);
    } else {
        fill_bound(2, 2, 4, 4, 0.0f, 1.0f, 0.0f);
    }
    while (gl.GetError() != 0) {}
}

/// The texture callback's events: 0 describes the shapes the scripts named, 1 makes the plugin's
/// textures, 2 writes into three shapes, 3 describes the bound target and draws into it, 4 the
/// same with half grey for an sRGB target; any other does nothing (what Unity itself reports
/// around a callback).
static void read_device_depth(void)
{
    typedef void (*GetTextureSubImageFn)(GLuint, GLint, GLint, GLint, GLint, GLsizei, GLsizei, GLsizei, GLenum, GLenum, GLsizei, void*);
    void* lib = dlopen("libOpenGL.so.0", RTLD_NOW | RTLD_GLOBAL);
    if (!lib) lib = dlopen("libGL.so.1", RTLD_NOW | RTLD_GLOBAL);
    GetTextureSubImageFn get = lib ? (GetTextureSubImageFn)dlsym(lib, "glGetTextureSubImage") : NULL;
    GLuint name = (GLuint)shape_named("depth");
    if (!get || !name) { texture_note("depth unavailable"); return; }
    GLint buffer = 0;
    gl.GetIntegerv(0x88ed, &buffer);
    gl.BindBuffer(0x88eb, 0);
    const GLenum states[] = {0x0d05, 0x0d02, 0x0d03, 0x0d04, 0x806c, 0x806b};
    GLint saved[6];
    for (int i = 0; i < 6; i++) { gl.GetIntegerv(states[i], &saved[i]); gl.PixelStorei(states[i], i == 0 ? 4 : 0); }
    while (gl.GetError()) {}
    float depth = -99.0f;
    get(name, 0, 0, 0, 0, 1, 1, 1, 0x1902, 0x1406, sizeof depth, &depth);
    GLenum error = gl.GetError();
    texture_note("depth value=%.9g error=0x%x", depth, error);
    get(name, 0, 6, 0, 0, 1, 1, 1, 0x1902, 0x1406, sizeof depth, &depth);
    error = gl.GetError();
    texture_note("depth right=%.9g error=0x%x", depth, error);
    for (int i = 0; i < 6; i++) gl.PixelStorei(states[i], saved[i]);
    gl.BindBuffer(0x88eb, (GLuint)buffer);
    if (lib) dlclose(lib);
}

static void on_texture_event(int id)
{
    if (!gl_current()) {
        texture_note("event %d gl=0", id);
        return;
    }
    switch (id) {
    case 0:
        for (int i = 0; i < SHAPE_COUNT; i++) {
            if (!s_shapes[i].label[0]) continue;
            char shape[160];
            describe_shape(s_shapes[i].name, shape, sizeof shape);
            texture_note("shape %s %s", s_shapes[i].label, shape);
        }
        break;
    case 1: make_textures(); break;
    case 2: write_shapes(); break;
    case 3: describe_bound(0); break;
    case 4: describe_bound(1); break;
    case 5: read_device_depth(); break;
    default: break;
    }
}

EXPORT void* NativeGfx_TextureEvent(void) { return (void*)on_texture_event; }

EXPORT void NativeGfx_SetShape(int index, const char* label, void* name)
{
    if (index < 0 || index >= SHAPE_COUNT) return;
    snprintf(s_shapes[index].label, sizeof s_shapes[index].label, "%s", label);
    s_shapes[index].name = (uintptr_t)name;
}

EXPORT void* NativeGfx_Made(int index) { return index >= 0 && index < MADE_COUNT ? (void*)(uintptr_t)s_made[index] : NULL; }

/// Copies the texture callback's record into `buffer` and empties it; returns its length.
EXPORT int NativeGfx_TextureNotes(char* buffer, int capacity)
{
    int length = (int)s_texture_notes_length;
    if (buffer && capacity > 0) {
        int n = length < capacity - 1 ? length : capacity - 1;
        if (n > 0) memcpy(buffer, s_texture_notes, n);
        buffer[n] = 0;
    }
    s_texture_notes_length = 0;
    s_texture_notes[0] = 0;
    return length;
}
#endif

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
EXPORT void NativeGfx_Mark(const char* text)
{
    record("mark %s thread=%s", text, thread_name());
#ifndef NATIVEGFX_PRELOAD
    note("mark %s", text);
    snprintf(s_last_mark, sizeof s_last_mark, "%s", text);
    if (strncmp(text, "frame ", 6) == 0) nativegfx_events_frame(atoi(text + 6));
#endif
}

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


#ifndef NATIVEGFX_PRELOAD
// Plugin-owned immutable cube and volume textures, initialized without changing bindings.
static GLuint external_owned_gl[2];
static void external_owned_gl_event(int id)
{
    void* lib = dlopen("libGL.so.1", RTLD_LAZY | RTLD_LOCAL);
    if (!lib) return;
    void (*create)(GLenum, GLsizei, GLuint*) = dlsym(lib, "glCreateTextures");
    void (*storage2)(GLuint, GLsizei, GLenum, GLsizei, GLsizei) = dlsym(lib, "glTextureStorage2D");
    void (*storage3)(GLuint, GLsizei, GLenum, GLsizei, GLsizei, GLsizei) = dlsym(lib, "glTextureStorage3D");
    void (*clear)(GLuint, GLint, GLenum, GLenum, const void*) = dlsym(lib, "glClearTexImage");
    void (*remove)(GLsizei, const GLuint*) = dlsym(lib, "glDeleteTextures");
    void (*finish)(void) = dlsym(lib, "glFinish");
    if (create && storage2 && storage3 && clear && remove && finish) {
        if (id == 1) {
            const float color[4] = {.25f, .5f, .75f, 1.f};
            create(0x8513, 1, &external_owned_gl[0]);
            storage2(external_owned_gl[0], 1, GL_RGBA8, 4, 4);
            clear(external_owned_gl[0], 0, GL_RGBA, GL_FLOAT, color);
            create(0x806F, 1, &external_owned_gl[1]);
            storage3(external_owned_gl[1], 1, GL_RGBA8, 4, 2, 2);
            clear(external_owned_gl[1], 0, GL_RGBA, GL_FLOAT, color);
        } else {
            remove(2, external_owned_gl);
            memset(external_owned_gl, 0, sizeof(external_owned_gl));
        }
        finish();
    }
    dlclose(lib);
}
EXPORT void* NativeGfx_ExternalOwnedEvent(void) { return (void*)external_owned_gl_event; }
EXPORT void* NativeGfx_ExternalOwnedPtr(int kind) { return (void*)(uintptr_t)external_owned_gl[kind]; }

#endif
