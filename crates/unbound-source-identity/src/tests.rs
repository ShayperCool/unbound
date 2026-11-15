use super::*;

/// A workspace below a temporary root: (path below the root, text).
struct Workspace(PathBuf);

impl Workspace {
    fn new(name: &str, files: &[(&str, &str)]) -> Workspace {
        let root = std::env::temp_dir().join(format!(
            "unbound-source-identity-{name}-{}",
            std::process::id()
        ));
        let _ = std::fs::remove_dir_all(&root);
        for (path, text) in files {
            let path = root.join(path);
            std::fs::create_dir_all(path.parent().unwrap()).unwrap();
            std::fs::write(path, text).unwrap();
        }
        if !root.join("Cargo.toml").is_file() {
            std::fs::write(root.join("Cargo.toml"), "[workspace]\n").unwrap();
        }
        std::fs::write(root.join("Cargo.lock"), "version = 4\n").unwrap();
        Workspace(root)
    }

    fn write(&self, path: &str, text: &str) {
        let path = self.0.join(path);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, text).unwrap();
    }

    /// The files of the output `start` makes, below the root, `/`-separated.
    fn sources(&self, start: &str) -> Vec<String> {
        let (files, _) = producing_sources(&self.0, start).unwrap();
        files
            .iter()
            .map(|p| {
                p.strip_prefix(lexical(&self.0))
                    .unwrap_or(p)
                    .to_string_lossy()
                    .replace('\\', "/")
            })
            .collect()
    }

    fn identity(&self, start: &str) -> String {
        let (files, _) = producing_sources(&self.0, start).unwrap();
        identity(&self.0, &files).unwrap()
    }
}

impl Drop for Workspace {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

/// A compiler crate depending on a rules crate; an unrelated crate beside them.
fn workspace(name: &str, compiler_manifest: &str) -> Workspace {
    Workspace::new(
        name,
        &[
            ("crates/unbound-compiler/Cargo.toml", compiler_manifest),
            (
                "crates/unbound-compiler/src/lib.rs",
                "pub fn compile() {}\n",
            ),
            (
                "crates/unbound-rules/Cargo.toml",
                "[package]\nname = \"unbound-rules\"\n",
            ),
            ("crates/unbound-rules/src/lib.rs", "mod audio;\n"),
            ("crates/unbound-rules/src/audio.rs", "pub struct Audio;\n"),
            (
                "crates/unbound-other/Cargo.toml",
                "[package]\nname = \"unbound-other\"\n",
            ),
            ("crates/unbound-other/src/lib.rs", "pub struct Other;\n"),
        ],
    )
}

const DEPENDS: &str =
    "[package]\nname = \"unbound-compiler\"\n[dependencies]\nunbound-rules.workspace = true\n";

/// The crate and the crates its manifest depends on count whole; an edit of one changes the
/// identity, an edit of a crate it does not depend on does not.
#[test]
fn the_crate_and_its_dependencies_count_whole() {
    let ws = workspace("depends", DEPENDS);
    let sources = ws.sources("unbound-compiler");
    assert!(sources.contains(&"crates/unbound-rules/src/audio.rs".to_string()));
    assert!(sources.contains(&"crates/unbound-compiler/Cargo.toml".to_string()));
    assert!(
        !sources
            .iter()
            .any(|s| s.starts_with("crates/unbound-other"))
    );
    let before = ws.identity("unbound-compiler");
    ws.write("crates/unbound-other/src/lib.rs", "pub struct Other(u8);\n");
    assert_eq!(ws.identity("unbound-compiler"), before);
    ws.write(
        "crates/unbound-rules/src/audio.rs",
        "pub struct Audio(u8);\n",
    );
    assert_ne!(ws.identity("unbound-compiler"), before);
    // Dependencies of dependencies count too.
    ws.write(
        "crates/unbound-rules/Cargo.toml",
        "[package]\nname = \"unbound-rules\"\n[dependencies]\nunbound-other = { path = \"../unbound-other\" }\n",
    );
    assert!(
        ws.sources("unbound-compiler")
            .contains(&"crates/unbound-other/src/lib.rs".to_string())
    );
}

/// A dependency under another name, here or in the workspace's manifest, counts.
#[test]
fn renamed_dependencies_count() {
    let audio = "crates/unbound-rules/src/audio.rs".to_string();
    let ws = workspace(
        "renamed",
        "[package]\nname = \"unbound-compiler\"\n[dependencies]\nrules = { package = \"unbound-rules\", path = \"../unbound-rules\" }\n",
    );
    assert!(ws.sources("unbound-compiler").contains(&audio));
    let ws = workspace(
        "workspace-renamed",
        "[package]\nname = \"unbound-compiler\"\n[dependencies]\nrules.workspace = true\n",
    );
    ws.write(
        "Cargo.toml",
        "[workspace]\n[workspace.dependencies]\nrules = { package = \"unbound-rules\", path = \"crates/unbound-rules\" }\n",
    );
    assert!(ws.sources("unbound-compiler").contains(&audio));
}

/// Every file of a crate counts (its build script, examples, data), hidden ones do not; a file
/// outside the crates that a string of their code names counts.
#[test]
fn files_of_the_crates_and_the_files_they_name() {
    let ws = workspace("files", DEPENDS);
    ws.write("crates/unbound-rules/build.rs", "fn main() {}\n");
    ws.write("crates/unbound-rules/examples/demo.rs", "fn main() {}\n");
    ws.write("crates/unbound-rules/.DS_Store", "x");
    ws.write("data/table.json", "[1]");
    ws.write(
        "crates/unbound-rules/src/audio.rs",
        "pub struct Audio;\nconst T: &str = include_str!(\"../../../data/table.json\");\n",
    );
    let sources = ws.sources("unbound-compiler");
    for file in [
        "crates/unbound-rules/build.rs",
        "crates/unbound-rules/examples/demo.rs",
        "data/table.json",
    ] {
        assert!(sources.contains(&file.to_string()), "{file}");
    }
    assert!(!sources.contains(&"crates/unbound-rules/.DS_Store".to_string()));
    let before = ws.identity("unbound-compiler");
    ws.write("data/table.json", "[2]");
    assert_ne!(ws.identity("unbound-compiler"), before);
    // Also relative to the crate (`concat!(env!("CARGO_MANIFEST_DIR"), "/../../data/x")`).
    ws.write("data/other.json", "[3]");
    ws.write(
        "crates/unbound-rules/src/audio.rs",
        "pub struct Audio;\nconst T: &str = include_str!(concat!(env!(\"CARGO_MANIFEST_DIR\"), \"/../../data/other.json\"));\n",
    );
    assert!(
        ws.sources("unbound-compiler")
            .contains(&"data/other.json".to_string())
    );
}

/// String literals: comments skipped; raw, byte and escaped strings read; character literals
/// and lifetimes do not open strings.
#[test]
fn string_literals_are_read_as_written() {
    let text = "// \"not\"\nfn f<'a>(x: &'a str) { let q = '\"'; let e = ['\\'','\"']; let a = \"one\\\"two\"; let b = br#\"three\"#; let c = r\"four\"; }\n";
    assert_eq!(
        string_literals(text),
        ["one\"two", "three", "four"].map(String::from)
    );
}

/// The shader compiler of this workspace depends on the device rules (`unbound-graphics`), not
/// on the rest of `unbound-compat`.
#[test]
fn the_shader_compiler_depends_on_the_device_rules_only() {
    let root = lexical(&Path::new(env!("CARGO_MANIFEST_DIR")).join("../.."));
    let (files, _) = producing_sources(&root, "unbound-shaders").unwrap();
    assert!(files.contains(&root.join("crates/unbound-graphics/src/lib.rs")));
    assert!(
        !files
            .iter()
            .any(|f| f.starts_with(root.join("crates/unbound-compat")))
    );
}

/// A dependency of the workspace's manifest written as a table of its own counts.
#[test]
fn workspace_dependency_tables_count() {
    let ws = workspace(
        "table",
        "[package]\nname = \"unbound-compiler\"\n[dependencies]\nrules.workspace = true\n",
    );
    ws.write(
        "Cargo.toml",
        "[workspace]\n[workspace.dependencies.rules]\npackage = \"unbound-rules\"\npath = \"crates/unbound-rules\"\n",
    );
    assert!(
        ws.sources("unbound-compiler")
            .contains(&"crates/unbound-rules/src/audio.rs".to_string())
    );
}
