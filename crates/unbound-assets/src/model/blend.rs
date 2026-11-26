//! `.blend` files, imported as the Editor imports them (measured: macOS Editor 6000.0.71f1,
//! tests/projects/dccmodels): the installed Blender runs the installation's own export script,
//! `<blender> -b <file.blend> -P <Tools/Unity-BlenderToFBX.py> -- <out.fbx>`, and the FBX
//! importer reads the file it writes. Without Blender the file does not import (the Editor
//! needs Blender as well).
use anyhow::{Context, Result, anyhow, bail};
use std::{
    path::{Path, PathBuf},
    process::Command,
    sync::{
        OnceLock,
        atomic::{AtomicU64, Ordering},
    },
};

/// The Editor installation's Blender export script, relative to its data directory
/// (`Editor/Data` on Linux, `Unity.app/Contents` on macOS).
pub const EXPORT_SCRIPT: &str = "Tools/Unity-BlenderToFBX.py";

pub(crate) fn is_blend(source: &Path) -> bool {
    source
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("blend"))
}

/// Blender: on macOS the application (where the Editor runs it from), else `blender` on the
/// `PATH` (the Linux Editor's lookup is unmeasured).
fn blender() -> Option<PathBuf> {
    let app = Path::new("/Applications/Blender.app/Contents/MacOS/Blender");
    if cfg!(target_os = "macos") && app.is_file() {
        return Some(app.to_path_buf());
    }
    let path = std::env::var_os("PATH")?;
    std::env::split_paths(&path)
        .map(|dir| dir.join("blender"))
        .find(|candidate| candidate.is_file())
}

/// Blender's `--version` text (its version and build hash), asked once per process.
fn blender_version() -> &'static Result<String, String> {
    static VERSION: OnceLock<Result<String, String>> = OnceLock::new();
    VERSION.get_or_init(|| {
        let blender = blender().ok_or_else(|| "Blender is not installed".to_string())?;
        let out = Command::new(&blender)
            .arg("--version")
            .output()
            .map_err(|e| format!("running {}: {e}", blender.display()))?;
        if !out.status.success() {
            return Err(format!("{} --version failed", blender.display()));
        }
        Ok(String::from_utf8_lossy(&out.stdout).trim().to_string())
    })
}

/// What decides a `.blend` file's FBX besides the file: the Blender that converts it and the
/// export script's content. Empty when either is missing (the import then fails).
pub(crate) fn conversion_key(script: Option<&Path>) -> String {
    let Some(script) = script else {
        return String::new();
    };
    let Ok(version) = blender_version() else {
        return String::new();
    };
    let Ok(text) = std::fs::read(script) else {
        return String::new();
    };
    format!(
        "blender\0{version}\0{}",
        blake3::hash(&text).to_hex().as_str()
    )
}

/// The FBX that Blender writes for `source` with the export script.
pub(crate) fn convert(source: &Path, script: Option<&Path>) -> Result<Vec<u8>> {
    let script = script.ok_or_else(|| {
        anyhow!(
            "cannot import {}: the Editor installation's {EXPORT_SCRIPT} is not known",
            source.display()
        )
    })?;
    if !script.is_file() {
        bail!(
            "cannot import {}: the Editor installation has no {} (it converts .blend files with it)",
            source.display(),
            script.display()
        );
    }
    let blender = blender().ok_or_else(|| {
        anyhow!(
            "cannot import {}: Blender is not installed (the Editor imports .blend files by running Blender)",
            source.display()
        )
    })?;
    static NEXT: AtomicU64 = AtomicU64::new(0);
    let out = std::env::temp_dir().join(format!(
        "unbound-blend-{}-{}.fbx",
        std::process::id(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    ));
    let absolute = std::path::absolute(source).unwrap_or_else(|_| source.to_path_buf());
    let result = Command::new(&blender)
        .arg("-b")
        .arg(&absolute)
        .arg("-P")
        .arg(script)
        .arg("--")
        .arg(&out)
        .output()
        .with_context(|| format!("running {}", blender.display()));
    let bytes = std::fs::read(&out);
    let _ = std::fs::remove_file(&out);
    let output = result?;
    match bytes {
        Ok(bytes) if output.status.success() => Ok(bytes),
        _ => {
            let log = String::from_utf8_lossy(&output.stdout);
            let tail: Vec<&str> = log.lines().rev().take(8).collect();
            bail!(
                "cannot import {}: Blender did not convert it to FBX ({}): {}",
                source.display(),
                output.status,
                tail.into_iter().rev().collect::<Vec<_>>().join(" | ")
            )
        }
    }
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
