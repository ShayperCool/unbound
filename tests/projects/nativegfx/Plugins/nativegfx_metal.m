// The probe's native rendering plugin for the Metal device: it records what IUnityGraphicsMetal,
// IUnityGraphicsMetalV1 and IUnityGraphicsMetalV2 give it (the device, the command queue, and in
// rendering callbacks the current command buffer, command encoder and render pass descriptor;
// the textures behind native pointers and render buffers) and draws with Metal commands into what
// the scripts hand it. The runner builds it into the project copy
// (Assets/Plugins/macOS/libNativeGfxMetal.dylib) for Metal captures only; the scripts
// (NativeGfxMetalProbe.cs) read the record back.
//
// Only the public plugin headers' declarations are used, repeated here.
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#include <pthread.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define EXPORT __attribute__((visibility("default")))

typedef struct { unsigned long long high, low; } Guid;
typedef struct IUnityInterfaces {
    void* (*GetInterface)(const Guid* guid);
    void (*RegisterInterface)(const Guid* guid, void* ptr);
    void* (*GetInterfaceSplit)(unsigned long long high, unsigned long long low);
    void (*RegisterInterfaceSplit)(unsigned long long high, unsigned long long low, void* ptr);
} IUnityInterfaces;
typedef struct IUnityGraphics {
    int (*GetRenderer)(void);
    void (*RegisterDeviceEventCallback)(void (*callback)(int));
    void (*UnregisterDeviceEventCallback)(void (*callback)(int));
    int (*ReserveEventIDRange)(int count);
} IUnityGraphics;
typedef void* UnityRenderBuffer;
// IUnityGraphicsMetal and its V1 have the same members; V2 adds two in front.
typedef struct IUnityGraphicsMetalV1 {
    NSBundle* (*MetalBundle)(void);
    id<MTLDevice> (*MetalDevice)(void);
    id<MTLCommandBuffer> (*CurrentCommandBuffer)(void);
    id<MTLCommandEncoder> (*CurrentCommandEncoder)(void);
    void (*EndCurrentCommandEncoder)(void);
    MTLRenderPassDescriptor* (*CurrentRenderPassDescriptor)(void);
    UnityRenderBuffer (*RenderBufferFromHandle)(void* bufferHandle);
    id<MTLTexture> (*TextureFromRenderBuffer)(UnityRenderBuffer buffer);
    id<MTLTexture> (*AAResolvedTextureFromRenderBuffer)(UnityRenderBuffer buffer);
    id<MTLTexture> (*StencilTextureFromRenderBuffer)(UnityRenderBuffer buffer);
} IUnityGraphicsMetalV1;
typedef struct IUnityGraphicsMetalV2 {
    id<MTLCommandBuffer> (*CommitCurrentCommandBuffer)(void);
    id<MTLCommandQueue> (*CommandQueue)(void);
    IUnityGraphicsMetalV1 v1;
} IUnityGraphicsMetalV2;

static const Guid kGraphics = {0x7CBA0A9CA4DDB544ULL, 0x8C5AD4926EB17B11ULL};
static const Guid kMetal = {0x992C8EAEA95811E5ULL, 0x9A62C4B5B9876117ULL};
static const Guid kMetalV1 = {0x29F8F3D03833465EULL, 0x92138551C15D823DULL};
static const Guid kMetalV2 = {0xF58857784FEF46ECULL, 0x9DB7A8803B87DA3DULL};
static const Guid kVulkan = {0x95355348d4ef4e11ULL, 0x9789313dfcffcc87ULL};

// The probe's events, after the reserved base.
enum { EventState = 0, EventDraw = 1, EventEndBlit = 2, EventCommit = 3, EventQueueBlit = 4, EventCount = 5 };

// ------------------------------------------------------------------ record

static pthread_mutex_t s_lock = PTHREAD_MUTEX_INITIALIZER;
static NSMutableString* s_log;
static pthread_t s_main;

static void record(const char* format, ...)
{
    va_list args;
    va_start(args, format);
    char line[1024];
    vsnprintf(line, sizeof line, format, args);
    va_end(args);
    pthread_mutex_lock(&s_lock);
    if (!s_log) s_log = [NSMutableString string];
    [s_log appendFormat:@"%s\n", line];
    // A copy as it goes, for a run that ends before the scripts read the record.
    const char* out = getenv("NATIVEGFX_METAL_OUT");
    if (out) {
        char path[1024];
        snprintf(path, sizeof path, "%s.plugin.log", out);
        FILE* file = fopen(path, "a");
        if (file) { fprintf(file, "%s\n", line); fclose(file); }
    }
    pthread_mutex_unlock(&s_lock);
}

static IUnityInterfaces* s_interfaces;
static IUnityGraphics* s_graphics;
static IUnityGraphicsMetalV2* s_v2;
static IUnityGraphicsMetalV1* s_v1;
static int s_eventBase = -1;
static char s_step[64] = "load";
static float s_color[4] = {1, 0, 0, 1};
enum { Slots = 16 };
static void* s_textures[Slots];
static void* s_renderBuffers[Slots];
static id<MTLRenderPipelineState> s_pipelines[8];
static NSString* s_pipelineKeys[8];
static id<MTLTexture> s_external;

static int main_thread(void) { return pthread_equal(pthread_self(), s_main) ? 1 : 0; }

static const char* encoder_kind(id<MTLCommandEncoder> encoder)
{
    if (!encoder) return "none";
    if ([encoder conformsToProtocol:@protocol(MTLRenderCommandEncoder)]) return "render";
    if ([encoder conformsToProtocol:@protocol(MTLBlitCommandEncoder)]) return "blit";
    if ([encoder conformsToProtocol:@protocol(MTLComputeCommandEncoder)]) return "compute";
    return "other";
}

/// The index of a texture among the ones the scripts handed over ("t<i>" native pointers, "b<i>"
/// render buffers' textures), else "other" (none: "none").
static void texture_slot(id<MTLTexture> texture, char* out, size_t size)
{
    if (!texture) { snprintf(out, size, "none"); return; }
    for (int i = 0; i < Slots; i++)
        if (s_textures[i] && (__bridge id<MTLTexture>)s_textures[i] == texture) { snprintf(out, size, "t%d", i); return; }
    if (s_v1)
        for (int i = 0; i < Slots; i++) {
            if (!s_renderBuffers[i]) continue;
            UnityRenderBuffer buffer = s_v1->RenderBufferFromHandle(s_renderBuffers[i]);
            if (s_v1->TextureFromRenderBuffer(buffer) == texture) { snprintf(out, size, "b%d", i); return; }
            if (s_v1->AAResolvedTextureFromRenderBuffer(buffer) == texture) { snprintf(out, size, "b%d.resolved", i); return; }
            if (s_v1->StencilTextureFromRenderBuffer(buffer) == texture) { snprintf(out, size, "b%d.stencil", i); return; }
        }
    snprintf(out, size, "other");
}

/// A texture's shape and format.
static void describe(id<MTLTexture> t, char* out, size_t size)
{
    if (!t) { snprintf(out, size, "null"); return; }
    snprintf(out, size, "format=%lu type=%lu size=%lux%lux%lu mips=%lu layers=%lu samples=%lu", (unsigned long)t.pixelFormat,
             (unsigned long)t.textureType, (unsigned long)t.width, (unsigned long)t.height, (unsigned long)t.depth,
             (unsigned long)t.mipmapLevelCount, (unsigned long)t.arrayLength, (unsigned long)t.sampleCount);
}

/// How a texture's memory is set up (a record of its own: `<what>.memory`).
static void record_memory(const char* what, id<MTLTexture> t)
{
    if (!t) return;
    record("%s.memory step=%s usage=%lu storage=%lu framebufferOnly=%d", what, s_step, (unsigned long)t.usage,
           (unsigned long)t.storageMode, t.framebufferOnly ? 1 : 0);
}

static void record_pass(const char* what)
{
    MTLRenderPassDescriptor* pass = s_v1->CurrentRenderPassDescriptor();
    if (!pass) { record("%s.pass step=%s none", what, s_step); return; }
    for (int i = 0; i < 8; i++) {
        MTLRenderPassColorAttachmentDescriptor* color = pass.colorAttachments[i];
        if (!color.texture) continue;
        char slot[32], resolve[32], desc[256];
        texture_slot(color.texture, slot, sizeof slot);
        texture_slot(color.resolveTexture, resolve, sizeof resolve);
        describe(color.texture, desc, sizeof desc);
        record("%s.color%d step=%s texture=%s resolve=%s level=%lu slice=%lu %s", what, i, s_step, slot, resolve,
               (unsigned long)color.level, (unsigned long)color.slice, desc);
        record("%s.color%d.actions step=%s load=%lu store=%lu", what, i, s_step, (unsigned long)color.loadAction,
               (unsigned long)color.storeAction);
    }
    if (pass.depthAttachment.texture) {
        char slot[32], desc[256];
        texture_slot(pass.depthAttachment.texture, slot, sizeof slot);
        describe(pass.depthAttachment.texture, desc, sizeof desc);
        record("%s.depth step=%s texture=%s %s", what, s_step, slot, desc);
        record("%s.depth.actions step=%s load=%lu store=%lu", what, s_step, (unsigned long)pass.depthAttachment.loadAction,
               (unsigned long)pass.depthAttachment.storeAction);
    }
    if (pass.stencilAttachment.texture) {
        char slot[32];
        texture_slot(pass.stencilAttachment.texture, slot, sizeof slot);
        record("%s.stencil step=%s texture=%s same_as_depth=%d", what, s_step, slot,
               pass.stencilAttachment.texture == pass.depthAttachment.texture ? 1 : 0);
        record("%s.stencil.actions step=%s load=%lu store=%lu", what, s_step, (unsigned long)pass.stencilAttachment.loadAction,
               (unsigned long)pass.stencilAttachment.storeAction);
    }
}

static void record_state(const char* what)
{
    id<MTLCommandBuffer> buffer = s_v1->CurrentCommandBuffer();
    id<MTLCommandEncoder> encoder = s_v1->CurrentCommandEncoder();
    record("%s step=%s main=%d renderer=%d buffer=%d status=%ld queue_match=%d encoder=%s", what, s_step, main_thread(),
           s_graphics->GetRenderer(), buffer ? 1 : 0, buffer ? (long)buffer.status : -1L,
           buffer && s_v2 ? (buffer.commandQueue == s_v2->CommandQueue() ? 1 : 0) : -1, encoder_kind(encoder));
    record_pass(what);
}

// ------------------------------------------------------------------ drawing

static NSString* const kShader =
    @"#include <metal_stdlib>\n"
     "using namespace metal;\n"
     "struct V { float4 p [[position]]; };\n"
     "vertex V vs(uint i [[vertex_id]]) { float2 uv = float2((i << 1) & 2, i & 2); V o; o.p = float4(uv * 2 - 1, 0.5, 1); return o; }\n"
     "fragment float4 fs(constant float4& c [[buffer(0)]]) { return c; }\n";

/// A pipeline that draws one color over the current pass's attachments.
static id<MTLRenderPipelineState> pipeline_for(MTLRenderPassDescriptor* pass)
{
    id<MTLDevice> device = s_v1->MetalDevice();
    MTLRenderPipelineDescriptor* desc = [MTLRenderPipelineDescriptor new];
    NSMutableString* key = [NSMutableString string];
    NSUInteger samples = 1;
    for (int i = 0; i < 8; i++) {
        id<MTLTexture> t = pass.colorAttachments[i].texture;
        if (!t) continue;
        desc.colorAttachments[i].pixelFormat = t.pixelFormat;
        desc.colorAttachments[i].writeMask = i == 0 ? MTLColorWriteMaskAll : MTLColorWriteMaskNone;
        samples = t.sampleCount;
        [key appendFormat:@"c%d:%lu ", i, (unsigned long)t.pixelFormat];
    }
    if (pass.depthAttachment.texture) {
        desc.depthAttachmentPixelFormat = pass.depthAttachment.texture.pixelFormat;
        samples = pass.depthAttachment.texture.sampleCount;
        [key appendFormat:@"d:%lu ", (unsigned long)desc.depthAttachmentPixelFormat];
    }
    if (pass.stencilAttachment.texture) {
        desc.stencilAttachmentPixelFormat = pass.stencilAttachment.texture.pixelFormat;
        [key appendFormat:@"s:%lu ", (unsigned long)desc.stencilAttachmentPixelFormat];
    }
    desc.rasterSampleCount = samples;
    [key appendFormat:@"n:%lu", (unsigned long)samples];
    for (int i = 0; i < 8; i++)
        if (s_pipelineKeys[i] && [s_pipelineKeys[i] isEqualToString:key]) return s_pipelines[i];
    NSError* error = nil;
    id<MTLLibrary> library = [device newLibraryWithSource:kShader options:nil error:&error];
    if (!library) { record("error step=%s library %s", s_step, error.localizedDescription.UTF8String); return nil; }
    desc.vertexFunction = [library newFunctionWithName:@"vs"];
    desc.fragmentFunction = [library newFunctionWithName:@"fs"];
    id<MTLRenderPipelineState> state = [device newRenderPipelineStateWithDescriptor:desc error:&error];
    if (!state) { record("error step=%s pipeline %s", s_step, error.localizedDescription.UTF8String); return nil; }
    for (int i = 0; i < 8; i++)
        if (!s_pipelineKeys[i]) { s_pipelineKeys[i] = key; s_pipelines[i] = state; break; }
    return state;
}

static void draw(void)
{
    id<MTLCommandEncoder> encoder = s_v1->CurrentCommandEncoder();
    if (![encoder conformsToProtocol:@protocol(MTLRenderCommandEncoder)]) {
        record("draw step=%s drawn=0 encoder=%s", s_step, encoder_kind(encoder));
        return;
    }
    id<MTLRenderPipelineState> state = pipeline_for(s_v1->CurrentRenderPassDescriptor());
    if (!state) return;
    id<MTLRenderCommandEncoder> render = (id<MTLRenderCommandEncoder>)encoder;
    [render setRenderPipelineState:state];
    [render setFragmentBytes:s_color length:sizeof s_color atIndex:0];
    [render drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:3];
    record("draw step=%s drawn=1", s_step);
}

/// Copies a 4x4 block of the current color (8-bit RGBA or BGRA texels) into texture slot 0 at
/// column `x` of its last four rows with a blit encoder of `buffer`.
static void blit_block(id<MTLCommandBuffer> buffer, NSUInteger x)
{
    id<MTLTexture> target = (__bridge id<MTLTexture>)s_textures[0];
    if (!target || !buffer) { record("blit step=%s done=0", s_step); return; }
    uint8_t texels[16][4];
    int bgra = target.pixelFormat == MTLPixelFormatBGRA8Unorm || target.pixelFormat == MTLPixelFormatBGRA8Unorm_sRGB;
    for (int i = 0; i < 16; i++)
        for (int c = 0; c < 4; c++) texels[i][c] = (uint8_t)(s_color[bgra && c < 3 ? 2 - c : c] * 255.0f + 0.5f);
    id<MTLBuffer> source = [s_v1->MetalDevice() newBufferWithBytes:texels length:sizeof texels options:MTLResourceStorageModeShared];
    id<MTLBlitCommandEncoder> blit = [buffer blitCommandEncoder];
    [blit copyFromBuffer:source sourceOffset:0 sourceBytesPerRow:16 sourceBytesPerImage:64 sourceSize:MTLSizeMake(4, 4, 1)
               toTexture:target destinationSlice:0 destinationLevel:0 destinationOrigin:MTLOriginMake(x, target.height - 4, 0)];
    [blit endEncoding];
    record("blit step=%s done=1 encoder_after=%s", s_step, encoder_kind(s_v1->CurrentCommandEncoder()));
}

/// Ends the current encoder (inside a pass: the render encoder) and blits with the current buffer.
static void end_and_blit(void)
{
    // Nothing is asked of the interface between ending Unity's encoder and making one: asking
    // for the current encoder begins Unity's again.
    s_v1->EndCurrentCommandEncoder();
    blit_block(s_v1->CurrentCommandBuffer(), 0);
}

/// Blits with a command buffer of the plugin's own from the device's queue (V2), committed.
static void queue_blit(void)
{
    if (!s_v2) { record("queueblit step=%s v2=0", s_step); return; }
    id<MTLCommandBuffer> buffer = [s_v2->CommandQueue() commandBuffer];
    blit_block(buffer, 4);
    [buffer commit];
    [buffer waitUntilCompleted];
    record("queueblit step=%s status=%ld", s_step, (long)buffer.status);
}

static void commit(void)
{
    if (!s_v2) { record("commit step=%s v2=0", s_step); return; }
    id<MTLCommandBuffer> before = s_v2->v1.CurrentCommandBuffer();
    id<MTLCommandBuffer> committed = s_v2->CommitCurrentCommandBuffer();
    id<MTLCommandBuffer> after = s_v2->v1.CurrentCommandBuffer();
    record("commit step=%s returned_current=%d status=%ld new_buffer=%d after_status=%ld encoder=%s", s_step,
           committed == before ? 1 : 0, committed ? (long)committed.status : -1L, after && after != before ? 1 : 0,
           after ? (long)after.status : -1L, encoder_kind(s_v2->v1.CurrentCommandEncoder()));
}

static void on_event(int id)
{
    int event = id - s_eventBase;
    if (!s_v1) { record("event step=%s id=%d metal=0", s_step, event); return; }
    switch (event) {
    case EventState: record_state("state"); break;
    case EventDraw: record_state("state"); draw(); break;
    case EventEndBlit: record_state("state"); end_and_blit(); break;
    case EventCommit: record_state("state"); commit(); break;
    case EventQueueBlit: record_state("state"); queue_blit(); break;
    default: record("event step=%s unknown=%d", s_step, event); break;
    }
}

static void on_device_event(int type) { record("device_event type=%d step=%s renderer=%d", type, s_step, s_graphics->GetRenderer()); }

// ------------------------------------------------------------------ exports

EXPORT void UnityPluginLoad(IUnityInterfaces* interfaces)
{
    s_main = pthread_self();
    s_interfaces = interfaces;
    s_graphics = (IUnityGraphics*)interfaces->GetInterface(&kGraphics);
    s_v2 = (IUnityGraphicsMetalV2*)interfaces->GetInterface(&kMetalV2);
    s_v1 = (IUnityGraphicsMetalV1*)interfaces->GetInterface(&kMetalV1);
    IUnityGraphicsMetalV1* plain = (IUnityGraphicsMetalV1*)interfaces->GetInterface(&kMetal);
    record("load renderer=%d metal=%d v1=%d v2=%d vulkan=%d v1_is_plain=%d", s_graphics ? s_graphics->GetRenderer() : -1, plain ? 1 : 0,
           s_v1 ? 1 : 0, s_v2 ? 1 : 0, interfaces->GetInterface(&kVulkan) ? 1 : 0, plain == s_v1 ? 1 : 0);
    if (!s_v1) s_v1 = plain;
    if (s_v1) {
        id<MTLDevice> device = s_v1->MetalDevice();
        id<MTLDevice> system = MTLCreateSystemDefaultDevice();
        NSBundle* bundle = s_v1->MetalBundle();
        record("load.device device=%d system_default=%d bundle=%s", device ? 1 : 0, device == system ? 1 : 0,
               bundle ? bundle.bundleIdentifier.UTF8String : "null");
        if (s_v2) {
            id<MTLCommandQueue> queue = s_v2->CommandQueue();
            record("load.queue queue=%d device_match=%d", queue ? 1 : 0, queue && queue.device == device ? 1 : 0);
        }
        record("load.state buffer=%d encoder=%s pass=%d", s_v1->CurrentCommandBuffer() ? 1 : 0, encoder_kind(s_v1->CurrentCommandEncoder()),
               s_v1->CurrentRenderPassDescriptor() ? 1 : 0);
    }
    if (s_graphics) {
        s_graphics->RegisterDeviceEventCallback(on_device_event);
        s_eventBase = s_graphics->ReserveEventIDRange(EventCount);
    }
}

EXPORT void UnityPluginUnload(void) { record("unload"); }

EXPORT void* NativeGfxMtl_Event(void) { return (void*)on_event; }
EXPORT int NativeGfxMtl_EventBase(void) { return s_eventBase; }
EXPORT void NativeGfxMtl_Step(const char* step) { snprintf(s_step, sizeof s_step, "%s", step); }
EXPORT void NativeGfxMtl_SetColor(float r, float g, float b, float a) { s_color[0] = r; s_color[1] = g; s_color[2] = b; s_color[3] = a; }
EXPORT void NativeGfxMtl_SetTexture(int index, void* pointer) { if (index >= 0 && index < Slots) s_textures[index] = pointer; }
EXPORT void NativeGfxMtl_SetRenderBuffer(int index, void* pointer) { if (index >= 0 && index < Slots) s_renderBuffers[index] = pointer; }

/// The device's name (compared with SystemInfo.graphicsDeviceName by the scripts).
EXPORT int NativeGfxMtl_DeviceName(char* out, int capacity)
{
    const char* name = s_v1 && s_v1->MetalDevice() ? s_v1->MetalDevice().name.UTF8String : "";
    snprintf(out, capacity, "%s", name);
    return (int)strlen(name);
}

/// What a native texture pointer is (main thread, outside callbacks).
EXPORT void NativeGfxMtl_Describe(const char* what, void* pointer)
{
    char desc[256];
    describe((__bridge id<MTLTexture>)pointer, desc, sizeof desc);
    id object = (__bridge id)pointer;
    record("native.%s texture=%d %s", what, pointer && [object conformsToProtocol:@protocol(MTLTexture)] ? 1 : 0, pointer ? desc : "null");
    char memory[96];
    snprintf(memory, sizeof memory, "native.%s", what);
    record_memory(memory, (__bridge id<MTLTexture>)pointer);
}

/// What a render buffer pointer gives through the interface: its texture, resolved texture and
/// stencil texture, each named by the native pointers handed over.
EXPORT void NativeGfxMtl_DescribeRenderBuffer(const char* what, void* handle)
{
    if (!s_v1) return;
    UnityRenderBuffer buffer = s_v1->RenderBufferFromHandle(handle);
    char a[32], b[32], c[32], desc[256];
    texture_slot(s_v1->TextureFromRenderBuffer(buffer), a, sizeof a);
    texture_slot(s_v1->AAResolvedTextureFromRenderBuffer(buffer), b, sizeof b);
    texture_slot(s_v1->StencilTextureFromRenderBuffer(buffer), c, sizeof c);
    describe(s_v1->TextureFromRenderBuffer(buffer), desc, sizeof desc);
    record("renderbuffer.%s handle_is_buffer=%d texture=%s resolved=%s stencil=%s %s", what, buffer == handle ? 1 : 0, a, b, c, desc);
}

/// A 4x4 RGBA8 texture of the plugin's own, filled with the current color (an external texture).
EXPORT void* NativeGfxMtl_MakeTexture(void)
{
    MTLTextureDescriptor* desc = [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatRGBA8Unorm width:4 height:4 mipmapped:NO];
    desc.usage = MTLTextureUsageShaderRead;
    s_external = [s_v1->MetalDevice() newTextureWithDescriptor:desc];
    uint8_t texels[16][4];
    for (int i = 0; i < 16; i++)
        for (int c = 0; c < 4; c++) texels[i][c] = (uint8_t)(s_color[c] * 255.0f + 0.5f);
    [s_external replaceRegion:MTLRegionMake2D(0, 0, 4, 4) mipmapLevel:0 withBytes:texels bytesPerRow:16];
    return (__bridge void*)s_external;
}

EXPORT int NativeGfxMtl_Record(char* out, int capacity)
{
    pthread_mutex_lock(&s_lock);
    const char* text = s_log ? s_log.UTF8String : "";
    int length = (int)strlen(text);
    snprintf(out, capacity, "%s", text);
    pthread_mutex_unlock(&s_lock);
    return length;
}
