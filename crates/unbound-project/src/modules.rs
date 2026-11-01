//! Which of the engine's module assemblies script assemblies compile against.
//!
//! The installation lists the engine's modules for each platform
//! (`PlaybackEngines/<platform>/modules.asset`, the Editor's own list in `Resources/modules.asset`):
//! a module that is `controlledByBuiltinPackage` exists for the project only when its built-in
//! package (`com.unity.modules.<lowercase name>`) is among the resolved packages. The Editor
//! compiles a runtime assembly against `UnityEngine.dll` and the `UnityEngine.<Name>Module.dll` of
//! every module the active build target's list has and the project has; an Editor-only assembly
//! compiles against every module. Code using a left-out module's API fails to compile with the
//! compiler's own errors (CS1069 for a name `UnityEngine.dll` forwards to that module).
use crate::install::UnityInstall;
use anyhow::{Context, Result};
use std::{
    collections::BTreeSet,
    path::{Path, PathBuf},
};

/// One entry of a `modules.asset`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EngineModule {
    /// `ScreenCapture`: the assembly is `UnityEngine.ScreenCaptureModule`.
    pub name: String,
    /// Present only when its built-in package is resolved.
    pub controlled_by_builtin_package: bool,
}

impl EngineModule {
    pub fn assembly(&self) -> String {
        format!("UnityEngine.{}Module", self.name)
    }

    /// `com.unity.modules.screencapture`.
    pub fn package(&self) -> String {
        format!("com.unity.modules.{}", self.name.to_ascii_lowercase())
    }
}

/// A module the project's packages leave out of runtime assemblies.
#[derive(Debug, Clone, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DisabledModule {
    /// `UnityEngine.ScreenCaptureModule`
    pub assembly: String,
    /// `com.unity.modules.screencapture`
    pub package: String,
    /// The package's `displayName` (`Screen Capture`), else its name.
    pub display_name: String,
}

/// The engine modules of `target_group`'s player (`Standalone` is the host's own), else the
/// Editor's list; with the file they came from. None when the installation has neither.
pub fn modules_for(
    install: &UnityInstall,
    target_group: &str,
) -> Result<Option<(Vec<EngineModule>, PathBuf)>> {
    let platform = playback_engine(target_group).and_then(|name| {
        install
            .playback_engines_dirs()
            .into_iter()
            .map(|dir| dir.join(name).join("modules.asset"))
            .find(|file| file.is_file())
    });
    if let Some(file) = platform {
        return Ok(Some((read(&file)?, file)));
    }
    let editor = install.data.join("Resources/modules.asset");
    if !editor.is_file() {
        return Ok(None);
    }
    tracing::warn!(
        "the Unity installation has no module list for build target {target_group}; using the Editor's ({})",
        editor.display()
    );
    Ok(Some((read(&editor)?, editor)))
}

/// The `PlaybackEngines` folder of a build target group's player.
fn playback_engine(target_group: &str) -> Option<&'static str> {
    Some(match target_group {
        "Standalone" if cfg!(target_os = "macos") => "MacStandaloneSupport",
        "Standalone" if cfg!(target_os = "windows") => "WindowsStandaloneSupport",
        "Standalone" => "LinuxStandaloneSupport",
        "Android" => "AndroidPlayer",
        "iOS" | "iPhone" => "iOSSupport",
        "WebGL" => "WebGLSupport",
        _ => return None,
    })
}

/// The modules a `modules.asset` (`PlatformModuleSetup`) lists.
pub fn read(file: &Path) -> Result<Vec<EngineModule>> {
    let text = std::fs::read_to_string(file).with_context(|| file.display().to_string())?;
    let documents =
        unbound_yaml::parse_documents(&text).with_context(|| file.display().to_string())?;
    let setup = documents
        .iter()
        .find(|d| d.type_name == "PlatformModuleSetup")
        .with_context(|| format!("{}: no PlatformModuleSetup", file.display()))?;
    let modules = setup
        .body
        .get("modules")
        .and_then(unbound_yaml::Value::as_seq)
        .with_context(|| format!("{}: no modules", file.display()))?;
    modules
        .iter()
        .map(|m| {
            let name = m
                .get("name")
                .and_then(unbound_yaml::Value::as_str)
                .filter(|n| !n.is_empty());
            let controlled = m
                .get("controlledByBuiltinPackage")
                .and_then(unbound_yaml::Value::as_i64);
            match (name, controlled) {
                (Some(name), Some(controlled)) => Ok(EngineModule {
                    name: name.to_string(),
                    controlled_by_builtin_package: controlled != 0,
                }),
                _ => anyhow::bail!(
                    "{}: a module without name or controlledByBuiltinPackage",
                    file.display()
                ),
            }
        })
        .collect()
}

/// The module assemblies (`UnityEngine.<Name>Module`, file stems) a runtime assembly does not
/// compile against: those of the engine's reference assemblies that `modules` does not have, or
/// whose package `packages` lacks. The left-out modules a package controls are listed with it.
pub fn excluded(
    modules: &[EngineModule],
    engine_assemblies: &[String],
    packages: &BTreeSet<&str>,
    display_name: &dyn Fn(&str) -> Option<String>,
) -> (BTreeSet<String>, Vec<DisabledModule>) {
    let mut kept: BTreeSet<String> = BTreeSet::new();
    let mut disabled = Vec::new();
    for module in modules {
        let package = module.package();
        if !module.controlled_by_builtin_package || packages.contains(package.as_str()) {
            kept.insert(module.assembly());
        } else {
            disabled.push(DisabledModule {
                assembly: module.assembly(),
                display_name: display_name(&package).unwrap_or_else(|| package.clone()),
                package,
            });
        }
    }
    let excluded = engine_assemblies
        .iter()
        .filter(|name| {
            name.starts_with("UnityEngine.") && name.ends_with("Module") && !kept.contains(*name)
        })
        .cloned()
        .collect();
    (excluded, disabled)
}

#[cfg(test)]
mod tests {
    use super::*;

    const ASSET: &str = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!877146078 &1\nPlatformModuleSetup:\n  m_ObjectHideFlags: 0\n  modules:\n  - name: Core\n    dependencies:\n      - SharedInternals\n    strippable: 1\n    controlledByBuiltinPackage: 0\n  - name: ScreenCapture\n    dependencies:\n      - Core\n      - ImageConversion\n    strippable: 1\n    controlledByBuiltinPackage: 1\n  - name: ImageConversion\n    dependencies: []\n    strippable: 1\n    controlledByBuiltinPackage: 1\n";

    #[test]
    fn package_controlled_modules_follow_the_packages() {
        let dir = std::env::temp_dir().join(format!("unbound-modules-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("modules.asset");
        std::fs::write(&file, ASSET).unwrap();
        let modules = read(&file).unwrap();
        std::fs::remove_dir_all(&dir).unwrap();
        assert_eq!(modules.len(), 3);
        assert!(!modules[0].controlled_by_builtin_package);
        assert_eq!(modules[1].package(), "com.unity.modules.screencapture");
        let engine: Vec<String> = [
            "UnityEngine",
            "UnityEngine.CoreModule",
            "UnityEngine.ScreenCaptureModule",
            "UnityEngine.ImageConversionModule",
            "UnityEngine.ARModule",
        ]
        .iter()
        .map(|s| s.to_string())
        .collect();
        let packages = BTreeSet::from(["com.unity.modules.imageconversion"]);
        let (excluded, disabled) = excluded(&modules, &engine, &packages, &|p| {
            (p == "com.unity.modules.screencapture").then(|| "Screen Capture".to_string())
        });
        // Not in the platform's list (AR) or its package left out (ScreenCapture).
        assert_eq!(
            excluded,
            BTreeSet::from([
                "UnityEngine.ARModule".to_string(),
                "UnityEngine.ScreenCaptureModule".to_string()
            ])
        );
        assert_eq!(
            disabled,
            vec![DisabledModule {
                assembly: "UnityEngine.ScreenCaptureModule".into(),
                package: "com.unity.modules.screencapture".into(),
                display_name: "Screen Capture".into(),
            }]
        );
    }
}
