//! The Editor's own resources (`Data/Resources/unity editor resources`), where UI Toolkit's default
//! theme, its images and fonts live (`EditorGUIUtility.Load` paths such as
//! `StyleSheets/Generated/Default.tss.asset`). Objects are read by their type trees and handed to
//! the managed side as JSON, references as `{fileID, guid}` with the built-in GUIDs of the files.
//! The install's other built-in resource files (`unity default resources`, `unity_builtin_extra`)
//! are read the same way, by their built-in GUID and an object's file id.
use anyhow::{Context, Result};
use serde_json::{Map as JsonMap, Value as Json, json};
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};
use unbound_assets::serialized::SerializedFile;
use unbound_project::assetdb::AssetDb;
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
    object_of(&r.file, path_id, EDITOR_GUID)
}

/// An object of an installed built-in resource file by its built-in `guid` and `file_id`:
/// `{classId, fields}` as [`object`] answers it, references within the file carrying that file's
/// GUID. The Editor's resources (`0000000000000000d000000000000000`) answer exactly as by path id;
/// `unity default resources` (`…e000…`) and `unity_builtin_extra` (`…f000…`) are found through the
/// serialized-file routing the other built-in readers use. Any other GUID, a missing file or a
/// missing object is an error naming it.
pub fn builtin_object(db: &AssetDb, unity_data: &Path, guid: &str, file_id: i64) -> Result<Json> {
    if guid.eq_ignore_ascii_case(EDITOR_GUID) {
        return object(unity_data, file_id);
    }
    let self_guid = if guid.eq_ignore_ascii_case(DEFAULT_GUID) {
        DEFAULT_GUID
    } else if guid.eq_ignore_ascii_case(EXTRA_GUID) {
        EXTRA_GUID
    } else {
        anyhow::bail!(
            "`{guid}` is not a built-in resource file (unity editor resources, unity default \
             resources or unity_builtin_extra)"
        );
    };
    let path = crate::lighting::serialized_file(db, unity_data, self_guid)?;
    let file = builtin_file(&path)?;
    object_of(file, file_id, self_guid)
        .with_context(|| format!("built-in object {file_id} of {}", path.display()))
}

/// A built-in resource file, mapped and parsed once for the process (the files are large and only
/// a few objects are read). A file that cannot be read stays an error for the process.
fn builtin_file(path: &Path) -> Result<&'static SerializedFile<'static>> {
    type Files = HashMap<PathBuf, Result<&'static SerializedFile<'static>, String>>;
    static FILES: OnceLock<Mutex<Files>> = OnceLock::new();
    let mut files = FILES
        .get_or_init(Default::default)
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    files
        .entry(path.to_path_buf())
        .or_insert_with(|| map_builtin_file(path).map_err(|e| format!("{e:#}")))
        .clone()
        .map_err(|e| anyhow::anyhow!("{e}"))
}

fn map_builtin_file(path: &Path) -> Result<&'static SerializedFile<'static>> {
    let handle =
        std::fs::File::open(path).with_context(|| format!("opening {}", path.display()))?;
    let map = unsafe { memmap2::Mmap::map(&handle) }
        .with_context(|| format!("mapping {}", path.display()))?;
    let data: &'static [u8] = Box::leak(Box::new(map));
    let file =
        SerializedFile::parse(data).with_context(|| format!("reading {}", path.display()))?;
    let file: &'static SerializedFile<'static> = Box::leak(Box::new(file));
    Ok(file)
}

/// An object of `file` as `{classId, fields}`, references with `m_FileID` 0 carrying `self_guid`.
fn object_of(file: &SerializedFile, path_id: i64, self_guid: &str) -> Result<Json> {
    let info = file.info(path_id)?;
    let value = file.read(path_id)?;
    let externals: Vec<String> = file
        .externals
        .iter()
        .map(|e| guid_of_external(&e.path).to_string())
        .collect();
    Ok(json!({ "classId": info.class_id, "fields": to_json(&value, &externals, self_guid) }))
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

/// `value` as JSON; references with `m_FileID` 0 point into the file itself (`self_guid`), others
/// into its `externals` (GUIDs, `m_FileID` 1 first).
fn to_json(value: &Value, externals: &[String], self_guid: &str) -> Json {
    match value {
        Value::Null => Json::Null,
        Value::Scalar(s) => Json::String(s.clone()),
        Value::Seq(items) => Json::Array(
            items
                .iter()
                .map(|v| to_json(v, externals, self_guid))
                .collect(),
        ),
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
                    self_guid.to_string()
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
                out.insert(k.clone(), to_json(v, externals, self_guid));
            }
            Json::Object(out)
        }
    }
}

