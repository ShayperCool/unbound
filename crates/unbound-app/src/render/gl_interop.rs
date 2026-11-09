//! The OpenGL context native rendering plugins draw with. Unbound reports OpenGL Core
//! (`SystemInfo.graphicsDeviceType`, `IUnityGraphics::GetRenderer`), as the Linux Editor does, so
//! plugins call OpenGL on the main thread with a context current and use OpenGL texture names
//! from `Texture.GetNativeTexturePtr`. The renderer draws with Vulkan: this module makes an
//! OpenGL 4.5 core context on the renderer's GPU (EGL, no window surface) and imports the
//! memory of the renderer's textures into OpenGL textures (`GL_EXT_memory_object_fd`), so both
//! see the same texels. The renderer finishes its work before a callback and OpenGL finishes the
//! plugin's before the renderer goes on (`glFinish`): rendering callbacks are rare and the
//! Editor's run synchronously on the main thread too (tests/projects/nativegfx).
//!
//! Linux only. Elsewhere [`Interop::create`] fails with the reason, and the renderer reports it.
#![cfg_attr(not(target_os = "linux"), allow(dead_code))]

/// An OpenGL texture made over memory the renderer exported.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct GlTexture {
    pub name: u32,
    pub memory: u32,
}

/// A format's OpenGL internal format (`GL_RGBA8`, ...).
pub type InternalFormat = u32;

pub mod format {
    pub const RGBA8: u32 = 0x8058;
    pub const SRGB8_ALPHA8: u32 = 0x8C43;
    pub const RGBA16F: u32 = 0x881A;
    pub const RGBA32F: u32 = 0x8814;
    pub const R8: u32 = 0x8229;
    pub const RG8: u32 = 0x822B;
    pub const R16F: u32 = 0x822D;
    pub const RG16F: u32 = 0x822F;
    pub const R32F: u32 = 0x822E;
    pub const RG32F: u32 = 0x8230;
    pub const R16: u32 = 0x822A;
    pub const RG16: u32 = 0x822C;
    pub const RGBA16: u32 = 0x805B;
    pub const RGB10_A2: u32 = 0x8059;
    pub const R11F_G11F_B10F: u32 = 0x8C3A;
    pub const DEPTH24_STENCIL8: u32 = 0x88F0;
    pub const DEPTH32F_STENCIL8: u32 = 0x8CAD;
    pub const DEPTH_COMPONENT32F: u32 = 0x8CAC;
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

/// The plugins' context, made at the first need (by the renderer, on its GPU, or before it is
/// up, on the first GPU that can import memory); its failure is kept and reported once.
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
/// is current on one thread at a time. `device_uuid`: the renderer's GPU, which the context must
/// draw on.
pub fn with<R>(
    device_uuid: Option<[u8; 16]>,
    work: impl FnOnce(&mut Interop) -> R,
) -> Result<R, String> {
    let mut slot = INTEROP
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    let interop = slot.get_or_insert_with(|| {
        let made = Interop::create(device_uuid);
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
    if device_uuid.is_some_and(|uuid| uuid != interop.device_uuid()) {
        return Err("the plugins' OpenGL context is not on the renderer's GPU".into());
    }
    if !interop.make_current() {
        return Err("the plugins' OpenGL context cannot be made current on this thread".into());
    }
    let result = work(interop);
    if !RENDER_THREAD.with(|render| render.get()) {
        interop.release_current();
    }
    Ok(result)
}

/// No interop: [`Interop::create`] fails with the reason.
#[cfg(not(target_os = "linux"))]
pub enum Interop {}

#[cfg(not(target_os = "linux"))]
#[allow(clippy::too_many_arguments)]
impl Interop {
    pub fn create(_device_uuid: Option<[u8; 16]>) -> Result<Self, String> {
        Err("the OpenGL interop for native plugins is available on Linux only".into())
    }
    pub fn device_uuid(&self) -> [u8; 16] {
        match *self {}
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
    pub fn import(
        &mut self,
        _: std::os::fd::OwnedFd,
        _: u64,
        _: u32,
        _: u32,
        _: u32,
        _: u32,
        _: i32,
        _: [i32; 2],
    ) -> Result<GlTexture, String> {
        match *self {}
    }
    pub fn delete(&mut self, _: GlTexture) {
        match *self {}
    }
    pub fn is_texture(&self, _: u32) -> bool {
        match *self {}
    }
    pub fn bind_target(&mut self, _: Option<(u32, u32)>, _: Option<(u32, bool)>, _: [i32; 4]) {
        match *self {}
    }
    pub fn copy(&self, _: u32, _: u32, _: u32, _: u32) {
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
    use super::GlTexture;
    use std::ffi::{CStr, c_char, c_void};
    use std::os::fd::{FromRawFd, IntoRawFd, OwnedFd};

    type EglDisplay = *mut c_void;
    type EglContext = *mut c_void;
    type EglDevice = *mut c_void;
    type EglBoolean = u32;

    const EGL_NONE: i32 = 0x3038;
    const EGL_OPENGL_API: u32 = 0x30A2;
    const EGL_PLATFORM_DEVICE_EXT: u32 = 0x313F;
    const EGL_CONTEXT_MAJOR_VERSION: i32 = 0x3098;
    const EGL_CONTEXT_MINOR_VERSION: i32 = 0x30FB;
    const EGL_CONTEXT_OPENGL_PROFILE_MASK: i32 = 0x30FD;
    const EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT: i32 = 0x1;
    const EGL_DEFAULT_DISPLAY: *mut c_void = std::ptr::null_mut();

    const GL_EXTENSIONS: u32 = 0x1F03;
    const GL_NUM_EXTENSIONS: u32 = 0x821D;
    const GL_DEVICE_UUID_EXT: u32 = 0x9597;
    const GL_TEXTURE_2D: u32 = 0x0DE1;
    const GL_TEXTURE_TILING_EXT: u32 = 0x9580;
    const GL_OPTIMAL_TILING_EXT: u32 = 0x9584;
    const GL_DEDICATED_MEMORY_OBJECT_EXT: u32 = 0x9581;
    const GL_HANDLE_TYPE_OPAQUE_FD_EXT: u32 = 0x9586;
    const GL_TEXTURE_MIN_FILTER: u32 = 0x2801;
    const GL_TEXTURE_MAG_FILTER: u32 = 0x2800;
    const GL_TEXTURE_WRAP_S: u32 = 0x2802;
    const GL_TEXTURE_WRAP_T: u32 = 0x2803;
    const GL_NEAREST: i32 = 0x2600;
    const GL_LINEAR: i32 = 0x2601;
    const GL_NEAREST_MIPMAP_NEAREST: i32 = 0x2700;
    const GL_LINEAR_MIPMAP_LINEAR: i32 = 0x2703;
    const GL_REPEAT: i32 = 0x2901;
    const GL_CLAMP_TO_EDGE: i32 = 0x812F;
    const GL_MIRRORED_REPEAT: i32 = 0x8370;
    const GL_FRAMEBUFFER: u32 = 0x8D40;
    const GL_COLOR_ATTACHMENT0: u32 = 0x8CE0;
    const GL_DEPTH_ATTACHMENT: u32 = 0x8D00;
    const GL_DEPTH_STENCIL_ATTACHMENT: u32 = 0x821A;
    const GL_FRAMEBUFFER_DEFAULT_WIDTH: u32 = 0x9310;
    const GL_FRAMEBUFFER_DEFAULT_HEIGHT: u32 = 0x9311;

    /// The OpenGL entry points the interop uses (loaded with `eglGetProcAddress`).
    #[allow(non_snake_case)]
    struct Gl {
        GetError: unsafe extern "C" fn() -> u32,
        GetString: unsafe extern "C" fn(u32) -> *const u8,
        GetStringi: unsafe extern "C" fn(u32, u32) -> *const u8,
        GetIntegerv: unsafe extern "C" fn(u32, *mut i32),
        GetUnsignedBytevEXT: unsafe extern "C" fn(u32, *mut u8),
        Finish: unsafe extern "C" fn(),
        IsTexture: unsafe extern "C" fn(u32) -> u8,
        CreateTextures: unsafe extern "C" fn(u32, i32, *mut u32),
        DeleteTextures: unsafe extern "C" fn(i32, *const u32),
        TextureParameteri: unsafe extern "C" fn(u32, u32, i32),
        CreateMemoryObjectsEXT: unsafe extern "C" fn(i32, *mut u32),
        DeleteMemoryObjectsEXT: unsafe extern "C" fn(i32, *const u32),
        MemoryObjectParameterivEXT: unsafe extern "C" fn(u32, u32, *const i32),
        ImportMemoryFdEXT: unsafe extern "C" fn(u32, u64, u32, i32),
        TextureStorageMem2DEXT: unsafe extern "C" fn(u32, i32, u32, i32, i32, u32, u64),
        CreateFramebuffers: unsafe extern "C" fn(i32, *mut u32),
        DeleteFramebuffers: unsafe extern "C" fn(i32, *const u32),
        NamedFramebufferTexture: unsafe extern "C" fn(u32, u32, u32, i32),
        NamedFramebufferParameteri: unsafe extern "C" fn(u32, u32, i32),
        BindFramebuffer: unsafe extern "C" fn(u32, u32),
        Viewport: unsafe extern "C" fn(i32, i32, i32, i32),
        CopyImageSubData: unsafe extern "C" fn(
            u32,
            u32,
            i32,
            i32,
            i32,
            i32,
            u32,
            u32,
            i32,
            i32,
            i32,
            i32,
            i32,
            i32,
            i32,
        ),
    }

    #[allow(non_snake_case)]
    struct Egl {
        _library: libloading::Library,
        GetProcAddress: unsafe extern "C" fn(*const c_char) -> *const c_void,
        GetDisplay: unsafe extern "C" fn(*mut c_void) -> EglDisplay,
        Initialize: unsafe extern "C" fn(EglDisplay, *mut i32, *mut i32) -> EglBoolean,
        Terminate: unsafe extern "C" fn(EglDisplay) -> EglBoolean,
        BindAPI: unsafe extern "C" fn(u32) -> EglBoolean,
        CreateContext:
            unsafe extern "C" fn(EglDisplay, *mut c_void, EglContext, *const i32) -> EglContext,
        DestroyContext: unsafe extern "C" fn(EglDisplay, EglContext) -> EglBoolean,
        MakeCurrent:
            unsafe extern "C" fn(EglDisplay, *mut c_void, *mut c_void, EglContext) -> EglBoolean,
        GetCurrentContext: unsafe extern "C" fn() -> EglContext,
        QueryString: unsafe extern "C" fn(EglDisplay, i32) -> *const c_char,
    }

    const EGL_EXTENSIONS: i32 = 0x3055;

    /// The plugins' OpenGL context and what it made.
    pub struct Interop {
        egl: Egl,
        gl: Gl,
        display: EglDisplay,
        context: EglContext,
        /// The device the context draws on (`GL_DEVICE_UUID_EXT`).
        device_uuid: [u8; 16],
        /// The framebuffer bound where the plugin finds no shared target: no attachments, but
        /// complete (the viewport's size): what the plugin draws there is discarded.
        empty_framebuffer: u32,
        /// Framebuffers of shared targets, by color, depth and mip.
        framebuffers: Vec<((u32, u32, u32), u32)>,
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

        /// The displays to try: every EGL device's (`EGL_EXT_device_enumeration`), else the
        /// default display.
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
                GetStringi: gl_fn!(egl, "glGetStringi"),
                GetIntegerv: gl_fn!(egl, "glGetIntegerv"),
                GetUnsignedBytevEXT: gl_fn!(egl, "glGetUnsignedBytevEXT"),
                Finish: gl_fn!(egl, "glFinish"),
                IsTexture: gl_fn!(egl, "glIsTexture"),
                CreateTextures: gl_fn!(egl, "glCreateTextures"),
                DeleteTextures: gl_fn!(egl, "glDeleteTextures"),
                TextureParameteri: gl_fn!(egl, "glTextureParameteri"),
                CreateMemoryObjectsEXT: gl_fn!(egl, "glCreateMemoryObjectsEXT"),
                DeleteMemoryObjectsEXT: gl_fn!(egl, "glDeleteMemoryObjectsEXT"),
                MemoryObjectParameterivEXT: gl_fn!(egl, "glMemoryObjectParameterivEXT"),
                ImportMemoryFdEXT: gl_fn!(egl, "glImportMemoryFdEXT"),
                TextureStorageMem2DEXT: gl_fn!(egl, "glTextureStorageMem2DEXT"),
                CreateFramebuffers: gl_fn!(egl, "glCreateFramebuffers"),
                DeleteFramebuffers: gl_fn!(egl, "glDeleteFramebuffers"),
                NamedFramebufferTexture: gl_fn!(egl, "glNamedFramebufferTexture"),
                NamedFramebufferParameteri: gl_fn!(egl, "glNamedFramebufferParameteri"),
                BindFramebuffer: gl_fn!(egl, "glBindFramebuffer"),
                Viewport: gl_fn!(egl, "glViewport"),
                CopyImageSubData: gl_fn!(egl, "glCopyImageSubData"),
            })
        }

        fn has_extension(&self, wanted: &str) -> bool {
            let mut count = 0;
            unsafe { (self.GetIntegerv)(GL_NUM_EXTENSIONS, &mut count) };
            (0..count.max(0) as u32).any(|i| {
                let name = unsafe { (self.GetStringi)(GL_EXTENSIONS, i) };
                !name.is_null()
                    && unsafe { CStr::from_ptr(name as *const c_char) }.to_bytes()
                        == wanted.as_bytes()
            })
        }
    }

    impl Interop {
        /// An OpenGL 4.5 core context on the GPU with `device_uuid` (the renderer's), else on the
        /// first that can import memory; it is current on this thread afterwards.
        pub fn create(device_uuid: Option<[u8; 16]>) -> Result<Interop, String> {
            let egl = Egl::load()?;
            let mut reasons = Vec::new();
            for display in egl.displays() {
                match Self::on_display(&egl, display, device_uuid) {
                    Ok((gl, context, uuid)) => {
                        let mut interop = Interop {
                            egl,
                            gl,
                            display,
                            context,
                            device_uuid: uuid,
                            empty_framebuffer: 0,
                            framebuffers: Vec::new(),
                        };
                        unsafe {
                            (interop.gl.CreateFramebuffers)(1, &mut interop.empty_framebuffer)
                        };
                        return Ok(interop);
                    }
                    Err(reason) => reasons.push(reason),
                }
            }
            Err(format!(
                "no OpenGL context can share the renderer's textures ({})",
                if reasons.is_empty() {
                    "no EGL display".to_string()
                } else {
                    reasons.join("; ")
                }
            ))
        }

        fn on_display(
            egl: &Egl,
            display: EglDisplay,
            device_uuid: Option<[u8; 16]>,
        ) -> Result<(Gl, EglContext, [u8; 16]), String> {
            let (mut major, mut minor) = (0, 0);
            if unsafe { (egl.Initialize)(display, &mut major, &mut minor) } == 0 {
                return Err("an EGL display does not initialize".into());
            }
            let extensions = unsafe { (egl.QueryString)(display, EGL_EXTENSIONS) };
            let extensions = if extensions.is_null() {
                String::new()
            } else {
                unsafe { CStr::from_ptr(extensions) }
                    .to_string_lossy()
                    .into_owned()
            };
            let fail = |reason: String| {
                unsafe { (egl.Terminate)(display) };
                Err(reason)
            };
            if !extensions
                .split(' ')
                .any(|e| e == "EGL_KHR_surfaceless_context")
                || !extensions
                    .split(' ')
                    .any(|e| e == "EGL_KHR_no_config_context")
            {
                return fail("an EGL display lacks surfaceless contexts without a config".into());
            }
            if unsafe { (egl.BindAPI)(EGL_OPENGL_API) } == 0 {
                return fail("an EGL display has no desktop OpenGL".into());
            }
            let attributes = [
                EGL_CONTEXT_MAJOR_VERSION,
                4,
                EGL_CONTEXT_MINOR_VERSION,
                5,
                EGL_CONTEXT_OPENGL_PROFILE_MASK,
                EGL_CONTEXT_OPENGL_CORE_PROFILE_BIT,
                EGL_NONE,
            ];
            let context = unsafe {
                (egl.CreateContext)(
                    display,
                    std::ptr::null_mut(),
                    std::ptr::null_mut(),
                    attributes.as_ptr(),
                )
            };
            if context.is_null() {
                return fail("an EGL display makes no OpenGL 4.5 core context".into());
            }
            let none = std::ptr::null_mut();
            if unsafe { (egl.MakeCurrent)(display, none, none, context) } == 0 {
                unsafe { (egl.DestroyContext)(display, context) };
                return fail("an OpenGL context cannot be made current".into());
            }
            let checked = (|| {
                let gl = Gl::load(egl)?;
                for extension in ["GL_EXT_memory_object", "GL_EXT_memory_object_fd"] {
                    if !gl.has_extension(extension) {
                        return Err(format!("OpenGL lacks {extension}"));
                    }
                }
                let mut uuid = [0u8; 16];
                unsafe { (gl.GetUnsignedBytevEXT)(GL_DEVICE_UUID_EXT, uuid.as_mut_ptr()) };
                if device_uuid.is_some_and(|wanted| wanted != uuid) {
                    return Err("an OpenGL device is not the renderer's GPU".into());
                }
                Ok((gl, uuid))
            })();
            match checked {
                Ok((gl, uuid)) => Ok((gl, context, uuid)),
                Err(reason) => {
                    unsafe {
                        (egl.MakeCurrent)(display, none, none, none);
                        (egl.DestroyContext)(display, context);
                    }
                    fail(reason)
                }
            }
        }

        pub fn device_uuid(&self) -> [u8; 16] {
            self.device_uuid
        }

        /// Whether `name` is a texture of this context (a plugin's own).
        pub fn is_texture(&self, name: u32) -> bool {
            unsafe { (self.gl.IsTexture)(name) != 0 }
        }

        /// Makes the context current on this thread (a no-op when it is).
        pub fn make_current(&self) -> bool {
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

        /// An OpenGL 2D texture over exported memory (`fd`, `size` bytes, a dedicated
        /// allocation), with `levels` levels of `internal_format`: OpenGL owns the descriptor
        /// from then on. `filter`, `wrap`: Unity's FilterMode and TextureWrapMode (U, V).
        #[allow(clippy::too_many_arguments)]
        pub fn import(
            &mut self,
            fd: OwnedFd,
            size: u64,
            width: u32,
            height: u32,
            levels: u32,
            internal_format: u32,
            filter: i32,
            wrap: [i32; 2],
        ) -> Result<GlTexture, String> {
            if !self.make_current() {
                return Err("the plugins' OpenGL context cannot be made current".into());
            }
            let gl = &self.gl;
            self.clear_errors();
            let mut memory = 0;
            let mut name = 0;
            unsafe {
                (gl.CreateMemoryObjectsEXT)(1, &mut memory);
                (gl.MemoryObjectParameterivEXT)(memory, GL_DEDICATED_MEMORY_OBJECT_EXT, &1);
                let prepared = self.clear_errors();
                if !prepared.is_empty() {
                    // The descriptor is still ours: it closes with `fd`.
                    (gl.DeleteMemoryObjectsEXT)(1, &memory);
                    return Err(format!(
                        "OpenGL cannot make a memory object: {}",
                        prepared
                            .iter()
                            .map(|e| super::error_text(*e))
                            .collect::<Vec<_>>()
                            .join(", ")
                    ));
                }
                let raw = fd.into_raw_fd();
                (gl.ImportMemoryFdEXT)(memory, size, GL_HANDLE_TYPE_OPAQUE_FD_EXT, raw);
                // OpenGL owns the descriptor only once the import succeeded.
                let refused = self.clear_errors();
                if !refused.is_empty() {
                    drop(OwnedFd::from_raw_fd(raw));
                    (gl.DeleteMemoryObjectsEXT)(1, &memory);
                    return Err(format!(
                        "OpenGL cannot import the renderer's memory: {}",
                        refused
                            .iter()
                            .map(|e| super::error_text(*e))
                            .collect::<Vec<_>>()
                            .join(", ")
                    ));
                }
                (gl.CreateTextures)(GL_TEXTURE_2D, 1, &mut name);
                (gl.TextureParameteri)(name, GL_TEXTURE_TILING_EXT, GL_OPTIMAL_TILING_EXT as i32);
                (gl.TextureStorageMem2DEXT)(
                    name,
                    levels as i32,
                    internal_format,
                    width as i32,
                    height as i32,
                    memory,
                    0,
                );
                let mips = levels > 1;
                let (min, mag) = match (filter, mips) {
                    (0, false) => (GL_NEAREST, GL_NEAREST),
                    (0, true) => (GL_NEAREST_MIPMAP_NEAREST, GL_NEAREST),
                    (_, false) => (GL_LINEAR, GL_LINEAR),
                    (_, true) => (GL_LINEAR_MIPMAP_LINEAR, GL_LINEAR),
                };
                (gl.TextureParameteri)(name, GL_TEXTURE_MIN_FILTER, min);
                (gl.TextureParameteri)(name, GL_TEXTURE_MAG_FILTER, mag);
                for (parameter, mode) in
                    [GL_TEXTURE_WRAP_S, GL_TEXTURE_WRAP_T].into_iter().zip(wrap)
                {
                    let mode = match mode {
                        0 => GL_REPEAT,
                        2 => GL_MIRRORED_REPEAT,
                        _ => GL_CLAMP_TO_EDGE,
                    };
                    (gl.TextureParameteri)(name, parameter, mode);
                }
            }
            let errors = self.clear_errors();
            if !errors.is_empty() {
                self.delete(GlTexture { name, memory });
                return Err(format!(
                    "OpenGL refused the renderer's memory: {}",
                    errors
                        .iter()
                        .map(|e| super::error_text(*e))
                        .collect::<Vec<_>>()
                        .join(", ")
                ));
            }
            Ok(GlTexture { name, memory })
        }

        /// Deletes an imported texture and the framebuffers that use it.
        pub fn delete(&mut self, texture: GlTexture) {
            if !self.make_current() {
                return;
            }
            let gl = &self.gl;
            self.framebuffers
                .retain(|((color, depth, _), framebuffer)| {
                    let uses = *color == texture.name || *depth == texture.name;
                    if uses {
                        unsafe { (gl.DeleteFramebuffers)(1, framebuffer) };
                    }
                    !uses
                });
            unsafe {
                (gl.DeleteTextures)(1, &texture.name);
                (gl.DeleteMemoryObjectsEXT)(1, &texture.memory);
            }
        }

        /// Binds the framebuffer of a color texture's mip (and a depth texture), or one without
        /// attachments of the viewport's size, with the viewport (pixels, origin at the first row).
        pub fn bind_target(
            &mut self,
            color: Option<(u32, u32)>,
            depth: Option<(u32, bool)>,
            viewport: [i32; 4],
        ) {
            let gl = &self.gl;
            let framebuffer = match color {
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
                Some((name, mip)) => {
                    let depth_name = depth.map_or(0, |(d, _)| d);
                    let key = (name, depth_name, mip);
                    match self.framebuffers.iter().find(|(k, _)| *k == key) {
                        Some((_, framebuffer)) => *framebuffer,
                        None => {
                            let mut framebuffer = 0;
                            unsafe {
                                (gl.CreateFramebuffers)(1, &mut framebuffer);
                                (gl.NamedFramebufferTexture)(
                                    framebuffer,
                                    GL_COLOR_ATTACHMENT0,
                                    name,
                                    mip as i32,
                                );
                                if let Some((depth, stencil)) = depth {
                                    let attachment = if stencil {
                                        GL_DEPTH_STENCIL_ATTACHMENT
                                    } else {
                                        GL_DEPTH_ATTACHMENT
                                    };
                                    (gl.NamedFramebufferTexture)(framebuffer, attachment, depth, 0);
                                }
                            }
                            self.framebuffers.push((key, framebuffer));
                            framebuffer
                        }
                    }
                }
            };
            unsafe {
                (gl.BindFramebuffer)(GL_FRAMEBUFFER, framebuffer);
                (gl.Viewport)(viewport[0], viewport[1], viewport[2], viewport[3]);
            }
        }

        /// Copies level 0 of a 2D texture into another of the same size and format.
        pub fn copy(&self, source: u32, destination: u32, width: u32, height: u32) {
            unsafe {
                (self.gl.CopyImageSubData)(
                    source,
                    GL_TEXTURE_2D,
                    0,
                    0,
                    0,
                    0,
                    destination,
                    GL_TEXTURE_2D,
                    0,
                    0,
                    0,
                    0,
                    width as i32,
                    height as i32,
                    1,
                )
            };
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
            }
        }
    }
}

#[cfg(all(test, target_os = "linux"))]
mod tests {
    #[test]
    fn the_context_stays_only_with_the_render_thread() {
        // Needs a GPU with EGL; without one there is no context to hand around.
        if super::with(None, |_| ()).is_err() {
            return;
        }
        // Unmarked threads (this one, a worker that loads a plugin) release it after use...
        let worker = std::thread::spawn(|| super::with(None, |_| ()).is_ok());
        assert!(worker.join().unwrap());
        // ...so the render thread can take it, and keeps it.
        let render = std::thread::spawn(|| {
            super::mark_render_thread();
            let current = super::with(None, |interop| interop.make_current()).unwrap();
            // A worker cannot take it from the render thread now: it fails cleanly.
            let worker = std::thread::spawn(|| super::with(None, |_| ()).is_err());
            (current, worker.join().unwrap())
        });
        assert_eq!(render.join().unwrap(), (true, true));
    }
}
