//! The Editor's own resources (`Data/Resources/unity editor resources`), where UI Toolkit's default
//! theme, its images and fonts live (`EditorGUIUtility.Load` paths such as
//! `StyleSheets/Generated/Default.tss.asset`). Objects are read by their type trees and handed to
//! the managed side as JSON, references as `{fileID, guid}` with the built-in GUIDs of the files.
use anyhow::{Context, Result};
use serde_json::{Map as JsonMap, Value as Json, json};
use std::collections::HashMap;
use std::path::Path;
use std::sync::OnceLock;
use unbound_assets::serialized::SerializedFile;
use unbound_yaml::Value;

/// The GUID references into `unity editor resources` use.
pub const EDITOR_GUID: &str = "0000000000000000d000000000000000";
/// `unity default resources` (built-in MonoScripts, fonts).
pub const DEFAULT_GUID: &str = "0000000000000000e000000000000000";
/// `unity_builtin_extra`.
pub const EXTRA_GUID: &str = "0000000000000000f000000000000000";

struct EditorResources {
    file: SerializedFile<'static>,
    /// Lower-case container path → path id.
    container: HashMap<String, i64>,
}

fn resources(unity_data: &Path) -> Result<&'static EditorResources> {
    static RESOURCES: OnceLock<Result<EditorResources, String>> = OnceLock::new();
    RESOURCES
        .get_or_init(|| load(unity_data).map_err(|e| format!("{e:#}")))
        .as_ref()
        .map_err(|e| anyhow::anyhow!("unity editor resources: {e}"))
}

fn load(unity_data: &Path) -> Result<EditorResources> {
    let path = unity_data.join("Resources/unity editor resources");
    let file = std::fs::File::open(&path).with_context(|| format!("opening {}", path.display()))?;
    // Mapped once for the process: the file is large and only a few objects are read.
    let map = unsafe { memmap2::Mmap::map(&file) }?;
    let data: &'static [u8] = Box::leak(Box::new(map));
    let file = SerializedFile::parse(data)?;
    let mut container = HashMap::new();
    if let Some(bundle) = file.objects.iter().find(|o| o.class_id == 142) {
        let value = file.read(bundle.path_id)?;
        if let Some(entries) = value.get("m_Container").and_then(Value::as_seq) {
            for entry in entries {
                let key = entry.get("first").and_then(Value::as_str);
                let id = entry
                    .path("second.asset.m_PathID")
                    .and_then(Value::as_str)
                    .and_then(|s| s.parse::<i64>().ok());
                if let (Some(key), Some(id)) = (key, id) {
                    container.insert(key.to_lowercase(), id);
                }
            }
        }
    }
    Ok(EditorResources { file, container })
}

/// The object a resource path names: `{pathId, classId}` or null.
pub fn find(unity_data: &Path, path: &str) -> Result<Json> {
    let r = resources(unity_data)?;
    let key = path.replace('\\', "/").to_lowercase();
    Ok(match r.container.get(&key) {
        Some(&id) => {
            let class_id = r.file.info(id).map(|o| o.class_id).unwrap_or(0);
            json!({ "pathId": id, "classId": class_id })
        }
        None => Json::Null,
    })
}

/// An object's fields as JSON (scalars as strings, byte arrays as hex, references as `{fileID, guid}`).
pub fn object(unity_data: &Path, path_id: i64) -> Result<Json> {
    let r = resources(unity_data)?;
    let info = r.file.info(path_id)?;
    let value = r.file.read(path_id)?;
    let externals: Vec<String> = r
        .file
        .externals
        .iter()
        .map(|e| guid_of_external(&e.path).to_string())
        .collect();
    Ok(json!({ "classId": info.class_id, "fields": to_json(&value, &externals) }))
}

/// The raw bytes of a font's `m_FontData`.
pub fn font_data(unity_data: &Path, path_id: i64) -> Result<Vec<u8>> {
    let r = resources(unity_data)?;
    let value = r.file.read(path_id)?;
    let hex = value
        .get("m_FontData")
        .and_then(Value::as_str)
        .context("the object has no font data")?;
    (0..hex.len() / 2)
        .map(|i| u8::from_str_radix(&hex[2 * i..2 * i + 2], 16).context("bad font data"))
        .collect()
}

fn guid_of_external(path: &str) -> &'static str {
    let lower = path.to_lowercase();
    if lower.ends_with("unity default resources") {
        DEFAULT_GUID
    } else if lower.ends_with("unity_builtin_extra") {
        EXTRA_GUID
    } else if lower.ends_with("unity editor resources") {
        EDITOR_GUID
    } else {
        ""
    }
}

fn to_json(value: &Value, externals: &[String]) -> Json {
    match value {
        Value::Null => Json::Null,
        Value::Scalar(s) => Json::String(s.clone()),
        Value::Seq(items) => Json::Array(items.iter().map(|v| to_json(v, externals)).collect()),
        Value::Map(map) => {
            if map.0.len() == 2
                && let (Some(file), Some(path)) = (map.get("m_FileID"), map.get("m_PathID"))
            {
                let file: i64 = file.as_str().and_then(|s| s.parse().ok()).unwrap_or(0);
                let path: i64 = path.as_str().and_then(|s| s.parse().ok()).unwrap_or(0);
                if path == 0 {
                    return json!({ "fileID": 0 });
                }
                let guid = if file == 0 {
                    EDITOR_GUID.to_string()
                } else {
                    externals
                        .get(file as usize - 1)
                        .cloned()
                        .unwrap_or_default()
                };
                return json!({ "fileID": path, "guid": guid });
            }
            let mut out = JsonMap::new();
            for (k, v) in &map.0 {
                out.insert(k.clone(), to_json(v, externals));
            }
            Json::Object(out)
        }
    }
}
