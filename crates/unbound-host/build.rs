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
        "Release",
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

/// Compiles PhysX 4.1 and NvCloth 1.1 (`native/physx`, `native/nvcloth`: the releases Unity runs)
/// into static archives and links them with the flat C shims (`native/unbound_physx.cpp`,
/// `native/unbound_cloth.cpp`) into the `unbound_physx` library next to the managed engine.
fn build_physx(root: &Path, out_dir: &Path) {
    let native = root.join("native");
    let physx = native.join("physx");
    let nvcloth = native.join("nvcloth");
    let target_os = std::env::var("CARGO_CFG_TARGET_OS").unwrap_or_default();
    let out = PathBuf::from(std::env::var("OUT_DIR").unwrap());

    // PhysX: every source directory is an include directory, as its CMake has them.
    let mut physx_includes = Vec::new();
    directories(&physx.join("source"), &mut physx_includes);
    physx_includes.push(physx.join("include"));
    physx_includes.push(physx.join("pxshared/include"));
    let physx_defines: &[(&str, Option<&str>)] = &[
        ("NDEBUG", None),
        ("PX_SUPPORT_PVD", Some("0")),
        ("PX_PHYSX_STATIC_LIB", None),
        ("DISABLE_CUDA_PHYSX", None),
        ("PX_NVTX", Some("0")),
        ("PX_COOKING", None),
    ];
    let mut physx_sources = Vec::new();
    sources(&physx.join("source"), "cpp", &mut physx_sources);
    let mut physx_headers = Vec::new();
    for dir in ["source", "include", "pxshared"] {
        sources(&physx.join(dir), "h", &mut physx_headers);
    }
    let physx_archive = archive(&out, "physx", &physx_sources, &physx_headers, &physx_includes, physx_defines, &[]);

    // NvCloth: the CPU solver (SSE2 on x86, scalar elsewhere; no CUDA or DirectX) and the cooking
    // extensions, on PhysX's PxShared. Its NEON solver is Android's (cpu-features), so on ARM the
    // compiler's NEON macros are taken away and NvSimd falls back to scalar.
    let nvcloth_includes = vec![
        nvcloth.join("include"),
        nvcloth.join("extensions/include"),
        nvcloth.join("src"),
        nvcloth.join("extensions/src"),
        physx.join("pxshared/include"),
    ];
    let nvcloth_defines: &[(&str, Option<&str>)] =
        &[("NDEBUG", None), ("NV_CLOTH_ENABLE_CUDA", Some("0")), ("NV_CLOTH_ENABLE_DX11", Some("0"))];
    let mut nvcloth_sources = Vec::new();
    for dir in ["src", "extensions/src"] {
        for entry in std::fs::read_dir(nvcloth.join(dir)).unwrap_or_else(|e| panic!("reading {dir}: {e}")) {
            let path = entry.unwrap().path();
            if path.extension().is_some_and(|e| e == "cpp") {
                nvcloth_sources.push(path);
            }
        }
    }
    nvcloth_sources.push(nvcloth.join("include/NvCloth/ps/PsAllocator.cpp"));
    nvcloth_sources.push(nvcloth.join(if target_os == "windows" {
        "src/ps/windows/PsWindowsAtomic.cpp"
    } else {
        "src/ps/unix/PsUnixAtomic.cpp"
    }));
    nvcloth_sources.sort();
    let mut nvcloth_headers = Vec::new();
    sources(&nvcloth, "h", &mut nvcloth_headers);
    let target_arch = std::env::var("CARGO_CFG_TARGET_ARCH").unwrap_or_default();
    let nvcloth_flags: &[&str] = if matches!(target_arch.as_str(), "aarch64" | "arm") {
        &["-U__ARM_NEON", "-U__ARM_NEON__"]
    } else {
        &[]
    };
    let nvcloth_archive =
        archive(&out, "nvcloth", &nvcloth_sources, &nvcloth_headers, &nvcloth_includes, nvcloth_defines, nvcloth_flags);

    println!("cargo:rerun-if-changed={}", native.display());
    let mut build = cc::Build::new();
    configure(&mut build, &physx_includes, physx_defines, &[]);
    let compiler = build.get_compiler();
    let mut objects = build.file(native.join("unbound_physx.cpp")).compile_intermediates();
    let mut build = cc::Build::new();
    configure(&mut build, &nvcloth_includes, nvcloth_defines, nvcloth_flags);
    objects.extend(build.file(native.join("unbound_cloth.cpp")).compile_intermediates());

    std::fs::create_dir_all(out_dir).unwrap_or_else(|e| panic!("creating {}: {e}", out_dir.display()));
    let name = match target_os.as_str() {
        "windows" => "unbound_physx.dll",
        "macos" | "ios" => "libunbound_physx.dylib",
        _ => "libunbound_physx.so",
    };
    let library = out_dir.join(name);
    let mut link = compiler.to_command();
    if compiler.is_like_msvc() {
        link.arg("/LD")
            .args(&objects)
            .arg(&physx_archive)
            .arg(&nvcloth_archive)
            .arg(format!("/Fe{}", library.display()));
    } else {
        link.arg(if target_os == "macos" { "-dynamiclib" } else { "-shared" })
            .arg("-o")
            .arg(&library)
            .args(&objects)
            .arg(&physx_archive)
            .arg(&nvcloth_archive);
        if target_os != "macos" {
            link.args(["-lpthread", "-ldl", "-lm"]);
        }
    }
    let status = link
        .status()
        .unwrap_or_else(|e| panic!("linking {}: {e}", library.display()));
    assert!(status.success(), "linking {} failed", library.display());
}

/// The C++ settings of the native code: as PhysX's CMake for Linux, plus no fused multiply-add
/// (determinism) and only the shims' functions exported.
fn configure(build: &mut cc::Build, includes: &[PathBuf], defines: &[(&str, Option<&str>)], flags: &[&str]) {
    build.cpp(true).includes(includes).opt_level(2).debug(false).pic(true).warnings(false).cargo_metadata(false);
    for (name, value) in defines {
        build.define(name, *value);
    }
    if build.get_compiler().is_like_msvc() {
        build.flag("/std:c++14");
    } else {
        build
            .flag("-std=c++14")
            .flag("-fno-rtti")
            .flag("-fno-exceptions")
            .flag("-fno-strict-aliasing")
            .flag("-ffp-contract=off")
            .flag("-fvisibility=hidden");
    }
    for flag in flags {
        build.flag(flag);
    }
}

/// A static archive of sources, kept by a fingerprint of the sources and headers (paths, sizes,
/// modification times).
fn archive(
    out: &Path,
    name: &str,
    files: &[PathBuf],
    headers: &[PathBuf],
    includes: &[PathBuf],
    defines: &[(&str, Option<&str>)],
    flags: &[&str],
) -> PathBuf {
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
    let archive_dir = out.join(format!("{name}-{fingerprint:016x}"));
    let lib = format!("unbound_{name}_sdk");
    let archive = archive_dir.join(format!("lib{lib}.a"));
    if !archive.exists() {
        let mut build = cc::Build::new();
        configure(&mut build, includes, defines, flags);
        build.files(files).out_dir(&archive_dir).compile(&lib);
    }
    archive
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
