fn main() {
    // The executable's own `execve` (src/editor_launch.rs) must be the one .NET's native library
    // calls: export it so the dynamic linker binds that library's reference to it.
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("linux") {
        println!("cargo:rustc-link-arg-bins=-Wl,--export-dynamic-symbol=execve");
    }
}
