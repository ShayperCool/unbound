//! Property paths of prefab modifications: `m_LocalPosition.x`, `m_Materials.Array.data[0]`,
//! `items.Array.size`, `managedReferences[1234].field`.
use unbound_yaml::{Map, Value};

#[derive(Debug, Clone, PartialEq)]
enum Step {
    Key(String),
    Index(usize),
    Size,
    ManagedReference(i64),
}

fn parse(path: &str) -> Vec<Step> {
    let mut steps = Vec::new();
    let mut parts = path.split('.').peekable();
    while let Some(part) = parts.next() {
        if part == "Array" {
            match parts.next() {
                Some("size") => steps.push(Step::Size),
                Some(data) => {
                    let index = data
                        .strip_prefix("data[")
                        .and_then(|s| s.strip_suffix(']'))
                        .and_then(|s| s.parse().ok())
                        .unwrap_or(0);
                    steps.push(Step::Index(index));
                }
                None => {}
            }
        } else if let Some(rid) = part
            .strip_prefix("managedReferences[")
            .and_then(|s| s.strip_suffix(']'))
            .and_then(|s| s.parse().ok())
        {
            steps.push(Step::ManagedReference(rid));
        } else {
            steps.push(Step::Key(part.to_string()));
        }
    }
    steps
}

/// The node a path names, if it exists.
pub fn get<'a>(root: &'a Value, path: &str) -> Option<&'a Value> {
    let mut node = root;
    for step in parse(path) {
        node = match step {
            Step::Key(key) => node.get(&key)?,
            Step::Index(i) => node.as_seq()?.get(i)?,
            Step::Size => return None,
            Step::ManagedReference(rid) => managed_data(node, rid)?,
        };
    }
    Some(node)
}

fn managed_data(root: &Value, rid: i64) -> Option<&Value> {
    root.path("references.RefIds")?
        .as_seq()?
        .iter()
        .find(|entry| entry.get("rid").and_then(Value::as_i64) == Some(rid))?
        .get("data")
}

fn managed_data_mut(root: &mut Value, rid: i64) -> Option<&mut Value> {
    let ids = root
        .as_map_mut()?
        .get_mut("references")?
        .as_map_mut()?
        .get_mut("RefIds")?;
    let Value::Seq(entries) = ids else {
        return None;
    };
    entries
        .iter_mut()
        .find(|entry| entry.get("rid").and_then(Value::as_i64) == Some(rid))?
        .as_map_mut()?
        .get_mut("data")
}

fn ensure_map(node: &mut Value) -> &mut Map {
    if !matches!(node, Value::Map(_)) {
        *node = Value::Map(Map::default());
    }
    match node {
        Value::Map(map) => map,
        _ => unreachable!(),
    }
}

fn ensure_seq(node: &mut Value) -> &mut Vec<Value> {
    if !matches!(node, Value::Seq(_)) {
        *node = Value::Seq(Vec::new());
    }
    match node {
        Value::Seq(seq) => seq,
        _ => unreachable!(),
    }
}

/// Sets the node a path names, creating missing mappings and sequence slots on the way.
/// `Array.size` resizes the sequence: new slots copy the last element, as the Editor fills them.
pub fn set(root: &mut Value, path: &str, value: Value) {
    let steps = parse(path);
    let mut node = root;
    for (i, step) in steps.iter().enumerate() {
        let last = i + 1 == steps.len();
        match step {
            Step::Size => {
                let size = value.as_i64().unwrap_or(0).max(0) as usize;
                let seq = ensure_seq(node);
                let filler = seq.last().cloned().unwrap_or(Value::Null);
                seq.resize(size, filler);
                return;
            }
            Step::Key(key) => {
                let map = ensure_map(node);
                if map.get(key).is_none() {
                    map.0.push((key.clone(), Value::Null));
                }
                node = map.get_mut(key).unwrap();
            }
            Step::Index(index) => {
                let seq = ensure_seq(node);
                if seq.len() <= *index {
                    seq.resize(index + 1, Value::Null);
                }
                node = &mut seq[*index];
            }
            Step::ManagedReference(rid) => match managed_data_mut(node, *rid) {
                Some(data) => node = data,
                None => return,
            },
        }
        if last {
            *node = value;
            return;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn yaml(text: &str) -> Value {
        unbound_yaml::parse_value(text).unwrap()
    }

    #[test]
    fn sets_nested_and_array_paths() {
        let mut v = yaml("m_LocalPosition: {x: 1, y: 2, z: 3}\nm_Materials:\n- {fileID: 1}\n");
        set(&mut v, "m_LocalPosition.y", Value::Scalar("5".into()));
        assert_eq!(v.path("m_LocalPosition.y").unwrap().as_f64(), Some(5.0));
        set(&mut v, "m_Materials.Array.size", Value::Scalar("3".into()));
        assert_eq!(v.get("m_Materials").unwrap().as_seq().unwrap().len(), 3);
        set(&mut v, "m_Materials.Array.data[2]", yaml("{fileID: 7}"));
        assert_eq!(
            get(&v, "m_Materials.Array.data[2].fileID")
                .unwrap()
                .as_i64(),
            Some(7)
        );
        set(&mut v, "newField.inner", Value::Scalar("x".into()));
        assert_eq!(v.path("newField.inner").unwrap().as_str(), Some("x"));
    }

    #[test]
    fn sets_managed_references() {
        let mut v = yaml(
            "value: {rid: 5}\nreferences:\n  version: 2\n  RefIds:\n  - rid: 5\n    type: {class: A, ns: , asm: Game}\n    data:\n      hp: 1\n",
        );
        set(&mut v, "managedReferences[5].hp", Value::Scalar("9".into()));
        assert_eq!(
            get(&v, "managedReferences[5].hp").unwrap().as_i64(),
            Some(9)
        );
    }
}
