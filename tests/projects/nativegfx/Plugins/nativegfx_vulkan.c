// The probe's native rendering plugin for the Vulkan device: it records what IUnityGraphicsVulkan
// and IUnityGraphicsVulkanV2 give it (the instance, the recording state of rendering callbacks
// under each render pass precondition, the images and buffers behind native pointers) and draws
// with Vulkan commands into the textures, render buffers and buffers the scripts hand it. The
// runner builds it into the project copy (Assets/Plugins/x86_64/libNativeGfxVulkan.so) for
// Vulkan captures only; the scripts (NativeGfxVulkanProbe.cs) read the record back.
//
// Vulkan entry points come from the getInstanceProcAddr the interface hands over (the plugin
// links no Vulkan library); only the public plugin headers' declarations are used, repeated here.
#define VK_NO_PROTOTYPES
#include <vulkan/vulkan.h>
#include <pthread.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
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

typedef struct {
    VkPipelineCache pipelineCache;
    VkInstance instance;
    VkPhysicalDevice physicalDevice;
    VkDevice device;
    VkQueue graphicsQueue;
    PFN_vkGetInstanceProcAddr getInstanceProcAddr;
    unsigned int queueFamilyIndex;
    void* reserved[8];
} UnityVulkanInstance;
typedef struct {
    VkDeviceMemory memory;
    VkDeviceSize offset;
    VkDeviceSize size;
    void* mapped;
    VkMemoryPropertyFlags flags;
    unsigned int memoryTypeIndex;
    void* reserved[4];
} UnityVulkanMemory;
typedef struct {
    UnityVulkanMemory memory;
    VkImage image;
    VkImageLayout layout;
    VkImageAspectFlags aspect;
    VkImageUsageFlags usage;
    VkFormat format;
    VkExtent3D extent;
    VkImageTiling tiling;
    VkImageType type;
    VkSampleCountFlagBits samples;
    int layers;
    int mipCount;
    void* reserved[4];
} UnityVulkanImage;
typedef struct {
    UnityVulkanMemory memory;
    VkBuffer buffer;
    size_t sizeInBytes;
    VkBufferUsageFlags usage;
    void* reserved[4];
} UnityVulkanBuffer;
typedef struct {
    VkCommandBuffer commandBuffer;
    VkCommandBufferLevel commandBufferLevel;
    VkRenderPass renderPass;
    VkFramebuffer framebuffer;
    int subPassIndex;
    unsigned long long currentFrameNumber;
    unsigned long long safeFrameNumber;
    void* reserved[4];
} UnityVulkanRecordingState;
typedef struct {
    int renderPassPrecondition;
    int graphicsQueueAccess;
    uint32_t flags;
} UnityVulkanPluginEventConfig;
typedef VkImageSubresource SubResource;
typedef PFN_vkGetInstanceProcAddr (*InitCallback)(PFN_vkGetInstanceProcAddr, void*);
// IUnityGraphicsVulkan and its V2 share their first members.
typedef struct IUnityGraphicsVulkan {
    int (*InterceptInitialization)(InitCallback func, void* userdata);
    PFN_vkVoidFunction (*InterceptVulkanAPI)(const char* name, PFN_vkVoidFunction func);
    void (*ConfigureEvent)(int eventID, const UnityVulkanPluginEventConfig* config);
    UnityVulkanInstance (*Instance)(void);
    int (*CommandRecordingState)(UnityVulkanRecordingState* out, int queueAccess);
    int (*AccessTexture)(void* nativeTexture, const SubResource* sub, VkImageLayout layout, VkPipelineStageFlags stages,
                         VkAccessFlags access, int mode, UnityVulkanImage* out);
    int (*AccessRenderBufferTexture)(void* renderBuffer, const SubResource* sub, VkImageLayout layout,
                                     VkPipelineStageFlags stages, VkAccessFlags access, int mode, UnityVulkanImage* out);
    int (*AccessRenderBufferResolveTexture)(void* renderBuffer, const SubResource* sub, VkImageLayout layout,
                                            VkPipelineStageFlags stages, VkAccessFlags access, int mode, UnityVulkanImage* out);
    int (*AccessBuffer)(void* nativeBuffer, VkPipelineStageFlags stages, VkAccessFlags access, int mode, UnityVulkanBuffer* out);
    void (*EnsureOutsideRenderPass)(void);
    void (*EnsureInsideRenderPass)(void);
} IUnityGraphicsVulkan;

static const Guid kGraphics = {0x7CBA0A9CA4DDB544ULL, 0x8C5AD4926EB17B11ULL};
static const Guid kVulkan = {0x95355348d4ef4e11ULL, 0x9789313dfcffcc87ULL};
static const Guid kVulkanV2 = {0x329334C09DCA4787ULL, 0xB347DD92A0097FFCULL};

enum { DontCare = 0, EnsureInside = 1, EnsureOutside = 2 };
enum { ObserveOnly = 0, PipelineBarrier = 1 };
// The probe's events, after the reserved base: their render pass preconditions.
enum { EventDontCare = 0, EventOutside = 1, EventInside = 2, EventOutsideState = 3, EventAccessInside = 4, EventLeaveInside = 5,
       EventCount = 6 };

// ------------------------------------------------------------------ record

static pthread_mutex_t s_lock = PTHREAD_MUTEX_INITIALIZER;
static char* s_log;
static size_t s_length, s_capacity;
static pthread_t s_main;

static void record(const char* format, ...)
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
    if (s_length + n + 1 > s_capacity) {
        s_capacity = (s_length + n + 1) * 2;
        s_log = realloc(s_log, s_capacity);
    }
    memcpy(s_log + s_length, line, n + 1);
    s_length += n;
    pthread_mutex_unlock(&s_lock);
}

// ------------------------------------------------------------------ state

static IUnityGraphics* s_graphics;
static IUnityGraphicsVulkan* s_vulkan;
static UnityVulkanInstance s_instance;
static int s_event_base = -1;
// Native texture pointers: a Texture2D, a render texture, a Texture2D with mipmaps, render
// textures cleared from inside a camera's render pass (accessed there; after leaving it), an sRGB
// Texture2D, a render texture of the default (sRGB) read-write mode.
enum { TextureCount = 7 };
static void* s_textures[TextureCount];
static VkRenderPass s_inside_pass;
static void* s_render_buffers[2]; // color buffers: the render texture's, a multisampled one's
static void* s_buffer;           // a ComputeBuffer's native pointer
static char s_step[64] = "none";
static unsigned long long s_first_frame;
static int s_first_frame_known;

static struct {
    PFN_vkGetDeviceProcAddr GetDeviceProcAddr;
    PFN_vkGetDeviceQueue GetDeviceQueue;
    PFN_vkGetPhysicalDeviceQueueFamilyProperties GetPhysicalDeviceQueueFamilyProperties;
    PFN_vkCmdClearColorImage CmdClearColorImage;
    PFN_vkCmdClearAttachments CmdClearAttachments;
    PFN_vkCmdFillBuffer CmdFillBuffer;
} vk;

static void load_vulkan(void)
{
    PFN_vkGetInstanceProcAddr gipa = s_instance.getInstanceProcAddr;
    if (!gipa || !s_instance.instance) return;
#define I(name) *(void**)&vk.name = (void*)gipa(s_instance.instance, "vk" #name)
    I(GetDeviceProcAddr); I(GetPhysicalDeviceQueueFamilyProperties);
#undef I
    if (!vk.GetDeviceProcAddr || !s_instance.device) return;
#define D(name) *(void**)&vk.name = (void*)vk.GetDeviceProcAddr(s_instance.device, "vk" #name)
    D(GetDeviceQueue); D(CmdClearColorImage); D(CmdClearAttachments); D(CmdFillBuffer);
#undef D
}

static void record_instance(void)
{
    UnityVulkanInstance* i = &s_instance;
    record("instance instance=%d physical=%d device=%d queue=%d gipa=%d cache=%d", i->instance != NULL,
           i->physicalDevice != NULL, i->device != NULL, i->graphicsQueue != NULL, i->getInstanceProcAddr != NULL,
           i->pipelineCache != VK_NULL_HANDLE);
    if (!vk.GetPhysicalDeviceQueueFamilyProperties || !vk.GetDeviceQueue) {
        record("instance functions=0");
        return;
    }
    uint32_t count = 0;
    vk.GetPhysicalDeviceQueueFamilyProperties(i->physicalDevice, &count, NULL);
    VkQueueFamilyProperties families[16];
    if (count > 16) count = 16;
    vk.GetPhysicalDeviceQueueFamilyProperties(i->physicalDevice, &count, families);
    int graphics = i->queueFamilyIndex < count && (families[i->queueFamilyIndex].queueFlags & VK_QUEUE_GRAPHICS_BIT);
    VkQueue queue = VK_NULL_HANDLE;
    vk.GetDeviceQueue(i->device, i->queueFamilyIndex, 0, &queue);
    record("instance family.graphics=%d queue.first=%d", graphics, queue == i->graphicsQueue);
}

static void on_device_event(int type)
{
    record("device event=%d thread=%s", type, pthread_equal(pthread_self(), s_main) ? "main" : "other");
}

static PFN_vkGetInstanceProcAddr pass_through(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void* userdata)
{
    (void)userdata;
    return getInstanceProcAddr;
}

EXPORT void UnityPluginLoad(IUnityInterfaces* interfaces)
{
    s_main = pthread_self();
    s_graphics = (IUnityGraphics*)interfaces->GetInterface(&kGraphics);
    IUnityGraphicsVulkan* v1 = (IUnityGraphicsVulkan*)interfaces->GetInterface(&kVulkan);
    IUnityGraphicsVulkan* v2 = (IUnityGraphicsVulkan*)interfaces->GetInterface(&kVulkanV2);
    record("load renderer=%d vulkan=%d vulkanV2=%d", s_graphics ? s_graphics->GetRenderer() : -1, v1 != NULL, v2 != NULL);
    // The interfaces every device has: the profiler's callbacks and the event queue.
    const Guid callbacks = {0x572FDB38CE3C4B1FULL, 0xA6071A9A7C4F52D8ULL}, callbacksV2 = {0x5DEB59E88F2D4571ULL, 0x81E8583069A5E33CULL};
    const Guid eventQueue = {0x9959C347F5AE374DULL, 0x9BADE6FC8EF49E7FULL};
    record("interfaces profilerCallbacks=%d profilerCallbacksV2=%d eventQueue=%d", interfaces->GetInterface(&callbacks) != NULL,
           interfaces->GetInterface(&callbacksV2) != NULL, interfaces->GetInterface(&eventQueue) != NULL);
    s_vulkan = v2 ? v2 : v1;
    if (s_graphics) {
        s_event_base = s_graphics->ReserveEventIDRange(EventCount);
        s_graphics->RegisterDeviceEventCallback(on_device_event);
    }
    if (!s_vulkan) return;
    // The device exists already (the plugin loads with the first script call): too late to
    // intercept its initialization.
    record("intercept.late=%d", s_vulkan->InterceptInitialization(pass_through, NULL) != 0);
    s_instance = s_vulkan->Instance();
    load_vulkan();
    record_instance();
    const UnityVulkanPluginEventConfig outside = {EnsureOutside, 0, 0};
    const UnityVulkanPluginEventConfig inside = {EnsureInside, 0, 0};
    s_vulkan->ConfigureEvent(s_event_base + EventOutside, &outside);
    s_vulkan->ConfigureEvent(s_event_base + EventOutsideState, &outside);
    s_vulkan->ConfigureEvent(s_event_base + EventInside, &inside);
}

EXPORT void UnityPluginUnload(void)
{
    if (s_graphics) s_graphics->UnregisterDeviceEventCallback(on_device_event);
}

// ------------------------------------------------------------------ callbacks

/// The recording state as the event finds it: the command buffer and its level, the render pass,
/// framebuffer and subpass, the frame counters relative to the first event's.
static int recording_state(const char* name, UnityVulkanRecordingState* state)
{
    memset(state, 0, sizeof *state);
    int ok = s_vulkan->CommandRecordingState(state, 0) != 0;
    if (!s_first_frame_known && ok) {
        s_first_frame = state->currentFrameNumber;
        s_first_frame_known = 1;
    }
    record("%s step=%s ok=%d thread=%s cb=%d level=%d renderPass=%d framebuffer=%d subpass=%d frame=%lld safe.before=%d",
           name, s_step, ok, pthread_equal(pthread_self(), s_main) ? "main" : "other", state->commandBuffer != NULL,
           (int)state->commandBufferLevel, state->renderPass != VK_NULL_HANDLE, state->framebuffer != VK_NULL_HANDLE,
           state->subPassIndex, (long long)(state->currentFrameNumber - s_first_frame),
           state->safeFrameNumber < state->currentFrameNumber);
    return ok;
}

static void record_image(const char* name, int ok, const UnityVulkanImage* image, const void* pointer)
{
    record("%s ok=%d image=%d pointer.image=%d layout=%d aspect=%d usage=%d format=%d extent=%ux%ux%u tiling=%d type=%d "
           "samples=%d layers=%d mips=%d",
           name, ok, image->image != VK_NULL_HANDLE, pointer && *(const VkImage*)pointer == image->image,
           (int)image->layout, (int)image->aspect, (int)image->usage, (int)image->format, image->extent.width,
           image->extent.height, image->extent.depth, (int)image->tiling, (int)image->type, (int)image->samples,
           image->layers, image->mipCount);
}

static const VkImageSubresourceRange kColorRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};

/// Two accesses of one texture with mipmaps: the whole image for transfers (mip 0 cleared
/// magenta), then mip 1 alone in the general layout (cleared cyan), so the second barrier starts
/// from the layout the first left.
static void mips(void)
{
    UnityVulkanRecordingState state;
    UnityVulkanImage image;
    memset(&image, 0, sizeof image);
    int ok = s_textures[2] && s_vulkan->AccessTexture(s_textures[2], NULL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                                      VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                      PipelineBarrier, &image);
    record_image("mips.whole", ok, &image, s_textures[2]);
    if (ok && s_vulkan->CommandRecordingState(&state, 0) && vk.CmdClearColorImage) {
        VkClearColorValue magenta = {{1.0f, 0.0f, 1.0f, 1.0f}};
        vk.CmdClearColorImage(state.commandBuffer, image.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &magenta, 1, &kColorRange);
    }
    const VkImageSubresource mip1 = {VK_IMAGE_ASPECT_COLOR_BIT, 1, 0};
    memset(&image, 0, sizeof image);
    ok = s_textures[2] && s_vulkan->AccessTexture(s_textures[2], &mip1, VK_IMAGE_LAYOUT_GENERAL,
                                                  VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                  PipelineBarrier, &image);
    record_image("mips.one", ok, &image, s_textures[2]);
    if (ok && s_vulkan->CommandRecordingState(&state, 0) && vk.CmdClearColorImage) {
        VkClearColorValue cyan = {{0.0f, 1.0f, 1.0f, 1.0f}};
        const VkImageSubresourceRange range = {VK_IMAGE_ASPECT_COLOR_BIT, 1, 1, 0, 1};
        vk.CmdClearColorImage(state.commandBuffer, image.image, VK_IMAGE_LAYOUT_GENERAL, &cyan, 1, &range);
    }
}

/// A multisampled render texture's color buffer and its resolve texture (observed only).
static void resolve(void)
{
    UnityVulkanImage image;
    memset(&image, 0, sizeof image);
    int ok = s_render_buffers[1] && s_vulkan->AccessRenderBufferTexture(s_render_buffers[1], NULL, VK_IMAGE_LAYOUT_GENERAL,
                                                                         VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_READ_BIT,
                                                                         ObserveOnly, &image);
    record_image("msaa.buffer", ok, &image, NULL);
    VkImage multisampled = image.image;
    memset(&image, 0, sizeof image);
    ok = s_render_buffers[1] && s_vulkan->AccessRenderBufferResolveTexture(s_render_buffers[1], NULL, VK_IMAGE_LAYOUT_GENERAL,
                                                                          VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_READ_BIT,
                                                                          ObserveOnly, &image);
    record_image("msaa.resolve", ok, &image, NULL);
    record("msaa.distinct images=%d", ok && image.image != multisampled);
    // A single-sample render buffer's resolve texture is not asked for: the Editor crashes there.
}

/// Outside a render pass: a Texture2D cleared red and a render buffer cleared blue through their
/// images, a buffer filled with a pattern; then the mipmap and resolve controls.
static void outside(void)
{
    UnityVulkanRecordingState state;
    if (!recording_state("outside", &state)) return;
    UnityVulkanImage image;
    memset(&image, 0, sizeof image);
    int ok = s_textures[0] && s_vulkan->AccessTexture(s_textures[0], NULL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                                      VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                      PipelineBarrier, &image);
    record_image("texture", ok, &image, s_textures[0]);
    // Resource access invalidates the recording state.
    if (ok && s_vulkan->CommandRecordingState(&state, 0) && vk.CmdClearColorImage) {
        VkClearColorValue red = {{1.0f, 0.0f, 0.0f, 1.0f}};
        vk.CmdClearColorImage(state.commandBuffer, image.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &red, 1, &kColorRange);
    }
    memset(&image, 0, sizeof image);
    ok = s_textures[1] && s_vulkan->AccessTexture(s_textures[1], NULL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                                  VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                  ObserveOnly, &image);
    record_image("rendertexture.observe", ok, &image, s_textures[1]);
    memset(&image, 0, sizeof image);
    ok = s_render_buffers[0] && s_vulkan->AccessRenderBufferTexture(s_render_buffers[0], NULL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                                                VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                                PipelineBarrier, &image);
    record_image("renderbuffer", ok, &image, NULL);
    if (ok && s_vulkan->CommandRecordingState(&state, 0) && vk.CmdClearColorImage) {
        VkClearColorValue blue = {{0.0f, 0.0f, 1.0f, 1.0f}};
        vk.CmdClearColorImage(state.commandBuffer, image.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &blue, 1, &kColorRange);
    }
    UnityVulkanBuffer buffer;
    memset(&buffer, 0, sizeof buffer);
    ok = s_buffer && s_vulkan->AccessBuffer(s_buffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                            PipelineBarrier, &buffer);
    // A buffer's native pointer is the engine's own object, not documented to hold the VkBuffer: it
    // is only passed back.
    record("buffer ok=%d buffer=%d size=%zu usage.transferdst=%d", ok, buffer.buffer != VK_NULL_HANDLE,
           buffer.sizeInBytes, (buffer.usage & VK_BUFFER_USAGE_TRANSFER_DST_BIT) != 0);
    if (ok && s_vulkan->CommandRecordingState(&state, 0) && vk.CmdFillBuffer)
        vk.CmdFillBuffer(state.commandBuffer, buffer.buffer, 0, VK_WHOLE_SIZE, 0x01020304u);
    mips();
    resolve();
    memset(&image, 0, sizeof image);
    ok = s_textures[5] && s_vulkan->AccessTexture(s_textures[5], NULL, VK_IMAGE_LAYOUT_GENERAL, VK_PIPELINE_STAGE_TRANSFER_BIT,
                                                  VK_ACCESS_TRANSFER_READ_BIT, ObserveOnly, &image);
    record_image("srgb.texture", ok, &image, s_textures[5]);
    memset(&image, 0, sizeof image);
    ok = s_textures[6] && s_vulkan->AccessTexture(s_textures[6], NULL, VK_IMAGE_LAYOUT_GENERAL, VK_PIPELINE_STAGE_TRANSFER_BIT,
                                                  VK_ACCESS_TRANSFER_READ_BIT, ObserveOnly, &image);
    record_image("srgb.rendertexture", ok, &image, s_textures[6]);
}

/// Inside the render pass of the camera's target: a 4x4 square at (2, 2) of its first color
/// attachment cleared yellow. From the second time on: whether the render pass is the first's.
static void inside(void)
{
    UnityVulkanRecordingState state;
    if (!recording_state("inside", &state) || state.renderPass == VK_NULL_HANDLE || !vk.CmdClearAttachments) return;
    if (s_inside_pass != VK_NULL_HANDLE) record("inside.samepass step=%s same=%d", s_step, state.renderPass == s_inside_pass);
    s_inside_pass = state.renderPass;
    VkClearAttachment attachment = {VK_IMAGE_ASPECT_COLOR_BIT, 0, {.color = {{1.0f, 1.0f, 0.0f, 1.0f}}}};
    VkClearRect rect = {{{2, 2}, {4, 4}}, 0, 1};
    vk.CmdClearAttachments(state.commandBuffer, 1, &attachment, 1, &rect);
}

/// A DontCare event inside a camera's render pass that accesses a texture (cleared green: the
/// access ends the pass if it must), then asks for the pass again and clears a 2x2 square at
/// (0, 0) of its first color attachment white.
static void access_inside(void)
{
    UnityVulkanRecordingState state;
    if (!recording_state("access.before", &state)) return;
    UnityVulkanImage image;
    memset(&image, 0, sizeof image);
    int ok = s_textures[3] && s_vulkan->AccessTexture(s_textures[3], NULL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                                      VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                      PipelineBarrier, &image);
    record("access.inside ok=%d", ok);
    if (recording_state("access.after", &state) && ok && state.renderPass == VK_NULL_HANDLE && vk.CmdClearColorImage) {
        VkClearColorValue green = {{0.0f, 1.0f, 0.0f, 1.0f}};
        vk.CmdClearColorImage(state.commandBuffer, image.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &green, 1, &kColorRange);
    }
    s_vulkan->EnsureInsideRenderPass();
    if (recording_state("access.resumed", &state) && state.renderPass != VK_NULL_HANDLE && vk.CmdClearAttachments) {
        VkClearAttachment attachment = {VK_IMAGE_ASPECT_COLOR_BIT, 0, {.color = {{1.0f, 1.0f, 1.0f, 1.0f}}}};
        VkClearRect rect = {{{0, 0}, {2, 2}}, 0, 1};
        vk.CmdClearAttachments(state.commandBuffer, 1, &attachment, 1, &rect);
    }
}

/// A DontCare event inside a camera's render pass that leaves it (EnsureOutsideRenderPass),
/// clears a texture blue through its image outside any pass, asks for the pass again and clears
/// a 2x2 square at (6, 6) of its first color attachment red.
static void leave_inside(void)
{
    UnityVulkanRecordingState state;
    if (!recording_state("leave.before", &state)) return;
    s_vulkan->EnsureOutsideRenderPass();
    recording_state("leave.outside", &state);
    UnityVulkanImage image;
    memset(&image, 0, sizeof image);
    int ok = s_textures[4] && s_vulkan->AccessTexture(s_textures[4], NULL, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                                                      VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
                                                      PipelineBarrier, &image);
    if (recording_state("leave.access", &state) && ok && state.renderPass == VK_NULL_HANDLE && vk.CmdClearColorImage) {
        VkClearColorValue blue = {{0.0f, 0.0f, 1.0f, 1.0f}};
        vk.CmdClearColorImage(state.commandBuffer, image.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &blue, 1, &kColorRange);
    }
    s_vulkan->EnsureInsideRenderPass();
    if (recording_state("leave.resumed", &state) && state.renderPass != VK_NULL_HANDLE && vk.CmdClearAttachments) {
        VkClearAttachment attachment = {VK_IMAGE_ASPECT_COLOR_BIT, 0, {.color = {{1.0f, 0.0f, 0.0f, 1.0f}}}};
        VkClearRect rect = {{{6, 6}, {2, 2}}, 0, 1};
        vk.CmdClearAttachments(state.commandBuffer, 1, &attachment, 1, &rect);
    }
}

static void on_event(int id)
{
    switch (id - s_event_base) {
    case EventDontCare: { UnityVulkanRecordingState s; recording_state("dontcare", &s); break; }
    case EventOutside: outside(); break;
    case EventInside: inside(); break;
    case EventOutsideState: { UnityVulkanRecordingState s; recording_state("outside.state", &s); break; }
    case EventAccessInside: access_inside(); break;
    case EventLeaveInside: leave_inside(); break;
    default: record("event unknown=%d", id); break;
    }
}

// ------------------------------------------------------------------ script entry points

EXPORT void* NativeGfxVk_Event(void) { return (void*)on_event; }
EXPORT int NativeGfxVk_EventBase(void) { return s_event_base; }
EXPORT void NativeGfxVk_SetTexture(int index, void* pointer) { if (index >= 0 && index < TextureCount) s_textures[index] = pointer; }
EXPORT void NativeGfxVk_SetRenderBuffer(int index, void* pointer) { if (index >= 0 && index < 2) s_render_buffers[index] = pointer; }
EXPORT void NativeGfxVk_SetBuffer(void* pointer) { s_buffer = pointer; }
EXPORT void NativeGfxVk_Step(const char* step) { snprintf(s_step, sizeof s_step, "%s", step ? step : "none"); }

EXPORT int NativeGfxVk_Record(char* buffer, int capacity)
{
    pthread_mutex_lock(&s_lock);
    int length = (int)s_length;
    if (buffer && capacity > 0) {
        int n = length < capacity - 1 ? length : capacity - 1;
        if (s_log) memcpy(buffer, s_log, n);
        buffer[n] = 0;
    }
    pthread_mutex_unlock(&s_lock);
    return length;
}
