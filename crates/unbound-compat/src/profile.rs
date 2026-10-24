//! Compatibility profiles: everything that differs between Unity releases.
//!
//! A profile covers one release family (`6000.0`, `2022.3`, ...). Its data tables (define symbols,
//! compiler options) are captured from real Editor compiles and live in `data/profiles/`; its
//! behaviour switches are [`Feature`]s with the first release that has them. Code asks
//! [`Profile::has`] instead of comparing versions itself.
use crate::version::UnityVersion;
use serde::Deserialize;
use std::collections::BTreeMap;

/// Per-family tables, `data/profiles/unity-<family>.json`.
#[derive(Debug, Clone, Deserialize)]
pub struct ProfileData {
    pub family: String,
    #[serde(default)]
    pub source: String,
    /// Engine defines every assembly gets regardless of platform.
    pub common: Vec<String>,
    /// Defines of the active build target, by build target group name.
    pub platforms: BTreeMap<String, Vec<String>>,
    /// Scripting backend and API compatibility level defines (.NET Framework).
    pub scripting: Vec<String>,
    /// The same at the .NET Standard 2.1 API compatibility level.
    pub scripting_net_standard: Vec<String>,
    /// What the Editor adds when it compiles for Play mode.
    pub editor: Vec<String>,
    /// Added to assemblies that only build for the Editor platform.
    pub editor_only_assembly: Vec<String>,
    /// Defines of built-in modules (`ENABLE_TILEMAP`, ...).
    pub modules: Vec<String>,
    /// Language level defines (`CSHARP_7_3_OR_NEWER`).
    pub language: Vec<String>,
    pub language_version: String,
    pub compiler_options: Vec<String>,
    /// The BCL reference directory inside `Editor/Data`.
    pub bcl: String,
    /// The assemblies of `bcl` the Editor references (with every facade); the rest of the
    /// directory (Mono's extras, cscompmgd, ...) is not visible to scripts.
    pub bcl_assemblies: Vec<String>,
    pub source_generators: Vec<String>,
    /// Script assemblies the Editor does not IL post-process (its safe-mode list).
    #[serde(default)]
    pub skip_code_gen: Vec<String>,
}

/// Player Settings' active input handling (`activeInputHandler`): the legacy Input Manager, the
/// Input System package, or both. It decides the input defines and which input APIs work.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum InputHandling {
    #[default]
    InputManager,
    InputSystem,
    Both,
}

impl InputHandling {
    /// The `activeInputHandler` value (0, 1, 2; anything else is the Input Manager, as Unity reads it).
    pub fn from_setting(value: i64) -> Self {
        match value {
            1 => InputHandling::InputSystem,
            2 => InputHandling::Both,
            _ => InputHandling::InputManager,
        }
    }

    /// The defines the Editor emits for it, in its order (they take the place of the profile's
    /// `ENABLE_LEGACY_INPUT_MANAGER` among the module defines).
    pub fn defines(self) -> &'static [&'static str] {
        match self {
            InputHandling::InputManager => &["ENABLE_LEGACY_INPUT_MANAGER"],
            InputHandling::InputSystem => &["ENABLE_INPUT_SYSTEM"],
            InputHandling::Both => &["ENABLE_INPUT_SYSTEM", "ENABLE_LEGACY_INPUT_MANAGER"],
        }
    }
}

/// Named behaviour switches. Add one whenever Unity changes behaviour or API between releases.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Feature {
    /// `Rigidbody.linearVelocity` / `linearDamping` (6000.0); `velocity`/`drag` are obsolete aliases.
    RigidbodyLinearVelocity,
    /// `Object.FindObjectsByType` / `FindFirstObjectByType` (2021.3.18+, 2022.2+).
    FindObjectsByType,
    /// `Awaitable` async API (2023.1+).
    Awaitable,
    /// Text serialization writes `m_EditorClassIdentifier` and `serializedVersion` as in 6000.
    PrefabInstanceRemovedGameObjects,
    /// uGUI and TextMeshPro ship as one built-in package `com.unity.ugui` 2.0 (2023.2+).
    UguiContainsTextMeshPro,
    /// `InstantiateAsync` (2022.3.20+, 2023.3+).
    InstantiateAsync,
    /// Every assembly references `UnityEngine.UI`/`UnityEditor.UI` (uGUI left the engine in 2019.2).
    ImplicitUguiReferences,
    /// Editor-only assemblies reference the Test Runner and NUnit without naming them.
    ImplicitTestRunnerReferences,
}

impl Feature {
    pub const ALL: [Feature; 8] = [
        Feature::ImplicitUguiReferences,
        Feature::ImplicitTestRunnerReferences,
        Feature::RigidbodyLinearVelocity,
        Feature::FindObjectsByType,
        Feature::Awaitable,
        Feature::PrefabInstanceRemovedGameObjects,
        Feature::UguiContainsTextMeshPro,
        Feature::InstantiateAsync,
    ];

    /// The first release with the feature.
    pub fn since(self) -> UnityVersion {
        match self {
            Feature::RigidbodyLinearVelocity => UnityVersion::new(6000, 0, 0),
            Feature::FindObjectsByType => UnityVersion::new(2021, 3, 18),
            Feature::Awaitable => UnityVersion::new(2023, 1, 0),
            Feature::PrefabInstanceRemovedGameObjects => UnityVersion::new(2022, 1, 0),
            Feature::UguiContainsTextMeshPro => UnityVersion::new(2023, 2, 0),
            Feature::InstantiateAsync => UnityVersion::new(2022, 3, 20),
            Feature::ImplicitUguiReferences => UnityVersion::new(2019, 2, 0),
            Feature::ImplicitTestRunnerReferences => UnityVersion::new(2019, 2, 0),
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Feature::RigidbodyLinearVelocity => "RigidbodyLinearVelocity",
            Feature::FindObjectsByType => "FindObjectsByType",
            Feature::Awaitable => "Awaitable",
            Feature::PrefabInstanceRemovedGameObjects => "PrefabInstanceRemovedGameObjects",
            Feature::UguiContainsTextMeshPro => "UguiContainsTextMeshPro",
            Feature::InstantiateAsync => "InstantiateAsync",
            Feature::ImplicitUguiReferences => "ImplicitUguiReferences",
            Feature::ImplicitTestRunnerReferences => "ImplicitTestRunnerReferences",
        }
    }
}

/// How game code is compiled.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CompileMode {
    /// As the Editor compiles for Play mode: `UNITY_EDITOR` and the active target's defines.
    EditorPlayMode,
    /// As a development Player of the active target is compiled.
    Player,
}

#[derive(Debug, Clone)]
pub struct Profile {
    pub version: UnityVersion,
    pub data: ProfileData,
    /// True when no table matched the release exactly and the nearest older one is used.
    pub approximate: bool,
}

const BUNDLED: &[(&str, &str)] = &[(
    "6000.0",
    include_str!("../../../data/profiles/unity-6000.0.json"),
)];

/// The objects of the release's built-in resource files, by file id (see tools/builtin-resources).
const BUILTINS: &[(&str, &str)] = &[(
    "6000.0",
    include_str!("../../../data/profiles/builtin-6000.0.json"),
)];

impl Profile {
    /// The profile for a release: its own family's tables, else the newest older family's.
    pub fn for_version(version: UnityVersion) -> anyhow::Result<Self> {
        let mut families: Vec<((u32, u32), &str)> = BUNDLED
            .iter()
            .map(|(family, json)| {
                let (major, minor) = family.split_once('.').expect("family is major.minor");
                ((major.parse().unwrap(), minor.parse().unwrap()), *json)
            })
            .collect();
        families.sort_by_key(|(family, _)| *family);
        let chosen = families
            .iter()
            .rev()
            .find(|(family, _)| *family <= version.family())
            .or_else(|| families.first())
            .ok_or_else(|| anyhow::anyhow!("no compatibility profiles are bundled"))?;
        let data: ProfileData = serde_json::from_str(chosen.1)?;
        Ok(Self {
            version,
            approximate: chosen.0 != version.family(),
            data,
        })
    }

    /// `{"extra": {fileId: {class, name}}, "default": {...}}` of the profile's family.
    pub fn builtin_resources(&self) -> &'static str {
        BUILTINS
            .iter()
            .find(|(family, _)| *family == self.data.family)
            .or_else(|| BUILTINS.last())
            .map(|(_, json)| *json)
            .unwrap_or("{}")
    }

    pub fn has(&self, feature: Feature) -> bool {
        self.version >= feature.since()
    }

    /// `UNITY_6000_0_71`, `UNITY_6000_0`, `UNITY_6000` and every `UNITY_X_Y_OR_NEWER` up to this
    /// release, in the Editor's order.
    pub fn version_defines(&self) -> Vec<String> {
        let v = self.version;
        let mut out = vec![
            format!("UNITY_{}_{}_{}", v.major, v.minor, v.patch),
            format!("UNITY_{}_{}", v.major, v.minor),
            format!("UNITY_{}", v.major),
        ];
        // Release families Unity emits `_OR_NEWER` symbols for: (year, last minor).
        const FAMILIES: &[(u32, u32, u32)] = &[
            (5, 3, 6),
            (2017, 1, 4),
            (2018, 1, 4),
            (2019, 1, 4),
            (2020, 1, 3),
            (2021, 1, 3),
            (2022, 1, 3),
            (2023, 1, 3),
        ];
        for &(year, first, last) in FAMILIES {
            for minor in first..=last {
                if (year, minor) <= (v.major, v.minor) {
                    out.push(format!("UNITY_{year}_{minor}_OR_NEWER"));
                }
            }
        }
        if v.major >= 6000 {
            for minor in 0..=v.minor {
                out.push(format!("UNITY_6000_{minor}_OR_NEWER"));
            }
        }
        out
    }

    /// The defines an assembly is compiled with, before the project's own scripting defines and
    /// the assembly's `versionDefines`.
    pub fn engine_defines(
        &self,
        mode: CompileMode,
        target_group: &str,
        editor_only_assembly: bool,
        net_standard: bool,
        input: InputHandling,
    ) -> Vec<String> {
        let d = &self.data;
        let mut out = self.version_defines();
        out.extend(d.common.iter().cloned());
        out.extend(d.platforms.get(target_group).into_iter().flatten().cloned());
        // Editor-only assemblies build against .NET Framework whatever the level.
        if net_standard && !editor_only_assembly {
            out.extend(d.scripting_net_standard.iter().cloned());
        } else {
            out.extend(d.scripting.iter().cloned());
        }
        if mode == CompileMode::EditorPlayMode {
            out.extend(d.editor.iter().cloned());
        } else {
            out.push("DEVELOPMENT_BUILD".into());
        }
        for module in &d.modules {
            if module == "ENABLE_LEGACY_INPUT_MANAGER" {
                out.extend(input.defines().iter().map(|d| d.to_string()));
            } else {
                out.push(module.clone());
            }
        }
        if editor_only_assembly {
            out.extend(d.editor_only_assembly.iter().cloned());
        }
        dedup_in_order(&mut out);
        out
    }

    /// Language defines go last, after the project's scripting defines, as the Editor orders them.
    pub fn language_defines(&self) -> &[String] {
        &self.data.language
    }
}

fn dedup_in_order(list: &mut Vec<String>) {
    let mut seen = std::collections::HashSet::new();
    list.retain(|item| seen.insert(item.clone()));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_defines_match_the_editor() {
        let profile = Profile::for_version("6000.0.71f1".parse().unwrap()).unwrap();
        let defines = profile.version_defines();
        assert_eq!(
            &defines[..4],
            [
                "UNITY_6000_0_71",
                "UNITY_6000_0",
                "UNITY_6000",
                "UNITY_5_3_OR_NEWER"
            ]
        );
        assert_eq!(defines.last().unwrap(), "UNITY_6000_0_OR_NEWER");
        assert_eq!(defines.len(), 32);
        assert!(!profile.approximate);
        assert!(profile.has(Feature::RigidbodyLinearVelocity));
    }

    #[test]
    fn input_handling_selects_the_input_defines() {
        let profile = Profile::for_version("6000.0.71f1".parse().unwrap()).unwrap();
        let defines = |input| {
            profile.engine_defines(CompileMode::EditorPlayMode, "Standalone", false, true, input)
        };
        let legacy = defines(InputHandling::InputManager);
        assert!(legacy.contains(&"ENABLE_LEGACY_INPUT_MANAGER".to_string()));
        assert!(!legacy.contains(&"ENABLE_INPUT_SYSTEM".to_string()));
        // The Editor's order (tests/projects/input's Bee record): after ENABLE_TIMELINE.
        let new = defines(InputHandling::InputSystem);
        let at = new.iter().position(|d| d == "ENABLE_TIMELINE").unwrap();
        assert_eq!(new[at + 1], "ENABLE_INPUT_SYSTEM");
        assert!(!new.contains(&"ENABLE_LEGACY_INPUT_MANAGER".to_string()));
        let both = defines(InputHandling::Both);
        let at = both.iter().position(|d| d == "ENABLE_TIMELINE").unwrap();
        assert_eq!(both[at + 1..at + 3], ["ENABLE_INPUT_SYSTEM", "ENABLE_LEGACY_INPUT_MANAGER"]);
        assert_eq!(InputHandling::from_setting(1), InputHandling::InputSystem);
        assert_eq!(InputHandling::from_setting(7), InputHandling::InputManager);
    }

    #[test]
    fn older_release_uses_nearest_profile_but_own_defines() {
        let profile = Profile::for_version("2022.3.10f1".parse().unwrap()).unwrap();
        assert!(profile.approximate);
        assert!(!profile.has(Feature::RigidbodyLinearVelocity));
        assert_eq!(
            profile.version_defines().last().unwrap(),
            "UNITY_2022_3_OR_NEWER"
        );
    }
}
