//! Imports a model (with its `.meta`) and prints its meshes as JSON: what Unbound's importer makes
//! of it, for comparing with Unity's.
//!
//! ```text
//! cargo run --release -p unbound-assets --example dump_model -- <model file>
//! ```
use anyhow::{Context, Result};
use std::path::PathBuf;

fn main() -> Result<()> {
    let path = PathBuf::from(std::env::args().nth(1).context("usage: dump_model <model file>")?);
    let bytes = std::fs::read(&path)?;
    let meta_path = PathBuf::from(format!("{}.meta", path.display()));
    let meta = unbound_yaml::parse_value(&std::fs::read_to_string(&meta_path).with_context(|| format!("reading {}", meta_path.display()))?)?;
    let model = unbound_assets::model::import_model(&path, &bytes, &meta)?;
    if std::env::args().any(|a| a == "--clips") {
        for clip in &model.clips {
            eprintln!("clip {} length {} curves {} floats {:?}", clip.name, clip.length, clip.curves.len(), clip.floats.iter().map(|f| (&f.path, &f.attribute, f.keys.len())).collect::<Vec<_>>());
        }
    }
    println!("{}", serde_json::to_string(&model.meshes)?);
    Ok(())
}
