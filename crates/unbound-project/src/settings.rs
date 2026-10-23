//! What the project's `ProjectSettings/` says that matters before content loads.
use anyhow::{Context, Result};
use std::{collections::BTreeMap, path::Path};
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
    /// Scripting define symbols by build target group (`Android`, `Standalone`, `iPhone`, ...).
    pub scripting_defines: BTreeMap<String, Vec<String>>,
    /// API compatibility level by group: 3 = .NET Framework, 6 = .NET Standard 2.1.
    pub api_compatibility: BTreeMap<String, i64>,
    pub allow_unsafe_code: bool,
    pub build_scenes: Vec<BuildScene>,
    /// `EditorBuildSettings.m_configObjects`: name → (guid, file id) of a project asset.
    pub config_objects: BTreeMap<String, (Guid, i64)>,
    pub tags: Vec<String>,
    pub layers: Vec<String>,
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
        if let Some(tags) = first_document(&dir.join("TagManager.asset"))? {
            settings.tags = string_list(tags.body.get("tags"));
            settings.layers = string_list(tags.body.get("layers"));
        }
        Ok(settings)
    }

    /// The defines of a build target group; PlayerSettings stores iOS as `iPhone`.
    pub fn defines_for(&self, group: &str) -> &[String] {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.scripting_defines
            .get(key)
            .map(Vec::as_slice)
            .unwrap_or_default()
    }

    /// API compatibility level of the group: .NET Framework (3) unless set to .NET Standard (6).
    pub fn uses_net_standard(&self, group: &str) -> bool {
        let key = if group == "iOS" { "iPhone" } else { group };
        self.api_compatibility.get(key).copied() == Some(6)
    }
}
