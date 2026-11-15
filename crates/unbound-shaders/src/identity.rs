//! The identity of the compiler a shader cache entry comes from: a hash of the sources that
//! decide what the compiler outputs. Shared by the build script, which builds it into the crate
//! ([`crate::COMPILER`]), and the crate's tests, which check it against the sources.
//!
//! Two builds whose compilers differ never read each other's entries, even under the same
//! [`crate::FORMAT_VERSION`] (two branches that each raise it to the same number, the land gate
//! beside a worker's build): the cache is shared by every build on a machine.

use std::path::{Path, PathBuf};

/// The source directories of the compiler's output, relative to the workspace root: this crate
/// and the device rules it compiles by (`unbound-compat`).
pub const SOURCE_DIRECTORIES: [&str; 2] =
    ["crates/unbound-shaders/src", "crates/unbound-compat/src"];

/// The lockfile packages that compile programs: naga (SPIR-V in, WGSL out) and glslang (shaderc).
pub const LOCKED_PACKAGES: [&str; 3] = ["naga", "shaderc", "shaderc-sys"];

/// Every file under `directory`, sorted by path.
fn files(directory: &Path) -> std::io::Result<Vec<PathBuf>> {
    let mut out = Vec::new();
    let mut pending = vec![directory.to_path_buf()];
    while let Some(dir) = pending.pop() {
        for entry in std::fs::read_dir(&dir)? {
            let path = entry?.path();
            if path.is_dir() {
                pending.push(path);
            } else {
                out.push(path);
            }
        }
    }
    out.sort();
    Ok(out)
}

/// The `[[package]]` blocks of `Cargo.lock` for [`LOCKED_PACKAGES`], in lockfile order.
fn locked_packages(lock: &str) -> String {
    lock.split("[[package]]")
        .filter(|block| {
            block.lines().any(|line| {
                LOCKED_PACKAGES
                    .iter()
                    .any(|name| line.trim() == format!("name = \"{name}\""))
            })
        })
        .collect()
}

/// The compiler identity of the workspace at `root`: the hex hash of the files of
/// [`SOURCE_DIRECTORIES`] (each by its path from the root and its bytes) and the locked
/// [`LOCKED_PACKAGES`].
pub fn compiler_identity(root: &Path) -> std::io::Result<String> {
    let mut h = blake3::Hasher::new();
    let mut field = |bytes: &[u8]| {
        h.update(&(bytes.len() as u64).to_le_bytes());
        h.update(bytes);
    };
    for directory in SOURCE_DIRECTORIES {
        for path in files(&root.join(directory))? {
            let relative = path.strip_prefix(root).unwrap_or(&path);
            field(relative.to_string_lossy().replace('\\', "/").as_bytes());
            field(&std::fs::read(&path)?);
        }
    }
    let lock = std::fs::read_to_string(root.join("Cargo.lock"))?;
    field(locked_packages(&lock).as_bytes());
    Ok(h.finalize().to_hex().to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The identity built into the crate is the hash of the sources it was built from.
    #[test]
    fn the_built_identity_is_that_of_the_sources() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
        assert_eq!(crate::COMPILER, compiler_identity(&root).unwrap());
    }

    /// A change to a compiler source or to a locked compiling package is another compiler; a
    /// change to another locked package is not.
    #[test]
    fn the_identity_follows_the_compiler_sources_and_its_packages() {
        let root = std::env::temp_dir().join(format!("unbound-compiler-id-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        for directory in SOURCE_DIRECTORIES {
            std::fs::create_dir_all(root.join(directory).join("nested")).unwrap();
            std::fs::write(root.join(directory).join("lib.rs"), "a").unwrap();
            std::fs::write(root.join(directory).join("nested/more.rs"), "b").unwrap();
        }
        let lock = |naga: &str, other: &str| {
            format!(
                "version = 4\n\n[[package]]\nname = \"naga\"\nversion = \"{naga}\"\n\n[[package]]\nname = \"other\"\nversion = \"{other}\"\ndependencies = [\n \"naga\",\n]\n"
            )
        };
        std::fs::write(root.join("Cargo.lock"), lock("30.0.1", "1.0.0")).unwrap();
        let first = compiler_identity(&root).unwrap();
        assert_eq!(compiler_identity(&root).unwrap(), first);
        std::fs::write(root.join("Cargo.lock"), lock("30.0.1", "2.0.0")).unwrap();
        assert_eq!(compiler_identity(&root).unwrap(), first);
        std::fs::write(root.join("Cargo.lock"), lock("30.0.2", "2.0.0")).unwrap();
        let naga = compiler_identity(&root).unwrap();
        assert_ne!(naga, first);
        let nested = root.join(SOURCE_DIRECTORIES[1]).join("nested/more.rs");
        std::fs::write(&nested, "c").unwrap();
        let source = compiler_identity(&root).unwrap();
        assert_ne!(source, naga);
        // The same bytes under another name are another source.
        std::fs::rename(&nested, nested.with_file_name("renamed.rs")).unwrap();
        assert_ne!(compiler_identity(&root).unwrap(), source);
        std::fs::remove_dir_all(&root).ok();
    }
}
