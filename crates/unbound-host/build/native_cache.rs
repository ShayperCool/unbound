//! Private native-link receipts. No Cargo target or build-script run is reused.
use std::{
    ffi::OsStr,
    fs, io,
    path::{Path, PathBuf},
    process::Command,
};

fn value(hash: &mut blake3::Hasher, bytes: &[u8]) {
    hash.update(&(bytes.len() as u64).to_le_bytes());
    hash.update(bytes);
}

fn file(hash: &mut blake3::Hasher, path: &Path) -> io::Result<()> {
    println!("cargo:rerun-if-changed={}", path.display());
    value(hash, path.as_os_str().as_encoded_bytes());
    value(hash, &fs::read(path)?);
    Ok(())
}

fn executable(program: &OsStr) -> Option<PathBuf> {
    let path = Path::new(program);
    if path.components().count() > 1 || path.is_absolute() {
        return path.is_file().then(|| path.to_path_buf());
    }
    std::env::split_paths(&std::env::var_os("PATH")?)
        .map(|dir| dir.join(path))
        .find(|candidate| candidate.is_file())
}

fn command(hash: &mut blake3::Hasher, compiler: &cc::Tool) -> io::Result<()> {
    let cmd = compiler.to_command();
    value(hash, cmd.get_program().as_encoded_bytes());
    file(
        hash,
        &executable(cmd.get_program()).ok_or_else(|| {
            io::Error::new(
                io::ErrorKind::NotFound,
                format!("compiler not found: {:?}", cmd.get_program()),
            )
        })?,
    )?;
    for arg in cmd.get_args() {
        value(hash, arg.as_encoded_bytes());
    }
    // to_command also snapshots the entire inherited process environment.
    // Cargo's per-run jobserver descriptors are not compiler inputs. Hash the
    // tool's explicit overrides here and the compiler's ambient inputs below.
    for (name, val) in compiler.get_envs() {
        value(hash, name.as_encoded_bytes());
        value(hash, val.as_encoded_bytes());
    }
    Ok(())
}

fn compiler_command(compiler: &cc::Tool) -> Command {
    // cc::Tool's path/args/env describe the compiler underneath a recognized
    // compiler-cache wrapper. Introspection/preprocessing has no cached outputs,
    // so do not start kache's engine nineteen times for these queries.
    let mut command = Command::new(compiler.path());
    command.args(compiler.args()).envs(compiler.get_envs());
    command
}

/// Hash the compiler's preprocessed inputs, actual SDK archives and toolchain.
/// Preprocessing also covers transitive/system headers, macros and include order.
/// This receipt does not change the existing SDK archive cache's input policy.
pub fn key(
    compilers: &[(&cc::Tool, &Path)],
    archives: &[&Path],
    library: &Path,
) -> io::Result<Option<blake3::Hash>> {
    if compilers
        .iter()
        .any(|(compiler, _)| compiler.is_like_msvc())
    {
        return Ok(None); // The supported Unix platforms use GCC/Clang preprocessing.
    }
    let mut hash = blake3::Hasher::new();
    value(&mut hash, b"unbound-native-link-v1");
    value(&mut hash, include_bytes!("../build.rs"));
    value(&mut hash, include_bytes!("native_cache.rs"));
    value(&mut hash, library.as_os_str().as_encoded_bytes());
    value(
        &mut hash,
        std::env::current_dir()?.as_os_str().as_encoded_bytes(),
    );
    for name in [
        "HOST",
        "TARGET",
        "PATH",
        "CPATH",
        "CPLUS_INCLUDE_PATH",
        "LIBRARY_PATH",
        "COMPILER_PATH",
        "GCC_EXEC_PREFIX",
        "SDKROOT",
        "MACOSX_DEPLOYMENT_TARGET",
        "LD_LIBRARY_PATH",
        "DYLD_LIBRARY_PATH",
        "SOURCE_DATE_EPOCH",
        "DEVELOPER_DIR",
        "TOOLCHAINS",
        "CCC_OVERRIDE_OPTIONS",
        "CLANG_NO_DEFAULT_CONFIG",
    ] {
        println!("cargo:rerun-if-env-changed={name}");
        value(&mut hash, name.as_bytes());
        value(
            &mut hash,
            std::env::var_os(name)
                .as_deref()
                .map(OsStr::as_encoded_bytes)
                .unwrap_or_default(),
        );
    }
    for archive in archives {
        file(&mut hash, archive)?;
    }
    for (compiler, source) in compilers {
        command(&mut hash, compiler)?;
        // cc::Tool may wrap the actual compiler in kache/sccache.
        file(
            &mut hash,
            &executable(compiler.path().as_os_str())
                .ok_or_else(|| io::Error::new(io::ErrorKind::NotFound, "C++ compiler not found"))?,
        )?;
        let output = compiler_command(compiler).arg("-E").arg(source).output()?;
        if !output.status.success() {
            return Err(io::Error::other(format!(
                "preprocessing {} failed: {}",
                source.display(),
                String::from_utf8_lossy(&output.stderr)
            )));
        }
        value(&mut hash, &output.stdout);
        // Preserve Cargo invalidation for transitive headers outside native/.
        // GCC/Clang emit line markers even when preprocessing to stdout.
        let mut headers = std::collections::BTreeSet::new();
        for line in String::from_utf8_lossy(&output.stdout).lines() {
            if let Some(marker) = line.strip_prefix("# ") {
                if let Some((_, tail)) = marker.split_once('"') {
                    if let Some((path, _)) = tail.rsplit_once('"') {
                        let path = PathBuf::from(path.replace("\\\"", "\"").replace("\\\\", "\\"));
                        if path.is_file() {
                            headers.insert(path);
                        }
                    }
                }
            }
        }
        for path in headers {
            println!("cargo:rerun-if-changed={}", path.display());
        }
    }
    let compiler = compilers[0].0;
    // A driver can stay unchanged while its backend, linker or startup objects
    // are replaced. Hash the paths it selects and their actual contents too.
    for query in [
        "--version",
        "-print-prog-name=cc1plus",
        "-print-prog-name=collect2",
        "-print-prog-name=as",
        "-print-prog-name=ld",
        "-print-prog-name=ld.lld",
        "-print-prog-name=ld.gold",
        "-print-file-name=crtbeginS.o",
        "-print-file-name=crtendS.o",
        "-print-file-name=crti.o",
        "-print-file-name=crtn.o",
        "-print-file-name=libstdc++.so",
        "-print-file-name=libgcc_s.so",
        "-print-file-name=libgcc.a",
        "-print-file-name=libgcc_eh.a",
        "-print-file-name=libc.so",
        "-print-file-name=libpthread.a",
        "-print-file-name=libdl.a",
        "-print-file-name=libm.so",
    ] {
        let output = compiler_command(compiler).arg(query).output()?;
        value(&mut hash, query.as_bytes());
        value(&mut hash, &output.stdout);
        value(&mut hash, &output.stderr);
        value(&mut hash, output.status.to_string().as_bytes());
        if query != "--version" && output.status.success() {
            if let Ok(path) = std::str::from_utf8(&output.stdout) {
                if let Some(path) = executable(OsStr::new(path.trim())) {
                    file(&mut hash, &path)?;
                }
            }
        }
    }
    Ok(Some(hash.finalize()))
}

pub fn valid(receipt: &Path, key: &blake3::Hash, library: &Path) -> bool {
    let (Ok(receipt), Ok(bytes)) = (fs::read_to_string(receipt), fs::read(library)) else {
        return false;
    };
    receipt == format!("{key}\n{}\n", blake3::hash(&bytes))
}

pub fn record(receipt: &Path, key: &blake3::Hash, library: &Path) -> io::Result<()> {
    let contents = format!("{key}\n{}\n", blake3::hash(&fs::read(library)?));
    let temporary = receipt.with_extension(format!("tmp-{}", std::process::id()));
    fs::write(&temporary, contents)?;
    fs::rename(temporary, receipt)
}
