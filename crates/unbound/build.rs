fn main() {
    // The executable's own `execve` (src/editor_launch.rs) must be the one .NET's native library
    // calls: export it (the list file names it) so the dynamic linker binds that library's
    // reference to it.
    println!("cargo:rerun-if-changed=exported.list");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("linux") {
        let list = std::path::Path::new(&std::env::var("CARGO_MANIFEST_DIR").unwrap())
            .join("exported.list");
        println!(
            "cargo:rustc-link-arg-bins=-Wl,--dynamic-list={}",
            list.display()
        );
    }
}
