//! `unbound doctor`: what in the project Unbound does not support. It lists the Unity classes
//! of the objects in the project's scenes, prefabs and YAML assets that the engine does not read
//! (each also warns when it loads), with the files that hold them.
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
    "Mesh GraphicsBuffers support GetData and read-only shader/compute inputs. GPU mesh editing and SetData/CopyBuffer into mesh buffers are not supported; update the mesh CPU data instead.",
    "Rendering sprites with non-finite PixelPerfectRendering.pixelSnapSpacing is not supported; finite spacing and property get/set are supported.",
    "Game-view gizmo drawing and OnDrawGizmos/OnDrawGizmosSelected repaint callbacks are not supported. Gizmos state and calls outside those callbacks match the measured Play-mode API.",
];

/// The keys whose referenced object turns an ignored scene settings class into an unread one
/// (`ClassCoverage.s_IgnoredWithoutData` on the managed side).
const DATA_KEYS: &[&str] = &["m_OcclusionCullingData", "m_NavMeshData"];

/// Classes whose documents the managed side checks value by value (`ClassCoverage.Report`):
/// Sprite (213) layouts, LightingSettings (850595691) features Unbound does not draw.
const CHECKED_CLASSES: &[i32] = &[213, 850595691];

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
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Coverage {
    has_table: bool,
    classes: Vec<Entry>,
    unread_in_release: Vec<Entry>,
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
    let db = project.asset_db();
    let mut found = BTreeMap::new();
    let mut files = 0usize;
    for asset in db.iter().filter(|a| !a.is_folder) {
        // Unity YAML only (scenes, prefabs, YAML assets); imported files are not objects here.
        let Some(bytes) = read_yaml(&asset.file) else {
            continue;
        };
        files += 1;
        scan(&String::from_utf8_lossy(&bytes), &asset.path, &mut found);
    }
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
            "hasTable": coverage.has_table,
            "classes": classes,
            "unreadInRelease": coverage.unread_in_release,
            "renderingLimitations": RENDERING_LIMITATIONS,
        });
        println!("{}", serde_json::to_string_pretty(&report)?);
        return Ok(());
    }

    let mut by_reading: BTreeMap<&str, Vec<&Entry>> = BTreeMap::new();
    for entry in &coverage.classes {
        by_reading.entry(&entry.reading).or_default().push(entry);
    }
    println!(
        "{} classes in {} Unity YAML files of {} ({:.2?})",
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
        assert_eq!(found[&850595691].documents["Assets/a.lighting"], lighting);
        assert!(found[&28].documents.is_empty());
    }
}
