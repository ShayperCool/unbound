//! The graphics device a run has, as the Unity Editor picks and reports it.
//!
//! Like Unity, Unbound has several graphics devices, and the device a game sees is the one that
//! draws. It is chosen once at startup ([`select`]) and every part reads the same
//! [`GraphicsDevice`]: SystemInfo's answers, the shader compilation target and its conventions,
//! the renderer's clip space and target orientation, and the native plugin interface.
//!
//! The choice follows the Unity 6000.0.71f1 Editor on Linux: a `-force-glcore*` / `-force-opengl`
//! or `-force-vulkan` option decides; else the Player Settings graphics API list of the Editor's
//! own standalone platform (StandaloneLinux64 on Linux, StandaloneOSX on macOS), when it is not
//! automatic, followed by that platform's first default API; else the platform's defaults (Linux:
//! OpenGL Core, then Vulkan; macOS: Metal). The first device that starts wins, OpenGL Core last.
//! The Manual (Configure graphics APIs) documents the list order and falling back to the next
//! API. Measured on the Linux Editor: no options or settings give OpenGL Core (tests/projects/urp
//! `paths-opengl`), `-force-vulkan` gives Vulkan (`paths-vulkan`), and a Vulkan-first
//! LinuxStandaloneSupport list gives Vulkan without options. Not measured: that the active build
//! target's list is not read, macOS.

use std::fmt;

/// A graphics device Unbound can run, with the `UnityEngine.Rendering.GraphicsDeviceType` value
/// of each as its discriminant. The default is the Linux Editor's own, OpenGL Core.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i32)]
pub enum GraphicsDevice {
    /// `GraphicsDeviceType.Metal`: not drawn by Unbound yet (macOS).
    Metal = 16,
    /// `GraphicsDeviceType.OpenGLCore`.
    #[default]
    OpenGLCore = 17,
    /// `GraphicsDeviceType.Vulkan`.
    Vulkan = 21,
}

/// How a device's shaders and rasterizer see clip space and textures (Unity's per-platform
/// shader macros and the GPU projection it computes, `GL.GetGPUProjectionMatrix`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Conventions {
    /// `UNITY_UV_STARTS_AT_TOP` / `SystemInfo.graphicsUVStartsAtTop`: texture row 0 is the top
    /// in clip space. Render textures are then drawn with the projection's y flipped (their rows
    /// stay bottom-up, as Unity's texture data is), the screen with the projection as it is (its
    /// rows top-down); `_ProjectionParams.x` is -1 while the projection is flipped.
    pub uv_starts_at_top: bool,
    /// `UNITY_REVERSED_Z` / `SystemInfo.usesReversedZBuffer`: depth 1 is near, 0 far; depth
    /// tests and clears are mirrored.
    pub reversed_z: bool,
    /// `UNITY_NEAR_CLIP_VALUE` is 0 (clip z from 0 to w) instead of -1 (from -w to w).
    pub clip_depth_zero_to_one: bool,
}

impl GraphicsDevice {
    /// Measured Unity 6000.0.71f1 compute comparisons follow texture state on Vulkan.
    /// OpenGL Core histories remain unresolved (tests/projects/samplerstates Compute.*).
    pub fn compute_depth_sampler_state(self) -> bool {
        matches!(self, Self::Vulkan)
    }

    /// Depth resolves on the measured Unity 6000.0.71f1 NVIDIA device. The OpenGL
    /// selection depends on the sample count (tests/projects/nativegfx device.sampleResolve);
    /// Vulkan uses sample zero (tests/projects/rtdepth). Other drivers remain unmeasured.
    pub fn depth_resolve_sample(self, samples: u32) -> u32 {
        match (self, samples) {
            (Self::OpenGLCore, 2) => 1,
            (Self::OpenGLCore, 8) => 3,
            _ => 0,
        }
    }

    /// Every device, in the discriminant's order.
    pub const ALL: [GraphicsDevice; 3] = [Self::Metal, Self::OpenGLCore, Self::Vulkan];

    /// `GraphicsDeviceType`'s value (and Unity's serialized graphics API id).
    pub fn device_type(self) -> i32 {
        self as i32
    }

    /// The device of a `GraphicsDeviceType` value.
    pub fn from_device_type(value: i32) -> Option<Self> {
        Self::ALL.into_iter().find(|d| d.device_type() == value)
    }

    /// `GraphicsDeviceType`'s name, which is also how the device is named on the command line
    /// and to the managed side.
    pub fn name(self) -> &'static str {
        match self {
            Self::Metal => "Metal",
            Self::OpenGLCore => "OpenGLCore",
            Self::Vulkan => "Vulkan",
        }
    }

    pub fn from_name(name: &str) -> Option<Self> {
        Self::ALL
            .into_iter()
            .find(|d| d.name().eq_ignore_ascii_case(name))
    }

    /// `UnityGfxRenderer` of the native plugin interface (`IUnityGraphics::GetRenderer`):
    /// kUnityGfxRendererOpenGLCore 17, kUnityGfxRendererVulkan 21, kUnityGfxRendererMetal 16.
    pub fn plugin_renderer(self) -> i32 {
        self.device_type()
    }

    /// The `SHADER_API_*` define programs compile with.
    pub fn shader_api(self) -> &'static str {
        match self {
            Self::Metal => "SHADER_API_METAL",
            Self::OpenGLCore => "SHADER_API_GLCORE",
            Self::Vulkan => "SHADER_API_VULKAN",
        }
    }

    pub fn conventions(self) -> Conventions {
        match self {
            Self::OpenGLCore => Conventions {
                uv_starts_at_top: false,
                reversed_z: false,
                clip_depth_zero_to_one: false,
            },
            Self::Vulkan | Self::Metal => Conventions {
                uv_starts_at_top: true,
                reversed_z: true,
                clip_depth_zero_to_one: true,
            },
        }
    }

    /// Whether the render pipelines read framebuffer inputs as subpass inputs on the device (the
    /// Core RP library's `PLATFORM_SUPPORTS_NATIVE_RENDERPASS`: Vulkan and Metal).
    pub fn subpass_inputs(self) -> bool {
        matches!(self, Self::Vulkan | Self::Metal)
    }

    /// Whether a graphics program samples a texture through the texture's own sampler state
    /// whatever sampler it names, an inline one or another texture's: OpenGL Core, where the
    /// state is the texture's. The other devices sample through the sampler a program names
    /// (tests/projects/samplerstates, the OpenGL Core and Vulkan Editors; the Manual excludes
    /// only OpenGL from inline samplers; unmeasured on Metal).
    pub fn samples_through_texture_state(self) -> bool {
        matches!(self, Self::OpenGLCore)
    }

    /// Whether the shader compiler provides the precision texture and sampler type names
    /// (`Texture2D_float`, `sampler3D_half`, ...) on the device: Unity's `HLSLSupport.cginc`
    /// defines them itself except on Vulkan, Metal and the mobile APIs.
    pub fn compiler_precision_types(self) -> bool {
        matches!(self, Self::Vulkan | Self::Metal)
    }

    /// Whether the device is drawn through OpenGL itself on this OS: OpenGL Core on Linux
    /// (macOS has no OpenGL 4.5 and draws every device with Metal). Programs for it are OpenGL
    /// shading language in the end, with that language's limits.
    pub fn drawn_by_opengl(self, os: HostOs) -> bool {
        matches!((self, os), (Self::OpenGLCore, HostOs::Linux))
    }

    /// Whether Unbound can draw with the device.
    pub fn drawable(self) -> bool {
        !matches!(self, Self::Metal)
    }
}

impl fmt::Display for GraphicsDevice {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.name())
    }
}

/// The operating system whose Editor Unbound answers as.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum HostOs {
    Linux,
    MacOs,
}

impl HostOs {
    pub fn current() -> Self {
        if cfg!(target_os = "macos") {
            Self::MacOs
        } else {
            Self::Linux
        }
    }

    /// The Player Settings graphics API list the Editor of this OS reads:
    /// `m_BuildTargetGraphicsAPIs`' entry of the OS's standalone platform.
    pub fn standalone_platform(self) -> &'static str {
        match self {
            Self::Linux => "LinuxStandaloneSupport",
            Self::MacOs => "MacStandaloneSupport",
        }
    }

    /// The standalone platform's default graphics APIs (its automatic list).
    pub fn default_devices(self) -> &'static [GraphicsDevice] {
        match self {
            Self::Linux => &[GraphicsDevice::OpenGLCore, GraphicsDevice::Vulkan],
            Self::MacOs => &[GraphicsDevice::Metal],
        }
    }
}

/// The device a command-line option forces, as the Editor's options name them (`-force-vulkan`,
/// `-force-glcore`, `-force-glcoreXY`, `-force-opengl`; one or two leading dashes). None for
/// other arguments.
pub fn forced_by_option(arg: &str) -> Option<GraphicsDevice> {
    let name = arg.strip_prefix("--").or_else(|| arg.strip_prefix('-'))?;
    let name = name.strip_prefix("force-")?;
    if name == "vulkan" {
        return Some(GraphicsDevice::Vulkan);
    }
    if name == "opengl" {
        return Some(GraphicsDevice::OpenGLCore);
    }
    let level = name.strip_prefix("glcore")?;
    (level.is_empty() || (level.len() == 2 && level.bytes().all(|b| b.is_ascii_digit())))
        .then_some(GraphicsDevice::OpenGLCore)
}

/// What the device choice starts from.
#[derive(Debug, Clone, Default)]
pub struct DeviceRequest {
    /// A device a command-line option forces.
    pub forced: Option<GraphicsDevice>,
    /// The project's graphics API list for the OS's standalone platform when it is not automatic
    /// (Unity's serialized API ids; ones Unbound does not know are skipped).
    pub project_list: Option<Vec<i32>>,
}

/// The devices to try, in order: the first one that starts is the run's device.
pub fn candidates(request: &DeviceRequest, os: HostOs) -> Vec<GraphicsDevice> {
    let mut order = Vec::new();
    let mut push = |d: GraphicsDevice| {
        if !order.contains(&d) {
            order.push(d);
        }
    };
    if let Some(forced) = request.forced {
        push(forced);
    }
    let defaults = os.default_devices();
    match &request.project_list {
        Some(list) if !list.is_empty() => {
            list.iter()
                .filter_map(|&id| GraphicsDevice::from_device_type(id))
                .for_each(&mut push);
            defaults.first().copied().into_iter().for_each(&mut push);
        }
        _ => defaults.iter().copied().for_each(&mut push),
    }
    push(GraphicsDevice::OpenGLCore);
    order
}

/// The run's device: the first candidate Unbound can draw with that `starts` accepts (it reports
/// the ones that do not start), else an error naming the candidates.
pub fn select(
    request: &DeviceRequest,
    os: HostOs,
    mut starts: impl FnMut(GraphicsDevice) -> bool,
) -> Result<GraphicsDevice, String> {
    let order = candidates(request, os);
    for device in &order {
        if device.drawable() && starts(*device) {
            return Ok(*device);
        }
    }
    let names: Vec<_> = order.iter().map(|d| d.name()).collect();
    Err(format!(
        "no graphics device could start (tried {})",
        names.join(", ")
    ))
}

/// Parses `m_APIs` of a `m_BuildTargetGraphicsAPIs` entry: 32-bit little-endian ids written as
/// hex, eight digits each (`1500000011000000` is Vulkan, OpenGL Core).
pub fn parse_api_list(hex: &str) -> Option<Vec<i32>> {
    let hex = hex.trim();
    if hex.len() % 8 != 0 {
        return None;
    }
    (0..hex.len())
        .step_by(8)
        .map(|i| {
            let bytes = u32::from_str_radix(&hex[i..i + 8], 16).ok()?.to_be_bytes();
            Some(i32::from_le_bytes(bytes))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use GraphicsDevice::*;

    #[test]
    fn options_force_the_editor_devices() {
        assert_eq!(forced_by_option("-force-vulkan"), Some(Vulkan));
        assert_eq!(forced_by_option("--force-vulkan"), Some(Vulkan));
        assert_eq!(forced_by_option("-force-glcore"), Some(OpenGLCore));
        assert_eq!(forced_by_option("-force-glcore45"), Some(OpenGLCore));
        assert_eq!(forced_by_option("-force-opengl"), Some(OpenGLCore));
        assert_eq!(forced_by_option("-force-glcore4"), None);
        assert_eq!(forced_by_option("-force-gfx-direct"), None);
        assert_eq!(forced_by_option("-force-vulkan-layers"), None);
    }

    #[test]
    fn linux_defaults_to_opengl_core_then_vulkan() {
        let order = candidates(&DeviceRequest::default(), HostOs::Linux);
        assert_eq!(order, [OpenGLCore, Vulkan]);
    }

    #[test]
    fn a_forced_device_goes_first() {
        let request = DeviceRequest {
            forced: Some(Vulkan),
            project_list: None,
        };
        assert_eq!(candidates(&request, HostOs::Linux), [Vulkan, OpenGLCore]);
    }

    #[test]
    fn a_project_list_is_followed_by_the_first_default() {
        let request = DeviceRequest {
            forced: None,
            project_list: Some(vec![21]),
        };
        assert_eq!(candidates(&request, HostOs::Linux), [Vulkan, OpenGLCore]);
        // Unknown ids (Direct3D11) are skipped; OpenGL Core is the last resort.
        let request = DeviceRequest {
            forced: None,
            project_list: Some(vec![2, 21]),
        };
        assert_eq!(
            candidates(&request, HostOs::MacOs),
            [Vulkan, Metal, OpenGLCore]
        );
    }

    #[test]
    fn selection_skips_devices_that_do_not_start() {
        let request = DeviceRequest {
            forced: Some(Vulkan),
            project_list: None,
        };
        assert_eq!(
            select(&request, HostOs::Linux, |d| d != Vulkan),
            Ok(OpenGLCore)
        );
        assert!(select(&request, HostOs::Linux, |_| false).is_err());
        // Metal is not drawn: the macOS default falls through to OpenGL Core.
        assert_eq!(
            select(&DeviceRequest::default(), HostOs::MacOs, |_| true),
            Ok(OpenGLCore)
        );
    }

    #[test]
    fn api_lists_are_little_endian_hex() {
        assert_eq!(parse_api_list("1500000011000000"), Some(vec![21, 17]));
        assert_eq!(parse_api_list(""), Some(vec![]));
        assert_eq!(parse_api_list("150000"), None);
    }

    #[test]
    fn conventions_follow_the_device() {
        assert!(!OpenGLCore.conventions().reversed_z);
        assert!(Vulkan.conventions().reversed_z && Vulkan.conventions().uv_starts_at_top);
        assert_eq!(Vulkan.shader_api(), "SHADER_API_VULKAN");
        assert_eq!(GraphicsDevice::from_device_type(17), Some(OpenGLCore));
    }
}
