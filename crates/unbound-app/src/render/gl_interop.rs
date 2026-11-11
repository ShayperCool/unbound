//! The OpenGL context native rendering plugins draw with on the OpenGL Core device
//! (`SystemInfo.graphicsDeviceType`, `IUnityGraphics::GetRenderer`), the Linux Editor's default:
//! plugins call OpenGL on the main thread with a context current and use OpenGL texture names
//! from `Texture.GetNativeTexturePtr`. The renderer draws that device with wgpu's OpenGL backend
//! (`render::device::backends`): the plugins' context is an EGL context in the renderer's share
//! group (its display and config), so the renderer's textures are the plugins' textures, by
//! name. It is current on the game thread, which also drives the renderer: the backend gives the
//! thread its context back after its own work (vendor/wgpu-sampler/PATCHES.md). Without a
//! renderer (a headless run) the context shares nothing.
//!
//! Linux only. Elsewhere [`Interop::create`] fails with the reason, and the renderer reports it.

/// A format's OpenGL internal format (`GL_RGBA8`, ...).
pub type InternalFormat = u32;

pub mod format {
    pub const RGBA8: u32 = 0x8058;
    pub const SRGB8_ALPHA8: u32 = 0x8C43;
    pub const COMPRESSED_RGB_S3TC_DXT1: u32 = 0x83F0;
    pub const COMPRESSED_RGBA_S3TC_DXT1: u32 = 0x83F1;
    pub const COMPRESSED_RGBA_S3TC_DXT3: u32 = 0x83F2;
    pub const COMPRESSED_RGBA_S3TC_DXT5: u32 = 0x83F3;
    pub const COMPRESSED_SRGB_S3TC_DXT1: u32 = 0x8C4C;
    pub const COMPRESSED_SRGB_ALPHA_S3TC_DXT1: u32 = 0x8C4D;
    pub const COMPRESSED_SRGB_ALPHA_S3TC_DXT3: u32 = 0x8C4E;
    pub const COMPRESSED_SRGB_ALPHA_S3TC_DXT5: u32 = 0x8C4F;
    pub const COMPRESSED_RGBA_BPTC_UNORM: u32 = 0x8E8C;
    pub const COMPRESSED_SRGB_ALPHA_BPTC_UNORM: u32 = 0x8E8D;
    pub const COMPRESSED_RGB_BPTC_UNSIGNED_FLOAT: u32 = 0x8E8F;
    pub const COMPRESSED_RED_RGTC1: u32 = 0x8DBB;
    pub const COMPRESSED_RG_RGTC2: u32 = 0x8DBD;
    pub const R8: u32 = 0x8229;
    pub const RG8: u32 = 0x822B;
    pub const R8_SNORM: u32 = 0x8F94;
    pub const RG8_SNORM: u32 = 0x8F95;
    pub const RGBA8_SNORM: u32 = 0x8F97;
    pub const R16: u32 = 0x822A;
    pub const RG16: u32 = 0x822C;
    pub const RGBA16: u32 = 0x805B;
    pub const RGB10_A2: u32 = 0x8059;
    pub const R11F_G11F_B10F: u32 = 0x8C3A;
    pub const RGB8: u32 = 0x8051;
    pub const SRGB8: u32 = 0x8C41;
    pub const R16F: u32 = 0x822D;
    pub const RG16F: u32 = 0x822F;
    pub const RGBA16F: u32 = 0x881A;
    pub const R32F: u32 = 0x822E;
    pub const RG32F: u32 = 0x8230;
    pub const RGBA32F: u32 = 0x8814;

    /// The sRGB format of a linear color format the device stores sRGB textures in (None: it has
    /// none, or it is sRGB already).
    pub fn srgb(format: u32) -> Option<u32> {
        Some(match format {
            RGBA8 => SRGB8_ALPHA8,
            COMPRESSED_RGB_S3TC_DXT1 => COMPRESSED_SRGB_S3TC_DXT1,
            COMPRESSED_RGBA_S3TC_DXT1 => COMPRESSED_SRGB_ALPHA_S3TC_DXT1,
            COMPRESSED_RGBA_S3TC_DXT3 => COMPRESSED_SRGB_ALPHA_S3TC_DXT3,
            COMPRESSED_RGBA_S3TC_DXT5 => COMPRESSED_SRGB_ALPHA_S3TC_DXT5,
            COMPRESSED_RGBA_BPTC_UNORM => COMPRESSED_SRGB_ALPHA_BPTC_UNORM,
            _ => return None,
        })
    }
}

/// The texture targets (`GL_TEXTURE_2D`, ...).
pub const TEXTURE_2D: u32 = 0x0DE1;
pub const TEXTURE_3D: u32 = 0x806F;
pub const TEXTURE_CUBE_MAP: u32 = 0x8513;
pub const TEXTURE_2D_ARRAY: u32 = 0x8C1A;
pub const TEXTURE_CUBE_MAP_ARRAY: u32 = 0x9009;

/// What OpenGL says of a texture name.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TextureInfo {
    /// `GL_TEXTURE_TARGET` (`GL_TEXTURE_2D`, ...).
    pub target: u32,
    /// Level 0's internal format.
    pub format: InternalFormat,
    /// Level 0's size.
    pub width: u32,
    pub height: u32,
    /// Its storage is immutable (`glTexStorage*`): it can have views.
    pub immutable: bool,
    /// Its levels: an immutable storage's, else the levels that have texels, from the first.
    pub levels: u32,
}

/// The images of a render target that is textures: a level of its color texture, of one layer
/// of it (`layer`: a cube's face, an array's layer, a 3D texture's slice) where it has several,
/// and its depth texture's first level (`stencil`: with stencil), of one layer the same way.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TargetImages {
    pub color: u32,
    pub mip: u32,
    pub layer: Option<u32>,
    pub depth: Option<(u32, bool)>,
    pub depth_layer: Option<u32>,
}

/// What a framebuffer draws into: a texture's level, or a renderbuffer.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Attachment {
    Texture { name: u32, level: u32 },
    Renderbuffer(u32),
}

/// The OpenGL error names and descriptions the Editor logs after a plugin callback that left an
/// error: "OPENGL NATIVE PLUG-IN ERROR: <this>".
pub fn error_text(error: u32) -> String {
    match error {
        0x0500 => "GL_INVALID_ENUM: enum argument out of range".into(),
        0x0501 => "GL_INVALID_VALUE: Numeric argument out of range".into(),
        0x0502 => "GL_INVALID_OPERATION: Operation illegal in current state".into(),
        0x0503 => "GL_STACK_OVERFLOW_KHR: OpenGL stack overflow".into(),
        0x0504 => "GL_STACK_UNDERFLOW_KHR: OpenGL stack underflow".into(),
        0x0505 => "GL_OUT_OF_MEMORY: Not enough memory left to execute command".into(),
        0x0506 => {
            "GL_INVALID_FRAMEBUFFER_OPERATION: Framebuffer is not complete or incompatible with command".into()
        }
        other => format!("0x{other:04X}"),
    }
}

#[cfg(target_os = "linux")]
pub use linux::Interop;

/// The renderer's EGL display and context (addresses), when it draws with OpenGL: the plugins'
/// context joins its share group.
static RENDERER: std::sync::Mutex<Option<(usize, usize)>> = std::sync::Mutex::new(None);

/// The renderer's GPU device was made: on wgpu's OpenGL backend, the plugins' context will share
/// its objects.
pub fn set_renderer(device: &wgpu::Device) {
    #[cfg(target_os = "linux")]
    {
        // SAFETY: the hal device is only read here, while the wgpu device lives; the EGL handles
        // stay valid as long as the renderer's device does (the whole run).
        let shared = unsafe { device.as_hal::<wgpu::hal::api::Gles>() }.and_then(|hal| {
            let context = hal.context();
            let display = context.raw_display()?.as_ptr() as usize;
            Some((display, context.raw_context() as usize))
        });
        *RENDERER.lock().unwrap_or_else(|e| e.into_inner()) = shared;
    }
    let _ = device;
}

/// The plugins' context, made at the first need; its failure is kept and reported once.
static INTEROP: std::sync::Mutex<Option<Result<Interop, String>>> = std::sync::Mutex::new(None);

thread_local! {
    /// This thread runs the renderer (the game's, marked by [`mark_render_thread`]): the context
    /// stays current on it, so that a plugin's OpenGL calls from scripts outside callbacks find
    /// it too, as in the Editor.
    static RENDER_THREAD: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

/// Marks this thread as the renderer's (the game's: it submits the rendering commands): the
/// context stays current on it after [`with`].
pub fn mark_render_thread() {
    RENDER_THREAD.with(|render| render.set(true));
}

/// Runs `work` with the plugins' context current on this thread; on another thread than the
/// renderer's (a plugin first called from a worker) it is released afterwards, since a context
/// is current on one thread at a time.
pub fn with<R>(work: impl FnOnce(&mut Interop) -> R) -> Result<R, String> {
    let device = unbound_runtime::graphics_device::get();
    if device != unbound_compat::GraphicsDevice::OpenGLCore {
        return Err(format!(
            "native rendering plugins on the {device} device get no OpenGL context (they use \
             the device's own plugin interface)"
        ));
    }
    let mut slot = INTEROP
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let interop = slot.get_or_insert_with(|| {
        let renderer = *RENDERER.lock().unwrap_or_else(|e| e.into_inner());
        let made = Interop::create(renderer);
        match &made {
            Ok(interop) => tracing::info!(
                "native rendering plugins draw with OpenGL {}",
                interop.version()
            ),
            Err(reason) => {
                tracing::error!("native rendering plugins get no OpenGL context: {reason}")
            }
        }
        made
    });
    let interop = interop.as_mut().map_err(|reason| reason.clone())?;
    if !interop.make_current() {
        return Err("the plugins' OpenGL context cannot be made current on this thread".into());
    }
    let result = work(interop);
    if !RENDER_THREAD.with(|render| render.get()) {
        interop.release_current();
    }
    Ok(result)
}

/// No context: [`Interop::create`] fails with the reason.
#[cfg(not(target_os = "linux"))]
pub enum Interop {}

#[cfg(not(target_os = "linux"))]
impl Interop {
    pub fn create(_renderer: Option<(usize, usize)>) -> Result<Self, String> {
        Err("the OpenGL context for native plugins is available on Linux only".into())
    }
    pub fn make_current(&self) -> bool {
        match *self {}
    }
    pub fn release_current(&self) {
        match *self {}
    }
    pub fn version(&self) -> String {
        match *self {}
    }
    pub fn texture_info(&self, _: u32) -> Option<TextureInfo> {
        match *self {}
    }
    pub fn view(
        &mut self,
        _: u32,
        _: TextureInfo,
        _: u32,
        _: InternalFormat,
        _: u32,
    ) -> Result<u32, String> {
        match *self {}
    }
    pub fn forget(&mut self, _: u32, _: bool) {
        match *self {}
    }
    pub fn bind_target(&mut self, _: Option<TargetImages>, _: [i32; 4]) {
        match *self {}
    }
    pub fn bind_attachments(&mut self, _: Attachment, _: Option<(Attachment, bool)>, _: [i32; 4]) {
        match *self {}
    }
    pub fn release_attachments(&mut self) {
        match *self {}
    }
    pub fn finish(&self) {
        match *self {}
    }
    pub fn clear_errors(&self) -> Vec<u32> {
        match *self {}
    }
}

#[cfg(target_os = "linux")]
mod linux {
    use super::{Attachment, InternalFormat, TargetImages, TextureInfo};
    use std::ffi::{CStr, c_char, c_void};

    type EglDisplay = *mut c_void;
    type EglContext = *mut c_void;
    type EglConfig = *mut c_void;
    type EglDevice = *mut c_void;
    type EglBoolean = u32;

    const EGL_NONE: i32 = 0x3038;
    const EGL_TRUE: i32 = 1;
    const EGL_CONFIG_ID: i32 = 0x3028;
    const EGL_EXTENSIONS: i32 = 0x3055;
    const EGL_OPENGL_API: u32 = 0x30A2;
    const EGL_PLATFORM_DEVICE_EXT: u32 = 0x313F;
    const EGL_CONTEXT_MAJOR_VERSION: i32 = 0x3098;
    const EGL_CONTEXT_MINOR_VERSION: i32 = 0x30FB;
    const EGL_CONTEXT_OPENGL_PROFILE_MASK: i32 = 0x30FD;
    const EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT: i32 = 0x1;
    const EGL_CONTEXT_OPENGL_ROBUST_ACCESS: i32 = 0x31B2;
    const EGL_DEFAULT_DISPLAY: *mut c_void = std::ptr::null_mut();

    const GL_TEXTURE_TARGET: u32 = 0x1006;
    const GL_TEXTURE_INTERNAL_FORMAT: u32 = 0x1003;
    const GL_TEXTURE_WIDTH: u32 = 0x1000;
    const GL_TEXTURE_HEIGHT: u32 = 0x1001;
    const GL_RENDERBUFFER: u32 = 0x8D41;
    /// More levels than any texture has.
    const MAX_LEVELS: i32 = 32;
    const GL_TEXTURE_IMMUTABLE_FORMAT: u32 = 0x912F;
    const GL_TEXTURE_IMMUTABLE_LEVELS: u32 = 0x82DF;
    const GL_FRAMEBUFFER: u32 = 0x8D40;
    const GL_COLOR_ATTACHMENT0: u32 = 0x8CE0;
    const GL_DEPTH_ATTACHMENT: u32 = 0x8D00;
    const GL_DEPTH_STENCIL_ATTACHMENT: u32 = 0x821A;
    const GL_FRAMEBUFFER_DEFAULT_WIDTH: u32 = 0x9310;
    const GL_FRAMEBUFFER_DEFAULT_HEIGHT: u32 = 0x9311;

    /// The OpenGL entry points the context uses (loaded with `eglGetProcAddress`).
    #[allow(non_snake_case)]
    struct Gl {
        GetError: unsafe extern "C" fn() -> u32,
        GetString: unsafe extern "C" fn(u32) -> *const u8,
        Finish: unsafe extern "C" fn(),
        IsTexture: unsafe extern "C" fn(u32) -> u8,
        GenTextures: unsafe extern "C" fn(i32, *mut u32),
        DeleteTextures: unsafe extern "C" fn(i32, *const u32),
        TextureView: unsafe extern "C" fn(u32, u32, u32, u32, u32, u32, u32, u32),
        GetTextureParameteriv: unsafe extern "C" fn(u32, u32, *mut i32),
        GetTextureLevelParameteriv: unsafe extern "C" fn(u32, i32, u32, *mut i32),
        CreateFramebuffers: unsafe extern "C" fn(i32, *mut u32),
        DeleteFramebuffers: unsafe extern "C" fn(i32, *const u32),
        NamedFramebufferTexture: unsafe extern "C" fn(u32, u32, u32, i32),
        NamedFramebufferTextureLayer: unsafe extern "C" fn(u32, u32, u32, i32, i32),
        NamedFramebufferRenderbuffer: unsafe extern "C" fn(u32, u32, u32, u32),
        NamedFramebufferParameteri: unsafe extern "C" fn(u32, u32, i32),
        BindFramebuffer: unsafe extern "C" fn(u32, u32),
        Viewport: unsafe extern "C" fn(i32, i32, i32, i32),
    }

    #[allow(non_snake_case)]
    struct Egl {
        _library: libloading::Library,
        GetProcAddress: unsafe extern "C" fn(*const c_char) -> *const c_void,
        GetDisplay: unsafe extern "C" fn(*mut c_void) -> EglDisplay,
        Initialize: unsafe extern "C" fn(EglDisplay, *mut i32, *mut i32) -> EglBoolean,
        Terminate: unsafe extern "C" fn(EglDisplay) -> EglBoolean,
        BindAPI: unsafe extern "C" fn(u32) -> EglBoolean,
        ChooseConfig: unsafe extern "C" fn(
            EglDisplay,
            *const i32,
            *mut EglConfig,
            i32,
            *mut i32,
        ) -> EglBoolean,
        QueryContext: unsafe extern "C" fn(EglDisplay, EglContext, i32, *mut i32) -> EglBoolean,
        CreateContext:
            unsafe extern "C" fn(EglDisplay, EglConfig, EglContext, *const i32) -> EglContext,
        DestroyContext: unsafe extern "C" fn(EglDisplay, EglContext) -> EglBoolean,
        MakeCurrent:
            unsafe extern "C" fn(EglDisplay, *mut c_void, *mut c_void, EglContext) -> EglBoolean,
        GetCurrentContext: unsafe extern "C" fn() -> EglContext,
        QueryString: unsafe extern "C" fn(EglDisplay, i32) -> *const c_char,
    }

    /// The plugins' OpenGL context and what it made.
    pub struct Interop {
        egl: Egl,
        gl: Gl,
        display: EglDisplay,
        context: EglContext,
        /// The display was initialized here (a context sharing nothing): terminated with it.
        own_display: bool,
        /// The framebuffer bound where the plugin finds no shared target: no attachments, but
        /// complete (the viewport's size): what the plugin draws there is discarded.
        empty_framebuffer: u32,
        /// Framebuffers of targets, by their images.
        framebuffers: Vec<(TargetImages, u32)>,
        /// The framebuffer of attachments bound for one callback ([`Interop::bind_attachments`]):
        /// they are detached after it, so it keeps none of them alive.
        attachments_framebuffer: u32,
    }

    // SAFETY: the context is only used by the thread that makes it current (the renderer's
    // caller, the game's thread); the handles are plain values.
    unsafe impl Send for Interop {}

    fn sym<T: Copy>(library: &libloading::Library, name: &[u8]) -> Result<T, String> {
        unsafe { library.get::<T>(name) }
            .map(|s| *s)
            .map_err(|e| format!("libEGL lacks {}: {e}", String::from_utf8_lossy(name)))
    }

    impl Egl {
        fn load() -> Result<Egl, String> {
            let library = unsafe { libloading::Library::new("libEGL.so.1") }
                .map_err(|e| format!("libEGL.so.1 cannot be loaded: {e}"))?;
            Ok(Egl {
                GetProcAddress: sym(&library, b"eglGetProcAddress\0")?,
                GetDisplay: sym(&library, b"eglGetDisplay\0")?,
                Initialize: sym(&library, b"eglInitialize\0")?,
                Terminate: sym(&library, b"eglTerminate\0")?,
                BindAPI: sym(&library, b"eglBindAPI\0")?,
                ChooseConfig: sym(&library, b"eglChooseConfig\0")?,
                QueryContext: sym(&library, b"eglQueryContext\0")?,
                CreateContext: sym(&library, b"eglCreateContext\0")?,
                DestroyContext: sym(&library, b"eglDestroyContext\0")?,
                MakeCurrent: sym(&library, b"eglMakeCurrent\0")?,
                GetCurrentContext: sym(&library, b"eglGetCurrentContext\0")?,
                QueryString: sym(&library, b"eglQueryString\0")?,
                _library: library,
            })
        }

        fn proc(&self, name: &CStr) -> *const c_void {
            unsafe { (self.GetProcAddress)(name.as_ptr()) }
        }

        /// The displays to try for a context sharing nothing: every EGL device's
        /// (`EGL_EXT_device_enumeration`), else the default display.
        fn displays(&self) -> Vec<EglDisplay> {
            let mut displays = Vec::new();
            let query = self.proc(c"eglQueryDevicesEXT");
            let platform = self.proc(c"eglGetPlatformDisplayEXT");
            if !query.is_null() && !platform.is_null() {
                let query: unsafe extern "C" fn(i32, *mut EglDevice, *mut i32) -> EglBoolean =
                    unsafe { std::mem::transmute(query) };
                let platform: unsafe extern "C" fn(u32, *mut c_void, *const i32) -> EglDisplay =
                    unsafe { std::mem::transmute(platform) };
                let mut devices = [std::ptr::null_mut(); 16];
                let mut count = 0;
                if unsafe { query(devices.len() as i32, devices.as_mut_ptr(), &mut count) } != 0 {
                    for device in &devices[..count.max(0) as usize] {
                        let display = unsafe {
                            platform(EGL_PLATFORM_DEVICE_EXT, *device, [EGL_NONE].as_ptr())
                        };
                        if !display.is_null() {
                            displays.push(display);
                        }
                    }
                }
            }
            let default = unsafe { (self.GetDisplay)(EGL_DEFAULT_DISPLAY) };
            if !default.is_null() {
                displays.push(default);
            }
            displays
        }

        fn has_extension(&self, display: EglDisplay, wanted: &str) -> bool {
            let extensions = unsafe { (self.QueryString)(display, EGL_EXTENSIONS) };
            !extensions.is_null()
                && unsafe { CStr::from_ptr(extensions) }
                    .to_string_lossy()
                    .split(' ')
                    .any(|e| e == wanted)
        }

        /// A 4.5 core context on `display` with `config`, sharing `share`'s objects (null:
        /// none); robust access first, as the renderer's context has it where EGL offers it
        /// (contexts of one share group must agree).
        fn create_context(
            &self,
            display: EglDisplay,
            config: EglConfig,
            share: EglContext,
        ) -> Option<EglContext> {
            if unsafe { (self.BindAPI)(EGL_OPENGL_API) } == 0 {
                return None;
            }
            [true, false].into_iter().find_map(|robust| {
                let mut attributes = vec![
                    EGL_CONTEXT_MAJOR_VERSION,
                    4,
                    EGL_CONTEXT_MINOR_VERSION,
                    5,
                    EGL_CONTEXT_OPENGL_PROFILE_MASK,
                    EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT,
                ];
                if robust {
                    attributes.extend([EGL_CONTEXT_OPENGL_ROBUST_ACCESS, EGL_TRUE]);
                }
                attributes.push(EGL_NONE);
                let context =
                    unsafe { (self.CreateContext)(display, config, share, attributes.as_ptr()) };
                (!context.is_null()).then_some(context)
            })
        }
    }

    macro_rules! gl_fn {
        ($egl:expr, $name:literal) => {{
            let address = $egl.proc(unsafe {
                CStr::from_bytes_with_nul_unchecked(concat!($name, "\0").as_bytes())
            });
            if address.is_null() {
                return Err(format!("OpenGL lacks {}", $name));
            }
            unsafe { std::mem::transmute(address) }
        }};
    }

    impl Gl {
        fn load(egl: &Egl) -> Result<Gl, String> {
            Ok(Gl {
                GetError: gl_fn!(egl, "glGetError"),
                GetString: gl_fn!(egl, "glGetString"),
                Finish: gl_fn!(egl, "glFinish"),
                IsTexture: gl_fn!(egl, "glIsTexture"),
                GenTextures: gl_fn!(egl, "glGenTextures"),
                DeleteTextures: gl_fn!(egl, "glDeleteTextures"),
                TextureView: gl_fn!(egl, "glTextureView"),
                GetTextureParameteriv: gl_fn!(egl, "glGetTextureParameteriv"),
                GetTextureLevelParameteriv: gl_fn!(egl, "glGetTextureLevelParameteriv"),
                CreateFramebuffers: gl_fn!(egl, "glCreateFramebuffers"),
                DeleteFramebuffers: gl_fn!(egl, "glDeleteFramebuffers"),
                NamedFramebufferTexture: gl_fn!(egl, "glNamedFramebufferTexture"),
                NamedFramebufferTextureLayer: gl_fn!(egl, "glNamedFramebufferTextureLayer"),
                NamedFramebufferRenderbuffer: gl_fn!(egl, "glNamedFramebufferRenderbuffer"),
                NamedFramebufferParameteri: gl_fn!(egl, "glNamedFramebufferParameteri"),
                BindFramebuffer: gl_fn!(egl, "glBindFramebuffer"),
                Viewport: gl_fn!(egl, "glViewport"),
            })
        }
    }

    impl Interop {
        /// An OpenGL 4.5 core context in the renderer's share group (`renderer`: its EGL display
        /// and context), else sharing nothing on the first display that makes one; it is current
        /// on this thread afterwards.
        pub fn create(renderer: Option<(usize, usize)>) -> Result<Interop, String> {
            let egl = Egl::load()?;
            let made = match renderer {
                Some((display, context)) => {
                    Self::sharing(&egl, display as EglDisplay, context as EglContext)
                        .map(|context| (display as EglDisplay, context, false))
                }
                None => Self::alone(&egl).map(|(display, context)| (display, context, true)),
            };
            let (display, context, own_display) = made?;
            let none = std::ptr::null_mut();
            let current = unsafe { (egl.MakeCurrent)(display, none, none, context) } != 0;
            let gl = if current {
                Gl::load(&egl)
            } else {
                Err("the plugins' OpenGL context cannot be made current".into())
            };
            let gl = match gl {
                Ok(gl) => gl,
                Err(reason) => {
                    unsafe {
                        (egl.MakeCurrent)(display, none, none, none);
                        (egl.DestroyContext)(display, context);
                        if own_display {
                            (egl.Terminate)(display);
                        }
                    }
                    return Err(reason);
                }
            };
            let mut interop = Interop {
                egl,
                gl,
                display,
                context,
                own_display,
                empty_framebuffer: 0,
                framebuffers: Vec::new(),
                attachments_framebuffer: 0,
            };
            unsafe {
                (interop.gl.CreateFramebuffers)(1, &mut interop.empty_framebuffer);
                (interop.gl.CreateFramebuffers)(1, &mut interop.attachments_framebuffer);
            }
            Ok(interop)
        }

        /// A context sharing the renderer's objects, with its config.
        fn sharing(
            egl: &Egl,
            display: EglDisplay,
            renderer: EglContext,
        ) -> Result<EglContext, String> {
            let mut id = 0;
            if unsafe { (egl.QueryContext)(display, renderer, EGL_CONFIG_ID, &mut id) } == 0 {
                return Err("the renderer's EGL context has no config".into());
            }
            let wanted = [EGL_CONFIG_ID, id, EGL_NONE];
            let mut config = std::ptr::null_mut();
            let mut count = 0;
            if unsafe { (egl.ChooseConfig)(display, wanted.as_ptr(), &mut config, 1, &mut count) }
                == 0
                || count < 1
            {
                return Err("the renderer's EGL config cannot be found".into());
            }
            egl.create_context(display, config, renderer)
                .ok_or_else(|| "no OpenGL 4.5 core context can share the renderer's objects".into())
        }

        /// A context sharing nothing (no renderer), on the first display that makes one.
        fn alone(egl: &Egl) -> Result<(EglDisplay, EglContext), String> {
            let mut reasons = Vec::new();
            for display in egl.displays() {
                let (mut major, mut minor) = (0, 0);
                if unsafe { (egl.Initialize)(display, &mut major, &mut minor) } == 0 {
                    reasons.push("an EGL display does not initialize");
                    continue;
                }
                if !egl.has_extension(display, "EGL_KHR_surfaceless_context")
                    || !egl.has_extension(display, "EGL_KHR_no_config_context")
                {
                    reasons.push("an EGL display lacks surfaceless contexts without a config");
                } else if let Some(context) =
                    egl.create_context(display, std::ptr::null_mut(), std::ptr::null_mut())
                {
                    return Ok((display, context));
                } else {
                    reasons.push("an EGL display makes no OpenGL 4.5 core context");
                }
                unsafe { (egl.Terminate)(display) };
            }
            Err(format!(
                "no OpenGL context ({})",
                if reasons.is_empty() {
                    "no EGL display".to_string()
                } else {
                    reasons.join("; ")
                }
            ))
        }

        /// Makes the context current on this thread (a no-op when it is), with desktop OpenGL
        /// the thread's client API.
        pub fn make_current(&self) -> bool {
            if unsafe { (self.egl.BindAPI)(EGL_OPENGL_API) } == 0 {
                return false;
            }
            if unsafe { (self.egl.GetCurrentContext)() } == self.context {
                return true;
            }
            let none = std::ptr::null_mut();
            unsafe { (self.egl.MakeCurrent)(self.display, none, none, self.context) != 0 }
        }

        /// Leaves this thread without the context (another thread may make it current).
        pub fn release_current(&self) {
            let none = std::ptr::null_mut();
            unsafe { (self.egl.MakeCurrent)(self.display, none, none, none) };
        }

        /// The OpenGL version string (for the log).
        pub fn version(&self) -> String {
            let version = unsafe { (self.gl.GetString)(0x1F02) };
            if version.is_null() {
                return String::new();
            }
            unsafe { CStr::from_ptr(version as *const c_char) }
                .to_string_lossy()
                .into_owned()
        }

        /// What OpenGL says of a texture name (None: not a texture of the share group).
        pub fn texture_info(&self, name: u32) -> Option<TextureInfo> {
            if name == 0 || unsafe { (self.gl.IsTexture)(name) } == 0 {
                return None;
            }
            let gl = &self.gl;
            let parameter = |p| {
                let mut value = 0;
                unsafe { (gl.GetTextureParameteriv)(name, p, &mut value) };
                value
            };
            let level = |level, p| {
                let mut value = 0;
                unsafe { (gl.GetTextureLevelParameteriv)(name, level, p, &mut value) };
                value
            };
            let immutable = parameter(GL_TEXTURE_IMMUTABLE_FORMAT) != 0;
            let levels = if immutable {
                parameter(GL_TEXTURE_IMMUTABLE_LEVELS)
            } else {
                // A level past the last one has no size (asking for one past the largest the
                // context has leaves an error, cleared here).
                let defined = (1..MAX_LEVELS)
                    .find(|&l| level(l, GL_TEXTURE_WIDTH) == 0)
                    .unwrap_or(MAX_LEVELS);
                self.clear_errors();
                defined
            };
            Some(TextureInfo {
                target: parameter(GL_TEXTURE_TARGET) as u32,
                format: level(0, GL_TEXTURE_INTERNAL_FORMAT) as u32,
                width: level(0, GL_TEXTURE_WIDTH).max(0) as u32,
                height: level(0, GL_TEXTURE_HEIGHT).max(0) as u32,
                immutable,
                levels: levels.max(1) as u32,
            })
        }

        /// A texture over all of `name`'s levels and its `layers` layers (a cube's six faces,
        /// an array's layers, else 1) with another (compatible) target or format.
        pub fn view(
            &mut self,
            name: u32,
            info: TextureInfo,
            target: u32,
            format: InternalFormat,
            layers: u32,
        ) -> Result<u32, String> {
            if !info.immutable {
                return Err(format!(
                    "texture {name} has no views (only textures with immutable storage do)"
                ));
            }
            self.clear_errors();
            let mut view = 0;
            unsafe {
                (self.gl.GenTextures)(1, &mut view);
                (self.gl.TextureView)(view, target, name, format, 0, info.levels, 0, layers);
            }
            let errors = self.clear_errors();
            if errors.is_empty() {
                Ok(view)
            } else {
                unsafe { (self.gl.DeleteTextures)(1, &view) };
                Err(format!(
                    "OpenGL refused a view of texture {name}: {}",
                    errors
                        .iter()
                        .map(|e| super::error_text(*e))
                        .collect::<Vec<_>>()
                        .join(", ")
                ))
            }
        }

        /// A texture name the renderer no longer uses for plugins: the framebuffers made of it
        /// go, and the texture too when it is a view made here (`owned`).
        pub fn forget(&mut self, name: u32, owned: bool) {
            let gl = &self.gl;
            self.framebuffers.retain(|(images, framebuffer)| {
                let uses = images.color == name || images.depth.is_some_and(|d| d.0 == name);
                if uses {
                    unsafe { (gl.DeleteFramebuffers)(1, framebuffer) };
                }
                !uses
            });
            if owned {
                unsafe { (gl.DeleteTextures)(1, &name) };
            }
        }

        /// Binds the framebuffer of a target's images, or one without attachments of the
        /// viewport's size, with the viewport (pixels, origin at the first row).
        pub fn bind_target(&mut self, images: Option<TargetImages>, viewport: [i32; 4]) {
            let gl = &self.gl;
            let framebuffer = match images {
                None => {
                    for (parameter, size) in [
                        (GL_FRAMEBUFFER_DEFAULT_WIDTH, viewport[0] + viewport[2]),
                        (GL_FRAMEBUFFER_DEFAULT_HEIGHT, viewport[1] + viewport[3]),
                    ] {
                        unsafe {
                            (gl.NamedFramebufferParameteri)(
                                self.empty_framebuffer,
                                parameter,
                                size.max(1),
                            )
                        };
                    }
                    self.empty_framebuffer
                }
                Some(images) => match self.framebuffers.iter().find(|(k, _)| *k == images) {
                    Some((_, framebuffer)) => *framebuffer,
                    None => {
                        let mut framebuffer = 0;
                        unsafe { (gl.CreateFramebuffers)(1, &mut framebuffer) };
                        let framebuffer = framebuffer;
                        // One layer of a texture that has several, else the texture's level.
                        let attach = |point, name, level: u32, layer: Option<u32>| unsafe {
                            match layer {
                                Some(layer) => (gl.NamedFramebufferTextureLayer)(
                                    framebuffer,
                                    point,
                                    name,
                                    level as i32,
                                    layer as i32,
                                ),
                                None => (gl.NamedFramebufferTexture)(
                                    framebuffer,
                                    point,
                                    name,
                                    level as i32,
                                ),
                            }
                        };
                        attach(GL_COLOR_ATTACHMENT0, images.color, images.mip, images.layer);
                        if let Some((depth, stencil)) = images.depth {
                            let point = if stencil {
                                GL_DEPTH_STENCIL_ATTACHMENT
                            } else {
                                GL_DEPTH_ATTACHMENT
                            };
                            attach(point, depth, 0, images.depth_layer);
                        }
                        self.framebuffers.push((images, framebuffer));
                        framebuffer
                    }
                },
            };
            unsafe {
                (gl.BindFramebuffer)(GL_FRAMEBUFFER, framebuffer);
                (gl.Viewport)(viewport[0], viewport[1], viewport[2], viewport[3]);
            }
        }

        /// Binds a framebuffer of these attachments (a multisampled target's), with the
        /// viewport; [`Interop::release_attachments`] detaches them after the callback.
        pub fn bind_attachments(
            &mut self,
            color: Attachment,
            depth: Option<(Attachment, bool)>,
            viewport: [i32; 4],
        ) {
            let framebuffer = self.attachments_framebuffer;
            self.attach(GL_COLOR_ATTACHMENT0, Some(color));
            // The depth and stencil point is both points: cleared first, then the one given.
            self.attach(GL_DEPTH_STENCIL_ATTACHMENT, None);
            if let Some((depth, stencil)) = depth {
                let point = if stencil {
                    GL_DEPTH_STENCIL_ATTACHMENT
                } else {
                    GL_DEPTH_ATTACHMENT
                };
                self.attach(point, Some(depth));
            }
            unsafe {
                (self.gl.BindFramebuffer)(GL_FRAMEBUFFER, framebuffer);
                (self.gl.Viewport)(viewport[0], viewport[1], viewport[2], viewport[3]);
            }
        }

        /// Detaches what [`Interop::bind_attachments`] attached.
        pub fn release_attachments(&mut self) {
            for point in [GL_COLOR_ATTACHMENT0, GL_DEPTH_STENCIL_ATTACHMENT] {
                self.attach(point, None);
            }
        }

        fn attach(&self, point: u32, attachment: Option<Attachment>) {
            let (gl, framebuffer) = (&self.gl, self.attachments_framebuffer);
            unsafe {
                match attachment {
                    Some(Attachment::Texture { name, level }) => {
                        (gl.NamedFramebufferTexture)(framebuffer, point, name, level as i32)
                    }
                    Some(Attachment::Renderbuffer(name)) => {
                        (gl.NamedFramebufferRenderbuffer)(framebuffer, point, GL_RENDERBUFFER, name)
                    }
                    None => (gl.NamedFramebufferTexture)(framebuffer, point, 0, 0),
                }
            }
        }

        /// Waits until OpenGL has done everything asked of it.
        pub fn finish(&self) {
            unsafe { (self.gl.Finish)() };
        }

        /// The errors OpenGL recorded since the last call, clearing them.
        pub fn clear_errors(&self) -> Vec<u32> {
            let mut errors = Vec::new();
            // OpenGL keeps at most one flag per error kind.
            for _ in 0..8 {
                let error = unsafe { (self.gl.GetError)() };
                if error == 0 {
                    break;
                }
                errors.push(error);
            }
            errors
        }
    }

    impl Drop for Interop {
        fn drop(&mut self) {
            let none = std::ptr::null_mut();
            unsafe {
                (self.egl.MakeCurrent)(self.display, none, none, none);
                (self.egl.DestroyContext)(self.display, self.context);
                if self.own_display {
                    (self.egl.Terminate)(self.display);
                }
            }
        }
    }
}

#[cfg(all(test, target_os = "linux"))]
mod tests {
    #[test]
    fn the_context_stays_only_with_the_render_thread() {
        // Needs a GPU with EGL; without one there is no context to hand around.
        if super::with(|_| ()).is_err() {
            return;
        }
        // Unmarked threads (this one, a worker that loads a plugin) release it after use...
        let worker = std::thread::spawn(|| super::with(|_| ()).is_ok());
        assert!(worker.join().unwrap());
        // ...so the render thread can take it, and keeps it.
        let render = std::thread::spawn(|| {
            super::mark_render_thread();
            let current = super::with(|interop| interop.make_current()).unwrap();
            // A worker cannot take it from the render thread now: it fails cleanly.
            let worker = std::thread::spawn(|| super::with(|_| ()).is_err());
            (current, worker.join().unwrap())
        });
        assert_eq!(render.join().unwrap(), (true, true));
    }

    #[test]
    fn srgb_counterparts_of_the_stored_formats() {
        use super::format::*;
        assert_eq!(srgb(RGBA8), Some(SRGB8_ALPHA8));
        assert_eq!(
            srgb(COMPRESSED_RGBA_BPTC_UNORM),
            Some(COMPRESSED_SRGB_ALPHA_BPTC_UNORM)
        );
        assert_eq!(srgb(SRGB8_ALPHA8), None);
    }
}
