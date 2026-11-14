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
//! copies the texture's earlier texels). Inside it the current encoder is begun when first asked,
//! over the current target's attachments at the image being drawn: a render texture's (their
//! contents loaded, a multisampled color resolved into its texture again) or the screen's (its
//! color texture and depth buffer, loaded). Its store actions are left unknown in the descriptor
//! and set when it ends, as the Editor's are (nativegfx `*.actions`: store 4). After
//! `EndCurrentCommandEncoder` there is no descriptor until the plugin asks for an encoder again,
//! which is begun over the screen without loading its contents, whatever the target was
//! ([`Redirect`]). Outside callbacks (a plugin's own calls from scripts) the current command
//! buffer is one of the queue's, committed by `CommitCurrentCommandBuffer`, else before the next
//! callback or at the end of the frame, so it runs after the rendering submitted by then (a
//! script's commit: before what the frame has not submitted yet; neither is measured), and the
//! current encoder is over the screen of the latest frame or callback (which frame's image a
//! script's draw reaches is unmeasured). A texture's native pointer is its `MTLTexture`
//! (in the format Unity's device stores it: sRGB where a Linear project samples it as sRGB);
//! a multisampled render texture's color and depth pointers are its samples. The screen's color
//! texture is the renderer's 8-bit RGBA one; the macOS Editor's is BGRA (`format=80`).
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
type RenderEncoder = ProtocolObject<dyn MTLRenderCommandEncoder>;

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
/// What plugins get outside callbacks: a command buffer of the queue (made when asked for) and
/// the encoder over the screen.
static OUTSIDE: Mutex<Outside> = Mutex::new(Outside {
    buffer: None,
    current: Current::SCREEN,
});
/// The screen's images of the latest frame or callback, for encoders outside callbacks.
static SCREEN: Mutex<Option<ScreenImages>> = Mutex::new(None);
/// The buffer `CommitCurrentCommandBuffer` last committed outside callbacks, retained so the
/// plugin can wait on it (0: none).
static COMMITTED_OUTSIDE: Mutex<usize> = Mutex::new(0);
/// What plugins were told once: no screen yet outside callbacks, the first of several colors
/// only, a depth buffer left out, no command buffer, renderer busy.
static WARNED_OUTSIDE: AtomicBool = AtomicBool::new(false);
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

/// Logs once that a plugin asked for the encoder or its descriptor outside callbacks before the
/// renderer had a screen.
fn warn_outside(what: &str) {
    if !WARNED_OUTSIDE.swap(true, Ordering::Relaxed) {
        tracing::warn!(
            "a native plugin asked for the {what} outside a rendering callback before the first frame: there is no screen yet (nil)"
        );
    }
}

/// Where a plugin's next encoder goes. In the macOS Editor, once the plugin ends the encoder (in
/// a render texture's pass, over the screen and outside callbacks) there is no descriptor until it
/// asks for an encoder again, which is over the screen with its contents not loaded (nativegfx
/// `plugin.state.*.camera2.2`, `*.mutate`, `*.screen`, `*.outside`: load 0); committing the
/// command buffer keeps the target (`*.commitrt.2`). Unity's next draw into the target, a new
/// target or a new frame takes it back (unmeasured).
#[derive(Clone, Copy, PartialEq, Eq, Default, Debug)]
pub(super) enum Redirect {
    /// The current target.
    #[default]
    Target,
    /// The plugin ended the encoder: the next one is over the screen, not loaded.
    Ended,
    /// An encoder was begun over the screen after that. The Editor's lasts into the next events
    /// (nativegfx `plugin.state.*.camera2.3`: its descriptor's load action is still 0); here
    /// each callback begins one of its own, which loads what the earlier one drew.
    Screen,
}

/// The store actions an encoder ends with: its descriptor leaves them unknown until then.
#[derive(Clone, Copy)]
struct Finish {
    color: Option<MTLStoreAction>,
    depth: bool,
    stencil: bool,
}

/// An encoder begun for a plugin and the descriptor it was begun from (a fresh object for each
/// encoder, so a plugin's change to it never reaches another).
struct Open {
    encoder: Retained<RenderEncoder>,
    pass: Retained<MTLRenderPassDescriptor>,
    finish: Finish,
}

impl Open {
    /// An encoder over `params` with `load`; its descriptor shows `shown` (the load action of the
    /// encoder it continues).
    fn begin(
        buffer: &CommandBuffer,
        params: &PassParams,
        load: MTLLoadAction,
        shown: MTLLoadAction,
    ) -> Option<Self> {
        let (pass, finish) = params.descriptor(load);
        let encoder = buffer.renderCommandEncoderWithDescriptor(&pass)?;
        if shown != load {
            // SAFETY: index 0 is within the attachments' range.
            unsafe { pass.colorAttachments().objectAtIndexedSubscript(0) }.setLoadAction(shown);
            pass.depthAttachment().setLoadAction(shown);
            pass.stencilAttachment().setLoadAction(shown);
        }
        Some(Self {
            encoder,
            pass,
            finish,
        })
    }

    /// Sets the store actions the descriptor left unknown, then ends the encoder.
    fn end(self) {
        if let Some(action) = self.finish.color {
            // SAFETY: attachment 0 is the descriptor's color.
            unsafe { self.encoder.setColorStoreAction_atIndex(action, 0) };
        }
        if self.finish.depth {
            self.encoder.setDepthStoreAction(MTLStoreAction::Store);
        }
        if self.finish.stencil {
            self.encoder.setStencilStoreAction(MTLStoreAction::Store);
        }
        self.encoder.endEncoding();
    }
}

/// Where an encoder was begun when the plugin asked.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum Begun {
    /// None was (there is one, or nothing to begin it over).
    No,
    /// Over the current target.
    Target,
    /// Over the screen, after the plugin ended the encoder.
    Screen,
}

/// The current encoder of a callback or of the calls outside callbacks.
struct Current {
    open: Option<Open>,
    /// The Editor's encoder exists (asking for the descriptor begins it), until the plugin ends
    /// it or commits the buffer.
    drawn: bool,
    redirect: Redirect,
}

impl Current {
    /// Outside callbacks: the Editor's encoder over the screen exists (nativegfx
    /// `plugin.peek.outside`, `plugin.load.state`).
    const SCREEN: Self = Self {
        open: None,
        drawn: true,
        redirect: Redirect::Target,
    };

    /// The current encoder, begun where the plugin's next one goes if there is none; where it
    /// was begun now.
    fn encoder(
        &mut self,
        buffer: &CommandBuffer,
        target: Option<&PassParams>,
        screen: Option<&PassParams>,
    ) -> (Object, Begun) {
        let mut begun = Begun::No;
        if self.open.is_none() {
            use MTLLoadAction as L;
            let (params, load, shown) = match self.redirect {
                Redirect::Target => (target, L::Load, L::Load),
                Redirect::Ended => (screen, L::DontCare, L::DontCare),
                Redirect::Screen => (screen, L::Load, L::DontCare),
            };
            self.open = params.and_then(|params| Open::begin(buffer, params, load, shown));
            if self.open.is_some() {
                begun = if self.redirect == Redirect::Target {
                    Begun::Target
                } else {
                    Begun::Screen
                };
                if self.redirect == Redirect::Ended {
                    self.redirect = Redirect::Screen;
                }
            }
        }
        let encoder = self
            .open
            .as_ref()
            .map_or(std::ptr::null_mut(), |o| object(&o.encoder));
        (encoder, begun)
    }

    /// The descriptor the current encoder was begun from: where the Editor's encoder exists it is
    /// begun here; after only a clear, and once the plugin ended the encoder (in this event or an
    /// earlier one), there is none (nil). Measured on the macOS Editor: nativegfx
    /// `plugin.peek.drawn`, `plugin.peek.plugindrew`, `plugin.peek.screen`, `plugin.peek.outside`
    /// (1), `plugin.peek.cleared`, `plugin.mutate`, `plugin.endpeek.*` and `plugin.peek.ended`,
    /// `plugin.peek.screen.2`, `plugin.peek.outside.2` (0).
    fn descriptor(
        &mut self,
        buffer: &CommandBuffer,
        target: Option<&PassParams>,
        screen: Option<&PassParams>,
    ) -> (Object, Begun) {
        let mut begun = Begun::No;
        if self.open.is_none() && self.drawn {
            begun = self.encoder(buffer, target, screen).1;
        }
        let pass = self
            .open
            .as_ref()
            .map_or(std::ptr::null_mut(), |o| object(&o.pass));
        (pass, begun)
    }

    /// `EndCurrentCommandEncoder`: the next encoder goes over the screen.
    fn end(&mut self) {
        self.close();
        self.drawn = false;
        self.redirect = Redirect::Ended;
    }

    /// The encoder ends with its command buffer (committed, or the callback is over).
    fn close(&mut self) {
        if let Some(open) = self.open.take() {
            open.end();
        }
    }
}

/// What plugins use outside callbacks.
struct Outside {
    buffer: Option<Retained<CommandBuffer>>,
    current: Current,
}

// SAFETY: Metal's command buffers and encoders may be used from any thread; the mutex orders
// their use.
unsafe impl Send for Outside {}

/// The screen's color texture and depth buffer.
struct ScreenImages {
    color: Retained<Texture>,
    depth: Retained<Texture>,
}

// SAFETY: Metal textures may be used from any thread; nothing here mutates them.
unsafe impl Send for ScreenImages {}

impl ScreenImages {
    /// A render pass over them (valid while they are held).
    fn params(&self) -> PassParams {
        PassParams {
            color: Retained::as_ptr(&self.color) as usize,
            resolved: 0,
            depth: Retained::as_ptr(&self.depth) as usize,
            level: 0,
            slice: 0,
            layered: false,
            volume: false,
        }
    }
}

/// What a callback sees: its command buffer, its current encoder, the render passes over the
/// current target and over the screen, and the render textures' images by handle (render buffer
/// pointers).
struct Scope {
    buffer: Retained<CommandBuffer>,
    current: Current,
    /// The render pass over the current target (None: a target the callback cannot draw into).
    params: Option<PassParams>,
    /// The render pass over the screen.
    screen: Option<PassParams>,
    images: HashMap<u64, Images>,
    /// Buffers committed by `CommitCurrentCommandBuffer` in this callback, kept until it ends
    /// (the plugin may wait on the one returned).
    committed: Vec<Retained<CommandBuffer>>,
    /// Whether an encoder was begun over a multisampled color.
    drew_samples: bool,
    multisampled: bool,
    /// Whether an encoder was begun over the screen after the plugin ended one.
    used_screen: bool,
}

impl Scope {
    /// What an encoder begun for the plugin drew over.
    fn note(&mut self, begun: Begun) {
        self.drew_samples |= begun == Begun::Target && self.multisampled;
        self.used_screen |= begun == Begun::Screen;
    }
}

/// What a render pass over the current target is made of: its images (pointers the native
/// pointer cache holds; 0: none), the level and the slice (a 3D target's depth plane).
#[derive(Clone, Copy)]
struct PassParams {
    color: usize,
    resolved: usize,
    depth: usize,
    level: usize,
    slice: usize,
    layered: bool,
    volume: bool,
}

impl PassParams {
    /// A new descriptor of the pass: its color (resolved again where multisampled) and depth
    /// buffer at the level and slice, with `load`, their store actions unknown; the actions the
    /// encoder ends with.
    fn descriptor(&self, load: MTLLoadAction) -> (Retained<MTLRenderPassDescriptor>, Finish) {
        let pass = MTLRenderPassDescriptor::new();
        let (level, slice) = (self.level, self.slice);
        let mut finish = Finish {
            color: None,
            depth: false,
            stencil: false,
        };
        if self.color != 0 {
            // SAFETY: index 0 is within the attachments' range; the cache holds the textures.
            let attachment = unsafe { pass.colorAttachments().objectAtIndexedSubscript(0) };
            attachment.setTexture(Some(unsafe { held(self.color) }));
            attachment.setLevel(level);
            if self.volume {
                attachment.setDepthPlane(slice);
            } else if self.layered {
                attachment.setSlice(slice);
            }
            attachment.setLoadAction(load);
            attachment.setStoreAction(MTLStoreAction::Unknown);
            finish.color = Some(if self.resolved != 0 {
                attachment.setResolveTexture(Some(unsafe { held(self.resolved) }));
                attachment.setResolveLevel(level);
                if self.volume {
                    attachment.setResolveDepthPlane(slice);
                } else if self.layered {
                    attachment.setResolveSlice(slice);
                }
                MTLStoreAction::StoreAndMultisampleResolve
            } else {
                MTLStoreAction::Store
            });
        }
        if self.depth != 0 {
            // SAFETY: the cache holds it.
            let texture = unsafe { held(self.depth) };
            let attachment = pass.depthAttachment();
            attachment.setTexture(Some(texture));
            attachment.setLevel(level);
            // A 3D target's depth buffer is one image; an array's or a cube's has a layer for
            // each of the color's.
            if self.layered {
                attachment.setSlice(slice);
            }
            attachment.setLoadAction(load);
            attachment.setStoreAction(MTLStoreAction::Unknown);
            finish.depth = true;
            if matches!(
                texture.pixelFormat(),
                MTLPixelFormat::Depth32Float_Stencil8 | MTLPixelFormat::Depth24Unorm_Stencil8
            ) {
                let stencil = pass.stencilAttachment();
                stencil.setTexture(Some(texture));
                stencil.setLevel(level);
                if self.layered {
                    stencil.setSlice(slice);
                }
                stencil.setLoadAction(load);
                stencil.setStoreAction(MTLStoreAction::Unknown);
                finish.stencil = true;
            }
        }
        (pass, finish)
    }
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
    outside_buffer(&mut outside).map_or(std::ptr::null_mut(), object)
}

/// The command buffer plugins get outside callbacks, made when first asked for.
fn outside_buffer(outside: &mut Outside) -> Option<&Retained<CommandBuffer>> {
    if outside.buffer.is_none() {
        outside.buffer = queue().and_then(|q| q.commandBuffer());
    }
    outside.buffer.as_ref()
}

/// Commits what plugins encoded outside callbacks, if they asked for a command buffer; the
/// Editor's encoder over the screen is there again after it.
pub(super) fn commit_outside() {
    let mut outside = OUTSIDE.lock().unwrap_or_else(|e| e.into_inner());
    outside.current.close();
    outside.current = Current::SCREEN;
    if let Some(buffer) = outside.buffer.take() {
        buffer.commit();
    }
}

/// Runs `f` with the current encoder outside callbacks, its command buffer and the pass over the
/// screen (None before the renderer's first frame).
fn with_outside<R>(
    f: impl FnOnce(&mut Current, &CommandBuffer, Option<&PassParams>) -> R,
) -> Option<R> {
    let screen = SCREEN.lock().unwrap_or_else(|e| e.into_inner());
    let params = screen.as_ref().map(ScreenImages::params);
    let mut outside = OUTSIDE.lock().unwrap_or_else(|e| e.into_inner());
    let buffer = outside_buffer(&mut outside)?.clone();
    Some(f(&mut outside.current, &buffer, params.as_ref()))
}

extern "C" fn current_command_encoder() -> Object {
    with_scope(|s| {
        let (encoder, begun) = s
            .current
            .encoder(&s.buffer, s.params.as_ref(), s.screen.as_ref());
        s.note(begun);
        encoder
    })
    .or_else(|| with_outside(|current, buffer, screen| current.encoder(buffer, screen, screen).0))
    .filter(|encoder| !encoder.is_null())
    .unwrap_or_else(|| {
        if SCREEN.lock().unwrap_or_else(|e| e.into_inner()).is_none() {
            warn_outside("current command encoder");
        }
        std::ptr::null_mut()
    })
}

extern "C" fn end_current_command_encoder() {
    if with_scope(|s| s.current.end()).is_none() {
        OUTSIDE
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .current
            .end();
    }
}

/// The descriptor the current encoder was begun from (as the header says; [`Current::descriptor`]).
extern "C" fn current_render_pass_descriptor() -> Object {
    with_scope(|s| {
        let (pass, begun) = s
            .current
            .descriptor(&s.buffer, s.params.as_ref(), s.screen.as_ref());
        s.note(begun);
        pass
    })
    .or_else(|| {
        with_outside(|current, buffer, screen| current.descriptor(buffer, screen, screen).0)
    })
    .unwrap_or(std::ptr::null_mut())
}

/// Commits the current command buffer and goes on in a new one; the committed one is returned
/// (nativegfx `plugin.commit.*`: status Committed, a new current buffer; `plugin.script.commit`
/// outside callbacks). The encoder ends with it, so the descriptor is nil until the plugin
/// asks for an encoder again (unmeasured), which is over the same target (`plugin.commit.commitrt`).
extern "C" fn commit_current_command_buffer() -> Object {
    let inside = with_scope(|s| {
        s.current.close();
        s.current.drawn = false;
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
    let buffer = {
        let mut outside = OUTSIDE.lock().unwrap_or_else(|e| e.into_inner());
        outside.current.close();
        outside.current.drawn = false;
        outside_buffer(&mut outside);
        outside.buffer.take()
    };
    let Some(buffer) = buffer else {
        return std::ptr::null_mut();
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
    /// The screen's color texture and depth buffer and their `MTLTexture`s.
    screen: Option<(wgpu::Texture, wgpu::Texture, ScreenImages)>,
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
            screen: None,
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

    /// The render pass over the screen of this frame (its color texture and depth buffer, cached
    /// while they are the same), which encoders outside callbacks then begin from too (None:
    /// the renderer has no Metal plugins).
    fn metal_screen(&mut self) -> Option<PassParams> {
        let slot = self.screen_slot;
        let screen = self.ensure_screen(slot);
        let (color, depth) = (screen.texture.clone(), screen.depth_texture.clone());
        let plugins = self.metal_plugins.as_mut()?;
        let kept = plugins
            .screen
            .as_ref()
            .is_some_and(|(c, d, _)| *c == color && *d == depth);
        if !kept {
            let images = ScreenImages {
                color: raw_texture(&color)?,
                depth: raw_texture(&depth)?,
            };
            let shared = ScreenImages {
                color: images.color.clone(),
                depth: images.depth.clone(),
            };
            plugins.screen = Some((color, depth, images));
            *SCREEN.lock().unwrap_or_else(|e| e.into_inner()) = Some(shared);
        }
        plugins
            .screen
            .as_ref()
            .map(|(_, _, images)| images.params())
    }

    /// Hands plugins' calls outside callbacks the screen of this frame.
    pub(super) fn publish_metal_screen(&mut self) {
        self.metal_screen();
    }

    /// The render pass over the current render texture target that encoders begin from, or None
    /// (a target the callback cannot draw into).
    fn metal_pass(&mut self) -> Option<PassParams> {
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
        let (width, height, samples) =
            (gpu.width >> level, gpu.height >> level, gpu.samples.max(1));
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
                        && (level as u32) < buffer.mip_level_count()
                        && (buffer.width() >> level).max(1) == width.max(1)
                        && (buffer.height() >> level).max(1) == height.max(1));
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
        Some(PassParams {
            color: color_image,
            resolved,
            depth: depth_image,
            level,
            slice,
            layered,
            volume,
        })
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
        let screen = self.metal_screen();
        let on_screen = self.target_handles.0 == 0;
        let params = if on_screen { screen } else { self.metal_pass() };
        let multisampled = !on_screen
            && params.is_some()
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
                current: Current {
                    open: None,
                    // The Editor's encoder over the screen is there until the plugin ends it
                    // (nativegfx `plugin.peek.screen`).
                    drawn: self.plugin_target_drawn
                        || (on_screen && self.plugin_redirect != Redirect::Ended),
                    redirect: self.plugin_redirect,
                },
                params,
                screen,
                images,
                committed: Vec::new(),
                drew_samples: false,
                multisampled,
                used_screen: false,
            })
        });
        invoke();
        let Some(mut scope) = SCOPE.with(|scope| scope.borrow_mut().take()) else {
            return;
        };
        // The Editor's encoder lasts into the next event where the plugin left it begun or did
        // not end the one a draw began (nativegfx `plugin.peek.plugindrew`, `plugin.peek.ended`);
        // so does where the next one goes.
        self.plugin_target_drawn = scope.current.drawn || scope.current.open.is_some();
        self.plugin_redirect = scope.current.redirect;
        scope.current.close();
        scope.buffer.commit();
        // An encoder over the screen is a use of it: the frame's screen goes on from what the
        // plugin drew (unmeasured; begun in an earlier event, the executor started the screen
        // before this one).
        self.screen_touched |= scope.used_screen;
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

#[cfg(test)]
mod tests {
    use super::*;
    use objc2_metal::{
        MTLCreateSystemDefaultDevice, MTLOrigin, MTLRegion, MTLSize, MTLStorageMode,
        MTLTextureDescriptor, MTLTextureUsage,
    };

    /// A 4x4 RGBA8 render target the CPU can read and write, filled with `texel`.
    fn texture(device: &ProtocolObject<dyn MTLDevice>, texel: [u8; 4]) -> Retained<Texture> {
        // SAFETY: valid sizes and format.
        let desc = unsafe {
            MTLTextureDescriptor::texture2DDescriptorWithPixelFormat_width_height_mipmapped(
                MTLPixelFormat::RGBA8Unorm,
                4,
                4,
                false,
            )
        };
        desc.setStorageMode(MTLStorageMode::Shared);
        desc.setUsage(MTLTextureUsage::RenderTarget | MTLTextureUsage::ShaderRead);
        let texture = device.newTextureWithDescriptor(&desc).unwrap();
        let bytes = [texel; 16];
        // SAFETY: 16 texels of 4 bytes, rows of 16 bytes.
        unsafe {
            texture.replaceRegion_mipmapLevel_withBytes_bytesPerRow(
                region(),
                0,
                std::ptr::NonNull::new(bytes.as_ptr() as *mut c_void).unwrap(),
                16,
            )
        };
        texture
    }

    fn region() -> MTLRegion {
        MTLRegion {
            origin: MTLOrigin { x: 0, y: 0, z: 0 },
            size: MTLSize {
                width: 4,
                height: 4,
                depth: 1,
            },
        }
    }

    fn texels(texture: &Texture) -> [[u8; 4]; 16] {
        let mut out = [[0u8; 4]; 16];
        // SAFETY: the buffer holds the 4x4 region.
        unsafe {
            texture.getBytes_bytesPerRow_fromRegion_mipmapLevel(
                std::ptr::NonNull::new(out.as_mut_ptr() as *mut c_void).unwrap(),
                16,
                region(),
                0,
            )
        };
        out
    }

    fn params(texture: &Retained<Texture>) -> PassParams {
        PassParams {
            color: Retained::as_ptr(texture) as usize,
            resolved: 0,
            depth: 0,
            level: 0,
            slice: 0,
            layered: false,
            volume: false,
        }
    }

    /// The descriptor handed out: its color texture and load and store actions.
    fn attachment(pass: Object) -> Option<(usize, MTLLoadAction, MTLStoreAction)> {
        // SAFETY: a descriptor handed out by `Current`, alive while its encoder is.
        let pass = unsafe { (pass as *const MTLRenderPassDescriptor).as_ref() }?;
        let color = unsafe { pass.colorAttachments().objectAtIndexedSubscript(0) };
        let texture = color.texture().map_or(0, |t| Retained::as_ptr(&t) as usize);
        Some((texture, color.loadAction(), color.storeAction()))
    }

    struct Setup {
        queue: Retained<ProtocolObject<dyn MTLCommandQueue>>,
        target: Retained<Texture>,
        screen: Retained<Texture>,
    }

    /// None where the machine has no Metal device.
    fn setup() -> Option<Setup> {
        let device = MTLCreateSystemDefaultDevice()?;
        Some(Setup {
            queue: device.newCommandQueue()?,
            target: texture(&device, [255, 0, 0, 255]),
            screen: texture(&device, [0, 255, 0, 255]),
        })
    }

    #[test]
    fn ending_the_encoder_moves_the_next_one_to_the_screen_unloaded() {
        let Some(s) = setup() else { return };
        let (target, screen) = (params(&s.target), params(&s.screen));
        let buffer = s.queue.commandBuffer().unwrap();
        let mut current = Current {
            open: None,
            drawn: true,
            redirect: Redirect::Target,
        };
        // The Editor's encoder over the target exists: the descriptor begins it, its store left
        // unknown.
        let (pass, begun) = current.descriptor(&buffer, Some(&target), Some(&screen));
        assert_eq!(begun, Begun::Target);
        assert_eq!(
            attachment(pass),
            Some((target.color, MTLLoadAction::Load, MTLStoreAction::Unknown))
        );
        current.end();
        assert!(
            current
                .descriptor(&buffer, Some(&target), Some(&screen))
                .0
                .is_null()
        );
        // Asked for again: over the screen, not loaded.
        let (encoder, begun) = current.encoder(&buffer, Some(&target), Some(&screen));
        assert!(!encoder.is_null());
        assert_eq!(begun, Begun::Screen);
        let pass = current.descriptor(&buffer, Some(&target), Some(&screen)).0;
        assert_eq!(
            attachment(pass),
            Some((
                screen.color,
                MTLLoadAction::DontCare,
                MTLStoreAction::Unknown
            ))
        );
        assert_eq!(current.redirect, Redirect::Screen);
        current.close();
        buffer.commit();
        buffer.waitUntilCompleted();
        assert_eq!(
            buffer.status(),
            objc2_metal::MTLCommandBufferStatus::Completed
        );
        // The target keeps what it had.
        assert_eq!(texels(&s.target), [[255, 0, 0, 255]; 16]);
    }

    #[test]
    fn a_continued_screen_encoder_keeps_its_contents_and_shows_how_it_began() {
        let Some(s) = setup() else { return };
        let (target, screen) = (params(&s.target), params(&s.screen));
        let buffer = s.queue.commandBuffer().unwrap();
        // A later event after the plugin began one over the screen.
        let mut current = Current {
            open: None,
            drawn: true,
            redirect: Redirect::Screen,
        };
        let pass = current.descriptor(&buffer, Some(&target), Some(&screen)).0;
        assert_eq!(
            attachment(pass),
            Some((
                screen.color,
                MTLLoadAction::DontCare,
                MTLStoreAction::Unknown
            ))
        );
        current.close();
        buffer.commit();
        buffer.waitUntilCompleted();
        // Loaded and stored: the store action set when it ended.
        assert_eq!(texels(&s.screen), [[0, 255, 0, 255]; 16]);
    }

    #[test]
    fn a_commit_keeps_the_target_and_the_screen_is_there_outside_callbacks() {
        let Some(s) = setup() else { return };
        let (target, screen) = (params(&s.target), params(&s.screen));
        let buffer = s.queue.commandBuffer().unwrap();
        let mut current = Current {
            open: None,
            drawn: true,
            redirect: Redirect::Target,
        };
        current.encoder(&buffer, Some(&target), Some(&screen));
        // `CommitCurrentCommandBuffer`: the encoder ends with the buffer, no descriptor until
        // one is asked for, over the same target.
        current.close();
        current.drawn = false;
        assert!(
            current
                .descriptor(&buffer, Some(&target), Some(&screen))
                .0
                .is_null()
        );
        let (_, begun) = current.encoder(&buffer, Some(&target), Some(&screen));
        assert_eq!(begun, Begun::Target);
        current.close();
        // Outside callbacks the target is the screen and its descriptor is there at once.
        let mut outside = Current::SCREEN;
        let pass = outside.descriptor(&buffer, Some(&screen), Some(&screen)).0;
        assert_eq!(
            attachment(pass),
            Some((screen.color, MTLLoadAction::Load, MTLStoreAction::Unknown))
        );
        outside.close();
        buffer.commit();
        buffer.waitUntilCompleted();
        assert_eq!(texels(&s.target), [[255, 0, 0, 255]; 16]);
        assert_eq!(texels(&s.screen), [[0, 255, 0, 255]; 16]);
    }
}
