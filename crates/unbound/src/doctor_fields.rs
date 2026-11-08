//! Field consumption is supplied by the serialized readers, through the diagnostic host's
//! private tracked nodes. This module inventories input paths and aggregates the difference.
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use serde_json::{Value as Json, json};
use std::collections::{BTreeMap, BTreeSet};
use unbound_yaml::{Document, Value};

/// Only authoring metadata belongs here. Runtime values, even zero or empty ones, stay visible.
/// Each exception has an explicit reason reviewed alongside the code.
const NO_RUNTIME_EFFECT: &[(&str, &str)] = &[(
    "m_EditorHideFlags",
    "Editor inspector visibility metadata; no Play Mode property",
)];

fn reason(path: &str) -> Option<&'static str> {
    NO_RUNTIME_EFFECT
        .iter()
        .find_map(|(field, reason)| (*field == path).then_some(*reason))
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Field {
    class_id: i32,
    type_name: String,
    path: String,
    count: usize,
    example_asset: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    reason: Option<String>,
}

#[derive(Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Report {
    known_no_runtime_effect: Vec<Field>,
    unread_may_change_behaviour: Vec<Field>,
    incomplete: Vec<Json>,
    objects: usize,
}

impl Report {
    pub(super) fn print(&self) {
        println!(
            "\nSerialized fields: {} unread paths may change behaviour, {} authoring metadata paths with no runtime effect ({} objects analyzed, {} incomplete readers/files).",
            self.unread_may_change_behaviour.len(),
            self.known_no_runtime_effect.len(),
            self.objects,
            self.incomplete.len()
        );
        for field in self.unread_may_change_behaviour.iter().take(12) {
            println!(
                "  {} (class {}).{}: {} objects, e.g. {}",
                field.type_name, field.class_id, field.path, field.count, field.example_asset
            );
        }
        if self.unread_may_change_behaviour.len() > 12 {
            println!("  --json lists all paths and the reasons for metadata exceptions");
        }
        for issue in self.incomplete.iter().take(5) {
            println!("  incomplete: {issue}");
        }
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ReaderResult {
    file_id: i64,
    consumed: BTreeSet<String>,
    error: Option<String>,
}

fn paths(value: &Value, prefix: &str, out: &mut BTreeSet<String>) {
    match value {
        Value::Map(map) if !map.is_empty() => {
            for (name, child) in map.iter() {
                let path = if prefix.is_empty() {
                    name.into()
                } else {
                    format!("{prefix}.{name}")
                };
                paths(child, &path, out);
            }
        }
        Value::Seq(items) if !items.is_empty() => {
            for child in items {
                paths(child, &format!("{prefix}[]"), out);
            }
        }
        _ => {
            out.insert(prefix.into());
        }
    }
}

fn node(value: &Value) -> Json {
    match value {
        Value::Null => Json::Null,
        Value::Scalar(s) => json!(s),
        Value::Seq(items) => Json::Array(items.iter().map(node).collect()),
        Value::Map(map) => Json::Object(map.iter().map(|(k, v)| (k.into(), node(v))).collect()),
    }
}

fn add_unread(
    doc: &Document,
    asset: &str,
    consumed: &BTreeSet<String>,
    fields: &mut BTreeMap<(i32, String), Field>,
) {
    let mut present = BTreeSet::new();
    paths(&doc.body, "", &mut present);
    for path in present.difference(consumed) {
        let field = fields
            .entry((doc.class_id, path.clone()))
            .or_insert_with(|| Field {
                class_id: doc.class_id,
                type_name: doc.type_name.clone(),
                path: path.clone(),
                count: 0,
                example_asset: asset.into(),
                reason: reason(path).map(String::from),
            });
        field.count += 1;
        if asset < field.example_asset.as_str() {
            field.example_asset = asset.into();
        }
    }
}

/// Recognize a serialized file by its header, rather than guessing from an extension. Source
/// images, audio and models are left to their importers. A recognized but unreadable file is an
/// explicit scan failure, including binary files without the type trees needed for a field audit.
pub(super) fn read_documents(path: &std::path::Path) -> Option<Result<Vec<Document>>> {
    read_with(path, false, &mut |_| {})
}

fn read_with(
    path: &std::path::Path,
    prepare: bool,
    observe: &mut dyn FnMut(&mut Document),
) -> Option<Result<Vec<Document>>> {
    use std::io::Read;
    let mut file = match std::fs::File::open(path) {
        Ok(file) => file,
        Err(error) => {
            return Some(Err(error).with_context(|| format!("opening {}", path.display())));
        }
    };
    let mut header = [0u8; 48];
    let n = match file.read(&mut header) {
        Ok(n) => n,
        Err(error) => return Some(Err(error).context("reading serialized header")),
    };
    let yaml = header[..n]
        .strip_prefix(b"\xef\xbb\xbf")
        .unwrap_or(&header[..n])
        .starts_with(b"%YAML 1.1");
    let bundle = header[..n].starts_with(b"UnityFS\0");
    let binary = if n >= 20 {
        let version = u32::from_be_bytes(header[8..12].try_into().unwrap());
        let length = file.metadata().ok()?.len();
        let declared = if version >= 22 && n >= 48 {
            u64::from_be_bytes(header[24..32].try_into().unwrap())
        } else {
            u32::from_be_bytes(header[4..8].try_into().unwrap()) as u64
        };
        (9..=100).contains(&version) && declared == length
    } else {
        false
    };
    if !yaml && !binary && !bundle {
        return None;
    }
    Some((|| {
        let bytes = std::fs::read(path)?;
        let mut documents = if yaml {
            unbound_yaml::parse_documents(std::str::from_utf8(&bytes).context("YAML encoding")?)?
        } else if binary {
            unbound_assets::serialized::documents(&bytes)?
        } else {
            let archive = unbound_assets::bundle::Bundle::parse(&bytes)?;
            let mut documents = Vec::new();
            for file in archive
                .files
                .iter()
                .filter(|f| f.flags & unbound_assets::bundle::NODE_SERIALIZED != 0)
            {
                let mut docs = archive.raw_documents(&file.path)?;
                for doc in &mut docs {
                    observe(doc);
                }
                if prepare {
                    archive.prepare_documents(&mut docs)?;
                }
                documents.extend(docs);
            }
            return Ok(documents);
        };
        for doc in &mut documents {
            observe(doc);
        }
        Ok(documents)
    })())
}

pub(super) fn report(
    project: &unbound_project::Project,
    runtime: &unbound_host::ManagedRuntime,
    supported: &BTreeSet<i32>,
) -> Result<Report> {
    let mut report = Report::default();
    let mut fields = BTreeMap::new();
    let db = project.asset_db();
    let loader =
        unbound_scene::DocumentLoader::new(|guid| db.by_guid(guid).map(|a| a.file.clone()));
    let mut assets: Vec<_> = db.iter().filter(|a| !a.is_folder).collect();
    assets.sort_by(|a, b| a.path.cmp(&b.path));
    for asset in assets {
        let mut original = Vec::new();
        let mut native_reads = Vec::new();
        let Some(input) = read_with(&asset.file, true, &mut |doc| {
            let reads = unbound_yaml::reads::FieldReads::default();
            reads.track(&mut doc.body);
            original.push(doc.clone());
            native_reads.push(reads);
        }) else {
            continue;
        };
        let documents = match input {
            Ok(docs) => docs,
            Err(error) => {
                report
                    .incomplete
                    .push(json!({"asset": asset.path, "error": error.to_string()}));
                continue;
            }
        };
        let selected: Vec<_> = documents
            .into_iter()
            .zip(original)
            .zip(native_reads)
            .filter(|((d, _), _)| !d.stripped && supported.contains(&d.class_id))
            .collect();
        let (pairs, native_reads): (Vec<_>, Vec<_>) = selected.into_iter().unzip();
        let (documents, original): (Vec<_>, Vec<_>) = pairs.into_iter().unzip();
        if documents.is_empty() {
            continue;
        }
        if documents.iter().any(|d| d.class_id == 1001) {
            if let Err(error) = unbound_scene::flatten(&documents, &loader, &mut Vec::new()) {
                report.incomplete.push(
                    json!({"asset": asset.path, "error": format!("prefab reader: {error:#}")}),
                );
            }
        }
        let request: Vec<_> = documents.iter().map(|d| json!({"classId": d.class_id, "fileId": d.file_id, "typeName": d.type_name, "body": node(&d.body)})).collect();
        let results: Vec<ReaderResult> = runtime
            .call("field-coverage", &request)
            .with_context(|| format!("auditing serialized fields in {}", asset.path))?;
        anyhow::ensure!(
            results.len() == documents.len(),
            "field reader omitted an object"
        );
        for ((doc, mut result), native) in original.into_iter().zip(results).zip(native_reads) {
            anyhow::ensure!(
                doc.file_id == result.file_id,
                "field reader reordered objects"
            );
            result.consumed.extend(native.paths());
            report.objects += 1;
            if let Some(error) = result.error {
                report.incomplete.push(json!({"asset": asset.path, "classId": doc.class_id, "fileId": doc.file_id, "error": error}));
            }
            add_unread(&doc, &asset.path, &result.consumed, &mut fields);
        }
    }
    for field in fields.into_values() {
        if field.reason.is_some() {
            report.known_no_runtime_effect.push(field);
        } else {
            report.unread_may_change_behaviour.push(field);
        }
    }
    Ok(report)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn a_field_no_longer_consumed_is_reported_including_zero_values() {
        let doc =
            unbound_yaml::parse_documents("--- !u!4 &1\nTransform:\n  x: 0\n  nested:\n    y: 1\n")
                .unwrap()
                .remove(0);
        let mut fields = BTreeMap::new();
        add_unread(
            &doc,
            "Assets/b.asset",
            &BTreeSet::from(["x".into(), "nested.y".into()]),
            &mut fields,
        );
        assert!(fields.is_empty());
        add_unread(
            &doc,
            "Assets/b.asset",
            &BTreeSet::from(["nested.y".into()]),
            &mut fields,
        );
        assert_eq!(fields[&(4, "x".into())].count, 1);
        add_unread(&doc, "Assets/a.asset", &BTreeSet::new(), &mut fields);
        assert_eq!(fields[&(4, "x".into())].count, 2);
        assert_eq!(fields[&(4, "x".into())].example_asset, "Assets/a.asset");
    }
    #[test]
    fn recognizes_binary_headers_and_reports_unsupported_versions() {
        let root = unbound_project::cache_root()
            .join("doctor-tests")
            .join(format!(
                "{}-{}",
                std::process::id(),
                std::time::SystemTime::now()
                    .duration_since(std::time::UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            ));
        std::fs::create_dir_all(&root).unwrap();
        let path = root.join("future.asset");
        let mut bytes = [0u8; 48];
        bytes[8..12].copy_from_slice(&31u32.to_be_bytes());
        bytes[24..32].copy_from_slice(&48u64.to_be_bytes());
        std::fs::write(&path, bytes).unwrap();
        let error = read_documents(&path).unwrap().unwrap_err();
        assert!(
            error
                .to_string()
                .contains("unsupported serialized file version 31")
        );
        let yaml = root.join("bom.asset");
        std::fs::write(
            &yaml,
            "\u{feff}%YAML 1.1\n--- !u!4 &1\nTransform:\n  m_RootOrder: 0\n",
        )
        .unwrap();
        assert_eq!(read_documents(&yaml).unwrap().unwrap().len(), 1);
        assert!(
            read_documents(&root.join("missing.asset"))
                .unwrap()
                .is_err()
        );
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn every_allow_list_entry_requires_a_specific_reason() {
        let mut seen = BTreeSet::new();
        for (path, why) in NO_RUNTIME_EFFECT {
            assert!(!path.trim().is_empty() && !why.trim().is_empty());
            assert!(seen.insert(path));
        }
        assert!(reason("m_Enabled").is_none());
        assert!(reason("nested.serializedVersion").is_none());
    }
}
