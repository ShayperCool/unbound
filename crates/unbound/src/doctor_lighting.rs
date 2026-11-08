//! GI impact comes from the same scene references, lighting asset reader and contributor
//! predicate used at runtime. Lighting Settings alone are precomputation inputs.
use anyhow::Result;
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use unbound_project::Project;

#[derive(Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Input {
    #[serde(rename = "realtimeGI")]
    realtime_gi: Option<bool>,
    data_guid: Option<String>,
    #[serde(default)]
    data_id: i64,
    settings_guid: Option<String>,
    #[serde(default)]
    settings_id: i64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Entry {
    scene: Option<String>,
    settings: Option<String>,
    reading: &'static str,
    contributors: usize,
    detail: String,
}

#[derive(Default)]
pub(super) struct Inventory {
    settings: BTreeMap<(String, i64), (String, bool)>,
    scenes: Vec<(String, String, Input)>,
}

impl Inventory {
    pub(super) fn add(&mut self, path: &str, guid: &str, id: i64, input: Input) {
        if let Some(enabled) = input.realtime_gi {
            self.settings
                .insert((guid.to_ascii_lowercase(), id), (path.into(), enabled));
        } else {
            self.scenes.push((path.into(), guid.into(), input));
        }
    }

    pub(super) fn report(
        self,
        project: &Project,
        runtime: &unbound_host::ManagedRuntime,
    ) -> Result<Vec<Entry>> {
        let db = project.asset_db();
        self.inspect(|guid, id| {
            let data =
                unbound_runtime::lighting::lighting_data(&db, &project.install.data, guid, id)?;
            runtime.call("realtime-gi-contributors", &data)
        })
    }

    fn inspect(self, mut read: impl FnMut(&str, i64) -> Result<usize>) -> Result<Vec<Entry>> {
        let mut output = Vec::new();
        // Shared lighting data is decoded once, including failures. A broken reference must
        // never be described as proof that no realtime data was precomputed.
        let mut counts = BTreeMap::new();
        let mut used = BTreeSet::new();
        for (scene, guid, input) in self.scenes {
            let key = (
                input
                    .settings_guid
                    .as_deref()
                    .unwrap_or(&guid)
                    .to_ascii_lowercase(),
                input.settings_id,
            );
            let settings = self.settings.get(&key);
            let settings_path = settings.map(|(path, _)| path.clone());
            let count = if input.data_id == 0 {
                Ok(0)
            } else {
                let data = (
                    input
                        .data_guid
                        .as_deref()
                        .unwrap_or(&guid)
                        .to_ascii_lowercase(),
                    input.data_id,
                );
                counts
                    .entry(data.clone())
                    .or_insert_with(|| read(&data.0, data.1).map_err(|error| format!("{error:#}")))
                    .clone()
            };
            let (reading, contributors, detail) = match count {
                Ok(count) if count > 0 => (
                    "Unsupported", count,
                    "Enlighten realtime GI unsupported: precomputed realtime lightmaps would contribute bounced light at runtime".into(),
                ),
                Err(error) => ("Incomplete", 0, format!("cannot determine realtime GI impact: {error}")),
                Ok(_) if settings.is_some_and(|(_, enabled)| *enabled) => (
                    "Informational", 0,
                    "realtime GI settings on, nothing precomputed: no realtime lightmap contributors in the scene's loaded lighting data".into(),
                ),
                Ok(_) if input.settings_id != 0 && settings.is_none() => (
                    "Incomplete", 0,
                    format!("Lighting Settings reference unresolved: {} fileID {}; no realtime lightmap contributors in the loaded lighting data", key.0, key.1),
                ),
                Ok(_) => continue,
            };
            used.insert(key);
            output.push(Entry {
                scene: Some(scene),
                settings: settings_path,
                reading,
                contributors,
                detail,
            });
        }
        for (key, (path, enabled)) in self.settings {
            if enabled && !used.contains(&key) {
                output.push(Entry {
                    scene: None, settings: Some(path), reading: "Informational", contributors: 0,
                    detail: "realtime GI settings on, nothing precomputed: no scanned scene uses this settings object with realtime lightmap contributors".into(),
                });
            }
        }
        Ok(output)
    }
}

pub(super) fn print(entries: &[Entry]) {
    println!("\nRealtime GI impact:");
    for entry in entries {
        println!(
            "  {}: {}{}: {}",
            entry.reading,
            entry.scene.as_deref().unwrap_or("unassigned settings"),
            entry
                .settings
                .as_ref()
                .map(|path| format!(" ({path})"))
                .unwrap_or_default(),
            entry.detail
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn managed_acronym_is_preserved_in_lighting_inputs() {
        let input: Input = serde_json::from_value(serde_json::json!({"realtimeGI": true})).unwrap();
        assert_eq!(input.realtime_gi, Some(true));
    }

    #[test]
    fn impact_uses_loaded_data_and_scene_references_including_settings_changed_after_bake() {
        let mut inventory = Inventory::default();
        for (guid, enabled) in [("enabled", true), ("off", false), ("unused", true)] {
            inventory.add(
                &format!("{guid}.lighting"),
                guid,
                1,
                Input {
                    realtime_gi: Some(enabled),
                    ..Input::default()
                },
            );
        }
        for (path, settings, data) in [
            ("plain", "enabled", 0),
            ("baked", "enabled", 1),
            ("edited-after-bake", "off", 1),
            ("broken", "enabled", 2),
            ("ordinary-baked", "off", 3),
        ] {
            inventory.add(
                path,
                "scene",
                157,
                Input {
                    settings_guid: Some(settings.into()),
                    settings_id: 1,
                    data_guid: Some("data".into()),
                    data_id: data,
                    ..Input::default()
                },
            );
        }
        let mut reads = 0;
        let entries = inventory
            .inspect(|guid, id| {
                assert_eq!(guid, "data");
                reads += 1;
                match id {
                    1 => Ok(2),
                    2 => anyhow::bail!("missing lighting data"),
                    _ => Ok(0),
                }
            })
            .unwrap();
        assert_eq!(reads, 3);
        assert_eq!(
            entries
                .iter()
                .filter(|e| e.reading == "Unsupported")
                .count(),
            2
        );
        assert_eq!(
            entries
                .iter()
                .find(|e| e.scene.as_deref() == Some("plain"))
                .unwrap()
                .reading,
            "Informational"
        );
        assert_eq!(
            entries
                .iter()
                .find(|e| e.scene.as_deref() == Some("broken"))
                .unwrap()
                .reading,
            "Incomplete"
        );
        assert_eq!(entries.iter().filter(|e| e.scene.is_none()).count(), 1);
        assert!(
            !entries
                .iter()
                .any(|e| e.scene.as_deref() == Some("ordinary-baked"))
        );
    }

    #[test]
    fn local_references_keep_their_owning_file_identity_and_missing_settings_stay_visible() {
        let mut inventory = Inventory::default();
        inventory.add(
            "local.unity",
            "ABC",
            9,
            Input {
                realtime_gi: Some(true),
                ..Input::default()
            },
        );
        for (path, guid) in [("local.unity", "ABC"), ("missing.unity", "DEF")] {
            inventory.add(
                path,
                guid,
                157,
                Input {
                    settings_id: 9,
                    ..Input::default()
                },
            );
        }
        let entries = inventory
            .inspect(|_, _| panic!("null data must not be loaded"))
            .unwrap();
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[0].reading, "Informational");
        assert_eq!(entries[0].settings.as_deref(), Some("local.unity"));
        assert_eq!(entries[1].reading, "Incomplete");
    }
}
