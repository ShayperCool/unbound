//! GUID conflicts discovered when opening a project's precompiled plugins.
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::{
    collections::{BTreeMap, HashSet},
    fs::{self, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
};
use unbound_assets::new_meta::{is_folder_plugin, meta_path, new_guid, replace_guid};
use unbound_project::{
    PackageSource, Project,
    assetdb::{meta_header, read_meta_guid},
    plugins::Plugin,
};
use unbound_yaml::Guid;

/// Contains the outstanding writes of a group, including writes that may already have completed.
/// Keeping the plan until the last rename lets the next import resume after a permissions error
/// or interruption, even when the first rename eliminated the duplicate.
const PENDING: &str = "plugin-guid-import-v2.pending.json";

#[derive(Deserialize, Serialize)]
struct Rewrite {
    asset: String,
    old: Guid,
    new: Guid,
    diagnostic: String,
}

struct WritableRoot {
    path: String,
    dir: PathBuf,
}

/// Import DLL metadata before indexing assets or building the compilation graph. Read-only
/// graph inspection never writes project files.
pub fn repair_plugin_guid_conflicts(project: &Project) -> Result<()> {
    if project.version.measured_family() != Some(unbound_compat::MeasuredFamily::Unity6000_0) {
        return Ok(());
    }
    let library = project.library_dir();
    let pending = library.join(PENDING);
    let plugins = project.plugins();
    if !pending.exists()
        && plugin_groups(&plugins)
            .values()
            .all(|group| group.len() == 1)
    {
        return Ok(());
    }

    fs::create_dir_all(&library)?;
    let lock = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(false)
        .open(library.join("plugin-guid-import.lock"))?;
    lock.lock().context("locking plugin metadata import")?;
    let previous_editor_database = project.root.join("Library/SourceAssetDB").exists();
    let editable: Vec<_> = project
        .asset_root_dirs()
        .into_iter()
        .filter(|(dir, _)| {
            *dir == project.assets
                || project
                    .packages
                    .iter()
                    .any(|p| p.root == *dir && p.source == PackageSource::Embedded)
        })
        .filter_map(|(dir, path)| {
            Some(WritableRoot {
                path,
                dir: dir.canonicalize().ok()?,
            })
        })
        .collect();
    match fs::read(&pending) {
        Ok(bytes) => {
            anyhow::ensure!(
                !previous_editor_database,
                "cannot resume plugin GUID repair after Unity opened the project"
            );
            let rewrites: Vec<Rewrite> = serde_json::from_slice(&bytes)
                .with_context(|| format!("reading interrupted import {}", pending.display()))?;
            resume(&pending, &editable, &rewrites)?;
        }
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.into()),
    }

    // Read again inside the lock. A different process may have completed an import after the
    // first scan, and all GUIDs read here are reserved before we allocate any replacements.
    let plugins = project.plugins();
    let groups = plugin_groups(&plugins);
    let mut reserved = groups.keys().copied().collect();
    let mut rewrites = Vec::new();
    for (guid, group) in groups.into_iter().filter(|(_, group)| group.len() > 1) {
        plan_group(
            project,
            guid,
            &group,
            &editable,
            &mut reserved,
            &mut rewrites,
        )?;
    }
    if !rewrites.is_empty() {
        replace_file(&pending, &serde_json::to_vec(&rewrites)?, None)?;
        resume(&pending, &editable, &rewrites)?;
    }
    Ok(())
}

fn plugin_groups(plugins: &[Plugin]) -> BTreeMap<Guid, Vec<&Plugin>> {
    let mut groups = BTreeMap::<_, Vec<_>>::new();
    for plugin in plugins {
        let mut segments = plugin.path.split('/');
        if segments.next() == Some("Packages") {
            segments.next(); // Package name, not an asset folder.
        }
        if let Some(guid) = plugin.guid
            && !segments.any(|segment| is_folder_plugin(Path::new(segment)))
        {
            groups.entry(guid).or_default().push(plugin);
        }
    }
    groups
}

struct Entry<'a> {
    plugin: &'a Plugin,
    meta: PathBuf,
    source: Option<PackageSource>,
}

impl Entry<'_> {
    fn writable(&self) -> bool {
        matches!(self.source, None | Some(PackageSource::Embedded))
    }
}

fn plan_group(
    project: &Project,
    guid: Guid,
    group: &[&Plugin],
    editable: &[WritableRoot],
    reserved: &mut HashSet<Guid>,
    rewrites: &mut Vec<Rewrite>,
) -> Result<()> {
    let mut entries = Vec::new();
    let mut paths = HashSet::new();
    for plugin in group {
        let meta = meta_path(&plugin.file).canonicalize()?;
        let source = plugin
            .path
            .strip_prefix("Packages/")
            .map(|path| {
                let name = path.split('/').next().unwrap_or_default();
                project
                    .packages
                    .iter()
                    .find(|candidate| candidate.name == name)
                    .map(|package| package.source)
                    .with_context(|| format!("no resolved package for {}", plugin.path))
            })
            .transpose()?;
        anyhow::ensure!(
            paths.insert(meta.clone()),
            "plugin GUID [{guid}] aliases one metadata file at {}",
            meta.display()
        );
        let entry = Entry {
            plugin,
            meta,
            source,
        };
        if entry.writable() {
            anyhow::ensure!(
                editable
                    .iter()
                    .any(|root| entry.meta.starts_with(&root.dir)),
                "{} resolves outside the project's writable asset roots",
                plugin.path
            );
        }
        entries.push(entry);
    }
    entries.sort_by_key(|entry| !entry.writable());
    let mut immutable = entries.iter().filter(|entry| !entry.writable());
    let owner = immutable.next();
    let previous_editor_database = project.root.join("Library/SourceAssetDB").exists();
    anyhow::ensure!(
        immutable.next().is_none()
            && owner.is_none_or(|entry| matches!(
                entry.source,
                Some(PackageSource::Registry | PackageSource::LocalTarball)
            ))
            && !previous_editor_database,
        "Unbound cannot resolve plugin GUID [{guid}] shared by {}. Open the project in Unity to resolve the GUID conflict before compiling.",
        entries
            .iter()
            .map(|entry| entry.plugin.path.as_str())
            .collect::<Vec<_>>()
            .join(", ")
    );
    if let Some(entry) = entries
        .iter()
        .find(|entry| read_meta_guid(&entry.meta) != Some(guid))
    {
        // The Editor reserializes metadata without a plain GUID token. Preserve the
        // existing compilation for these files until that format can be written.
        tracing::warn!(
            "{}: plugin GUID [{guid}] requires an unmeasured metadata rewrite",
            entry.plugin.path
        );
        return Ok(());
    }

    // Unity 6000.0.71f1 imports Assets and registry packages in the initial batch. In the
    // embedded-package scan, a local tarball has already claimed its GUID. Both branches are
    // recorded under tests/projects/pluginties. In a fresh group with no immutable owner,
    // every writable participant is reassigned.
    for entry in entries.iter().filter(|entry| entry.writable()) {
        let known_owner = owner.is_some_and(|other| {
            other.source == Some(PackageSource::LocalTarball)
                && entry.source == Some(PackageSource::Embedded)
        });
        let suffix = if known_owner { "" } else { ".meta" };
        let owner_label = if known_owner { " (current owner)" } else { "" };
        let others = entries
            .iter()
            .filter(|other| {
                if known_owner {
                    owner.is_some_and(|owner| other.meta == owner.meta)
                } else {
                    other.meta != entry.meta
                }
            })
            .map(|other| format!("  '{}{suffix}'{owner_label}\n", other.plugin.path))
            .collect::<String>();
        let new = std::iter::repeat_with(new_guid)
            .find(|&candidate| reserved.insert(candidate))
            .expect("there are unused GUIDs");
        let diagnostic = format!(
            "GUID [{guid}] for asset '{}{suffix}' conflicts with:\n{others}Assigning a new guid.",
            entry.plugin.path
        );
        rewrites.push(Rewrite {
            asset: entry.plugin.path.clone(),
            old: guid,
            new,
            diagnostic,
        });
    }
    Ok(())
}

fn resume(pending: &Path, editable: &[WritableRoot], rewrites: &[Rewrite]) -> Result<()> {
    for rewrite in rewrites {
        let meta = editable
            .iter()
            .find_map(|root| {
                let relative =
                    Path::new(rewrite.asset.strip_prefix(&root.path)?.strip_prefix('/')?);
                relative
                    .components()
                    .all(|part| matches!(part, std::path::Component::Normal(_)))
                    .then(|| meta_path(&root.dir.join(relative)))
            })
            .with_context(|| format!("{} is no longer a writable project asset", rewrite.asset))?;
        let path = match meta.canonicalize() {
            Ok(path) => path,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(error) => return Err(error.into()),
        };
        anyhow::ensure!(
            editable.iter().any(|root| path.starts_with(&root.dir)),
            "{} moved outside the writable asset roots during plugin import",
            rewrite.asset
        );
        let text = fs::read_to_string(&path)?;
        let metadata = unbound_yaml::parse_value(&text)?;
        if meta_header(&metadata).0 != Some(rewrite.old) {
            continue; // This write already completed or the GUID was deliberately changed.
        }
        let replacement = replace_guid(&text, rewrite.old, rewrite.new)
            .with_context(|| format!("reassigning GUID in {}", path.display()))?;
        replace_file(&path, replacement.as_bytes(), Some(&text))?;
        tracing::warn!("{}", rewrite.diagnostic);
    }
    fs::remove_file(pending)?;
    Ok(())
}

fn replace_file(path: &Path, replacement: &[u8], original: Option<&str>) -> Result<()> {
    let temporary = path.with_file_name(format!(".{}.tmp", new_guid()));
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)
        .with_context(|| format!("preparing a write to {}", path.display()))?;
    let commit = (|| -> Result<()> {
        output.write_all(replacement)?;
        if let Some(original) = original {
            let permissions = fs::metadata(path)?.permissions();
            anyhow::ensure!(!permissions.readonly(), "the .meta file is read-only");
            output.set_permissions(permissions)?;
            anyhow::ensure!(
                fs::read_to_string(path)? == original,
                "the .meta file changed"
            );
        }
        output.sync_all()?;
        drop(output);
        fs::rename(&temporary, path)?;
        Ok(())
    })();
    if commit.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    commit.with_context(|| format!("writing {}", path.display()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn interrupted_group_retries_the_remaining_metadata_write() -> Result<()> {
        let dir = std::env::temp_dir().join(format!("unbound-plugin-repair-{}", new_guid()));
        fs::create_dir(&dir)?;
        let dir = dir.canonicalize()?;
        let first = dir.join("first.dll.meta");
        let second = dir.join("second.dll.meta");
        let pending = dir.join(PENDING);
        let guid = Guid(1);
        let original = format!("fileFormatVersion: 2\r\nguid: {guid}\r\n");
        fs::write(&first, &original)?;
        fs::write(&second, &original)?;
        let permissions = fs::metadata(&second)?.permissions();
        let mut readonly = permissions.clone();
        readonly.set_readonly(true);
        fs::set_permissions(&second, readonly)?;
        let rewrites = [
            ("Assets/first.dll", Guid(2)),
            ("Assets/second.dll", Guid(3)),
        ]
        .map(|(asset, new)| Rewrite {
            asset: asset.into(),
            old: guid,
            new,
            diagnostic: String::new(),
        });
        fs::write(&pending, serde_json::to_vec(&rewrites)?)?;
        let editable = [WritableRoot {
            path: "Assets".into(),
            dir: dir.clone(),
        }];
        assert!(resume(&pending, &editable, &rewrites).is_err());
        assert_eq!(read_meta_guid(&first), Some(Guid(2)));
        assert_eq!(fs::read_to_string(&second)?, original);
        let moved = dir.with_extension("moved");
        fs::rename(&dir, &moved)?;
        let pending = moved.join(PENDING);
        let first = moved.join("first.dll.meta");
        let second = moved.join("second.dll.meta");
        let editable = [WritableRoot {
            path: "Assets".into(),
            dir: moved.clone(),
        }];
        fs::set_permissions(&second, permissions)?;
        let rewrites: Vec<Rewrite> = serde_json::from_slice(&fs::read(&pending)?)?;
        resume(&pending, &editable, &rewrites)?;
        assert_eq!(read_meta_guid(&first), Some(Guid(2)));
        assert_eq!(read_meta_guid(&second), Some(Guid(3)));
        assert!(!pending.exists());
        fs::remove_dir_all(moved)?;
        Ok(())
    }

    #[test]
    fn only_dll_metadata_in_the_asset_namespace_participates() {
        let text = format!("guid: {}\n", Guid(1));
        let meta = unbound_yaml::parse_value(&text).unwrap();
        let plugins = [
            "Assets/Ordinary.dll",
            "Assets/Device.androidlib/lib/Helper.dll",
            "Packages/com.example.tools.plugin/Framework.framework/Helper.dll",
            "Packages/com.example.tools.plugin/Ordinary.dll",
        ]
        .map(|path| {
            unbound_project::plugins::plugin_from_meta(Path::new(path), path.into(), &meta)
        });
        let groups = plugin_groups(&plugins);
        let group = &groups[&Guid(1)];
        assert_eq!(group.len(), 2);
        assert_eq!(group[0].path, "Assets/Ordinary.dll");
        assert_eq!(
            group[1].path,
            "Packages/com.example.tools.plugin/Ordinary.dll"
        );
    }
}
