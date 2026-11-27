use std::path::PathBuf;
use std::process::Command;

fn main() {
    // The executable's own `execve` (src/editor_launch.rs) must be the one .NET's native library
    // calls: on Linux export it via --dynamic-list; on macOS link a small __DATA,__interpose dylib
    // placed in target/managed/ (beside libunbound_physx.dylib so target/, cargo test, runtime
    // snapshots and release packages all resolve @rpath/libunbound_launch_guard.dylib).
    println!("cargo:rerun-if-changed=exported.list");
    println!("cargo:rerun-if-changed=launch_guard_macos.c");
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let manifest = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
    if target_os == "linux" {
        let list = manifest.join("exported.list");
        println!(
            "cargo:rustc-link-arg-bins=-Wl,--dynamic-list={}",
            list.display()
        );
    } else if target_os == "macos" {
        let root = manifest.parent().unwrap().parent().unwrap();
        println!("cargo:rerun-if-env-changed=CARGO_TARGET_DIR");
        let target_dir = match std::env::var_os("CARGO_TARGET_DIR") {
            Some(dir) => {
                let path = PathBuf::from(dir);
                if path.is_absolute() {
                    path
                } else {
                    root.join(path)
                }
            }
            None => root.join("target"),
        };
        let managed = target_dir.join("managed");
        std::fs::create_dir_all(&managed)
            .unwrap_or_else(|e| panic!("creating {}: {e}", managed.display()));
        let dylib = managed.join("libunbound_launch_guard.dylib");
        println!("cargo:rerun-if-changed={}", dylib.display());
        let tmp = managed.join(format!(
            ".libunbound_launch_guard.{}.dylib.tmp",
            std::process::id()
        ));
        let src = manifest.join("launch_guard_macos.c");
        println!("cargo:rerun-if-env-changed=CC");
        let cc = std::env::var_os("CC").unwrap_or_else(|| "cc".into());
        let status = Command::new(&cc)
            .args([
                "-O2",
                "-Wall",
                "-Wextra",
                "-Werror",
                "-dynamiclib",
                "-install_name",
                "@rpath/libunbound_launch_guard.dylib",
                "-o",
            ])
            .arg(&tmp)
            .arg(&src)
            .status()
            .expect("running cc to build libunbound_launch_guard.dylib");
        assert!(
            status.success(),
            "building libunbound_launch_guard.dylib failed with {status}"
        );
        std::fs::rename(&tmp, &dylib).unwrap_or_else(|e| {
            let _ = std::fs::remove_file(&tmp);
            panic!("installing {}: {e}", dylib.display());
        });
        println!("cargo:rustc-link-search=native={}", managed.display());
        println!("cargo:rustc-link-lib=dylib=unbound_launch_guard");
        // Cover runtime snapshots/packages (@executable_path/managed), target/<profile>/unbound
        // (@executable_path/../managed), target/<profile>/deps/ tests (@executable_path/../../managed),
        // and CARGO_BUILD_BUILD_DIR=target/build/<profile>/deps/ tests (@executable_path/../../../managed).
        for rpath in [
            "@executable_path/managed",
            "@executable_path/../managed",
            "@executable_path/../../managed",
            "@executable_path/../../../managed",
        ] {
            println!("cargo:rustc-link-arg=-Wl,-rpath,{rpath}");
        }
    }
}
