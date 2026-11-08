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

const META_NO_RUNTIME_EFFECT: &[(&str, &str)] = &[
    (
        "fileFormatVersion",
        "Metadata envelope format marker; contains no importer option or runtime property",
    ),
    (
        "folderAsset",
        "Marks an asset database folder; no imported object data",
    ),
    (
        "timeCreated",
        "Authoring creation timestamp; no runtime property",
    ),
    (
        "licenseType",
        "Authoring license label; no runtime property",
    ),
];

fn reason(type_name: &str, path: &str) -> Option<&'static str> {
    NO_RUNTIME_EFFECT
        .iter()
        .chain(
            META_NO_RUNTIME_EFFECT
                .iter()
                .filter(|_| type_name == "Meta"),
        )
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
    dependencies: Vec<Json>,
    #[serde(skip_serializing)]
    pub realtime_gi: Vec<super::lighting::Entry>,
}

impl Report {
    pub(super) fn print(&self) {
        self.print_named("Serialized fields");
        super::lighting::print(&self.realtime_gi);
    }

    fn print_named(&self, title: &str) {
        println!(
            "\n{title}: {} unread paths may change behaviour, {} authoring metadata paths with no runtime effect ({} objects analyzed, {} incomplete readers/files).",
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
        for dependency in self.dependencies.iter().take(12) {
            println!("  dependency: {dependency}");
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
    dependency: Option<String>,
    lighting: Option<super::lighting::Input>,
}

fn paths(value: &Value, prefix: &str, out: &mut BTreeSet<String>) {
    match value {
        Value::Map(map) if !map.is_empty() => {
            for (name, child) in &map.0 {
                let path = unbound_yaml::reads::field_path(prefix, name);
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
        Value::Map(map) => Json::Object(map.0.iter().map(|(k, v)| (k.clone(), node(v))).collect()),
    }
}

fn add_unread(
    doc: &Document,
    asset: &str,
    consumed: &BTreeSet<String>,
    fields: &mut BTreeMap<(i32, String, String), Field>,
) {
    let mut present = BTreeSet::new();
    paths(&doc.body, "", &mut present);
    for path in &present {
        let mut scalar_sequence = path.as_str();
        let mut read = consumed.contains(path);
        while let Some(parent) = scalar_sequence.strip_suffix("[]") {
            read |= consumed.contains(parent);
            scalar_sequence = parent;
        }
        if read {
            continue;
        }
        let field = fields
            .entry((doc.class_id, doc.type_name.clone(), path.clone()))
            .or_insert_with(|| Field {
                class_id: doc.class_id,
                type_name: doc.type_name.clone(),
                path: path.clone(),
                count: 0,
                example_asset: asset.into(),
                reason: reason(&doc.type_name, path).map(String::from),
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
    read_with(path, false, &mut |_, _| {})
}

fn read_with(
    path: &std::path::Path,
    prepare: bool,
    observe: &mut dyn FnMut(&mut Document, &str),
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
            for file in archive.serialized_files() {
                let mut docs = archive.raw_documents(&file.path)?;
                for doc in &mut docs {
                    observe(doc, &format!("bundle:{}", file.path));
                }
                if prepare {
                    archive.prepare_documents(&mut docs)?;
                }
                documents.extend(docs);
            }
            return Ok(documents);
        };
        for doc in &mut documents {
            observe(doc, "");
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
    let mut lighting = super::lighting::Inventory::default();
    let mut fields = BTreeMap::new();
    let db = project.asset_db();
    let mut assets: Vec<_> = db.iter().filter(|a| !a.is_folder).collect();
    assets.sort_by(|a, b| a.path.cmp(&b.path));
    for asset in assets {
        let mut original = Vec::new();
        let mut native_reads = Vec::new();
        let Some(input) = read_with(&asset.file, true, &mut |doc, file_guid| {
            let reads = unbound_yaml::reads::FieldReads::default();
            reads.track(&mut doc.body);
            original.push(doc.clone());
            native_reads.push((reads, file_guid.to_owned()));
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
        unbound_scene::read_prefab_inputs(&documents);
        let request: Vec<_> = documents.iter().zip(&native_reads).map(|(d, (_, file_guid))| json!({"classId": d.class_id, "fileId": d.file_id, "fileGuid": file_guid, "typeName": d.type_name, "body": node(&d.body)})).collect();
        let results: Vec<ReaderResult> = runtime
            .call("field-coverage", &request)
            .with_context(|| format!("auditing serialized fields in {}", asset.path))?;
        anyhow::ensure!(
            results.len() == documents.len(),
            "field reader omitted an object"
        );
        for ((doc, mut result), (native, file_guid)) in
            original.into_iter().zip(results).zip(native_reads)
        {
            anyhow::ensure!(
                doc.file_id == result.file_id,
                "field reader reordered objects"
            );
            if let Some(input) = result.lighting {
                let guid = if file_guid.is_empty() {
                    asset.guid.to_string()
                } else {
                    format!("{}:{file_guid}", asset.guid)
                };
                lighting.add(&asset.path, &guid, doc.file_id, input);
            }
            result.consumed.extend(native.paths());
            if let Some(detail) = result.dependency {
                report.dependencies.push(json!({"asset": asset.path, "fileId": doc.file_id, "typeName": doc.type_name, "detail": detail}));
            }
            report.objects += 1;
            if let Some(error) = result.error {
                report.incomplete.push(json!({"asset": asset.path, "classId": doc.class_id, "fileId": doc.file_id, "error": error}));
            }
            add_unread(&doc, &asset.path, &result.consumed, &mut fields);
        }
    }
    finish(&mut report, fields);
    report.realtime_gi = lighting.report(project, runtime)?;
    Ok(report)
}

fn finish(report: &mut Report, fields: BTreeMap<(i32, String, String), Field>) {
    for field in fields.into_values() {
        if field.reason.is_some() {
            report.known_no_runtime_effect.push(field);
        } else {
            report.unread_may_change_behaviour.push(field);
        }
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct SettingsReports {
    pub importer_fields: Report,
    pub project_settings_fields: Report,
}

impl SettingsReports {
    pub(super) fn print(&self) {
        self.importer_fields.print_named("Importer fields");
        self.project_settings_fields
            .print_named("Project settings fields");
    }
}

fn read_settings_documents(path: &std::path::Path) -> Result<Vec<Document>> {
    if let Some(documents) = read_documents(path) {
        return documents;
    }
    // Some package-owned project settings use JSON despite the .asset extension. They have no
    // startup reader, but their actual fields still belong in the unread inventory.
    fn value(json: Json) -> Value {
        match json {
            Json::Null => Value::Null,
            Json::String(text) => Value::Scalar(text),
            Json::Array(items) => Value::Seq(items.into_iter().map(value).collect()),
            Json::Object(fields) => Value::Map(
                fields
                    .into_iter()
                    .map(|(key, item)| (key, value(item)))
                    .collect::<Vec<_>>()
                    .into(),
            ),
            scalar => Value::Scalar(scalar.to_string()),
        }
    }
    let text = std::fs::read_to_string(path)?;
    let json: Json = serde_json::from_str(text.trim_start_matches('\u{feff}'))
        .context("settings are neither a recognized serialized file nor valid JSON")?;
    anyhow::ensure!(
        json.is_object(),
        "JSON project settings root is not an object"
    );
    Ok(vec![Document {
        class_id: 0,
        file_id: 0,
        stripped: false,
        type_name: path
            .file_stem()
            .unwrap_or_default()
            .to_string_lossy()
            .into_owned(),
        body: value(json),
    }])
}

pub(super) fn settings_report(
    project: &unbound_project::Project,
    runtime: &unbound_host::ManagedRuntime,
    target: &str,
) -> Result<SettingsReports> {
    let mut imports = Report::default();
    let mut fields = BTreeMap::new();
    let db = project.asset_db();
    imports
        .incomplete
        .extend(db.problems.iter().map(|error| json!({"error": error})));
    let mut assets: Vec<_> = db.iter().collect();
    assets.sort_by(|a, b| a.path.cmp(&b.path));
    for asset in assets {
        let file = std::path::PathBuf::from(format!("{}.meta", asset.file.display()));
        let asset_path = format!("{}.meta", asset.path);
        let input = std::fs::read_to_string(&file)
            .context("reading importer metadata")
            .and_then(|text| unbound_yaml::parse_value(&text));
        let mut body = match input {
            Ok(body) => body,
            Err(error) => {
                imports
                    .incomplete
                    .push(json!({"asset": asset_path, "error": error.to_string()}));
                continue;
            }
        };
        let original = body.clone();
        let reads = unbound_yaml::reads::FieldReads::default();
        reads.track(&mut body);
        let _ = unbound_project::assetdb::meta_header(&body);
        let _ = unbound_runtime::config::execution_order(&body);
        unbound_assets::meta::read_inputs(&body, target);
        if body.get("PluginImporter").is_some() {
            if unbound_project::plugins::is_managed_assembly(&asset.file) {
                let plugin = unbound_project::plugins::plugin_from_meta(
                    &asset.file,
                    asset.path.clone(),
                    &body,
                );
                let _ = plugin.compatible_with("Editor");
                let _ = plugin.compatible_with(target);
            } else if asset
                .file
                .extension()
                .and_then(|ext| ext.to_str())
                .is_some_and(|ext| unbound_project::plugins::NATIVE_EXTENSIONS.contains(&ext))
            {
                let _ =
                    unbound_project::plugins::native_editor_plugin_from_meta(&asset.file, &body);
            }
        }
        let consumed = reads.paths();
        imports.objects += 1;
        // Audit the whole envelope as well as the importer, including folder metadata. Field
        // keys include the importer name so different importer classes cannot collide.
        let doc = Document {
            class_id: 0,
            file_id: 0,
            stripped: false,
            type_name: "Meta".into(),
            body: original,
        };
        add_unread(&doc, &asset_path, &consumed, &mut fields);
    }
    finish(&mut imports, fields);
    let (result, reads) = unbound_yaml::reads::FileReads::capture(|| {
        let result = unbound_project::settings::Settings::read(&project.root);
        unbound_runtime::config::read_settings_inputs(project);
        result
    });
    result?;
    let mut settings = Report::default();
    let mut fields = BTreeMap::new();
    let mut files: Vec<_> = std::fs::read_dir(project.root.join("ProjectSettings"))?
        .collect::<std::io::Result<Vec<_>>>()?;
    files.sort_by_key(|file| file.file_name());
    for file in files {
        let path = file.path();
        if path.extension().and_then(|x| x.to_str()) != Some("asset") {
            continue;
        }
        let asset = format!("ProjectSettings/{}", file.file_name().to_string_lossy());
        let documents = match read_settings_documents(&path) {
            Ok(docs) if !docs.is_empty() => docs,
            input => {
                settings
                    .incomplete
                    .push(json!({"asset": asset, "error": match input {
                        Err(error) => format!("{error:#}"), _ => "empty settings file".into(),
                    }}));
                continue;
            }
        };
        let request: Vec<_> = documents.iter().map(|doc| json!({"context": "projectSettings", "classId": doc.class_id, "fileId": doc.file_id, "typeName": doc.type_name, "body": node(&doc.body)})).collect();
        let results: Vec<ReaderResult> = runtime.call("field-coverage", &request)?;
        anyhow::ensure!(
            results.len() == documents.len(),
            "settings reader omitted an object"
        );
        for (doc, mut result) in documents.into_iter().zip(results) {
            anyhow::ensure!(
                result.file_id == doc.file_id,
                "settings reader reordered objects"
            );
            result.consumed.extend(reads.paths(&path, doc.file_id));
            if let Some(error) = result.error {
                settings
                    .incomplete
                    .push(json!({"asset": asset, "error": error}));
            }
            settings.objects += 1;
            add_unread(&doc, &asset, &result.consumed, &mut fields);
        }
    }
    finish(&mut settings, fields);
    Ok(SettingsReports {
        importer_fields: imports,
        project_settings_fields: settings,
    })
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
        assert_eq!(fields[&(4, "Transform".into(), "x".into())].count, 1);
        add_unread(&doc, "Assets/a.asset", &BTreeSet::new(), &mut fields);
        assert_eq!(fields[&(4, "Transform".into(), "x".into())].count, 2);
        assert_eq!(
            fields[&(4, "Transform".into(), "x".into())].example_asset,
            "Assets/a.asset"
        );
    }
    #[test]
    fn diagnostic_tree_encoding_does_not_count_as_a_reader() {
        let mut value = unbound_yaml::parse_value("unread: 0\nnested: {unread: 0}\n").unwrap();
        let reads = unbound_yaml::reads::FieldReads::default();
        reads.track(&mut value);
        let _ = node(&value);
        let mut present = BTreeSet::new();
        paths(&value, "", &mut present);
        assert_eq!(present.len(), 2);
        assert!(reads.paths().is_empty());
    }

    #[test]
    fn importer_and_settings_coverage_follows_the_reader_not_a_key_table() {
        for (name, yaml) in [
            (
                "Meta",
                "TextureImporter:\n  mipmaps:\n    enableMipMap: 0\n  ignoredOption: 0\n",
            ),
            ("PlayerSettings", "productName: Test\nignoredOption: 0\n"),
        ] {
            let original = unbound_yaml::parse_value(yaml).unwrap();
            let run = |consume: bool| {
                let mut value = original.clone();
                let reads = unbound_yaml::reads::FieldReads::default();
                reads.track(&mut value);
                if consume {
                    if name == "Meta" {
                        unbound_assets::meta::read_inputs(&value, "Android");
                    } else {
                        let _ = value.get("productName").and_then(Value::as_str);
                    }
                }
                let mut fields = BTreeMap::new();
                let doc = Document {
                    class_id: 0,
                    file_id: 1,
                    stripped: false,
                    type_name: name.into(),
                    body: original.clone(),
                };
                add_unread(&doc, "input", &reads.consumed(&value), &mut fields);
                fields
                    .into_values()
                    .map(|field| field.path)
                    .collect::<BTreeSet<_>>()
            };
            let path = if name == "Meta" {
                "TextureImporter.mipmaps.enableMipMap"
            } else {
                "productName"
            };
            assert!(!run(true).contains(path));
            assert!(run(false).contains(path));
            assert!(run(true).iter().any(|p| p.ends_with("ignoredOption")));
        }
    }

    #[test]
    fn model_animation_inputs_use_the_same_decoder_as_the_importer() {
        let mut value = unbound_yaml::parse_value("ModelImporter:\n  animations:\n    animationCompression: 0\n    motionNodeName: Root\n    unread: 0\n").unwrap();
        let reads = unbound_yaml::reads::FieldReads::default();
        reads.track(&mut value);
        unbound_assets::meta::read_inputs(&value, "Standalone");
        assert!(
            reads
                .paths()
                .contains("ModelImporter.animations.animationCompression")
        );
        assert!(
            reads
                .paths()
                .contains("ModelImporter.animations.motionNodeName")
        );
        assert!(!reads.paths().contains("ModelImporter.animations.unread"));
    }

    #[test]
    fn retained_but_unapplied_import_options_still_may_change_behaviour() {
        let mut body = unbound_yaml::parse_value(
            "AudioImporter:\n  forceToMono: 1\n  defaultSettings:\n    quality: 0.25\n    sampleRateSetting: 1\n    compressionFormat: 1\n",
        )
        .unwrap();
        let original = body.clone();
        let reads = unbound_yaml::reads::FieldReads::default();
        reads.track(&mut body);
        let settings = unbound_assets::AudioSettings::read(&body, "Standalone");
        assert_eq!(settings.sample.quality, 0.25);
        assert_eq!(settings.sample.sample_rate_setting, 1);
        assert_eq!(settings.sample.compression_format, 1);
        let doc = Document {
            class_id: 0,
            file_id: 1,
            stripped: false,
            type_name: "Meta".into(),
            body: original,
        };
        let mut fields = BTreeMap::new();
        add_unread(&doc, "sound.wav.meta", &reads.paths(), &mut fields);
        for path in [
            "AudioImporter.defaultSettings.sampleRateSetting",
            "AudioImporter.defaultSettings.compressionFormat",
        ] {
            assert!(
                fields
                    .values()
                    .any(|field| field.path == path && field.reason.is_none())
            );
        }
        assert!(fields.values().any(
            |field| field.path == "AudioImporter.defaultSettings.quality" && field.reason.is_none()
        ));
        assert!(
            !fields
                .values()
                .any(|field| field.path == "AudioImporter.forceToMono")
        );
    }

    #[test]
    fn json_project_settings_preserve_unread_array_and_literal_key_paths() {
        let root = unbound_project::cache_root()
            .join("doctor-tests")
            .join(format!("json-settings-{}", std::process::id()));
        std::fs::create_dir_all(&root).unwrap();
        let path = root.join("PackageSettings.asset");
        std::fs::write(
            &path,
            concat!(
                "\u{feff}",
                r#"{"keys":["a"],"a.b":0,"nested":{"unused":false}}"#
            ),
        )
        .unwrap();
        let doc = read_settings_documents(&path).unwrap().remove(0);
        let mut fields = BTreeMap::new();
        add_unread(
            &doc,
            "ProjectSettings/PackageSettings.asset",
            &BTreeSet::new(),
            &mut fields,
        );
        let paths: BTreeSet<_> = fields.values().map(|field| field.path.as_str()).collect();
        assert_eq!(paths, BTreeSet::from(["keys[]", "a\\.b", "nested.unused"]));
        std::fs::remove_dir_all(root).unwrap();
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
        for (path, why) in NO_RUNTIME_EFFECT.iter().chain(META_NO_RUNTIME_EFFECT) {
            assert!(!path.trim().is_empty() && !why.trim().is_empty());
            assert!(seen.insert(path));
        }
        assert!(reason("Meta", "m_Enabled").is_none());
        assert!(reason("Meta", "nested.serializedVersion").is_none());
    }

    #[test]
    fn literal_field_names_cannot_collide_with_nested_or_array_paths() {
        let doc = unbound_yaml::parse_documents(
            "--- !u!4 &1\nTransform:\n  a.b: 1\n  a:\n    b: 2\n  'items[]': 3\n  items: [4]\n",
        )
        .unwrap()
        .remove(0);
        let mut fields = BTreeMap::new();
        add_unread(
            &doc,
            "Assets/a.asset",
            &BTreeSet::from(["a\\.b".into()]),
            &mut fields,
        );
        assert!(!fields.contains_key(&(4, "Transform".into(), "a\\.b".into())));
        for path in ["a.b", "items\\[\\]", "items[]"] {
            assert_eq!(fields[&(4, "Transform".into(), path.into())].count, 1);
        }
    }
}
