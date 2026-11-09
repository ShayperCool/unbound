//! The Vulkan device's plugin interface (`IUnityGraphicsVulkan`, `IUnityGraphicsVulkanV2`): the
//! renderer's own instance, device and queue, and in rendering callbacks a command buffer the
//! plugin records into, as the Unity 6000.0.71f1 Editor gives them on Vulkan
//! (tests/projects/nativegfx `Reference/unity-6000.0.71f1/vulkan.jsonl`).
//!
//! A callback runs where it is in the stream: the work before it has been submitted; the plugin
//! records into secondary command buffers that the renderer executes in a command buffer of its
//! own, submitted before the work after it. Every texture a plugin can reach (those whose native
//! pointer it was given, render textures for texture IDs and render buffer pointers, the current
//! target) is first put in a resting layout by wgpu (`transition_resources`). `AccessTexture`
//! and its kin record the barrier from the layout each subresource is in to the one the plugin
//! asks for, and the barriers back are recorded after the callback, so wgpu's own tracking stays
//! right. The plugin's commands are ordered with everything around them by full memory barriers.
//!
//! With `kUnityVulkanRenderPass_EnsureInside` the callback runs inside a render pass over the
//! current render texture target's attachments (loaded and stored, a multisampled color resolved
//! into its texture), with its viewport and scissor set; a DontCare event does where the stream
//! is inside a camera's pass. As the interface defines, `EnsureOutsideRenderPass` ends the pass,
//! `EnsureInsideRenderPass` begins it again, and a barrier for resource access inside the pass
//! is recorded between two passes (the callback stays inside, as in the Editor); each change
//! starts a new command buffer (the recording state is invalidated then). Not
//! supported (logged once, the call answers false or null): intercepting Vulkan's functions,
//! intercepting its initialization (answered as too late, unlogged: the device is made before
//! any plugin loads, preloaded ones too), queue access (`AccessQueue`, events configured for it, which are
//! skipped), swapchain configuration, `Recreate` access, and a render pass over the screen or
//! several colors (EnsureInside events are then skipped).
use super::UnityRenderer;
use super::plugins::vulkan::vk_format;
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
    /// Its wgpu texture among the callback's (put in its resting layout when accessed).
    texture: usize,
}

/// A texture's images: the texture itself, a render target's multisampled color (its color
/// buffer, resolved into the texture) and its depth buffer.
#[derive(Clone, Copy)]
struct TextureImages {
    texture: ImageEntry,
    msaa: Option<ImageEntry>,
    depth: Option<ImageEntry>,
}

/// A subresource's layout while a callback runs, and the one it is moved back to.
#[derive(Clone, Copy)]
struct Tracked {
    aspect: vk::ImageAspectFlags,
    current: vk::ImageLayout,
    resting: vk::ImageLayout,
}

/// A plugin render pass over the current target (both cached by the renderer: the handles stay
/// the same while the target does).
#[derive(Clone, Copy)]
struct PluginPass {
    render_pass: vk::RenderPass,
    framebuffer: vk::Framebuffer,
    extent: vk::Extent2D,
}

/// The callback being run: where the plugin records and what it can reach.
struct Scope {
    device: ash::Device,
    pool: vk::CommandPool,
    /// The pass over the current target (None: no target a render pass can begin on) and the
    /// target's images.
    pass: Option<PluginPass>,
    attachments: Vec<vk::Image>,
    /// The secondary command buffers recorded so far, in order, and whether each is recorded
    /// inside the plugin pass (the last one is recording).
    segments: Vec<(vk::CommandBuffer, bool)>,
    frame: u64,
    safe: u64,
    /// By native texture pointer, and by handle (texture IDs, render buffer pointers).
    by_pointer: HashMap<usize, ImageEntry>,
    by_handle: HashMap<u64, TextureImages>,
    buffers: HashMap<u64, (vk::Buffer, u64, vk::BufferUsageFlags)>,
    /// The layouts of subresources accessed, by (image, mip, layer).
    layouts: HashMap<(vk::Image, u32, u32), Tracked>,
    /// Buffers accessed (kept alive until the GPU finished the callback's frame) and textures
    /// (by index; put in their resting layouts before the callback's commands).
    accessed_buffers: Vec<u64>,
    accessed_textures: Vec<usize>,
}

impl Scope {
    /// The command buffer recording and whether it is inside the plugin pass.
    fn current(&self) -> Option<(vk::CommandBuffer, bool)> {
        self.segments.last().copied()
    }

    /// Records from here on inside the plugin pass or outside it, in a new command buffer when
    /// that changes (as `EnsureInsideRenderPass`/`EnsureOutsideRenderPass` and resource access
    /// do: the interface invalidates the recording state then). False when there is no pass to
    /// be inside, or no command buffer.
    fn enter(&mut self, inside: bool) -> bool {
        if let Some((_, current)) = self.current()
            && current == inside
        {
            return true;
        }
        let pass = if inside {
            let Some(pass) = self.pass else {
                return false;
            };
            // The attachments are used in their resting layouts.
            let attachments = std::mem::take(&mut self.attachments);
            if self.restore(Some(&attachments)) {
                warn_locked(
                    "a layout asked for an attachment of the render pass a callback goes back into (it is the attachment's again)",
                );
            }
            self.attachments = attachments;
            Some(pass)
        } else {
            None
        };
        // SAFETY: the pool is the renderer's, used on this thread only.
        let Some(commands) = (unsafe { begin_secondary(&self.device, self.pool, pass) }) else {
            return false;
        };
        if let Some((previous, _)) = self.current() {
            // SAFETY: a command buffer in the recording state.
            let _ = unsafe { self.device.end_command_buffer(previous) };
        }
        self.segments.push((commands, inside));
        true
    }

    /// Moves the accessed subresources of `images` (None: of every image) back to their resting
    /// layouts (recorded outside the pass). Whether any was moved.
    fn restore(&mut self, images: Option<&[vk::Image]>) -> bool {
        let Some((commands, false)) = self.current() else {
            return false;
        };
        let mut barriers = Vec::new();
        for (&(image, mip, layer), tracked) in &mut self.layouts {
            if tracked.current != tracked.resting && images.is_none_or(|i| i.contains(&image)) {
                barriers.push(image_barrier(
                    image,
                    subresources(tracked.aspect, mip, 1, layer, 1),
                    tracked.current,
                    tracked.resting,
                    vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE,
                ));
                tracked.current = tracked.resting;
            }
        }
        if barriers.is_empty() {
            return false;
        }
        // SAFETY: a command buffer in the recording state outside a render pass.
        unsafe {
            self.device.cmd_pipeline_barrier(
                commands,
                vk::PipelineStageFlags::ALL_COMMANDS,
                vk::PipelineStageFlags::ALL_COMMANDS,
                vk::DependencyFlags::empty(),
                &[],
                &[],
                &barriers,
            )
        };
        true
    }

    /// Records `record`'s commands outside the pass: inside it, the pass is ended for them and
    /// begun again after, as resource access inside a pass is answered in the Editor (the
    /// recording state is still inside after it: nativegfx `plugin.access.after.camera`). None
    /// without a command buffer.
    fn outside<R>(&mut self, record: impl FnOnce(&mut Self, vk::CommandBuffer) -> R) -> Option<R> {
        let inside = self.current()?.1;
        if !self.enter(false) {
            return None;
        }
        let (commands, _) = self.current()?;
        let result = record(self, commands);
        if inside {
            self.enter(true);
        }
        Some(result)
    }

    /// Records the barrier into `layout` for a `PipelineBarrier` access of `sub` (null: the whole
    /// image), from the layout each subresource is in (outside the pass, see [`Self::outside`]).
    /// False for a subresource the image does not have.
    fn barrier_image(
        &mut self,
        entry: ImageEntry,
        sub: Option<&vk::ImageSubresource>,
        layout: vk::ImageLayout,
        stages: vk::PipelineStageFlags,
        access_flags: vk::AccessFlags,
    ) -> bool {
        let (mips, layers) = match sub {
            Some(sub) => {
                if sub.mip_level >= entry.mips as u32 || sub.array_layer >= entry.layers as u32 {
                    return false;
                }
                (
                    sub.mip_level..sub.mip_level + 1,
                    sub.array_layer..sub.array_layer + 1,
                )
            }
            None => (0..entry.mips as u32, 0..entry.layers as u32),
        };
        self.outside(|scope, commands| {
            scope.record_image_barrier(commands, entry, mips, layers, layout, stages, access_flags)
        })
        .is_some()
    }

    fn record_image_barrier(
        &mut self,
        commands: vk::CommandBuffer,
        entry: ImageEntry,
        mips: std::ops::Range<u32>,
        layers: std::ops::Range<u32>,
        layout: vk::ImageLayout,
        stages: vk::PipelineStageFlags,
        access_flags: vk::AccessFlags,
    ) {
        let mut barriers = Vec::new();
        for mip in mips.clone() {
            for layer in layers.clone() {
                let tracked = self
                    .layouts
                    .entry((entry.image, mip, layer))
                    .or_insert(Tracked {
                        aspect: entry.aspect,
                        current: entry.resting,
                        resting: entry.resting,
                    });
                barriers.push(image_barrier(
                    entry.image,
                    subresources(entry.aspect, mip, 1, layer, 1),
                    tracked.current,
                    layout,
                    access_flags,
                ));
                tracked.current = layout;
            }
        }
        // One barrier for the range when every subresource was in one layout.
        if barriers.len() > 1
            && barriers
                .iter()
                .all(|b| b.old_layout == barriers[0].old_layout)
        {
            let old = barriers[0].old_layout;
            barriers = vec![image_barrier(
                entry.image,
                subresources(
                    entry.aspect,
                    mips.start,
                    mips.len() as u32,
                    layers.start,
                    layers.len() as u32,
                ),
                old,
                layout,
                access_flags,
            )];
        }
        // SAFETY: a command buffer in the recording state outside a render pass.
        unsafe {
            self.device.cmd_pipeline_barrier(
                commands,
                vk::PipelineStageFlags::ALL_COMMANDS,
                stages | vk::PipelineStageFlags::TOP_OF_PIPE,
                vk::DependencyFlags::empty(),
                &[],
                &[],
                &barriers,
            )
        };
    }

    /// Ends the callback's recording: subresources back in their resting layouts, the last
    /// command buffer ended. The command buffers in order, with whether each is inside the pass.
    fn finish(mut self) -> Finished {
        if self.layouts.values().any(|t| t.current != t.resting) {
            if self.enter(false) {
                self.restore(None);
            } else {
                tracing::error!(
                    "Unbound: a Vulkan plugin's images could not be moved back to their layouts"
                );
            }
        }
        if let Some((last, _)) = self.current() {
            // SAFETY: a command buffer in the recording state.
            let _ = unsafe { self.device.end_command_buffer(last) };
        }
        Finished {
            segments: self.segments,
            // A pass made holds its attachments' views.
            pass: self.pass,
            accessed_buffers: self.accessed_buffers,
            accessed_textures: self.accessed_textures,
        }
    }
}

/// What a callback recorded (see [`Scope::finish`]).
struct Finished {
    segments: Vec<(vk::CommandBuffer, bool)>,
    pass: Option<PluginPass>,
    accessed_buffers: Vec<u64>,
    accessed_textures: Vec<usize>,
}

fn subresources(
    aspect: vk::ImageAspectFlags,
    mip: u32,
    mips: u32,
    layer: u32,
    layers: u32,
) -> vk::ImageSubresourceRange {
    vk::ImageSubresourceRange {
        aspect_mask: aspect,
        base_mip_level: mip,
        level_count: mips,
        base_array_layer: layer,
        layer_count: layers,
    }
}

fn image_barrier(
    image: vk::Image,
    range: vk::ImageSubresourceRange,
    from: vk::ImageLayout,
    to: vk::ImageLayout,
    access_flags: vk::AccessFlags,
) -> vk::ImageMemoryBarrier<'static> {
    vk::ImageMemoryBarrier::default()
        .src_access_mask(vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE)
        .dst_access_mask(access_flags)
        .old_layout(from)
        .new_layout(to)
        .src_queue_family_index(vk::QUEUE_FAMILY_IGNORED)
        .dst_queue_family_index(vk::QUEUE_FAMILY_IGNORED)
        .image(image)
        .subresource_range(range)
}

#[derive(Default)]
struct State {
    instance: UnityVulkanInstance,
    configs: HashMap<i32, EventConfig>,
    scope: Option<Scope>,
    /// Unsupported uses logged once, and those to log when the lock is released.
    warned: Vec<&'static str>,
    to_warn: Vec<&'static str>,
}

impl State {
    /// Logs an unsupported use once (as an error script message), after the lock is released.
    fn warn(&mut self, what: &'static str) {
        if !self.warned.contains(&what) {
            self.warned.push(what);
            self.to_warn.push(what);
        }
    }
}

static STATE: Mutex<Option<State>> = Mutex::new(None);

/// Runs `f` on the interface's state; what it warns of is logged once the lock is released (a
/// log message may reach scripts, which may call the interface again).
fn with_state<R>(f: impl FnOnce(&mut State) -> R) -> R {
    let (result, warnings) = {
        let mut guard = STATE.lock().unwrap_or_else(|p| p.into_inner());
        let state = guard.get_or_insert_with(State::default);
        let result = f(state);
        for what in PENDING.with(|p| std::mem::take(&mut *p.borrow_mut())) {
            state.warn(what);
        }
        (result, std::mem::take(&mut state.to_warn))
    };
    for what in warnings {
        plugin_api::log(
            0,
            &format!("Unbound: the Vulkan plugin interface's {what} is not supported"),
        );
    }
    result
}

fn unsupported(what: &'static str) {
    with_state(|s| s.warn(what));
}

/// [`unsupported`] from code that runs under the state's lock (the scope's methods): logged
/// when the lock is released.
fn warn_locked(what: &'static str) {
    PENDING.with(|p| p.borrow_mut().push(what));
}

thread_local! {
    static PENDING: std::cell::RefCell<Vec<&'static str>> =
        const { std::cell::RefCell::new(Vec::new()) };
}

/// The device exists before any plugin loads, preloaded ones too: too late, the interface's own
/// answer for plugins loaded by scripts (nativegfx `plugin.intercept.late`).
unsafe extern "C" fn intercept_initialization(_: InitCallback, _: *mut c_void) -> bool {
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

/// The device's handles: set when the renderer opens the device, which is before any system
/// runs, so before scripts load or preload a plugin.
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
        let Some((commands, inside)) = scope.current() else {
            return false;
        };
        let pass = scope.pass.filter(|_| inside);
        *out = UnityVulkanRecordingState {
            command_buffer: commands,
            command_buffer_level: vk::CommandBufferLevel::SECONDARY,
            render_pass: pass.map_or(vk::RenderPass::null(), |p| p.render_pass),
            framebuffer: pass.map_or(vk::Framebuffer::null(), |p| p.framebuffer),
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

/// `AccessTexture` and its kin: the image a lookup finds in the callback's scope, after the
/// barrier into `layout` a `PipelineBarrier` access records, described as the Editor describes
/// it (the layout field left 0, the memory empty). False without a scope or the image.
fn access_image(
    find: impl FnOnce(&Scope) -> Option<ImageEntry>,
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
    // SAFETY: the plugin's subresource (or null for the whole image).
    let sub = unsafe { sub.as_ref() };
    with_state(|s| {
        let Some(scope) = s.scope.as_mut() else {
            return false;
        };
        let Some(entry) = find(scope) else {
            return false;
        };
        if !scope.accessed_textures.contains(&entry.texture) {
            scope.accessed_textures.push(entry.texture);
        }
        match mode {
            ACCESS_OBSERVE_ONLY => {}
            ACCESS_PIPELINE_BARRIER => {
                if !scope.barrier_image(entry, sub, layout, stages, access_flags) {
                    return false;
                }
            }
            _ => {
                s.warn("AccessTexture with kUnityVulkanResourceAccess_Recreate");
                return false;
            }
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
    access_image(
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
    access_image(
        |s| s.by_handle.get(&(id as u64)).map(|t| t.texture),
        sub,
        layout,
        stages,
        access_flags,
        mode,
        out,
    )
}

/// A render buffer's image: its depth buffer, else its color (multisampled where the texture
/// is), or that color's resolve texture (`resolve`; multisampled textures only).
fn render_buffer_image(scope: &Scope, pointer: *mut c_void, resolve: bool) -> Option<ImageEntry> {
    let (handle, depth) = render_buffer(pointer as u64);
    let images = scope.by_handle.get(&handle)?;
    match (depth, resolve) {
        (true, false) => images.depth,
        (true, true) => None,
        (false, false) => Some(images.msaa.unwrap_or(images.texture)),
        (false, true) => images.msaa.map(|_| images.texture),
    }
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
    access_image(
        |s| render_buffer_image(s, buffer, false),
        sub,
        layout,
        stages,
        access_flags,
        mode,
        out,
    )
}

unsafe extern "C" fn access_render_buffer_resolve_texture(
    buffer: *mut c_void,
    sub: *const vk::ImageSubresource,
    layout: vk::ImageLayout,
    stages: vk::PipelineStageFlags,
    access_flags: vk::AccessFlags,
    mode: i32,
    out: *mut UnityVulkanImage,
) -> bool {
    access_image(
        |s| render_buffer_image(s, buffer, true),
        sub,
        layout,
        stages,
        access_flags,
        mode,
        out,
    )
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
    let handle = buffer as u64;
    with_state(|s| {
        let Some(scope) = s.scope.as_mut() else {
            return false;
        };
        let Some(&(raw, size, usage)) = scope.buffers.get(&handle) else {
            return false;
        };
        match mode {
            ACCESS_OBSERVE_ONLY => {}
            ACCESS_PIPELINE_BARRIER => {
                let barrier = vk::MemoryBarrier::default()
                    .src_access_mask(vk::AccessFlags::MEMORY_READ | vk::AccessFlags::MEMORY_WRITE)
                    .dst_access_mask(access_flags);
                let recorded = scope.outside(|scope, commands| {
                    // SAFETY: a command buffer in the recording state outside a render pass.
                    unsafe {
                        scope.device.cmd_pipeline_barrier(
                            commands,
                            vk::PipelineStageFlags::ALL_COMMANDS,
                            stages | vk::PipelineStageFlags::TOP_OF_PIPE,
                            vk::DependencyFlags::empty(),
                            &[barrier],
                            &[],
                            &[],
                        )
                    }
                });
                if recorded.is_none() {
                    return false;
                }
            }
            _ => {
                s.warn("AccessBuffer with kUnityVulkanResourceAccess_Recreate");
                return false;
            }
        }
        if !scope.accessed_buffers.contains(&handle) {
            scope.accessed_buffers.push(handle);
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
    with_state(|s| {
        if let Some(scope) = s.scope.as_mut() {
            scope.enter(false);
        }
    });
}

unsafe extern "C" fn ensure_inside_render_pass() {
    with_state(|s| {
        if let Some(scope) = s.scope.as_mut()
            && !scope.enter(true)
        {
            s.warn("EnsureInsideRenderPass without a render texture target (a single color, not the screen)");
        }
    });
}

unsafe extern "C" fn access_queue(_: QueueCallback, _: i32, _: *mut c_void, _: bool) {
    unsupported("AccessQueue (the callback is not called)");
}

unsafe extern "C" fn configure_swapchain(_: *const c_void) -> bool {
    unsupported("ConfigureSwapchain");
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
    pipeline_cache: vk::PipelineCache,
    /// Native texture pointers: the image of each (handle, depth buffer) lives here.
    slots: HashMap<(u64, bool), Box<vk::Image>>,
    /// Plugin render passes, kept for the renderer's lifetime: a plugin may keep pipelines made
    /// for one, as Unity's own render passes stay.
    passes: HashMap<PassKey, vk::RenderPass>,
    /// Framebuffers over render texture targets, by the target's (color, depth) handles.
    framebuffers: HashMap<(u64, u64), CachedFramebuffer>,
    /// What callbacks submitted (and framebuffers replaced), freed once the GPU finished their
    /// frame.
    retired: Vec<Retired>,
}

/// What identifies a plugin render pass: its attachments' formats and samples.
#[derive(Clone, Copy, PartialEq, Eq, Hash)]
struct PassKey {
    color: vk::Format,
    samples: vk::SampleCountFlags,
    depth: Option<vk::Format>,
    resolve: bool,
}

/// A framebuffer over a target's views, which it holds (their handles cannot be reused while it
/// lives), and the last frame that used it.
struct CachedFramebuffer {
    framebuffer: vk::Framebuffer,
    raw: Vec<vk::ImageView>,
    extent: vk::Extent2D,
    _views: Vec<wgpu::TextureView>,
    last_used: u64,
}

/// Command buffers, framebuffers and buffers in use by the GPU until `frame` is finished.
struct Retired {
    frame: u64,
    commands: Vec<vk::CommandBuffer>,
    framebuffers: Vec<CachedFramebuffer>,
    _buffers: Vec<wgpu::Buffer>,
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
        let pipeline_cache =
            unsafe { raw.create_pipeline_cache(&vk::PipelineCacheCreateInfo::default(), None) }
                .unwrap_or_default();
        let entry = shared.entry();
        let instance = UnityVulkanInstance {
            pipeline_cache,
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
            pipeline_cache,
            slots: HashMap::new(),
            passes: HashMap::new(),
            framebuffers: HashMap::new(),
            retired: Vec::new(),
        })
    }

    /// A released texture's native pointers stop naming an image, and framebuffers over it go
    /// once the GPU is done with them.
    pub fn release(&mut self, handle: u64) {
        self.slots.remove(&(handle, false));
        self.slots.remove(&(handle, true));
        let released: Vec<(u64, u64)> = self
            .framebuffers
            .keys()
            .filter(|(color, depth)| *color == handle || *depth == handle)
            .copied()
            .collect();
        for key in released {
            if let Some(framebuffer) = self.framebuffers.remove(&key) {
                self.retire_framebuffer(framebuffer);
            }
        }
    }

    fn retire_framebuffer(&mut self, framebuffer: CachedFramebuffer) {
        self.retired.push(Retired {
            frame: framebuffer.last_used,
            commands: Vec::new(),
            framebuffers: vec![framebuffer],
            _buffers: Vec::new(),
        });
    }

    /// The render pass and framebuffer over the target whose textures are `handles` (made once
    /// for each; a framebuffer is made again when the target's views change). None when Vulkan
    /// refuses them.
    fn plugin_pass(
        &mut self,
        target: &PassTarget,
        handles: (u64, u64),
        frame: u64,
    ) -> Option<PluginPass> {
        let key = PassKey {
            color: target.color_format,
            samples: target.samples,
            depth: target.depth.map(|(_, format)| format),
            resolve: target.resolve.is_some(),
        };
        let render_pass = match self.passes.get(&key) {
            Some(&pass) => pass,
            None => {
                // SAFETY: a valid description on the renderer's device.
                let pass = unsafe { create_render_pass(&self.device, key) }?;
                self.passes.insert(key, pass);
                pass
            }
        };
        let raw = target.raw_views();
        let current = self
            .framebuffers
            .get(&handles)
            .is_some_and(|f| f.raw == raw && f.extent == target.extent);
        if !current {
            if let Some(stale) = self.framebuffers.remove(&handles) {
                self.retire_framebuffer(stale);
            }
            // SAFETY: the views live while the cache entry holds them.
            let framebuffer = unsafe {
                self.device.create_framebuffer(
                    &vk::FramebufferCreateInfo::default()
                        .render_pass(render_pass)
                        .attachments(&raw)
                        .width(target.extent.width)
                        .height(target.extent.height)
                        .layers(1),
                    None,
                )
            }
            .ok()?;
            self.framebuffers.insert(
                handles,
                CachedFramebuffer {
                    framebuffer,
                    raw,
                    extent: target.extent,
                    _views: target.views.clone(),
                    last_used: frame,
                },
            );
        }
        let cached = self.framebuffers.get_mut(&handles)?;
        cached.last_used = frame;
        Some(PluginPass {
            render_pass,
            framebuffer: cached.framebuffer,
            extent: target.extent,
        })
    }

    /// Frees what callbacks of frames the GPU finished used.
    pub fn free_retired(&mut self, completed: u64) {
        let device = &self.device;
        let pool = self.pool;
        self.retired.retain(|retired| {
            if retired.frame > completed {
                return true;
            }
            // SAFETY: the GPU finished the frame that last used them.
            unsafe {
                if !retired.commands.is_empty() {
                    device.free_command_buffers(pool, &retired.commands);
                }
                for framebuffer in &retired.framebuffers {
                    device.destroy_framebuffer(framebuffer.framebuffer, None);
                }
            }
            false
        });
    }

    fn retire(&mut self, frame: u64, finished: &Finished, buffers: Vec<wgpu::Buffer>) {
        self.retired.push(Retired {
            frame,
            commands: finished.segments.iter().map(|&(c, _)| c).collect(),
            framebuffers: Vec::new(),
            _buffers: buffers,
        });
    }
}

impl Drop for VulkanPlugins {
    fn drop(&mut self) {
        with_state(|s| s.instance = UnityVulkanInstance::default());
        // SAFETY: once the device is idle nothing uses them; command buffers go with the pool.
        unsafe {
            let _ = self.device.device_wait_idle();
            for retired in self.retired.drain(..) {
                for framebuffer in retired.framebuffers {
                    self.device
                        .destroy_framebuffer(framebuffer.framebuffer, None);
                }
            }
            for (_, framebuffer) in self.framebuffers.drain() {
                self.device
                    .destroy_framebuffer(framebuffer.framebuffer, None);
            }
            for (_, pass) in self.passes.drain() {
                self.device.destroy_render_pass(pass, None);
            }
            self.device.destroy_command_pool(self.pool, None);
            self.device
                .destroy_pipeline_cache(self.pipeline_cache, None);
        }
    }
}

/// The state a texture rests in during a callback, and the Vulkan layout wgpu-hal gives it.
fn resting(texture: &wgpu::Texture) -> (wgpu::TextureUses, vk::ImageLayout) {
    let format = texture.format();
    let usage = texture.usage();
    let uses = if usage.contains(wgpu::TextureUsages::RENDER_ATTACHMENT) {
        if format.is_depth_stencil_format() {
            wgpu::TextureUses::DEPTH_STENCIL_WRITE
        } else {
            wgpu::TextureUses::COLOR_TARGET
        }
    } else if usage.contains(wgpu::TextureUsages::TEXTURE_BINDING) {
        wgpu::TextureUses::RESOURCE
    } else {
        wgpu::TextureUses::COPY_DST
    };
    (
        uses,
        wgpu::hal::vulkan::conv::derive_image_layout(uses, format),
    )
}

/// What a plugin learns of a wgpu texture (its raw image and description; `format` is the one
/// the renderer uses it in, sRGB where it is viewed so), and the texture among the callback's
/// `candidates` (put in its resting layout if the plugin accesses it).
fn image_entry(
    adapter: &wgpu::Adapter,
    texture: &wgpu::Texture,
    format: wgpu::TextureFormat,
    candidates: &mut Vec<(wgpu::Texture, wgpu::TextureUses)>,
) -> Option<ImageEntry> {
    let depth = texture.format().is_depth_stencil_format();
    // SAFETY: the raw handle is only read, while the texture lives (the candidates hold it).
    let image = unsafe { texture.as_hal::<Vulkan>().map(|raw| raw.raw_handle()) }?;
    let (uses, resting_layout) = resting(texture);
    candidates.push((texture.clone(), uses));
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
    let aspect = if !depth {
        vk::ImageAspectFlags::COLOR
    } else if texture.format().has_stencil_aspect() {
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
        format: vk_format(adapter, format).unwrap_or(vk::Format::UNDEFINED),
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
        texture: candidates.len() - 1,
    })
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
        let target = self.plugin_render_pass_target();
        // The frame being recorded (the renderer counts a frame when its rendering begins).
        let frame = self.frame_number + 1;
        let handles = self.target_handles;
        let Some(plugins) = self.vulkan_plugins.as_mut() else {
            invoke();
            return;
        };
        let completed = self
            .frames_completed
            .load(std::sync::atomic::Ordering::Acquire);
        plugins.free_retired(completed);
        let pass = target
            .as_ref()
            .and_then(|t| plugins.plugin_pass(t, handles, frame));
        // Inside a render pass when the event asks for one; a DontCare event stays where the
        // stream is: inside the current target's pass while it draws (a camera's events),
        // outside elsewhere (nativegfx `plugin.dontcare.*`).
        let inside = match config.render_pass_precondition {
            ENSURE_INSIDE => {
                if pass.is_none() {
                    unsupported(
                        "kUnityVulkanRenderPass_EnsureInside events without a render texture target (a single color, not the screen): they are skipped",
                    );
                    return;
                }
                true
            }
            DONT_CARE => {
                if self.plugin_pass_open && pass.is_none() {
                    unsupported(
                        "a render pass for DontCare events while the screen or several colors are drawn (they run outside one)",
                    );
                }
                self.plugin_pass_open && pass.is_some()
            }
            _ => false,
        };
        // What the plugin can reach: textures it has pointers of, render textures (render
        // buffer pointers, texture IDs). Those it accesses are put in resting layouts first.
        let color_space = self.color_space;
        let mut candidates: Vec<(wgpu::Texture, wgpu::TextureUses)> = Vec::new();
        let mut by_pointer = HashMap::new();
        let mut by_handle = HashMap::new();
        for (&handle, gpu) in &self.textures {
            let pointed = plugins.slots.contains_key(&(handle, false))
                || plugins.slots.contains_key(&(handle, true));
            if !pointed && !gpu.render_target {
                continue;
            }
            let format = gpu.target_format(color_space);
            let Some(texture) = image_entry(&self.adapter, &gpu.texture, format, &mut candidates)
            else {
                continue;
            };
            let msaa = gpu.msaa.as_ref().and_then(|(view, _)| {
                image_entry(&self.adapter, view.texture(), format, &mut candidates)
            });
            let depth = gpu
                .depth_texture
                .as_ref()
                .and_then(|d| image_entry(&self.adapter, d, d.format(), &mut candidates));
            for (is_depth, entry) in [(false, Some(texture)), (true, depth)] {
                if let (Some(entry), Some(slot)) =
                    (entry, plugins.slots.get_mut(&(handle, is_depth)))
                {
                    **slot = entry.image;
                    by_pointer.insert(&**slot as *const vk::Image as usize, entry);
                }
            }
            by_handle.insert(
                handle,
                TextureImages {
                    texture,
                    msaa,
                    depth,
                },
            );
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
        let mut scope = Scope {
            device: plugins.device.clone(),
            pool: plugins.pool,
            pass,
            attachments: target
                .as_ref()
                .map(|t| t.images.clone())
                .unwrap_or_default(),
            segments: Vec::new(),
            frame,
            safe: completed,
            by_pointer,
            by_handle,
            buffers,
            layouts: HashMap::new(),
            accessed_buffers: Vec::new(),
            accessed_textures: Vec::new(),
        };
        if !scope.enter(inside) {
            tracing::error!("Unbound: no command buffer for a Vulkan plugin callback");
            return;
        }
        with_state(|s| s.scope = Some(scope));
        invoke();
        let Some(scope) = with_state(|s| s.scope.take()) else {
            return;
        };
        let finished = scope.finish();
        // The resting layouts (wgpu's encoding), then the plugin's commands (raw encoding: an
        // encoder cannot mix the two), submitted together in that order.
        let mut transitions: Vec<(wgpu::Texture, wgpu::TextureUses)> = finished
            .accessed_textures
            .iter()
            .map(|&i| candidates[i].clone())
            .collect();
        if finished.segments.iter().any(|&(_, inside)| inside)
            && let Some(target) = &target
        {
            transitions.extend(target.textures.iter().map(|t| (t.clone(), resting(t).0)));
        }
        let mut commands = Vec::with_capacity(2);
        if !transitions.is_empty() {
            let mut transition =
                self.device
                    .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                        label: Some("unbound vulkan plugin layouts"),
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
            commands.push(transition.finish());
        }
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor {
                label: Some("unbound vulkan plugin"),
            });
        let device = plugins.device.clone();
        // SAFETY: the raw primary command buffer is recorded only inside this closure, in the
        // encoder's recording state; the command buffers and pass it executes outlive the
        // submission (retired below, the pass cached).
        unsafe {
            encoder.as_hal_mut::<Vulkan, _, _>(|hal| {
                if let Some(hal) = hal {
                    execute(&device, hal.raw_handle(), &finished);
                }
            })
        };
        commands.push(encoder.finish());
        self.queue.submit(commands);
        let kept = finished
            .accessed_buffers
            .iter()
            .filter_map(|handle| self.buffers.get(handle).map(|b| b.buffer.clone()))
            .collect();
        if let Some(plugins) = self.vulkan_plugins.as_mut() {
            plugins.retire(frame, &finished, kept);
        }
        // An event that ran inside a pass leaves it open for the next event; one that ran
        // outside it (configured so) leaves it ended: a DontCare event after it runs outside
        // (nativegfx `plugin.dontcare.camera.2`).
        self.plugin_pass_open = inside;
    }

    /// The current target's color (multisampled where it is) and its depth as a plugin render
    /// pass's attachments; None for a target Unbound cannot begin one on (several colors, the
    /// screen, a slice of a 3D texture, a format with no Vulkan equivalent).
    fn plugin_render_pass_target(&self) -> Option<PassTarget> {
        let target = self.state.target.as_ref()?;
        if !target.extra.is_empty() || target.screen || target.depth_slice.is_some() {
            return None;
        }
        // SAFETY: raw handles only read, while the target's views live (the renderer's).
        let color = unsafe { target.color.as_hal::<Vulkan>().map(|v| v.raw_handle()) }?;
        let color_format = vk_format(&self.adapter, target.format)?;
        // A multisampled color resolves into the target's texture at the pass's end, as the
        // renderer's own passes do.
        let resolve = match target.resolve.as_ref().filter(|_| target.samples > 1) {
            Some(r) => Some((unsafe { r.as_hal::<Vulkan>().map(|v| v.raw_handle()) }?, r)),
            None => None,
        };
        // The depth attachment where its samples are the color's.
        let depth = match target
            .depth
            .as_ref()
            .filter(|d| d.texture().sample_count() == target.samples)
        {
            Some(d) => Some((
                unsafe { d.as_hal::<Vulkan>().map(|v| v.raw_handle()) }?,
                vk_format(&self.adapter, d.texture().format())?,
                d,
            )),
            None => None,
        };
        let views: Vec<&wgpu::TextureView> = std::iter::once(&target.color)
            .chain(depth.map(|(_, _, d)| d))
            .chain(resolve.map(|(_, r)| r))
            .collect();
        Some(PassTarget {
            color,
            samples: vk::SampleCountFlags::from_raw(target.samples),
            color_format,
            depth: depth.map(|(raw, format, _)| (raw, format)),
            resolve: resolve.map(|(raw, _)| raw),
            textures: views.iter().map(|v| v.texture().clone()).collect(),
            // SAFETY: raw handles only read.
            images: views
                .iter()
                .filter_map(|v| unsafe { v.texture().as_hal::<Vulkan>().map(|r| r.raw_handle()) })
                .collect(),
            views: views.into_iter().cloned().collect(),
            extent: vk::Extent2D {
                width: target.width,
                height: target.height,
            },
        })
    }
}

/// Records a callback's command buffers into the renderer's: the plugin pass begun around those
/// recorded inside it, every other one ordered after all earlier work, and all later work after
/// the last.
unsafe fn execute(device: &ash::Device, primary: vk::CommandBuffer, finished: &Finished) {
    let mut open = false;
    for &(commands, inside) in &finished.segments {
        unsafe {
            match (inside, open, finished.pass) {
                (true, false, Some(pass)) => {
                    begin_render_pass(device, primary, pass);
                    open = true;
                }
                (false, true, _) => {
                    device.cmd_end_render_pass(primary);
                    open = false;
                    full_barrier(device, primary);
                }
                (false, false, _) => full_barrier(device, primary),
                _ => {}
            }
            device.cmd_execute_commands(primary, &[commands]);
        }
    }
    unsafe {
        if open {
            device.cmd_end_render_pass(primary);
        }
        full_barrier(device, primary);
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
    /// The attachments' textures (put in their resting layouts first), images, and views (held
    /// by the framebuffer made over them).
    textures: Vec<wgpu::Texture>,
    images: Vec<vk::Image>,
    views: Vec<wgpu::TextureView>,
}

impl PassTarget {
    /// The framebuffer's attachments: color, depth, resolve.
    fn raw_views(&self) -> Vec<vk::ImageView> {
        std::iter::once(self.color)
            .chain(self.depth.map(|(view, _)| view))
            .chain(self.resolve)
            .collect()
    }
}

/// A render pass over a target as `key` describes it (attachments loaded and stored, in their
/// resting layouts) whose contents are secondary command buffers.
unsafe fn create_render_pass(device: &ash::Device, key: PassKey) -> Option<vk::RenderPass> {
    let mut attachments = vec![
        vk::AttachmentDescription::default()
            .format(key.color)
            .samples(key.samples)
            .load_op(vk::AttachmentLoadOp::LOAD)
            .store_op(vk::AttachmentStoreOp::STORE)
            .stencil_load_op(vk::AttachmentLoadOp::DONT_CARE)
            .stencil_store_op(vk::AttachmentStoreOp::DONT_CARE)
            .initial_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL)
            .final_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL),
    ];
    if let Some(format) = key.depth {
        attachments.push(
            vk::AttachmentDescription::default()
                .format(format)
                .samples(key.samples)
                .load_op(vk::AttachmentLoadOp::LOAD)
                .store_op(vk::AttachmentStoreOp::STORE)
                .stencil_load_op(vk::AttachmentLoadOp::LOAD)
                .stencil_store_op(vk::AttachmentStoreOp::STORE)
                .initial_layout(vk::ImageLayout::DEPTH_STENCIL_ATTACHMENT_OPTIMAL)
                .final_layout(vk::ImageLayout::DEPTH_STENCIL_ATTACHMENT_OPTIMAL),
        );
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
        attachment: attachments.len() as u32,
        layout: vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL,
    }];
    if key.resolve {
        attachments.push(
            vk::AttachmentDescription::default()
                .format(key.color)
                .samples(vk::SampleCountFlags::TYPE_1)
                .load_op(vk::AttachmentLoadOp::DONT_CARE)
                .store_op(vk::AttachmentStoreOp::STORE)
                .stencil_load_op(vk::AttachmentLoadOp::DONT_CARE)
                .stencil_store_op(vk::AttachmentStoreOp::DONT_CARE)
                .initial_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL)
                .final_layout(vk::ImageLayout::COLOR_ATTACHMENT_OPTIMAL),
        );
    }
    let mut subpass = vk::SubpassDescription::default()
        .pipeline_bind_point(vk::PipelineBindPoint::GRAPHICS)
        .color_attachments(&color_ref);
    if key.resolve {
        subpass = subpass.resolve_attachments(&resolve_ref);
    }
    if key.depth.is_some() {
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
    unsafe {
        device.create_render_pass(
            &vk::RenderPassCreateInfo::default()
                .attachments(&attachments)
                .subpasses(&subpasses)
                .dependencies(&dependencies),
            None,
        )
    }
    .ok()
}

unsafe fn begin_render_pass(device: &ash::Device, primary: vk::CommandBuffer, pass: PluginPass) {
    unsafe {
        device.cmd_begin_render_pass(
            primary,
            &vk::RenderPassBeginInfo::default()
                .render_pass(pass.render_pass)
                .framebuffer(pass.framebuffer)
                .render_area(vk::Rect2D {
                    offset: vk::Offset2D::default(),
                    extent: pass.extent,
                }),
            vk::SubpassContents::SECONDARY_COMMAND_BUFFERS,
        )
    };
}

/// A secondary command buffer begun for recording, inside `pass` (with the viewport and scissor
/// set to its target, as Unity sets them) or outside any.
unsafe fn begin_secondary(
    device: &ash::Device,
    pool: vk::CommandPool,
    pass: Option<PluginPass>,
) -> Option<vk::CommandBuffer> {
    let commands = unsafe {
        device.allocate_command_buffers(
            &vk::CommandBufferAllocateInfo::default()
                .command_pool(pool)
                .level(vk::CommandBufferLevel::SECONDARY)
                .command_buffer_count(1),
        )
    }
    .ok()?[0];
    let inheritance = vk::CommandBufferInheritanceInfo::default()
        .render_pass(pass.map_or(vk::RenderPass::null(), |p| p.render_pass))
        .subpass(0)
        .framebuffer(pass.map_or(vk::Framebuffer::null(), |p| p.framebuffer));
    let flags = if pass.is_some() {
        vk::CommandBufferUsageFlags::ONE_TIME_SUBMIT
            | vk::CommandBufferUsageFlags::RENDER_PASS_CONTINUE
    } else {
        vk::CommandBufferUsageFlags::ONE_TIME_SUBMIT
    };
    let begun = unsafe {
        device.begin_command_buffer(
            commands,
            &vk::CommandBufferBeginInfo::default()
                .flags(flags)
                .inheritance_info(&inheritance),
        )
    };
    if begun.is_err() {
        unsafe { device.free_command_buffers(pool, &[commands]) };
        return None;
    }
    if let Some(pass) = pass {
        unsafe {
            device.cmd_set_viewport(
                commands,
                0,
                &[vk::Viewport {
                    x: 0.0,
                    y: 0.0,
                    width: pass.extent.width as f32,
                    height: pass.extent.height as f32,
                    min_depth: 0.0,
                    max_depth: 1.0,
                }],
            );
            device.cmd_set_scissor(
                commands,
                0,
                &[vk::Rect2D {
                    offset: vk::Offset2D::default(),
                    extent: pass.extent,
                }],
            );
        }
    }
    Some(commands)
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
