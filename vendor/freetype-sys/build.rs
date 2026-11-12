// Unbound: the upstream build script with the bundled sources taken from
// native/freetype/source.py's prepared copy instead of this crate's directory; see PATCHES.md.
use std::env;
use std::path::{Path, PathBuf};

fn add_sources(build: &mut cc::Build, root: &Path, files: &[&str]) {
    build.files(files.iter().map(|src| {
        let mut p = root.join(src);
        p.set_extension("c");
        p
    }));

    build.include(root);
}

/// The prepared FreeType and libpng sources (Unbound's dependency cache).
fn prepared_sources() -> PathBuf {
    let manifest = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let native = manifest.join("../../native");
    let prepare = native.join("freetype/source.py");
    println!("cargo:rerun-if-changed={}", prepare.display());
    println!(
        "cargo:rerun-if-changed={}",
        native.join("pinned_source.py").display()
    );
    for key in [
        "XDG_CACHE_HOME",
        "UNBOUND_FREETYPE_ARCHIVE",
        "UNBOUND_OFFLINE",
        "CARGO_NET_OFFLINE",
    ] {
        println!("cargo:rerun-if-env-changed={key}");
    }
    let output = std::process::Command::new("python3")
        .arg(&prepare)
        .output()
        .expect("running the FreeType dependency preparation");
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let root = PathBuf::from(String::from_utf8(output.stdout).unwrap().trim());
    println!("cargo:rerun-if-changed={}", root.display());
    root
}

fn main() {
    if !cfg!(feature = "bundled") {
        let target = env::var("TARGET").unwrap();
        if !target.contains("android") && !target.contains("ohos") {
            pkg_config::Config::new()
                .atleast_version("24.3.18")
                .probe("freetype2")
                .unwrap();
        }
        return;
    }

    let root = prepared_sources();
    let libpng = root.join("libpng");

    let mut build = cc::Build::new();

    build
        .warnings(false)
        .include(&root)
        .include(root.join("freetype2/include"))
        .include(&libpng)
        .define("FT2_BUILD_LIBRARY", None)
        .define("FT_CONFIG_OPTION_USE_PNG", None);

    add_sources(
        &mut build,
        &root.join("freetype2/src"),
        &[
            "autofit/autofit",
            "base/ftbase",
            "base/ftbbox",
            "base/ftbdf",
            "base/ftbitmap",
            "base/ftcid",
            "base/ftdebug",
            "base/ftfstype",
            "base/ftgasp",
            "base/ftglyph",
            "base/ftgxval",
            "base/ftinit",
            "base/ftmm",
            "base/ftotval",
            "base/ftpatent",
            "base/ftpfr",
            "base/ftstroke",
            "base/ftsynth",
            "base/ftsystem",
            "base/fttype1",
            "base/ftwinfnt",
            "bdf/bdf",
            "bzip2/ftbzip2",
            "cache/ftcache",
            "cff/cff",
            "cid/type1cid",
            "gzip/ftgzip",
            "lzw/ftlzw",
            "pcf/pcf",
            "pfr/pfr",
            "psaux/psaux",
            "pshinter/pshinter",
            "psnames/psnames",
            "raster/raster",
            "sdf/sdf",
            "svg/svg",
            "sfnt/sfnt",
            "smooth/smooth",
            "truetype/truetype",
            "type1/type1",
            "type42/type42",
            "winfonts/winfnt",
        ],
    );

    build.compile("freetype2");

    println!("cargo:rustc-link-lib=z");

    let mut build = cc::Build::new();
    // Upstream also names libz-sys/src/zlib here, a directory the crate does not contain; zlib.h
    // comes from the system's headers.
    build.include(&libpng);
    for file in [
        "png.c",
        "pngerror.c",
        "pngget.c",
        "pngmem.c",
        "pngpread.c",
        "pngread.c",
        "pngrio.c",
        "pngrtran.c",
        "pngrutil.c",
        "pngset.c",
        "pngtrans.c",
        "pngwio.c",
        "pngwrite.c",
        "pngwtran.c",
        "pngwutil.c",
    ] {
        build.file(libpng.join(file));
    }

    let arch = std::env::var("CARGO_CFG_TARGET_ARCH").unwrap();
    if arch == "arm" || arch == "aarch64" {
        for file in [
            "arm/arm_init.c",
            "arm/filter_neon_intrinsics.c",
            "arm/filter_neon.S",
            "arm/palette_neon_intrinsics.c",
        ] {
            build.file(libpng.join(file));
        }
    }

    build.compile("libpng.a");
}
