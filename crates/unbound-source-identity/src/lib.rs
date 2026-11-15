//! The identity of the code that produces a cache's entries: a hash of the
//! workspace sources that output is made by and of the locked packages it uses. A cache keyed by
//! it is never read by a build whose producing code differs, whatever format number each build
//! declares (two branches raising it to the same number, a land gate beside a worker's build:
//! the caches are shared by every build on a machine).
//!
//! The sources are found from where the output is made: modules of a crate, followed through the
//! paths their code names (`crate::module`, `super::item`, `unbound_compat::GraphicsDevice`, the
//! re-exports of a crate root) and through the modules that implement the types found so. A
//! module is a file of a crate's `src` and the files of its directory; a crate's root and
//! manifest always count. What the reading cannot follow (a name it cannot resolve, `#[path]`,
//! a glob) counts the whole crate: an unsure reading hashes more, never less.
//!
//! The identities are made by the build script, one per cache producer, from the workspace the
//! crate is built in: [`SHADER_COMPILER`] (`unbound-shaders`, the program and kernel cache) and
//! [`BUILTIN_SHADERS`] (`unbound-assets`' built-in shader generator, its generated sources and
//! declarations caches).
use std::path::{Path, PathBuf};

mod scan;
pub use scan::{identity, producing_sources};

/// The identity of the shader compiler: `unbound-shaders` and what it names, with the locked
/// naga and glslang (shaderc) packages.
pub const SHADER_COMPILER: &str = env!("UNBOUND_IDENTITY_SHADER_COMPILER");

/// The identity of the built-in shader generator: `unbound-assets`' `builtin_shaders` module and
/// what it names, with the locked serialization packages of its declarations entries.
pub const BUILTIN_SHADERS: &str = env!("UNBOUND_IDENTITY_BUILTIN_SHADERS");

include!("producers.rs");

/// The directory below `parent` of a cache's entries made by code of `identity` in `format`:
/// named by the first 128 bits of their hash. Builds whose producing code differs use
/// directories of their own.
pub fn keyed_dir(parent: &Path, format: u32, identity: &str) -> PathBuf {
    let mut h = blake3::Hasher::new();
    h.update(&format.to_le_bytes());
    h.update(identity.as_bytes());
    parent.join(&h.finalize().to_hex()[..32])
}

#[cfg(test)]
mod keyed_tests {
    use super::*;

    /// A cache's directory follows both the format and the producing code.
    #[test]
    fn keyed_directories_follow_the_format_and_the_identity() {
        let parent = Path::new("cache");
        let dir = keyed_dir(parent, 3, "abc");
        assert_eq!(dir.parent(), Some(parent));
        assert_eq!(dir.file_name().unwrap().len(), 32);
        assert_eq!(keyed_dir(parent, 3, "abc"), dir);
        assert_ne!(keyed_dir(parent, 4, "abc"), dir);
        assert_ne!(keyed_dir(parent, 3, "abd"), dir);
    }

    /// The identities built in are those of the sources the producers are made of now.
    #[test]
    fn the_built_identities_are_those_of_the_sources() {
        let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
        for ((_, start, modules, locked), built) in
            PRODUCERS.iter().zip([SHADER_COMPILER, BUILTIN_SHADERS])
        {
            let (files, _) = producing_sources(&root, start, modules).unwrap();
            assert_eq!(identity(&root, &files, locked).unwrap(), built, "{start}");
        }
    }
}
