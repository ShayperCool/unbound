//! The graphics device a run has, as the Unity Editor picks and reports it.
//!
//! Like Unity, Unbound has several graphics devices, and the device a game sees is the one that
//! draws. It is chosen once at startup ([`select`]) and every part reads the same
//! [`GraphicsDevice`]: SystemInfo's answers, the shader compilation target and its conventions,
//! the renderer's clip space and target orientation, and the native plugin interface.
//!
//! The choice follows the Unity 6000.0.71f1 Editor of the OS. On Linux a `-force-glcore*` /
//! `-force-opengl` or `-force-vulkan` option decides; else the Player Settings graphics API list
//! of the Editor's own standalone platform (StandaloneLinux64), when it is not automatic, followed
//! by that platform's first default API; else the platform's defaults (OpenGL Core, then Vulkan).
//! The first device that starts wins, OpenGL Core last. The Manual (Configure graphics APIs)
//! documents the list order and falling back to the next API. Measured on the Linux Editor: no
//! options or settings give OpenGL Core (tests/projects/urp `paths-opengl`), `-force-vulkan` gives
//! Vulkan (`paths-vulkan`), and a Vulkan-first LinuxStandaloneSupport list gives Vulkan without
//! options. The macOS Editor runs Metal, its only device: with `-force-glcore`, `-force-vulkan`
//! and a MacStandaloneSupport list of OpenGL Core and Vulkan too (tests/projects/urp CapsProbe,
//! `caps-metal.jsonl`; arm64). Not measured: that the active build target's list is not read.

use std::fmt;

/// A graphics device Unbound can run, with the `UnityEngine.Rendering.GraphicsDeviceType` value
/// of each as its discriminant. The default is the Linux Editor's own, OpenGL Core.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
#[repr(i32)]
pub enum GraphicsDevice {
    /// `GraphicsDeviceType.Metal`: the macOS Editor's.
    Metal = 16,
    /// `GraphicsDeviceType.OpenGLCore`.
    #[default]
    OpenGLCore = 17,
    /// `GraphicsDeviceType.Vulkan`.
    Vulkan = 21,
}

/// How a device's shader compiler checks the samplers a program names
/// ([`GraphicsDevice::named_samplers`]).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct NamedSamplers {
    /// Each stage's samplers resolve against the textures of that stage alone.
    pub per_stage: bool,
    /// A wrongly spelt inline state (two filters, `Clamp` with `Repeat`, `Aniso3`) is an error.
    pub refuses_misspelt_inline: bool,
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

    /// Sampling a depth texture reads its depth in red, green and blue and 1 in alpha, where
    /// OpenGL Core and Vulkan read it in red alone (green and blue 0). Measured on the Unity
    /// 6000.0.71f1 macOS Editor, Metal on Apple M2 Pro (tests/projects/samplerstates
    /// DepthChannels.*, DepthOnly.depth); OpenGL Core and Vulkan on NVIDIA.
    pub fn depth_sample_fills_color(self) -> bool {
        matches!(self, Self::Metal)
    }

    /// Whether a program writing depth draws (its colors alone) into a target without a depth
    /// buffer. The Metal device makes no pipeline for it ("depthAttachmentPixelFormat is not
    /// valid and shader writes to depth"), so the target keeps what it held. Measured on the Unity
    /// 6000.0.71f1 macOS Editor, Metal on Apple M2 Pro (tests/projects/samplerstates Fill.color);
    /// OpenGL Core and Vulkan draw it on NVIDIA.
    pub fn draws_depth_output_without_depth_buffer(self) -> bool {
        !matches!(self, Self::Metal)
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

    /// The device's name in a program's `#pragma only_renderers` and `exclude_renderers`.
    pub fn pragma_renderer(self) -> &'static str {
        match self {
            Self::Metal => "metal",
            Self::OpenGLCore => "glcore",
            Self::Vulkan => "vulkan",
        }
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

    /// Whether the screen's 8-bit color is stored blue first (BGRA) instead of red first: the
    /// macOS Editor's Metal screen is `MTLPixelFormatBGRA8Unorm` (tests/projects/nativegfx
    /// `metal.jsonl`, `plugin.state.color0.screen`). OpenGL Core and Vulkan keep RGBA order
    /// (unmeasured: no recorded interface exposes their screen's storage).
    pub fn screen_bgra(self) -> bool {
        matches!(self, Self::Metal)
    }

    /// Whether sampling applies `Texture.mipMapBias`: the macOS Editor's Metal device samples the
    /// level its derivatives choose whatever the bias, with every filter mode (tests/projects/
    /// nativegfx `metal.jsonl` `mipbias.*`, two captures on an Apple M2 Pro; Metal samplers have
    /// no bias). OpenGL Core and Vulkan apply it (tests/projects/samplerbias).
    pub fn applies_mip_bias(self) -> bool {
        !matches!(self, Self::Metal)
    }

    /// Whether a clear of a packed render target rounds its color to the channels' bits
    /// directly: the Vulkan Editor clears 0.5 to 32 of 63 (and 16 of 31) in a 5-6-5 target, where
    /// a draw, and every OpenGL Core write, keeps the top bits of the 8-bit value (31 of 63); the
    /// Metal Editor clears as it draws, rounding ([`Self::packed_writes_round`])
    /// (tests/projects/samplerstates Packed.*.clear, `vulkan.jsonl`, `metal.jsonl`).
    pub fn packed_clears_round(self) -> bool {
        matches!(self, Self::Vulkan | Self::Metal)
    }

    /// Whether a draw into a packed render target (4-4-4-4 included) keeps each channel's nearest
    /// level, read back as exactly `level / (2^bits - 1)`: the macOS Editor's Metal device
    /// stores the formats themselves (0.5 reads 16/31 in a 5-bit channel, 0.7 11/15 in a 4-bit
    /// one), where OpenGL Core and Vulkan keep the top bits of an 8-bit value and 4-4-4-4 targets
    /// at 8 bits (tests/projects/samplerstates `metal.jsonl` Packed.*, an Apple M2 Pro). A blend
    /// within one pass reads the unrounded value there: the probe's added 0.04 lands on 0.3, not on
    /// its rounded 9/31.
    pub fn packed_writes_round(self) -> bool {
        matches!(self, Self::Metal)
    }

    /// Whether a graphics program samples a texture through the texture's own sampler state
    /// whatever sampler it names, an inline one or another texture's: OpenGL Core, where the
    /// state is the texture's. The other devices sample through the sampler a program names
    /// (tests/projects/samplerstates, the OpenGL Core, Vulkan and macOS Metal Editors; the
    /// Manual excludes only OpenGL from inline samplers).
    pub fn samples_through_texture_state(self) -> bool {
        self.named_samplers().is_none()
    }

    /// How the shader compiler checks the samplers a graphics program names, where programs
    /// sample through them ([`Self::samples_through_texture_state`] is false); None where they
    /// do not. The Vulkan Editor refuses a sampler that is no texture's of either stage and no
    /// inline state, and a wrongly spelt inline state; the macOS Editor's Metal compiler checks
    /// each stage on its own (a sampler named for a texture of the other stage only is refused:
    /// tests/projects/samplerstates `Vertex`, `UsedVertex`) and refuses no wrongly spelt inline
    /// state, which it reads leniently (`Bad*` draw; `InlineSampler::read_leniently`),
    /// `metal.jsonl`, two captures on an Apple M2 Pro.
    pub fn named_samplers(self) -> Option<NamedSamplers> {
        match self {
            Self::OpenGLCore => None,
            Self::Vulkan => Some(NamedSamplers {
                per_stage: false,
                refuses_misspelt_inline: true,
            }),
            Self::Metal => Some(NamedSamplers {
                per_stage: true,
                refuses_misspelt_inline: false,
            }),
        }
    }

    /// Whether the shader compiler provides the precision texture and sampler type names
    /// (`Texture2D_float`, `sampler3D_half`, ...) on the device: Unity's `HLSLSupport.cginc`
    /// defines them itself except on Vulkan, Metal and the mobile APIs.
    pub fn compiler_precision_types(self) -> bool {
        matches!(self, Self::Vulkan | Self::Metal)
    }

    /// Whether a program's `ddx`/`ddy` take the coarse derivative (one per 2x2 quad), as the
    /// Editor's Vulkan programs do: HLSL's `ddx` is `ddx_coarse` since Shader Model 5, and on
    /// Vulkan the Editor keeps it so, where its OpenGL Core programs take the driver's default
    /// (6000.0.71f1, tests/projects/urp rendering paths, `decal_screen_space_*` on Vulkan: the
    /// silhouette pixels of URP's derivative normal reconstruction).
    pub fn coarse_derivatives(self) -> bool {
        matches!(self, Self::Vulkan)
    }

    /// Whether the device is drawn through OpenGL itself on this OS: OpenGL Core on Linux
    /// (macOS has no OpenGL 4.5 and draws every device with Metal). Programs for it are OpenGL
    /// shading language in the end, with that language's limits.
    pub fn drawn_by_opengl(self, os: HostOs) -> bool {
        matches!((self, os), (Self::OpenGLCore, HostOs::Linux))
    }

    /// Whether Unbound can draw with the device on `os`: the devices of that OS's Editor (Metal
    /// on macOS; OpenGL Core and Vulkan on Linux).
    pub fn drawable(self, os: HostOs) -> bool {
        os.default_devices().contains(&self)
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

    /// Whether the OS's Editor starts the device a command-line option forces or the project's
    /// list names: the Linux Editor does, the macOS Editor runs Metal whatever is asked
    /// (tests/projects/urp CapsProbe on macOS, `-force-glcore`, `-force-vulkan` and an OpenGL
    /// Core and Vulkan list).
    pub fn honours_device_requests(self) -> bool {
        matches!(self, Self::Linux)
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

/// The devices to try, in order: the first one that starts is the run's device. On an OS whose
/// Editor ignores what is asked ([`HostOs::honours_device_requests`]) its defaults alone.
pub fn candidates(request: &DeviceRequest, os: HostOs) -> Vec<GraphicsDevice> {
    if !os.honours_device_requests() {
        return os.default_devices().to_vec();
    }
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
        if device.drawable(os) && starts(*device) {
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
    fn metal_fills_depth_samples_and_refuses_depth_output_without_depth() {
        for device in GraphicsDevice::ALL {
            let metal = device == GraphicsDevice::Metal;
            assert_eq!(device.depth_sample_fills_color(), metal, "{device}");
            assert_eq!(
                device.draws_depth_output_without_depth_buffer(),
                !metal,
                "{device}"
            );
        }
    }

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
        assert_eq!(candidates(&request, HostOs::Linux), [Vulkan, OpenGLCore]);
    }

    #[test]
    fn macos_runs_metal_whatever_is_asked() {
        assert_eq!(
            candidates(&DeviceRequest::default(), HostOs::MacOs),
            [Metal]
        );
        let request = DeviceRequest {
            forced: Some(OpenGLCore),
            project_list: Some(vec![17, 21]),
        };
        assert_eq!(candidates(&request, HostOs::MacOs), [Metal]);
        assert_eq!(select(&request, HostOs::MacOs, |_| true), Ok(Metal));
        assert!(select(&request, HostOs::MacOs, |_| false).is_err());
        // Each OS draws its own Editor's devices only.
        assert!(Metal.drawable(HostOs::MacOs) && !Metal.drawable(HostOs::Linux));
        assert!(!OpenGLCore.drawable(HostOs::MacOs) && Vulkan.drawable(HostOs::Linux));
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
    }

    #[test]
    fn only_vulkan_programs_take_coarse_derivatives() {
        assert!(GraphicsDevice::Vulkan.coarse_derivatives());
        assert!(!GraphicsDevice::OpenGLCore.coarse_derivatives());
        assert!(!GraphicsDevice::Metal.coarse_derivatives());
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
        assert!(Metal.conventions().reversed_z && Metal.conventions().uv_starts_at_top);
        assert_eq!(Metal.shader_api(), "SHADER_API_METAL");
        assert!(Metal.screen_bgra() && !Vulkan.screen_bgra() && !OpenGLCore.screen_bgra());
        assert!(
            !Metal.applies_mip_bias() && Vulkan.applies_mip_bias() && OpenGLCore.applies_mip_bias()
        );
        assert_eq!(GraphicsDevice::from_device_type(17), Some(OpenGLCore));
    }
}
