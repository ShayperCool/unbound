//! `.blend` files, imported as the Editor imports them (measured: macOS Editor 6000.0.71f1,
//! tests/projects/dccmodels): the installed Blender runs the installation's own export script,
//! `<blender> -b <file.blend> -P <Tools/Unity-BlenderToFBX.py> -- <out.fbx>`, and the FBX
//! importer reads the file it writes. Without Blender the file does not import (the Editor
//! needs Blender as well).
use anyhow::{Context, Result, anyhow, bail};
use std::{
    io::Read,
    path::{Path, PathBuf},
    process::{Command, ExitStatus, Stdio},
    sync::{
        OnceLock,
        atomic::{AtomicU64, Ordering},
    },
    time::{Duration, Instant},
};

/// The Editor installation's Blender export script, relative to its data directory
/// (`Editor/Data` on Linux, `Unity.app/Contents` on macOS).
pub const EXPORT_SCRIPT: &str = "Tools/Unity-BlenderToFBX.py";

pub(crate) fn is_blend(source: &Path) -> bool {
    source
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("blend"))
}

/// How long one conversion may take before Unbound stops Blender (a scene that big is not
/// expected; a hung Blender would otherwise stall the import without a word).
const CONVERSION_LIMIT: Duration = Duration::from_secs(600);

/// The Blender that converts: its executable and its `--version` text (version and build hash).
struct Blender {
    path: PathBuf,
    version: String,
}

/// Blender, found and asked its version once per process: on macOS the application (where the
/// Editor runs it from), else `blender` on the `PATH` (unmeasured: the first conversion says so).
fn installed() -> &'static Result<Blender, String> {
    static BLENDER: OnceLock<Result<Blender, String>> = OnceLock::new();
    BLENDER.get_or_init(|| {
        let app = Path::new("/Applications/Blender.app/Contents/MacOS/Blender");
        let path = if cfg!(target_os = "macos") && app.is_file() {
            app.to_path_buf()
        } else {
            std::env::var_os("PATH")
                .and_then(|path| {
                    std::env::split_paths(&path)
                        .map(|dir| dir.join("blender"))
                        .find(|candidate| candidate.is_file())
                })
                .ok_or_else(|| "Blender is not installed".to_string())?
        };
        let out = Command::new(&path)
            .arg("--version")
            .output()
            .map_err(|e| format!("running {}: {e}", path.display()))?;
        if !out.status.success() {
            return Err(format!("{} --version failed ({})", path.display(), out.status));
        }
        let version = String::from_utf8_lossy(&out.stdout).trim().to_string();
        if path != app {
            tracing::warn!(
                "converting .blend files with {} ({}), found on the PATH: whether the Editor finds Blender there is unmeasured",
                path.display(),
                version.lines().next().unwrap_or("")
            );
        }
        Ok(Blender { path, version })
    })
}

/// What decides a `.blend` file's FBX besides the file: the Blender that converts it and the
/// export script's content. Empty when either is missing (the import then fails).
pub(crate) fn conversion_key(script: Option<&Path>) -> String {
    let (Some(script), Ok(blender)) = (script, installed()) else {
        return String::new();
    };
    if let Err(e) = fetch_script(script) {
        tracing::warn!("{e:#}");
        return String::new();
    }
    let Ok(text) = std::fs::read(script) else {
        return String::new();
    };
    format!(
        "blender\0{}\0{}",
        blender.version,
        blake3::hash(&text).to_hex().as_str()
    )
}

/// Fetches the export script first when the Editor files hold it only as a file still to fetch
/// (a cache tree linked from an earlier unpack set, `unbound_project::editor_archive::ensure`).
fn fetch_script(script: &Path) -> Result<()> {
    match script.parent().and_then(Path::parent) {
        Some(data) if script.ends_with(EXPORT_SCRIPT) => {
            unbound_project::editor_archive::ensure(data, EXPORT_SCRIPT)
        }
        _ => Ok(()),
    }
}

/// The FBX that Blender writes for `source` with the export script. A conversion is refused
/// when what identifies it in the cache (see [`conversion_key`]) is not known.
pub(crate) fn convert(source: &Path, script: Option<&Path>) -> Result<Vec<u8>> {
    let script = script.ok_or_else(|| {
        anyhow!(
            "cannot import {}: the Editor installation's {EXPORT_SCRIPT} is not known",
            source.display()
        )
    })?;
    fetch_script(script).with_context(|| format!("cannot import {}", source.display()))?;
    if !script.is_file() {
        bail!(
            "cannot import {}: the Editor installation has no {} (it converts .blend files with it)",
            source.display(),
            script.display()
        );
    }
    let blender = installed().as_ref().map_err(|e| {
        anyhow!(
            "cannot import {}: {e} (the Editor imports .blend files by running Blender)",
            source.display()
        )
    })?;
    if conversion_key(Some(script)).is_empty() {
        bail!(
            "cannot import {}: {} is not readable",
            source.display(),
            script.display()
        );
    }
    static NEXT: AtomicU64 = AtomicU64::new(0);
    let out = std::env::temp_dir().join(format!(
        "unbound-blend-{}-{}.fbx",
        std::process::id(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    ));
    let absolute = std::path::absolute(source).unwrap_or_else(|_| source.to_path_buf());
    let result = run(
        Command::new(&blender.path)
            .arg("-b")
            .arg(&absolute)
            .arg("-P")
            .arg(script)
            .arg("--")
            .arg(&out),
        source,
    );
    let bytes = std::fs::read(&out);
    let _ = std::fs::remove_file(&out);
    let (status, log) = result?;
    match bytes {
        Ok(bytes) if status.success() => Ok(bytes),
        _ => {
            let tail: Vec<&str> = log.lines().rev().take(8).collect();
            bail!(
                "cannot import {}: Blender did not convert it to FBX ({status}): {}",
                source.display(),
                tail.into_iter().rev().collect::<Vec<_>>().join(" | ")
            )
        }
    }
}

/// Runs Blender, its output kept, stopping it after [`CONVERSION_LIMIT`].
fn run(command: &mut Command, source: &Path) -> Result<(ExitStatus, String)> {
    let mut child = command
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("running Blender")?;
    let readers: Vec<_> = [
        child
            .stdout
            .take()
            .map(|s| Box::new(s) as Box<dyn Read + Send>),
        child
            .stderr
            .take()
            .map(|s| Box::new(s) as Box<dyn Read + Send>),
    ]
    .into_iter()
    .flatten()
    .map(|mut stream| {
        std::thread::spawn(move || {
            let mut text = Vec::new();
            let _ = stream.read_to_end(&mut text);
            text
        })
    })
    .collect();
    let started = Instant::now();
    let status = loop {
        if let Some(status) = child.try_wait()? {
            break status;
        }
        if started.elapsed() > CONVERSION_LIMIT {
            let _ = child.kill();
            let _ = child.wait();
            bail!(
                "cannot import {}: Blender did not finish converting it within {} s",
                source.display(),
                CONVERSION_LIMIT.as_secs()
            );
        }
        std::thread::sleep(Duration::from_millis(20));
    };
    let mut log = String::new();
    for reader in readers {
        log.push_str(&String::from_utf8_lossy(&reader.join().unwrap_or_default()));
    }
    Ok((status, log))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_blend_files_are_converted() {
        assert!(is_blend(Path::new("Assets/Scene.blend")));
        assert!(is_blend(Path::new("Assets/Scene.BLEND")));
        assert!(!is_blend(Path::new("Assets/Scene.fbx")));
        assert!(!is_blend(Path::new("Assets/Scene.blend1")));
    }

    #[test]
    fn a_blend_file_without_the_export_script_fails_loudly() {
        let missing = Path::new("/nonexistent/Tools/Unity-BlenderToFBX.py");
        let error = convert(Path::new("Assets/Scene.blend"), Some(missing)).unwrap_err();
        assert!(
            format!("{error}").contains("Unity-BlenderToFBX.py"),
            "{error}"
        );
        let error = convert(Path::new("Assets/Scene.blend"), None).unwrap_err();
        assert!(format!("{error}").contains("not known"), "{error}");
        // Without the script the key is empty: such an entry is never made.
        assert_eq!(conversion_key(None), "");
        assert_eq!(conversion_key(Some(missing)), "");
    }
}
