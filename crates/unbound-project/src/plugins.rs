//! Precompiled plugin DLLs and their `PluginImporter` settings.
use std::path::{Path, PathBuf};
use unbound_yaml::{Guid, Value};

/// A GUID's bytes in the order used by the Editor's plugin reference graph.
pub fn guid_order_bytes(guid: Guid) -> [u8; 16] {
    guid.0.to_be_bytes().map(|byte| byte.rotate_left(4))
}

#[derive(Debug, Clone)]
pub struct Plugin {
    pub file: PathBuf,
    /// `Assets/...` or `Packages/<name>/...`
    pub path: String,
    pub guid: Option<Guid>,
    /// Referenced only by assemblies that list it in `precompiledReferences`.
    pub explicitly_referenced: bool,
    pub define_constraints: Vec<String>,
    /// Labelled `RoslynAnalyzer`: a Roslyn analyzer or source generator, not a reference.
    pub analyzer: bool,
    /// The assembly name and version the DLL declares (`None` where they cannot be read).
    pub identity: Option<crate::assembly_identity::AssemblyIdentity>,
    /// Its `.meta` has no platform settings: it has those the Editor gives it by its kind and
    /// folders ([`crate::plugin_defaults`]).
    pub settings_defaulted: bool,
    importer: Value,
}

impl Plugin {
    pub fn file_name(&self) -> &str {
        self.file
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or_default()
    }

    /// Whether the plugin is enabled for a platform (`Editor`, `Android`, `Linux64`, ...):
    /// with "Any Platform" on, unless that platform is excluded from it (the `Exclude <platform>`
    /// setting of `Any`); otherwise when it is enabled for the platform
    /// (tests/projects/plugindefaults `importers.*`, `precompiled`).
    pub fn compatible_with(&self, platform: &str) -> bool {
        if self.platform_enabled("Any").unwrap_or(false) {
            !self
                .platform_setting("Any", &format!("Exclude {platform}"))
                .is_some_and(|value| value == "1" || value == "true")
        } else {
            self.platform_enabled(platform).unwrap_or(false)
        }
    }

    fn platform_enabled(&self, key: &str) -> Option<bool> {
        self.platform_entry(key)?.get("enabled")?.as_bool()
    }

    /// A platform's entry of `platformData` (its `enabled` and `settings`).
    fn platform_entry(&self, key: &str) -> Option<&Value> {
        match self.importer.get("platformData")? {
            // `platformData: {Any: {enabled: 1}, Editor: {...}}`
            Value::Map(map) => map.get(key),
            // `- first: {Any: }  second: {enabled: 1}` and `- first: {Editor: Editor} ...`
            Value::Seq(items) => items.iter().find_map(|item| {
                let first = item.get("first")?.as_map()?;
                let matched = first.iter().any(|(group, name)| {
                    group == key
                        || name.as_str() == Some(key)
                        || format!("{group} {}", name.as_str().unwrap_or("")) == key
                });
                if matched { item.get("second") } else { None }
            }),
            _ => None,
        }
    }

    /// A setting of a platform's entry (`Any`, `Editor`, a build target), as text.
    fn platform_setting(&self, platform: &str, key: &str) -> Option<String> {
        let settings = self.platform_entry(platform)?.get("settings")?;
        settings.get(key)?.as_str().map(String::from)
    }
}

/// The file extensions of the native libraries the Editor of this OS loads: `.so` on Linux,
/// `.bundle` and `.dylib` on macOS, `.dll` on Windows (Unity tells a plugin's OS by them).
pub const NATIVE_EXTENSIONS: &[&str] = if cfg!(target_os = "macos") {
    &["bundle", "dylib"]
} else if cfg!(windows) {
    &["dll"]
} else {
    &["so"]
};

/// The Editor's name of this OS in a plugin's `OS` setting.
const EDITOR_OS: &str = if cfg!(target_os = "macos") {
    "OSX"
} else if cfg!(windows) {
    "Windows"
} else {
    "Linux"
};

/// A native library and its importer settings, read from its `.meta`; None without a readable one.
/// `path` is its project path (`Assets/...`, `Packages/<name>/...`), whose folders give the settings
/// of a `.meta` without any.
pub fn native_plugin(file: &Path, path: &str) -> Option<Plugin> {
    let meta = std::fs::read_to_string(format!("{}.meta", file.display())).ok()?;
    let meta = unbound_yaml::parse_value(&meta).ok()?;
    Some(native_plugin_from_meta(file, path, &meta))
}

/// Whether a native plugin's importer loads it on startup ("Load on startup", `isPreloaded`):
/// the Editor loads it before Play mode, before any script calls it.
pub fn native_plugin_preloaded(file: &Path) -> bool {
    let Ok(meta) = std::fs::read_to_string(format!("{}.meta", file.display())) else {
        return false;
    };
    let Ok(meta) = unbound_yaml::parse_value(&meta) else {
        return false;
    };
    meta.get("PluginImporter")
        .and_then(|importer| importer.get("isPreloaded"))
        .and_then(|value| value.as_i64().or_else(|| value.as_str()?.parse().ok()))
        .is_some_and(|value| value != 0)
}

/// [`native_plugin`] from a `.meta` already read.
pub fn native_plugin_from_meta(file: &Path, path: &str, meta: &Value) -> Plugin {
    let (importer, settings_defaulted) = importer_settings(meta, path, false);
    Plugin {
        file: file.to_path_buf(),
        path: path.to_string(),
        guid: None,
        explicitly_referenced: false,
        define_constraints: Vec::new(),
        analyzer: false,
        identity: None,
        settings_defaulted,
        importer,
    }
}

/// Says once that plug-ins' importer settings are given and read as the Unity 6000.0 Editor does
/// in a project of a release whose Editor was not measured.
pub fn warn_unmeasured_defaults(release: &dyn std::fmt::Display) {
    static WARNED: std::sync::Once = std::sync::Once::new();
    WARNED.call_once(|| {
        tracing::warn!(
            "Unbound: plug-ins' PluginImporter settings are given and read as the Unity 6000.0 Editor does (the defaults of a .meta without them); what the {release} Editor does is not known"
        )
    });
}

/// The loadable file of a native plugin: the file itself, or a macOS bundle directory's
/// executable (`Contents/MacOS/<CFBundleExecutable>`, else the bundle's name).
pub fn native_library_file(plugin: &Path) -> Option<PathBuf> {
    if plugin.is_file() {
        return Some(plugin.to_path_buf());
    }
    let contents = plugin.join("Contents");
    let executable = std::fs::read_to_string(contents.join("Info.plist"))
        .ok()
        .and_then(|plist| {
            let (_, rest) = plist.split_once("<key>CFBundleExecutable</key>")?;
            let (_, rest) = rest.split_once("<string>")?;
            let (name, _) = rest.split_once("</string>")?;
            Some(name.trim().to_string())
        })
        .or_else(|| Some(plugin.file_stem()?.to_str()?.to_string()))?;
    let file = contents.join("MacOS").join(executable);
    file.is_file().then_some(file)
}

impl Plugin {
    /// A native library the Editor loads on this machine: enabled for the Editor, for this OS and
    /// CPU.
    pub fn loads_in_editor(&self) -> bool {
        self.compatible_with("Editor") && self.editor_machine_allowed()
    }

    /// Whether the plugin's Editor settings allow this machine: its OS (`OS: Linux|OSX|AnyOS`) and
    /// CPU (`CPU: x86_64|ARM64|AnyCPU`). The Editor neither loads a native library nor compiles
    /// against a managed one on another (tests/projects/plugindefaults `precompiled`: a managed
    /// plug-in for x86).
    pub fn editor_machine_allowed(&self) -> bool {
        let os = self.editor_setting("OS").unwrap_or_default();
        let cpu = self.editor_setting("CPU").unwrap_or_default();
        let os_ok = os.is_empty() || os == "AnyOS" || os == EDITOR_OS;
        let cpu_ok = match std::env::consts::ARCH {
            "x86_64" => matches!(cpu.as_str(), "" | "AnyCPU" | "x86_64"),
            "aarch64" => matches!(cpu.as_str(), "" | "AnyCPU" | "ARM64"),
            _ => cpu.is_empty() || cpu == "AnyCPU",
        };
        os_ok && cpu_ok
    }

    /// A setting of the Editor platform (`OS`, `CPU`).
    fn editor_setting(&self, key: &str) -> Option<String> {
        self.platform_setting("Editor", key)
    }
}

/// Reads a DLL's `.meta`; `None` when it is not a managed assembly. A DLL without a `.meta` yet
/// (a build step just copied it in) under a `mutable` root (the project's Assets, an embedded or
/// local folder package) is a plugin with the default settings, as the Editor imports it; in a
/// package the Editor keeps in its cache it is ignored, as there. A `.meta` that does not parse
/// leaves the DLL out.
pub fn read_plugin(file: &Path, path: String, mutable: bool) -> Option<Plugin> {
    if !is_managed_assembly(file) {
        return None;
    }
    let meta = match std::fs::read_to_string(format!("{}.meta", file.display())) {
        Ok(text) => unbound_yaml::parse_value(&text).ok()?,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
            if !mutable {
                // The Editor's warning for such a file, once per file (the plugins are listed
                // more than once per start).
                static WARNED: std::sync::Mutex<Vec<PathBuf>> = std::sync::Mutex::new(Vec::new());
                let mut warned = WARNED.lock().unwrap_or_else(|e| e.into_inner());
                if !warned.iter().any(|w| w == file) {
                    warned.push(file.to_path_buf());
                    tracing::warn!(
                        "Asset {path} has no meta file, but it's in an immutable folder. The asset will be ignored."
                    );
                }
                return None;
            }
            Value::default()
        }
        Err(_) => return None,
    };
    let mut plugin = plugin_from_meta(file, path, &meta);
    plugin.identity = crate::assembly_identity::read(file);
    Some(plugin)
}

pub fn plugin_from_meta(file: &Path, path: String, meta: &Value) -> Plugin {
    let (guid, labels) = crate::assetdb::meta_header(meta);
    let (importer, settings_defaulted) = importer_settings(meta, &path, true);
    let explicitly_referenced = importer
        .get("isExplicitlyReferenced")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let define_constraints = importer
        .get("defineConstraints")
        .and_then(Value::as_seq)
        .map(|l| {
            l.iter()
                .filter_map(Value::as_str)
                .map(String::from)
                .collect()
        })
        .unwrap_or_default();
    Plugin {
        file: file.to_path_buf(),
        path,
        guid: guid.filter(|guid| !guid.is_zero()),
        explicitly_referenced,
        define_constraints,
        analyzer: labels.iter().any(|label| label == "RoslynAnalyzer"),
        identity: None,
        settings_defaulted,
        importer,
    }
}

/// A plug-in's importer settings. Unity imports every plug-in with the PluginImporter whatever
/// importer its `.meta` names; one without platform settings (its GUID alone, Google's Version
/// Handler's `DefaultImporter`) gets the ones the Editor gives it by its kind and folders
/// ([`crate::plugin_defaults`]). `path` is the plug-in's project path; `managed` whether it is a
/// .NET assembly. Only platform flags and Editor settings are read from it here, so the Android
/// module's setting is left out. Also says whether they are the defaults.
fn importer_settings(meta: &Value, path: &str, managed: bool) -> (Value, bool) {
    if crate::plugin_defaults::has_platform_settings(meta) {
        return (
            meta.get("PluginImporter").cloned().unwrap_or_default(),
            false,
        );
    }
    let importer = crate::plugin_defaults::defaults(path, managed, false)
        .map(|defaults| crate::plugin_defaults::with_defaults(meta, &defaults))
        .unwrap_or_default();
    (importer, true)
}

/// A PE file with a CLI header (data directory 14).
pub fn is_managed_assembly(file: &Path) -> bool {
    use std::io::{Read, Seek, SeekFrom};
    let Ok(mut f) = std::fs::File::open(file) else {
        return false;
    };
    let mut dos = [0u8; 64];
    if f.read_exact(&mut dos).is_err() || &dos[..2] != b"MZ" {
        return false;
    }
    let pe = u32::from_le_bytes(dos[60..64].try_into().unwrap()) as u64;
    let mut header = [0u8; 24 + 2];
    if f.seek(SeekFrom::Start(pe)).is_err()
        || f.read_exact(&mut header).is_err()
        || &header[..4] != b"PE\0\0"
    {
        return false;
    }
    let magic = u16::from_le_bytes([header[24], header[25]]);
    // The data directories start at 96 (PE32) or 112 (PE32+) bytes into the optional header.
    let directories = match magic {
        0x10b => 96,
        0x20b => 112,
        _ => return false,
    };
    let clr = pe + 24 + directories + 14 * 8;
    let mut entry = [0u8; 8];
    if f.seek(SeekFrom::Start(clr)).is_err() || f.read_exact(&mut entry).is_err() {
        return false;
    }
    u32::from_le_bytes(entry[..4].try_into().unwrap()) != 0
}

#[cfg(test)]
mod tests {
    use super::*;

    fn plugin(meta: &str) -> Plugin {
        let meta = unbound_yaml::parse_value(meta).unwrap();
        plugin_from_meta(Path::new("A.dll"), "Assets/A.dll".into(), &meta)
    }

    #[test]
    fn a_dll_without_plugin_importer_settings_is_for_any_platform() {
        let p = plugin(
            "fileFormatVersion: 2\nguid: f84de9af142947eeb3522e42d9487838\nDefaultImporter:\n  userData:\n",
        );
        assert!(p.compatible_with("Editor"));
        assert!(p.compatible_with("Android"));
    }

    /// A DLL without a `.meta` yet is a plugin for every platform where the Editor imports it.
    #[test]
    fn a_dll_without_a_meta_is_a_plugin_with_the_default_settings() {
        let dir = std::env::temp_dir().join(format!("unbound-no-meta-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let file = dir.join("ProbeDup.dll");
        let probe = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/compilesetup/Assets/Plugins/Dup/ProbeDup.dll");
        std::fs::copy(probe, &file).unwrap();
        let plugin = read_plugin(&file, "Assets/ProbeDup.dll".into(), true);
        // In a package the Editor keeps in its cache a file without a `.meta` is ignored.
        let cached = read_plugin(&file, "Packages/com.example/ProbeDup.dll".into(), false);
        std::fs::remove_dir_all(&dir).unwrap();
        assert!(cached.is_none());
        let plugin = plugin.expect("a plugin");
        assert!(plugin.compatible_with("Editor") && plugin.compatible_with("Android"));
        assert!(!plugin.explicitly_referenced && !plugin.analyzer);
        assert_eq!(plugin.identity.map(|i| i.name).as_deref(), Some("ProbeDup"));
    }

    #[test]
    fn plugin_importer_settings_are_kept() {
        let p = plugin(
            "PluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n    Editor:\n      enabled: 1\n",
        );
        assert!(p.compatible_with("Editor"));
        assert!(!p.compatible_with("Android"));
    }

    /// tests/projects/plugindefaults: a `.meta` with the GUID alone gives the plug-in the
    /// platforms of its kind and folders.
    #[test]
    fn a_guid_only_meta_gets_the_editors_defaults_by_folder() {
        let guid_only = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef";
        let managed_with = |meta: &str, path: &str| {
            plugin_from_meta(
                Path::new("A.dll"),
                path.to_string(),
                &unbound_yaml::parse_value(meta).unwrap(),
            )
        };
        let managed = |path: &str| managed_with(guid_only, path);
        let any = managed("Assets/Plugins/Managed.dll");
        assert!(any.settings_defaulted);
        assert!(any.compatible_with("Editor") && any.compatible_with("Android"));
        let editor = managed("Assets/Tools/Editor/Managed.dll");
        assert!(editor.compatible_with("Editor") && !editor.compatible_with("Android"));
        let android = managed("Assets/Plugins/Android/Managed.dll");
        assert!(!android.compatible_with("Editor") && android.compatible_with("Android"));
        // In Plugins/x86 the Editor's CPU is x86: no Editor's.
        let x86 = managed("Assets/Plugins/x86/Managed.dll");
        assert!(x86.compatible_with("Editor") && !x86.editor_machine_allowed());
        // A section without platform settings keeps its own settings.
        let own = managed_with(
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nPluginImporter:\n  isExplicitlyReferenced: 1\n  defineConstraints:\n  - FOO\n",
            "Assets/Plugins/Own.dll",
        );
        assert!(own.settings_defaulted && own.explicitly_referenced);
        assert_eq!(own.define_constraints, ["FOO"]);
        assert!(own.compatible_with("Editor"));
        // A section with platform settings is read as it is.
        let section = plugin("PluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n");
        assert!(!section.settings_defaulted && !section.compatible_with("Editor"));
    }

    #[test]
    fn a_guid_only_native_library_loads_where_its_defaults_enable_the_editor() {
        let guid_only = unbound_yaml::parse_value(
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef",
        )
        .unwrap();
        let loads = |path: &str| {
            native_plugin_from_meta(Path::new(path), path, &guid_only).loads_in_editor()
        };
        // A macOS library and a shared object are on for the Editor in any folder but the
        // platform and architecture folders of other machines.
        assert!(loads("Assets/Plugins/libmac.dylib"));
        assert!(loads("Assets/Plugins/Kit.bundle"));
        assert!(loads("Assets/Plugins/libnative.so"));
        assert!(loads("Assets/Other/Editor/libnative.so"));
        assert!(!loads("Assets/Plugins/Android/libnative.so"));
        assert!(!loads("Assets/Plugins/iOS/libmac.dylib"));
        assert!(!loads("Assets/Plugins/x86/libnative.so"));
        let here = if cfg!(target_arch = "aarch64") {
            "ARM64"
        } else {
            "x86_64"
        };
        let other = if cfg!(target_arch = "aarch64") {
            "x86_64"
        } else {
            "ARM64"
        };
        assert!(loads(&format!("Assets/Plugins/{here}/libnative.so")));
        assert!(!loads(&format!("Assets/Plugins/{other}/libnative.so")));
    }

    /// tests/projects/plugindefaults `Assets/Controls/AnyExcluding.dll`: "Any Platform" with the
    /// Editor excluded is no Editor plug-in.
    #[test]
    fn an_exclusion_is_a_setting_of_any_platform() {
        let excluded = plugin(
            "PluginImporter:\n  platformData:\n    Any:\n      enabled: 1\n      settings:\n        Exclude Editor: 1\n        Exclude Android: 1\n    Editor:\n      enabled: 0\n",
        );
        assert!(!excluded.compatible_with("Editor"));
        assert!(!excluded.compatible_with("Android"));
        assert!(excluded.compatible_with("Win64"));
        // The old layout: `- first: {: Any}`.
        let old = plugin(
            "PluginImporter:\n  platformData:\n  - first:\n      : Any\n    second:\n      enabled: 1\n      settings:\n        Exclude Editor: 1\n",
        );
        assert!(!old.compatible_with("Editor") && old.compatible_with("Android"));
    }

    #[test]
    fn a_bundle_directory_loads_by_its_executable() {
        let dir = std::env::temp_dir().join(format!("unbound-bundle-{}", std::process::id()));
        let bundle = dir.join("Wrapper.bundle");
        std::fs::create_dir_all(bundle.join("Contents/MacOS")).unwrap();
        std::fs::write(
            bundle.join("Contents/Info.plist"),
            "<dict><key>CFBundleExecutable</key>\n<string>WrapperLib</string></dict>",
        )
        .unwrap();
        std::fs::write(bundle.join("Contents/MacOS/WrapperLib"), b"").unwrap();
        assert_eq!(
            native_library_file(&bundle),
            Some(bundle.join("Contents/MacOS/WrapperLib"))
        );
        let file = dir.join("Plain.bundle");
        std::fs::write(&file, b"").unwrap();
        assert_eq!(native_library_file(&file), Some(file.clone()));
        std::fs::remove_dir_all(&dir).unwrap();
    }
}
