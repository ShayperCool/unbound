//! Compiles libvorbis and libogg (`native/vorbis`) into the crate: FSB5 Vorbis clips need their
//! setup headers made again by libvorbis' encoder (see `src/fsb5.rs`), through
//! `native/unbound_vorbis.c`. Also libtess2 (`native/libtess2`), which triangulates Tight sprite
//! meshes as the Editor does (see `src/sprite_mesh.rs`), and the GLU polygon tessellator for
//! model n-gons (`native/glu_tess`, `src/model/triangulation.rs`).
use std::path::{Path, PathBuf};

fn main() {
    let native = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../native");
    build_crunch(&native);
    let astc = native.join("astcenc");
    let astc_adapter = native.join("unbound_astc.cpp");
    println!("cargo:rerun-if-changed={}", astc.display());
    println!("cargo:rerun-if-changed={}", astc_adapter.display());
    let mut astc_sources: Vec<_> = std::fs::read_dir(&astc)
        .expect("reading native/astcenc")
        .map(|e| e.unwrap().path())
        .filter(|p| p.extension().is_some_and(|e| e == "cpp"))
        .collect();
    astc_sources.sort();
    let arch = std::env::var("CARGO_CFG_TARGET_ARCH").unwrap();
    cc::Build::new()
        .cpp(true)
        .std("c++14")
        .files(astc_sources)
        .file(astc_adapter)
        .include(&native)
        .define("ASTCENC_DYNAMIC_LIBRARY", None)
        .define("ASTCENC_SSE", if arch == "x86_64" { "20" } else { "0" })
        .define("ASTCENC_NEON", if arch == "aarch64" { "1" } else { "0" })
        .define("ASTCENC_AVX", "0")
        .define("ASTCENC_F16C", "0")
        .define("ASTCENC_POPCNT", "0")
        .define("NDEBUG", None)
        .opt_level(2)
        .flag_if_supported("-ffp-contract=off")
        .warnings(false)
        .compile("unbound_astc");
    println!(
        "cargo:rerun-if-changed={}",
        native.join("amd_bc4").display()
    );
    println!("cargo:rerun-if-changed={}", native.join("bc7e").display());
    println!(
        "cargo:rerun-if-changed={}",
        native.join("stb_dxt").display()
    );
    println!(
        "cargo:rerun-if-changed={}",
        native.join("unbound_runtime_dxt.cpp").display()
    );
    println!(
        "cargo:rerun-if-changed={}",
        native.join("unbound_bc7e.cpp").display()
    );
    cc::Build::new()
        .cpp(true)
        .std("c++17")
        .file(native.join("bc7e/basisu_bc7e_scalar.cpp"))
        .file(native.join("unbound_bc7e.cpp"))
        .file(native.join("amd_bc4/encode.cpp"))
        .file(native.join("unbound_runtime_dxt.cpp"))
        .define("NDEBUG", None)
        .opt_level(2)
        .flag_if_supported("-ffp-contract=off")
        .warnings(false)
        .compile("unbound_bc7e");
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
    let tess = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../native/libtess2");
    println!("cargo:rerun-if-changed={}", tess.display());
    let mut tess_sources: Vec<PathBuf> = std::fs::read_dir(tess.join("Source"))
        .expect("reading native/libtess2/Source")
        .map(|e| e.unwrap().path())
        .filter(|p| p.extension().is_some_and(|e| e == "c"))
        .collect();
    tess_sources.sort();
    // The Editor's libtess2 has no fused multiply-adds: keep every product rounded.
    cc::Build::new()
        .files(&tess_sources)
        .include(tess.join("Include"))
        .define("NDEBUG", None)
        .opt_level(2)
        .flag_if_supported("-ffp-contract=off")
        .warnings(false)
        .compile("unbound_tess2");
    let glu = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../native/glu_tess");
    let adapter = glu.join("../unbound_model_tess.c");
    println!("cargo:rerun-if-changed={}", glu.display());
    println!("cargo:rerun-if-changed={}", adapter.display());
    let mut sources: Vec<_> = std::fs::read_dir(glu.join("Source"))
        .unwrap()
        .map(|e| e.unwrap().path())
        // This implementation file is included by priorityq.c.
        .filter(|p| {
            p.extension().is_some_and(|e| e == "c") && p.file_name().unwrap() != "priorityq-heap.c"
        })
        .collect();
    sources.sort();
    cc::Build::new()
        .files(sources)
        .file(adapter)
        .include(glu.join("Include"))
        .define("NDEBUG", None)
        .opt_level(2)
        .flag_if_supported("-ffp-contract=off")
        .warnings(false)
        .compile("unbound_model_tess");
    // intel_tex_2's ISPC kernels come with C++ glue (exception personality): the platform's C++
    // runtime, libc++ on Apple systems.
    let cxx = match std::env::var("CARGO_CFG_TARGET_VENDOR").as_deref() {
        Ok("apple") => "c++",
        _ => "stdc++",
    };
    println!("cargo:rustc-link-lib=dylib={cxx}");
}

fn build_crunch(native: &Path) {
    let prepare = native.join("crunch/source.py");
    let adapter = native.join("unbound_crunch.cpp");
    println!("cargo:rerun-if-changed={}", prepare.display());
    println!("cargo:rerun-if-changed={}", adapter.display());
    for key in [
        "XDG_CACHE_HOME",
        "UNBOUND_CRUNCH_ARCHIVE",
        "UNBOUND_OFFLINE",
        "CARGO_NET_OFFLINE",
    ] {
        println!("cargo:rerun-if-env-changed={key}");
    }
    let output = std::process::Command::new("python3")
        .arg(&prepare)
        .output()
        .expect("running the Crunch dependency preparation");
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let root = PathBuf::from(String::from_utf8(output.stdout).unwrap().trim());
    let source = root.join("crnlib");
    println!("cargo:rerun-if-changed={}", root.display());
    // Use the library's source list, without the command-line program or stale build entries.
    let makefile = std::fs::read_to_string(source.join("Makefile")).unwrap();
    let objects = makefile
        .split("OBJECTS =")
        .nth(1)
        .unwrap()
        .split("\n\n")
        .next()
        .unwrap();
    let files: Vec<_> = objects
        .split_whitespace()
        .filter_map(|word| word.strip_suffix(".o"))
        .map(|name| source.join(format!("{name}.cpp")))
        .filter(|file| file.is_file())
        .collect();
    cc::Build::new()
        .cpp(true)
        .std("c++14")
        .files(files)
        .file(adapter)
        .include(&source)
        .include(root.join("inc"))
        .define("CRNLIB_ANSI_CPLUSPLUS", "1")
        .define("NDEBUG", None)
        .flag("-include")
        .flag("stdint.h")
        .flag_if_supported("-fno-strict-aliasing")
        .flag_if_supported("-fno-fast-math")
        .flag_if_supported("-ffp-contract=off")
        .opt_level(2)
        .warnings(false)
        .compile("unbound_crunch");
}
