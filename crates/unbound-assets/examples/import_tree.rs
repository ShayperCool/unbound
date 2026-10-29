//! Imports every texture, audio clip and model under a directory, twice: a first pass that
//! imports what the cache does not have, and a second pass with a fresh cache instance (as a new
//! process would) that should be served entirely from the cache.
//!
//! ```text
//! cargo run --release -p unbound-assets --example import_tree -- <dir> [--cache <dir>] [--target <build target>]
//! ```
//!
//! The cache defaults to `<project>/Library/Unbound/Imports` when `<dir>` is a project's `Assets`
//! folder. Like Unity, hidden names (`.x`, `x~`, `cvs`, `*.tmp`) are skipped, and so is
//! `Assets/StreamingAssets`, whose files Unity copies verbatim instead of importing.
use anyhow::{Context, Result, bail};
use rayon::prelude::*;
use std::{
    path::{Path, PathBuf},
    sync::Mutex,
    time::Instant,
};
use unbound_assets::{
    AssetCache, AssetKind, AudioImporter, ModelImporter, Outcome, TextureImporter,
};

fn is_hidden(name: &str) -> bool {
    name.starts_with('.')
        || name.ends_with('~')
        || name.eq_ignore_ascii_case("cvs")
        || name.ends_with(".tmp")
}

#[derive(Default)]
struct Stats {
    textures: usize,
    sprites: usize,
    resized: usize,
    texture_bytes: u64,
    audio: usize,
    audio_seconds: f64,
    models: usize,
    meshes: usize,
    clips: usize,
    clip_curves: usize,
    clip_keys: usize,
    hits: usize,
    imported: usize,
    failures: Vec<(PathBuf, String)>,
    warnings: Vec<(PathBuf, String)>,
}

fn main() -> Result<()> {
    let mut args = std::env::args().skip(1);
    let mut dir = None;
    let mut cache_dir = None;
    let mut target = "Standalone".to_string();
    while let Some(arg) = args.next() {
        match arg.as_str() {
            "--cache" => cache_dir = Some(PathBuf::from(args.next().context("--cache <dir>")?)),
            "--target" => target = args.next().context("--target <build target>")?,
            _ if dir.is_none() => dir = Some(PathBuf::from(arg)),
            _ => bail!("usage: import_tree <dir> [--cache <dir>] [--target <build target>]"),
        }
    }
    let dir = dir
        .context("usage: import_tree <dir> [--cache <dir>] [--target <build target>]")?
        .canonicalize()?;
    let cache_dir = match cache_dir {
        Some(dir) => dir,
        None if dir.file_name().is_some_and(|n| n == "Assets") => {
            dir.parent().unwrap().join("Library/Unbound/Imports")
        }
        None => bail!(
            "{} is not an Assets folder: pass --cache <dir>",
            dir.display()
        ),
    };

    let streaming = dir.join("StreamingAssets");
    let mut files = Vec::new();
    let mut skipped_streaming = 0;
    let walker = walkdir::WalkDir::new(&dir)
        .follow_links(true)
        .into_iter()
        .filter_entry(|e| e.depth() == 0 || !is_hidden(&e.file_name().to_string_lossy()));
    for entry in walker.flatten() {
        if !entry.file_type().is_file() {
            continue;
        }
        let Some(kind) = AssetKind::from_path(entry.path()) else {
            continue;
        };
        if entry.path().starts_with(&streaming) {
            skipped_streaming += 1;
            continue;
        }
        files.push((kind, entry.into_path()));
    }
    println!(
        "{} assets under {} ({} in StreamingAssets skipped); cache {}",
        files.len(),
        dir.display(),
        skipped_streaming,
        cache_dir.display()
    );

    for pass in ["first pass", "second pass (fresh cache instance)"] {
        let cache = AssetCache::new(&cache_dir);
        let start = Instant::now();
        let stats = run(&cache, &files, &target);
        cache.save()?;
        let elapsed = start.elapsed();
        println!("\n== {pass}: {:.2?}", elapsed);
        println!(
            "  textures {} ({} sprites, {} resized, {:.1} MiB of pixels incl. mips), audio {} ({:.1} s), models {} ({} meshes, {} clips animating {} nodes with {} keys)",
            stats.textures,
            stats.sprites,
            stats.resized,
            stats.texture_bytes as f64 / (1 << 20) as f64,
            stats.audio,
            stats.audio_seconds,
            stats.models,
            stats.meshes,
            stats.clips,
            stats.clip_curves,
            stats.clip_keys,
        );
        println!(
            "  cache hits {}, imported {}, failures {}",
            stats.hits,
            stats.imported,
            stats.failures.len()
        );
        for (path, error) in &stats.failures {
            println!("  FAILED {}: {error}", relative(&dir, path));
        }
        for (path, warning) in &stats.warnings {
            println!("  warning {}: {warning}", relative(&dir, path));
        }
    }
    Ok(())
}

fn relative(dir: &Path, path: &Path) -> String {
    path.strip_prefix(dir).unwrap_or(path).display().to_string()
}

fn run(cache: &AssetCache, files: &[(AssetKind, PathBuf)], target: &str) -> Stats {
    let textures = TextureImporter {
        build_target: target.to_string(),
        compress: true,
        gamma: false,
    };
    let audio = AudioImporter {
        build_target: target.to_string(),
    };
    let stats = Mutex::new(Stats::default());
    files.par_iter().for_each(|(kind, path)| {
        let meta = PathBuf::from(format!("{}.meta", path.display()));
        let result: Result<Outcome> = (|| {
            Ok(match kind {
                AssetKind::Texture => {
                    let (tex, outcome) = cache.get_or_import_traced(&textures, path, &meta)?;
                    let mut s = stats.lock().unwrap();
                    s.textures += 1;
                    s.sprites += tex.sprites.len();
                    s.resized += usize::from(
                        (tex.width, tex.height) != (tex.original_width, tex.original_height),
                    );
                    s.texture_bytes += tex.mips.iter().map(|m| m.data.len() as u64).sum::<u64>();
                    outcome
                }
                AssetKind::Audio => {
                    let (clip, outcome) = cache.get_or_import_traced(&audio, path, &meta)?;
                    let mut s = stats.lock().unwrap();
                    s.audio += 1;
                    s.audio_seconds += clip.length();
                    outcome
                }
                AssetKind::Model => {
                    let (model, outcome) =
                        cache.get_or_import_traced(&ModelImporter, path, &meta)?;
                    let mut s = stats.lock().unwrap();
                    s.models += 1;
                    s.meshes += model.meshes.len();
                    s.clips += model.clips.len();
                    for clip in &model.clips {
                        s.clip_curves += clip.curves.len();
                        s.clip_keys += clip
                            .curves
                            .iter()
                            .map(|c| c.positions.len() + c.rotations.len() + c.scales.len())
                            .sum::<usize>();
                    }
                    for warning in &model.warnings {
                        s.warnings.push((path.clone(), warning.clone()));
                    }
                    outcome
                }
            })
        })();
        let mut s = stats.lock().unwrap();
        match result {
            Ok(Outcome::Hit) => s.hits += 1,
            Ok(Outcome::Imported) => s.imported += 1,
            Err(e) => s.failures.push((path.clone(), format!("{e:#}"))),
        }
    });
    let mut stats = stats.into_inner().unwrap();
    stats.failures.sort();
    stats.warnings.sort();
    stats
}
