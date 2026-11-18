// Which .NET installation builds and runs the managed engine. Shared by the build script
// (`include!`) and the runtime host, so both pick the same one; standard library only.

/// The .NET major version the managed engine targets (`TargetFramework` in
/// managed/Directory.Build.props).
pub const DOTNET_MAJOR: u32 = 10;

/// What an installation must have.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum DotnetNeed {
    /// An SDK of [`DOTNET_MAJOR`] (`sdk/10.*`): to build the managed engine.
    Sdk,
    /// The runtime of [`DOTNET_MAJOR`] (`shared/Microsoft.NETCore.App/10.*`) and its host
    /// resolver (`host/fxr`): to run it.
    Runtime,
}

/// Where .NET installations are looked for, in order: `DOTNET_ROOT`, `~/.dotnet`, the system
/// locations, then the directory of a `dotnet` on `PATH` (through its symlinks).
pub fn dotnet_candidates() -> Vec<std::path::PathBuf> {
    use std::path::PathBuf;
    let mut candidates = Vec::new();
    if let Some(root) = std::env::var_os("DOTNET_ROOT").filter(|r| !r.is_empty()) {
        candidates.push(PathBuf::from(root));
    }
    if let Some(home) = std::env::var_os("HOME") {
        candidates.push(PathBuf::from(home).join(".dotnet"));
    }
    candidates.extend(
        [
            "/usr/local/share/dotnet",
            "/usr/share/dotnet",
            "/usr/lib/dotnet",
        ]
        .map(PathBuf::from),
    );
    if let Some(path) = std::env::var_os("PATH") {
        for dir in std::env::split_paths(&path) {
            let exe = dir.join("dotnet");
            if let Some(root) = exe
                .is_file()
                .then(|| exe.canonicalize().ok())
                .flatten()
                .and_then(|e| e.parent().map(PathBuf::from))
            {
                candidates.push(root);
            }
        }
    }
    let mut seen = std::collections::HashSet::new();
    candidates.retain(|c| seen.insert(c.clone()));
    candidates
}

/// The versions in `dir` (its subdirectories' names) of major `major`.
fn dotnet_versions(dir: &std::path::Path, major: u32) -> Vec<String> {
    let prefix = format!("{major}.");
    let mut versions: Vec<String> = std::fs::read_dir(dir)
        .map(|entries| {
            entries
                .filter_map(|e| e.ok())
                .filter(|e| e.path().is_dir())
                .filter_map(|e| e.file_name().to_str().map(str::to_string))
                .filter(|name| name.starts_with(&prefix))
                .collect()
        })
        .unwrap_or_default();
    versions.sort();
    versions
}

/// Whether an installation has what is needed.
pub fn dotnet_has(root: &std::path::Path, need: DotnetNeed) -> bool {
    match need {
        DotnetNeed::Sdk => {
            root.join("dotnet").is_file()
                && !dotnet_versions(&root.join("sdk"), DOTNET_MAJOR).is_empty()
        }
        DotnetNeed::Runtime => {
            root.join("host/fxr").is_dir()
                && !dotnet_versions(&root.join("shared/Microsoft.NETCore.App"), DOTNET_MAJOR)
                    .is_empty()
        }
    }
}

/// The first installation that has what is needed, or an error that lists what each candidate
/// had (an installation with only other SDKs is skipped, not used).
pub fn find_dotnet(need: DotnetNeed) -> Result<std::path::PathBuf, String> {
    let candidates = dotnet_candidates();
    if let Some(found) = candidates.iter().find(|root| dotnet_has(root, need)) {
        return Ok(found.clone());
    }
    let (what, sub) = match need {
        DotnetNeed::Sdk => ("SDK", "sdk"),
        DotnetNeed::Runtime => ("runtime", "shared/Microsoft.NETCore.App"),
    };
    let mut seen = Vec::new();
    for root in &candidates {
        if !root.is_dir() {
            continue;
        }
        let mut all: Vec<String> = std::fs::read_dir(root.join(sub))
            .map(|entries| {
                entries
                    .filter_map(|e| e.ok())
                    .filter_map(|e| e.file_name().to_str().map(str::to_string))
                    .collect()
            })
            .unwrap_or_default();
        all.sort();
        seen.push(format!(
            "{} ({})",
            root.display(),
            if all.is_empty() {
                "none".to_string()
            } else {
                all.join(", ")
            }
        ));
    }
    Err(format!(
        "no .NET {DOTNET_MAJOR} {what} found: the managed engine targets net{DOTNET_MAJOR}.0; looked in DOTNET_ROOT, ~/.dotnet, \
         /usr/local/share/dotnet, /usr/share/dotnet, /usr/lib/dotnet and PATH{}; install the .NET {DOTNET_MAJOR} SDK or set \
         DOTNET_ROOT to an installation that has it",
        if seen.is_empty() {
            String::new()
        } else {
            format!(", found {}", seen.join("; "))
        }
    ))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_installation_counts_only_with_the_engines_major_version() {
        let root = std::env::temp_dir().join(format!("unbound-dotnet-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("sdk/9.0.100")).unwrap();
        std::fs::create_dir_all(root.join("host/fxr/9.0.0")).unwrap();
        std::fs::create_dir_all(root.join("shared/Microsoft.NETCore.App/9.0.0")).unwrap();
        std::fs::write(root.join("dotnet"), b"").unwrap();
        assert!(!dotnet_has(&root, DotnetNeed::Sdk));
        assert!(!dotnet_has(&root, DotnetNeed::Runtime));
        std::fs::create_dir_all(
            root.join(format!("shared/Microsoft.NETCore.App/{DOTNET_MAJOR}.0.1")),
        )
        .unwrap();
        assert!(dotnet_has(&root, DotnetNeed::Runtime));
        assert!(!dotnet_has(&root, DotnetNeed::Sdk), "a runtime is no SDK");
        std::fs::create_dir_all(root.join(format!("sdk/{DOTNET_MAJOR}.0.100"))).unwrap();
        assert!(dotnet_has(&root, DotnetNeed::Sdk));
        // A version that merely starts with the digits is another major.
        let other = root.join("other");
        std::fs::create_dir_all(other.join(format!("sdk/{DOTNET_MAJOR}0.0.1"))).unwrap();
        std::fs::write(other.join("dotnet"), b"").unwrap();
        assert!(!dotnet_has(&other, DotnetNeed::Sdk));
        let _ = std::fs::remove_dir_all(&root);
    }
}
