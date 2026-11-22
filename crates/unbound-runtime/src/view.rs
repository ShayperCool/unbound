//! The directory the game runs in. Unity's Mono maps `Packages/<name>/...` to each package's
//! files for plain file IO, so Editor code reads package files by those paths; .NET has no such
//! hook. The game runs instead in `LibraryUnbound/ProjectView`, a directory of symbolic links: every
//! top-level entry of the project but Unbound's own directory and Unity's `Library/`,
//! `Packages/<name>` for every resolved package, and a `Library/` of Unbound's own.
use anyhow::{Context, Result};
use serde_json::{Value as Json, json};
use std::path::{Path, PathBuf};
use unbound_project::{Package, PackageSource, Project};

/// The project view's directory: `<project>/LibraryUnbound/ProjectView`.
pub fn dir(project: &Project) -> PathBuf {
    project.unbound_dir().join("ProjectView")
}

/// The `Library/` the game sees (Editor code keeps its own files there, as in the Editor's
/// `Library/`): `<project>/LibraryUnbound/Library`, which outlives the view's rebuilds. Unity's
/// own `Library/` is no part of the view: Unbound neither reads nor writes it.
pub fn library(project: &Project) -> PathBuf {
    project.unbound_dir().join("Library")
}

/// A project or package file as the game sees it through the view (its working directory):
/// `Path.GetFullPath("Assets/...")` and the paths Unbound reports for the same file agree.
/// A file's path as Unity names it: `Assets/...` or `Packages/<name>/...`.
pub fn unity_path(project: &Project, path: &Path) -> String {
    let view = to_view(project, path);
    view.strip_prefix(dir(project))
        .unwrap_or(&view)
        .to_string_lossy()
        .replace('\\', "/")
}

pub fn to_view(project: &Project, path: &Path) -> PathBuf {
    let view = dir(project);
    // Unbound's own files (compiled assemblies, caches) are no part of the view, wherever the
    // directory is: they keep their paths.
    if path.starts_with(project.unbound_dir()) {
        return path.to_path_buf();
    }
    // Packages first: an embedded package lives under the project's Packages/ folder too.
    let mut best: Option<(&Package, usize)> = None;
    for package in &project.packages {
        if path.starts_with(&package.root) {
            let depth = package.root.components().count();
            if best.is_none_or(|(_, d)| depth > d) {
                best = Some((package, depth));
            }
        }
    }
    if let Some((package, _)) = best
        && let Ok(rest) = path.strip_prefix(&package.root)
    {
        return view.join("Packages").join(&package.name).join(rest);
    }
    // Unity's Library/ is no part of the view (the view's Library/ is Unbound's own).
    if let Ok(rest) = path.strip_prefix(&project.root)
        && !rest.starts_with("Packages")
        && !rest.starts_with("Library")
    {
        return view.join(rest);
    }
    path.to_path_buf()
}

/// Builds (or rebuilds) the project view and returns its directory.
pub fn build(project: &Project) -> Result<PathBuf> {
    let view = dir(project);
    if view.exists() {
        // Only links live here: removing them leaves their targets alone.
        std::fs::remove_dir_all(&view).with_context(|| format!("clearing {}", view.display()))?;
    }
    let packages = view.join("Packages");
    std::fs::create_dir_all(&packages)?;
    let data = project.unbound_dir();
    for entry in std::fs::read_dir(&project.root)?.flatten() {
        let name = entry.file_name();
        // Unbound's own directory is no part of the project the game sees, nor Unity's Library/.
        if name == "Packages" || name == "Library" || entry.path() == data {
            continue;
        }
        link(&entry.path(), &view.join(&name))?;
    }
    // The Editor always has a Library/ (it makes one when it opens a project); Editor code writes
    // its own files there.
    let library = library(project);
    std::fs::create_dir_all(&library).with_context(|| format!("creating {}", library.display()))?;
    link(&library, &view.join("Library"))?;
    // The manifest, the lock and anything else at the top of Packages/.
    if let Ok(entries) = std::fs::read_dir(project.root.join("Packages")) {
        for entry in entries.flatten() {
            if entry.path().is_file() {
                link(&entry.path(), &packages.join(entry.file_name()))?;
            }
        }
    }
    for package in &project.packages {
        let target = packages.join(&package.name);
        if !target.exists() {
            link(&package.root, &target)?;
        }
    }
    Ok(view)
}

fn link(target: &Path, at: &Path) -> Result<()> {
    std::os::unix::fs::symlink(target, at)
        .with_context(|| format!("linking {} to {}", at.display(), target.display()))
}

/// The packages as the Package Manager reports them (`PackageInfo`).
pub fn package_infos(project: &Project) -> Vec<Json> {
    let direct = direct_dependencies(&project.root);
    project
        .packages
        .iter()
        // Embedded packages are the project's own: direct dependencies whether or not the
        // manifest lists them (tests/projects/packageresolve).
        .map(|p| {
            package_info(
                p,
                direct.contains(&p.name) || p.source == PackageSource::Embedded,
            )
        })
        .collect()
}

fn package_info(package: &Package, direct: bool) -> Json {
    let manifest: Json = std::fs::read_to_string(package.root.join("package.json"))
        .ok()
        .and_then(|t| serde_json::from_str(&t).ok())
        .unwrap_or(Json::Null);
    let text = |key: &str| {
        manifest
            .get(key)
            .and_then(Json::as_str)
            .unwrap_or("")
            .to_string()
    };
    let keywords: Vec<String> = manifest
        .get("keywords")
        .and_then(Json::as_array)
        .map(|k| {
            k.iter()
                .filter_map(|v| v.as_str().map(str::to_string))
                .collect()
        })
        .unwrap_or_default();
    // UnityEditor.PackageManager.PackageSource values.
    let source = match package.source {
        PackageSource::Registry => 1,
        PackageSource::BuiltIn => 2,
        PackageSource::Embedded => 3,
        PackageSource::Local => 4,
        PackageSource::Git => 5,
        PackageSource::LocalTarball => 6,
    };
    json!({
        "name": package.name,
        "version": package.version,
        "source": source,
        "resolvedPath": package.root,
        "displayName": text("displayName"),
        "description": text("description"),
        "category": text("category"),
        "type": text("type"),
        "keywords": keywords,
        "isDirectDependency": direct,
    })
}

fn direct_dependencies(root: &Path) -> Vec<String> {
    std::fs::read_to_string(root.join("Packages/manifest.json"))
        .ok()
        .and_then(|t| serde_json::from_str::<Json>(&t).ok())
        .and_then(|m| m.get("dependencies").and_then(Json::as_object).cloned())
        .map(|d| d.keys().cloned().collect())
        .unwrap_or_default()
}
