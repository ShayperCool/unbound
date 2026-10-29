//! What the project's `ProjectSettings/` says that matters before content loads.
use anyhow::{Context, Result};
use std::{collections::BTreeMap, path::Path};
use unbound_compat::InputHandling;
use unbound_yaml::{Document, Guid, Value};

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
    /// `GraphicsSettings.m_TierSettings` the project sets by hand, by (BuildTargetGroup, tier).
    pub tier_settings: BTreeMap<(i64, i64), TierSettings>,
}

/// A graphics tier's settings, which choose the built-in shader defines.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TierSettings {
    /// 0 low, 1 medium, 2 high: the Standard shaders' BRDF 3, 2 or 1.
    pub standard_shader_quality: i64,
    pub box_projection: bool,
    pub probe_blending: bool,
    pub detail_normal_map: bool,
    pub cascaded_shadow_maps: bool,
    pub dither_alpha_shadows: bool,
}

impl TierSettings {
    /// What the Editor uses for a tier left automatic. Android's highest tier is measured; the
    /// desktop one is the Linux Editor's OpenGLCore set (measured with `HasShaderDefine`).
    fn automatic(mobile: bool) -> Self {
        Self {
            standard_shader_quality: if mobile { 1 } else { 2 },
            box_projection: !mobile,
            probe_blending: !mobile,
            detail_normal_map: !mobile,
            cascaded_shadow_maps: !mobile,
            dither_alpha_shadows: !mobile,
        }
    }

    fn read(settings: &Value) -> Self {
        let flag = |key: &str| settings.get(key).and_then(Value::as_i64).unwrap_or(0) != 0;
        Self {
            standard_shader_quality: settings
                .get("standardShaderQuality")
                .and_then(Value::as_i64)
                .unwrap_or(2),
            box_projection: flag("useReflectionProbeBoxProjection"),
            probe_blending: flag("useReflectionProbeBlending"),
            detail_normal_map: flag("useDetailNormalMap"),
            cascaded_shadow_maps: flag("useCascadedShadowMaps"),
            dither_alpha_shadows: flag("useDitherMaskForAlphaBlendedShadows"),
        }
    }
}

fn first_document(path: &Path) -> Result<Option<Document>> {
    let Ok(text) = std::fs::read_to_string(path) else {
        return Ok(None);
    };
    let docs = unbound_yaml::parse_documents(&text).with_context(|| path.display().to_string())?;
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
                for (group, symbols) in map.iter() {
                    let list = symbols
                        .as_str()
                        .unwrap_or_default()
                        .split(';')
                        .map(str::trim)
                        .filter(|s| !s.is_empty())
                        .map(String::from)
                        .collect();
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
        }
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
        if let Some(graphics) = first_document(&dir.join("GraphicsSettings.asset"))? {
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
                    settings
                        .tier_settings
                        .insert((group, index), TierSettings::read(values));
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
    /// normal maps unpacked from RGB), then come those of the highest graphics
    /// tier the Editor runs, which `UNITY_HARDWARE_TIER3` names only when the tiers differ.
    /// `UNITY_LIGHT_PROBE_PROXY_VOLUME` is left out: proxy volumes are not supported, and
    /// without one its branch is off.
    pub fn shader_defines(&self, group: &str) -> Vec<&'static str> {
        // BuildTargetGroup: Standalone 1, iOS 4, Android 7, WebGL 13.
        let (id, mobile) = match group {
            "Android" => (7, true),
            "iOS" | "iPhone" => (4, false),
            "WebGL" => (13, false),
            _ => (1, false),
        };
        let tier = |index| {
            self.tier_settings
                .get(&(id, index))
                .copied()
                .unwrap_or_else(|| TierSettings::automatic(mobile))
        };
        let tiers = [tier(0), tier(1), tier(2)];
        // Automatic tiers differ from each other.
        let tiers_differ = tiers.iter().any(|t| *t != tiers[0])
            || (0..3).any(|i| !self.tier_settings.contains_key(&(id, i)));
        let high = tiers[2];
        let mut defines = if mobile {
            vec!["SHADER_API_MOBILE", "UNITY_NO_DXT5nm"]
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
            (tiers_differ, "UNITY_HARDWARE_TIER3"),
        ] {
            if on {
                defines.push(define);
            }
        }
        defines
    }

    /// The defines of a build target group; PlayerSettings stores iOS as `iPhone`.
    pub fn defines_for(&self, group: &str) -> &[String] {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.scripting_defines
            .get(key)
            .map(Vec::as_slice)
            .unwrap_or_default()
    }

    /// The application identifier of a build target group; PlayerSettings stores iOS as `iPhone`.
    pub fn identifier_for(&self, group: &str) -> Option<&str> {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.application_identifiers.get(key).map(String::as_str)
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

#[cfg(test)]
mod tests {
    use super::{Settings, TierSettings};

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
    fn shader_defines_follow_the_targets_tier_settings() {
        let desktop = Settings::default().shader_defines("Standalone");
        assert_eq!(
            desktop,
            [
                "SHADER_API_DESKTOP",
                "UNITY_PBS_USE_BRDF1",
                "UNITY_SPECCUBE_BOX_PROJECTION",
                "UNITY_SPECCUBE_BLENDING",
                "UNITY_ENABLE_DETAIL_NORMALMAP",
                "UNITY_USE_DITHER_MASK_FOR_ALPHABLENDED_SHADOWS",
                "UNITY_HARDWARE_TIER3",
            ]
        );
        // Measured: Android's automatic tiers.
        assert_eq!(
            Settings::default().shader_defines("Android"),
            [
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_PBS_USE_BRDF2",
                "UNITY_NO_SCREENSPACE_SHADOWS",
                "UNITY_HARDWARE_TIER3",
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
        };
        let mut settings = Settings::default();
        for tier in 0..3 {
            settings.tier_settings.insert((7, tier), low);
        }
        assert_eq!(
            settings.shader_defines("Android"),
            [
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_PBS_USE_BRDF3"
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
            settings.shader_defines("Android"),
            [
                "SHADER_API_MOBILE",
                "UNITY_NO_DXT5nm",
                "UNITY_PBS_USE_BRDF3",
                "UNITY_HARDWARE_TIER3"
            ]
        );
    }
}
