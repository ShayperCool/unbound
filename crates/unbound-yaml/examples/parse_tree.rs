//! Parses every Unity YAML file under a directory and reports failures: a corpus check.
use std::time::Instant;

fn main() {
    let root = std::env::args().nth(1).expect("usage: parse_tree <dir>");
    let start = Instant::now();
    let (mut ok, mut failed, mut bytes, mut docs) = (0usize, 0usize, 0usize, 0usize);
    for entry in walkdir::WalkDir::new(&root)
        .into_iter()
        .filter_map(Result::ok)
    {
        if !entry.file_type().is_file() {
            continue;
        }
        let path = entry.path();
        let Ok(text) = std::fs::read_to_string(path) else {
            continue;
        };
        let result = if text.starts_with("%YAML") {
            unbound_yaml::parse_documents(&text).map(|d| docs += d.len())
        } else if path.extension().is_some_and(|e| e == "meta") {
            unbound_yaml::parse_value(&text).map(|_| docs += 1)
        } else {
            continue;
        };
        bytes += text.len();
        match result {
            Ok(()) => ok += 1,
            Err(error) => {
                failed += 1;
                if failed <= 20 {
                    eprintln!("{}: {error:#}", path.display());
                }
            }
        }
    }
    println!(
        "{ok} ok, {failed} failed, {docs} documents, {:.1} MB in {:.2?}",
        bytes as f64 / 1e6,
        start.elapsed()
    );
}
