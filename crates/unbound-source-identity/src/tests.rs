use super::*;

/// A workspace of crates below a temporary root: (path below the root, text).
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
        std::fs::write(root.join("Cargo.lock"), "version = 4\n").unwrap();
        Workspace(root)
    }

    fn write(&self, path: &str, text: &str) {
        std::fs::write(self.0.join(path), text).unwrap();
    }

    /// The files reached, below the root, as `/`-separated strings.
    fn sources(&self, start: &str, modules: &[&str]) -> Vec<String> {
        let (files, _) = producing_sources(&self.0, start, modules).unwrap();
        files
            .iter()
            .map(|p| {
                p.strip_prefix(&self.0)
                    .unwrap()
                    .to_string_lossy()
                    .replace('\\', "/")
            })
            .collect()
    }

    fn identity(&self, start: &str, modules: &[&str]) -> String {
        let (files, _) = producing_sources(&self.0, start, modules).unwrap();
        identity(&self.0, &files, &[]).unwrap()
    }
}

impl Drop for Workspace {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

const RULES_ROOT: &str = "//! Rules.\nmod device;\nmod audio;\nmod version;\n\
    pub use device::{Device, select as select_device};\npub use audio::Audio;\n\
    pub use version::Version;\n";

/// A compiler crate naming one module of a rules crate through its root's re-exports.
fn compiler_and_rules(name: &str) -> Workspace {
    Workspace::new(
        name,
        &[
            ("crates/unbound-compiler/Cargo.toml", "[package]"),
            (
                "crates/unbound-compiler/src/lib.rs",
                "mod fit;\npub fn compile(d: unbound_rules::Device) {}\n",
            ),
            (
                "crates/unbound-compiler/src/fit.rs",
                "use unbound_rules::{Device, select_device};\n",
            ),
            ("crates/unbound-rules/Cargo.toml", "[package]"),
            ("crates/unbound-rules/src/lib.rs", RULES_ROOT),
            (
                "crates/unbound-rules/src/device.rs",
                "use crate::version::Version;\npub enum Device { A }\npub fn select() {}\n",
            ),
            (
                "crates/unbound-rules/src/version.rs",
                "pub struct Version;\n",
            ),
            ("crates/unbound-rules/src/audio.rs", "pub struct Audio;\n"),
        ],
    )
}

/// What the compiler names of the rules crate is reached, through re-exports (also renamed
/// ones) and `crate::` paths; a module it does not reach is not, and leaves the identity alone.
#[test]
fn only_the_modules_named_count() {
    let ws = compiler_and_rules("named");
    assert_eq!(
        ws.sources("unbound_compiler", &[]),
        [
            "crates/unbound-compiler/Cargo.toml",
            "crates/unbound-compiler/src/fit.rs",
            "crates/unbound-compiler/src/lib.rs",
            "crates/unbound-rules/Cargo.toml",
            "crates/unbound-rules/src/device.rs",
            "crates/unbound-rules/src/lib.rs",
            "crates/unbound-rules/src/version.rs",
        ]
    );
    let before = ws.identity("unbound_compiler", &[]);
    ws.write(
        "crates/unbound-rules/src/audio.rs",
        "pub struct Audio { loud: bool }\n",
    );
    assert_eq!(ws.identity("unbound_compiler", &[]), before);
    ws.write(
        "crates/unbound-rules/src/version.rs",
        "pub struct Version(u32);\n",
    );
    assert_ne!(ws.identity("unbound_compiler", &[]), before);
}

/// A module that implements a type reached is reached too.
#[test]
fn modules_implementing_a_reached_type_count() {
    let ws = compiler_and_rules("impl");
    ws.write(
        "crates/unbound-rules/src/audio.rs",
        "pub struct Audio;\nimpl crate::Device { pub fn audio(&self) {} }\n",
    );
    assert!(
        ws.sources("unbound_compiler", &[])
            .contains(&"crates/unbound-rules/src/audio.rs".to_string())
    );
}

/// What the reading cannot follow counts the whole crate: a glob, a name nowhere defined, a
/// `#[path]` module.
#[test]
fn an_unsure_reading_counts_the_whole_crate() {
    let audio = "crates/unbound-rules/src/audio.rs".to_string();
    for (case, fit) in [
        ("glob", "use unbound_rules::*;\n"),
        ("unknown", "use unbound_rules::Missing;\n"),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-compiler/src/fit.rs", fit);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
    let ws = compiler_and_rules("path");
    ws.write(
        "crates/unbound-rules/src/device.rs",
        "#[path = \"elsewhere.rs\"]\nmod inner;\npub enum Device { A }\npub fn select() {}\n",
    );
    assert!(ws.sources("unbound_compiler", &[]).contains(&audio));
}

/// Starting from modules of a crate, its other modules count only when named; a root with code
/// of its own counts the whole crate; files it includes count.
#[test]
fn modules_of_a_crate_and_what_they_include() {
    let ws = Workspace::new(
        "modules",
        &[
            ("crates/unbound-assets/Cargo.toml", "[package]"),
            (
                "crates/unbound-assets/src/lib.rs",
                "mod gen;\nmod parse;\nmod other;\npub use parse::VERSION;\n",
            ),
            (
                "crates/unbound-assets/src/gen.rs",
                "use crate::parse::File;\nconst T: &str = include_str!(\"../data/t.txt\");\n\
                 fn v() -> u32 { crate::VERSION }\n",
            ),
            (
                "crates/unbound-assets/src/parse.rs",
                "pub struct File;\npub const VERSION: u32 = 1;\n",
            ),
            ("crates/unbound-assets/src/other.rs", "pub struct Other;\n"),
            ("crates/unbound-assets/data/t.txt", "template"),
        ],
    );
    assert_eq!(
        ws.sources("unbound_assets", &["gen"]),
        [
            "crates/unbound-assets/Cargo.toml",
            "crates/unbound-assets/data/t.txt",
            "crates/unbound-assets/src/gen.rs",
            "crates/unbound-assets/src/lib.rs",
            "crates/unbound-assets/src/parse.rs",
        ]
    );
    let before = ws.identity("unbound_assets", &["gen"]);
    ws.write("crates/unbound-assets/data/t.txt", "another template");
    assert_ne!(ws.identity("unbound_assets", &["gen"]), before);
    // A root with code of its own is not read apart: the whole crate counts.
    ws.write(
        "crates/unbound-assets/src/lib.rs",
        "mod gen;\nmod parse;\nmod other;\npub use parse::VERSION;\npub fn helper() {}\n",
    );
    assert!(
        ws.sources("unbound_assets", &["gen"])
            .contains(&"crates/unbound-assets/src/other.rs".to_string())
    );
}

/// The shader compiler of this workspace reaches the device rules of `unbound-compat`, not its
/// audio, texture or profile rules.
#[test]
fn the_shader_compiler_reaches_the_rules_it_compiles_by() {
    let root = lexical(&Path::new(env!("CARGO_MANIFEST_DIR")).join("../.."));
    let (files, _) = producing_sources(&root, "unbound_shaders", &[]).unwrap();
    let compat = root.join("crates/unbound-compat/src");
    assert!(files.contains(&compat.join("graphics.rs")));
    for other in ["audio.rs", "texture.rs", "profile.rs"] {
        assert!(!files.contains(&compat.join(other)), "{other}");
    }
}

/// `super::` from a module's directory names the module unless it climbs past it; test code
/// names nothing (it is not in the output's build).
#[test]
fn super_paths_and_test_code() {
    let ws = Workspace::new(
        "super",
        &[
            ("crates/unbound-gen/Cargo.toml", "[package]"),
            (
                "crates/unbound-gen/src/lib.rs",
                "mod gen;\nmod far;\nmod near;\nmod tested;\n",
            ),
            (
                "crates/unbound-gen/src/gen.rs",
                "mod part;\npub struct Shape;\n#[cfg(test)]\nmod tests {\n    use super::*;\n    \
                 use crate::tested::Helper;\n}\n",
            ),
            (
                "crates/unbound-gen/src/gen/part.rs",
                "use super::Shape;\nuse super::super::near::Near;\n",
            ),
            ("crates/unbound-gen/src/near.rs", "pub struct Near;\n"),
            ("crates/unbound-gen/src/far.rs", "pub struct Far;\n"),
            ("crates/unbound-gen/src/tested.rs", "pub struct Helper;\n"),
        ],
    );
    assert_eq!(
        ws.sources("unbound_gen", &["gen"]),
        [
            "crates/unbound-gen/Cargo.toml",
            "crates/unbound-gen/src/gen/part.rs",
            "crates/unbound-gen/src/gen.rs",
            "crates/unbound-gen/src/lib.rs",
            "crates/unbound-gen/src/near.rs",
        ]
    );
}

/// Code the reading cannot follow, or that hides its paths from a plain reading, still reaches
/// what it names: each case reaches the rules crate's `audio` module.
#[test]
fn hidden_and_unfollowable_names_reach_what_they_name() {
    let audio = "crates/unbound-rules/src/audio.rs".to_string();
    let cases = [
        // A nested group names its later items too.
        ("nested", "use unbound_rules::{device::{Device}, Audio};\n"),
        // A comment marker in a string is no comment.
        (
            "string",
            "const G: &str = \"Shaders/*.shader\";\nuse unbound_rules::Audio;\nconst H: &str = \"*/\";\n",
        ),
        (
            "url",
            "const U: &str = \"http://x\"; use unbound_rules::Audio;\n",
        ),
        // A brace in a test item's string does not take the code after it.
        (
            "test-brace",
            "#[cfg(test)]\nfn helper() -> &'static str { \"{\" }\nuse unbound_rules::Audio;\n",
        ),
        // A crate under another name: all of it.
        (
            "alias",
            "use unbound_rules as r;\nfn f() { r::Device::A; }\n",
        ),
    ];
    for (case, fit) in cases {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-compiler/src/fit.rs", fit);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
    // Macros defined in a crate partly reached: all of it (their uses expand unread).
    let ws = compiler_and_rules("macro");
    ws.write(
        "crates/unbound-rules/src/audio.rs",
        "pub struct Audio;\n#[macro_export]\nmacro_rules! gain { () => {} }\n",
    );
    assert!(ws.sources("unbound_compiler", &[]).contains(&audio));
    // `super::` from a directory's `mod.rs` names the crate root's modules.
    let ws = compiler_and_rules("mod-rs");
    std::fs::remove_file(ws.0.join("crates/unbound-rules/src/device.rs")).unwrap();
    std::fs::create_dir_all(ws.0.join("crates/unbound-rules/src/device")).unwrap();
    ws.write(
        "crates/unbound-rules/src/device/mod.rs",
        "pub enum Device { A }\npub fn select() {}\nfn g() { super::audio::Audio; }\n",
    );
    assert!(ws.sources("unbound_compiler", &[]).contains(&audio));
    // An include of a computed path: all of the crate; included code: all of it too.
    for (case, device) in [
        (
            "computed",
            "pub enum Device { A }\npub fn select() {}\nconst T: &str = include_str!(concat!(\"a\", \"b\"));\n",
        ),
        (
            "include",
            "pub enum Device { A }\npub fn select() {}\ninclude!(\"generated.rs\");\n",
        ),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-rules/src/device.rs", device);
        ws.write("crates/unbound-rules/src/generated.rs", "");
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
}

/// A root with code of its own, a byte or C raw string, a top-level `use super::*` beside an
/// inline module and `{self as r}`: each reaches the rules crate's `audio` module.
#[test]
fn root_code_raw_strings_and_more_aliases_reach_what_they_name() {
    let audio = "crates/unbound-rules/src/audio.rs".to_string();
    let rules_root = |extra: &str| format!("{RULES_ROOT}{extra}");
    for (case, root) in [
        (
            "root-use",
            rules_root("use audio::Audio;\npub fn mix() -> u32 { Audio::gain() }\n"),
        ),
        (
            "root-impl",
            rules_root("impl Device { pub fn conv(&self) { audio::Audio; } }\n"),
        ),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-rules/src/lib.rs", &root);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
    for (case, fit) in [
        (
            "byte-raw",
            "const B: &[u8] = br#\"a\"b\"#;\nuse unbound_rules::Audio;\n",
        ),
        (
            "self-as",
            "use unbound_rules::{self as r};\nfn f() { r::Audio; }\n",
        ),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-compiler/src/fit.rs", fit);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
    let ws = compiler_and_rules("super-glob");
    ws.write(
        "crates/unbound-rules/src/device.rs",
        "use super::*;\nmod inner { }\npub enum Device { A }\npub fn select() {}\n",
    );
    assert!(ws.sources("unbound_compiler", &[]).contains(&audio));
}

/// Aliases after a nested group, `super::` paths inside function bodies beside an inline
/// module, a root's `#[path]` (also under `cfg_attr`) and `extern crate` alias, and C raw
/// strings: each reaches the rules crate's `audio` module.
#[test]
fn aliases_super_paths_and_root_attributes_reach_what_they_name() {
    let audio = "crates/unbound-rules/src/audio.rs".to_string();
    for (case, fit) in [
        (
            "late-self-as",
            "use unbound_rules::{device::{Device}, self as r};\nfn f() { r::Audio; }\n",
        ),
        (
            "c-raw",
            "const C: &str = cr#\"a\"b\"#;\nuse unbound_rules::Audio;\n",
        ),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-compiler/src/fit.rs", fit);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
    for (case, device) in [
        (
            "glob-in-fn",
            "mod inner {}\npub enum Device { A }\npub fn select() { use super::*; let _ = Audio; }\n",
        ),
        (
            "own-name-in-fn",
            "mod inner {}\nfn audio() {}\npub enum Device { A }\npub fn select() { super::audio::Audio; }\n",
        ),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-rules/src/device.rs", device);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
    for (case, root) in [
        (
            "cfg-attr-path",
            format!("#[cfg_attr(target_os = \"macos\", path = \"dev_mac.rs\")]\n{RULES_ROOT}"),
        ),
        (
            "extern-alias",
            format!("extern crate std as standard;\n{RULES_ROOT}"),
        ),
    ] {
        let ws = compiler_and_rules(case);
        ws.write("crates/unbound-rules/src/lib.rs", &root);
        assert!(
            ws.sources("unbound_compiler", &[]).contains(&audio),
            "{case}"
        );
    }
}
