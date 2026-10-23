//! Expanding prefab instances: the objects of each instance's source prefab (itself expanded) are
//! copied into the file under new file ids, then the instance's modifications, removals and
//! additions are applied, as the Editor merges a prefab instance.
use crate::path;
use anyhow::{Result, bail};
use std::{
    collections::{HashMap, HashSet},
    sync::Arc,
};
use unbound_yaml::{Document, Guid, Map, Value};

const PREFAB_INSTANCE: i32 = 1001;
const TRANSFORM: i32 = 4;
const RECT_TRANSFORM: i32 = 224;
/// File ids of a prefab instance's objects: `(instance id ^ source id) & ID_MASK`.
const ID_MASK: i64 = 0x7fff_ffff_ffff_ffff;

/// Supplies the expanded documents of the prefab a GUID names.
pub trait PrefabSource {
    fn prefab(&self, guid: Guid, stack: &mut Vec<Guid>) -> Result<Arc<Vec<Document>>>;
}

fn is_transform(class_id: i32) -> bool {
    class_id == TRANSFORM || class_id == RECT_TRANSFORM
}

fn file_id(value: Option<&Value>) -> i64 {
    value
        .and_then(|v| v.get("fileID"))
        .and_then(Value::as_i64)
        .unwrap_or(0)
}

fn guid_of(value: Option<&Value>) -> Guid {
    value
        .and_then(|v| v.get("guid"))
        .and_then(Value::as_guid)
        .unwrap_or_default()
}

fn object_ref(file_id: i64) -> Value {
    Value::Map(Map(vec![(
        "fileID".into(),
        Value::Scalar(file_id.to_string()),
    )]))
}

/// Rewrites references local to the source prefab (no guid, or the source's own guid) to the
/// ids its objects get in the instance.
fn remap_refs(value: &mut Value, source: Guid, map: &dyn Fn(i64) -> i64) {
    match value {
        Value::Map(m) => {
            let file = m.get("fileID").and_then(Value::as_i64);
            if let Some(file) = file {
                let guid = m.get("guid").and_then(Value::as_guid).unwrap_or_default();
                if file != 0 && (guid.is_zero() || guid == source) && m.len() <= 3 {
                    m.insert("fileID", Value::Scalar(map(file).to_string()));
                    m.remove("guid");
                    m.remove("type");
                    return;
                }
            }
            for (_, v) in m.0.iter_mut() {
                remap_refs(v, source, map);
            }
        }
        Value::Seq(items) => items.iter_mut().for_each(|v| remap_refs(v, source, map)),
        _ => {}
    }
}

/// Expands every prefab instance of a file's documents.
pub fn flatten(
    docs: &[Document],
    prefabs: &dyn PrefabSource,
    stack: &mut Vec<Guid>,
) -> Result<Vec<Document>> {
    let mut out: Vec<Document> = docs
        .iter()
        .filter(|d| !d.stripped && d.class_id != PREFAB_INSTANCE)
        .cloned()
        .collect();

    // Ids the file gave instance objects it references (legacy files keep explicit ones).
    let mut stripped: HashMap<(i64, i64), i64> = HashMap::new();
    for d in docs.iter().filter(|d| d.stripped) {
        let instance = file_id(d.body.get("m_PrefabInstance"));
        let source = file_id(d.body.get("m_CorrespondingSourceObject"));
        if instance != 0 && source != 0 {
            stripped.insert((instance, source), d.file_id);
        }
    }

    for instance in docs
        .iter()
        .filter(|d| d.class_id == PREFAB_INSTANCE && !d.stripped)
    {
        let p = instance.file_id;
        let source_guid = guid_of(instance.body.get("m_SourcePrefab"));
        if source_guid.is_zero() {
            continue;
        }
        if stack.contains(&source_guid) {
            bail!("prefab {source_guid} contains an instance of itself");
        }
        let source = match prefabs.prefab(source_guid, stack) {
            Ok(source) => source,
            Err(error) => {
                // The Editor keeps such an instance as a "Missing Prefab" placeholder object.
                tracing::warn!("prefab instance {p}: {error:#}");
                out.extend(missing_prefab_placeholder(instance, docs, source_guid));
                continue;
            }
        };
        let map_id = |s: i64| stripped.get(&(p, s)).copied().unwrap_or((p ^ s) & ID_MASK);
        let mut objects: Vec<Document> = source
            .iter()
            .map(|d| {
                let mut d = d.clone();
                d.file_id = map_id(d.file_id);
                remap_refs(&mut d.body, source_guid, &map_id);
                d
            })
            .collect();
        let modification = instance
            .body
            .get("m_Modification")
            .cloned()
            .unwrap_or_default();
        apply_instance(&mut objects, &modification, &map_id);
        out.extend(objects);
    }

    link_children(&mut out);
    Ok(out)
}

/// A GameObject standing for an instance whose prefab is missing, under the instance's parent;
/// a stripped transform of the instance (what other objects may reference) becomes its transform.
fn missing_prefab_placeholder(
    instance: &Document,
    docs: &[Document],
    source: Guid,
) -> Vec<Document> {
    let p = instance.file_id;
    let modification = instance
        .body
        .get("m_Modification")
        .cloned()
        .unwrap_or_default();
    let name = modification
        .get("m_Modifications")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .find(|m| m.get("propertyPath").and_then(Value::as_str) == Some("m_Name"))
        .and_then(|m| m.get("value"))
        .and_then(Value::as_str)
        .map(String::from)
        .unwrap_or_else(|| format!("Missing Prefab with guid: {source}"));
    let transform_id = docs
        .iter()
        .find(|d| {
            d.stripped && is_transform(d.class_id) && file_id(d.body.get("m_PrefabInstance")) == p
        })
        .map(|d| d.file_id)
        .unwrap_or((p ^ 2) & ID_MASK);
    let go_id = (p ^ 1) & ID_MASK;
    let parse = |yaml: String| unbound_yaml::parse_value(&yaml).unwrap_or_default();
    let go = Document {
        class_id: 1,
        file_id: go_id,
        stripped: false,
        type_name: "GameObject".into(),
        body: parse(format!(
            "m_Component:\n- component: {{fileID: {transform_id}}}\nm_Name: '{}'\nm_IsActive: 1\n",
            name.replace('\'', "''")
        )),
    };
    let parent = file_id(modification.get("m_TransformParent"));
    let transform = Document {
        class_id: TRANSFORM,
        file_id: transform_id,
        stripped: false,
        type_name: "Transform".into(),
        body: parse(format!(
            "m_GameObject: {{fileID: {go_id}}}\nm_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\nm_LocalPosition: {{x: 0, y: 0, z: 0}}\nm_LocalScale: {{x: 1, y: 1, z: 1}}\nm_Children: []\nm_Father: {{fileID: {parent}}}\n"
        )),
    };
    vec![go, transform]
}

fn apply_instance(objects: &mut Vec<Document>, modification: &Value, map_id: &dyn Fn(i64) -> i64) {
    let index: HashMap<i64, usize> = objects
        .iter()
        .enumerate()
        .map(|(i, d)| (d.file_id, i))
        .collect();

    // Property modifications: sizes first so that elements land in resized arrays.
    let mods: Vec<&Value> = modification
        .get("m_Modifications")
        .and_then(Value::as_seq)
        .map(|s| s.iter().collect())
        .unwrap_or_default();
    let (sizes, others): (Vec<&Value>, Vec<&Value>) = mods.into_iter().partition(|m| {
        m.get("propertyPath")
            .and_then(Value::as_str)
            .is_some_and(|p| p.ends_with("Array.size"))
    });
    for m in sizes.into_iter().chain(others) {
        let target = map_id(file_id(m.get("target")));
        let Some(&i) = index.get(&target) else {
            continue;
        };
        let Some(property) = m.get("propertyPath").and_then(Value::as_str) else {
            continue;
        };
        let reference = m.get("objectReference").cloned().unwrap_or(Value::Null);
        let reference_set = file_id(Some(&reference)) != 0;
        let body = &mut objects[i].body;
        let existing_is_ref = path::get(body, property)
            .and_then(Value::as_map)
            .is_some_and(|m| m.get("fileID").is_some());
        let value = if reference_set || existing_is_ref {
            reference
        } else {
            match m.get("value") {
                Some(Value::Scalar(s)) => Value::Scalar(s.clone()),
                _ => Value::Scalar(String::new()),
            }
        };
        path::set(body, property, value);
    }

    // Removed components and GameObjects.
    let mut removed: HashSet<i64> = HashSet::new();
    for r in modification
        .get("m_RemovedComponents")
        .and_then(Value::as_seq)
        .unwrap_or_default()
    {
        removed.insert(map_id(file_id(Some(r))));
    }
    for r in modification
        .get("m_RemovedGameObjects")
        .and_then(Value::as_seq)
        .unwrap_or_default()
    {
        let go = map_id(file_id(Some(r)));
        collect_subtree(objects, go, &mut removed);
    }
    if !removed.is_empty() {
        objects.retain(|d| !removed.contains(&d.file_id));
        for d in objects.iter_mut() {
            prune_refs(&mut d.body, "m_Component", "component", &removed);
            prune_list(&mut d.body, "m_Children", &removed);
        }
    }

    // The instance root goes under the transform the file names.
    let parent = file_id(modification.get("m_TransformParent"));
    if let Some(root) = objects
        .iter_mut()
        .find(|d| is_transform(d.class_id) && file_id(d.body.get("m_Father")) == 0)
        && let Some(map) = root.body.as_map_mut()
    {
        map.insert("m_Father", object_ref(parent));
    }

    // Objects the file adds inside the instance, at their recorded positions.
    let added_children: Vec<(i64, i64, i64)> = modification
        .get("m_AddedGameObjects")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .map(|a| {
            (
                map_id(file_id(a.get("targetCorrespondingSourceObject"))),
                file_id(a.get("addedObject")),
                a.get("insertIndex").and_then(Value::as_i64).unwrap_or(-1),
            )
        })
        .collect();
    for (target, added, at) in added_children {
        if let Some(t) = objects.iter_mut().find(|d| d.file_id == target) {
            insert_ref(&mut t.body, "m_Children", None, added, at);
        }
    }
    let added_components: Vec<(i64, i64, i64)> = modification
        .get("m_AddedComponents")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .map(|a| {
            (
                map_id(file_id(a.get("targetCorrespondingSourceObject"))),
                file_id(a.get("addedObject")),
                a.get("insertIndex").and_then(Value::as_i64).unwrap_or(-1),
            )
        })
        .collect();
    for (target, added, at) in added_components {
        if let Some(go) = objects.iter_mut().find(|d| d.file_id == target) {
            insert_ref(&mut go.body, "m_Component", Some("component"), added, at);
        }
    }
}

/// A GameObject, its components, and every descendant's, by the transform hierarchy.
fn collect_subtree(objects: &[Document], go: i64, into: &mut HashSet<i64>) {
    into.insert(go);
    let components: Vec<i64> = objects
        .iter()
        .filter(|d| file_id(d.body.get("m_GameObject")) == go)
        .map(|d| d.file_id)
        .collect();
    for c in &components {
        into.insert(*c);
    }
    for t in objects
        .iter()
        .filter(|d| is_transform(d.class_id) && components.contains(&d.file_id))
    {
        for child in t
            .body
            .get("m_Children")
            .and_then(Value::as_seq)
            .unwrap_or_default()
        {
            let child_transform = file_id(Some(child));
            if let Some(owner) = objects
                .iter()
                .find(|d| d.file_id == child_transform)
                .map(|d| file_id(d.body.get("m_GameObject")))
            {
                collect_subtree(objects, owner, into);
            }
        }
    }
}

fn prune_refs(body: &mut Value, list: &str, key: &str, removed: &HashSet<i64>) {
    if let Some(Value::Seq(items)) = body.as_map_mut().and_then(|m| m.get_mut(list)) {
        items.retain(|item| !removed.contains(&file_id(item.get(key))));
    }
}

fn prune_list(body: &mut Value, list: &str, removed: &HashSet<i64>) {
    if let Some(Value::Seq(items)) = body.as_map_mut().and_then(|m| m.get_mut(list)) {
        items.retain(|item| !removed.contains(&file_id(Some(item))));
    }
}

fn insert_ref(body: &mut Value, list: &str, key: Option<&str>, id: i64, at: i64) {
    let Some(map) = body.as_map_mut() else { return };
    if map.get(list).is_none() {
        map.insert(list, Value::Seq(Vec::new()));
    }
    let Some(Value::Seq(items)) = map.get_mut(list) else {
        return;
    };
    let present = items.iter().any(|item| match key {
        Some(k) => file_id(item.get(k)) == id,
        None => file_id(Some(item)) == id,
    });
    if present {
        return;
    }
    let entry = match key {
        Some(k) => Value::Map(Map(vec![(k.to_string(), object_ref(id))])),
        None => object_ref(id),
    };
    if at >= 0 && (at as usize) <= items.len() {
        items.insert(at as usize, entry);
    } else {
        items.push(entry);
    }
}

/// Every transform with a parent is listed in that parent's `m_Children`, once.
fn link_children(docs: &mut [Document]) {
    let index: HashMap<i64, usize> = docs
        .iter()
        .enumerate()
        .map(|(i, d)| (d.file_id, i))
        .collect();
    let links: Vec<(usize, i64)> = docs
        .iter()
        .filter(|d| is_transform(d.class_id))
        .filter_map(|d| {
            let father = file_id(d.body.get("m_Father"));
            index.get(&father).map(|&i| (i, d.file_id))
        })
        .collect();
    for (parent, child) in links {
        insert_ref(&mut docs[parent].body, "m_Children", None, child, -1);
    }
    // Drop children entries that point at objects no longer present or parented elsewhere.
    let fathers: HashMap<i64, i64> = docs
        .iter()
        .filter(|d| is_transform(d.class_id))
        .map(|d| (d.file_id, file_id(d.body.get("m_Father"))))
        .collect();
    for d in docs.iter_mut().filter(|d| is_transform(d.class_id)) {
        let me = d.file_id;
        if let Some(Value::Seq(items)) = d.body.as_map_mut().and_then(|m| m.get_mut("m_Children")) {
            items.retain(|item| fathers.get(&file_id(Some(item))) == Some(&me));
        }
    }
}
