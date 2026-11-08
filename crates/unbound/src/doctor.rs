//! `unbound doctor`: what in the project Unbound does not support. It lists the Unity classes
//! of the objects in the project's scenes, prefabs and YAML assets that the engine does not read
//! (each also warns when it loads), with the files that hold them.
#[path = "doctor_fields.rs"]
mod fields;
#[path = "doctor_lighting.rs"]
mod lighting;

use crate::ProjectArgs;
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::json;
use std::{
    collections::{BTreeMap, BTreeSet},
    time::Instant,
};
use unbound_project::Project;

const RENDERING_LIMITATIONS: &[&str] = &[
    "Editor asynchronous shader-compilation startup delays are not reproduced; the FlareLayer probe controls compilation synchronously.",
    "SkinnedMeshRenderer GPU buffer access with active Cloth and previous-frame motion-vector buffers is not supported.",
    "Game-view Gizmos.DrawIcon, DrawGUITexture and DrawFrustum are not supported; Scene-view rendering is not available.",
];

const AUDIO_LIMITATIONS: &[&str] = &[
    "3D AudioDistortionFilter and AudioChorusFilter are measured only on Unity 6000.0.71f1; other releases reject these placements.",
    "AudioListener filter components interleaved with OnAudioFilterRead scripts, or sharing their GameObject with an AudioSource, are not supported.",
    "Activating or reactivating AudioReverbZones during playback is not supported; stop the sources and disable all zones before reactivating them. The unsupported zone return is silent.",
];

/// The keys whose referenced object turns an ignored scene settings class into an unread one
/// (`ClassCoverage.s_IgnoredWithoutData` on the managed side).
const DATA_KEYS: &[&str] = &["m_OcclusionCullingData", "m_NavMeshData"];

/// Classes whose documents the managed side checks value by value (`ClassCoverage.Report`):
/// Sprite (213) layouts. Lighting Settings are checked together with scenes by the field audit.
const CHECKED_CLASSES: &[i32] = &[213];

/// The objects of one class in the project's files.
#[derive(Default)]
struct Found {
    type_name: String,
    objects: usize,
    files: BTreeSet<String>,
    keys: BTreeSet<String>,
    referenced: BTreeSet<String>,
    /// The documents of a [`CHECKED_CLASSES`] class, by file.
    documents: BTreeMap<String, String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct FoundRequest<'a> {
    class_id: i32,
    type_name: &'a str,
    keys: &'a BTreeSet<String>,
    referenced: &'a BTreeSet<String>,
    documents: &'a BTreeMap<String, String>,
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Entry {
    class_id: i32,
    type_name: String,
    reading: String,
    detail: String,
    unity_name: Option<String>,
    #[serde(default)]
    inspect_fields: bool,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Coverage {
    has_table: bool,
    classes: Vec<Entry>,
    unread_in_release: Vec<Entry>,
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct UnsupportedMessage {
    file: String,
    line: usize,
    #[serde(rename = "type")]
    type_name: String,
    message: String,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
struct MessageCoverage {
    messages: Vec<UnsupportedMessage>,
    failed_assemblies: Vec<String>,
}

fn message_coverage(
    project: &Project,
    args: &ProjectArgs,
    runtime: &unbound_host::ManagedRuntime,
) -> Result<MessageCoverage> {
    let mode = crate::commands::mode(args);
    let graph = project.script_graph(mode, &args.target)?;
    for warning in &graph.warnings {
        tracing::warn!("{warning}");
    }
    let request = project.compile_request(
        &graph,
        mode,
        false,
        unbound_project::CodeOptimization::at_startup(&[]),
    );
    let facade_dir = unbound_project::cache_root()
        .join("facades")
        .join(project.version.to_string());
    let facades: serde_json::Value = runtime.call(
        "facades",
        &json!({
            "unityManagedEngineDir": project.install.managed_engine_dir(),
            "platformModules": project.install.platform_modules(),
            "outputDir": facade_dir,
        }),
    )?;
    let _: serde_json::Value = runtime.call(
        "prepare-field-coverage",
        &json!({
            "compilation": request, "release": project.version.to_string(), "facadeDir": facades["outputDir"],
        }),
    )?;
    let mut coverage: MessageCoverage = runtime
        .call("message-coverage", &request)
        .context("checking the project's unsupported script messages")?;
    for message in &mut coverage.messages {
        if let Ok(relative) = std::path::Path::new(&message.file).strip_prefix(&project.root) {
            message.file = relative.to_string_lossy().into_owned();
        }
    }
    Ok(coverage)
}

fn message_line(message: &UnsupportedMessage) -> String {
    format!(
        "{}:{}: {}.{}",
        message.file, message.line, message.type_name, message.message
    )
}

/// Adds the non-stripped documents of one Unity YAML file to `found`: class id, the type name
/// (the document's single top-level key), its keys, and which of [`DATA_KEYS`] reference an
/// object. Not a full parse: document headers and the first indentation level only.
fn scan(text: &str, path: &str, found: &mut BTreeMap<i32, Found>) {
    let mut current: Option<i32> = None;
    let mut expect_name = false;
    for line in text.lines() {
        if let Some(header) = line.strip_prefix("--- !u!") {
            let mut parts = header.split_whitespace();
            let class_id = parts.next().and_then(|id| id.parse::<i32>().ok());
            let stripped = parts.any(|p| p == "stripped");
            current = class_id.filter(|_| !stripped);
            expect_name = current.is_some();
            if let Some(id) = current {
                let entry = found.entry(id).or_default();
                entry.objects += 1;
                entry.files.insert(path.to_string());
                if CHECKED_CLASSES.contains(&id) {
                    entry
                        .documents
                        .entry(path.to_string())
                        .or_default()
                        .push_str(&format!("--- !u!{header}\n"));
                }
            }
            continue;
        }
        let Some(id) = current else { continue };
        if CHECKED_CLASSES.contains(&id) {
            let document = found.get_mut(&id).unwrap().documents.get_mut(path).unwrap();
            document.push_str(line);
            document.push('\n');
        }
        if expect_name {
            if let Some(name) = line.strip_suffix(':').filter(|n| !n.starts_with(' ')) {
                let entry = found.entry(id).or_default();
                if entry.type_name.is_empty() {
                    entry.type_name = name.to_string();
                }
                expect_name = false;
            }
            continue;
        }
        // `  m_Key: value`: a key of the object itself.
        let Some(rest) = line.strip_prefix("  ") else {
            continue;
        };
        if rest.starts_with([' ', '-']) {
            continue;
        }
        let Some((key, value)) = rest.split_once(':') else {
            continue;
        };
        let entry = found.entry(id).or_default();
        if !entry.keys.contains(key) {
            entry.keys.insert(key.to_string());
        }
        if DATA_KEYS.contains(&key) {
            let value = value.trim();
            let null = value.starts_with("{fileID: 0}") || value.starts_with("{fileID: 0,");
            if value.starts_with("{fileID:") && !null {
                entry.referenced.insert(key.to_string());
            }
        }
    }
}

/// The same class inventory for binary documents after the shared serialized-file decoder.
fn scan_binary(documents: &[unbound_yaml::Document], path: &str, found: &mut BTreeMap<i32, Found>) {
    for doc in documents.iter().filter(|d| !d.stripped) {
        let entry = found.entry(doc.class_id).or_default();
        entry.type_name = doc.type_name.clone();
        entry.objects += 1;
        entry.files.insert(path.into());
        if let unbound_yaml::Value::Map(map) = &doc.body {
            for (key, value) in map.iter() {
                entry.keys.insert(key.into());
                if DATA_KEYS.contains(&key) && value.as_ref().is_some_and(|r| !r.is_null()) {
                    entry.referenced.insert(key.into());
                }
            }
        }
    }
}

/// The file's bytes when it is Unity YAML (`%YAML 1.1`); other files are not read past the header.
fn read_yaml(path: &std::path::Path) -> Option<Vec<u8>> {
    use std::io::Read;
    let mut file = std::fs::File::open(path).ok()?;
    let mut bytes = vec![0u8; 9];
    file.read_exact(&mut bytes).ok()?;
    if bytes != b"%YAML 1.1" {
        return None;
    }
    file.read_to_end(&mut bytes).ok()?;
    Some(bytes)
}

/// `a, b, c +4`: the first files.
fn some_files(files: &BTreeSet<String>, shown: usize) -> String {
    let mut text = files
        .iter()
        .take(shown)
        .map(String::as_str)
        .collect::<Vec<_>>()
        .join(", ");
    if files.len() > shown {
        text.push_str(&format!(" +{}", files.len() - shown));
    }
    text
}

pub fn doctor(args: &ProjectArgs, all: bool, as_json: bool) -> Result<()> {
    let start = Instant::now();
    let project = Project::open(&args.project)?;
    let project_seconds = start.elapsed().as_secs_f64();
    let db = project.asset_db();
    let database_seconds = start.elapsed().as_secs_f64() - project_seconds;
    let mut found = BTreeMap::new();
    let mut files = 0usize;
    for asset in db.iter().filter(|a| !a.is_folder) {
        // Unity YAML only (scenes, prefabs, YAML assets); imported files are not objects here.
        if let Some(bytes) = read_yaml(&asset.file) {
            files += 1;
            scan(&String::from_utf8_lossy(&bytes), &asset.path, &mut found);
        } else if let Some(result) = fields::read_documents(&asset.file) {
            files += 1;
            match result {
                Ok(documents) => scan_binary(&documents, &asset.path, &mut found),
                Err(error) => tracing::warn!("serialized scan {}: {error:#}", asset.path),
            }
        }
    }
    let inventory_seconds = start.elapsed().as_secs_f64();
    let message_start = Instant::now();
    let request: Vec<FoundRequest> = found
        .iter()
        .map(|(&class_id, f)| FoundRequest {
            class_id,
            type_name: &f.type_name,
            keys: &f.keys,
            referenced: &f.referenced,
            documents: &f.documents,
        })
        .collect();
    let release = project.version.to_string();
    let runtime = unbound_host::ManagedRuntime::start()?;
    let coverage: Coverage = runtime
        .call(
            "class-coverage",
            &json!({ "release": release, "classes": request }),
        )
        .context("classifying the project's classes")?;
    let messages = message_coverage(&project, args, &runtime)?;
    let message_seconds = message_start.elapsed().as_secs_f64();
    let fields_start = Instant::now();
    let field_coverage = fields::report(
        &project,
        &runtime,
        &coverage
            .classes
            .iter()
            .filter(|e| e.inspect_fields)
            .map(|e| e.class_id)
            .collect(),
    )?;
    let fields_seconds = fields_start.elapsed().as_secs_f64();
    let settings_start = Instant::now();
    let settings_coverage = fields::settings_report(&project, &runtime, &args.target)?;
    if as_json {
        let classes: Vec<_> = coverage
            .classes
            .iter()
            .map(|e| {
                let f = &found[&e.class_id];
                json!({ "class": e, "objects": f.objects, "files": f.files })
            })
            .collect();
        let report = json!({
            "release": release,
            "target": args.target,
            "analysisSeconds": {"project": project_seconds, "database": database_seconds, "inventory": inventory_seconds, "messages": message_seconds, "serializedFields": fields_seconds, "settings": settings_start.elapsed().as_secs_f64(), "total": start.elapsed().as_secs_f64()},
            "hasTable": coverage.has_table,
            "classes": classes,
            "realtimeGI": field_coverage.realtime_gi,
            "serializedFields": field_coverage,
            "importerFields": settings_coverage.importer_fields,
            "projectSettingsFields": settings_coverage.project_settings_fields,
            "unreadInRelease": coverage.unread_in_release,
            "renderingLimitations": RENDERING_LIMITATIONS,
            "audioLimitations": AUDIO_LIMITATIONS,
            "unsupportedMessages": messages.messages,
            "messageAnalysisFailedAssemblies": messages.failed_assemblies,
        });
        println!("{}", serde_json::to_string_pretty(&report)?);
        return Ok(());
    }

    let mut by_reading: BTreeMap<&str, Vec<&Entry>> = BTreeMap::new();
    for entry in &coverage.classes {
        by_reading.entry(&entry.reading).or_default().push(entry);
    }
    println!(
        "{} classes in {} serialized files of {} ({:.2?})",
        coverage.classes.len(),
        files,
        project.root.display(),
        start.elapsed()
    );
    let not_in_release = |e: &Entry| {
        if coverage.has_table && e.unity_name.is_none() {
            format!(" (not a class of Unity {release})")
        } else {
            String::new()
        }
    };
    for (reading, title) in [
        (
            "Unsupported",
            "with content Unbound rejects or does not draw",
        ),
        ("Unread", "not read: they load with default settings"),
        ("Unknown", "no engine class: their objects are skipped"),
        ("Ignored", "ignored on purpose"),
    ] {
        let Some(entries) = by_reading.get(reading) else {
            continue;
        };
        println!("\n{} classes {title}:", entries.len());
        for e in entries {
            let f = &found[&e.class_id];
            println!(
                "  {} (class {}){}: {}",
                e.type_name,
                e.class_id,
                not_in_release(e),
                e.detail
            );
            if reading != "Ignored" {
                println!(
                    "    {} objects in {} files: {}",
                    f.objects,
                    f.files.len(),
                    some_files(&f.files, 3)
                );
            }
        }
    }
    println!("\nAudio limitations:");
    for limitation in AUDIO_LIMITATIONS {
        println!("  {limitation}");
    }
    field_coverage.print();
    settings_coverage.print();
    println!(
        "\n{} script messages Unbound does not deliver:",
        messages.messages.len()
    );
    for message in &messages.messages {
        println!("  {}", message_line(message));
    }
    if !messages.failed_assemblies.is_empty() {
        println!(
            "  incomplete analysis: assemblies failed to compile: {}",
            messages.failed_assemblies.join(", ")
        );
    }
    println!("\nRendering limitations:");
    for limitation in RENDERING_LIMITATIONS {
        println!("  {limitation}");
    }
    let read =
        by_reading.get("Read").map_or(0, Vec::len) + by_reading.get("Loader").map_or(0, Vec::len);
    println!("\n{read} classes read");
    if !coverage.has_table {
        println!("no class table for Unity {release} (tests/projects/classids records one)");
    } else if all {
        println!(
            "\n{} concrete runtime classes of Unity {release} without a reader of their serialized data (imported assets such as shaders and video clips are made by their importers instead):",
            coverage.unread_in_release.len()
        );
        for e in &coverage.unread_in_release {
            println!("  {} (class {}): {}", e.type_name, e.class_id, e.detail);
        }
    } else {
        println!(
            "{} concrete runtime classes of Unity {release} have no reader (--all lists them)",
            coverage.unread_in_release.len()
        );
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn q67_classification_and_real_reader_consumption() {
        let runtime = unbound_host::ManagedRuntime::start().unwrap();
        let coverage: Coverage = runtime
            .call(
                "class-coverage",
                &json!({
                    "release": "6000.0.71f1", "classes": [
                        {"classId": 81, "typeName": "AudioListener", "keys": ["m_Enabled"]},
                        {"classId": 124, "typeName": "FlareLayer", "keys": ["m_Enabled"]},
                        {"classId": 92, "typeName": "GUILayer", "keys": ["m_Enabled"]},
                        {"classId": 181963792, "typeName": "Preset", "keys": ["m_Properties"]}
                    ]
                }),
            )
            .unwrap();
        let reading: Vec<_> = coverage
            .classes
            .iter()
            .map(|e| e.reading.as_str())
            .collect();
        assert_eq!(reading, ["Read", "Read", "Ignored", "Unsupported"]);
        assert!(
            !coverage
                .unread_in_release
                .iter()
                .any(|e| matches!(e.class_id, 81 | 124))
        );
        assert!(
            coverage
                .classes
                .iter()
                .find(|entry| entry.class_id == 124)
                .unwrap()
                .detail
                .contains("requires Camera")
        );
        let dependencies: serde_json::Value = runtime.call("field-coverage", &json!([
            {"classId": 124, "fileId": 1, "typeName": "FlareLayer", "body": {"m_GameObject": {"fileID": "10"}, "m_Enabled": "1"}},
            {"classId": 124, "fileId": 2, "typeName": "FlareLayer", "body": {"m_GameObject": {"fileID": "20"}, "m_Enabled": "1"}},
            {"classId": 20, "fileId": 3, "typeName": "Camera", "body": {"m_GameObject": {"fileID": "20"}, "m_Enabled": "false"}},
            {"context": "projectSettings", "classId": 55, "fileId": 4, "typeName": "PhysicsManager", "body": {"m_Gravity": {"x": "0", "y": "-9.81", "z": "0", "ignored": "0"}, "ignored": "0"}},
            {"classId": 1, "fileId": 10, "typeName": "GameObject", "body": {}},
            {"classId": 1, "fileId": 20, "typeName": "GameObject", "body": {}},
            {"context": "projectSettings", "classId": 126, "fileId": 5, "typeName": "NavMeshProjectSettings", "body": {"areas": [{"name":"Walkable", "cost":"1", "unread":"0"}], "m_Settings":[{"agentRadius":"0.5", "unread":"0"}], "m_SettingNames":["Humanoid"], "unread":"0"}}
        ])).unwrap();
        assert!(
            dependencies[0]["dependency"]
                .as_str()
                .unwrap()
                .contains("Camera missing")
        );
        assert!(
            dependencies[1]["dependency"]
                .as_str()
                .unwrap()
                .contains("Camera disabled")
        );
        let settings_reads = dependencies[3]["consumed"].as_array().unwrap();
        assert!(settings_reads.contains(&json!("m_Gravity.y")));
        assert!(!settings_reads.contains(&json!("m_Gravity.ignored")));
        assert!(!settings_reads.contains(&json!("ignored")));
        let nav_reads = dependencies[6]["consumed"].as_array().unwrap();
        for path in [
            "areas[].name",
            "areas[].cost",
            "m_Settings[].agentRadius",
            "m_SettingNames[]",
        ] {
            assert!(nav_reads.contains(&json!(path)), "{dependencies}");
        }
        for path in ["unread", "areas[].unread", "m_Settings[].unread"] {
            assert!(!nav_reads.contains(&json!(path)), "{dependencies}");
        }
        let read: serde_json::Value = runtime
            .call(
                "field-coverage",
                &json!([{
                    "classId": 4, "fileId": 1, "typeName": "Transform", "body": {
                        "m_LocalPosition": {"x": "0", "y": "1", "z": "2", "extra": "3"},
                        "notRead": "0"
                    }
                }]),
            )
            .unwrap();
        assert!(read[0].get("error").is_none(), "{read}");
        let consumed = read[0]["consumed"].as_array().unwrap();
        assert!(consumed.contains(&json!("m_LocalPosition.x")));
        assert!(!consumed.contains(&json!("m_LocalPosition.extra")));
        assert!(!consumed.contains(&json!("notRead")));
    }

    #[test]
    fn message_report_preserves_file_type_and_message() {
        let coverage: MessageCoverage = serde_json::from_value(json!({
            "messages": [{"file": "Assets/IK.cs", "line": 17, "type": "Game.IK", "message": "OnStateIK"}],
            "failedAssemblies": ["Broken"]
        })).unwrap();
        assert_eq!(
            message_line(&coverage.messages[0]),
            "Assets/IK.cs:17: Game.IK.OnStateIK"
        );
        assert_eq!(coverage.failed_assemblies, ["Broken"]);
        let value = serde_json::to_value(&coverage.messages[0]).unwrap();
        assert_eq!(value["type"], "Game.IK");
        assert_eq!(value["message"], "OnStateIK");
    }

    #[test]
    fn scans_classes_keys_and_baked_data() {
        let text = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!196 &4\nNavMeshSettings:\n  serializedVersion: 2\n  m_BuildSettings:\n    agentRadius: 0.5\n  m_NavMeshData: {fileID: 23800000, guid: 0123, type: 2}\n--- !u!29 &1\nOcclusionCullingSettings:\n  m_OcclusionCullingData: {fileID: 0}\n--- !u!84 &2 stripped\nRenderTexture:\n  m_Width: 4\n--- !u!84 &3\nRenderTexture:\n  m_Name: rt\n  m_Width: 4\n  - item\n";
        let mut found = BTreeMap::new();
        scan(text, "Assets/a.unity", &mut found);
        assert_eq!(found.len(), 3);
        let nav = &found[&196];
        assert_eq!(nav.type_name, "NavMeshSettings");
        assert!(nav.keys.contains("m_BuildSettings") && !nav.keys.contains("agentRadius"));
        assert!(nav.referenced.contains("m_NavMeshData"));
        assert!(found[&29].referenced.is_empty());
        let rt = &found[&84];
        assert_eq!((rt.objects, rt.type_name.as_str()), (1, "RenderTexture"));
        assert_eq!(rt.keys, BTreeSet::from(["m_Name".into(), "m_Width".into()]));
        assert_eq!(rt.files, BTreeSet::from(["Assets/a.unity".into()]));
    }

    #[test]
    fn keeps_only_non_stripped_checked_documents() {
        let first = "--- !u!213 &12\nSprite:\n  m_RD:\n    settingsRaw: 64\n";
        let second = "--- !u!213 &14\nSprite:\n  m_RD:\n    settingsRaw: 1\n";
        let text = format!(
            "%YAML 1.1\n{first}--- !u!28 &13\nTexture2D:\n  m_Width: 64\n{second}--- !u!213 &15 stripped\nSprite:\n  m_Name: ignored\n"
        );
        let mut found = BTreeMap::new();
        scan(&text, "Assets/sprites.asset", &mut found);
        assert_eq!(found[&213].objects, 2);
        assert_eq!(
            found[&213].documents["Assets/sprites.asset"],
            format!("{first}{second}")
        );
        let lighting = "--- !u!850595691 &4890085278179872738\nLightingSettings:\n  m_EnableRealtimeLightmaps: 1\n";
        let mut found = BTreeMap::new();
        scan(
            &format!("%YAML 1.1\n{lighting}--- !u!28 &13\nTexture2D:\n  m_Width: 64\n"),
            "Assets/a.lighting",
            &mut found,
        );
        assert!(found[&850595691].documents.is_empty());
        assert!(found[&850595691].keys.contains("m_EnableRealtimeLightmaps"));
        assert!(found[&28].documents.is_empty());
    }
}
