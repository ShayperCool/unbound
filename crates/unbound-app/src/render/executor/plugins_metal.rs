//! The Metal device's plugin interface (`IUnityGraphicsMetal`, `IUnityGraphicsMetalV1`,
//! `IUnityGraphicsMetalV2`): the renderer's own device and command queue, and in rendering
//! callbacks a command buffer of that queue, a render command encoder over the current target and
//! its render pass descriptor, as the Unity 6000.0.71f1 macOS Editor gives them
//! (tests/projects/nativegfx `Reference/unity-6000.0.71f1/metal.jsonl`).
//!
//! A callback runs where it is in the stream: the passes before it have been submitted to the
//! queue; the plugin's command buffer is committed after the callback, so it runs between the
//! work before and the work after (Metal runs one queue's command buffers in order). A texture
//! upload made just before the event (`Texture2D.Apply` in the same frame, with no pass between)
//! runs after the plugin's buffer, as the Editor's does (nativegfx `target.pixels`: the plugin
//! copies the texture's earlier texels). Inside it
//! the current encoder is begun when first asked, over the current render texture target's
//! attachments at the image being drawn (their contents loaded and stored, a multisampled color
//! resolved into its texture again); `EndCurrentCommandEncoder` ends it, and the next request
//! begins another over the same target. Outside callbacks (a plugin's own calls from scripts)
//! the current command buffer is one of the queue's, committed by `CommitCurrentCommandBuffer`,
//! else before the next callback or at the end of the frame, so it runs after the rendering
//! submitted by then (a script's commit: before what the frame has not submitted yet; neither is
//! measured). There is no encoder there (logged once). A
//! texture's native pointer is its `MTLTexture`
//! (in the format Unity's device stores it: sRGB where a Linear project samples it as sRGB);
//! a multisampled render texture's color and depth pointers are its samples. Not followed (the
//! interface leaves them to the device; measured in batchmode only): outside a render texture
//! target the Editor's current encoder is a render encoder over the screen, and after
//! `EndCurrentCommandEncoder` its next one is too; the screen is not shared here (the encoder
//! and descriptor are nil, logged once when asked for).
use super::UnityRenderer;
use crate::render::resources::GpuTexture;
use objc2::Message;
use objc2::rc::Retained;
use objc2::runtime::ProtocolObject;
use objc2_foundation::{NSBundle, NSRange, NSString};
use objc2_metal::{
    MTLCommandBuffer, MTLCommandEncoder, MTLCommandQueue, MTLDevice, MTLLoadAction, MTLPixelFormat,
    MTLRenderCommandEncoder, MTLRenderPassDescriptor, MTLStoreAction, MTLTexture, MTLTextureType,
};
use rustc_hash::FxHashMap as HashMap;
use std::cell::RefCell;
use std::ffi::c_void;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use unbound_runtime::plugin_api;
use wgpu::hal::api::Metal;

type Object = *mut c_void;
type Texture = ProtocolObject<dyn MTLTexture>;
type CommandBuffer = ProtocolObject<dyn MTLCommandBuffer>;

/// `IUnityGraphicsMetalV1`; `IUnityGraphicsMetal` has the same members.
#[repr(C)]
struct InterfaceV1 {
    metal_bundle: extern "C" fn() -> Object,
    metal_device: extern "C" fn() -> Object,
    current_command_buffer: extern "C" fn() -> Object,
    current_command_encoder: extern "C" fn() -> Object,
    end_current_command_encoder: extern "C" fn(),
    current_render_pass_descriptor: extern "C" fn() -> Object,
    render_buffer_from_handle: extern "C" fn(Object) -> Object,
    texture_from_render_buffer: extern "C" fn(Object) -> Object,
    aa_resolved_texture_from_render_buffer: extern "C" fn(Object) -> Object,
    stencil_texture_from_render_buffer: extern "C" fn(Object) -> Object,
}

/// `IUnityGraphicsMetalV2`: V1's members after two of its own.
#[repr(C)]
struct InterfaceV2 {
    commit_current_command_buffer: extern "C" fn() -> Object,
    command_queue: extern "C" fn() -> Object,
    v1: InterfaceV1,
}

const V1_MEMBERS: InterfaceV1 = InterfaceV1 {
    metal_bundle,
    metal_device,
    current_command_buffer,
    current_command_encoder,
    end_current_command_encoder,
    current_render_pass_descriptor,
    render_buffer_from_handle,
    texture_from_render_buffer,
    aa_resolved_texture_from_render_buffer,
    stencil_texture_from_render_buffer,
};

// Three tables, as the Editor's are three (nativegfx `plugin.load`: V1 is not the plain one).
static PLAIN: InterfaceV1 = V1_MEMBERS;
static V1: InterfaceV1 = V1_MEMBERS;
static V2: InterfaceV2 = InterfaceV2 {
    commit_current_command_buffer,
    command_queue,
    v1: V1_MEMBERS,
};

/// Offers the interfaces before any plugin loads; the device and queue are there once the
/// renderer is up ([`MetalPlugins::new`]), before any system runs.
pub fn provide_metal_plugin_interfaces() {
    use plugin_api::guid;
    plugin_api::provide(guid::GRAPHICS_METAL, &PLAIN as *const _ as usize);
    plugin_api::provide(guid::GRAPHICS_METAL_V1, &V1 as *const _ as usize);
    plugin_api::provide(guid::GRAPHICS_METAL_V2, &V2 as *const _ as usize);
}

/// The renderer's device and queue (retained by [`MetalPlugins`] for the renderer's lifetime).
static DEVICE: AtomicUsize = AtomicUsize::new(0);
static QUEUE: AtomicUsize = AtomicUsize::new(0);
/// Metal.framework's bundle, which the Editor answers for `MetalBundle` (retained once).
static BUNDLE: AtomicUsize = AtomicUsize::new(0);
/// The command buffer plugins get outside callbacks, retained (0: none yet).
static OUTSIDE: Mutex<usize> = Mutex::new(0);
/// The buffer `CommitCurrentCommandBuffer` last committed outside callbacks, retained so the
/// plugin can wait on it (0: none).
static COMMITTED_OUTSIDE: Mutex<usize> = Mutex::new(0);
/// What plugins were told once: no encoder outside callbacks, none over the screen, the first
/// of several colors only, a depth buffer left out, no command buffer, renderer busy.
static WARNED_OUTSIDE: AtomicBool = AtomicBool::new(false);
static WARNED_SCREEN: AtomicBool = AtomicBool::new(false);
static WARNED_COLORS: AtomicBool = AtomicBool::new(false);
static WARNED_DEPTH: AtomicBool = AtomicBool::new(false);
static WARNED_BUFFER: AtomicBool = AtomicBool::new(false);
static WARNED_BUSY: AtomicBool = AtomicBool::new(false);

thread_local! {
    /// The renderer runs a plugin's call on this thread without a scope (a texture update's
    /// events): render buffer lookups cannot ask it.
    static BUSY: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// Runs `f` (a texture update's plugin events) marked as running inside the renderer.
pub(super) fn while_renderer_busy<R>(f: impl FnOnce() -> R) -> R {
    BUSY.with(|b| b.set(true));
    let result = f();
    BUSY.with(|b| b.set(false));
    result
}

/// Logs once, inside a callback over the screen, that there is no pass.
fn warn_screen() {
    if !WARNED_SCREEN.swap(true, Ordering::Relaxed) {
        tracing::warn!(
            "a native plugin callback while the screen is the target asked for its encoder or \
             render pass: the screen is not shared with plugins (nil)"
        );
    }
}

/// Logs once that a plugin asked for the encoder or its descriptor outside a callback.
fn warn_outside(what: &str) {
    if !WARNED_OUTSIDE.swap(true, Ordering::Relaxed) {
        tracing::warn!(
            "a native plugin asked for the {what} outside a rendering callback: Unbound has none there (nil; the Editor's draws to its screen, which is not shared with plugins)"
        );
    }
}

/// What a callback sees: its command buffer, the encoder begun in it, the descriptor encoders
/// begin from, and the render textures' images by handle (render buffer pointers).
struct Scope {
    buffer: Retained<CommandBuffer>,
    encoder: Option<Retained<ProtocolObject<dyn MTLRenderCommandEncoder>>>,
    pass: Option<Retained<MTLRenderPassDescriptor>>,
    /// The screen is the target (it has no pass: not shared with plugins).
    screen: bool,
    images: HashMap<u64, Images>,
    /// Buffers committed by `CommitCurrentCommandBuffer` in this callback, kept until it ends
    /// (the plugin may wait on the one returned).
    committed: Vec<Retained<CommandBuffer>>,
    /// Whether an encoder was begun over a multisampled color.
    drew_samples: bool,
    multisampled: bool,
}

/// A render texture's images, as render buffer pointers name them (0: none).
#[derive(Clone, Copy)]
struct Images {
    /// Its color (a multisampled target's samples), the texture its native pointer names.
    color: usize,
    /// A multisampled target's resolved texture.
    resolved: usize,
    /// Its depth buffer.
    depth: usize,
}

thread_local! {
    static SCOPE: RefCell<Option<Scope>> = const { RefCell::new(None) };
}

fn with_scope<R>(f: impl FnOnce(&mut Scope) -> R) -> Option<R> {
    SCOPE.with(|scope| scope.borrow_mut().as_mut().map(f))
}

fn object<T: ?Sized + Message>(retained: &Retained<T>) -> Object {
    Retained::as_ptr(retained) as *mut c_void
}

extern "C" fn metal_bundle() -> Object {
    let bundle = BUNDLE.load(Ordering::Acquire);
    if bundle != 0 {
        return bundle as Object;
    }
    let Some(found) = NSBundle::bundleWithIdentifier(&NSString::from_str("com.apple.Metal")) else {
        return std::ptr::null_mut();
    };
    let raw = Retained::into_raw(found) as usize;
    match BUNDLE.compare_exchange(0, raw, Ordering::AcqRel, Ordering::Acquire) {
        Ok(_) => raw as Object,
        Err(other) => {
            // SAFETY: `raw` was made by `into_raw` above and is released once.
            drop(unsafe { Retained::from_raw(raw as *mut NSBundle) });
            other as Object
        }
    }
}

extern "C" fn metal_device() -> Object {
    DEVICE.load(Ordering::Acquire) as Object
}

extern "C" fn command_queue() -> Object {
    QUEUE.load(Ordering::Acquire) as Object
}

fn queue() -> Option<&'static ProtocolObject<dyn MTLCommandQueue>> {
    let queue = QUEUE.load(Ordering::Acquire) as *const ProtocolObject<dyn MTLCommandQueue>;
    // SAFETY: the queue is retained by the renderer's MetalPlugins for the process's lifetime.
    unsafe { queue.as_ref() }
}

extern "C" fn current_command_buffer() -> Object {
    if let Some(buffer) = with_scope(|s| object(&s.buffer)) {
        return buffer;
    }
    let mut outside = OUTSIDE.lock().unwrap_or_else(|e| e.into_inner());
    if *outside == 0
        && let Some(buffer) = queue().and_then(|q| q.commandBuffer())
    {
        *outside = Retained::into_raw(buffer) as usize;
    }
    *outside as Object
}

/// Commits the command buffer plugins were given outside callbacks, if they asked for one.
pub(super) fn commit_outside() {
    let raw = std::mem::take(&mut *OUTSIDE.lock().unwrap_or_else(|e| e.into_inner()));
    // SAFETY: made by `into_raw` in `current_command_buffer`, taken once.
    if let Some(buffer) = unsafe { Retained::from_raw(raw as *mut CommandBuffer) } {
        buffer.commit();
    }
}

extern "C" fn current_command_encoder() -> Object {
    with_scope(|s| {
        if s.encoder.is_none()
            && let Some(pass) = &s.pass
        {
            s.encoder = s.buffer.renderCommandEncoderWithDescriptor(pass);
            s.drew_samples |= s.multisampled && s.encoder.is_some();
        }
        if s.pass.is_none() && s.screen {
            warn_screen();
        }
        s.encoder.as_ref().map_or(std::ptr::null_mut(), object)
    })
    .unwrap_or_else(|| {
        warn_outside("current command encoder");
        std::ptr::null_mut()
    })
}

extern "C" fn end_current_command_encoder() {
    with_scope(|s| {
        if let Some(encoder) = s.encoder.take() {
            encoder.endEncoding();
        }
    });
}

extern "C" fn current_render_pass_descriptor() -> Object {
    with_scope(|s| {
        if s.pass.is_none() && s.screen {
            warn_screen();
        }
        s.pass.as_ref().map_or(std::ptr::null_mut(), object)
    })
    .unwrap_or_else(|| {
        warn_outside("current render pass descriptor");
        std::ptr::null_mut()
    })
}

/// Commits the current command buffer and goes on in a new one; the committed one is returned
/// (nativegfx `plugin.commit.*`: status Committed, a new current buffer; `plugin.script.commit`
/// outside callbacks).
extern "C" fn commit_current_command_buffer() -> Object {
    let inside = with_scope(|s| {
        if let Some(encoder) = s.encoder.take() {
            encoder.endEncoding();
        }
        let next = queue().and_then(|q| q.commandBuffer())?;
        let committed = std::mem::replace(&mut s.buffer, next);
        committed.commit();
        let pointer = object(&committed);
        s.committed.push(committed);
        Some(pointer)
    });
    match inside {
        Some(committed) => committed.unwrap_or(std::ptr::null_mut()),
        None => commit_outside_now(),
    }
}

/// `CommitCurrentCommandBuffer` outside callbacks: the buffer plugins were given (a new one if
/// none was), committed and kept until the next such commit.
fn commit_outside_now() -> Object {
    let raw = std::mem::take(&mut *OUTSIDE.lock().unwrap_or_else(|e| e.into_inner()));
    // SAFETY: made by `into_raw` in `current_command_buffer`, taken once.
    let buffer = match unsafe { Retained::from_raw(raw as *mut CommandBuffer) } {
        Some(buffer) => buffer,
        None => match queue().and_then(|q| q.commandBuffer()) {
            Some(buffer) => buffer,
            None => return std::ptr::null_mut(),
        },
    };
    buffer.commit();
    let pointer = object(&buffer);
    let previous = std::mem::replace(
        &mut *COMMITTED_OUTSIDE.lock().unwrap_or_else(|e| e.into_inner()),
        Retained::into_raw(buffer) as usize,
    );
    // SAFETY: the previous kept buffer, made by `into_raw` above.
    drop(unsafe { Retained::from_raw(previous as *mut CommandBuffer) });
    pointer
}

/// The Editor's render buffer pointers are its native render buffers (nativegfx
/// `plugin.renderbuffer.*`: `handle_is_buffer=1`).
extern "C" fn render_buffer_from_handle(handle: Object) -> Object {
    handle
}

/// A render buffer's image: inside a callback from its scope (the renderer is busy running
/// it), elsewhere from the renderer.
fn render_buffer_image(buffer: Object, resolved: bool) -> Object {
    let (handle, depth) = super::plugins::render_buffer(buffer as u64);
    let which = match (depth, resolved) {
        (true, true) => return std::ptr::null_mut(),
        (true, false) => Which::Depth,
        (false, false) => Which::Color,
        (false, true) => Which::Resolved,
    };
    if let Some(image) = with_scope(|s| s.images.get(&handle).map_or(0, |i| i.pick(which))) {
        return image as Object;
    }
    if BUSY.with(|b| b.get()) {
        if !WARNED_BUSY.swap(true, Ordering::Relaxed) {
            tracing::warn!(
                "a native plugin asked for a render buffer's texture in a texture update's \
                 event: Unbound answers nil there"
            );
        }
        return std::ptr::null_mut();
    }
    unbound_runtime::state::backend().render_buffer_texture(handle, which as u8) as Object
}

extern "C" fn texture_from_render_buffer(buffer: Object) -> Object {
    render_buffer_image(buffer, false)
}

/// Nil for a depth buffer and a single-sample color.
extern "C" fn aa_resolved_texture_from_render_buffer(buffer: Object) -> Object {
    render_buffer_image(buffer, true)
}

/// Nil for every buffer: the device's depth-stencil formats hold stencil in the depth texture
/// (nativegfx `plugin.renderbuffer.*`: `stencil=none` for D32_SFloat_S8_UInt depth buffers).
extern "C" fn stencil_texture_from_render_buffer(_: Object) -> Object {
    std::ptr::null_mut()
}

/// Which image of a texture a native pointer names (`RenderBackend::render_buffer_texture`'s
/// numbers).
#[derive(Clone, Copy, PartialEq, Eq, Hash)]
enum Which {
    Color = 0,
    Depth = 1,
    Resolved = 2,
}

impl Images {
    fn pick(self, which: Which) -> usize {
        match which {
            Which::Color => self.color,
            Which::Depth => self.depth,
            Which::Resolved => self.resolved,
        }
    }
}

/// A Metal object the renderer holds across threads. Metal's devices, queues and textures may be
/// used from any thread (wgpu's Metal backend holds them the same way).
struct Shared<T: ?Sized>(Retained<T>);

// SAFETY: see above; nothing here mutates them.
unsafe impl<T: ?Sized> Send for Shared<T> {}
unsafe impl<T: ?Sized> Sync for Shared<T> {}

/// The renderer's side: the device and queue plugins get, and the `MTLTexture` each texture's
/// pointers name (kept while the texture's storage is the same).
pub(super) struct MetalPlugins {
    _device: Shared<ProtocolObject<dyn MTLDevice>>,
    _queue: Shared<ProtocolObject<dyn MTLCommandQueue>>,
    natives: HashMap<(u64, Which), (wgpu::Texture, Shared<Texture>)>,
    /// The render textures' handles at a callback (reused).
    targets: Vec<u64>,
    /// The render textures' images a callback's scope holds (reused, lent to the scope).
    images: HashMap<u64, Images>,
    /// The last render pass made, by its images, level and slice.
    pass: Option<([usize; 5], Shared<MTLRenderPassDescriptor>)>,
}

impl MetalPlugins {
    /// The device's objects for plugins; None when the renderer does not draw with Metal.
    pub fn new(device: &wgpu::Device, queue: &wgpu::Queue) -> Option<Self> {
        // SAFETY: the hal objects are only read, while the wgpu objects live.
        let raw_device = unsafe { device.as_hal::<Metal>() }?.raw_device().clone();
        let raw_queue = unsafe { queue.as_hal::<Metal>() }?.as_raw().retain();
        DEVICE.store(Retained::as_ptr(&raw_device) as usize, Ordering::Release);
        QUEUE.store(Retained::as_ptr(&raw_queue) as usize, Ordering::Release);
        Some(Self {
            _device: Shared(raw_device),
            _queue: Shared(raw_queue),
            natives: HashMap::default(),
            targets: Vec::new(),
            images: HashMap::default(),
            pass: None,
        })
    }

    /// What plugins saw of a texture goes with it.
    pub fn release(&mut self, handle: u64) {
        for which in [Which::Color, Which::Depth, Which::Resolved] {
            self.natives.remove(&(handle, which));
        }
    }
}

/// The `MTLTexture` of a wgpu texture.
fn raw_texture(texture: &wgpu::Texture) -> Option<Retained<Texture>> {
    // SAFETY: the raw handle is only read and retained.
    unsafe { texture.as_hal::<Metal>().map(|t| t.raw_handle().retain()) }
}

/// The sRGB form of an 8-bit color pixel format (None: it has none or is one).
fn srgb_form(format: MTLPixelFormat) -> Option<MTLPixelFormat> {
    match format {
        MTLPixelFormat::RGBA8Unorm => Some(MTLPixelFormat::RGBA8Unorm_sRGB),
        MTLPixelFormat::BGRA8Unorm => Some(MTLPixelFormat::BGRA8Unorm_sRGB),
        _ => None,
    }
}

/// Whether a texture's native pointer is sRGB: a color texture a Linear project samples as
/// sRGB (nativegfx `plugin.native.t14.srgb`, `t8.srgbtarget`: RGBA8Unorm_sRGB).
fn wants_srgb(gpu: &GpuTexture, color_space: i32) -> bool {
    gpu.srgb_view.is_some() && color_space == 1
}

/// A texture held by the native pointer cache, by its pointer.
///
/// # Safety
/// `pointer` must come from [`UnityRenderer::metal_texture`] while its entry is held.
unsafe fn held(pointer: usize) -> &'static Texture {
    unsafe { &*(pointer as *const Texture) }
}

impl UnityRenderer {
    /// The `MTLTexture` a texture's pointer names (cached while its storage is the same): its
    /// color in the format Unity's device stores it (sRGB where a Linear project samples it as
    /// sRGB; nativegfx `plugin.native.*`), a multisampled target's samples, its depth buffer (a
    /// depth-only target's for its color too), or a multisampled target's resolved texture.
    fn metal_texture(&mut self, handle: u64, which: Which) -> Option<usize> {
        let gpu = self.textures.get(&handle)?;
        let depth_only = gpu.format == 5 && gpu.depth_texture.is_some();
        let (storage, srgb) = match which {
            Which::Depth => (gpu.depth_texture.as_ref()?, false),
            Which::Color if depth_only => (gpu.depth_texture.as_ref()?, false),
            Which::Color => (
                gpu.msaa.as_ref().map_or(&gpu.texture, |m| m.0.texture()),
                wants_srgb(gpu, self.color_space),
            ),
            Which::Resolved => (gpu.msaa.as_ref().map(|_| &gpu.texture)?, false),
        };
        let storage = storage.clone();
        let (cube_texture, cube_array) = (gpu.cube, gpu.cube_array);
        let plugins = self.metal_plugins.as_mut()?;
        if let Some((kept, raw)) = plugins.natives.get(&(handle, which))
            && *kept == storage
        {
            return Some(Retained::as_ptr(&raw.0) as usize);
        }
        let mut raw = raw_texture(&storage)?;
        // Unity's device makes a Cubemap a cube texture and a CubemapArray a cube array
        // (nativegfx `plugin.native.cube`); the renderer stores them as 2D arrays.
        let cube = match which {
            Which::Color if cube_texture => Some(MTLTextureType::TypeCube),
            Which::Color if cube_array => Some(MTLTextureType::TypeCubeArray),
            _ => None,
        };
        let format = srgb
            .then(|| srgb_form(raw.pixelFormat()))
            .flatten()
            .unwrap_or(raw.pixelFormat());
        if let Some(texture_type) = cube {
            let levels = NSRange::new(0, raw.mipmapLevelCount());
            let slices = NSRange::new(0, raw.arrayLength());
            // SAFETY: the ranges are the texture's own.
            raw = unsafe {
                raw.newTextureViewWithPixelFormat_textureType_levels_slices(
                    format,
                    texture_type,
                    levels,
                    slices,
                )
            }?;
        } else if format != raw.pixelFormat() {
            raw = raw.newTextureViewWithPixelFormat(format)?;
        }
        let pointer = Retained::as_ptr(&raw) as usize;
        plugins
            .natives
            .insert((handle, which), (storage, Shared(raw)));
        Some(pointer)
    }

    /// A render texture's render buffer image outside callbacks
    /// (`RenderBackend::render_buffer_texture`).
    pub(crate) fn metal_render_buffer_texture(&mut self, handle: u64, image: u8) -> u64 {
        let which = match image {
            0 => Which::Color,
            1 => Which::Depth,
            _ => Which::Resolved,
        };
        self.metal_texture(handle, which).unwrap_or(0) as u64
    }

    /// A texture's native pointer on the Metal device: its `MTLTexture` (`depth`: its depth
    /// buffer's; a texture without one gives its color). 0 for an unknown texture.
    pub(super) fn metal_native_texture(&mut self, handle: u64, depth: bool) -> u64 {
        let has_depth = self
            .textures
            .get(&handle)
            .is_some_and(|t| t.depth_texture.is_some());
        let which = if depth && has_depth {
            Which::Depth
        } else {
            Which::Color
        };
        self.metal_texture(handle, which).unwrap_or(0) as u64
    }

    /// The render pass over the current target that encoders begin from, or None (the screen,
    /// a target not drawn into); the last one is kept and given again while the target's
    /// images are the same.
    fn metal_pass(&mut self) -> Option<Retained<MTLRenderPassDescriptor>> {
        let (color, depth) = self.target_handles;
        if color == 0 {
            return None;
        }
        if !self.state.target.as_ref()?.extra.is_empty()
            && !WARNED_COLORS.swap(true, Ordering::Relaxed)
        {
            tracing::warn!(
                "a native plugin callback while several colors are the target gets a render pass \
                 over the first color only"
            );
        }
        let (mip, slice) = self.plugins.target;
        let (level, slice) = (mip.max(0) as usize, slice.max(0) as usize);
        let gpu = self.textures.get(&color)?;
        let (volume, layered) = (gpu.volume, gpu.cube || gpu.cube_array || gpu.array);
        let (multisampled, depth_only) = (gpu.msaa.is_some(), gpu.format == 5);
        let (width, samples) = (gpu.width >> level, gpu.samples.max(1));
        let color_image = if depth_only {
            0
        } else {
            self.metal_texture(color, Which::Color)?
        };
        let resolved = if multisampled && !depth_only {
            self.metal_texture(color, Which::Resolved)?
        } else {
            0
        };
        // The depth buffer where it can be attached with the color: its samples and size at the
        // level are the color's (another render texture's may not be).
        let owner = super::depth_owner(color, depth);
        let depth_image = owner
            .and_then(|owner| {
                let buffer = self.textures.get(&owner)?.depth_texture.as_ref()?;
                let fits = depth_only
                    || (buffer.sample_count().max(1) == samples
                        && (buffer.width() >> level).max(1) == width.max(1));
                if !fits && !WARNED_DEPTH.swap(true, Ordering::Relaxed) {
                    tracing::warn!(
                        "a native plugin callback's render pass leaves out a depth buffer whose \
                         samples or size are not its color's"
                    );
                }
                fits.then_some(owner)
            })
            .and_then(|owner| self.metal_texture(owner, Which::Depth))
            .unwrap_or(0);
        if depth_only && depth_image == 0 {
            return None;
        }
        let key = [color_image, resolved, depth_image, level, slice];
        let plugins = self.metal_plugins.as_mut()?;
        if let Some((kept, pass)) = &plugins.pass
            && *kept == key
        {
            return Some(pass.0.clone());
        }
        let pass = MTLRenderPassDescriptor::new();
        if color_image != 0 {
            // SAFETY: index 0 is within the attachments' range.
            let attachment = unsafe { pass.colorAttachments().objectAtIndexedSubscript(0) };
            // SAFETY: the cache holds the textures.
            attachment.setTexture(Some(unsafe { held(color_image) }));
            attachment.setLevel(level);
            if volume {
                attachment.setDepthPlane(slice);
            } else if layered {
                attachment.setSlice(slice);
            }
            attachment.setLoadAction(MTLLoadAction::Load);
            if resolved != 0 {
                attachment.setResolveTexture(Some(unsafe { held(resolved) }));
                attachment.setResolveLevel(level);
                if volume {
                    attachment.setResolveDepthPlane(slice);
                } else if layered {
                    attachment.setResolveSlice(slice);
                }
                attachment.setStoreAction(MTLStoreAction::StoreAndMultisampleResolve);
            } else {
                attachment.setStoreAction(MTLStoreAction::Store);
            }
        }
        if depth_image != 0 {
            // SAFETY: the cache holds it.
            let texture = unsafe { held(depth_image) };
            // A 3D target's depth buffer is one image; an array's or a cube's has a layer for
            // each of the color's.
            let depth_slice = layered.then_some(slice);
            let attachment = pass.depthAttachment();
            attachment.setTexture(Some(texture));
            attachment.setLevel(level);
            if let Some(slice) = depth_slice {
                attachment.setSlice(slice);
            }
            attachment.setLoadAction(MTLLoadAction::Load);
            attachment.setStoreAction(MTLStoreAction::Store);
            if matches!(
                texture.pixelFormat(),
                MTLPixelFormat::Depth32Float_Stencil8 | MTLPixelFormat::Depth24Unorm_Stencil8
            ) {
                let stencil = pass.stencilAttachment();
                stencil.setTexture(Some(texture));
                stencil.setLevel(level);
                if let Some(slice) = depth_slice {
                    stencil.setSlice(slice);
                }
                stencil.setLoadAction(MTLLoadAction::Load);
                stencil.setStoreAction(MTLStoreAction::Store);
            }
        }
        plugins.pass = Some((key, Shared(pass.clone())));
        Some(pass)
    }

    /// Runs a callback on the Metal device (see the module's documentation).
    pub(super) fn run_plugin_metal(&mut self, invoke: impl FnOnce()) {
        commit_outside();
        let Some(buffer) = queue().and_then(|q| q.commandBuffer()) else {
            if !WARNED_BUFFER.swap(true, Ordering::Relaxed) {
                tracing::warn!(
                    "native plugin callbacks are skipped: the Metal queue made no command buffer"
                );
            }
            return;
        };
        let pass = self.metal_pass();
        let screen = self.target_handles.0 == 0;
        let multisampled = pass.is_some()
            && self
                .textures
                .get(&self.target_handles.0)
                .is_some_and(|t| t.msaa.is_some());
        // Render buffer pointers name render textures: their images, by handle (the cached
        // pointers; the map and the handle list are reused from callback to callback).
        let Some((mut targets, mut images)) = self.metal_plugins.as_mut().map(|p| {
            (
                std::mem::take(&mut p.targets),
                std::mem::take(&mut p.images),
            )
        }) else {
            return;
        };
        targets.clear();
        targets.extend(
            self.textures
                .iter()
                .filter(|(_, t)| t.render_target)
                .map(|(&h, _)| h),
        );
        images.clear();
        for &handle in &targets {
            let image = Images {
                color: self.metal_texture(handle, Which::Color).unwrap_or(0),
                resolved: self.metal_texture(handle, Which::Resolved).unwrap_or(0),
                depth: self.metal_texture(handle, Which::Depth).unwrap_or(0),
            };
            images.insert(handle, image);
        }
        SCOPE.with(|scope| {
            *scope.borrow_mut() = Some(Scope {
                buffer,
                encoder: None,
                pass,
                screen,
                images,
                committed: Vec::new(),
                drew_samples: false,
                multisampled,
            })
        });
        invoke();
        let Some(scope) = SCOPE.with(|scope| scope.borrow_mut().take()) else {
            return;
        };
        if let Some(encoder) = scope.encoder {
            encoder.endEncoding();
        }
        scope.buffer.commit();
        // What it drew into a multisampled target's samples reaches the resolved image and its
        // retained copy as the OpenGL path's does (the executor resolves again).
        self.plugins.drew_into_samples |= scope.drew_samples;
        if let Some(plugins) = self.metal_plugins.as_mut() {
            plugins.targets = targets;
            plugins.images = scope.images;
        }
    }

    /// Shows a plugin's own `MTLTexture` (`native`), or another texture's storage when it is
    /// one's pointer (`CreateExternalTexture` with `GetNativeTexturePtr`).
    pub(super) fn external_texture_metal(
        &mut self,
        handle: u64,
        native: u64,
        info: &unbound_runtime::render_protocol::ExternalTextureInfo,
    ) -> Result<(), String> {
        use super::plugins::descriptor;
        use unbound_runtime::render_protocol::owned;
        if native == 0 {
            return Err("the external Metal texture pointer is null".into());
        }
        let plugins = self
            .metal_plugins
            .as_ref()
            .ok_or("the renderer does not draw with Metal")?;
        let source = plugins
            .natives
            .iter()
            .find(|(_, (_, raw))| Retained::as_ptr(&raw.0) as u64 == native)
            .map(|(&(source, _), (storage, _))| (source, storage.clone()));
        if let Some((source, storage)) = source {
            let shown = &self.textures[&source];
            let mut packet = descriptor(shown);
            if packet.dimension != info.dimension {
                return Err(
                    "the external texture dimension does not match its native storage".into(),
                );
            }
            packet.render_target = false;
            packet.random_write = false;
            packet.auto_generate_mips = false;
            packet.defer_mips = false;
            packet.depth_bits = 0;
            packet.samples = 1;
            self.install_external_texture(handle, &packet, storage, true, false);
            return Ok(());
        }
        // SAFETY: the caller passes its id<MTLTexture>, retained here while it is shown.
        let raw = unsafe { Retained::retain(native as *mut Texture) }
            .ok_or("the external Metal texture pointer is null")?;
        let (format, code) = external_format(raw.pixelFormat()).ok_or_else(|| {
            format!(
                "the external Metal texture's pixel format {} is not supported",
                raw.pixelFormat().0
            )
        })?;
        let (dimension, layers) = match raw.textureType() {
            MTLTextureType::Type2D => (2, 1),
            MTLTextureType::TypeCube => (4, 6),
            MTLTextureType::Type3D => (3, raw.depth() as i32),
            other => {
                return Err(format!(
                    "the external Metal texture's type {} is not supported",
                    other.0
                ));
            }
        };
        if dimension != info.dimension {
            return Err("the external texture dimension does not match its Metal type".into());
        }
        let packet = owned::Texture {
            width: raw.width().max(1) as i32,
            height: raw.height().max(1) as i32,
            layers,
            dimension,
            format: code,
            mip_count: info.mip_count.clamp(1, raw.mipmapLevelCount() as i32),
            filter_mode: 1,
            wrap_u: 1,
            wrap_v: 1,
            wrap_w: 1,
            samples: 1,
            ..Default::default()
        };
        let device = self.device.clone();
        self.generation += 1;
        let gpu = crate::render::resources::upload_texture_with(
            &device,
            &self.queue,
            None,
            &packet,
            self.generation,
            &mut |desc| plugin_texture(&device, raw.clone(), format, desc),
        );
        self.install_external_gpu(handle, &packet, gpu, false, false);
        let storage = self.textures[&handle].texture.clone();
        if let Some(plugins) = self.metal_plugins.as_mut() {
            plugins
                .natives
                .insert((handle, Which::Color), (storage, Shared(raw)));
        }
        Ok(())
    }
}

/// The wgpu format and texture packet code of a plugin's `MTLTexture` pixel format: the color
/// formats shaders sample as floats (as `plugins::external_format` has OpenGL's). Others are
/// refused.
fn external_format(format: MTLPixelFormat) -> Option<(wgpu::TextureFormat, i32)> {
    use unbound_runtime::render_protocol::texture_format as code;
    use wgpu::TextureFormat as F;
    Some(match format {
        MTLPixelFormat::RGBA8Unorm => (F::Rgba8Unorm, code::LINEAR_RGBA8),
        MTLPixelFormat::RGBA8Unorm_sRGB => (F::Rgba8UnormSrgb, code::LINEAR_RGBA8),
        MTLPixelFormat::BGRA8Unorm => (F::Bgra8Unorm, code::LINEAR_RGBA8),
        MTLPixelFormat::BGRA8Unorm_sRGB => (F::Bgra8UnormSrgb, code::LINEAR_RGBA8),
        MTLPixelFormat::R8Unorm => (F::R8Unorm, code::R8_UNORM),
        MTLPixelFormat::RG8Unorm => (F::Rg8Unorm, code::RG8_UNORM),
        MTLPixelFormat::R16Float => (F::R16Float, code::R16_FLOAT),
        MTLPixelFormat::RG16Float => (F::Rg16Float, code::RG16_FLOAT),
        MTLPixelFormat::RGBA16Float => (F::Rgba16Float, code::RGBA_HALF),
        MTLPixelFormat::R32Float => (F::R32Float, code::R32_FLOAT),
        MTLPixelFormat::RG32Float => (F::Rg32Float, code::RG32_FLOAT),
        MTLPixelFormat::RGBA32Float => (F::Rgba32Float, code::RGBA32_FLOAT),
        _ => return None,
    })
}

/// A renderer texture over a plugin's own `MTLTexture` (its shape in `desc`, its format
/// `format`): the renderer samples it and releases it when its texture goes.
fn plugin_texture(
    device: &wgpu::Device,
    raw: Retained<Texture>,
    format: wgpu::TextureFormat,
    desc: &wgpu::TextureDescriptor,
) -> wgpu::Texture {
    let raw_type = raw.textureType();
    let desc = wgpu::TextureDescriptor {
        format,
        view_formats: &[],
        ..desc.clone()
    };
    let copy_size = wgpu::hal::CopyExtent {
        width: desc.size.width,
        height: desc.size.height,
        depth: desc.size.depth_or_array_layers,
    };
    let layers = if raw_type == MTLTextureType::Type3D {
        1
    } else {
        desc.size.depth_or_array_layers
    };
    // SAFETY: `raw` matches `desc` (its size, levels, type and format were read from it).
    unsafe {
        let hal = wgpu::hal::metal::Device::texture_from_raw(
            raw,
            format,
            raw_type,
            layers,
            desc.mip_level_count,
            copy_size,
            None,
        );
        device.create_texture_from_hal::<Metal>(hal, &desc, wgpu::TextureUses::RESOURCE)
    }
}
