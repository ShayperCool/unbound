//! Prints a binary serialized asset (one the Editor wrote with type trees) as its documents:
//! `cargo run -p unbound-assets --example dump_serialized -- FILE [MAX_SEQ]`. Long sequences are cut
//! to MAX_SEQ items (default 8).
use unbound_yaml::Value;

/// `DUMP_FULL=1` prints long scalars (byte arrays) whole.
fn full() -> bool {
    std::env::var_os("DUMP_FULL").is_some()
}

fn print(value: &Value, indent: usize, max: usize) {
    let pad = " ".repeat(indent);
    match value {
        Value::Null => println!("{pad}~"),
        Value::Scalar(s) => println!("{pad}{}", if s.len() > 120 && !full() { &s[..120] } else { s }),
        Value::Seq(items) => {
            println!("{pad}[{} items]", items.len());
            for item in items.iter().take(max) {
                print(item, indent + 2, max);
            }
        }
        Value::Map(map) => {
            for (k, v) in &map.0 {
                match v {
                    Value::Scalar(s) => println!("{pad}{k}: {}", if s.len() > 120 && !full() { &s[..120] } else { s }),
                    _ => {
                        println!("{pad}{k}:");
                        print(v, indent + 2, max);
                    }
                }
            }
        }
    }
}

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let data = std::fs::read(&args[1]).expect("reading the file");
    let max = args.get(2).and_then(|m| m.parse().ok()).unwrap_or(8);
    if data.starts_with(b"UnityFS") {
        let bundle = unbound_assets::bundle::Bundle::parse(&data).expect("a bundle");
        for f in &bundle.files {
            println!("=== {} ({} bytes, flags {})", f.path, f.data.len(), f.flags);
        }
        let paths: Vec<String> = bundle.serialized_files().map(|f| f.path.clone()).collect();
        for path in paths {
            println!("=== documents of {path}");
            for doc in bundle.documents(&path).expect("documents") {
                println!("--- !u!{} &{} {}", doc.class_id, doc.file_id, doc.type_name);
                print(&doc.body, 2, max);
            }
        }
        return;
    }
    for doc in unbound_assets::serialized::documents(&data).expect("a serialized file") {
        println!("--- !u!{} &{} {}", doc.class_id, doc.file_id, doc.type_name);
        print(&doc.body, 2, max);
    }
}
