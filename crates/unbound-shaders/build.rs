//! Builds the compiler identity into the crate (`UNBOUND_SHADERS_COMPILER`, see
//! `src/identity.rs`): shader cache entries are keyed by it.
#[path = "src/identity.rs"]
mod identity;

use std::path::Path;

fn main() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    for directory in identity::SOURCE_DIRECTORIES {
        println!("cargo:rerun-if-changed={}", root.join(directory).display());
    }
    println!(
        "cargo:rerun-if-changed={}",
        root.join("Cargo.lock").display()
    );
    let id = identity::compiler_identity(&root).expect("hashing the shader compiler's sources");
    println!("cargo:rustc-env=UNBOUND_SHADERS_COMPILER={id}");
}
