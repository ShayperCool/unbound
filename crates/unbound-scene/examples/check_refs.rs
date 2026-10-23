//! Expands every prefab and scene of a project and reports local object references that point
//! at no object of the expanded file: a corpus check of prefab flattening's id remapping.
//! Usage: `check_refs <project> [path-filter]`.
use std::collections::{BTreeMap, HashSet};
use unbound_project::Project;
use unbound_scene::DocumentLoader;
use unbound_yaml::Value;

fn refs<'a>(value: &'a Value, path: &mut Vec<String>, out: &mut Vec<(String, i64)>) {
    if let Some(r) = value.as_ref()
        && value.as_map().is_some_and(|m| m.0.len() <= 3)
    {
        if !r.is_null() && r.is_local() {
            out.push((path.join("."), r.file_id));
        }
        return;
    }
    match value {
        Value::Map(map) => {
            for (k, v) in &map.0 {
                path.push(k.clone());
                refs(v, path, out);
                path.pop();
            }
        }
        Value::Seq(items) => {
            for (i, v) in items.iter().enumerate() {
                path.push(i.to_string());
                refs(v, path, out);
                path.pop();
            }
        }
        _ => {}
    }
}

fn main() -> anyhow::Result<()> {
    let mut args = std::env::args().skip(1);
    let root = args.next().expect("usage: check_refs <project> [filter]");
    let filter = args.next().unwrap_or_default();
    let project = Project::open(std::path::Path::new(&root))?;
    let db = project.asset_db();
    let loader = DocumentLoader::new(|guid| db.by_guid(guid).map(|a| a.file.clone()));
    let (mut files, mut dangling_files, mut dangling) = (0, 0, 0);
    let mut by_field: BTreeMap<String, usize> = BTreeMap::new();
    for asset in db.iter() {
        let ext = asset.path.rsplit('.').next().unwrap_or("");
        if (ext != "prefab" && ext != "unity") || !asset.path.contains(&filter) {
            continue;
        }
        let Ok(docs) = loader.load(&asset.file) else { continue };
        files += 1;
        let ids: HashSet<i64> = docs.iter().map(|d| d.file_id).collect();
        let mut found = 0;
        for doc in &docs {
            if doc.stripped {
                continue;
            }
            let mut out = Vec::new();
            refs(&doc.body, &mut vec![], &mut out);
            for (field, id) in out {
                if field.starts_with("m_CorrespondingSourceObject")
                    || field.starts_with("m_PrefabInternal")
                    || field.starts_with("m_PrefabParent")
                    || field.starts_with("m_PrefabInstance")
                    || field.starts_with("m_PrefabAsset")
                {
                    continue;
                }
                if !ids.contains(&id) {
                    found += 1;
                    *by_field.entry(format!("{}.{}", doc.type_name, field)).or_default() += 1;
                    if found <= 3 && dangling_files < 15 {
                        eprintln!("{}: {} {} &{} -> {id} missing", asset.path, doc.type_name, field, doc.file_id);
                    }
                }
            }
        }
        if found > 0 {
            dangling_files += 1;
            dangling += found;
        }
    }
    println!("{files} files, {dangling_files} with dangling local references ({dangling} references)");
    let mut fields: Vec<_> = by_field.into_iter().collect();
    fields.sort_by(|a, b| b.1.cmp(&a.1));
    for (field, n) in fields.into_iter().take(25) {
        println!("{n:6} {field}");
    }
    Ok(())
}
