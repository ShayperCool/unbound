//! C# compiler response files (`csc.rsp`), read as the Editor reads them.
//!
//! An assembly definition's assembly takes the `csc.rsp` in its asmdef's folder when there is
//! one, otherwise the project's `Assets/csc.rsp` (else the obsolete `Assets/mcs.rsp`); the
//! predefined assemblies take the project's. A package's assembly falls back to the project's
//! file too. The file's arguments follow the Editor's own on the compiler's command line, so a
//! later option (`-langversion`, `-nullable`, `-warnaserror`) wins; `-r`/`-reference` options
//! become references, and `-define`/`-d` defines count for define constraints although
//! `CompilationPipeline` does not list them among an assembly's defines (Unity 6000.0.71f1,
//! tests/projects/gameinterop `rsp.*`). A `#` ends a line's arguments as in the Editor's reading of
//! the file for define constraints; its compile passes such text to the compiler and fails.
use std::path::{Path, PathBuf};

/// The file name the Editor looks for, and the obsolete one it still reads at the project root.
pub const RESPONSE_FILE: &str = "csc.rsp";
pub const OBSOLETE_RESPONSE_FILE: &str = "mcs.rsp";

/// One assembly's response file, parsed.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ResponseFile {
    /// The file, as the Editor names it in `ScriptCompilerOptions.ResponseFiles`.
    pub path: PathBuf,
    /// `-define`/`-d` defines, for define constraints.
    pub defines: Vec<String>,
    /// `-r`/`-reference` assemblies, resolved.
    pub references: Vec<PathBuf>,
    /// `-r:alias=path` assemblies: referenced under the alias (`extern alias`), resolved.
    pub aliased: Vec<(String, PathBuf)>,
    /// Every other argument, in order, as the compiler takes it.
    pub arguments: Vec<String>,
    /// References that could not be resolved, with the Editor's wording.
    pub errors: Vec<String>,
}

/// The response file that applies to an assembly whose asmdef is in `folder` (`None` for a
/// predefined assembly).
pub fn applying(assets: &Path, folder: Option<&Path>) -> Option<PathBuf> {
    if let Some(own) = folder
        .map(|f| f.join(RESPONSE_FILE))
        .filter(|p| p.is_file())
    {
        return Some(own);
    }
    [RESPONSE_FILE, OBSOLETE_RESPONSE_FILE]
        .iter()
        .map(|name| assets.join(name))
        .find(|p| p.is_file())
}

/// Reads and parses a response file; a reference resolves against `system_directories` (the
/// class library's), then the project directory, which wins when both have the file.
pub fn read(path: &Path, project: &Path, system_directories: &[PathBuf]) -> ResponseFile {
    let text = std::fs::read_to_string(path).unwrap_or_default();
    parse(&text, path, project, system_directories)
}

pub fn parse(
    text: &str,
    path: &Path,
    project: &Path,
    system_directories: &[PathBuf],
) -> ResponseFile {
    let mut out = ResponseFile {
        path: path.to_path_buf(),
        ..Default::default()
    };
    for (token, raw) in split(text) {
        let (name, value) = match token.find(':') {
            Some(i) => (&token[..i], &token[i + 1..]),
            None => (token.as_str(), ""),
        };
        let name = name.strip_prefix('-').or_else(|| name.strip_prefix('/'));
        match name {
            Some("d" | "define") => {
                out.defines.extend(
                    value
                        .split([';', ','])
                        .filter(|s| !s.is_empty())
                        .map(str::to_string),
                );
                out.arguments.push(raw);
            }
            Some("r" | "reference") => {
                let parts: Vec<&str> = value.split([';', ',']).collect();
                if value.is_empty() {
                    out.errors.push("No value set for reference".into());
                } else if parts.len() != 1 {
                    out.errors.push(
                        "Cannot specify multiple aliases using single /reference option".into(),
                    );
                } else {
                    // `alias=path` names the path after the alias.
                    let (alias, reference) = match parts[0].split_once('=') {
                        Some((alias, reference)) => (Some(alias), reference),
                        None => (None, parts[0]),
                    };
                    match resolve(reference, project, system_directories) {
                        Some(p) => match alias {
                            Some(alias) if is_identifier(alias) => {
                                out.aliased.push((alias.to_string(), p))
                            }
                            Some(alias) => out.errors.push(format!(
                                "{}: `{alias}` is not a valid extern alias",
                                path.display()
                            )),
                            None => out.references.push(p),
                        },
                        None => out.errors.push(format!(
                            "{}: not parsed correctly: {reference} could not be found as a system library.\n\
                             If this was meant as a user reference please provide the relative path from \
                             project root (parent of the Assets folder) in the response file.",
                            path.display()
                        )),
                    }
                }
            }
            _ => out.arguments.push(raw),
        }
    }
    out
}

/// A C# identifier (an extern alias), as the compiler accepts one.
fn is_identifier(name: &str) -> bool {
    let mut chars = name.chars();
    chars.next().is_some_and(|c| c == '_' || c.is_alphabetic())
        && chars.all(|c| c == '_' || c.is_alphanumeric())
}

fn resolve(reference: &str, project: &Path, system_directories: &[PathBuf]) -> Option<PathBuf> {
    let path = Path::new(reference);
    if path.is_absolute() {
        return Some(path.to_path_buf());
    }
    let user = project.join(path);
    if user.is_file() {
        return Some(user);
    }
    system_directories
        .iter()
        .map(|d| d.join(path))
        .find(|p| p.is_file())
}

/// The arguments of a response file: separated by spaces, a quoted part (`"` or `'`) kept whole
/// without its quotes, and `#` ending a line's arguments.
pub fn tokens(text: &str) -> Vec<String> {
    split(text).into_iter().map(|(value, _)| value).collect()
}

/// Each argument with its text as written (quotes included), which the compiler reads as a
/// command line of its own.
fn split(text: &str) -> Vec<(String, String)> {
    let mut out = Vec::new();
    for line in text.split(['\n', '\r']) {
        let mut current = String::new();
        let mut raw = String::new();
        let mut chars = line.chars();
        while let Some(c) = chars.next() {
            match c {
                '"' | '\'' => {
                    raw.push(c);
                    for q in chars.by_ref() {
                        raw.push(q);
                        if q == c {
                            break;
                        }
                        current.push(q);
                    }
                }
                ' ' => {
                    if !current.is_empty() || !raw.is_empty() {
                        out.push((std::mem::take(&mut current), std::mem::take(&mut raw)));
                    }
                }
                '#' => break,
                _ => {
                    current.push(c);
                    raw.push(c);
                }
            }
        }
        if !current.is_empty() || !raw.is_empty() {
            out.push((current, raw));
        }
    }
    out.retain(|(value, _)| !value.is_empty());
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("unbound-rsp-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn splits_like_the_editor() {
        assert_eq!(
            tokens(
                "-define:A;B  -unsafe # a comment -d:X\n'-nowarn:CS0219'\r\n-r:\"My Lib.dll\"\t"
            ),
            vec![
                "-define:A;B",
                "-unsafe",
                "-nowarn:CS0219",
                "-r:My Lib.dll\t"
            ]
        );
    }

    #[test]
    fn parses_options() {
        let dir = scratch("parse");
        let project = dir.as_path();
        std::fs::create_dir_all(project.join("Assets/Libs")).unwrap();
        std::fs::write(project.join("Assets/Libs/Ref.dll"), b"").unwrap();
        let system = project.join("bcl");
        std::fs::create_dir_all(&system).unwrap();
        std::fs::write(system.join("System.Web.dll"), b"").unwrap();
        let rsp = parse(
            "-define:A;B\n-d:C,D\n/define:E\n-D:F\n-unsafe\n-r:Assets/Libs/Ref.dll\n\
             -reference:alias=System.Web.dll\n-r:Missing.dll\n-r:A.dll;B.dll\n-nowarn:CS0219\n-langversion:10\n\
             -ruleset:\"Assets/Code Rules/a.ruleset\"\n-unsafe\t\n-r:1bad=Assets/Libs/Ref.dll",
            &project.join("Assets/csc.rsp"),
            project,
            &[system.clone()],
        );
        assert_eq!(rsp.defines, ["A", "B", "C", "D", "E"]);
        assert_eq!(rsp.references, [project.join("Assets/Libs/Ref.dll")]);
        assert_eq!(
            rsp.aliased,
            [("alias".to_string(), system.join("System.Web.dll"))]
        );
        assert_eq!(
            rsp.arguments,
            [
                "-define:A;B",
                "-d:C,D",
                "/define:E",
                "-D:F",
                "-unsafe",
                "-nowarn:CS0219",
                "-langversion:10",
                "-ruleset:\"Assets/Code Rules/a.ruleset\"",
                "-unsafe\t"
            ]
        );
        assert_eq!(rsp.errors.len(), 3);
        assert!(
            rsp.errors[2].contains("not a valid extern alias"),
            "{:?}",
            rsp.errors
        );
        assert!(rsp.errors[0].contains("Missing.dll could not be found"));
        assert!(rsp.errors[1].contains("multiple aliases"));
    }

    #[test]
    fn picks_the_assembly_folder_then_the_project() {
        let dir = scratch("applying");
        let assets = dir.join("Assets");
        let local = assets.join("Local");
        let other = assets.join("Other");
        std::fs::create_dir_all(&local).unwrap();
        std::fs::create_dir_all(&other).unwrap();
        assert_eq!(applying(&assets, Some(&local)), None);
        std::fs::write(assets.join(OBSOLETE_RESPONSE_FILE), "").unwrap();
        assert_eq!(
            applying(&assets, None),
            Some(assets.join(OBSOLETE_RESPONSE_FILE))
        );
        std::fs::write(assets.join(RESPONSE_FILE), "").unwrap();
        std::fs::write(local.join(RESPONSE_FILE), "").unwrap();
        assert_eq!(
            applying(&assets, Some(&local)),
            Some(local.join(RESPONSE_FILE))
        );
        assert_eq!(
            applying(&assets, Some(&other)),
            Some(assets.join(RESPONSE_FILE))
        );
        assert_eq!(applying(&assets, None), Some(assets.join(RESPONSE_FILE)));
    }
}
