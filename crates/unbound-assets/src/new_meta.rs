//! The `.meta` file the Editor writes for an asset it imports for the first time: when it opens a
//! project or refreshes (a file or folder without one), and for an asset an editor API creates.
//!
//! Matches the Unity 6000.0.71f1 Editor (tests/projects/projectwrites, `exit.Assets/Startup/*`):
//! `fileFormatVersion: 2`, a new random GUID (32 lowercase hex digits), then the importer's
//! settings at their defaults, with LF line endings and no creation time. Each importer's
//! defaults do not depend on the file's format (a PNG, TGA, PSD, EXR and HDR texture get the
//! same; an OBJ and an FBX model; a WAV, OGG and MP3 clip), except a font's names, which come
//! from the font. An extension without an importer gets the DefaultImporter (measured: `.xyz`).
//! Extensions whose importer has not been measured get no `.meta` ([`importer_for`] says so):
//! the caller reports them instead of writing a guess.

use std::path::Path;
use unbound_yaml::Guid;

/// The importer the Editor gives a new asset, with what its `.meta` depends on.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NewImporter {
    /// A folder: `folderAsset: yes` and the DefaultImporter.
    Folder,
    Default,
    /// A C# script: the GUID only.
    Script,
    AssemblyDefinition,
    TextScript,
    Shader,
    ShaderInclude,
    ComputeShader,
    /// A YAML asset of the Editor's native format; `main` is its main object's file id (0 for a
    /// file the Editor finds on disk, the object's id for an asset an API creates).
    NativeFormat {
        main: i64,
    },
    Prefab,
    Texture,
    Model,
    Audio,
    Font,
    /// A ScriptedImporter of the Editor's own (UI Toolkit's style sheets, documents, themes):
    /// the importer script's file id in the built-in extra resources, and whether its settings
    /// have `disableValidation`.
    Scripted {
        script: i64,
        validation: bool,
    },
}

/// UI Toolkit's importers, as the Editor names their scripts (built-in GUID
/// `0000000000000000e000000000000000`).
const STYLE_SHEET_IMPORTER: i64 = 12385;
const UXML_IMPORTER: i64 = 13804;
const THEME_STYLE_SHEET_IMPORTER: i64 = 12388;

/// The importer the Editor gives a new file or folder at `path`, or `None` when its extension's
/// importer has not been measured (a `.meta` would be a guess).
pub fn importer_for(path: &Path, is_dir: bool) -> Option<NewImporter> {
    if is_dir {
        return Some(NewImporter::Folder);
    }
    let extension = path
        .extension()
        .and_then(|e| e.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    Some(match extension.as_str() {
        "cs" => NewImporter::Script,
        "asmdef" => NewImporter::AssemblyDefinition,
        "txt" | "html" | "htm" | "xml" | "bytes" | "json" | "csv" | "yaml" | "fnt" | "md" => {
            NewImporter::TextScript
        }
        "shader" => NewImporter::Shader,
        "hlsl" | "cginc" | "glslinc" => NewImporter::ShaderInclude,
        "compute" => NewImporter::ComputeShader,
        "mat" | "anim" | "controller" | "overridecontroller" | "mask" | "physicmaterial"
        | "physicsmaterial2d" | "rendertexture" | "flare" | "mixer" | "shadervariants"
        | "terrainlayer" | "giparams" => NewImporter::NativeFormat { main: 0 },
        "prefab" => NewImporter::Prefab,
        "png" | "jpg" | "jpeg" | "tga" | "psd" | "exr" | "hdr" | "tif" | "tiff" | "bmp" | "gif"
        | "iff" | "pict" | "pic" | "pct" => NewImporter::Texture,
        "fbx" | "obj" | "dae" | "3ds" | "dxf" => NewImporter::Model,
        "wav" | "ogg" | "mp3" | "aif" | "aiff" | "flac" | "mod" | "it" | "s3m" | "xm" => {
            NewImporter::Audio
        }
        "ttf" | "otf" | "ttc" | "dfont" => NewImporter::Font,
        "uss" => NewImporter::Scripted {
            script: STYLE_SHEET_IMPORTER,
            validation: true,
        },
        "uxml" => NewImporter::Scripted {
            script: UXML_IMPORTER,
            validation: false,
        },
        "tss" => NewImporter::Scripted {
            script: THEME_STYLE_SHEET_IMPORTER,
            validation: true,
        },
        // A scene gets the DefaultImporter (measured); so does an extension no importer claims.
        "unity" | "xyz" => NewImporter::Default,
        _ => return None,
    })
}

/// What a new `.meta` depends on beyond the asset: the Editor installation's platform modules.
#[derive(Debug, Clone, Default)]
pub struct MetaContext {
    /// The installed platform modules other than the standalone players, in the build settings'
    /// order, by the names texture settings give them (`Android`, `iOS`, `WebGL`).
    pub platforms: Vec<String>,
}

impl MetaContext {
    /// The context of the installation whose platform module folders are `playback_engines`
    /// (`unbound_project::install::playback_engines`).
    pub fn for_install(playback_engines: &[std::path::PathBuf]) -> Self {
        // The build settings' order of the modules a Linux or macOS installation can hold.
        const MODULES: [(&str, &str); 3] = [
            ("AndroidPlayer", "Android"),
            ("iOSSupport", "iOS"),
            ("WebGLSupport", "WebGL"),
        ];
        let platforms = MODULES
            .iter()
            .filter(|(folder, _)| playback_engines.iter().any(|dir| dir.join(folder).is_dir()))
            .map(|(_, name)| name.to_string())
            .collect();
        MetaContext { platforms }
    }
}

/// A new GUID, as random as the Editor's.
pub fn new_guid() -> Guid {
    let mut bytes = [0u8; 16];
    getrandom::fill(&mut bytes).expect("the operating system's random source");
    let guid = Guid(u128::from_le_bytes(bytes));
    if guid.is_zero() { new_guid() } else { guid }
}

const TRAILER: &str = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

/// The text of the new `.meta` of `asset` (the file itself, for what it names: a font).
pub fn text(importer: NewImporter, guid: Guid, asset: &Path, context: &MetaContext) -> String {
    let mut out = format!("fileFormatVersion: 2\nguid: {guid}\n");
    let simple = |name: &str| format!("{name}:\n  externalObjects: {{}}\n{TRAILER}");
    match importer {
        NewImporter::Folder => {
            out.push_str("folderAsset: yes\n");
            out.push_str(&simple("DefaultImporter"));
        }
        NewImporter::Default => out.push_str(&simple("DefaultImporter")),
        // The Editor ends a script's .meta without a line break.
        NewImporter::Script => {
            out.pop();
        }
        NewImporter::TextScript => out.push_str(&simple("TextScriptImporter")),
        NewImporter::ShaderInclude => out.push_str(&simple("ShaderIncludeImporter")),
        NewImporter::AssemblyDefinition => out.push_str(&simple("AssemblyDefinitionImporter")),
        NewImporter::ComputeShader => out.push_str(&simple("ComputeShaderImporter")),
        NewImporter::Prefab => out.push_str(&simple("PrefabImporter")),
        NewImporter::Shader => out.push_str(&format!(
            "ShaderImporter:\n  externalObjects: {{}}\n  defaultTextures: []\n  nonModifiableTextures: []\n{TRAILER}"
        )),
        NewImporter::NativeFormat { main } => out.push_str(&format!(
            "NativeFormatImporter:\n  externalObjects: {{}}\n  mainObjectFileID: {main}\n{TRAILER}"
        )),
        NewImporter::Texture => {
            let entry = include_str!("new_meta/texture_platform.yaml");
            let platforms: String = ["DefaultTexturePlatform", "Standalone"]
                .into_iter()
                .chain(context.platforms.iter().map(String::as_str))
                .map(|name| entry.replace("{platform}", name))
                .collect();
            out.push_str(
                &include_str!("new_meta/texture_importer.yaml").replace("{platforms}", &platforms),
            );
        }
        NewImporter::Model => out.push_str(include_str!("new_meta/model_importer.yaml")),
        NewImporter::Audio => out.push_str(include_str!("new_meta/audio_importer.yaml")),
        NewImporter::Font => {
            let family = std::fs::read(asset)
                .ok()
                .and_then(|data| crate::font::face_family_style(&data, 0))
                .map(|(family, _)| family);
            let names = match family {
                Some(family) => format!("\n  - {family}"),
                None => " []".into(),
            };
            out.push_str(&include_str!("new_meta/font_importer.yaml").replace("{names}", &names));
        }
        NewImporter::Scripted { script, validation } => {
            out.push_str(&format!(
                "ScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {{}}\n  serializedVersion: 2\n{TRAILER}  script: {{fileID: {script}, guid: 0000000000000000e000000000000000, type: 0}}\n"
            ));
            if validation {
                out.push_str("  disableValidation: 0\n");
            }
        }
    }
    out
}

/// Writes the new `.meta` of the file or folder at `asset` unless it has one. Returns the GUID
/// written, `Ok(None)` when it had one, and an error naming the extension when its importer has
/// not been measured.
pub fn write(asset: &Path, context: &MetaContext) -> anyhow::Result<Option<Guid>> {
    let meta = meta_path(asset);
    if meta.exists() {
        return Ok(None);
    }
    let importer = importer_for(asset, asset.is_dir()).ok_or_else(|| {
        anyhow::anyhow!(
            "{}: Unbound does not know the .meta file the Editor writes for a new {} file yet",
            asset.display(),
            asset
                .extension()
                .map(|e| format!(".{}", e.to_string_lossy()))
                .unwrap_or_else(|| "extensionless".into())
        )
    })?;
    let guid = new_guid();
    std::fs::write(&meta, text(importer, guid, asset, context))?;
    Ok(Some(guid))
}

/// `<asset>.meta`.
pub fn meta_path(asset: &Path) -> std::path::PathBuf {
    let mut name = asset.as_os_str().to_owned();
    name.push(".meta");
    name.into()
}

/// What importing the files and folders under `root` that have no `.meta` did: the new
/// `.meta` files, and the files left without one (their importer is not measured yet).
#[derive(Debug, Default)]
pub struct Imported {
    pub written: Vec<std::path::PathBuf>,
    pub unsupported: Vec<String>,
}

/// Writes the `.meta` of every file and folder under `root` that has none, as the Editor does
/// when it opens or refreshes a project. Hidden names are skipped as the asset database skips
/// them (`unbound_project::assetdb::is_hidden`); `root` itself is not an asset.
pub fn import_new(root: &Path, context: &MetaContext, hidden: impl Fn(&str) -> bool) -> Imported {
    let mut imported = Imported::default();
    let walker = walkdir::WalkDir::new(root)
        .follow_links(true)
        .sort_by_file_name()
        .into_iter()
        .filter_entry(|e| e.depth() == 0 || !hidden(&e.file_name().to_string_lossy()));
    for entry in walker.flatten() {
        if entry.depth() == 0 {
            continue;
        }
        let path = entry.path();
        if path
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("meta"))
            && entry.file_type().is_file()
        {
            continue;
        }
        match write(path, context) {
            Ok(Some(_)) => imported.written.push(meta_path(path)),
            Ok(None) => {}
            Err(error) => imported.unsupported.push(error.to_string()),
        }
    }
    imported
}

/// [`import_new`] over each of `roots` (that exists), with every file left without a `.meta`
/// reported as a warning: what the Editor imports when it opens or refreshes a project, over its
/// editable roots (`unbound_project::Project::editable_roots`).
pub fn import_roots<P: AsRef<Path>>(
    roots: &[P],
    context: &MetaContext,
    hidden: impl Fn(&str) -> bool + Copy,
) -> Imported {
    let mut imported = Imported::default();
    for root in roots.iter().map(AsRef::as_ref).filter(|r| r.is_dir()) {
        let found = import_new(root, context, hidden);
        imported.written.extend(found.written);
        imported.unsupported.extend(found.unsupported);
    }
    for problem in &imported.unsupported {
        tracing::warn!(
            "{problem}; the asset has no .meta file, so the asset database does not list it"
        );
    }
    imported
}

#[cfg(test)]
mod tests {
    use super::*;

    fn guid_of(text: &str) -> &str {
        text.lines().nth(1).unwrap().strip_prefix("guid: ").unwrap()
    }

    #[test]
    fn new_guids_are_32_lowercase_hex_digits_and_differ() {
        let a = new_guid().to_string();
        let b = new_guid().to_string();
        assert_eq!(a.len(), 32);
        assert!(
            a.chars()
                .all(|c| c.is_ascii_hexdigit() && !c.is_ascii_uppercase())
        );
        assert_ne!(a, b);
    }

    #[test]
    fn importers_follow_the_extension_and_unmeasured_ones_are_refused() {
        let of = |name: &str| importer_for(Path::new(name), false);
        assert_eq!(of("A.PNG"), Some(NewImporter::Texture));
        assert_eq!(of("a.fbx"), Some(NewImporter::Model));
        assert_eq!(of("a.cs"), Some(NewImporter::Script));
        assert_eq!(of("a.mat"), Some(NewImporter::NativeFormat { main: 0 }));
        assert_eq!(of("a.unity"), Some(NewImporter::Default));
        assert_eq!(of("a.asmdef"), Some(NewImporter::AssemblyDefinition));
        assert_eq!(of("a.asmref"), None);
        assert_eq!(of("a.dll"), None);
        assert_eq!(
            importer_for(Path::new("Folder.png"), true),
            Some(NewImporter::Folder)
        );
    }

    #[test]
    fn a_texture_lists_the_installed_platform_modules_after_default_and_standalone() {
        let context = MetaContext {
            platforms: vec!["Android".into(), "iOS".into()],
        };
        let text = text(NewImporter::Texture, Guid(1), Path::new("a.png"), &context);
        let targets: Vec<_> = text
            .lines()
            .filter_map(|l| l.trim().strip_prefix("buildTarget: "))
            .collect();
        assert_eq!(
            targets,
            ["DefaultTexturePlatform", "Standalone", "Android", "iOS"]
        );
        assert_eq!(guid_of(&text), "00000000000000000000000000000001");
        assert!(text.ends_with("  assetBundleVariant: \n"));
        assert!(!text.contains('\r') && !text.contains("timeCreated"));
        let bare = super::text(
            NewImporter::Texture,
            Guid(1),
            Path::new("a.png"),
            &MetaContext::default(),
        );
        assert_eq!(bare.matches("buildTarget:").count(), 2);
    }

    #[test]
    fn small_importers_match_the_editor() {
        let context = MetaContext::default();
        assert_eq!(
            text(NewImporter::Folder, Guid(2), Path::new("F"), &context),
            "fileFormatVersion: 2\nguid: 00000000000000000000000000000002\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        );
        assert_eq!(
            text(NewImporter::Script, Guid(2), Path::new("a.cs"), &context),
            "fileFormatVersion: 2\nguid: 00000000000000000000000000000002"
        );
        let uss = text(
            importer_for(Path::new("a.uss"), false).unwrap(),
            Guid(2),
            Path::new("a.uss"),
            &context,
        );
        assert!(uss.ends_with("  script: {fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}\n  disableValidation: 0\n"));
        let uxml = text(
            importer_for(Path::new("a.uxml"), false).unwrap(),
            Guid(2),
            Path::new("a.uxml"),
            &context,
        );
        assert!(uxml.ends_with("type: 0}\n"));
        // A font the Editor cannot read keeps no names.
        assert!(
            text(
                NewImporter::Font,
                Guid(2),
                Path::new("/missing.ttf"),
                &context
            )
            .contains("  fontNames: []\n")
        );
    }

    #[test]
    fn importing_writes_each_missing_meta_once_and_reports_unmeasured_files() {
        let dir = std::env::temp_dir().join(format!("unbound-new-meta-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("Sub/.hidden")).unwrap();
        std::fs::write(dir.join("Sub/a.txt"), "a").unwrap();
        std::fs::write(dir.join("Sub/b.asmref"), "{}").unwrap();
        std::fs::write(dir.join("Sub/.hidden/c.txt"), "c").unwrap();
        std::fs::write(dir.join("Kept.txt"), "k").unwrap();
        std::fs::write(dir.join("Kept.txt.meta"), "fileFormatVersion: 2\nguid: 1\n").unwrap();
        let context = MetaContext::default();
        let imported = import_new(&dir, &context, |name| name.starts_with('.'));
        let mut written: Vec<_> = imported
            .written
            .iter()
            .map(|p| p.strip_prefix(&dir).unwrap().to_string_lossy().into_owned())
            .collect();
        written.sort();
        assert_eq!(written, ["Sub.meta", "Sub/a.txt.meta"]);
        assert_eq!(imported.unsupported.len(), 1);
        assert!(imported.unsupported[0].contains(".asmref"));
        assert_eq!(
            std::fs::read_to_string(dir.join("Kept.txt.meta")).unwrap(),
            "fileFormatVersion: 2\nguid: 1\n"
        );
        assert!(
            import_new(&dir, &context, |name| name.starts_with('.'))
                .written
                .is_empty()
        );
        std::fs::remove_dir_all(&dir).unwrap();
    }
}
