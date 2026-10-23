//! Compiles the NVENC encoder's C source.
fn main() {
    println!("cargo:rerun-if-changed=build.rs");
    println!("cargo:rerun-if-changed=native/nvenc/unbound_nvenc.c");
    println!("cargo:rerun-if-changed=native/nvenc/nvEncodeAPI.h");
    cc::Build::new()
        .file("native/nvenc/unbound_nvenc.c")
        .include("native/nvenc")
        .compile("unbound_nvenc");
    println!("cargo:rustc-link-lib=dl");
}
