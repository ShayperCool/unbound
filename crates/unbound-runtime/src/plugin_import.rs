//! Resolve authored GUID conflicts for the managed DLL compilation graph.
use anyhow::{Context, Result};
use std::{
    collections::{BTreeMap, HashMap, HashSet},
    fs::{self, File, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
};
use unbound_assets::new_meta::{is_folder_plugin, meta_path, new_guid, replace_guid};
use unbound_project::{PackageSource, Project, plugins::Plugin};
use unbound_yaml::Guid;

use crate::editor_asset_guids::EditorAssetGuids;

struct PluginAssets {
    plugins: Vec<Plugin>,
    guids: BTreeMap<Guid, Vec<String>>,
}

impl PluginAssets {
    fn scan(project: &Project) -> Self {
        let (plugins, guids) = project.plugin_assets(is_folder_plugin);
        Self { plugins, guids }
    }

    fn has_conflicts(&self) -> bool {
        self.plugins.iter().any(|plugin| {
            plugin
                .guid
                .and_then(|guid| self.guids.get(&guid))
                .is_some_and(|paths| paths.len() > 1 && paths.contains(&plugin.path))
        })
    }
}

/// Plugin metadata is imported before compiling or starting a project.
pub fn prepare_plugins(project: &Project) -> Vec<Plugin> {
    select_plugins(project, true)
}

/// Inspection and hot reload can use registered GUIDs without authoring metadata.
pub fn inspect_plugins(project: &Project) -> Vec<Plugin> {
    select_plugins(project, false)
}

fn select_plugins(project: &Project, import: bool) -> Vec<Plugin> {
    if project.version.measured_family() != Some(unbound_compat::MeasuredFamily::Unity6000_0) {
        return project.plugins();
    }
    let database = project.root.join("Library/SourceAssetDB");
    let established = has_editor_database(&database);
    if !import && !established {
        return project.plugins();
    }
    let found = PluginAssets::scan(project);
    if !found.has_conflicts() {
        return found.plugins;
    }
    let library = project.library_dir();
    if established {
        let registered = EditorAssetGuids::open(&database, &library);
        return resolve_groups(project, found, |guid, entries, reserved| {
            registered
                .as_ref()
                .map_err(|error| anyhow::anyhow!("{error:#}"))?
                .resolve(guid, entries, reserved)
        });
    }
    // Serialize only the exceptional import, with a fresh scan under the lock. No import
    // state is carried from one scan to the next.
    let _lock = match import_lock(&library) {
        Ok(lock) => lock,
        Err(error) => {
            tracing::warn!("Skipping plugin GUID repair: {error:#}");
            return found.plugins;
        }
    };
    let found = PluginAssets::scan(project);
    resolve_groups(project, found, |guid, entries, reserved| {
        anyhow::ensure!(
            !has_editor_database(&database),
            "the project has an established Unity asset database"
        );
        let plan = plan_group(guid, entries, reserved, &library)?;
        let mut applied = Vec::new();
        for replacement in plan {
            match replacement.apply() {
                Ok(change) => applied.push(change),
                Err(error) => {
                    warn_group(guid, entries.iter().map(|entry| entry.path), &error);
                    break;
                }
            }
        }
        Ok(applied)
    })
}

fn has_editor_database(database: &Path) -> bool {
    !matches!(fs::symlink_metadata(database), Err(error) if error.kind() == std::io::ErrorKind::NotFound)
}

fn import_lock(library: &Path) -> Result<File> {
    fs::create_dir_all(library)?;
    let file = OpenOptions::new()
        .write(true)
        .create(true)
        .truncate(false)
        .open(library.join("plugin-guid-import.lock"))?;
    file.lock().context("locking plugin metadata import")?;
    Ok(file)
}

fn resolve_groups(
    project: &Project,
    mut found: PluginAssets,
    mut resolve: impl FnMut(Guid, &[Entry<'_>], &mut HashSet<Guid>) -> Result<Vec<(usize, Guid)>>,
) -> Vec<Plugin> {
    let by_path: HashMap<_, _> = found
        .plugins
        .iter()
        .enumerate()
        .map(|(index, plugin)| (plugin.path.as_str(), (index, plugin)))
        .collect();
    let mut reserved: HashSet<_> = found.guids.keys().copied().collect();
    reserved.insert(unbound_project::assetdb::ASSETS_ROOT_GUID);
    let mut changes = Vec::new();
    for (&guid, paths) in &found.guids {
        let mut members = paths.iter().filter_map(|path| {
            let &(index, plugin) = by_path.get(path.as_str())?;
            (plugin.guid == Some(guid)).then_some((index, plugin))
        });
        let Some(first) = members.next() else {
            continue;
        };
        if paths.len() < 2 {
            continue;
        }
        let group: Vec<_> = std::iter::once(first).chain(members).collect();
        let result = if group.len() != paths.len() {
            Err(anyhow::anyhow!("the group includes other asset types"))
        } else {
            entries_for(project, &group).and_then(|entries| resolve(guid, &entries, &mut reserved))
        };
        match result {
            Ok(resolved) => changes.extend(resolved),
            Err(error) => warn_group(guid, paths.iter().map(String::as_str), &error),
        }
    }
    for (index, guid) in changes {
        found.plugins[index].guid = Some(guid);
    }
    found.plugins
}

fn warn_group<'a>(guid: Guid, paths: impl Iterator<Item = &'a str>, error: &anyhow::Error) {
    let mut paths: Vec<_> = paths.collect();
    paths.sort_unstable();
    tracing::warn!(
        "Plugin GUID [{guid}] shared by {}: {error:#}",
        paths.join(", ")
    );
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub(super) enum Source {
    Assets,
    Package(PackageSource),
}

impl Source {
    fn writable(self) -> bool {
        matches!(self, Self::Assets | Self::Package(PackageSource::Embedded))
    }
}

pub(super) struct Entry<'a> {
    pub index: usize,
    pub path: &'a str,
    pub source: Source,
    root: &'a Path,
    relative: &'a Path,
}

fn entries_for<'a>(project: &'a Project, group: &[(usize, &'a Plugin)]) -> Result<Vec<Entry<'a>>> {
    group
        .iter()
        .map(|&(index, plugin)| {
            let (source, root, relative) = if let Some(asset) = plugin.path.strip_prefix("Assets/")
            {
                (Source::Assets, &project.assets, asset)
            } else {
                let (name, asset) = plugin
                    .path
                    .strip_prefix("Packages/")
                    .and_then(|path| path.split_once('/'))
                    .context("invalid plugin asset path")?;
                let package = project
                    .packages
                    .iter()
                    .find(|package| package.name == name)
                    .context("the package is no longer resolved")?;
                (Source::Package(package.source), &package.root, asset)
            };
            Ok(Entry {
                index,
                path: &plugin.path,
                source,
                root,
                relative: Path::new(relative),
            })
        })
        .collect()
}

impl Entry<'_> {
    pub(super) fn metadata(&self) -> Result<PathBuf> {
        anyhow::ensure!(
            self.relative
                .components()
                .all(|part| matches!(part, std::path::Component::Normal(_))),
            "invalid plugin path {}",
            self.path
        );
        // Resolve the declared root, including a symlinked Assets directory. An asset or
        // metadata link within the root can refer to another asset's source and is unmeasured.
        let root = self.root.canonicalize()?;
        let asset = root.join(self.relative);
        let meta = meta_path(&asset);
        anyhow::ensure!(
            asset.canonicalize()? == asset && meta.canonicalize()? == meta,
            "a plugin or metadata path aliases another file"
        );
        Ok(meta)
    }
}

struct PendingMetadata {
    index: usize,
    path: PathBuf,
    original: String,
    temp: StagedFile,
    guid: Guid,
    diagnostic: String,
}

impl PendingMetadata {
    fn apply(self) -> Result<(usize, Guid)> {
        anyhow::ensure!(
            fs::read_to_string(&self.path)? == self.original,
            "{} changed while preparing its metadata",
            self.path.display()
        );
        fs::rename(&self.temp.0, &self.path)
            .with_context(|| format!("writing {}", self.path.display()))?;
        tracing::warn!("{}", self.diagnostic);
        Ok((self.index, self.guid))
    }
}

pub(super) struct StagedFile(pub PathBuf);

impl StagedFile {
    pub fn create(library: &Path) -> Result<(Self, File)> {
        let path = library.join(format!("plugin-import-{}.tmp", new_guid()));
        let file = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&path)?;
        Ok((Self(path), file))
    }
}

impl Drop for StagedFile {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.0);
    }
}

fn plan_group(
    guid: Guid,
    entries: &[Entry<'_>],
    reserved: &mut HashSet<Guid>,
    library: &Path,
) -> Result<Vec<PendingMetadata>> {
    let mut immutable = entries.iter().filter(|entry| !entry.source.writable());
    let owner = immutable.next();
    anyhow::ensure!(
        immutable.next().is_none()
            && owner.is_none_or(|entry| matches!(
                entry.source,
                Source::Package(PackageSource::Registry | PackageSource::LocalTarball)
            )),
        "this package-source combination is unmeasured"
    );
    let mut seen = HashSet::new();
    let mut plan = Vec::new();
    for entry in entries {
        let path = entry
            .metadata()
            .with_context(|| format!("reading {}", entry.path))?;
        anyhow::ensure!(seen.insert(path.clone()), "two paths share a .meta file");
        let original = fs::read_to_string(&path)?;
        let new = std::iter::repeat_with(new_guid)
            .find(|&candidate| reserved.insert(candidate))
            .expect("there are unused GUIDs");
        // Validate the plain GUID token for every member before publishing any change.
        let text = replace_guid(&original, guid, new)
            .with_context(|| format!("{} needs an unmeasured metadata rewrite", entry.path))?;
        if !entry.source.writable() {
            continue;
        }
        let metadata = fs::metadata(&path)?;
        let parent = fs::metadata(path.parent().context("metadata has no parent")?)?;
        anyhow::ensure!(
            !metadata.permissions().readonly() && !parent.permissions().readonly(),
            "{} is read-only",
            path.display()
        );
        #[cfg(unix)]
        {
            use std::os::unix::fs::MetadataExt as _;
            anyhow::ensure!(
                metadata.dev() == fs::metadata(library)?.dev(),
                "{} is not on the project cache filesystem",
                path.display()
            );
        }
        let (temp, mut output) = StagedFile::create(library)?;
        output.write_all(text.as_bytes())?;
        output.set_permissions(metadata.permissions())?;
        output.sync_all()?;
        plan.push(PendingMetadata {
            index: entry.index,
            path,
            original,
            temp,
            guid: new,
            diagnostic: conflict_message(guid, entry, entries, owner),
        });
    }
    Ok(plan)
}

fn conflict_message(
    guid: Guid,
    entry: &Entry<'_>,
    entries: &[Entry<'_>],
    owner: Option<&Entry<'_>>,
) -> String {
    // The shorter form names the prior owner when it was already imported. The fresh
    // local-tarball/embedded cases in pluginties are the measured instances of that history.
    let registered = owner.filter(|owner| {
        owner.source == Source::Package(PackageSource::LocalTarball)
            && entry.source == Source::Package(PackageSource::Embedded)
    });
    let (suffix, owner_label) = if registered.is_some() {
        ("", " (current owner)")
    } else {
        (".meta", "")
    };
    let mut peers: Vec<_> = entries
        .iter()
        .filter(|peer| {
            registered.map_or(peer.index != entry.index, |owner| peer.index == owner.index)
        })
        .map(|peer| peer.path)
        .collect();
    peers.sort_unstable();
    let others = peers
        .into_iter()
        .map(|peer| format!("  '{peer}{suffix}'{owner_label}\n"))
        .collect::<String>();
    format!(
        "GUID [{guid}] for asset '{}{suffix}' conflicts with:\n{others}Assigning a new guid.",
        entry.path
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Files {
        root: PathBuf,
        library: PathBuf,
    }

    impl Files {
        fn new() -> Result<Self> {
            let root = std::env::temp_dir().join(format!("unbound-plugin-import-{}", new_guid()));
            let library = root.join("Library/Unbound");
            fs::create_dir_all(&library)?;
            Ok(Self { root, library })
        }

        fn plugin<'a>(&'a self, index: usize, source: Source, path: &'a str) -> Result<Entry<'a>> {
            let relative = Path::new(path);
            let file = self.root.join(relative);
            fs::create_dir_all(file.parent().context("test plugin has no parent")?)?;
            fs::write(&file, "plugin")?;
            fs::write(meta_path(&file), authored())?;
            Ok(Entry {
                index,
                source,
                path,
                root: &self.root,
                relative,
            })
        }
    }

    impl Drop for Files {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.root);
        }
    }

    fn authored() -> String {
        format!(
            "fileFormatVersion: 2\r\nguid: {}\r\nPluginImporter:\r\n",
            Guid(1)
        )
    }

    #[test]
    fn fresh_registry_owner_keeps_its_guid_and_writable_copies_change_token_only() -> Result<()> {
        let files = Files::new()?;
        let entries = [
            files.plugin(0, Source::Assets, "Assets/A.dll")?,
            files.plugin(
                1,
                Source::Package(PackageSource::Registry),
                "Packages/package/B.dll",
            )?,
            files.plugin(
                2,
                Source::Package(PackageSource::Embedded),
                "Packages/embedded/C.dll",
            )?,
        ];
        let mut reserved = HashSet::from([Guid(1)]);
        let plan = plan_group(Guid(1), &entries, &mut reserved, &files.library)?;
        assert_eq!(
            plan.iter().map(|change| change.index).collect::<Vec<_>>(),
            [0, 2]
        );
        assert!(
            entries
                .iter()
                .all(|entry| fs::read_to_string(entry.metadata().unwrap()).unwrap() == authored())
        );
        for rewrite in plan {
            let (index, assigned) = rewrite.apply()?;
            let expected = authored().replace(&Guid(1).to_string(), &assigned.to_string());
            assert_eq!(fs::read_to_string(entries[index].metadata()?)?, expected);
        }
        assert_eq!(fs::read_to_string(entries[1].metadata()?)?, authored());
        assert!(fs::read_dir(&files.library)?.next().is_none());
        Ok(())
    }

    #[test]
    fn all_writable_members_are_reassigned_but_an_unmeasured_source_leaves_all_intact() -> Result<()>
    {
        let files = Files::new()?;
        let mut entries = [
            files.plugin(0, Source::Assets, "Assets/A.dll")?,
            files.plugin(1, Source::Assets, "Assets/B.dll")?,
        ];
        let mut reserved = HashSet::from([Guid(1)]);
        assert_eq!(
            plan_group(Guid(1), &entries, &mut reserved, &files.library)?.len(),
            2
        );
        for source in [
            PackageSource::Local,
            PackageSource::Git,
            PackageSource::BuiltIn,
        ] {
            entries[1].source = Source::Package(source);
            let error = plan_group(Guid(1), &entries, &mut reserved, &files.library)
                .err()
                .unwrap();
            assert!(error.to_string().contains("package-source combination"));
        }
        entries[0].source = Source::Package(PackageSource::Registry);
        entries[1].source = Source::Package(PackageSource::LocalTarball);
        assert!(plan_group(Guid(1), &entries, &mut reserved, &files.library).is_err());
        for entry in &entries {
            assert_eq!(fs::read_to_string(entry.metadata()?)?, authored());
        }
        assert!(fs::read_dir(&files.library)?.next().is_none());
        Ok(())
    }

    #[test]
    fn an_unwritable_or_quoted_member_prevents_a_group_write() -> Result<()> {
        let files = Files::new()?;
        let entries = [
            files.plugin(0, Source::Assets, "Assets/First.dll")?,
            files.plugin(1, Source::Assets, "Assets/Second.dll")?,
        ];
        let path = entries[1].metadata()?;
        let permissions = fs::metadata(&path)?.permissions();
        let mut readonly = permissions.clone();
        readonly.set_readonly(true);
        fs::set_permissions(&path, readonly)?;
        let mut reserved = HashSet::from([Guid(1)]);
        let result = plan_group(Guid(1), &entries, &mut reserved, &files.library);
        fs::set_permissions(&path, permissions)?;
        assert!(result.err().unwrap().to_string().contains("read-only"));

        let quoted = authored().replace(&Guid(1).to_string(), &format!("'{}'", Guid(1)));
        fs::write(&path, &quoted)?;
        let error = plan_group(Guid(1), &entries, &mut reserved, &files.library)
            .err()
            .unwrap();
        assert!(error.to_string().contains("unmeasured metadata rewrite"));
        assert_eq!(fs::read_to_string(entries[0].metadata()?)?, authored());
        assert_eq!(fs::read_to_string(&path)?, quoted);
        assert!(fs::read_dir(&files.library)?.next().is_none());
        Ok(())
    }

    #[test]
    fn established_database_that_cannot_be_read_has_a_fallback() -> Result<()> {
        let files = Files::new()?;
        let database = files.root.join("Library/SourceAssetDB");
        fs::write(&database, "")?;
        assert!(EditorAssetGuids::open(&database, &files.library).is_err());
        assert_eq!(fs::read(&database)?, b"");
        assert!(fs::read_dir(&files.library)?.next().is_none());
        Ok(())
    }
}
