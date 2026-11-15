//! Reading the workspace for the files an output is made by (see the crate docs). Shared with
//! the build script, which computes the identities from it.
use std::collections::{BTreeSet, VecDeque};
use std::io;
use std::path::{Path, PathBuf};

/// `path` with its `.` and `..` components resolved in the path itself.
pub(crate) fn lexical(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir if out.file_name().is_some() => {
                out.pop();
            }
            other => out.push(other),
        }
    }
    out
}

/// Every file below `dir`, sorted: hidden ones (a folder's metadata, an editor's swap file) left
/// out, linked directories not followed.
fn all_files(dir: &Path) -> io::Result<Vec<PathBuf>> {
    let mut out = Vec::new();
    let mut pending = vec![dir.to_path_buf()];
    while let Some(dir) = pending.pop() {
        for entry in std::fs::read_dir(&dir)? {
            let entry = entry?;
            if entry.file_name().to_string_lossy().starts_with('.') {
                continue;
            }
            let path = entry.path();
            if entry.file_type()?.is_dir() {
                pending.push(path);
            } else if path.is_file() {
                out.push(path);
            }
        }
    }
    out.sort();
    Ok(out)
}

/// Whether `word` stands in `text` on its own: not inside a longer name (letters, digits, `_`,
/// `-`).
fn names(text: &str, word: &str) -> bool {
    let part = |c: char| c.is_ascii_alphanumeric() || c == '_' || c == '-';
    text.match_indices(word)
        .any(|(at, _)| !text[..at].ends_with(part) && !text[at + word.len()..].starts_with(part))
}

/// The string literals of a Rust file (comments skipped; raw, byte and C strings included),
/// each as written between its quotes.
fn string_literals(text: &str) -> Vec<String> {
    let chars: Vec<char> = text.chars().collect();
    let mut out = Vec::new();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        let next = chars.get(i + 1).copied();
        if c == '/' && next == Some('/') {
            while i < chars.len() && chars[i] != '\n' {
                i += 1;
            }
        } else if c == '/' && next == Some('*') {
            let mut depth = 0;
            while i < chars.len() {
                if chars[i] == '/' && chars.get(i + 1) == Some(&'*') {
                    depth += 1;
                    i += 2;
                } else if chars[i] == '*' && chars.get(i + 1) == Some(&'/') {
                    depth -= 1;
                    i += 2;
                    if depth == 0 {
                        break;
                    }
                } else {
                    i += 1;
                }
            }
        } else if c == 'r'
            && matches!(next, Some('"' | '#'))
            && let Some(hashes) = raw_string_start(&chars[i + 1..])
        {
            // A raw string: `r"..."`, `r#"..."#` (also after `b` or `c`).
            i += 2 + hashes;
            let start = i;
            let closes = |at: usize| {
                chars[at] == '"' && (1..=hashes).all(|k| chars.get(at + k) == Some(&'#'))
            };
            while i < chars.len() && !closes(i) {
                i += 1;
            }
            out.push(chars[start..i.min(chars.len())].iter().collect());
            i += 1 + hashes;
        } else if c == '"' {
            i += 1;
            let mut literal = String::new();
            while i < chars.len() && chars[i] != '"' {
                if chars[i] == '\\' {
                    i += 1;
                }
                if i < chars.len() {
                    literal.push(chars[i]);
                }
                i += 1;
            }
            out.push(literal);
            i += 1;
        } else if c == '\'' {
            // A character literal (`'"'`, `'\''`) is skipped; a lifetime (`'a`) is not one.
            match next {
                Some('\\') => {
                    // The escaped character, then up to the closing quote (`'\''`, `'\u{..}'`).
                    let close = chars
                        .get(i + 3..)
                        .and_then(|r| r.iter().position(|&c| c == '\''));
                    i = close.map_or(i + 1, |p| i + 3 + p + 1);
                }
                Some(_) if chars.get(i + 2) == Some(&'\'') => i += 3,
                _ => i += 1,
            }
        } else {
            i += 1;
        }
    }
    out
}

/// How many `#` open a raw string at the start of `rest` (after its `r`), if one does.
fn raw_string_start(rest: &[char]) -> Option<usize> {
    let hashes = rest.iter().take_while(|&&c| c == '#').count();
    (rest.get(hashes) == Some(&'"')).then_some(hashes)
}

/// The entries of the workspace's manifest, each a line, or a `[...dependencies.<key>]` table
/// joined into one line keyed by `<key>` (so that the line naming a crate names its key too).
fn workspace_entries(workspace: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut table: Option<String> = None;
    for line in workspace.lines() {
        let trimmed = line.trim();
        if trimmed.starts_with('[') {
            if let Some(entry) = table.take() {
                out.push(entry);
            }
            let header = trimmed.trim_matches(['[', ']']).trim();
            if let Some((parent, key)) = header.rsplit_once('.')
                && parent.trim().ends_with("dependencies")
            {
                table = Some(format!("{} =", key.trim().trim_matches(['"', '\''])));
                continue;
            }
        }
        match &mut table {
            Some(entry) => {
                entry.push(' ');
                entry.push_str(trimmed);
            }
            None => out.push(line.to_string()),
        }
    }
    out.extend(table);
    out
}

/// The workspace crates (directories of `crates/` with a manifest), by directory name.
fn workspace_crates(root: &Path) -> io::Result<Vec<String>> {
    let mut out = Vec::new();
    for entry in std::fs::read_dir(root.join("crates"))? {
        let path = entry?.path();
        if path.join("Cargo.toml").is_file()
            && let Some(dir) = path.file_name()
        {
            out.push(dir.to_string_lossy().into_owned());
        }
    }
    out.sort();
    Ok(out)
}

/// The workspace crates a crate's manifest may depend on: each whose package name (its
/// directory's, with dashes or underscores) the manifest names anywhere, and each a key of the
/// workspace's manifest maps to (`foo = { package = "unbound-x" }` there, `foo.workspace = true`
/// here) when the crate's manifest names that key. Naming more than it depends on only hashes
/// more.
fn dependencies(manifest: &str, workspace: &str, crates: &[String]) -> Vec<String> {
    crates
        .iter()
        .filter(|dir| {
            let underscored = dir.replace('-', "_");
            names(manifest, dir)
                || names(manifest, &underscored)
                || workspace_entries(workspace).iter().any(|line| {
                    let key = line.split('=').next().unwrap_or("").trim();
                    let key = key
                        .split('.')
                        .next()
                        .unwrap_or("")
                        .trim_matches(['"', '\'']);
                    !key.is_empty()
                        && (names(line, dir) || names(line, &underscored))
                        && names(manifest, key)
                })
        })
        .cloned()
        .collect()
}

/// The files the output of the workspace crate in directory `crates/<start>` is made by: every
/// (non-hidden) file of it and of the workspace crates it depends on, found through their
/// manifests ([`dependencies`]), and every file outside those crates that a string of their Rust
/// code names (an `include_str!`, a `#[path]`, a manifest-relative path: relative to the naming
/// file's directory or to its crate's). Also what to watch for `cargo:rerun-if-changed`: those
/// crates' directories and the workspace's manifest.
pub fn producing_sources(
    root: &Path,
    start: &str,
) -> io::Result<(BTreeSet<PathBuf>, BTreeSet<PathBuf>)> {
    let root = &lexical(root);
    let crates = workspace_crates(root)?;
    let workspace = std::fs::read_to_string(root.join("Cargo.toml")).unwrap_or_default();
    let mut reached: BTreeSet<String> = BTreeSet::new();
    let mut pending = VecDeque::from([start.to_string()]);
    while let Some(name) = pending.pop_front() {
        if !reached.insert(name.clone()) {
            continue;
        }
        let manifest = std::fs::read_to_string(root.join("crates").join(&name).join("Cargo.toml"))?;
        pending.extend(
            dependencies(&manifest, &workspace, &crates)
                .into_iter()
                .filter(|d| *d != name),
        );
    }
    // The workspace's manifest too: its dependency features and patches decide what is built.
    let mut files = BTreeSet::from([root.join("Cargo.toml")]);
    let mut watched = BTreeSet::from([root.join("Cargo.toml")]);
    for name in &reached {
        let dir = root.join("crates").join(name);
        watched.insert(dir.clone());
        for path in all_files(&dir)? {
            if path.extension().is_some_and(|e| e == "rs") {
                let here = path.parent().unwrap_or(&dir).to_path_buf();
                for literal in string_literals(&std::fs::read_to_string(&path)?) {
                    // Relative to the crate: also after `env!("CARGO_MANIFEST_DIR")`.
                    let from_crate = literal.trim_start_matches('/');
                    // An absolute path names a file of the machine, not of the workspace: it
                    // is only read relative to the crate.
                    let mut bases = vec![(&dir, from_crate)];
                    if !Path::new(&literal).is_absolute() {
                        bases.push((&here, literal.as_str()));
                    }
                    for (base, literal) in bases {
                        let named = lexical(&base.join(literal));
                        if named.is_file() && !named.starts_with(&dir) {
                            files.insert(named);
                        }
                    }
                }
            }
            files.insert(path);
        }
    }
    Ok((files, watched))
}

/// The hex identity of `files` (each by its path from `root` and its bytes) and of
/// `root/Cargo.lock` (the packages every crate is built with).
pub fn identity(root: &Path, files: &BTreeSet<PathBuf>) -> io::Result<String> {
    let root = &lexical(root);
    let mut h = blake3::Hasher::new();
    let mut field = |bytes: &[u8]| {
        h.update(&(bytes.len() as u64).to_le_bytes());
        h.update(bytes);
    };
    for path in files {
        // Below the root, by their relative path (the same in every checkout); a file outside
        // it by its whole path.
        let path = lexical(path);
        let relative = path.strip_prefix(root).unwrap_or(&path);
        field(relative.to_string_lossy().replace('\\', "/").as_bytes());
        field(&std::fs::read(&path)?);
    }
    field(&std::fs::read(root.join("Cargo.lock"))?);
    Ok(h.finalize().to_hex().to_string())
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
