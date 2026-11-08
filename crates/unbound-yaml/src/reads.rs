//! Optional per-tree read capture for diagnostics. Ordinary parsed maps carry no collector.
//! Cloning a tracked map keeps its source path, so readers that clone and transform prefab or
//! binary input still account for the fields of the original serialized object.
use crate::{Document, Value};
use std::{
    cell::RefCell,
    collections::BTreeMap,
    path::{Path, PathBuf},
};
use std::{
    collections::BTreeSet,
    sync::{Arc, Mutex},
};

#[derive(Clone, Default, Debug)]
pub struct FieldReads(Arc<Mutex<BTreeSet<String>>>);

/// Join a diagnostic path without confusing literal punctuation with path separators.
pub fn field_path(prefix: &str, key: &str) -> String {
    let mut path = prefix.to_owned();
    if !path.is_empty() {
        path.push('.');
    }
    for ch in key.chars() {
        if matches!(ch, '.' | '[' | ']' | '\\') {
            path.push('\\');
        }
        path.push(ch);
    }
    path
}

#[derive(Clone, Debug)]
pub(crate) struct TrackedMap {
    prefix: String,
    reads: FieldReads,
}

impl TrackedMap {
    pub(crate) fn note(&self, key: &str) {
        let path = field_path(&self.prefix, key);
        self.reads.0.lock().unwrap().insert(path);
    }
}

impl FieldReads {
    pub fn track(&self, value: &mut Value) {
        self.track_at(value, "");
    }

    fn track_at(&self, value: &mut Value, prefix: &str) {
        match value {
            Value::Map(map) => {
                map.1 = Some(TrackedMap {
                    prefix: prefix.into(),
                    reads: self.clone(),
                });
                for (name, child) in &mut map.0 {
                    self.track_at(child, &field_path(prefix, name));
                }
            }
            Value::Seq(items) => {
                for child in items {
                    self.track_at(child, &format!("{prefix}[]"));
                }
            }
            _ => {}
        }
    }

    pub fn paths(&self) -> BTreeSet<String> {
        self.0.lock().unwrap().clone()
    }
}

/// Reads recorded while a diagnostic invokes the ordinary file readers on this thread.
/// The collector is scoped and never changes parser or reader results.
#[derive(Default)]
pub struct FileReads {
    files: BTreeMap<PathBuf, Vec<(Document, FieldReads)>>,
}

thread_local! {
    static FILE_READS: RefCell<Option<FileReads>> = const { RefCell::new(None) };
}

impl FileReads {
    pub fn capture<T>(read: impl FnOnce() -> T) -> (T, Self) {
        struct Reset;
        impl Drop for Reset {
            fn drop(&mut self) {
                FILE_READS.with(|slot| slot.borrow_mut().take());
            }
        }
        FILE_READS.with(|slot| {
            assert!(slot.borrow().is_none(), "nested file read capture");
            *slot.borrow_mut() = Some(Self::default());
        });
        let _reset = Reset;
        let result = read();
        let reads = FILE_READS.with(|slot| slot.borrow_mut().take().unwrap());
        (result, reads)
    }

    pub fn paths(&self, path: &Path, file_id: i64) -> BTreeSet<String> {
        self.files
            .get(path)
            .into_iter()
            .flatten()
            .filter(|(doc, _)| doc.file_id == file_id)
            .flat_map(|(doc, reads)| reads.consumed(&doc.body))
            .collect()
    }
}

/// Parse a settings file through the same reader used during startup. Diagnostics can observe
/// the trees without invoking a game, creating a device or importing source content.
pub fn read_documents(path: &Path) -> anyhow::Result<Vec<Document>> {
    let text = std::fs::read_to_string(path)?;
    let mut docs = crate::parse_documents(&text)?;
    FILE_READS.with(|slot| {
        if let Some(capture) = slot.borrow_mut().as_mut() {
            for doc in &mut docs {
                let reads = FieldReads::default();
                reads.track(&mut doc.body);
                capture
                    .files
                    .entry(path.into())
                    .or_default()
                    .push((doc.clone(), reads));
            }
        }
    });
    Ok(docs)
}

impl FieldReads {
    /// Expand reads of scalar sequences into their leaf paths. Reading a map never implies
    /// reading every child, so an ignored member of a consumed structure remains visible.
    pub fn consumed(&self, value: &Value) -> BTreeSet<String> {
        fn walk(
            value: &Value,
            path: &str,
            reads: &BTreeSet<String>,
            out: &mut BTreeSet<String>,
            sequence_read: bool,
        ) {
            match value {
                Value::Map(map) if !map.is_empty() => {
                    for (key, value) in &map.0 {
                        walk(value, &field_path(path, key), reads, out, false);
                    }
                }
                Value::Seq(items) if !items.is_empty() => {
                    for item in items {
                        walk(
                            item,
                            &format!("{path}[]"),
                            reads,
                            out,
                            reads.contains(path) || sequence_read,
                        );
                    }
                }
                _ if reads.contains(path) || sequence_read => {
                    out.insert(path.into());
                }
                _ => {}
            }
        }
        let mut out = BTreeSet::new();
        walk(value, "", &self.paths(), &mut out, false);
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn iteration_and_scalar_arrays_record_leaves_but_not_unread_struct_members() {
        let mut value =
            crate::parse_value("tags: [a, b]\nmap: {a: 1, b: 2}\nnested: {used: 1, unused: 2}\n")
                .unwrap();
        let reads = FieldReads::default();
        reads.track(&mut value);
        let _ = value.get("tags").unwrap().as_seq();
        for (_, item) in value.get("map").unwrap().as_map().unwrap().iter() {
            let _ = item.as_i64();
        }
        value.path("nested.used");
        assert_eq!(
            reads.consumed(&value),
            BTreeSet::from([
                "tags[]".into(),
                "map.a".into(),
                "map.b".into(),
                "nested.used".into()
            ])
        );
    }

    #[test]
    fn file_capture_is_scoped_and_stopping_a_settings_read_exposes_it() {
        let path =
            std::env::temp_dir().join(format!("unbound-settings-{}.asset", std::process::id()));
        std::fs::write(
            &path,
            "--- !u!129 &1\nPlayerSettings:\n  productName: Test\n  ignored: 0\n",
        )
        .unwrap();
        let read = |consume| {
            FileReads::capture(|| {
                let doc = read_documents(&path).unwrap().remove(0);
                if consume {
                    doc.body.get("productName");
                }
            })
            .1
            .paths(&path, 1)
        };
        assert!(read(true).contains("productName"));
        assert!(!read(false).contains("productName"));
        assert!(!read(true).contains("ignored"));
        let ordinary = read_documents(&path).unwrap();
        assert_eq!(
            ordinary[0].body.get("productName").unwrap().as_str(),
            Some("Test")
        );
        std::fs::remove_file(path).unwrap();
    }

    #[test]
    fn records_actual_reads_through_cloned_trees_without_changing_equality_or_serialization() {
        let plain = crate::parse_value("x: 0\nnested:\n  y: 2\n  extra: 0\n").unwrap();
        let mut value = plain.clone();
        let reads = FieldReads::default();
        reads.track(&mut value);
        let cloned = value.clone();
        assert_eq!(cloned, plain);
        let legacy = crate::parse_value("x: 0").unwrap();
        assert_eq!(
            serde_json::to_value(legacy).unwrap(),
            serde_json::json!({"Map": [["x", {"Scalar": "0"}]]})
        );
        assert_eq!(
            serde_json::to_string(&cloned).unwrap(),
            serde_json::to_string(&plain).unwrap()
        );
        cloned.get("nested").unwrap().get("y");
        assert_eq!(
            reads.paths(),
            BTreeSet::from(["nested".into(), "nested.y".into()])
        );
        assert!(!reads.paths().contains("x"));
        cloned.get("x");
        assert!(reads.paths().contains("x"));
    }
}
