//! The documents of prefabs that ScriptedImporters make (SpeedTree 9 trees, a project's
//! importers): the importer runs in managed code, which writes the GameObjects and components of
//! the import's main hierarchy in Unity's text serialization. Scene and prefab instances of such
//! an asset then expand from these documents like any prefab's.
use anyhow::{Context, Result, bail};
use std::ffi::c_void;
use std::sync::RwLock;
use unbound_yaml::{Document, Guid};

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
}
