//! The Vulkan device's plugin interface (`IUnityGraphicsVulkan`, `IUnityGraphicsVulkanV2`): the
//! renderer's own instance, device and queue, and in rendering callbacks a command buffer the
//! plugin records into, as the Unity 6000.0.71f1 Editor gives them on Vulkan
//! (tests/projects/nativegfx `Reference/unity-6000.0.71f1/vulkan.jsonl`).
//!
//! A callback runs where it is in the stream: the work before it has been submitted; the plugin
//! records into a secondary command buffer that the renderer executes in a command buffer of its
//! own, submitted before the work after it. Every texture a plugin can reach (those whose native
//! pointer it was given, render textures for render buffer pointers, the current target) is first
//! put in a resting layout by wgpu (`transition_resources`); `AccessTexture` records the barrier
//! from it to the layout the plugin asks for, and the barrier back is recorded after the callback,
//! so wgpu's own tracking stays right. The plugin's commands are ordered with everything around
//! them by full memory barriers. With `kUnityVulkanRenderPass_EnsureInside` the callback runs
//! inside a render pass over the current target's attachments (loaded and stored), with its
//! viewport and scissor set. Not supported (logged, the call answers false or null): intercepting
//! Vulkan's initialization or functions, queue access (`AccessQueue`, events configured for it,
//! which are skipped), swapchain configuration, multisampled render buffers' resolve textures,
//! and switching the render pass state inside a callback.
use super::UnityRenderer;
use ash::vk;
use std::collections::HashMap;
use std::ffi::{c_char, c_void};
use std::sync::Mutex;
use unbound_runtime::plugin_api;
use unbound_runtime::render_commands::PluginCall;
use wgpu::hal::api::Vulkan;

#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct UnityVulkanInstance {
    pipeline_cache: vk::PipelineCache,
    instance: vk::Instance,
    physical_device: vk::PhysicalDevice,
    device: vk::Device,
    graphics_queue: vk::Queue,
    get_instance_proc_addr: Option<vk::PFN_vkGetInstanceProcAddr>,
    queue_family_index: u32,
    reserved: [usize; 8],
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct UnityVulkanMemory {
    memory: vk::DeviceMemory,
    offset: vk::DeviceSize,
    size: vk::DeviceSize,
    mapped: usize,
    flags: vk::MemoryPropertyFlags,
    memory_type_index: u32,
    reserved: [usize; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct UnityVulkanImage {
    memory: UnityVulkanMemory,
    image: vk::Image,
    layout: vk::ImageLayout,
    aspect: vk::ImageAspectFlags,
    usage: vk::ImageUsageFlags,
    format: vk::Format,
    extent: vk::Extent3D,
    tiling: vk::ImageTiling,
    image_type: vk::ImageType,
    samples: vk::SampleCountFlags,
    layers: i32,
    mip_count: i32,
    reserved: [usize; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct UnityVulkanBuffer {
    memory: UnityVulkanMemory,
    buffer: vk::Buffer,
    size_in_bytes: usize,
    usage: vk::BufferUsageFlags,
    reserved: [usize; 4],
}

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct UnityVulkanRecordingState {
    command_buffer: vk::CommandBuffer,
    command_buffer_level: vk::CommandBufferLevel,
    render_pass: vk::RenderPass,
    framebuffer: vk::Framebuffer,
    sub_pass_index: i32,
    current_frame_number: u64,
    safe_frame_number: u64,
    reserved: [usize; 4],
}

/// `UnityVulkanPluginEventConfig`.
#[repr(C)]
#[derive(Clone, Copy, Default, PartialEq, Eq, Debug)]
pub struct EventConfig {
    render_pass_precondition: i32,
    graphics_queue_access: i32,
    flags: u32,
}

/// `kUnityVulkanRenderPass_EnsureInside`, `kUnityVulkanResourceAccess_*`.
const DONT_CARE: i32 = 0;
const ENSURE_INSIDE: i32 = 1;
const ACCESS_OBSERVE_ONLY: i32 = 0;
const ACCESS_PIPELINE_BARRIER: i32 = 1;

type InitCallback = Option<
    unsafe extern "C" fn(
        vk::PFN_vkGetInstanceProcAddr,
        *mut c_void,
    ) -> vk::PFN_vkGetInstanceProcAddr,
>;
type QueueCallback = Option<unsafe extern "C" fn(i32, *mut c_void)>;

/// `IUnityGraphicsVulkan`; `IUnityGraphicsVulkanV2` adds the last two members.
#[repr(C)]
struct IUnityGraphicsVulkan {
    intercept_initialization: unsafe extern "C" fn(InitCallback, *mut c_void) -> bool,
    intercept_vulkan_api:
        unsafe extern "C" fn(*const c_char, vk::PFN_vkVoidFunction) -> vk::PFN_vkVoidFunction,
    configure_event: unsafe extern "C" fn(i32, *const EventConfig),
    instance: unsafe extern "C" fn() -> UnityVulkanInstance,
    command_recording_state: unsafe extern "C" fn(*mut UnityVulkanRecordingState, i32) -> bool,
    access_texture: unsafe extern "C" fn(
        *mut c_void,
        *const vk::ImageSubresource,
        vk::ImageLayout,
        vk::PipelineStageFlags,
        vk::AccessFlags,
        i32,
        *mut UnityVulkanImage,
    ) -> bool,
    access_render_buffer_texture: unsafe extern "C" fn(
        *mut c_void,
        *const vk::ImageSubresource,
        vk::ImageLayout,
        vk::PipelineStageFlags,
        vk::AccessFlags,
        i32,
        *mut UnityVulkanImage,
    ) -> bool,
    access_render_buffer_resolve_texture: unsafe extern "C" fn(
        *mut c_void,
        *const vk::ImageSubresource,
        vk::ImageLayout,
        vk::PipelineStageFlags,
        vk::AccessFlags,
        i32,
        *mut UnityVulkanImage,
    ) -> bool,
    access_buffer: unsafe extern "C" fn(
        *mut c_void,
        vk::PipelineStageFlags,
        vk::AccessFlags,
        i32,
        *mut UnityVulkanBuffer,
    ) -> bool,
    ensure_outside_render_pass: unsafe extern "C" fn(),
    ensure_inside_render_pass: unsafe extern "C" fn(),
    access_queue: unsafe extern "C" fn(QueueCallback, i32, *mut c_void, bool),
    configure_swapchain: unsafe extern "C" fn(*const c_void) -> bool,
    access_texture_by_id: unsafe extern "C" fn(
        u32,
        *const vk::ImageSubresource,
        vk::ImageLayout,
        vk::PipelineStageFlags,
        vk::AccessFlags,
        i32,
        *mut UnityVulkanImage,
    ) -> bool,
}

#[repr(C)]
struct IUnityGraphicsVulkanV2 {
    v1: IUnityGraphicsVulkan,
    add_intercept_initialization: unsafe extern "C" fn(InitCallback, *mut c_void, i32) -> bool,
    remove_intercept_initialization: unsafe extern "C" fn(InitCallback) -> bool,
}

const V1_FUNCTIONS: IUnityGraphicsVulkan = IUnityGraphicsVulkan {
    intercept_initialization,
    intercept_vulkan_api,
    configure_event,
    instance,
    command_recording_state,
    access_texture,
    access_render_buffer_texture,
    access_render_buffer_resolve_texture,
    access_buffer,
    ensure_outside_render_pass,
    ensure_inside_render_pass,
    access_queue,
    configure_swapchain,
    access_texture_by_id,
};
static V1: IUnityGraphicsVulkan = V1_FUNCTIONS;
static V2: IUnityGraphicsVulkanV2 = IUnityGraphicsVulkanV2 {
    v1: V1_FUNCTIONS,
    add_intercept_initialization,
    remove_intercept_initialization,
};

/// Offers both interfaces to plugins (the Vulkan device's).
pub fn provide_interfaces() {
    plugin_api::provide(plugin_api::guid::GRAPHICS_VULKAN, &V1 as *const _ as usize);
    plugin_api::provide(
        plugin_api::guid::GRAPHICS_VULKAN_V2,
        &V2 as *const _ as usize,
    );
}

/// An image a plugin can reach: what `AccessTexture` reports and the layout it rests in.
#[derive(Clone, Copy)]
struct ImageEntry {
    image: vk::Image,
    resting: vk::ImageLayout,
    aspect: vk::ImageAspectFlags,
    usage: vk::ImageUsageFlags,
    format: vk::Format,
    extent: vk::Extent3D,
    image_type: vk::ImageType,
    samples: vk::SampleCountFlags,
    layers: i32,
    mips: i32,
}

/// The callback being run: where the plugin records and what it can reach.
struct Scope {
    device: ash::Device,
    commands: vk::CommandBuffer,
    render_pass: vk::RenderPass,
    framebuffer: vk::Framebuffer,
    frame: u64,
    safe: u64,
    /// By native texture pointer, render buffer pointer and texture ID (the handle).
    by_pointer: HashMap<usize, ImageEntry>,
    by_handle: HashMap<u64, (ImageEntry, Option<ImageEntry>)>,
    buffers: HashMap<u64, (vk::Buffer, u64, vk::BufferUsageFlags)>,
    /// Images moved out of their resting layout (moved back after the callback).
    moved: Vec<(
        vk::Image,
        vk::ImageAspectFlags,
        vk::ImageLayout,
        vk::ImageLayout,
    )>,
}

#[derive(Default)]
struct State {
    instance: UnityVulkanInstance,
    configs: HashMap<i32, EventConfig>,
    scope: Option<Scope>,
    warned: Vec<&'static str>,
}

static STATE: Mutex<Option<State>> = Mutex::new(None);

fn with_state<R>(f: impl FnOnce(&mut State) -> R) -> R {
    let mut guard = STATE.lock().unwrap_or_else(|p| p.into_inner());
    f(guard.get_or_insert_with(State::default))
}

/// Logs an unsupported use once (as an error script message).
fn unsupported(what: &'static str) {
    let first = with_state(|s| {
        let first = !s.warned.contains(&what);
        if first {
            s.warned.push(what);
        }
        first
    });
    if first {
        plugin_api::log(
            0,
            &format!("Unbound: the Vulkan plugin interface's {what} is not supported"),
        );
    }
}

unsafe extern "C" fn intercept_initialization(_: InitCallback, _: *mut c_void) -> bool {
    // The device exists before any plugin loads: too late (the interface's own failure).
    false
}

unsafe extern "C" fn add_intercept_initialization(_: InitCallback, _: *mut c_void, _: i32) -> bool {
    false
}

unsafe extern "C" fn remove_intercept_initialization(_: InitCallback) -> bool {
    false
}

unsafe extern "C" fn intercept_vulkan_api(
    _: *const c_char,
    _: vk::PFN_vkVoidFunction,
) -> vk::PFN_vkVoidFunction {
    unsupported("InterceptVulkanAPI");
    None
}

unsafe extern "C" fn configure_event(id: i32, config: *const EventConfig) {
    // SAFETY: the plugin passes its configuration (or null).
    let Some(config) = (unsafe { config.as_ref() }).copied() else {
        return;
    };
    with_state(|s| s.configs.insert(id, config));
}

unsafe extern "C" fn instance() -> UnityVulkanInstance {
    with_state(|s| s.instance)
}

unsafe extern "C" fn command_recording_state(
    out: *mut UnityVulkanRecordingState,
    _queue_access: i32,
) -> bool {
    // SAFETY: the plugin passes where to write (or null).
    let Some(out) = (unsafe { out.as_mut() }) else {
        return false;
    };
    *out = UnityVulkanRecordingState {
        sub_pass_index: -1,
        ..Default::default()
    };
    with_state(|s| {
        let Some(scope) = &s.scope else {
            return false;
        };
        *out = UnityVulkanRecordingState {
            command_buffer: scope.commands,
            command_buffer_level: vk::CommandBufferLevel::SECONDARY,
            render_pass: scope.render_pass,
            framebuffer: scope.framebuffer,
            // The Editor answers 0 inside a render pass and outside alike (the header says -1
            // outside; measured: nativegfx vulkan.jsonl `plugin.outside.*`).
            sub_pass_index: 0,
            current_frame_number: scope.frame,
            safe_frame_number: scope.safe,
            reserved: [0; 4],
        };
        true
    })
}

/// `AccessTexture`'s answer for an image: its description (Unity leaves the layout field 0 and
/// the memory empty) after the barrier into `layout` a `PipelineBarrier` access records.
fn access(
    scope: &mut Scope,
    entry: ImageEntry,
    sub: *const vk::ImageSubresource,
    layout: vk::ImageLayout,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanImage,
) -> bool {
    // SAFETY: the plugin passes where to write (or null).
    let Some(out) = (unsafe { out.as_mut() }) else {
        return false;
    };
    if mode == ACCESS_PIPELINE_BARRIER {
        if scope.render_pass != vk::RenderPass::null() {
            unsupported("resource access inside a render pass");
            return false;
        }
        // SAFETY: the plugin's subresource (or null for the whole image).
        let range = match unsafe { sub.as_ref() } {
            Some(sub) => vk::ImageSubresourceRange {
                aspect_mask: sub.aspect_mask,
                base_mip_level: sub.mip_level,
                level_count: 1,
                base_array_layer: sub.array_layer,
                layer_count: 1,
            },
            None => whole(entry.aspect),
        };
        let barrier = vk::ImageMemoryBarrier::default()
            .src_access_mask(vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE)
            .dst_access_mask(access_flags)
            .old_layout(entry.resting)
            .new_layout(layout)
            .src_queue_family_index(vk::QUEUE_FAMILY_IGNORED)
            .dst_queue_family_index(vk::QUEUE_FAMILY_IGNORED)
            .image(entry.image)
            .subresource_range(range);
        // SAFETY: a command buffer in the recording state on the scope's device.
        unsafe {
            scope.device.cmd_pipeline_barrier(
                scope.commands,
                vk::PipelineStageFlags::ALL_COMMANDS,
                stages | vk::PipelineStageFlags::TOP_OF_PIPE,
                vk::DependencyFlags::empty(),
                &[],
                &[],
                &[barrier],
            )
        };
        if layout != entry.resting {
            scope
                .moved
                .push((entry.image, entry.aspect, layout, entry.resting));
        }
    } else if mode != ACCESS_OBSERVE_ONLY {
        unsupported("AccessTexture with kUnityVulkanResourceAccess_Recreate");
        return false;
    }
    *out = UnityVulkanImage {
        image: entry.image,
        aspect: entry.aspect,
        usage: entry.usage,
        format: entry.format,
        extent: entry.extent,
        tiling: vk::ImageTiling::OPTIMAL,
        image_type: entry.image_type,
        samples: entry.samples,
        layers: entry.layers,
        mip_count: entry.mips,
        ..Default::default()
    };
    true
}

fn whole(aspect: vk::ImageAspectFlags) -> vk::ImageSubresourceRange {
    vk::ImageSubresourceRange {
        aspect_mask: aspect,
        base_mip_level: 0,
        level_count: vk::REMAINING_MIP_LEVELS,
        base_array_layer: 0,
        layer_count: vk::REMAINING_ARRAY_LAYERS,
    }
}

/// The image a lookup finds in the callback's scope, accessed (false without a scope).
fn access_found(
    find: impl FnOnce(&Scope) -> Option<ImageEntry>,
    sub: *const vk::ImageSubresource,
    layout: vk::ImageLayout,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanImage,
) -> bool {
    with_state(|s| {
        let Some(scope) = s.scope.as_mut() else {
            return false;
        };
        match find(scope) {
            Some(entry) => access(scope, entry, sub, layout, stages, access_flags, mode, out),
            None => false,
        }
    })
}

unsafe extern "C" fn access_texture(
    texture: *mut c_void,
    sub: *const vk::ImageSubresource,
    layout: vk::ImageLayout,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanImage,
) -> bool {
    access_found(
        |s| s.by_pointer.get(&(texture as usize)).copied(),
        sub,
        layout,
        stages,
        access_flags,
        mode,
        out,
    )
}

unsafe extern "C" fn access_texture_by_id(
    id: u32,
    sub: *const vk::ImageSubresource,
    layout: vk::ImageLayout,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanImage,
) -> bool {
    access_found(
        |s| s.by_handle.get(&(id as u64)).map(|e| e.0),
        sub,
        layout,
        stages,
        access_flags,
        mode,
        out,
    )
}

unsafe extern "C" fn access_render_buffer_texture(
    buffer: *mut c_void,
    sub: *const vk::ImageSubresource,
    layout: vk::ImageLayout,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanImage,
) -> bool {
    let (handle, depth) = render_buffer(buffer as u64);
    access_found(
        |s| {
            s.by_handle
                .get(&handle)
                .and_then(|e| if depth { e.1 } else { Some(e.0) })
        },
        sub,
        layout,
        stages,
        access_flags,
        mode,
        out,
    )
}

unsafe extern "C" fn access_render_buffer_resolve_texture(
    _: *mut c_void,
    _: *const vk::ImageSubresource,
    _: vk::ImageLayout,
    _: vk::PipelineStageFlags,
    _: vk::AccessFlags,
    _: i32,
    _: *mut UnityVulkanImage,
) -> bool {
    unsupported("AccessRenderBufferResolveTexture");
    false
}

unsafe extern "C" fn access_buffer(
    buffer: *mut c_void,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanBuffer,
) -> bool {
    // SAFETY: the plugin passes where to write (or null).
    let Some(out) = (unsafe { out.as_mut() }) else {
        return false;
    };
    with_state(|s| {
        let Some(scope) = s.scope.as_mut() else {
            return false;
        };
        let Some(&(raw, size, usage)) = scope.buffers.get(&(buffer as u64)) else {
            return false;
        };
        if mode == ACCESS_PIPELINE_BARRIER {
            let barrier = vk::MemoryBarrier::default()
                .src_access_mask(vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE)
                .dst_access_mask(access_flags);
            // SAFETY: a command buffer in the recording state on the scope's device.
            unsafe {
                scope.device.cmd_pipeline_barrier(
                    scope.commands,
                    vk::PipelineStageFlags::ALL_COMMANDS,
                    stages | vk::PipelineStageFlags::TOP_OF_PIPE,
                    vk::DependencyFlags::empty(),
                    &[barrier],
                    &[],
                    &[],
                )
            };
        } else if mode != ACCESS_OBSERVE_ONLY {
            unsupported("AccessBuffer with kUnityVulkanResourceAccess_Recreate");
            return false;
        }
        *out = UnityVulkanBuffer {
            buffer: raw,
            size_in_bytes: size as usize,
            usage,
            ..Default::default()
        };
        true
    })
}

unsafe extern "C" fn ensure_outside_render_pass() {
    if with_state(|s| {
        s.scope
            .as_ref()
            .is_some_and(|c| c.render_pass != vk::RenderPass::null())
    }) {
        unsupported("EnsureOutsideRenderPass inside a callback configured for a render pass");
    }
}

unsafe extern "C" fn ensure_inside_render_pass() {
    if with_state(|s| {
        s.scope
            .as_ref()
            .is_some_and(|c| c.render_pass == vk::RenderPass::null())
    }) {
        unsupported(
            "EnsureInsideRenderPass (configure the event with kUnityVulkanRenderPass_EnsureInside)",
        );
    }
}

unsafe extern "C" fn access_queue(_: QueueCallback, _: i32, _: *mut c_void, _: bool) {
    unsupported("AccessQueue (the callback is not called)");
}

unsafe extern "C" fn configure_swapchain(_: *const c_void) -> bool {
    false
}

/// A render buffer pointer (`RenderBuffer.GetNativeRenderBufferPtr`, managed `NativePlugins`):
/// its render texture's handle and whether it is the depth buffer.
fn render_buffer(pointer: u64) -> (u64, bool) {
    const BASE: u64 = 0x5500_0000_0000_0000;
    let rest = pointer.wrapping_sub(BASE);
    (rest >> 1, rest & 1 == 1)
}

/// The renderer's side: what plugins get from the device, made once.
pub(super) struct VulkanPlugins {
    device: ash::Device,
    pool: vk::CommandPool,
    /// Native texture pointers: the image of each (handle, depth buffer) lives here.
    slots: HashMap<(u64, bool), Box<vk::Image>>,
    /// Secondary command buffers, render passes and framebuffers in flight, by the frame that
    /// submitted them (freed once the GPU finished it).
    retired: Vec<(u64, vk::CommandBuffer, vk::RenderPass, vk::Framebuffer)>,
}

impl VulkanPlugins {
    /// The device's handles for plugins; None when the renderer does not draw with Vulkan.
    pub fn new(device: &wgpu::Device, queue: &wgpu::Queue) -> Option<Self> {
        // SAFETY: the hal objects are only read, while the wgpu objects live.
        let hal = unsafe { device.as_hal::<Vulkan>() }?;
        let raw = hal.raw_device().clone();
        let shared = hal.shared_instance();
        let queue_hal = unsafe { queue.as_hal::<Vulkan>() }?;
        let family = hal.queue_family_index();
        let pool = unsafe {
            raw.create_command_pool(
                &vk::CommandPoolCreateInfo::default()
                    .queue_family_index(family)
                    .flags(vk::CommandPoolCreateFlags::TRANSIENT),
                None,
            )
        }
        .ok()?;
        // Plugins build pipelines with it, as with the Editor's (which it serializes).
        let cache =
            unsafe { raw.create_pipeline_cache(&vk::PipelineCacheCreateInfo::default(), None) }
                .unwrap_or_default();
        let entry = shared.entry();
        let instance = UnityVulkanInstance {
            pipeline_cache: cache,
            instance: shared.raw_instance().handle(),
            physical_device: hal.raw_physical_device(),
            device: raw.handle(),
            graphics_queue: queue_hal.as_raw(),
            get_instance_proc_addr: Some(entry.static_fn().get_instance_proc_addr),
            queue_family_index: family,
            reserved: [0; 8],
        };
        with_state(|s| s.instance = instance);
        Some(Self {
            device: raw,
            pool,
            slots: HashMap::new(),
            retired: Vec::new(),
        })
    }
}

/// The resting state a texture is put in for a callback and the Vulkan layout it means
/// (wgpu-hal's layouts for those uses).
fn resting(texture: &wgpu::Texture, depth: bool) -> (wgpu::TextureUses, vk::ImageLayout) {
    let usage = texture.usage();
    if usage.contains(wgpu::TextureUsages::RENDER_ATTACHMENT) {
        if depth {
            (
                wgpu::TextureUses::DEPTH_STENCIL_WRITE,
                vk::ImageLayout::DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
            )
        } else {
            (
                wgpu::TextureUses::COLOR_TARGET,
                vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL,
            )
        }
    } else if usage.contains(wgpu::TextureUsages::TEXTURE_BINDING) {
        (
            wgpu::TextureUses::RESOURCE,
            vk::ImageLayout::SHADER_READ_ONLY_OPTIMAL,
        )
    } else {
        (
            wgpu::TextureUses::COPY_DST,
            vk::ImageLayout::TRANSFER_DST_OPTIMAL,
        )
    }
}

/// What a plugin learns of a wgpu texture (its raw image and description).
fn image_entry(
    texture: &wgpu::Texture,
    depth: bool,
    resting_layout: vk::ImageLayout,
) -> Option<ImageEntry> {
    // SAFETY: the raw handle is only read, while the texture lives (the scope's).
    let raw = unsafe { texture.as_hal::<Vulkan>() }?;
    let image = unsafe { raw.raw_handle() };
    let usage = texture.usage();
    let mut flags = vk::ImageUsageFlags::empty();
    for (wgpu_usage, vk_usage) in [
        (
            wgpu::TextureUsages::COPY_SRC,
            vk::ImageUsageFlags::TRANSFER_SRC,
        ),
        (
            wgpu::TextureUsages::COPY_DST,
            vk::ImageUsageFlags::TRANSFER_DST,
        ),
        (
            wgpu::TextureUsages::TEXTURE_BINDING,
            vk::ImageUsageFlags::SAMPLED,
        ),
        (
            wgpu::TextureUsages::STORAGE_BINDING,
            vk::ImageUsageFlags::STORAGE,
        ),
    ] {
        if usage.contains(wgpu_usage) {
            flags |= vk_usage;
        }
    }
    if usage.contains(wgpu::TextureUsages::RENDER_ATTACHMENT) {
        flags |= if depth {
            vk::ImageUsageFlags::DEPTH_STENCIL_ATTACHMENT
        } else {
            vk::ImageUsageFlags::COLOR_ATTACHMENT
        };
    }
    let format = texture.format();
    let aspect = if !depth {
        vk::ImageAspectFlags::COLOR
    } else if format.has_stencil_aspect() {
        vk::ImageAspectFlags::DEPTH | vk::ImageAspectFlags::STENCIL
    } else {
        vk::ImageAspectFlags::DEPTH
    };
    let size = texture.size();
    let volume = texture.dimension() == wgpu::TextureDimension::D3;
    Some(ImageEntry {
        image,
        resting: resting_layout,
        aspect,
        usage: flags,
        format: vk_format(format),
        extent: vk::Extent3D {
            width: size.width,
            height: size.height,
            depth: if volume {
                size.depth_or_array_layers
            } else {
                1
            },
        },
        image_type: if volume {
            vk::ImageType::TYPE_3D
        } else {
            vk::ImageType::TYPE_2D
        },
        samples: vk::SampleCountFlags::from_raw(texture.sample_count()),
        layers: if volume {
            1
        } else {
            size.depth_or_array_layers as i32
        },
        mips: texture.mip_level_count() as i32,
    })
}

/// wgpu's texture format as Vulkan names it (wgpu-hal's choices; undefined for others).
fn vk_format(format: wgpu::TextureFormat) -> vk::Format {
    use wgpu::TextureFormat as F;
    match format {
        F::Rgba8Unorm => vk::Format::R8G8B8A8_UNORM,
        F::Rgba8UnormSrgb => vk::Format::R8G8B8A8_SRGB,
        F::Bgra8Unorm => vk::Format::B8G8R8A8_UNORM,
        F::Bgra8UnormSrgb => vk::Format::B8G8R8A8_SRGB,
        F::Rgba16Float => vk::Format::R16G16B16A16_SFLOAT,
        F::Rgba32Float => vk::Format::R32G32B32A32_SFLOAT,
        F::R8Unorm => vk::Format::R8_UNORM,
        F::Rg8Unorm => vk::Format::R8G8_UNORM,
        F::R16Float => vk::Format::R16_SFLOAT,
        F::Rg16Float => vk::Format::R16G16_SFLOAT,
        F::R32Float => vk::Format::R32_SFLOAT,
        F::Rg32Float => vk::Format::R32G32_SFLOAT,
        F::R16Unorm => vk::Format::R16_UNORM,
        F::Rg16Unorm => vk::Format::R16G16_UNORM,
        F::Rgba16Unorm => vk::Format::R16G16B16A16_UNORM,
        F::Rgb10a2Unorm => vk::Format::A2B10G10R10_UNORM_PACK32,
        F::Rg11b10Ufloat => vk::Format::B10G11R11_UFLOAT_PACK32,
        F::Depth32Float => vk::Format::D32_SFLOAT,
        F::Depth32FloatStencil8 => vk::Format::D32_SFLOAT_S8_UINT,
        F::Depth16Unorm => vk::Format::D16_UNORM,
        // wgpu-hal stores it as D24S8 where the device samples and draws into that.
        F::Depth24PlusStencil8 | F::Depth24Plus => vk::Format::D24_UNORM_S8_UINT,
        _ => vk::Format::UNDEFINED,
    }
}

impl UnityRenderer {
    /// A texture's native pointer on the Vulkan device: the address of its `VkImage`, stable for
    /// the texture (`depth`: its depth buffer's). 0 for an unknown texture.
    pub(super) fn vulkan_native_texture(&mut self, handle: u64, depth: bool) -> u64 {
        let image = self.textures.get(&handle).and_then(|t| {
            let texture = if depth {
                t.depth_texture.as_ref()?
            } else {
                &t.texture
            };
            // SAFETY: the raw handle is only read.
            unsafe { texture.as_hal::<Vulkan>().map(|raw| raw.raw_handle()) }
        });
        let (Some(image), Some(plugins)) = (image, self.vulkan_plugins.as_mut()) else {
            return 0;
        };
        let slot = plugins
            .slots
            .entry((handle, depth))
            .or_insert_with(|| Box::new(image));
        **slot = image;
        &**slot as *const vk::Image as u64
    }

    /// Runs a plugin callback on the Vulkan device (see the module's documentation).
    pub(super) fn run_plugin_vulkan(&mut self, call: &PluginCall, invoke: impl FnOnce()) {
        let id = match *call {
            PluginCall::Event { id, .. } | PluginCall::EventAndData { id, .. } => Some(id),
            _ => None,
        };
        let config = id
            .and_then(|id| with_state(|s| s.configs.get(&id).copied()))
            .unwrap_or_default();
        if config.graphics_queue_access != 0 {
            unsupported("an event configured for graphics queue access (it is skipped)");
            return;
        }
        // Inside a render pass when the event asks for one; a DontCare event stays where the
        // stream is: inside the current target's pass while it draws (a camera's events),
        // outside elsewhere (nativegfx `plugin.dontcare.*`).
        let inside = match config.render_pass_precondition {
            ENSURE_INSIDE => true,
            DONT_CARE => self.plugin_pass_open,
            _ => false,
        };
        let target = if inside {
            self.plugin_render_pass_target()
        } else {
            None
        };
        if inside && target.is_none() {
            unsupported(
                "kUnityVulkanRenderPass_EnsureInside without a target Unbound can begin a render pass on (the callback runs outside one)",
            );
        }
        let Some(plugins) = self.vulkan_plugins.as_mut() else {
            invoke();
            return;
        };
        let completed = self
            .frames_completed
            .load(std::sync::atomic::Ordering::Acquire);
        plugins.free_retired(completed);
        // What the plugin can reach: textures it has pointers of, render textures (render
        // buffer pointers, texture IDs) and the current target.
        let mut transitions: Vec<(wgpu::Texture, wgpu::TextureUses)> = Vec::new();
        let mut by_pointer = HashMap::new();
        let mut by_handle = HashMap::new();
        for (&handle, gpu) in &self.textures {
            let pointed = plugins.slots.contains_key(&(handle, false))
                || plugins.slots.contains_key(&(handle, true));
            if !pointed && !gpu.render_target {
                continue;
            }
            let (uses, layout) = resting(&gpu.texture, false);
            let Some(color) = image_entry(&gpu.texture, false, layout) else {
                continue;
            };
            transitions.push((gpu.texture.clone(), uses));
            let depth = gpu.depth_texture.as_ref().and_then(|d| {
                let (uses, layout) = resting(d, true);
                let entry = image_entry(d, true, layout)?;
                transitions.push((d.clone(), uses));
                Some(entry)
            });
            for (is_depth, entry) in [(false, Some(color)), (true, depth)] {
                if let (Some(entry), Some(slot)) =
                    (entry, plugins.slots.get_mut(&(handle, is_depth)))
                {
                    **slot = entry.image;
                    by_pointer.insert(&**slot as *const vk::Image as usize, entry);
                }
            }
            by_handle.insert(handle, (color, depth));
        }
        let buffers = self
            .buffers
            .iter()
            .filter_map(|(&handle, b)| {
                // SAFETY: the raw handle is only read.
                let raw = unsafe { b.buffer.as_hal::<Vulkan>() }
                    .map(|raw| unsafe { raw.raw_handle() })?;
                Some((
                    handle,
                    (raw, b.buffer.size(), buffer_usage(b.buffer.usage())),
                ))
            })
            .collect();
        // The resting layouts (wgpu's encoding), then the plugin's commands (raw encoding: an
        // encoder cannot mix the two), submitted together in that order.
        if let Some(target) = &target {
            for texture in &target.textures {
                let depth = texture.format().is_depth_stencil_format();
                transitions.push((texture.clone(), resting(texture, depth).0));
            }
        }
        let mut transition = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("unbound vulkan plugin layouts"),
            });
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("unbound vulkan plugin"),
            });
        transition.transition_resources(
            std::iter::empty(),
            transitions
                .iter()
                .map(|(texture, state)| wgpu::TextureTransition {
                    texture,
                    selector: None,
                    state: *state,
                }),
        );
        let device = plugins.device.clone();
        let pool = plugins.pool;
        // The frame being recorded (the renderer counts a frame when its rendering begins).
        let frame = self.frame_number + 1;
        let mut retired = None;
        // SAFETY: the raw command buffer is recorded only inside this closure, in the encoder's
        // recording state; every object it uses outlives the submission (retired below).
        unsafe {
            encoder.as_hal_mut::<Vulkan, _, _>(|hal| {
                let Some(hal) = hal else { return };
                let primary = hal.raw_handle();
                let Ok(secondary) = device.allocate_command_buffers(
                    &vk::CommandBufferAllocateInfo::default()
                        .command_pool(pool)
                        .level(vk::CommandBufferLevel::SECONDARY)
                        .command_buffer_count(1),
                ) else {
                    return;
                };
                let secondary = secondary[0];
                let (render_pass, framebuffer, extent) = match &target {
                    Some(t) => match begin_render_pass(&device, primary, t) {
                        Some((pass, framebuffer)) => (pass, framebuffer, Some(t.extent)),
                        None => (vk::RenderPass::null(), vk::Framebuffer::null(), None),
                    },
                    None => (vk::RenderPass::null(), vk::Framebuffer::null(), None),
                };
                let inheritance = vk::CommandBufferInheritanceInfo::default()
                    .render_pass(render_pass)
                    .subpass(0)
                    .framebuffer(framebuffer);
                let flags = if render_pass != vk::RenderPass::null() {
                    vk::CommandBufferUsageFlags::ONE_TIME_SUBMIT
                        | vk::CommandBufferUsageFlags::RENDER_PASS_CONTINUE
                } else {
                    vk::CommandBufferUsageFlags::ONE_TIME_SUBMIT
                };
                if device
                    .begin_command_buffer(
                        secondary,
                        &vk::CommandBufferBeginInfo::default()
                            .flags(flags)
                            .inheritance_info(&inheritance),
                    )
                    .is_err()
                {
                    return;
                }
                if let Some(extent) = extent {
                    // Unity sets the dynamic viewport and scissor to the target inside.
                    device.cmd_set_viewport(
                        secondary,
                        0,
                        &[vk::Viewport {
                            x: 0.0,
                            y: 0.0,
                            width: extent.width as f32,
                            height: extent.height as f32,
                            min_depth: 0.0,
                            max_depth: 1.0,
                        }],
                    );
                    device.cmd_set_scissor(
                        secondary,
                        0,
                        &[vk::Rect2D {
                            offset: vk::Offset2D::default(),
                            extent,
                        }],
                    );
                }
                if render_pass == vk::RenderPass::null() {
                    // Everything before, then the plugin's work.
                    full_barrier(&device, primary);
                }
                with_state(|s| {
                    s.scope = Some(Scope {
                        device: device.clone(),
                        commands: secondary,
                        render_pass,
                        framebuffer,
                        frame,
                        safe: completed,
                        by_pointer,
                        by_handle,
                        buffers,
                        moved: Vec::new(),
                    })
                });
                invoke();
                let scope = with_state(|s| s.scope.take());
                if let Some(scope) = scope {
                    for &(image, aspect, from, to) in &scope.moved {
                        let barrier = vk::ImageMemoryBarrier::default()
                            .src_access_mask(
                                vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE,
                            )
                            .dst_access_mask(
                                vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE,
                            )
                            .old_layout(from)
                            .new_layout(to)
                            .src_queue_family_index(vk::QUEUE_FAMILY_IGNORED)
                            .dst_queue_family_index(vk::QUEUE_FAMILY_IGNORED)
                            .image(image)
                            .subresource_range(whole(aspect));
                        device.cmd_pipeline_barrier(
                            secondary,
                            vk::PipelineStageFlags::ALL_COMMANDS,
                            vk::PipelineStageFlags::ALL_COMMANDS,
                            vk::DependencyFlags::empty(),
                            &[],
                            &[],
                            &[barrier],
                        );
                    }
                }
                let _ = device.end_command_buffer(secondary);
                device.cmd_execute_commands(primary, &[secondary]);
                if render_pass != vk::RenderPass::null() {
                    device.cmd_end_render_pass(primary);
                } else {
                    // The plugin's work, then everything after.
                    full_barrier(&device, primary);
                }
                retired = Some((frame, secondary, render_pass, framebuffer));
            })
        };
        self.queue.submit([transition.finish(), encoder.finish()]);
        if let (Some(retired), Some(plugins)) = (retired, self.vulkan_plugins.as_mut()) {
            plugins.retired.push(retired);
        }
        // The stream's pass goes on after an event that ran inside it.
        self.plugin_pass_open = inside && target.is_some();
    }

    /// The current target's color (multisampled where it is) and its depth as a plugin render
    /// pass's attachments; None for a target Unbound cannot (several colors, the screen).
    fn plugin_render_pass_target(&self) -> Option<PassTarget> {
        let target = self.state.target.as_ref()?;
        if !target.extra.is_empty() || target.screen {
            return None;
        }
        // SAFETY: raw handles only read, while the target's views live (the renderer's).
        let color = unsafe { target.color.as_hal::<Vulkan>().map(|v| v.raw_handle()) }?;
        // A multisampled color resolves into the target's texture at the pass's end, as the
        // renderer's own passes do.
        let resolve = target
            .resolve
            .as_ref()
            .filter(|_| target.samples > 1)
            .and_then(|r| {
                unsafe { r.as_hal::<Vulkan>().map(|v| v.raw_handle()) }.map(|raw| (raw, r))
            });
        // The depth attachment where its samples are the color's.
        let depth = target
            .depth
            .as_ref()
            .filter(|d| d.texture().sample_count() == target.samples)
            .and_then(|d| {
                unsafe { d.as_hal::<Vulkan>().map(|v| v.raw_handle()) }.map(|raw| (raw, d))
            });
        Some(PassTarget {
            color,
            samples: vk::SampleCountFlags::from_raw(target.samples),
            color_format: vk_format(target.format),
            depth: depth.map(|(raw, d)| (raw, vk_format(d.texture().format()))),
            resolve: resolve.map(|(raw, _)| raw),
            textures: std::iter::once(target.color.texture().clone())
                .chain(depth.map(|(_, d)| d.texture().clone()))
                .chain(resolve.map(|(_, r)| r.texture().clone()))
                .collect(),
            extent: vk::Extent2D {
                width: target.width,
                height: target.height,
            },
        })
    }
}

/// A plugin render pass's attachments.
struct PassTarget {
    color: vk::ImageView,
    samples: vk::SampleCountFlags,
    color_format: vk::Format,
    depth: Option<(vk::ImageView, vk::Format)>,
    extent: vk::Extent2D,
    /// The single-sample view a multisampled color resolves into.
    resolve: Option<vk::ImageView>,
    /// The attachments' textures (put in their resting layouts first; the depth one second).
    textures: Vec<wgpu::Texture>,
}

/// Begins a render pass over the target (attachments loaded and stored, in their resting
/// layouts) whose contents are secondary command buffers.
unsafe fn begin_render_pass(
    device: &ash::Device,
    primary: vk::CommandBuffer,
    target: &PassTarget,
) -> Option<(vk::RenderPass, vk::Framebuffer)> {
    let mut attachments = vec![
        vk::AttachmentDescription::default()
            .format(target.color_format)
            .samples(target.samples)
            .load_op(vk::AttachmentLoadOp::LOAD)
            .store_op(vk::AttachmentStoreOp::STORE)
            .stencil_load_op(vk::AttachmentLoadOp::DONT_CARE)
            .stencil_store_op(vk::AttachmentStoreOp::DONT_CARE)
            .initial_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL)
            .final_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL),
    ];
    let mut views = vec![target.color];
    if let Some((view, format)) = target.depth {
        attachments.push(
            vk::AttachmentDescription::default()
                .format(format)
                .samples(target.samples)
                .load_op(vk::AttachmentLoadOp::LOAD)
                .store_op(vk::AttachmentStoreOp::STORE)
                .stencil_load_op(vk::AttachmentLoadOp::LOAD)
                .stencil_store_op(vk::AttachmentStoreOp::STORE)
                .initial_layout(vk::ImageLayout::DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
                .final_layout(vk::ImageLayout::DEPTH_STENCIL_ATTACHMENT_OPTIMAL),
        );
        views.push(view);
    }
    let color_ref = [vk::AttachmentReference {
        attachment: 0,
        layout: vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL,
    }];
    let depth_ref = vk::AttachmentReference {
        attachment: 1,
        layout: vk::ImageLayout::DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
    };
    let resolve_ref = [vk::AttachmentReference {
        attachment: views.len() as u32,
        layout: vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL,
    }];
    if let Some(view) = target.resolve {
        attachments.push(
            vk::AttachmentDescription::default()
                .format(target.color_format)
                .samples(vk::SampleCountFlags::TYPE_1)
                .load_op(vk::AttachmentLoadOp::DONT_CARE)
                .store_op(vk::AttachmentStoreOp::STORE)
                .stencil_load_op(vk::AttachmentLoadOp::DONT_CARE)
                .stencil_store_op(vk::AttachmentStoreOp::DONT_CARE)
                .initial_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL)
                .final_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL),
        );
        views.push(view);
    }
    let mut subpass = vk::SubpassDescription::default()
        .pipeline_bind_point(vk::PipelineBindPoint::GRAPHICS)
        .color_attachments(&color_ref);
    if target.resolve.is_some() {
        subpass = subpass.resolve_attachments(&resolve_ref);
    }
    if target.depth.is_some() {
        subpass = subpass.depth_stencil_attachment(&depth_ref);
    }
    // Everything before, then the pass; the pass, then everything after.
    let dependencies = [
        vk::SubpassDependency {
            src_subpass: vk::SUBPASS_EXTERNAL,
            dst_subpass: 0,
            src_stage_mask: vk::PipelineStageFlags::ALL_COMMANDS,
            dst_stage_mask: vk::PipelineStageFlags::ALL_COMMANDS,
            src_access_mask: vk::AccessFlags::MEMORY_WRITE,
            dst_access_mask: vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE,
            dependency_flags: vk::DependencyFlags::empty(),
        },
        vk::SubpassDependency {
            src_subpass: 0,
            dst_subpass: vk::SUBPASS_EXTERNAL,
            src_stage_mask: vk::PipelineStageFlags::ALL_COMMANDS,
            dst_stage_mask: vk::PipelineStageFlags::ALL_COMMANDS,
            src_access_mask: vk::AccessFlags::MEMORY_WRITE,
            dst_access_mask: vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE,
            dependency_flags: vk::DependencyFlags::empty(),
        },
    ];
    let subpasses = [subpass];
    let pass = unsafe {
        device.create_render_pass(
            &vk::RenderPassCreateInfo::default()
                .attachments(&attachments)
                .subpasses(&subpasses)
                .dependencies(&dependencies),
            None,
        )
    }
    .ok()?;
    let framebuffer = match unsafe {
        device.create_framebuffer(
            &vk::FramebufferCreateInfo::default()
                .render_pass(pass)
                .attachments(&views)
                .width(target.extent.width)
                .height(target.extent.height)
                .layers(1),
            None,
        )
    } {
        Ok(framebuffer) => framebuffer,
        Err(_) => {
            unsafe { device.destroy_render_pass(pass, None) };
            return None;
        }
    };
    unsafe {
        device.cmd_begin_render_pass(
            primary,
            &vk::RenderPassBeginInfo::default()
                .render_pass(pass)
                .framebuffer(framebuffer)
                .render_area(vk::Rect2D {
                    offset: vk::Offset2D::default(),
                    extent: target.extent,
                }),
            vk::SubpassContents::SECONDARY_COMMAND_BUFFERS,
        )
    };
    Some((pass, framebuffer))
}

/// Orders all earlier commands' memory before all later commands.
unsafe fn full_barrier(device: &ash::Device, commands: vk::CommandBuffer) {
    let barrier = vk::MemoryBarrier::default()
        .src_access_mask(vk::AccessFlags::MEMORY_WRITE)
        .dst_access_mask(vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE);
    unsafe {
        device.cmd_pipeline_barrier(
            commands,
            vk::PipelineStageFlags::ALL_COMMANDS,
            vk::PipelineStageFlags::ALL_COMMANDS,
            vk::DependencyFlags::empty(),
            &[barrier],
            &[],
            &[],
        )
    };
}

fn buffer_usage(usage: wgpu::BufferUsages) -> vk::BufferUsageFlags {
    let mut flags = vk::BufferUsageFlags::empty();
    for (wgpu_usage, vk_usage) in [
        (
            wgpu::BufferUsages::COPY_SRC,
            vk::BufferUsageFlags::TRANSFER_SRC,
        ),
        (
            wgpu::BufferUsages::COPY_DST,
            vk::BufferUsageFlags::TRANSFER_DST,
        ),
        (
            wgpu::BufferUsages::UNIFORM,
            vk::BufferUsageFlags::UNIFORM_BUFFER,
        ),
        (
            wgpu::BufferUsages::STORAGE,
            vk::BufferUsageFlags::STORAGE_BUFFER,
        ),
        (
            wgpu::BufferUsages::INDEX,
            vk::BufferUsageFlags::INDEX_BUFFER,
        ),
        (
            wgpu::BufferUsages::VERTEX,
            vk::BufferUsageFlags::VERTEX_BUFFER,
        ),
        (
            wgpu::BufferUsages::INDIRECT,
            vk::BufferUsageFlags::INDIRECT_BUFFER,
        ),
    ] {
        if usage.contains(wgpu_usage) {
            flags |= vk_usage;
        }
    }
    flags
}

impl VulkanPlugins {
    /// Frees what callbacks of frames the GPU finished used.
    fn free_retired(&mut self, completed: u64) {
        let device = &self.device;
        let pool = self.pool;
        self.retired
            .retain(|&(frame, commands, pass, framebuffer)| {
                if frame > completed {
                    return true;
                }
                // SAFETY: the GPU finished the frame that submitted them.
                unsafe {
                    device.free_command_buffers(pool, &[commands]);
                    if framebuffer != vk::Framebuffer::null() {
                        device.destroy_framebuffer(framebuffer, None);
                    }
                    if pass != vk::RenderPass::null() {
                        device.destroy_render_pass(pass, None);
                    }
                }
                false
            });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::mem::{offset_of, size_of};

    /// The structs are laid out as IUnityGraphicsVulkan.h's on LP64 (x86-64, arm64).
    #[test]
    fn structs_have_the_headers_layout() {
        assert_eq!(size_of::<UnityVulkanInstance>(), 120);
        assert_eq!(offset_of!(UnityVulkanInstance, queue_family_index), 48);
        assert_eq!(size_of::<UnityVulkanMemory>(), 72);
        assert_eq!(size_of::<UnityVulkanImage>(), 160);
        assert_eq!(offset_of!(UnityVulkanImage, image), 72);
        assert_eq!(offset_of!(UnityVulkanImage, layout), 80);
        assert_eq!(offset_of!(UnityVulkanImage, mip_count), 124);
        assert_eq!(size_of::<UnityVulkanBuffer>(), 128);
        assert_eq!(offset_of!(UnityVulkanBuffer, usage), 88);
        assert_eq!(size_of::<UnityVulkanRecordingState>(), 88);
        assert_eq!(offset_of!(UnityVulkanRecordingState, sub_pass_index), 32);
        assert_eq!(offset_of!(UnityVulkanRecordingState, safe_frame_number), 48);
        assert_eq!(size_of::<EventConfig>(), 12);
        assert_eq!(size_of::<IUnityGraphicsVulkan>(), 14 * size_of::<usize>());
        assert_eq!(
            offset_of!(IUnityGraphicsVulkanV2, add_intercept_initialization),
            14 * size_of::<usize>()
        );
    }

    /// Render buffer pointers carry the render texture's handle and the buffer kind (managed
    /// `NativePlugins.RenderBufferPointer`).
    #[test]
    fn render_buffer_pointers_name_their_texture() {
        assert_eq!(
            render_buffer(0x5500_0000_0000_0000 | (42 << 1)),
            (42, false)
        );
        assert_eq!(
            render_buffer(0x5500_0000_0000_0000 | (42 << 1) | 1),
            (42, true)
        );
    }

    /// Outside a callback a plugin gets no command buffer, and no image or buffer.
    #[test]
    fn nothing_is_recorded_outside_a_callback() {
        let mut state = UnityVulkanRecordingState::default();
        assert!(!unsafe { command_recording_state(&mut state, 0) });
        assert_eq!(state.command_buffer, vk::CommandBuffer::null());
        let mut image = UnityVulkanImage::default();
        let mut pointer = 0u64;
        assert!(!unsafe {
            access_texture(
                &mut pointer as *mut u64 as *mut c_void,
                std::ptr::null(),
                vk::ImageLayout::TRANSFER_DST_OPTIMAL,
                vk::PipelineStageFlags::TRANSFER,
                vk::AccessFlags::TRANSFER_WRITE,
                ACCESS_PIPELINE_BARRIER,
                &mut image,
            )
        });
        let config = EventConfig {
            render_pass_precondition: ENSURE_INSIDE,
            graphics_queue_access: 0,
            flags: 0,
        };
        unsafe { configure_event(7, &config) };
        assert_eq!(with_state(|s| s.configs.get(&7).copied()), Some(config));
    }
}
