//! The documents of prefabs that ScriptedImporters make (SpeedTree 9 trees, a project's
//! importers): the importer runs in managed code, which writes the GameObjects and components of
//! the import's main hierarchy in Unity's text serialization. Scene and prefab instances of such
//! an asset then expand from these documents like any prefab's. Models and prefab files whose
//! imports the project's AssetPostprocessors change pass through the managed engine the same way
//! ([`postprocess`]).
use anyhow::{Context, Result, bail};
use std::collections::{HashMap, HashSet};
use std::ffi::c_void;
use std::path::Path;
use std::sync::RwLock;
use std::sync::atomic::{AtomicBool, Ordering};
use unbound_yaml::{Document, Guid, Value};

/// (asset GUID as 32 hex digits, length, sink): writes the asset's documents (Unity YAML) into the
/// sink through [`write`] and returns 0, or writes an error message and returns 1.
pub type DocumentSource = extern "C" fn(*const u8, i32, *mut c_void) -> i32;

static SOURCE: RwLock<Option<DocumentSource>> = RwLock::new(None);

/// Where the documents come from: the managed engine registers this once it runs.
pub fn set_source(source: Option<DocumentSource>) {
    *SOURCE.write().unwrap() = source;
}

/// Appends bytes to the sink a [`DocumentSource`] was given.
///
/// # Safety
/// `sink` is the pointer the source was called with, during that call; `bytes` holds `length`
/// bytes.
pub unsafe fn write(sink: *mut c_void, bytes: *const u8, length: i32) {
    if sink.is_null() || bytes.is_null() || length <= 0 {
        return;
    }
    let out = unsafe { &mut *(sink as *mut Vec<u8>) };
    out.extend_from_slice(unsafe { std::slice::from_raw_parts(bytes, length as usize) });
}

/// The documents of the prefab the asset `guid` imports to (its ScriptedImporter runs in the
/// managed engine). The lock is not held while the managed side runs: importing can load other
/// documents.
pub fn documents(guid: Guid) -> Result<Vec<Document>> {
    let source = (*SOURCE.read().unwrap())
        .context("its ScriptedImporter runs in the managed engine, which has not started")?;
    let key = guid.to_string();
    let mut out: Vec<u8> = Vec::new();
    let status = source(
        key.as_ptr(),
        key.len() as i32,
        &mut out as *mut Vec<u8> as *mut c_void,
    );
    let text = String::from_utf8(out).context("the imported prefab's documents are not UTF-8")?;
    if status != 0 {
        bail!("{text}");
    }
    unbound_yaml::parse_documents(&text).context("parsing the imported prefab's documents")
}

/// Whether the managed engine's postprocessors change model imports (OnPostprocessModel and the
/// other model callbacks) and prefab imports (OnPostprocessPrefab): set as its editor domain
/// loads.
static POSTPROCESS_MODELS: AtomicBool = AtomicBool::new(false);
static POSTPROCESS_PREFABS: AtomicBool = AtomicBool::new(false);

/// Sets which imports the postprocessors change; true when that differs from before (expanded
/// prefabs are then stale).
pub fn set_postprocessing(models: bool, prefabs: bool) -> bool {
    let before = (
        POSTPROCESS_MODELS.swap(models, Ordering::SeqCst),
        POSTPROCESS_PREFABS.swap(prefabs, Ordering::SeqCst),
    );
    before != (models, prefabs)
}

/// The documents of a prefab source after the managed engine's import postprocessors, from its
/// expanded documents: `None` when no postprocessor changes this kind of import (or the file is
/// neither a model nor a prefab: a ScriptedImporter's prefab gets none, measured in the Unity
/// 6000.0.71f1 Editor, tests/projects/assetpostprocessors Calm.st9).
pub fn postprocess(
    guid: Guid,
    file: &Path,
    expanded: &[Document],
) -> Option<Result<Vec<Document>>> {
    let extension = file.extension()?.to_str()?.to_ascii_lowercase();
    let active = if extension == "prefab" {
        POSTPROCESS_PREFABS.load(Ordering::SeqCst)
    } else if unbound_assets::model::EXTENSIONS.contains(&extension.as_str()) {
        POSTPROCESS_MODELS.load(Ordering::SeqCst)
    } else {
        false
    };
    if !active {
        return None;
    }
    Some(postprocessed(guid, expanded))
}

fn postprocessed(guid: Guid, expanded: &[Document]) -> Result<Vec<Document>> {
    let source = (*SOURCE.read().unwrap())
        .context("its postprocessors run in the managed engine, which has not started")?;
    let key = format!("postprocess:{guid}");
    let mut out: Vec<u8> = Vec::new();
    let status = source(
        key.as_ptr(),
        key.len() as i32,
        &mut out as *mut Vec<u8> as *mut c_void,
    );
    let text = String::from_utf8(out).context("the postprocessed documents are not UTF-8")?;
    if status != 0 {
        bail!("{text}");
    }
    merge(guid, expanded, &text)
}

/// Applies the managed engine's answer to the expanded documents of the asset `guid`: header
/// lines `# external: <ids>` (the import's objects outside the hierarchy: instances reference
/// the loaded ones, which the postprocessors may have changed, instead of copies), `# removed:
/// <ids>` (objects the postprocessors destroyed) and `# remap: <old>=<new> ...` (objects whose
/// fileID changed), then the documents of changed and added objects. Kept documents take the
/// new IDs, in their own ID and their local references; a changed one's keys replace theirs in
/// place; added ones follow.
fn merge(guid: Guid, expanded: &[Document], answer: &str) -> Result<Vec<Document>> {
    let mut external = HashSet::new();
    let mut removed = HashSet::new();
    let mut remap = HashMap::new();
    let mut yaml = answer;
    while let Some(line) = yaml.strip_prefix("# ") {
        let (header, rest) = line.split_once('\n').unwrap_or((line, ""));
        if let Some(ids) = header.strip_prefix("external:") {
            for id in ids.split_whitespace() {
                external.insert(id.parse::<i64>().context("an external fileID")?);
            }
        } else if let Some(ids) = header.strip_prefix("removed:") {
            for id in ids.split_whitespace() {
                removed.insert(id.parse::<i64>().context("a removed fileID")?);
            }
        } else if let Some(pairs) = header.strip_prefix("remap:") {
            for pair in pairs.split_whitespace() {
                let (old, new) = pair.split_once('=').context("a remapped fileID")?;
                remap.insert(old.parse::<i64>()?, new.parse::<i64>()?);
            }
        }
        yaml = rest;
    }
    let mut changed: Vec<Option<Document>> = if yaml.trim().is_empty() {
        Vec::new()
    } else {
        unbound_yaml::parse_documents(yaml)
            .context("parsing the postprocessed documents")?
            .into_iter()
            .map(Some)
            .collect()
    };
    let by_id: HashMap<i64, usize> = changed
        .iter()
        .enumerate()
        .map(|(i, d)| (d.as_ref().unwrap().file_id, i))
        .collect();
    let mut out = Vec::with_capacity(expanded.len() + changed.len());
    let rewrite = References {
        remap: &remap,
        external: &external,
        guid: guid.to_string(),
    };
    for doc in expanded {
        if removed.contains(&doc.file_id) || external.contains(&doc.file_id) {
            continue;
        }
        let id = remap.get(&doc.file_id).copied().unwrap_or(doc.file_id);
        let mut doc = doc.clone();
        doc.file_id = id;
        if !remap.is_empty() || !external.is_empty() {
            rewrite.apply(&mut doc.body);
        }
        // A changed object's document: its keys over the kept ones (what the writer leaves out,
        // such as a nested prefab's links, stays).
        if let Some(&i) = by_id.get(&id) {
            let changed = changed[i].take().unwrap();
            match (&mut doc.body, changed.body) {
                (Value::Map(kept), Value::Map(new)) => {
                    for (key, value) in new.iter() {
                        kept.insert(key, value.clone());
                    }
                }
                (body, new) => *body = new,
            }
        }
        out.push(doc);
    }
    out.extend(changed.into_iter().flatten());
    Ok(out)
}

/// How a kept document's local references (`{fileID: n}` without a GUID) change: to remapped
/// objects' new IDs, and to the asset's own objects outside the hierarchy by its GUID.
struct References<'a> {
    remap: &'a HashMap<i64, i64>,
    external: &'a HashSet<i64>,
    guid: String,
}

impl References<'_> {
    fn apply(&self, value: &mut Value) {
        match value {
            Value::Map(map) => {
                if map.get("guid").is_none()
                    && let Some(Value::Scalar(text)) = map.get_mut("fileID")
                {
                    let Ok(id) = text.trim().parse::<i64>() else {
                        return;
                    };
                    if let Some(new) = self.remap.get(&id) {
                        *text = new.to_string();
                    } else if self.external.contains(&id) {
                        map.insert("guid", Value::Scalar(self.guid.clone()));
                        map.insert("type", Value::Scalar("3".into()));
                    }
                    return;
                }
                let keys: Vec<String> = map.iter().map(|(k, _)| k.to_string()).collect();
                for key in keys {
                    if let Some(child) = map.get_mut(&key) {
                        self.apply(child);
                    }
                }
            }
            Value::Seq(items) => items.iter_mut().for_each(|item| self.apply(item)),
            _ => {}
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    extern "C" fn answer(guid: *const u8, length: i32, sink: *mut c_void) -> i32 {
        let guid = unsafe { std::slice::from_raw_parts(guid, length as usize) };
        let (status, text): (i32, &[u8]) = if guid.starts_with(b"0000") {
            (1, b"no such import")
        } else {
            (
                0,
                b"%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1 &5\nGameObject:\n  m_Name: Tree\n",
            )
        };
        unsafe { write(sink, text.as_ptr(), text.len() as i32) };
        status
    }

    #[test]
    fn documents_come_from_the_registered_source() {
        set_source(None);
        let guid = Guid::parse("9d7c89a9f6418f5886659b04a7e86b6b").unwrap();
        assert!(
            format!("{:#}", documents(guid).unwrap_err()).contains("has not started"),
            "without a source"
        );
        set_source(Some(answer));
        let docs = documents(guid).unwrap();
        assert_eq!(docs.len(), 1);
        assert_eq!((docs[0].class_id, docs[0].file_id), (1, 5));
        assert_eq!(
            docs[0].body.get("m_Name").and_then(|v| v.as_str()),
            Some("Tree")
        );
        let failing = Guid::parse("00000000000000000000000000000001").unwrap();
        assert_eq!(
            format!("{:#}", documents(failing).unwrap_err()),
            "no such import"
        );
        set_source(None);
    }

    #[test]
    fn the_postprocessors_answer_removes_remaps_overlays_and_adds_documents() {
        let expanded = unbound_yaml::parse_documents(
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n\
             --- !u!1 &1\nGameObject:\n  m_Component:\n  - component: {fileID: 2}\n  - component: {fileID: 3}\n  m_Name: Node\n\
             --- !u!4 &2\nTransform:\n  m_GameObject: {fileID: 1}\n  m_Children: []\n  m_Father: {fileID: 0}\n\
             --- !u!95 &3\nAnimator:\n  m_GameObject: {fileID: 1}\n  m_Avatar: {fileID: 9, guid: 0000000000000000000000000000abcd, type: 3}\n\
             --- !u!114 &4\nMonoBehaviour:\n  m_GameObject: {fileID: 1}\n",
        )
        .unwrap();
        // The node renamed (new IDs), its behaviour destroyed, a collider added; the GameObject's
        // document changed (the writer leaves out m_PrefabInstance, which stays).
        let answer = "# removed: 4\n# remap: 1=10 2=20 3=30\n%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n\
             --- !u!1 &10\nGameObject:\n  m_Component:\n  - component: {fileID: 20}\n  - component: {fileID: 30}\n  - component: {fileID: 40}\n  m_Name: Renamed\n\
             --- !u!65 &40\nBoxCollider:\n  m_GameObject: {fileID: 10}\n";
        let mut expanded = expanded;
        if let Value::Map(map) = &mut expanded[0].body {
            map.insert("m_PrefabInstance", Value::Scalar("{fileID: 0}".into()));
        }
        let guid = Guid::parse("0000000000000000000000000000abcd").unwrap();
        let merged = merge(guid, &expanded, answer).unwrap();
        let ids: Vec<i64> = merged.iter().map(|d| d.file_id).collect();
        assert_eq!(ids, [10, 20, 30, 40]);
        assert_eq!(
            merged[0].body.get("m_Name").and_then(|v| v.as_str()),
            Some("Renamed")
        );
        assert!(merged[0].body.get("m_PrefabInstance").is_some());
        // A kept document's own references follow the new IDs; external ones stay.
        assert_eq!(
            merged[2].body.path("m_GameObject.fileID").unwrap().as_i64(),
            Some(10)
        );
        assert_eq!(
            merged[2].body.path("m_Avatar.fileID").unwrap().as_i64(),
            Some(9)
        );
        assert_eq!(
            merged[1].body.path("m_GameObject.fileID").unwrap().as_i64(),
            Some(10)
        );
        // Nothing changed: the expansion as it was.
        assert_eq!(merge(guid, &expanded, "").unwrap(), expanded);
        // The import's other objects: referenced by the asset's GUID, their documents left out.
        let mesh = unbound_yaml::parse_documents(
            "%YAML 1.1\n--- !u!33 &5\nMeshFilter:\n  m_GameObject: {fileID: 1}\n  m_Mesh: {fileID: 6}\n--- !u!43 &6\nMesh:\n  m_Name: M\n",
        )
        .unwrap();
        let merged = merge(guid, &mesh, "# external: 6\n").unwrap();
        assert_eq!(merged.len(), 1);
        assert_eq!(
            merged[0].body.path("m_Mesh.fileID").unwrap().as_i64(),
            Some(6)
        );
        assert_eq!(
            merged[0].body.path("m_Mesh.guid").unwrap().as_guid(),
            Some(guid)
        );
        assert!(merged[0].body.path("m_GameObject.guid").is_none());
    }
}
