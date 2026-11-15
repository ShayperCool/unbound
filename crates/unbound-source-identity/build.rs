//! Makes the identities of the cache producers (`PRODUCERS` in `src/producers.rs`) from the
//! workspace's files and builds them into the crate.
#[path = "src/scan.rs"]
mod scan;

use std::path::Path;

// The producers, as `src/lib.rs` lists them (the build script cannot use the crate itself).
include!("src/producers.rs");

fn main() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
    println!(
        "cargo:rerun-if-changed={}",
        root.join("Cargo.lock").display()
    );
    for (env, start) in PRODUCERS {
        let (files, watched) =
            scan::producing_sources(&root, start).expect("reading the producing sources");
        for path in watched.iter().chain(&files) {
            println!("cargo:rerun-if-changed={}", path.display());
        }
        let id = scan::identity(&root, &files).expect("hashing the producing sources");
        println!("cargo:rustc-env={env}={id}");
    }
}
