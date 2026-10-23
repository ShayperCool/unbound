//! Builds the managed engine (`managed/`) and the native libraries it calls (`native/`: Box3D with
//! Unbound's shim) into `target/managed` whenever their sources change, so that `cargo build` alone
//! produces a runnable tool.
use std::{
    path::{Path, PathBuf},
    process::Command,
};

fn main() {
    let manifest = PathBuf::from(std::env::var("CARGO_MANIFEST_DIR").unwrap());
    let root = manifest.parent().unwrap().parent().unwrap();
    let managed = root.join("managed");
    println!("cargo:rerun-if-changed={}", managed.display());
    let target = std::env::var_os("CARGO_TARGET_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| root.join("target"));
    build_box3d(root, &target.join("managed"));
    println!("cargo:rerun-if-env-changed=UNBOUND_SKIP_MANAGED_BUILD");
    if std::env::var_os("UNBOUND_SKIP_MANAGED_BUILD").is_some() {
        return;
    }
    let dotnet = dotnet_executable();
    if build(&dotnet, &target, &managed, false) {
        return;
    }
    // Hand-written code that changed under its generated stubs (a signature now implemented by
    // hand) stops the build until `unbound gen-stubs` runs, which needs this build: without stubs
    // the tools still work, the game misses the stubbed members.
    if build(&dotnet, &target, &managed, true) {
        println!(
            "cargo:warning=the managed engine was built WITHOUT compatibility stubs because they no \
             longer fit the hand-written code; run `unbound gen-stubs -p <project>` and rebuild"
        );
        return;
    }
    panic!("building the managed engine failed");
}

fn build(dotnet: &PathBuf, target: &Path, managed: &Path, no_stubs: bool) -> bool {
    let mut command = Command::new(dotnet);
    command.args([
        "build",
        "-c",
        "Debug",
        "-v",
        "q",
        "-nologo",
        "-clp:ErrorsOnly",
        "-o",
    ]);
    command.arg(target.join("managed"));
    if no_stubs {
        command.arg("-p:UnboundNoStubs=true");
    }
    command
        .arg(managed.join("Unbound.Host/Unbound.Host.csproj"))
        .status()
        .unwrap_or_else(|e| panic!("running {}: {e}", dotnet.display()))
        .success()
}

/// Compiles Box3D and `native/unbound_box3d.c` into the `unbound_box3d` shared library next to the
/// managed engine, which loads it by name. Optimized in every profile: the solver is the hot path.
fn build_box3d(root: &Path, out_dir: &Path) {
    let native = root.join("native");
    println!("cargo:rerun-if-changed={}", native.display());
    let src = native.join("box3d/src");
    let mut files: Vec<PathBuf> = std::fs::read_dir(&src)
        .unwrap_or_else(|e| panic!("reading {}: {e}", src.display()))
        .filter_map(|entry| entry.ok().map(|e| e.path()))
        .filter(|path| path.extension().is_some_and(|ext| ext == "c"))
        .collect();
    files.sort();
    files.push(native.join("unbound_box3d.c"));

    let mut build = cc::Build::new();
    build
        .files(&files)
        .include(native.join("box3d/include"))
        .include(&src)
        .opt_level(2)
        .debug(false)
        .pic(true)
        .warnings(false)
        .define("NDEBUG", None)
        .cargo_metadata(false);
    let compiler = build.get_compiler();
    let msvc = compiler.is_like_msvc();
    if msvc {
        build.flag("/std:c17");
    } else {
        // As Box3D's CMake: C17 with extensions, no fused multiply-add (determinism), and only
        // the shim's functions exported.
        build
            .flag("-std=gnu17")
            .flag("-ffp-contract=off")
            .flag("-fvisibility=hidden");
    }
    let objects = build.compile_intermediates();

    std::fs::create_dir_all(out_dir).unwrap_or_else(|e| panic!("creating {}: {e}", out_dir.display()));
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let name = match target_os.as_str() {
        "windows" => "unbound_box3d.dll",
        "macos" | "ios" => "libunbound_box3d.dylib",
        _ => "libunbound_box3d.so",
    };
    let library = out_dir.join(name);
    let mut link = compiler.to_command();
    if msvc {
        link.arg("/LD").args(&objects).arg(format!("/Fe{}", library.display()));
    } else {
        link.arg(if target_os == "macos" { "-dynamiclib" } else { "-shared" })
            .arg("-o")
            .arg(&library)
            .args(&objects);
        if target_os != "macos" {
            link.arg("-lm");
        }
    }
    let status = link
        .status()
        .unwrap_or_else(|e| panic!("linking {}: {e}", library.display()));
    assert!(status.success(), "linking {} failed", library.display());
}

fn dotnet_executable() -> PathBuf {
    if let Some(root) = std::env::var_os("DOTNET_ROOT") {
        return PathBuf::from(root).join("dotnet");
    }
    let home = PathBuf::from(std::env::var_os("HOME").unwrap_or_default());
    let user = home.join(".dotnet/dotnet");
    if user.exists() {
        return user;
    }
    PathBuf::from("dotnet")
}
