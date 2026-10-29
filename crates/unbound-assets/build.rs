//! Compiles libvorbis and libogg (`native/vorbis`) into the crate: FSB5 Vorbis clips need their
//! setup headers made again by libvorbis' encoder (see `src/fsb5.rs`), through
//! `native/unbound_vorbis.c`.
use std::path::{Path, PathBuf};

fn main() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../native/vorbis");
    let mut sources: Vec<PathBuf> = Vec::new();
    for dir in ["lib", "ogg/src"] {
        for entry in std::fs::read_dir(root.join(dir))
            .unwrap_or_else(|e| panic!("reading native/vorbis/{dir}: {e}"))
        {
            let path = entry.unwrap().path();
            if path.extension().is_some_and(|e| e == "c") {
                sources.push(path);
            }
        }
    }
    sources.sort();
    // The flat API the crate calls.
    sources.push(root.join("../unbound_vorbis.c"));
    println!("cargo:rerun-if-changed={}", root.display());
    println!(
        "cargo:rerun-if-changed={}",
        root.join("../unbound_vorbis.c").display()
    );
    cc::Build::new()
        .files(&sources)
        .include(root.join("include"))
        .include(root.join("lib"))
        .include(root.join("ogg/include"))
        .define("NDEBUG", None)
        .opt_level(2)
        .warnings(false)
        .compile("unbound_vorbis");
    // intel_tex_2's ISPC kernels come with C++ glue (exception personality): the platform's C++
    // runtime, libc++ on Apple systems.
    let cxx = match std::env::var("CARGO_CFG_TARGET_VENDOR").as_deref() {
        Ok("apple") => "c++",
        _ => "stdc++",
    };
    println!("cargo:rustc-link-lib=dylib={cxx}");
}
