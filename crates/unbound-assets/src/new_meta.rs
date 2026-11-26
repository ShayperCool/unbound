//! The `.meta` file the Editor writes for an asset it imports for the first time: when it opens a
//! project or refreshes (a file or folder without one), and for an asset an editor API creates.
//!
//! Matches the Unity 6000.0.71f1 Editor (tests/projects/projectwrites, `exit.Assets/Startup/*`):
//! `fileFormatVersion: 2`, a new random GUID (32 lowercase hex digits), then the importer's
//! settings at their defaults, with LF line endings and no creation time. Each importer's
//! defaults do not depend on the file's format (a PNG, TGA, PSD, EXR and HDR texture get the
//! same; an OBJ and an FBX model; a WAV, OGG and MP3 clip), except a font's names, which come
//! from the font. A scene gets the DefaultImporter. Extensions whose importer has not been
//! measured get no `.meta` ([`importer_for`] says so), nor does an extension no importer of the
//! Editor claims (the Editor gives it the DefaultImporter, measured with `.xyz`, but a package's
//! ScriptedImporter may claim any extension, which is not known before the scripts compile): the
//! caller reports them instead of writing a guess. A plug-in (a managed or native library, a
//! native source file, a folder plug-in such as `.bundle` or `.framework`) gets a `.meta` with its
//! GUID alone, as a script does; nothing inside a folder plug-in gets one.

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
    /// A plug-in of the PluginImporter (a library, a native source, a folder plug-in): the GUID
    /// only, as a script.
    Plugin,
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
    SpriteAtlas,
    /// A Visual Effect Graph (`.vfx`): the VisualEffectImporter with an empty template.
    VisualEffect,
    /// A SpeedTree 8 tree, with what its `.meta` takes from the file.
    SpeedTree8(crate::speedtree8_meta::TreeShape),
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
        return Some(if is_folder_plugin(path) {
            NewImporter::Plugin
        } else {
            NewImporter::Folder
        });
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
        // A SpeedTree 8 tree of a measured version (another is left without a .meta, reported).
        "st" => NewImporter::SpeedTree8(
            std::fs::read(path)
                .ok()
                .and_then(|bytes| crate::speedtree8_meta::shape(&bytes))?,
        ),
        "spriteatlasv2" => NewImporter::SpriteAtlas,
        // Measured for a graph the package's VisualEffectAssetEditorUtility.CreateNewAsset writes
        // and imports (tests/projects/vfxwrites create/meta); a graph found on disk at startup or
        // refresh gets the same .meta, unmeasured; subgraphs (.vfxoperator, .vfxblock) are not
        // measured.
        "vfx" => NewImporter::VisualEffect,
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
        // A program database beside a library: no importer claims it.
        "unity" | "pdb" => NewImporter::Default,
        plugin if PLUGIN_FILE_EXTENSIONS.contains(&plugin) => NewImporter::Plugin,
        _ => return None,
    })
}

pub use unbound_project::plugin_defaults::{FOLDER_PLUGIN_EXTENSIONS, PLUGIN_FILE_EXTENSIONS};

/// A directory the Editor imports as one plug-in asset, whose contents are no assets.
pub fn is_folder_plugin(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .is_some_and(|e| FOLDER_PLUGIN_EXTENSIONS.contains(&e.to_ascii_lowercase().as_str()))
}

/// What a new `.meta` depends on beyond the asset: the Editor installation's platform modules and
/// the project's default behaviour mode.
#[derive(Debug, Clone, Default)]
pub struct MetaContext {
    /// The installed platform modules other than the standalone players, in the build settings'
    /// order, by the names texture settings give them (`Android`, `iOS`, `WebGL`).
    pub platforms: Vec<String>,
    /// `EditorSettings.m_DefaultBehaviorMode` is 2D: a new texture imports as sprites.
    pub mode_2d: bool,
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
        MetaContext {
            platforms,
            mode_2d: false,
        }
    }

    /// The context with the project's default behaviour mode (`TierDefaults::mode_2d`).
    pub fn with_mode_2d(mut self, mode_2d: bool) -> Self {
        self.mode_2d = mode_2d;
        self
    }

    /// The context in the 3D mode: a new texture gets the Default texture's section.
    pub fn in_3d(&self) -> Self {
        self.clone().with_mode_2d(false)
    }
}

/// The importer section the Editor writes into a C# script's `.meta` when it writes the file
/// again (setting its labels, for one): the MonoImporter at its defaults (tests/projects/projectwrites
/// 09.labels.script). A new script's `.meta` has none.
pub const MONO_IMPORTER_SECTION: &str = "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";

/// The per-platform entry of a texture's settings at its defaults, for the platform `name`.
pub fn texture_platform_entry(name: &str) -> String {
    include_str!("new_meta/texture_platform.yaml").replace("{platform}", name)
}

/// The Editor's layout of an importer's settings, as it writes them whenever it saves them: the
/// section of a new asset's `.meta` (the importer named and its keys in order, at their defaults).
/// Measured for the audio and model importers (tests/projects/importermeta); a texture's layout is
/// its new-asset section with its platforms (`texture-meta-template`); `None` for the others. The
/// second element, a per-platform entry, is for layouts whose own section lists none.
pub fn importer_layout(importer: &str) -> Option<(String, Option<String>)> {
    match importer {
        "AudioImporter" => Some((include_str!("new_meta/audio_importer.yaml").into(), None)),
        "ModelImporter" => Some((include_str!("new_meta/model_importer.yaml").into(), None)),
        _ => None,
    }
}

/// The importer section of a `.meta` that holds the GUID alone, for the importers that read it
/// with their oldest settings: what such an asset imports with, and what the Editor writes there
/// when it writes the file again (labels, a saved setting). A texture is a cube map with
/// gamma decoding and uncompressed platforms, a sound compressed in memory without a sample-rate
/// override (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave label-metas
/// labelled.Tex.png and labelled.Beep.wav, tests/projects/guidonlymetas); `None` for the others
/// (a model's legacy import, which also writes its materials, is not supported).
pub fn guid_only_section(importer: NewImporter, context: &MetaContext) -> Option<String> {
    // The new asset's section: its .meta after the format version and GUID lines.
    let new = |name: &str| {
        let text = text(importer, Guid(1), Path::new(name), context);
        text.splitn(3, '\n').nth(2).unwrap_or_default().to_string()
    };
    let changed = |section: String, edits: &[(&str, &str)]| {
        edits.iter().fold(section, |section, (from, to)| {
            debug_assert!(section.contains(from), "{from}");
            section.replace(from, to)
        })
    };
    match importer {
        NewImporter::Texture => Some(changed(
            new("Texture.png"),
            &[
                ("\n  textureShape: 1\n", "\n  textureShape: 2\n"),
                ("\n  applyGammaDecoding: 0\n", "\n  applyGammaDecoding: 1\n"),
                ("\n  cookieLightType: 0\n", "\n  cookieLightType: 2\n"),
                (
                    "\n    textureCompression: 1\n",
                    "\n    textureCompression: 0\n",
                ),
            ],
        )),
        NewImporter::Audio => Some(changed(
            new("Sound.wav"),
            &[
                ("\n    loadType: 0\n", "\n    loadType: 1\n"),
                (
                    "\n    sampleRateOverride: 44100\n",
                    "\n    sampleRateOverride: 0\n",
                ),
            ],
        )),
        _ => None,
    }
}

/// Whether a `.meta` (parsed) names no importer: it holds its GUID (and perhaps labels) alone.
pub fn names_no_importer(meta: &unbound_yaml::Value) -> bool {
    meta.as_map()
        .is_some_and(|m| !m.iter().any(|(k, _)| k.ends_with("Importer")))
}

/// `meta` as its importer reads it: a `.meta` that names no importer with the
/// [`guid_only_section`] of `importer` (the installation's platforms left out, which the import
/// does not read); `None` where `meta` names one or the importer has none.
pub fn with_guid_only_section(
    meta: &unbound_yaml::Value,
    importer: NewImporter,
    name: &str,
) -> Option<unbound_yaml::Value> {
    if !names_no_importer(meta) {
        return None;
    }
    let section = guid_only_section(importer, &MetaContext::default())?;
    let parsed = unbound_yaml::parse_value(&section).ok()?;
    let mut map = meta.as_map()?.clone();
    map.insert(name, parsed.get(name)?.clone());
    Some(unbound_yaml::Value::Map(map))
}

/// A new GUID, as random as the Editor's.
pub fn new_guid() -> Guid {
    let mut bytes = [0u8; 16];
    getrandom::fill(&mut bytes).expect("the operating system's random source");
    let guid = Guid(u128::from_le_bytes(bytes));
    if guid.is_zero() { new_guid() } else { guid }
}

/// Replaces the top-level GUID token while leaving the other metadata bytes alone.
pub fn replace_guid(text: &str, old: Guid, new: Guid) -> anyhow::Result<String> {
    use anyhow::Context;

    let mut offset = 0;
    for line in text.split_inclusive('\n') {
        if let Some(value) = line.strip_prefix("guid:") {
            let token = value.trim();
            anyhow::ensure!(
                token.bytes().all(|byte| byte.is_ascii_hexdigit())
                    && Guid::parse(token) == Some(old),
                "the .meta has no matching plain GUID token"
            );
            let start = offset + "guid:".len() + value.len() - value.trim_start().len();
            return Ok(format!(
                "{}{new}{}",
                &text[..start],
                &text[start + token.len()..]
            ));
        }
        offset += line.len();
    }
    None.context("no guid line")
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
        // The Editor ends a script's or a plug-in's .meta without a line break.
        NewImporter::Script | NewImporter::Plugin => {
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
            let platforms: String = ["DefaultTexturePlatform", "Standalone"]
                .into_iter()
                .chain(context.platforms.iter().map(String::as_str))
                .map(texture_platform_entry)
                .collect();
            let section =
                include_str!("new_meta/texture_importer.yaml").replace("{platforms}", &platforms);
            out.push_str(&if context.mode_2d {
                sprites_2d(section, asset)
            } else {
                section
            });
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
        NewImporter::SpriteAtlas => {
            out.push_str(include_str!("new_meta/sprite_atlas_importer.yaml"))
        }
        NewImporter::VisualEffect => out.push_str(&format!(
            "VisualEffectImporter:\n  externalObjects: {{}}\n  serializedVersion: 1\n  template:\n    name: \n    category: \n    description: \n    icon: {{instanceID: 0}}\n    thumbnail: {{instanceID: 0}}\n{TRAILER}"
        )),
        NewImporter::SpeedTree8(shape) => out.push_str(&crate::speedtree8_meta::section(&shape)),
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

/// A new texture's section in a project of the 2D default behaviour mode: the Editor imports it
/// as sprites (`Multiple`), sliced automatically, clamped, without mipmaps or NPOT scaling, its
/// alpha transparency (matches the Unity 6000.0.71f1 Editor, tests/projects/spritemeta New,
/// NewNpot): a PNG of no transparent pixel is one sprite `<name>_0` of the whole texture, whose
/// file id and sprite ID are the Editor's hashes. The slicing of one with transparent pixels, and
/// other formats, are not measured: they keep the Default texture's section, with a warning.
fn sprites_2d(section: String, asset: &Path) -> String {
    let png = asset
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("png"));
    let opaque = png
        .then(|| std::fs::read(asset).ok())
        .flatten()
        .and_then(|bytes| crate::image_io::decode(&bytes).ok())
        .filter(|image| image.rgba.chunks_exact(4).all(|pixel| pixel[3] > 0));
    let Some(image) = opaque else {
        tracing::warn!(
            "{}: in a 2D project the Editor imports a new texture as sprites, sliced automatically; Unbound does so for an opaque PNG only (not one with transparent pixels, another format or one it cannot read) yet, so this one imports as a Default texture",
            asset.display()
        );
        return section;
    };
    let stem = asset
        .file_stem()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default();
    let name = format!("{stem}_0");
    let id = crate::meta::hashed_file_id("Sprite", &name, 0);
    let sprite_id = crate::meta::sprite_id(id);
    let (width, height) = (image.width, image.height);
    let mut text = section;
    for (from, to) in [
        (
            "  internalIDToNameTable: []\n".to_string(),
            format!(
                "  internalIDToNameTable:\n  - first:\n      {SPRITE_CLASS_ID}: {id}\n    second: {name}\n"
            ),
        ),
        (
            "    enableMipMap: 1\n".into(),
            "    enableMipMap: 0\n".into(),
        ),
        (
            "    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n  nPOTScale: 1\n".into(),
            "    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n".into(),
        ),
        ("  spriteMode: 0\n".into(), "  spriteMode: 2\n".into()),
        (
            "  alphaIsTransparency: 0\n".into(),
            "  alphaIsTransparency: 1\n".into(),
        ),
        ("  textureType: 0\n".into(), "  textureType: 8\n".into()),
        (
            "    sprites: []\n".into(),
            format!(
                "    sprites:\n    - serializedVersion: 2\n      name: {name}\n      rect:\n        serializedVersion: 2\n        x: 0\n        y: 0\n        width: {width}\n        height: {height}\n      alignment: 0\n      pivot: {{x: 0, y: 0}}\n      border: {{x: 0, y: 0, z: 0, w: 0}}\n      customData: \n      outline: []\n      physicsShape: []\n      tessellationDetail: -1\n      bones: []\n      spriteID: {sprite_id}\n      internalID: {id}\n      vertices: []\n      indices: \n      edges: []\n      weights: []\n"
            ),
        ),
        (
            "    nameFileIdTable: {}\n".into(),
            format!("    nameFileIdTable:\n      {name}: {id}\n"),
        ),
    ] {
        debug_assert!(text.contains(&from), "the texture template has no {from:?}");
        text = text.replacen(&from, &to, 1);
    }
    text
}

/// Unity's class id of Sprite, the key of a texture's internal id table.
const SPRITE_CLASS_ID: u32 = 213;

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
    let mut walker = walkdir::WalkDir::new(root)
        .follow_links(true)
        .sort_by_file_name()
        .into_iter()
        .filter_entry(|e| e.depth() == 0 || !hidden(&e.file_name().to_string_lossy()));
    while let Some(entry) = walker.next() {
        let Ok(entry) = entry else { continue };
        if entry.depth() == 0 {
            continue;
        }
        // A folder plug-in is one asset: what is inside it is not imported on its own.
        if entry.file_type().is_dir() && is_folder_plugin(entry.path()) {
            walker.skip_current_dir();
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
    fn a_speedtree8_tree_gets_its_measured_meta() {
        let dir = std::env::temp_dir().join(format!("unbound-st8-meta-{}", new_guid()));
        std::fs::create_dir_all(&dir).unwrap();
        let tree = dir.join("Oak.st");
        std::fs::write(&tree, crate::speedtree8_meta::tests::tree(8, 2, true, 4)).unwrap();
        let importer = importer_for(&tree, false).unwrap();
        let guid = new_guid();
        let text = text(importer, guid, &tree, &MetaContext::default());
        assert!(text.starts_with(&format!(
            "fileFormatVersion: 2\nguid: {guid}\nSpeedTreeImporter:\n  externalObjects: {{}}\n"
        )));
        assert!(text.ends_with(
            "  fileIDType: 2\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        ));
        // A file of an unmeasured version, or no tree at all, gets none.
        let other = dir.join("Other.st");
        std::fs::write(&other, crate::speedtree8_meta::tests::tree(9, 2, true, 4)).unwrap();
        assert_eq!(importer_for(&other, false), None);
        std::fs::write(&other, b"not a tree").unwrap();
        assert_eq!(importer_for(&other, false), None);
        std::fs::remove_dir_all(&dir).unwrap();
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
        assert_eq!(of("a.xyz"), None);
        assert_eq!(
            importer_for(Path::new("Plugin.framework"), true),
            Some(NewImporter::Plugin)
        );
        assert_eq!(of("a.dll"), Some(NewImporter::Plugin));
        assert_eq!(of("A.DLL"), Some(NewImporter::Plugin));
        assert_eq!(of("libx.so"), Some(NewImporter::Plugin));
        assert_eq!(of("a.cpp"), Some(NewImporter::Plugin));
        assert_eq!(of("a.pdb"), Some(NewImporter::Default));
        assert_eq!(of("a.winmd"), None);
        assert_eq!(
            importer_for(Path::new("Folder.png"), true),
            Some(NewImporter::Folder)
        );
    }

    #[test]
    fn plugins_get_the_guid_alone_and_nothing_inside_a_folder_plugin() {
        let root =
            std::env::temp_dir().join(format!("unbound-new-meta-plugins-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("Plugins/Kit.bundle/Contents")).unwrap();
        std::fs::write(
            root.join("Plugins/Kit.bundle/Contents/Info.plist"),
            "<plist/>",
        )
        .unwrap();
        std::fs::write(root.join("Plugins/Lib.dll"), "MZ").unwrap();
        std::fs::write(root.join("Plugins/Lib.pdb"), "pdb").unwrap();
        let imported = import_new(&root, &MetaContext::default(), |_| false);
        assert!(
            imported.unsupported.is_empty(),
            "{:?}",
            imported.unsupported
        );
        let meta = |p: &str| std::fs::read_to_string(root.join(p)).unwrap();
        for plugin in ["Plugins/Lib.dll.meta", "Plugins/Kit.bundle.meta"] {
            let text = meta(plugin);
            assert_eq!(text.lines().count(), 2, "{plugin}: {text}");
            assert!(text.starts_with("fileFormatVersion: 2\nguid: ") && !text.ends_with('\n'));
        }
        assert!(meta("Plugins/Lib.pdb.meta").contains("\nDefaultImporter:\n"));
        assert!(meta("Plugins.meta").contains("folderAsset: yes"));
        assert!(!root.join("Plugins/Kit.bundle/Contents.meta").exists());
        assert!(
            !root
                .join("Plugins/Kit.bundle/Contents/Info.plist.meta")
                .exists()
        );
        std::fs::remove_dir_all(&root).unwrap();
    }

    fn png(path: &Path, width: u32, height: u32, alpha: impl Fn(u32, u32) -> u8) {
        let mut rgba = Vec::new();
        for y in 0..height {
            for x in 0..width {
                rgba.extend_from_slice(&[200, 100, 50, alpha(x, y)]);
            }
        }
        let bytes =
            crate::image_io::encode_rgba8(crate::image_io::Encoding::Png, width, height, &rgba)
                .unwrap();
        std::fs::write(path, bytes).unwrap();
    }

    /// The `.meta` of a new opaque texture in a 2D project, as the Editor writes it
    /// (tests/projects/spritemeta Reference open/NewNpot): sprites of the whole texture.
    #[test]
    fn a_new_opaque_texture_of_a_2d_project_is_one_sprite_of_its_whole_size() {
        let dir = std::env::temp_dir().join(format!("unbound-sprites-2d-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let opaque = dir.join("NewNpot.png");
        png(&opaque, 6, 5, |_, _| 255);
        let context = MetaContext::default().with_mode_2d(true);
        let sprites = text(NewImporter::Texture, Guid(1), &opaque, &context);
        for line in [
            "  internalIDToNameTable:\n  - first:\n      213: 5218904635955191541\n    second: NewNpot_0\n",
            "    enableMipMap: 0\n",
            "    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n  nPOTScale: 0\n",
            "  spriteMode: 2\n",
            "  alphaIsTransparency: 1\n",
            "  textureType: 8\n",
            "      name: NewNpot_0\n      rect:\n        serializedVersion: 2\n        x: 0\n        y: 0\n        width: 6\n        height: 5\n      alignment: 0\n      pivot: {x: 0, y: 0}\n",
            "      spriteID: 5fe9fbe8d164d6840800000000000000\n      internalID: 5218904635955191541\n",
            "    nameFileIdTable:\n      NewNpot_0: 5218904635955191541\n",
        ] {
            assert!(sprites.contains(line), "{line:?} not in\n{sprites}");
        }
        // In a 3D project, and for a texture with a transparent pixel (whose slicing is not
        // known), the Default texture's section.
        let default = text(
            NewImporter::Texture,
            Guid(1),
            &opaque,
            &MetaContext::default(),
        );
        let gaps = dir.join("Gaps.png");
        png(&gaps, 4, 4, |x, _| if x == 0 { 0 } else { 255 });
        let transparent = text(NewImporter::Texture, Guid(1), &gaps, &context);
        assert!(default.contains("  textureType: 0\n  textureShape"));
        assert_eq!(transparent, default);
        assert_eq!(
            text(NewImporter::Texture, Guid(1), &opaque, &context.in_3d()),
            default
        );
        // Another format is not measured: an opaque JPG keeps the Default texture's section.
        let jpg = dir.join("Opaque.jpg");
        let rgb = vec![200u8; 4 * 4 * 4];
        let bytes =
            crate::image_io::encode_rgba8(crate::image_io::Encoding::Jpg(90), 4, 4, &rgb).unwrap();
        std::fs::write(&jpg, bytes).unwrap();
        let jpg_default = text(NewImporter::Texture, Guid(1), &jpg, &MetaContext::default());
        assert_eq!(
            text(NewImporter::Texture, Guid(1), &jpg, &context),
            jpg_default
        );
        std::fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn a_texture_lists_the_installed_platform_modules_after_default_and_standalone() {
        let context = MetaContext {
            platforms: vec!["Android".into(), "iOS".into()],
            ..Default::default()
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
    fn layouts_are_the_new_meta_sections_of_the_measured_importers() {
        let (audio, none) = importer_layout("AudioImporter").unwrap();
        assert!(audio.starts_with("AudioImporter:\n") && none.is_none());
        assert!(
            importer_layout("ModelImporter")
                .unwrap()
                .0
                .starts_with("ModelImporter:\n")
        );
        assert!(importer_layout("TextureImporter").is_none());
        assert!(importer_layout("TrueTypeFontImporter").is_none());
    }

    #[test]
    fn a_new_graph_gets_the_visual_effect_importer() {
        let context = MetaContext::default();
        // tests/projects/vfxwrites create/meta (the Editor's text, GUID line aside).
        assert_eq!(
            text(
                importer_for(Path::new("a.vfx"), false).unwrap(),
                Guid(2),
                Path::new("a.vfx"),
                &context
            ),
            "fileFormatVersion: 2\nguid: 00000000000000000000000000000002\nVisualEffectImporter:\n  externalObjects: {}\n  serializedVersion: 1\n  template:\n    name: \n    category: \n    description: \n    icon: {instanceID: 0}\n    thumbnail: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
        );
        assert!(importer_for(Path::new("a.vfxoperator"), false).is_none());
        assert!(importer_for(Path::new("a.vfxblock"), false).is_none());
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
        std::fs::create_dir_all(dir.join("Native.bundle/Contents")).unwrap();
        std::fs::write(dir.join("Native.bundle/Contents/Info.plist"), "<plist/>").unwrap();
        std::fs::write(dir.join("Kept.txt.meta"), "fileFormatVersion: 2\nguid: 1\n").unwrap();
        let context = MetaContext::default();
        let imported = import_new(&dir, &context, |name| name.starts_with('.'));
        let mut written: Vec<_> = imported
            .written
            .iter()
            .map(|p| p.strip_prefix(&dir).unwrap().to_string_lossy().into_owned())
            .collect();
        written.sort();
        assert_eq!(
            written,
            ["Native.bundle.meta", "Sub.meta", "Sub/a.txt.meta"]
        );
        // The unmeasured importer is reported; the folder plug-in is one asset, nothing inside it written.
        assert_eq!(imported.unsupported.len(), 1);
        assert!(imported.unsupported.iter().any(|u| u.contains(".asmref")));
        assert!(!dir.join("Native.bundle/Contents.meta").exists());
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

    #[test]
    fn sprite_atlas_v2_meta_matches_editor_defaults() {
        let path = Path::new("Assets/Atlases/SavedNew.spriteatlasv2");
        assert_eq!(importer_for(path, false), Some(NewImporter::SpriteAtlas));
        let rendered = text(
            NewImporter::SpriteAtlas,
            Guid(0x302dcf2a13c64c0838742e9c90bcf6ca),
            path,
            &MetaContext::default(),
        );
        assert!(rendered.starts_with("fileFormatVersion: 2\nguid: "));
        assert!(rendered.contains("\nSpriteAtlasImporter:\n"));
        assert!(rendered.contains("  platformSettings: []\n"));
        assert!(rendered.contains("  variantMultiplier: 1\n  bindAsDefault: 1\n"));
        assert!(rendered.ends_with("  userData: \n  assetBundleName: \n  assetBundleVariant: \n"));
    }

    /// The sections a texture's and a sound's .meta holding the GUID alone are written with are
    /// the Editor's (tests/projects/scenesave label-metas, recorded with the Android module).
    #[test]
    fn guid_only_sections_match_the_editor() {
        let snapshot = |name: &str, importer: &str| {
            let path = concat!(
                env!("CARGO_MANIFEST_DIR"),
                "/../../tests/projects/scenesave/Reference/unity-6000.0.71f1/label-metas/snapshots/"
            );
            let text = std::fs::read_to_string(format!("{path}{name}")).unwrap();
            text[text.find(&format!("{importer}:\n")).unwrap()..].to_string()
        };
        let android = MetaContext {
            platforms: vec!["Android".into()],
        };
        assert_eq!(
            guid_only_section(NewImporter::Texture, &android).unwrap(),
            snapshot("labelled.Tex.png.meta", "TextureImporter")
        );
        assert_eq!(
            guid_only_section(NewImporter::Audio, &android).unwrap(),
            snapshot("labelled.Beep.wav.meta", "AudioImporter")
        );
        assert_eq!(guid_only_section(NewImporter::Model, &android), None);
    }

    /// Only a .meta that names no importer reads the oldest section.
    #[test]
    fn a_meta_naming_no_importer_reads_the_oldest_section() {
        let guid_only = unbound_yaml::parse_value(
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- a\n",
        )
        .unwrap();
        let read =
            with_guid_only_section(&guid_only, NewImporter::Texture, "TextureImporter").unwrap();
        assert_eq!(
            read.get("TextureImporter")
                .and_then(|t| t.get("textureShape"))
                .and_then(|v| v.as_str()),
            Some("2")
        );
        assert_eq!(
            read.get("labels").map(|l| l.as_seq().unwrap().len()),
            Some(1)
        );
        let named = unbound_yaml::parse_value("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextureImporter:\n  isReadable: 1\n").unwrap();
        assert!(with_guid_only_section(&named, NewImporter::Texture, "TextureImporter").is_none());
        let texture = crate::texture::TextureImporter {
            guid_only_section: true,
            ..Default::default()
        };
        let settings = texture.settings(&guid_only);
        assert_eq!(
            (settings.shape, settings.compression),
            (crate::texture::TextureShape::Cube, 0)
        );
        let plain = crate::texture::TextureImporter::default().settings(&guid_only);
        assert_eq!(plain.shape, crate::texture::TextureShape::Texture2D);
    }
}
