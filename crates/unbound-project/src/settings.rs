//! What the project's `ProjectSettings/` says that matters before content loads.
use anyhow::{Context, Result};
use std::{collections::BTreeMap, path::Path};
use unbound_compat::InputHandling;
use unbound_yaml::{Document, Guid, Value};

/// Writes `ProjectSettings/InputManager.asset` as the Editor does when it opens a project that
/// lacks it: the release's default axes (Unity 6000.0.71f1, tests/projects/inputdefaults). A file
/// that cannot be written is reported and the defaults apply without it.
pub fn write_missing_input_manager(project: &Path, profile: &unbound_compat::Profile) {
    let file = project.join("ProjectSettings/InputManager.asset");
    if file.exists() {
        return;
    }
    let text = profile.default_input_manager();
    let temporary = file.with_extension("asset.unbound-tmp");
    let written =
        std::fs::write(&temporary, text).and_then(|()| std::fs::rename(&temporary, &file));
    if let Err(e) = written {
        let _ = std::fs::remove_file(&temporary);
        tracing::warn!(
            "{}: writing the default Input Manager settings: {e}",
            file.display()
        );
    }
}

#[derive(Debug, Clone, Default)]
pub struct BuildScene {
    pub path: String,
    pub guid: Guid,
    pub enabled: bool,
}

#[derive(Debug, Clone, Default)]
pub struct Settings {
    pub product_name: String,
    pub company_name: String,
    /// `PlayerSettings.productGUID` as the settings write it (hex, low nibble first).
    pub product_guid: String,
    /// `PlayerSettings.bundleVersion`: Application.version.
    pub bundle_version: String,
    /// `PlayerSettings.applicationIdentifier` per build target group (`Android`, `iPhone`, ...).
    pub application_identifiers: BTreeMap<String, String>,
    /// `PlayerSettings.preloadedAssets`: (guid, file id) of project assets.
    pub preloaded_assets: Vec<(Guid, i64)>,
    /// Scripting define symbols by build target group (`Android`, `Standalone`, `iPhone`, ...).
    pub scripting_defines: BTreeMap<String, Vec<String>>,
    /// API compatibility level by group: 3 = .NET Framework, 6 = .NET Standard 2.1.
    pub api_compatibility: BTreeMap<String, i64>,
    /// The level of groups `api_compatibility` does not name (`apiCompatibilityLevel`).
    pub default_api_compatibility: i64,
    pub allow_unsafe_code: bool,
    pub build_scenes: Vec<BuildScene>,
    /// `EditorBuildSettings.m_configObjects`: name → (guid, file id) of a project asset.
    pub config_objects: BTreeMap<String, (Guid, i64)>,
    pub tags: Vec<String>,
    pub layers: Vec<String>,
    /// `PlayerSettings.defaultInterfaceOrientation` (UIOrientation: 0 Portrait, 1 PortraitUpsideDown,
    /// 2 LandscapeRight, 3 LandscapeLeft, 4 AutoRotation).
    pub default_orientation: i64,
    /// The orientations auto-rotation may use: portrait, portrait upside down, landscape right,
    /// landscape left.
    pub autorotate: [bool; 4],
    /// The standalone player's default resolution.
    pub default_screen: (i32, i32),
    /// The web player's default resolution.
    pub default_screen_web: (i32, i32),
    /// `PlayerSettings.activeInputHandler`: which input handling the player runs.
    pub input_handling: InputHandling,
    /// `m_BuildTargetGraphicsAPIs`: the graphics API ids (`GraphicsDeviceType` values) a platform
    /// (`LinuxStandaloneSupport`, ...) lists by hand; a platform left automatic has no entry.
    pub graphics_apis: BTreeMap<String, Vec<i32>>,
    /// `GraphicsSettings.m_TierSettings` the project sets by hand, by (BuildTargetGroup, tier).
    pub tier_settings: BTreeMap<(i64, i64), TierSettings>,
    /// What the automatic tier settings follow ([`TierSettings::automatic`]).
    pub tier_defaults: TierDefaults,
    /// `EditorSettings.spritePackerMode` (`SpritePackerMode`; 0, Disabled, when unset).
    pub sprite_packer_mode: i64,
    /// The release in `ProjectVersion.txt` (None without one).
    pub unity_version: Option<unbound_compat::UnityVersion>,
    /// `m_BuildTargetGroupLightmapEncodingQuality`: the lightmap encoding quality a build target
    /// group (`Standalone`, `Android`, ...) sets (0 Low, 1 Normal, 2 High); others use their
    /// default.
    pub lightmap_encoding_quality: BTreeMap<String, i64>,
    /// `m_BuildTargetGroupHDRCubemapEncodingQuality`, alike for HDR cubemaps.
    pub hdr_cubemap_encoding_quality: BTreeMap<String, i64>,
}

/// `SpritePackerMode.AlwaysOnAtlas` ("Sprite Atlas V1 - Always Enabled").
const ALWAYS_ON_ATLAS: i64 = 4;
/// `SpritePackerMode.SpriteAtlasV2` ("Sprite Atlas V2 - Enabled").
const SPRITE_ATLAS_V2: i64 = 5;

/// The sprite atlases the Editor packs before it enters Play mode, by the project's Sprite
/// Packer mode: documented for "Sprite Atlas V1 - Always Enabled" (`.spriteatlas` assets) and
/// "Sprite Atlas V2 - Enabled" (`.spriteatlasv2`) in the Editor settings Manual; in "Sprite Atlas
/// V2 - Enabled" `.spriteatlas` assets are packed too (matches the Unity 6000.0.71f1 Editor,
/// tests/projects/spriteatlaseditor v1wholewritev2). Unbound packs them for Play-mode sessions
/// (unbound-runtime's sprite_atlases).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PlayModeAtlases {
    /// The mode's name in the Editor settings.
    pub mode: &'static str,
    /// The atlases' asset paths, sorted.
    pub paths: Vec<String>,
}

impl PlayModeAtlases {
    /// The atlases the one-time warning names before it counts the rest (`a, b +3`).
    pub const WARNING_SHOWN: usize = 5;

    /// The first `shown` atlases (`a, b +3`).
    fn names(&self, shown: usize) -> String {
        let mut names = self
            .paths
            .iter()
            .take(shown)
            .map(String::as_str)
            .collect::<Vec<_>>()
            .join(", ");
        if self.paths.len() > shown {
            names.push_str(&format!(" +{}", self.paths.len() - shown));
        }
        names
    }

    /// The warning of an Edit-mode session: sprites it loads stay unpacked when it enters Play
    /// mode, where the Editor packs them.
    pub fn edit_mode_message(&self) -> String {
        format!(
            "Sprite Packer mode \"{}\" packs sprite atlases before entering Play mode; in this \
             Edit-mode session, sprites loaded before it enters Play mode stay unpacked (their own \
             textures), and sprites loaded in Play mode stay packed after it leaves. Atlases ({}): {}",
            self.mode,
            self.paths.len(),
            self.names(Self::WARNING_SHOWN)
        )
    }

    /// `unbound doctor`'s report: the atlases and what of their packing is not reproduced.
    pub fn doctor_message(&self) -> String {
        format!(
            "Sprite Packer mode \"{}\" packs these atlases for Play mode, as the Editor packs them; \
             not reproduced: their page textures' names end with Unbound's own hash, an Edit-mode \
             session entering Play mode keeps its sprites unpacked, secondary textures get no \
             secondary pages, alpha splitting is not made. Atlases ({}): {}",
            self.mode,
            self.paths.len(),
            self.names(self.paths.len())
        )
    }
}

/// A graphics tier's settings (`EditorGraphicsSettings.GetTierSettings`), which also choose the
/// built-in shader defines.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TierSettings {
    /// 0 low, 1 medium, 2 high: the Standard shaders' BRDF 3, 2 or 1.
    pub standard_shader_quality: i64,
    /// `CameraHDRMode`: 1 FP16, 2 R11G11B10.
    pub hdr_mode: i64,
    pub box_projection: bool,
    pub probe_blending: bool,
    pub hdr: bool,
    pub detail_normal_map: bool,
    pub cascaded_shadow_maps: bool,
    pub prefer_32bit_shadow_maps: bool,
    /// `enableLPPV`: light probe proxy volumes.
    pub light_probe_proxy_volume: bool,
    /// `semitransparentShadows` (`useDitherMaskForAlphaBlendedShadows`).
    pub dither_alpha_shadows: bool,
    /// `RenderingPath`.
    pub rendering_path: i64,
    /// `RealtimeGICPUUsage`: 25 low, 50 medium, 75 high, 100 unlimited.
    pub realtime_gi_cpu_usage: i64,
}

/// What a project's automatic tier settings follow besides the platform group and tier:
/// GraphicsSettings' default rendering paths and the Editor's default behaviour mode.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TierDefaults {
    /// `m_DefaultRenderingPath` (`RenderingPath`, Forward when unset).
    pub rendering_path: i64,
    /// `m_DefaultMobileRenderingPath`.
    pub mobile_rendering_path: i64,
    /// `EditorSettings.m_DefaultBehaviorMode` is 2D.
    pub mode_2d: bool,
}

impl Default for TierDefaults {
    fn default() -> Self {
        Self {
            rendering_path: RENDERING_PATH_FORWARD,
            mobile_rendering_path: RENDERING_PATH_FORWARD,
            mode_2d: false,
        }
    }
}

/// `RenderingPath.Forward`.
const RENDERING_PATH_FORWARD: i64 = 1;
/// `BuildTargetGroup` values the tier rule names.
const GROUP_STANDALONE: i64 = 1;
const GROUP_IOS: i64 = 4;
const GROUP_ANDROID: i64 = 7;
const GROUP_WSA: i64 = 14;
const GROUP_TVOS: i64 = 25;
/// The `BuildTargetGroup` values whose tier rule is their own; every other group's automatic
/// settings are those of [`TierSettings::OTHER_GROUP`].
pub const TIER_RULE_GROUPS: [i64; 5] = [
    GROUP_STANDALONE,
    GROUP_IOS,
    GROUP_ANDROID,
    GROUP_WSA,
    GROUP_TVOS,
];

impl Settings {
    /// What the Editor of this OS starts its graphics device from: a device the command line
    /// forces and the project's graphics API list for the OS's standalone platform (see
    /// [`unbound_compat::candidates`]).
    pub fn device_request(
        &self,
        forced: Option<unbound_compat::GraphicsDevice>,
    ) -> unbound_compat::DeviceRequest {
        let os = unbound_compat::HostOs::current();
        unbound_compat::DeviceRequest {
            forced,
            project_list: self.graphics_apis.get(os.standalone_platform()).cloned(),
        }
    }
}

impl TierSettings {
    /// A group without a tier rule of its own (`BuildTargetGroup.Unknown`).
    pub const OTHER_GROUP: i64 = 0;

    /// What the Editor uses for a tier (0 Tier1 .. 2 Tier3) of a platform group left automatic.
    /// The low-end set (R11G11B10 HDR, every feature off, the mobile rendering path) is iOS's,
    /// Android's and tvOS's, and Windows Store's lowest tier's; every other one has FP16 HDR,
    /// every feature on but 32-bit shadow maps (HDR also off in a 2D project) and the default
    /// rendering path. The Standard shader quality is low for the lowest tier of iOS, Android
    /// and Windows Store, medium for the other iOS, Android and tvOS tiers, else high; realtime
    /// GI's CPU usage is medium for Standalone's highest tier, else low. Matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/tiersettings (2D projects unmeasured); the Linux
    /// Editor's OpenGLCore defines follow it (`HasShaderDefine`, tests/projects/lightmapencoding).
    pub fn automatic(group: i64, tier: i64, defaults: &TierDefaults) -> Self {
        let mobile = matches!(group, GROUP_IOS | GROUP_ANDROID | GROUP_TVOS);
        let low_end = mobile || (group == GROUP_WSA && tier == 0);
        let lowest = tier == 0 && matches!(group, GROUP_IOS | GROUP_ANDROID | GROUP_WSA);
        Self {
            standard_shader_quality: if lowest {
                0
            } else if mobile {
                1
            } else {
                2
            },
            hdr_mode: if low_end { 2 } else { 1 },
            box_projection: !low_end,
            probe_blending: !low_end,
            hdr: !low_end && !defaults.mode_2d,
            detail_normal_map: !low_end,
            cascaded_shadow_maps: !low_end,
            prefer_32bit_shadow_maps: false,
            light_probe_proxy_volume: !low_end,
            dither_alpha_shadows: !low_end,
            rendering_path: if low_end {
                defaults.mobile_rendering_path
            } else {
                defaults.rendering_path
            },
            realtime_gi_cpu_usage: if group == GROUP_STANDALONE && tier == 2 {
                50
            } else {
                25
            },
        }
    }

    /// A tier set by hand (`m_TierSettings`' `m_Settings`); a field it lacks keeps
    /// `automatic`'s value.
    fn read(settings: &Value, automatic: Self) -> Self {
        let int =
            |key: &str, default: i64| settings.get(key).and_then(Value::as_i64).unwrap_or(default);
        let flag = |key: &str, default: bool| int(key, default as i64) != 0;
        Self {
            standard_shader_quality: int(
                "standardShaderQuality",
                automatic.standard_shader_quality,
            ),
            hdr_mode: int("hdrMode", automatic.hdr_mode),
            box_projection: flag("useReflectionProbeBoxProjection", automatic.box_projection),
            probe_blending: flag("useReflectionProbeBlending", automatic.probe_blending),
            hdr: flag("useHDR", automatic.hdr),
            detail_normal_map: flag("useDetailNormalMap", automatic.detail_normal_map),
            cascaded_shadow_maps: flag("useCascadedShadowMaps", automatic.cascaded_shadow_maps),
            prefer_32bit_shadow_maps: flag(
                "prefer32BitShadowMaps",
                automatic.prefer_32bit_shadow_maps,
            ),
            light_probe_proxy_volume: flag("enableLPPV", automatic.light_probe_proxy_volume),
            dither_alpha_shadows: flag(
                "useDitherMaskForAlphaBlendedShadows",
                automatic.dither_alpha_shadows,
            ),
            rendering_path: int("renderingPath", automatic.rendering_path),
            realtime_gi_cpu_usage: int("realtimeGICPUUsage", automatic.realtime_gi_cpu_usage),
        }
    }

    /// The fields that choose shader defines.
    fn define_fields(&self) -> (i64, [bool; 6]) {
        (
            self.standard_shader_quality,
            [
                self.box_projection,
                self.probe_blending,
                self.detail_normal_map,
                self.cascaded_shadow_maps,
                self.dither_alpha_shadows,
                self.light_probe_proxy_volume,
            ],
        )
    }
}

impl Settings {
    /// `EditorGraphicsSettings.GetTierSettings(group, tier)`: the tier set by hand, else the
    /// automatic one ([`TierSettings::automatic`]).
    pub fn tier_settings(&self, group: i64, tier: i64) -> TierSettings {
        self.tier_settings
            .get(&(group, tier))
            .copied()
            .unwrap_or_else(|| TierSettings::automatic(group, tier, &self.tier_defaults))
    }
}

fn first_document(path: &Path) -> Result<Option<Document>> {
    let docs = match unbound_yaml::reads::read_documents(path) {
        Ok(docs) => docs,
        Err(error) if error.is::<std::io::Error>() => return Ok(None),
        Err(error) => return Err(error).with_context(|| path.display().to_string()),
    };
    Ok(docs.into_iter().next())
}

fn string_list(value: Option<&Value>) -> Vec<String> {
    value
        .and_then(Value::as_seq)
        .map(|items| {
            items
                .iter()
                .map(|v| v.as_str().unwrap_or_default().to_string())
                .collect()
        })
        .unwrap_or_default()
}

impl Settings {
    pub fn read(project: &Path) -> Result<Self> {
        let dir = project.join("ProjectSettings");
        let mut settings = Settings::default();
        if let Some(player) = first_document(&dir.join("ProjectSettings.asset"))? {
            let body = &player.body;
            settings.product_name = body
                .get("productName")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .into();
            settings.company_name = body
                .get("companyName")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .into();
            settings.product_guid = body
                .get("productGUID")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .into();
            settings.bundle_version = body
                .get("bundleVersion")
                .and_then(Value::as_str)
                .unwrap_or("1.0")
                .into();
            for item in body
                .get("preloadedAssets")
                .and_then(Value::as_seq)
                .unwrap_or_default()
            {
                if let Some(r) = item.as_ref()
                    && !r.is_null()
                {
                    settings.preloaded_assets.push((r.guid, r.file_id));
                }
            }
            if let Some(map) = body.get("applicationIdentifier").and_then(Value::as_map) {
                for (group, id) in map.iter() {
                    if let Some(id) = id.as_str() {
                        settings
                            .application_identifiers
                            .insert(group.to_string(), id.to_string());
                    }
                }
            }
            if let Some(map) = body.get("scriptingDefineSymbols").and_then(Value::as_map) {
                for (group, defines) in map.iter() {
                    let list = parse_defines(defines.as_str().unwrap_or_default());
                    settings.scripting_defines.insert(group.into(), list);
                }
            }
            if let Some(map) = body
                .get("apiCompatibilityLevelPerPlatform")
                .and_then(Value::as_map)
            {
                for (group, level) in map.iter() {
                    settings
                        .api_compatibility
                        .insert(group.into(), level.as_i64().unwrap_or(3));
                }
            }
            let int =
                |key: &str, default: i64| body.get(key).and_then(Value::as_i64).unwrap_or(default);
            // .NET Standard 2.1 is the default of projects Unity 2021.2+ creates.
            settings.default_api_compatibility = int("apiCompatibilityLevel", 6);
            let flag = |key: &str| body.get(key).and_then(Value::as_i64).unwrap_or(1) != 0;
            settings.default_orientation = int("defaultScreenOrientation", 4);
            settings.input_handling = InputHandling::from_setting(int("activeInputHandler", 0));
            for entry in body
                .get("m_BuildTargetGraphicsAPIs")
                .and_then(Value::as_seq)
                .into_iter()
                .flatten()
            {
                let automatic = entry.get("m_Automatic").and_then(Value::as_i64) != Some(0);
                let platform = entry.get("m_BuildTarget").and_then(Value::as_str);
                let apis = entry
                    .get("m_APIs")
                    .and_then(Value::as_str)
                    .and_then(unbound_compat::parse_api_list);
                if let (false, Some(platform), Some(apis)) = (automatic, platform, apis) {
                    settings.graphics_apis.insert(platform.to_string(), apis);
                }
            }
            settings.autorotate = [
                flag("allowedAutorotateToPortrait"),
                flag("allowedAutorotateToPortraitUpsideDown"),
                flag("allowedAutorotateToLandscapeRight"),
                flag("allowedAutorotateToLandscapeLeft"),
            ];
            settings.default_screen = (
                int("defaultScreenWidth", 1024) as i32,
                int("defaultScreenHeight", 768) as i32,
            );
            settings.default_screen_web = (
                int("defaultScreenWidthWeb", 960) as i32,
                int("defaultScreenHeightWeb", 600) as i32,
            );
            settings.allow_unsafe_code = body
                .get("allowUnsafeCode")
                .and_then(Value::as_bool)
                .unwrap_or(false);
            for (key, map) in [
                (
                    "m_BuildTargetGroupLightmapEncodingQuality",
                    &mut settings.lightmap_encoding_quality,
                ),
                (
                    "m_BuildTargetGroupHDRCubemapEncodingQuality",
                    &mut settings.hdr_cubemap_encoding_quality,
                ),
            ] {
                for entry in body.get(key).and_then(Value::as_seq).unwrap_or_default() {
                    let group = entry.get("m_BuildTarget").and_then(Value::as_str);
                    let quality = entry.get("m_EncodingQuality").and_then(Value::as_i64);
                    if let (Some(group), Some(quality)) = (group, quality) {
                        map.entry(group.to_string()).or_insert(quality);
                    }
                }
            }
        }
        settings.unity_version = std::fs::read_to_string(dir.join("ProjectVersion.txt"))
            .ok()
            .and_then(|text| unbound_compat::UnityVersion::from_project_version_txt(&text).ok());
        if let Some(build) = first_document(&dir.join("EditorBuildSettings.asset"))? {
            for scene in build
                .body
                .get("m_Scenes")
                .and_then(Value::as_seq)
                .unwrap_or_default()
            {
                settings.build_scenes.push(BuildScene {
                    path: scene
                        .get("path")
                        .and_then(Value::as_str)
                        .unwrap_or_default()
                        .into(),
                    guid: scene
                        .get("guid")
                        .and_then(Value::as_guid)
                        .unwrap_or_default(),
                    enabled: scene
                        .get("enabled")
                        .and_then(Value::as_bool)
                        .unwrap_or(true),
                });
            }
            if let Some(objects) = build.body.get("m_configObjects").and_then(Value::as_map) {
                for (name, value) in objects.iter() {
                    if let Some(r) = value.as_ref() {
                        settings
                            .config_objects
                            .insert(name.to_string(), (r.guid, r.file_id));
                    }
                }
            }
        }
        // The Sprite Packer mode (for a warning) and the default behaviour mode (2D or 3D, which
        // the tiers' automatic HDR follows): an unreadable file leaves them unset instead of
        // failing the project.
        match first_document(&dir.join("EditorSettings.asset")) {
            Ok(editor) => {
                let int = |key: &str| editor.as_ref().and_then(|e| e.body.get(key)?.as_i64());
                settings.sprite_packer_mode = int("m_SpritePackerMode").unwrap_or(0);
                settings.tier_defaults.mode_2d = int("m_DefaultBehaviorMode") == Some(1);
            }
            Err(error) => tracing::warn!("{error:#}: the Editor settings are not read"),
        }
        if let Some(graphics) = first_document(&dir.join("GraphicsSettings.asset"))? {
            let path = |key: &str| graphics.body.get(key).and_then(Value::as_i64);
            if let Some(path) = path("m_DefaultRenderingPath") {
                settings.tier_defaults.rendering_path = path;
            }
            if let Some(path) = path("m_DefaultMobileRenderingPath") {
                settings.tier_defaults.mobile_rendering_path = path;
            }
            for tier in graphics
                .body
                .get("m_TierSettings")
                .and_then(Value::as_seq)
                .unwrap_or_default()
            {
                let int = |key: &str| tier.get(key).and_then(Value::as_i64);
                if let (Some(group), Some(index), Some(values), Some(0)) = (
                    int("m_BuildTarget"),
                    int("m_Tier"),
                    tier.get("m_Settings"),
                    int("m_Automatic"),
                ) {
                    let automatic = TierSettings::automatic(group, index, &settings.tier_defaults);
                    settings
                        .tier_settings
                        .insert((group, index), TierSettings::read(values, automatic));
                }
            }
        }
        if let Some(tags) = first_document(&dir.join("TagManager.asset"))? {
            settings.tags = string_list(tags.body.get("tags"));
            settings.layers = string_list(tags.body.get("layers"));
        }
        Ok(settings)
    }

    /// Whether a handheld player starts in landscape: a fixed landscape orientation, or
    /// auto-rotation allowed into landscape but not into portrait.
    pub fn starts_landscape(&self) -> bool {
        match self.default_orientation {
            0 | 1 => false,
            2 | 3 => true,
            _ => {
                let [portrait, upside_down, right, left] = self.autorotate;
                (right || left) && !(portrait || upside_down)
            }
        }
    }

    /// The screen a build target group's player opens with, scaled for a desktop window:
    /// phones (Android, iOS) as a 540x960 screen in the project's orientation, the web and
    /// standalone players at their default resolutions.
    pub fn default_screen_for(&self, group: &str) -> (i32, i32) {
        match group {
            "Android" | "iOS" | "iPhone" => {
                if self.starts_landscape() {
                    (960, 540)
                } else {
                    (540, 960)
                }
            }
            "WebGL" => self.default_screen_web,
            _ => self.default_screen,
        }
    }

    /// The built-in shader defines a build target group's shaders compile with in the Editor
    /// (measured on 6000.0 with Android active): Android's are mobile ones (`SHADER_API_MOBILE`,
    /// normal maps unpacked from RGB, no RGBM and no cubemap arrays), then come those of the
    /// highest graphics tier the Editor runs, which `UNITY_HARDWARE_TIER3` names only when the
    /// tiers differ, and the define of the lightmap encoding ([`Self::lighting_encodings`]).
    /// `GraphicsSettings.HasShaderDefine` answers the same set without the tier's name
    /// (tests/projects/lightmapencoding, Standalone and Android); programs compile with them all,
    /// `UNITY_LIGHT_PROBE_PROXY_VOLUME` included where the tier enables proxy volumes
    /// (tests/projects/lppv). The Metal device adds framebuffer fetch, and its fallback
    /// for GPUs without it while the standalone target is active and the macOS standalone
    /// platform's graphics API list has Metal (an automatic list has): measured on the macOS
    /// Editor (tests/projects/urp CapsProbe `HasShaderDefine.*`: `caps-metal.jsonl`; without the
    /// fallback with Android active, `caps-metal-android.jsonl`, and with a list of OpenGL Core
    /// and Vulkan, `caps-metal-list.jsonl`). Other targets (iOS, WebGL) are unmeasured and
    /// taken as Android. Programs compile with framebuffer fetch (unbound_shaders' `fetch`).
    pub fn shader_defines(
        &self,
        group: &str,
        device: unbound_compat::GraphicsDevice,
    ) -> Vec<&'static str> {
        // BuildTargetGroup: Standalone 1, iOS 4, Android 7, WebGL 13.
        let (id, mobile) = match group {
            "Android" => (7, true),
            "iOS" | "iPhone" => (4, false),
            "WebGL" => (13, false),
            _ => (1, false),
        };
        // iOS compiles with the desktop set: its defines are unmeasured, so a tier it leaves
        // automatic stays a desktop tier here, though GetTierSettings answers its own (mobile)
        // values (tests/projects/tiersettings).
        let rule_group = if id == GROUP_IOS {
            TierSettings::OTHER_GROUP
        } else {
            id
        };
        let tiers = [0, 1, 2].map(|index| {
            self.tier_settings
                .get(&(id, index))
                .copied()
                .unwrap_or_else(|| TierSettings::automatic(rule_group, index, &self.tier_defaults))
        });
        // Automatic tiers differ from each other.
        let tiers_differ = tiers
            .iter()
            .any(|t| t.define_fields() != tiers[0].define_fields())
            || (0..3).any(|i| !self.tier_settings.contains_key(&(id, i)));
        let high = tiers[2];
        let mut defines = if mobile {
            vec![
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_NO_RGBM",
                "UNITY_NO_CUBEMAP_ARRAY",
            ]
        } else {
            vec!["SHADER_API_DESKTOP"]
        };
        defines.push(match high.standard_shader_quality {
            0 => "UNITY_PBS_USE_BRDF3",
            1 => "UNITY_PBS_USE_BRDF2",
            _ => "UNITY_PBS_USE_BRDF1",
        });
        for (on, define) in [
            (high.box_projection, "UNITY_SPECCUBE_BOX_PROJECTION"),
            (high.probe_blending, "UNITY_SPECCUBE_BLENDING"),
            (high.detail_normal_map, "UNITY_ENABLE_DETAIL_NORMALMAP"),
            (!high.cascaded_shadow_maps, "UNITY_NO_SCREENSPACE_SHADOWS"),
            (
                high.dither_alpha_shadows,
                "UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS",
            ),
            (
                high.light_probe_proxy_volume,
                "UNITY_LIGHT_PROBE_PROXY_VOLUME",
            ),
            (tiers_differ, "UNITY_HARDWARE_TIER3"),
        ] {
            if on {
                defines.push(define);
            }
        }
        defines.push(self.lighting_encodings(group).lightmap_define());
        if device == unbound_compat::GraphicsDevice::Metal {
            defines.push("UNITY_FRAMEBUFFER_FETCH_AVAILABLE");
            let metal = unbound_compat::GraphicsDevice::Metal.device_type();
            if id == 1
                && self
                    .graphics_apis
                    .get(unbound_compat::HostOs::MacOs.standalone_platform())
                    .is_none_or(|list| list.contains(&metal))
            {
                defines.push("UNITY_NEEDS_RENDERPASS_FBFETCH_FALLBACK");
            }
        }
        defines
    }

    /// How a build target group stores baked lightmaps and HDR cubemaps: its Player Settings
    /// encoding qualities for the project's release ([`unbound_compat::LightingEncodings`]);
    /// PlayerSettings stores iOS as `iPhone`.
    pub fn lighting_encodings(&self, group: &str) -> unbound_compat::LightingEncodings {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.unity_version
            .map(|version| {
                unbound_compat::LightingEncodings::resolve(
                    version,
                    group,
                    self.lightmap_encoding_quality.get(key).copied(),
                    self.hdr_cubemap_encoding_quality.get(key).copied(),
                )
            })
            .unwrap_or_default()
    }

    /// The defines of a build target group; PlayerSettings stores iOS as `iPhone`.
    pub fn defines_for(&self, group: &str) -> &[String] {
        self.scripting_defines
            .get(defines_key(group))
            .map(Vec::as_slice)
            .unwrap_or_default()
    }

    /// Replaces a build target group's defines (`PlayerSettings.SetScriptingDefineSymbols`).
    pub fn set_defines(&mut self, group: &str, defines: Vec<String>) {
        self.scripting_defines
            .insert(defines_key(group).to_string(), defines);
    }

    /// The application identifier of a build target group; PlayerSettings stores iOS as `iPhone`.
    pub fn identifier_for(&self, group: &str) -> Option<&str> {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.application_identifiers.get(key).map(String::as_str)
    }

    /// The atlases among `asset_paths` (files, not folders) the Editor packs before Play mode under the project's
    /// Sprite Packer mode, or `None` when the mode packs none in Play mode.
    pub fn play_mode_atlases<'a>(
        &self,
        asset_paths: impl IntoIterator<Item = &'a str>,
    ) -> Option<PlayModeAtlases> {
        let (mode, extensions): (_, &[&str]) = match self.sprite_packer_mode {
            ALWAYS_ON_ATLAS => ("Sprite Atlas V1 - Always Enabled", &[".spriteatlas"]),
            SPRITE_ATLAS_V2 => (
                "Sprite Atlas V2 - Enabled",
                &[".spriteatlasv2", ".spriteatlas"],
            ),
            _ => return None,
        };
        let mut paths: Vec<String> = asset_paths
            .into_iter()
            .filter(|path| {
                extensions.iter().any(|extension| {
                    path.len() > extension.len()
                        && path.is_char_boundary(path.len() - extension.len())
                        && path[path.len() - extension.len()..].eq_ignore_ascii_case(extension)
                })
            })
            .map(String::from)
            .collect();
        paths.sort();
        (!paths.is_empty()).then_some(PlayModeAtlases { mode, paths })
    }

    /// API compatibility level of the group is .NET Standard 2.1 (6), not .NET Framework (3).
    pub fn uses_net_standard(&self, group: &str) -> bool {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.api_compatibility
            .get(key)
            .copied()
            .unwrap_or(self.default_api_compatibility)
            == 6
    }
}

/// The key PlayerSettings stores a build target group's defines under (`iPhone` for iOS).
pub fn defines_key(group: &str) -> &str {
    if group == "iOS" { "iPhone" } else { group }
}

/// Scripting defines as PlayerSettings holds them: separated by `;`, trimmed, empty ones dropped.
pub fn parse_defines(defines: &str) -> Vec<String> {
    defines
        .split(';')
        .map(str::trim)
        .filter(|s| !s.is_empty())
        .map(String::from)
        .collect()
}

#[cfg(test)]
mod tests {
    use super::{PlayModeAtlases, Settings, TierDefaults, TierSettings, parse_defines};
    use unbound_compat::GraphicsDevice::{Metal, OpenGLCore};

    /// A project without InputManager.asset gets the Editor's default file once; an existing one
    /// is kept as it is (Unity 6000.0.71f1, tests/projects/inputdefaults).
    #[test]
    fn a_missing_input_manager_is_written_with_the_editors_defaults() {
        let project =
            std::env::temp_dir().join(format!("unbound-input-defaults-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&project);
        std::fs::create_dir_all(project.join("ProjectSettings")).unwrap();
        let profile = unbound_compat::Profile::for_version("6000.0.67f1".parse().unwrap()).unwrap();
        let file = project.join("ProjectSettings/InputManager.asset");
        super::write_missing_input_manager(&project, &profile);
        let written = std::fs::read_to_string(&file).unwrap();
        let input = unbound_yaml::parse_documents(&written)
            .unwrap()
            .remove(0)
            .body;
        let axes = input
            .get("m_Axes")
            .and_then(unbound_yaml::Value::as_seq)
            .unwrap();
        assert_eq!(axes.len(), 18);
        assert_eq!(
            axes[0].get("m_Name").and_then(unbound_yaml::Value::as_str),
            Some("Horizontal")
        );
        std::fs::write(&file, "kept").unwrap();
        super::write_missing_input_manager(&project, &profile);
        assert_eq!(std::fs::read_to_string(&file).unwrap(), "kept");
        let _ = std::fs::remove_dir_all(&project);
    }

    #[test]
    fn set_defines_replace_a_groups_symbols_under_the_key_player_settings_uses() {
        assert_eq!(
            parse_defines(" A; ;B ;C;"),
            vec!["A".to_string(), "B".into(), "C".into()]
        );
        assert!(parse_defines("").is_empty());
        let mut settings = Settings::default();
        settings.set_defines("iOS", parse_defines("ON"));
        settings.set_defines("Standalone", parse_defines("X;Y"));
        // iOS is stored as iPhone, as PlayerSettings does; both names read it back.
        assert_eq!(settings.defines_for("iOS"), ["ON"]);
        assert_eq!(settings.defines_for("iPhone"), ["ON"]);
        assert!(settings.scripting_defines.contains_key("iPhone"));
        assert_eq!(settings.defines_for("Standalone"), ["X", "Y"]);
        assert!(settings.defines_for("Android").is_empty());
        settings.set_defines("Standalone", Vec::new());
        assert!(settings.defines_for("Standalone").is_empty());
    }

    #[test]
    fn play_mode_atlases_follow_the_sprite_packer_mode() {
        let paths = [
            "Assets/UI/b.spriteatlas",
            "Assets/UI/a.spriteatlas",
            "Assets/UI/c.spriteatlasv2",
            "Assets/UI/C.SPRITEATLASV2",
            "Assets/UI/d.spriteatlasv2.png",
            "Assets/UI/e.png",
            ".spriteatlas",
        ];
        let with_mode = |mode| Settings {
            sprite_packer_mode: mode,
            ..Settings::default()
        };
        assert_eq!(
            with_mode(4).play_mode_atlases(paths),
            Some(PlayModeAtlases {
                mode: "Sprite Atlas V1 - Always Enabled",
                paths: vec![
                    "Assets/UI/a.spriteatlas".into(),
                    "Assets/UI/b.spriteatlas".into()
                ],
            })
        );
        // V2 mode packs the V1 atlases too.
        assert_eq!(
            with_mode(5).play_mode_atlases(paths).unwrap().paths,
            [
                "Assets/UI/C.SPRITEATLASV2",
                "Assets/UI/a.spriteatlas",
                "Assets/UI/b.spriteatlas",
                "Assets/UI/c.spriteatlasv2"
            ]
        );
        // Disabled, packing for builds only, and the legacy packer: nothing packed in Play mode.
        for mode in [0, 1, 2, 3, 6] {
            assert_eq!(with_mode(mode).play_mode_atlases(paths), None, "{mode}");
        }
        // A packing mode without atlases warns about nothing.
        assert_eq!(with_mode(5).play_mode_atlases(["Assets/a.png"]), None);
        let many: Vec<String> = (0..7).map(|i| format!("Assets/{i}.spriteatlas")).collect();
        let message = with_mode(4)
            .play_mode_atlases(many.iter().map(String::as_str))
            .unwrap()
            .edit_mode_message();
        assert!(
            message.contains("Atlases (7): Assets/0.spriteatlas, "),
            "{message}"
        );
        assert!(message.ends_with("Assets/4.spriteatlas +2"), "{message}");
    }

    #[test]
    fn sprite_packer_mode_reads_editor_settings() {
        let dir = std::env::temp_dir().join(format!("unbound-packer-{}", std::process::id()));
        std::fs::create_dir_all(dir.join("ProjectSettings")).unwrap();
        let settings = Settings::read(&dir).unwrap();
        assert_eq!(settings.sprite_packer_mode, 0);
        std::fs::write(
            dir.join("ProjectSettings/EditorSettings.asset"),
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!159 &1\nEditorSettings:\n  \
             m_ObjectHideFlags: 0\n  serializedVersion: 12\n  m_SpritePackerMode: 5\n",
        )
        .unwrap();
        let settings = Settings::read(&dir).unwrap();
        std::fs::remove_dir_all(&dir).ok();
        assert_eq!(settings.sprite_packer_mode, 5);
    }

    fn with_orientation(default_orientation: i64, autorotate: [bool; 4]) -> Settings {
        Settings {
            default_orientation,
            autorotate,
            default_screen: (1024, 768),
            ..Settings::default()
        }
    }

    #[test]
    fn phones_open_in_the_projects_orientation() {
        // Auto-rotation limited to landscape (the test game).
        let landscape = with_orientation(4, [false, false, true, true]);
        assert_eq!(landscape.default_screen_for("Android"), (960, 540));
        // Auto-rotation into any orientation starts in portrait.
        let any = with_orientation(4, [true, true, true, true]);
        assert_eq!(any.default_screen_for("iOS"), (540, 960));
        assert_eq!(
            with_orientation(3, [true; 4]).default_screen_for("Android"),
            (960, 540)
        );
        assert_eq!(
            with_orientation(0, [true; 4]).default_screen_for("Android"),
            (540, 960)
        );
        assert_eq!(landscape.default_screen_for("Standalone"), (1024, 768));
    }

    #[test]
    fn graphics_api_lists_read_by_hand_set_platforms_only() {
        let dir = std::env::temp_dir().join(format!("unbound-gfxapis-{}", std::process::id()));
        std::fs::create_dir_all(dir.join("ProjectSettings")).unwrap();
        std::fs::write(
            dir.join("ProjectSettings/ProjectSettings.asset"),
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  \
             productName: P\n  productGUID: 4588963874733d2918f18865c29aebdc\n  m_BuildTargetGraphicsAPIs:\n  - m_BuildTarget: LinuxStandaloneSupport\n    \
             m_APIs: 1500000011000000\n    m_Automatic: 0\n  - m_BuildTarget: AndroidPlayer\n    \
             m_APIs: 0b000000\n    m_Automatic: 1\n",
        )
        .unwrap();
        let settings = Settings::read(&dir).unwrap();
        std::fs::remove_dir_all(&dir).ok();
        assert_eq!(settings.product_guid, "4588963874733d2918f18865c29aebdc");
        assert_eq!(
            settings.graphics_apis.get("LinuxStandaloneSupport"),
            Some(&vec![21, 17])
        );
        // An automatic platform uses its defaults, whatever its list says.
        assert!(!settings.graphics_apis.contains_key("AndroidPlayer"));
        if cfg!(target_os = "linux") {
            let request = settings.device_request(None);
            assert_eq!(
                unbound_compat::candidates(&request, unbound_compat::HostOs::Linux),
                [
                    unbound_compat::GraphicsDevice::Vulkan,
                    unbound_compat::GraphicsDevice::OpenGLCore
                ]
            );
        }
    }

    /// iOS's defines are unmeasured and stay the desktop set they were before its tiers had a
    /// rule of their own.
    #[test]
    fn ios_keeps_the_desktop_defines() {
        let settings = Settings::default();
        assert_eq!(
            settings.shader_defines("iOS", OpenGLCore),
            settings.shader_defines("Standalone", OpenGLCore)
        );
        assert_eq!(settings.tier_settings(4, 2).standard_shader_quality, 1);
    }

    /// The automatic tiers of tests/projects/tiersettings (desktop path DeferredShading, mobile
    /// VertexLit) and its hand-set Standalone Tier2.
    #[test]
    fn tier_settings_follow_the_platform_group_and_tier() {
        let defaults = TierDefaults {
            rendering_path: 3,
            mobile_rendering_path: 0,
            mode_2d: false,
        };
        let desktop = TierSettings::automatic(1, 0, &defaults);
        assert_eq!(
            (
                desktop.standard_shader_quality,
                desktop.hdr_mode,
                desktop.rendering_path
            ),
            (2, 1, 3)
        );
        assert!(desktop.hdr && desktop.box_projection && !desktop.prefer_32bit_shadow_maps);
        assert_eq!(desktop.realtime_gi_cpu_usage, 25);
        assert_eq!(
            TierSettings::automatic(1, 2, &defaults).realtime_gi_cpu_usage,
            50
        );
        // WebGL and unnamed groups are desktop ones; Standalone alone has medium GI at Tier3.
        assert_eq!(
            TierSettings::automatic(13, 2, &defaults).realtime_gi_cpu_usage,
            25
        );
        assert_eq!(
            TierSettings::automatic(13, 0, &defaults),
            TierSettings::automatic(TierSettings::OTHER_GROUP, 0, &defaults)
        );
        for group in [4, 7] {
            let lowest = TierSettings::automatic(group, 0, &defaults);
            assert_eq!(
                (
                    lowest.standard_shader_quality,
                    lowest.hdr_mode,
                    lowest.rendering_path
                ),
                (0, 2, 0)
            );
            assert!(!lowest.hdr && !lowest.box_projection && !lowest.dither_alpha_shadows);
            assert_eq!(
                TierSettings::automatic(group, 1, &defaults).standard_shader_quality,
                1
            );
        }
        assert_eq!(
            TierSettings::automatic(25, 0, &defaults).standard_shader_quality,
            1
        );
        // Windows Store: its lowest tier alone is low-end.
        let wsa = TierSettings::automatic(14, 0, &defaults);
        assert_eq!(
            (
                wsa.standard_shader_quality,
                wsa.hdr_mode,
                wsa.box_projection
            ),
            (0, 2, false)
        );
        let wsa = TierSettings::automatic(14, 1, &defaults);
        assert_eq!(
            (
                wsa.standard_shader_quality,
                wsa.hdr_mode,
                wsa.box_projection
            ),
            (2, 1, true)
        );
        // A 2D project's automatic tiers have HDR off.
        let flat = TierDefaults {
            mode_2d: true,
            ..defaults
        };
        assert!(!TierSettings::automatic(1, 0, &flat).hdr);

        let dir = std::env::temp_dir().join(format!("unbound-tiers-{}", std::process::id()));
        std::fs::create_dir_all(dir.join("ProjectSettings")).unwrap();
        let source = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/tiersettings/ProjectSettings/GraphicsSettings.asset");
        std::fs::copy(source, dir.join("ProjectSettings/GraphicsSettings.asset")).unwrap();
        let settings = Settings::read(&dir).unwrap();
        std::fs::remove_dir_all(&dir).ok();
        assert_eq!(settings.tier_defaults, defaults);
        let set = settings.tier_settings(1, 1);
        assert_eq!(
            (
                set.standard_shader_quality,
                set.hdr_mode,
                set.rendering_path,
                set.realtime_gi_cpu_usage
            ),
            (0, 2, 0, 75)
        );
        assert!(set.prefer_32bit_shadow_maps && !set.hdr && !set.cascaded_shadow_maps);
        assert_eq!(
            settings.tier_settings(1, 2),
            TierSettings::automatic(1, 2, &defaults)
        );
    }

    #[test]
    fn metal_compiles_with_framebuffer_fetch_and_its_fallback_while_its_list_has_metal() {
        let desktop = Settings::default().shader_defines("Standalone", OpenGLCore);
        let mut metal = desktop.clone();
        metal.extend([
            "UNITY_FRAMEBUFFER_FETCH_AVAILABLE",
            "UNITY_NEEDS_RENDERPASS_FBFETCH_FALLBACK",
        ]);
        assert_eq!(
            Settings::default().shader_defines("Standalone", Metal),
            metal
        );
        // A macOS list without Metal (OpenGL Core, Vulkan) drops the fallback.
        let mut settings = Settings::default();
        settings
            .graphics_apis
            .insert("MacStandaloneSupport".into(), vec![17, 21]);
        assert_eq!(
            settings.shader_defines("Standalone", Metal).last(),
            Some(&"UNITY_FRAMEBUFFER_FETCH_AVAILABLE")
        );
        settings
            .graphics_apis
            .insert("MacStandaloneSupport".into(), vec![17, 16]);
        assert_eq!(settings.shader_defines("Standalone", Metal), metal);
        // With Android active: framebuffer fetch without the fallback.
        assert_eq!(
            Settings::default().shader_defines("Android", Metal).last(),
            Some(&"UNITY_FRAMEBUFFER_FETCH_AVAILABLE")
        );
    }

    #[test]
    fn shader_defines_follow_the_targets_tier_settings() {
        let desktop = Settings::default().shader_defines("Standalone", OpenGLCore);
        assert_eq!(
            desktop,
            [
                "SHADER_API_DESKTOP",
                "UNITY_PBS_USE_BRDF1",
                "UNITY_SPECCUBE_BOX_PROJECTION",
                "UNITY_SPECCUBE_BLENDING",
                "UNITY_ENABLE_DETAIL_NORMALMAP",
                "UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS",
                "UNITY_LIGHT_PROBE_PROXY_VOLUME",
                "UNITY_HARDWARE_TIER3",
                "UNITY_LIGHTMAP_FULL_HDR",
            ]
        );
        // Measured: Android's automatic tiers.
        assert_eq!(
            Settings::default().shader_defines("Android", OpenGLCore),
            [
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_NO_RGBM",
                "UNITY_NO_CUBEMAP_ARRAY",
                "UNITY_PBS_USE_BRDF2",
                "UNITY_NO_SCREENSPACE_SHADOWS",
                "UNITY_HARDWARE_TIER3",
                "UNITY_LIGHTMAP_FULL_HDR",
            ]
        );
        // Measured: every Android tier set alike, low quality, cascades on.
        let low = TierSettings {
            standard_shader_quality: 0,
            box_projection: false,
            probe_blending: false,
            detail_normal_map: false,
            cascaded_shadow_maps: true,
            dither_alpha_shadows: false,
            light_probe_proxy_volume: false,
            ..TierSettings::automatic(7, 2, &TierDefaults::default())
        };
        let mut settings = Settings::default();
        for tier in 0..3 {
            settings.tier_settings.insert((7, tier), low);
        }
        assert_eq!(
            settings.shader_defines("Android", OpenGLCore),
            [
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_NO_RGBM",
                "UNITY_NO_CUBEMAP_ARRAY",
                "UNITY_PBS_USE_BRDF3",
                "UNITY_LIGHTMAP_FULL_HDR"
            ]
        );
        // A lower tier set otherwise: the highest tier's, named.
        settings.tier_settings.insert(
            (7, 0),
            TierSettings {
                standard_shader_quality: 2,
                ..low
            },
        );
        assert_eq!(
            settings.shader_defines("Android", OpenGLCore),
            [
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_NO_RGBM",
                "UNITY_NO_CUBEMAP_ARRAY",
                "UNITY_PBS_USE_BRDF3",
                "UNITY_HARDWARE_TIER3",
                "UNITY_LIGHTMAP_FULL_HDR"
            ]
        );
    }

    #[test]
    fn lightmap_encoding_follows_the_targets_player_settings() {
        let dir = std::env::temp_dir().join(format!("unbound-encodings-{}", std::process::id()));
        std::fs::create_dir_all(dir.join("ProjectSettings")).unwrap();
        std::fs::write(
            dir.join("ProjectSettings/ProjectVersion.txt"),
            "m_EditorVersion: 6000.0.71f1\n",
        )
        .unwrap();
        std::fs::write(
            dir.join("ProjectSettings/ProjectSettings.asset"),
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  \
             productName: P\n  m_BuildTargetGroupLightmapEncodingQuality:\n  - serializedVersion: 2\n    \
             m_BuildTarget: Standalone\n    m_EncodingQuality: 1\n  \
             m_BuildTargetGroupHDRCubemapEncodingQuality:\n  - serializedVersion: 2\n    \
             m_BuildTarget: Android\n    m_EncodingQuality: 2\n",
        )
        .unwrap();
        let settings = Settings::read(&dir).unwrap();
        std::fs::remove_dir_all(&dir).ok();
        use unbound_compat::HdrEncoding::*;
        let android = settings.lighting_encodings("Android");
        // Android's lightmaps keep their default (Low); its cubemaps are set High.
        assert_eq!((android.lightmap, android.cubemap), (DoubleLdr, Native));
        let standalone = settings.lighting_encodings("Standalone");
        assert_eq!((standalone.lightmap, standalone.cubemap), (Rgbm, Native));
        assert_eq!(
            settings.shader_defines("Android", OpenGLCore).last(),
            Some(&"UNITY_LIGHTMAP_DLDR_ENCODING")
        );
        assert_eq!(
            settings.shader_defines("Standalone", OpenGLCore).last(),
            Some(&"UNITY_LIGHTMAP_RGBM_ENCODING")
        );
    }
}
