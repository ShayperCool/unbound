//! Builds the managed engine (`managed/`) and the native library it calls (`native/`: PhysX with
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
    build_physx(root, &target.join("managed"));
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

/// Every directory under `dir` (itself included), for PhysX's flat include paths.
fn directories(dir: &Path, into: &mut Vec<PathBuf>) {
    into.push(dir.to_path_buf());
    let mut entries: Vec<PathBuf> = std::fs::read_dir(dir)
        .unwrap_or_else(|e| panic!("reading {}: {e}", dir.display()))
        .filter_map(|entry| entry.ok().map(|e| e.path()))
        .filter(|path| path.is_dir())
        .collect();
    entries.sort();
    for entry in entries {
        directories(&entry, into);
    }
}

fn sources(dir: &Path, extension: &str, into: &mut Vec<PathBuf>) {
    let mut entries: Vec<PathBuf> = std::fs::read_dir(dir)
        .unwrap_or_else(|e| panic!("reading {}: {e}", dir.display()))
        .filter_map(|entry| entry.ok().map(|e| e.path()))
        .collect();
    entries.sort();
    for entry in entries {
        if entry.is_dir() {
            sources(&entry, extension, into);
        } else if entry.extension().is_some_and(|ext| ext == extension) {
            into.push(entry);
        }
    }
}

/// Compiles PhysX 4.1 (`native/physx`, the release Unity runs) and `native/unbound_physx.cpp` into
/// the `unbound_physx` shared library the managed 3D physics loads. PhysX itself is compiled once
/// into a static archive kept by its version and flags; the shim is rebuilt on every change.
fn build_physx(root: &Path, out_dir: &Path) {
    let native = root.join("native");
    let physx = native.join("physx");
    let mut includes = Vec::new();
    directories(&physx.join("source"), &mut includes);
    includes.push(physx.join("include"));
    includes.push(physx.join("pxshared/include"));
    let defines = [
        ("NDEBUG", None),
        ("PX_SUPPORT_PVD", Some("0")),
        ("PX_PHYSX_STATIC_LIB", None),
        ("DISABLE_CUDA_PHYSX", None),
        ("PX_NVTX", Some("0")),
        ("PX_COOKING", None),
    ];
    let configure = |build: &mut cc::Build| {
        build.cpp(true).includes(&includes).opt_level(2).debug(false).pic(true).warnings(false).cargo_metadata(false);
        for (name, value) in defines {
            build.define(name, value);
        }
        if build.get_compiler().is_like_msvc() {
            build.flag("/std:c++14");
        } else {
            // As PhysX's CMake for Linux, plus no fused multiply-add (determinism) and only the
            // shim's functions exported.
            build
                .flag("-std=c++14")
                .flag("-fno-rtti")
                .flag("-fno-exceptions")
                .flag("-fno-strict-aliasing")
                .flag("-ffp-contract=off")
                .flag("-fvisibility=hidden");
        }
    };

    let out = PathBuf::from(std::env::var("OUT_DIR").unwrap());
    // The archive is kept by a fingerprint of the sources (paths, sizes, modification times).
    let mut files = Vec::new();
    sources(&physx.join("source"), "cpp", &mut files);
    let mut headers = Vec::new();
    sources(&physx.join("source"), "h", &mut headers);
    sources(&physx.join("include"), "h", &mut headers);
    sources(&physx.join("pxshared"), "h", &mut headers);
    let mut fingerprint: u64 = 0xcbf29ce484222325;
    for file in files.iter().chain(headers.iter()) {
        let meta = std::fs::metadata(file).ok();
        let modified = meta
            .as_ref()
            .and_then(|m| m.modified().ok())
            .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
            .map_or(0, |d| d.as_nanos() as u64);
        let text = format!("{}:{}:{}", file.display(), meta.map_or(0, |m| m.len()), modified);
        for byte in text.bytes() {
            fingerprint = (fingerprint ^ u64::from(byte)).wrapping_mul(0x100000001b3);
        }
    }
    let archive_dir = out.join(format!("physx-{fingerprint:016x}"));
    let archive = archive_dir.join("libunbound_physx_sdk.a");
    if !archive.exists() {
        let mut build = cc::Build::new();
        configure(&mut build);
        build.files(&files).out_dir(&archive_dir).compile("unbound_physx_sdk");
    }

    println!("cargo:rerun-if-changed={}", native.display());
    let shim = native.join("unbound_physx.cpp");
    let mut build = cc::Build::new();
    configure(&mut build);
    let compiler = build.get_compiler();
    let objects = build.file(&shim).compile_intermediates();

    std::fs::create_dir_all(out_dir).unwrap_or_else(|e| panic!("creating {}: {e}", out_dir.display()));
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let name = match target_os.as_str() {
        "windows" => "unbound_physx.dll",
        "macos" | "ios" => "libunbound_physx.dylib",
        _ => "libunbound_physx.so",
    };
    let library = out_dir.join(name);
    let mut link = compiler.to_command();
    if compiler.is_like_msvc() {
        link.arg("/LD").args(&objects).arg(&archive).arg(format!("/Fe{}", library.display()));
    } else {
        link.arg(if target_os == "macos" { "-dynamiclib" } else { "-shared" })
            .arg("-o")
            .arg(&library)
            .args(&objects)
            .arg(&archive);
        if target_os != "macos" {
            link.args(["-lpthread", "-ldl", "-lm"]);
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
