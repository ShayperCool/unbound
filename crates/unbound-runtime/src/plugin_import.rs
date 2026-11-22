//! GUID conflicts discovered when opening a project's precompiled plugins.
use anyhow::{Context, Result};
use std::{
    collections::{BTreeMap, HashSet},
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
};
use unbound_assets::new_meta::{meta_path, new_guid, replace_guid};
use unbound_project::{Project, assetdb::read_meta_guid, plugins::Plugin};
use unbound_yaml::Guid;

/// Import DLL metadata before indexing assets or building the compilation graph. Read-only
/// graph inspection never writes project files.
pub fn repair_plugin_guid_conflicts(project: &Project) -> Result<()> {
    if project.version.measured_family() != Some(unbound_compat::MeasuredFamily::Unity6000_0) {
        return Ok(());
    }
    let plugins = project.plugins();
    let mut groups = BTreeMap::<Guid, Vec<&Plugin>>::new();
    for plugin in &plugins {
        if let Some(guid) = plugin.guid {
            groups.entry(guid).or_default().push(plugin);
        }
    }
    if !groups.values().any(|group| group.len() > 1) {
        return Ok(());
    }

    let library = project.library_dir();
    fs::create_dir_all(&library)?;
    let lock = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(false)
        .open(library.join("plugin-guid-import.lock"))?;
    lock.lock().context("locking plugin metadata import")?;

    let editable = project.editable_roots();
    let editable_on_disk: Vec<_> = editable
        .iter()
        .filter_map(|dir| dir.canonicalize().ok())
        .collect();
    let mut reserved: HashSet<_> = groups.keys().copied().collect();
    for (guid, group) in groups.into_iter().filter(|(_, group)| group.len() > 1) {
        let mut entries = Vec::new();
        for plugin in group {
            let meta = meta_path(&plugin.file);
            let Some(current) = read_meta_guid(&meta) else {
                anyhow::bail!("{}: could not reread the plugin GUID", meta.display());
            };
            if current != guid {
                // Another import can finish while this one waits for the lock.
                reserved.insert(current);
                continue;
            }
            let canonical = meta.canonicalize()?;
            let writable = editable.iter().any(|dir| meta.starts_with(dir))
                && editable_on_disk
                    .iter()
                    .any(|dir| canonical.starts_with(dir));
            entries.push((plugin, canonical, writable));
        }
        repair_group(project, guid, &entries, &mut reserved)?;
    }
    Ok(())
}

fn repair_group(
    project: &Project,
    guid: Guid,
    entries: &[(&Plugin, PathBuf, bool)],
    reserved: &mut HashSet<Guid>,
) -> Result<()> {
    if entries.len() < 2 {
        return Ok(());
    }
    let mut paths = HashSet::new();
    let immutable = entries.iter().filter(|(_, _, writable)| !writable).count();
    if immutable > 1
        || entries.iter().any(|(_, meta, _)| !paths.insert(meta))
        || (immutable == 0 && project.root.join("Library/SourceAssetDB").exists())
    {
        let names: Vec<_> = entries.iter().map(|(p, _, _)| p.path.as_str()).collect();
        tracing::warn!(
            "Unbound has no measured plugin GUID repair for [{guid}] shared by {}",
            names.join(", ")
        );
        return Ok(());
    }

    // On a fresh import, the Unity 6000.0.71f1 Editor (tests/projects/pluginties)
    // keeps an immutable package's GUID. When neither DLL has an established
    // owner, it reassigns each writable copy. A new GUID takes part in the
    // regular managed DLL selection on the subsequent compile.
    for (plugin, meta, writable) in entries {
        if !writable {
            continue;
        }
        let current_owner = immutable == 1 && plugin.path.starts_with("Packages/");
        let suffix = if current_owner { "" } else { ".meta" };
        let others = entries
            .iter()
            .filter(|(other, _, writable)| {
                other.path != plugin.path && (immutable == 0 || !writable)
            })
            .map(|(other, _, _)| {
                let owner = if current_owner {
                    " (current owner)"
                } else {
                    ""
                };
                format!("  '{}{suffix}'{owner}\n", other.path)
            })
            .collect::<String>();
        let fresh = std::iter::repeat_with(new_guid)
            .find(|&candidate| reserved.insert(candidate))
            .expect("there are unused GUIDs");
        rewrite_meta(meta, guid, fresh)
            .with_context(|| format!("reassigning GUID of {}", plugin.path))?;
        tracing::warn!(
            "GUID [{guid}] for asset '{}{suffix}' conflicts with:\n{others}Assigning a new guid.",
            plugin.path
        );
    }
    Ok(())
}

fn rewrite_meta(path: &Path, guid: Guid, fresh: Guid) -> Result<()> {
    let text = fs::read_to_string(path)?;
    let replacement = replace_guid(&text, guid, fresh)?;
    let permissions = fs::metadata(path)?.permissions();
    let temporary = path.with_file_name(format!(".{fresh}.tmp"));
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)?;
    let commit = (|| -> Result<()> {
        output.set_permissions(permissions)?;
        output.write_all(replacement.as_bytes())?;
        drop(output);
        anyhow::ensure!(fs::read_to_string(path)? == text, "the .meta file changed");
        fs::rename(&temporary, path)?;
        Ok(())
    })();
    if commit.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    commit
}
