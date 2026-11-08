//! Optional per-tree read capture for diagnostics. Ordinary parsed maps carry no collector.
//! Cloning a tracked map keeps its source path, so readers that clone and transform prefab or
//! binary input still account for the fields of the original serialized object.
use crate::Value;
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

#[cfg(test)]
mod tests {
    use super::*;
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
