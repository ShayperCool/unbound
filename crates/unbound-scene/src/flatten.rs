//! Expanding prefab instances: the objects of each instance's source prefab (itself expanded) are
//! copied into the file under new file ids, then the instance's modifications, removals and
//! additions are applied, as the Editor merges a prefab instance. Each copied object keeps its
//! link as the Editor's merged objects do: `m_CorrespondingSourceObject` names the object of the
//! source prefab it comes from and `m_PrefabInstance` the file's PrefabInstance document, which
//! stays in the output (the source's own nested PrefabInstance documents do not: in the file,
//! the outermost instance stands for them).
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

fn instance_source(body: &Value) -> Guid {
    guid_of(body.get("m_SourcePrefab"))
}

fn instance_modification(body: &Value) -> &Value {
    body.get("m_Modification").unwrap_or(&Value::Null)
}

/// Collect the prefab input declarations using the same decoder as flattening, without
/// resolving referenced prefabs or importing models. The destination stays empty.
pub fn read_prefab_inputs(docs: &[Document]) {
    for instance in docs
        .iter()
        .filter(|d| d.class_id == PREFAB_INSTANCE && !d.stripped)
    {
        let _ = instance_source(&instance.body);
        apply_instance(
            &mut Vec::new(),
            instance_modification(&instance.body),
            &|id| id,
        );
        // The fields a modification sets, which apply_instance reads only for present targets.
        let mut scratch = Value::Map(Map::default());
        for m in modifications(&instance.body) {
            apply_modification(&mut scratch, m);
        }
    }
}

fn object_ref(file_id: i64) -> Value {
    Value::Map(Map::from(vec![(
        "fileID".into(),
        Value::Scalar(file_id.to_string()),
    )]))
}

/// Rewrites references local to the source prefab (no guid, or the source's own guid) to the
/// ids its objects get in the instance. A reference with the source's guid to an object that is
/// not one of its documents (`objects`) stays as it is: an asset of the source's import (a
/// ScriptedImporter's meshes and materials), loaded from the asset itself.
fn remap_refs(value: &mut Value, source: Guid, objects: &HashSet<i64>, map: &dyn Fn(i64) -> i64) {
    match value {
        Value::Map(m) => {
            let file = m.get("fileID").and_then(Value::as_i64);
            if let Some(file) = file {
                let guid = m.get("guid").and_then(Value::as_guid).unwrap_or_default();
                let local = guid.is_zero() || (guid == source && objects.contains(&file));
                if file != 0 && local && m.len() <= 3 {
                    m.insert("fileID", Value::Scalar(map(file).to_string()));
                    m.remove("guid");
                    m.remove("type");
                    return;
                }
            }
            for (_, v) in m.0.iter_mut() {
                remap_refs(v, source, objects, map);
            }
        }
        Value::Seq(items) => items
            .iter_mut()
            .for_each(|v| remap_refs(v, source, objects, map)),
        _ => {}
    }
}

/// Expands every prefab instance of a file's documents.
pub fn flatten(
    docs: &[Document],
    prefabs: &dyn PrefabSource,
    stack: &mut Vec<Guid>,
) -> Result<Vec<Document>> {
    let mut out: Vec<Document> = docs.iter().filter(|d| !d.stripped).cloned().collect();

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
        let source_guid = instance_source(&instance.body);
        if source_guid.is_zero() {
            continue;
        }
        if stack.contains(&source_guid) {
            bail!("prefab {source_guid} contains an instance of itself");
        }
        let source = match prefabs.prefab(source_guid, stack) {
            Ok(source) => source,
            Err(error) => {
                tracing::warn!("prefab instance {p}: {error:#}");
                let placeholders = missing_prefab_placeholder(instance, docs, source_guid);
                if let Some(map) = out
                    .iter_mut()
                    .find(|d| d.class_id == PREFAB_INSTANCE && d.file_id == p)
                    .and_then(|d| d.body.as_map_mut())
                {
                    let name = missing_instance_name(&instance.body, source_guid);
                    map.insert(MISSING_PREFAB_NAME, Value::Scalar(name));
                }
                out.extend(placeholders);
                continue;
            }
        };
        let map_id = |s: i64| stripped.get(&(p, s)).copied().unwrap_or((p ^ s) & ID_MASK);
        let source_ids: HashSet<i64> = source.iter().map(|d| d.file_id).collect();
        let mut objects: Vec<Document> = source
            .iter()
            .filter(|d| d.class_id != PREFAB_INSTANCE)
            .map(|d| {
                let mut d = d.clone();
                let source_id = d.file_id;
                d.file_id = map_id(source_id);
                remap_refs(&mut d.body, source_guid, &source_ids, &map_id);
                link_to_source(&mut d.body, source_id, source_guid, p);
                d
            })
            .collect();
        apply_instance(&mut objects, instance_modification(&instance.body), &map_id);
        out.extend(objects);
    }

    link_children(&mut out);
    Ok(out)
}

/// Whether the prefab these expanded documents make has, as its root, an instance of a missing
/// prefab (a variant whose parent is missing): the Unity 6000.0 Editor fails such a prefab's
/// import, so it has no objects and its own instances are missing too
/// (tests/projects/missingprefab).
pub fn root_is_missing_prefab(docs: &[Document]) -> bool {
    docs.iter().any(|d| {
        d.class_id == PREFAB_INSTANCE
            && d.body.get(MISSING_PREFAB_NAME).is_some()
            && file_id(instance_modification(&d.body).get("m_TransformParent")) == 0
    })
}

/// Records where an instance's object comes from: the source prefab's object and the instance.
fn link_to_source(body: &mut Value, source_id: i64, source: Guid, instance: i64) {
    let Some(map) = body.as_map_mut() else { return };
    map.insert(
        "m_CorrespondingSourceObject",
        Value::Map(Map::from(vec![
            ("fileID".into(), Value::Scalar(source_id.to_string())),
            ("guid".into(), Value::Scalar(source.to_string())),
            ("type".into(), Value::Scalar("3".into())),
        ])),
    );
    map.insert("m_PrefabInstance", object_ref(instance));
}

/// The prefix of the name of the placeholder for a stripped object of a missing prefab's instance.
const PLACEHOLDER_NAME: &str = "Placeholder for referenced";
/// Mixed into an instance's file id for the ids of the objects its placeholders add.
const PLACEHOLDER_ID_SALT: i64 = 0x4d50_0000_0000_0000;
/// The Unbound-only field a missing instance's PrefabInstance document gets: the name its root
/// shows (read by the managed scene load, which logs the Editor's error for it).
pub const MISSING_PREFAB_NAME: &str = "m_UnboundMissingPrefabName";
/// Property paths whose first modification names the instance's root Transform.
const ROOT_TRANSFORM_PROPERTIES: [&str; 3] = [
    "m_LocalPosition",
    "m_LocalRotation",
    "m_LocalEulerAnglesHint",
];

fn modifications(instance: &Value) -> &[Value] {
    instance_modification(instance)
        .get("m_Modifications")
        .and_then(Value::as_seq)
        .unwrap_or_default()
}

/// The source object the first modification of a property matching `matches` targets.
fn first_target(instance: &Value, matches: impl Fn(&str) -> bool) -> Option<&Value> {
    modifications(instance).iter().find(|m| {
        m.get("propertyPath")
            .and_then(Value::as_str)
            .is_some_and(&matches)
    })
}

/// The name a missing prefab's instance root shows: the instance's first `m_Name` modification
/// with the missing GUID, as the Unity 6000.0 Editor names it (tests/projects/missingprefab).
fn missing_instance_name(instance: &Value, source: Guid) -> String {
    let name = first_target(instance, |p| p == "m_Name")
        .and_then(|m| m.get("value"))
        .and_then(Value::as_str)
        .unwrap_or_default();
    if name.is_empty() {
        format!("Missing Prefab with guid: {source}")
    } else {
        format!("{name} (Missing Prefab with guid: {source})")
    }
}

fn source_file_id(doc: &Document) -> i64 {
    file_id(doc.body.get("m_CorrespondingSourceObject"))
}

/// The objects standing for an instance whose prefab is missing, as the Unity 6000.0 Editor makes
/// them (tests/projects/missingprefab): a root GameObject named by [`missing_instance_name`] under
/// the instance's parent, whose Transform takes the instance's modifications of the root
/// Transform, and for every other stripped object of the instance (what the file references),
/// in class id then file id order, a child of the root named "Placeholder for referenced
/// <class> in Prefab instance (<n>)", n counting the root's children: a stripped GameObject gets a
/// Transform, a stripped Transform a GameObject, a stripped component a GameObject and a
/// Transform. The root is the stripped GameObject the instance's first `m_Name` modification
/// targets, its Transform the stripped Transform the first position, rotation or Euler hint
/// modification targets; without both modifications the root is new and unmodified. Objects the
/// instance adds keep their parents and owners among these (added children of the root come first).
fn missing_prefab_placeholder(
    instance: &Document,
    docs: &[Document],
    source: Guid,
) -> Vec<Document> {
    let p = instance.file_id;
    let body = &instance.body;
    let modification = instance_modification(body);
    let root_go_source =
        first_target(body, |path| path == "m_Name").map(|m| file_id(m.get("target")));
    let root_t_source = first_target(body, |path| {
        ROOT_TRANSFORM_PROPERTIES
            .iter()
            .any(|prefix| path.starts_with(prefix))
    })
    .map(|m| file_id(m.get("target")));
    let known = root_go_source.is_some_and(|s| s != 0) && root_t_source.is_some_and(|s| s != 0);
    let mut stripped: Vec<&Document> = docs
        .iter()
        .filter(|d| d.stripped && file_id(d.body.get("m_PrefabInstance")) == p)
        .collect();
    stripped.sort_by_key(|d| (d.class_id, d.file_id));
    let find = |source: Option<i64>, class: &dyn Fn(i32) -> bool| {
        source
            .filter(|_| known)
            .and_then(|s| {
                stripped
                    .iter()
                    .find(|d| class(d.class_id) && source_file_id(d) == s)
            })
            .copied()
    };
    let root_go = find(root_go_source, &|c| c == 1);
    let root_t = find(root_t_source, &is_transform);
    let mut next_id = 0;
    let mut new_id = || {
        next_id += 1;
        (p ^ (PLACEHOLDER_ID_SALT + next_id)) & ID_MASK
    };
    let go_id = root_go.map_or_else(&mut new_id, |d| d.file_id);
    let transform_id = root_t.map_or_else(&mut new_id, |d| d.file_id);
    let parse = |yaml: String| unbound_yaml::parse_value(&yaml).unwrap_or_default();
    let link = |doc: Option<&Document>| match doc {
        Some(d) => format!(
            "m_CorrespondingSourceObject: {{fileID: {}, guid: {source}, type: 3}}\nm_PrefabInstance: {{fileID: {p}}}\n",
            source_file_id(d)
        ),
        None => format!("m_PrefabInstance: {{fileID: {p}}}\n"),
    };
    let game_object = |id: i64, name: &str, transform: i64, from: Option<&Document>| Document {
        class_id: 1,
        file_id: id,
        stripped: false,
        type_name: "GameObject".into(),
        body: parse(format!(
            "{}m_Component:\n- component: {{fileID: {transform}}}\nm_Name: '{}'\nm_IsActive: 1\n",
            link(from),
            name.replace('\'', "''")
        )),
    };
    let transform = |id: i64, go: i64, parent: i64, from: Option<&Document>| Document {
        class_id: from.map_or(TRANSFORM, |d| d.class_id),
        file_id: id,
        stripped: false,
        type_name: from.map_or("Transform".into(), |d| d.type_name.clone()),
        body: parse(format!(
            "{}m_GameObject: {{fileID: {go}}}\nm_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}\nm_LocalPosition: {{x: 0, y: 0, z: 0}}\nm_LocalScale: {{x: 1, y: 1, z: 1}}\nm_Children: []\nm_Father: {{fileID: {parent}}}\n",
            link(from)
        )),
    };

    let name = missing_instance_name(body, source);
    let mut root = transform(
        transform_id,
        go_id,
        file_id(modification.get("m_TransformParent")),
        root_t,
    );
    if known {
        let target = root_t_source.unwrap_or_default();
        for m in modifications(body)
            .iter()
            .filter(|m| file_id(m.get("target")) == target)
        {
            apply_modification(&mut root.body, m);
        }
    }
    let mut out = vec![game_object(go_id, &name, transform_id, root_go)];
    // The instance's added children of the root, then the placeholders.
    let mut children: Vec<i64> = docs
        .iter()
        .filter(|d| !d.stripped && is_transform(d.class_id))
        .filter(|d| file_id(d.body.get("m_Father")) == transform_id)
        .map(|d| d.file_id)
        .collect();
    for d in stripped {
        if root_go.is_some_and(|r| r.file_id == d.file_id)
            || root_t.is_some_and(|r| r.file_id == d.file_id)
        {
            continue;
        }
        let n = children.len() + 1;
        let class = if d.class_id == 1 {
            "GameObject"
        } else {
            d.type_name.as_str()
        };
        let label = format!("{PLACEHOLDER_NAME} {class} in Prefab instance ({n})");
        let (go, t) = match d.class_id {
            1 => (d.file_id, new_id()),
            c if is_transform(c) => (new_id(), d.file_id),
            _ => (new_id(), new_id()),
        };
        out.push(game_object(go, &label, t, (d.class_id == 1).then_some(d)));
        out.push(transform(
            t,
            go,
            transform_id,
            is_transform(d.class_id).then_some(d),
        ));
        if d.class_id != 1 && !is_transform(d.class_id) {
            let mut component = d.clone();
            component.stripped = false;
            if let Some(map) = component.body.as_map_mut() {
                map.insert("m_GameObject", object_ref(go));
            }
            if let Some(owner) = out.iter_mut().rev().find(|o| o.file_id == go) {
                insert_ref(
                    &mut owner.body,
                    "m_Component",
                    Some("component"),
                    d.file_id,
                    -1,
                );
            }
            out.push(component);
        }
        children.push(t);
    }
    if let Some(map) = root.body.as_map_mut() {
        map.insert(
            "m_Children",
            Value::Seq(children.into_iter().map(object_ref).collect()),
        );
    }
    out.insert(1, root);
    // Components the instance adds, on the objects they name as their GameObject.
    for added in modification
        .get("m_AddedComponents")
        .and_then(Value::as_seq)
        .unwrap_or_default()
    {
        let id = file_id(added.get("addedObject"));
        let Some(owner) = docs
            .iter()
            .find(|d| !d.stripped && d.file_id == id)
            .map(|d| file_id(d.body.get("m_GameObject")))
        else {
            continue;
        };
        if let Some(go) = out
            .iter_mut()
            .find(|o| o.class_id == 1 && o.file_id == owner)
        {
            insert_ref(&mut go.body, "m_Component", Some("component"), id, -1);
        }
    }
    out
}

/// Sets the property one `m_Modifications` entry names on an object's body.
fn apply_modification(body: &mut Value, m: &Value) {
    let Some(property) = m.get("propertyPath").and_then(Value::as_str) else {
        return;
    };
    let reference = m.get("objectReference").unwrap_or(&Value::Null);
    let reference_set = reference.as_ref().is_some_and(|r| r.file_id != 0);
    let existing_is_ref = path::get(body, property)
        .and_then(Value::as_map)
        .is_some_and(|m| m.get("fileID").is_some());
    let value = if reference_set || existing_is_ref {
        reference.clone()
    } else {
        match m.get("value") {
            Some(Value::Scalar(s)) => Value::Scalar(s.clone()),
            _ => Value::Scalar(String::new()),
        }
    };
    path::set(body, property, value);
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
        if let Some(&i) = index.get(&target) {
            apply_modification(&mut objects[i].body, m);
        }
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
        Some(k) => Value::Map(Map::from(vec![(k.to_string(), object_ref(id))])),
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

#[cfg(test)]
mod field_tests {
    use super::*;

    #[test]
    fn prefab_declarations_read_reference_parts_without_a_source_or_destination() {
        let mut body = unbound_yaml::parse_value(
            "m_SourcePrefab: {fileID: 100100000, guid: 11111111111111111111111111111111, type: 3}\nm_Modification:\n  m_Modifications:\n  - target: {fileID: 4}\n    propertyPath: m_LocalPosition.x\n    value: 3\n    objectReference: {fileID: 0}\n  - target: {fileID: 4}\n    propertyPath: m_Reference\n    value: unused\n    objectReference: {fileID: 5, guid: 22222222222222222222222222222222, type: 3, extra: 0}\n  m_AddedComponents:\n  - targetCorrespondingSourceObject: {fileID: 1}\n    addedObject: {fileID: 9}\n    insertIndex: 2\n",
        ).unwrap();
        let reads = unbound_yaml::reads::FieldReads::default();
        reads.track(&mut body);
        read_prefab_inputs(&[Document {
            class_id: PREFAB_INSTANCE,
            file_id: 10,
            stripped: false,
            type_name: "PrefabInstance".into(),
            body: body.clone(),
        }]);
        let paths = reads.paths();
        for field in [
            "value",
            "objectReference.fileID",
            "objectReference.guid",
            "objectReference.type",
        ] {
            assert!(
                paths.contains(&format!("m_Modification.m_Modifications[].{field}")),
                "{paths:?}"
            );
        }
        assert!(paths.contains("m_Modification.m_AddedComponents[].insertIndex"));
        assert!(!paths.contains("m_Modification.m_Modifications[].objectReference.extra"));

        let mut objects = vec![Document {
            class_id: TRANSFORM,
            file_id: 4,
            stripped: false,
            type_name: "Transform".into(),
            body: unbound_yaml::parse_value("m_LocalPosition: {x: 0}\n").unwrap(),
        }];
        apply_instance(&mut objects, instance_modification(&body), &|id| id);
        assert_eq!(
            objects[0]
                .body
                .get("m_LocalPosition")
                .unwrap()
                .get("x")
                .unwrap()
                .as_i64(),
            Some(3)
        );
        let reference = objects[0].body.get("m_Reference").unwrap();
        let decoded = reference.as_ref().unwrap();
        assert_eq!(decoded.file_id, 5);
        assert_eq!(
            decoded.guid,
            Guid::parse("22222222222222222222222222222222").unwrap()
        );
        assert_eq!(decoded.kind, 3);
        assert_eq!(reference.get("extra").unwrap().as_i64(), Some(0));
    }

    #[test]
    fn missing_targets_and_malformed_reference_ids_keep_their_existing_result() {
        let modification = unbound_yaml::parse_value("m_Modifications:\n- target: {fileID: 99}\n  propertyPath: missing\n  value: 7\n  objectReference: {fileID: 5}\n- target: {fileID: 4}\n  propertyPath: number\n  value: 6\n  objectReference: {fileID: invalid, guid: invalid, type: invalid}\n").unwrap();
        let mut objects = vec![Document {
            class_id: TRANSFORM,
            file_id: 4,
            stripped: false,
            type_name: "Transform".into(),
            body: unbound_yaml::parse_value("number: 1\n").unwrap(),
        }];
        apply_instance(&mut objects, &modification, &|id| id);
        assert_eq!(objects.len(), 1);
        assert_eq!(objects[0].body.get("number").unwrap().as_i64(), Some(6));
        assert!(objects[0].body.get("missing").is_none());
    }
}
