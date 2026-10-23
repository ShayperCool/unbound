//! Expands every prefab and scene of a project: a corpus check of prefab resolution.
use std::time::Instant;
use unbound_project::Project;
use unbound_scene::DocumentLoader;

fn main() -> anyhow::Result<()> {
    let root = std::env::args()
        .nth(1)
        .expect("usage: expand_all <project>");
    let project = Project::open(std::path::Path::new(&root))?;
    let db = project.asset_db();
    let loader = DocumentLoader::new(|guid| db.by_guid(guid).map(|a| a.file.clone()));
    let start = Instant::now();
    let (mut ok, mut failed, mut objects) = (0, 0, 0);
    for asset in db.iter() {
        let ext = asset.path.rsplit('.').next().unwrap_or("");
        if ext != "prefab" && ext != "unity" {
            continue;
        }
        match loader.load(&asset.file) {
            Ok(docs) => {
                ok += 1;
                objects += docs.len();
            }
            Err(e) => {
                failed += 1;
                if failed <= 15 {
                    eprintln!("{}: {e:#}", asset.path);
                }
            }
        }
    }
    println!(
        "{ok} expanded, {failed} failed, {objects} objects in {:.2?}",
        start.elapsed()
    );
    Ok(())
}
