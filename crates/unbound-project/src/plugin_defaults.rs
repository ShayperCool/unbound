//! The PluginImporter settings of a plug-in whose `.meta` has none (its GUID alone, a section
//! without platform settings, another importer's section): the ones the Editor gives it when it
//! imports it, from the plug-in's kind and folders (matches the Unity 6000.0.71f1 Editor,
//! tests/projects/plugindefaults).
//!
//! In order, the first rule that applies:
//! - a folder named `Editor` anywhere: the Editor alone, on the CPU an architecture folder anywhere
//!   names;
//! - an architecture folder right under a folder named `Plugins` (`Plugins/x86`, `x86_64`, `x64`,
//!   `ARM64`): any platform, the standalone players of that CPU;
//! - an Android library or source (`.jar`, `.aar`, `.java`, `.kt`, `.androidlib`): Android;
//! - a platform folder right under the project's `Assets/Plugins` (`Android`, `iOS`, `visionOS`,
//!   `WebGL`, `WSA` or `Metro`): that platform;
//! - else the kind's own platforms.
//!
//! Folder names compare without case (ABI folders exactly). A managed library also gets its Windows Store CPU setting and
//! a shared object its Android 16 KB alignment setting (the Android module's) whatever its folder.

use unbound_yaml::Value;

/// The files the PluginImporter imports, by extension (lowercase): managed and native libraries,
/// archives and native sources (tests/projects/projectwrites `exit.Assets/Startup/Plugins/*`).
pub const PLUGIN_FILE_EXTENSIONS: [&str; 17] = [
    "dll", "so", "dylib", "a", "jar", "aar", "c", "cc", "cpp", "h", "m", "mm", "java", "kt",
    "swift", "jslib", "jspre",
];

/// The folders the PluginImporter imports as one plug-in each, by extension (lowercase).
pub const FOLDER_PLUGIN_EXTENSIONS: [&str; 5] =
    ["bundle", "framework", "xcframework", "plugin", "androidlib"];

/// What a plug-in is, as far as its default platforms go.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Kind {
    /// A .NET assembly (`.dll`).
    Managed,
    /// A `.dll` that is no .NET assembly: a Windows library.
    WindowsLibrary,
    /// A `.so`.
    SharedObject,
    /// A `.dylib` or a `.bundle`: a macOS library.
    MacLibrary,
    /// A static library, a C or C++ source or header, a `.plugin`, `.framework` or `.xcframework`:
    /// any platform.
    AnyPlatform,
    /// A `.jar`, `.aar`, `.java`, `.kt` or `.androidlib`.
    Android,
    /// An Objective-C or Swift source (`.m`, `.mm`, `.swift`).
    Apple,
    /// A WebGL JavaScript library or prelude (`.jslib`, `.jspre`).
    Web,
}

/// A plug-in's kind by its file name's extension; `managed` says whether a `.dll` is a .NET
/// assembly. None for a file the PluginImporter does not import.
pub fn kind(path: &str, managed: bool) -> Option<Kind> {
    let name = path.rsplit('/').next().unwrap_or(path);
    let extension = name.rsplit_once('.')?.1.to_ascii_lowercase();
    Some(match extension.as_str() {
        "dll" if managed => Kind::Managed,
        "dll" => Kind::WindowsLibrary,
        "so" => Kind::SharedObject,
        "dylib" | "bundle" => Kind::MacLibrary,
        "a" | "c" | "cc" | "cpp" | "h" | "plugin" | "framework" | "xcframework" => {
            Kind::AnyPlatform
        }
        "jar" | "aar" | "java" | "kt" | "androidlib" => Kind::Android,
        "m" | "mm" | "swift" => Kind::Apple,
        "jslib" | "jspre" => Kind::Web,
        _ => return None,
    })
}

/// One platform's entry of the importer's `platformData`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Entry {
    pub platform: &'static str,
    pub enabled: bool,
    /// In ordinal order of their keys.
    pub settings: Vec<(&'static str, &'static str)>,
}

/// The importer settings the Editor gives a plug-in.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Defaults {
    /// A package's plug-in may be overridden by one of the same name in the project.
    pub overridable: bool,
    /// In ordinal order of their platforms, as the Editor writes them.
    pub entries: Vec<Entry>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Cpu {
    X86,
    X86_64,
    Arm64,
}

impl Cpu {
    /// An architecture folder's CPU (`x64` is `x86_64`).
    fn of(folder: &str) -> Option<Self> {
        let folder = folder.to_ascii_lowercase();
        Some(match folder.as_str() {
            "x86" => Cpu::X86,
            "x86_64" | "x64" => Cpu::X86_64,
            "arm64" => Cpu::Arm64,
            _ => return None,
        })
    }

    fn name(self) -> &'static str {
        match self {
            Cpu::X86 => "x86",
            Cpu::X86_64 => "x86_64",
            Cpu::Arm64 => "ARM64",
        }
    }

    /// The standalone players' entries of a plug-in in this architecture's folder: enabled for the
    /// players of this CPU, the others set to no CPU (macOS keeps `x86`, which it cannot run).
    fn players(self) -> [Entry; 4] {
        let player = |platform, on, cpu| entry(platform, on, &[("CPU", cpu)]);
        let name = self.name();
        match self {
            Cpu::X86 => [
                player("Linux64", false, "None"),
                player("OSXUniversal", false, name),
                player("Win", true, name),
                player("Win64", false, "None"),
            ],
            Cpu::X86_64 => [
                player("Linux64", true, name),
                player("OSXUniversal", true, name),
                player("Win", false, "None"),
                player("Win64", true, name),
            ],
            Cpu::Arm64 => [
                player("Linux64", false, "None"),
                player("OSXUniversal", true, name),
                player("Win", false, "None"),
                player("Win64", true, name),
            ],
        }
    }
}

/// The platforms a folder right under `Assets/Plugins` stands for.
fn platform_folder(folder: &str) -> Option<&'static str> {
    let folder = folder.to_ascii_lowercase();
    Some(match folder.as_str() {
        "android" => "Android",
        "ios" => "iOS",
        "visionos" => "VisionOS",
        "webgl" => "WebGL",
        "wsa" | "metro" => "WindowsStoreApps",
        _ => return None,
    })
}

/// An Android shared object's CPU by the ABI folder nearest to it (names as the Android NDK spells
/// them, compared exactly); None for `armeabi`, which leaves Android off. Without an ABI folder the
/// Editor picks ARMv7 (and logs that it could not tell).
fn android_abi(folders: &[&str]) -> Option<&'static str> {
    for folder in folders.iter().rev() {
        match *folder {
            "arm64-v8a" => return Some("ARM64"),
            "armeabi-v7a" => return Some("ARMv7"),
            "x86_64" => return Some("x86_64"),
            "x86" => return Some("x86"),
            "armeabi" => return None,
            _ => {}
        }
    }
    Some("ARMv7")
}

/// The settings a plug-in of a kind has whatever its platforms, each in its platform's entry (the
/// platform off): a managed library's Windows Store CPU, a shared object's 16 KB alignment for
/// Android (the Android module's setting).
fn kind_entries(kind: Kind, android_module: bool) -> Vec<Entry> {
    match kind {
        Kind::Managed => vec![entry("WindowsStoreApps", false, &[("CPU", "AnyCPU")])],
        Kind::SharedObject if android_module => {
            vec![entry("Android", false, &[("Is16KbAligned", "false")])]
        }
        _ => Vec::new(),
    }
}

fn entry(
    platform: &'static str,
    enabled: bool,
    settings: &[(&'static str, &'static str)],
) -> Entry {
    Entry {
        platform,
        enabled,
        settings: settings.to_vec(),
    }
}

fn eq(folder: &str, name: &str) -> bool {
    folder.eq_ignore_ascii_case(name)
}

/// The settings the Editor gives the plug-in at `path` (`Assets/...` or `Packages/<name>/...`)
/// when its `.meta` has none. `android_module`: the installation has the Android module, which adds
/// a shared object's 16 KB alignment setting. None for a file the PluginImporter does not import.
pub fn defaults(path: &str, managed: bool, android_module: bool) -> Option<Defaults> {
    let kind = kind(path, managed)?;
    let mut parts: Vec<&str> = path.split('/').collect();
    parts.pop();
    // The folders inside the asset root: below Assets, or below the package's own folder.
    let (in_assets, folders) = match parts.first() {
        Some(&"Assets") => (true, &parts[1..]),
        Some(&"Packages") if parts.len() >= 2 => (false, &parts[2..]),
        _ => (false, &parts[..]),
    };
    let mut any = false;
    let mut editor = false;
    let mut editor_settings: Vec<(&'static str, &'static str)> = Vec::new();
    let mut entries: Vec<Entry> = Vec::new();
    let architecture = folders
        .windows(2)
        .rev()
        .find_map(|pair| eq(pair[0], "Plugins").then(|| Cpu::of(pair[1])).flatten());
    let platform = match folders {
        [plugins, folder, ..] if in_assets && eq(plugins, "Plugins") => platform_folder(folder),
        _ => None,
    };
    if folders.iter().any(|folder| eq(folder, "Editor")) {
        editor = true;
        if let Some(cpu) = folders.iter().rev().find_map(|folder| Cpu::of(folder)) {
            editor_settings.push(("CPU", cpu.name()));
        }
    } else if let Some(cpu) = architecture {
        any = true;
        editor_settings.push(("CPU", cpu.name()));
        entries.extend(cpu.players());
    } else if let (Some(platform), false) = (platform, kind == Kind::Android) {
        entries.push(match platform {
            "Android" if kind == Kind::SharedObject => match android_abi(&folders[2..]) {
                Some(cpu) => entry(platform, true, &[("CPU", cpu)]),
                None => entry(platform, false, &[]),
            },
            "iOS" | "VisionOS" => {
                // Dynamic frameworks and libraries are embedded into the app.
                let lower = path.to_ascii_lowercase();
                let embed = lower.ends_with(".framework") || lower.ends_with(".dylib");
                entry(
                    platform,
                    true,
                    &[(
                        "AddToEmbeddedBinaries",
                        if embed { "true" } else { "false" },
                    )],
                )
            }
            "WindowsStoreApps" => entry(platform, true, &[("CPU", "AnyCPU")]),
            _ => entry(platform, true, &[]),
        });
    } else {
        match kind {
            Kind::Managed | Kind::SharedObject | Kind::AnyPlatform => any = true,
            Kind::WindowsLibrary => {
                editor = true;
                editor_settings.push(("CPU", "AnyCPU"));
                entries.push(entry("Win", true, &[("CPU", "AnyCPU")]));
                entries.push(entry("Win64", true, &[("CPU", "AnyCPU")]));
                entries.push(entry("WindowsStoreApps", false, &[("CPU", "AnyCPU")]));
            }
            Kind::MacLibrary => {
                editor = true;
                entries.push(entry("OSXUniversal", true, &[("CPU", "AnyCPU")]));
            }
            Kind::Android => entries.push(entry("Android", true, &[])),
            Kind::Apple => {
                for platform in ["iOS", "tvOS", "VisionOS"] {
                    entries.push(entry(platform, true, &[]));
                }
            }
            Kind::Web => entries.push(entry("WebGL", true, &[])),
        }
    }
    // Settings of the kind whatever its platforms: added to its platform's entry, or the entry
    // with the platform off.
    for kind_entry in kind_entries(kind, android_module) {
        match entries
            .iter_mut()
            .find(|e| e.platform == kind_entry.platform)
        {
            Some(existing) => {
                for setting in kind_entry.settings {
                    if !existing.settings.iter().any(|(key, _)| *key == setting.0) {
                        existing.settings.push(setting);
                    }
                }
            }
            None => entries.push(kind_entry),
        }
    }
    // The importer has initialized its defaults.
    editor_settings.push(("DefaultValueInitialized", "true"));
    entries.push(entry("Any", any, &[]));
    entries.push(Entry {
        platform: "Editor",
        enabled: editor,
        settings: editor_settings,
    });
    for entry in &mut entries {
        entry.settings.sort_by(|a, b| a.0.cmp(b.0));
    }
    entries.sort_by(|a, b| a.platform.cmp(b.platform));
    Some(Defaults {
        overridable: parts.first() == Some(&"Packages"),
        entries,
    })
}

impl Entry {
    /// The entry as the `.meta` reader gives it (`enabled`, `settings`).
    fn value(&self) -> Value {
        let settings = self
            .settings
            .iter()
            .map(|(key, value)| (key.to_string(), Value::Scalar(value.to_string())))
            .collect::<Vec<_>>();
        Value::Map(
            vec![
                (
                    "enabled".to_string(),
                    Value::Scalar((self.enabled as u8).to_string()),
                ),
                ("settings".to_string(), Value::Map(settings.into())),
            ]
            .into(),
        )
    }
}

impl Defaults {
    /// The PluginImporter section of a `.meta` as the Editor writes it with these settings.
    pub fn section(&self) -> String {
        let mut out = String::from(
            "PluginImporter:\n  externalObjects: {}\n  serializedVersion: 3\n  iconMap: {}\n  \
             executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n",
        );
        out.push_str(&format!(
            "  isOverridable: {}\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n",
            self.overridable as u8
        ));
        for entry in &self.entries {
            out.push_str(&format!(
                "    {}:\n      enabled: {}\n",
                entry.platform, entry.enabled as u8
            ));
            if entry.settings.is_empty() {
                out.push_str("      settings: {}\n");
            } else {
                out.push_str("      settings:\n");
                for (key, value) in &entry.settings {
                    out.push_str(&format!("        {key}: {value}\n"));
                }
            }
        }
        out.push_str("  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        out
    }

    /// The section's settings as the `.meta` reader gives them (the map under `PluginImporter:`).
    pub fn importer(&self) -> Value {
        unbound_yaml::parse_value(&self.section())
            .ok()
            .and_then(|meta| meta.get("PluginImporter").cloned())
            .unwrap_or_default()
    }
}

/// A `.meta`'s PluginImporter settings with these defaults where it has none: its own section's
/// settings kept, the defaults' platform settings and the settings it lacks added. Every reader
/// of a plug-in's settings (the compile graph, the native libraries, the managed importer) reads
/// them so.
pub fn with_defaults(meta: &Value, defaults: &Defaults) -> Value {
    let importer = defaults.importer();
    let (Some(section), Some(defaults)) = (
        meta.get("PluginImporter").and_then(Value::as_map),
        importer.as_map(),
    ) else {
        return importer;
    };
    let mut merged = section.clone();
    for (key, value) in defaults.iter() {
        if key == "platformData" || merged.get(key).is_none() {
            merged.insert(key, value.clone());
        }
    }
    Value::Map(merged)
}

/// Whether the importer section of a `.meta` without platform settings (its PluginImporter
/// section, or another importer's) holds only what these defaults write: then the Editor's
/// section with the defaults can take its place without losing a setting.
pub fn section_holds_only_defaults(meta: &Value, defaults: &Defaults) -> bool {
    let importer = defaults.importer();
    let Some(section) = meta.as_map().and_then(|m| {
        m.iter()
            .find(|(key, _)| key.ends_with("Importer"))
            .map(|(_, section)| section)
    }) else {
        return true;
    };
    let Some(section) = section.as_map() else {
        return true;
    };
    section.iter().all(|(key, value)| {
        key == "platformData" || importer.get(key).is_some_and(|default| default == value)
    })
}

/// Whether a `.meta`'s PluginImporter settings say on which platforms the plug-in is: without them
/// (its GUID alone, a section without `platformData`, another importer's section) the Editor
/// gives it the defaults.
pub fn has_platform_settings(meta: &Value) -> bool {
    meta.get("PluginImporter")
        .and_then(|importer| importer.get("platformData"))
        .is_some_and(|data| !matches!(data, Value::Null))
}

/// The CPU settings of the Editor platform that name one of its architectures (`""` is none).
const EDITOR_CPUS: [&str; 4] = ["", "AnyCPU", "x86_64", "ARM64"];

/// A `.meta`'s PluginImporter settings (of the plug-in at `path`) as the importer reads them
/// (matches the Unity 6000.0.71f1 Editor, tests/projects/plugindefaults `Assets/Controls/*`):
/// - the settings of its kind it lacks are added, in their platform's entry (the platform off);
/// - an Editor CPU setting that is none of the Editor's architectures (`x86`, which the Editor
///   gives a plug-in in `Plugins/x86`) turns the Editor off and excludes it from "Any Platform".
///
/// Settings in the old sequence layout are read as they are. The defaults are not read this way:
/// the Editor answers with them as they are until the `.meta` is written.
pub fn read_settings(importer: &mut Value, path: &str, managed: bool, android_module: bool) {
    let Some(Value::Map(data)) = importer
        .as_map_mut()
        .and_then(|map| map.get_mut("platformData"))
    else {
        return;
    };
    if let Some(kind) = kind(path, managed) {
        for kind_entry in kind_entries(kind, android_module) {
            if data.get(kind_entry.platform).is_none() {
                data.insert(kind_entry.platform, kind_entry.value());
            }
        }
    }
    let cpu = data
        .get("Editor")
        .and_then(|editor| editor.get("settings"))
        .and_then(|settings| settings.get("CPU"))
        .and_then(Value::as_str)
        .unwrap_or("");
    if EDITOR_CPUS.contains(&cpu) {
        return;
    }
    if let Some(editor) = data.get_mut("Editor").and_then(Value::as_map_mut) {
        editor.insert("enabled", Value::Scalar("0".into()));
    }
    let any = match data.get_mut("Any") {
        Some(any) => any,
        None => {
            data.insert("Any", Value::Map(Default::default()));
            data.get_mut("Any").expect("inserted")
        }
    };
    let Some(any) = any.as_map_mut() else {
        return;
    };
    if !matches!(any.get("settings"), Some(Value::Map(_))) {
        any.insert("settings", Value::Map(Default::default()));
    }
    if let Some(settings) = any.get_mut("settings").and_then(Value::as_map_mut) {
        settings.insert("Exclude Editor", Value::Scalar("1".into()));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The entries as `Platform=enabled{Key=Value,...}`, the Editor's DefaultValueInitialized left
    /// out, and ` overridable` for a package's plug-in.
    fn summary(defaults: &Defaults) -> String {
        let mut out = defaults
            .entries
            .iter()
            .map(|e| {
                let settings: Vec<_> = e
                    .settings
                    .iter()
                    .filter(|(key, _)| *key != "DefaultValueInitialized")
                    .map(|(key, value)| format!("{key}={value}"))
                    .collect();
                let settings = if settings.is_empty() {
                    String::new()
                } else {
                    format!("{{{}}}", settings.join(","))
                };
                format!("{}={}{settings}", e.platform, e.enabled as u8)
            })
            .collect::<Vec<_>>()
            .join(" ");
        if defaults.overridable {
            out.push_str(" overridable");
        }
        out
    }

    /// What the Editor wrote for these plug-ins (tests/projects/plugindefaults `*.labelled`, the
    /// installation with the Android module).
    #[test]
    fn defaults_match_the_editors_by_kind_and_folder() {
        let cases = [
            (
                "Assets/Plugins/Managed.dll",
                true,
                "Any=1 Editor=0 WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/native.dll",
                false,
                "Any=0 Editor=1{CPU=AnyCPU} Win=1{CPU=AnyCPU} Win64=1{CPU=AnyCPU} WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/libnative.so",
                false,
                "Android=0{Is16KbAligned=false} Any=1 Editor=0",
            ),
            (
                "Assets/Plugins/libmac.dylib",
                false,
                "Any=0 Editor=1 OSXUniversal=1{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/Kit.bundle",
                false,
                "Any=0 Editor=1 OSXUniversal=1{CPU=AnyCPU}",
            ),
            ("Assets/Plugins/libstatic.a", false, "Any=1 Editor=0"),
            ("Assets/Plugins/Kit.xcframework", false, "Any=1 Editor=0"),
            (
                "Assets/Plugins/library.jar",
                false,
                "Android=1 Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Lib.androidlib",
                false,
                "Android=1 Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Native.swift",
                false,
                "Any=0 Editor=0 VisionOS=1 iOS=1 tvOS=1",
            ),
            (
                "Assets/Plugins/prelude.jspre",
                false,
                "Any=0 Editor=0 WebGL=1",
            ),
            (
                "Assets/Plugins/x86/ManagedX86.dll",
                true,
                "Any=1 Editor=0{CPU=x86} Linux64=0{CPU=None} OSXUniversal=0{CPU=x86} Win=1{CPU=x86} Win64=0{CPU=None} WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/x86_64/Bridge.mm",
                false,
                "Any=1 Editor=0{CPU=x86_64} Linux64=1{CPU=x86_64} OSXUniversal=1{CPU=x86_64} Win=0{CPU=None} Win64=1{CPU=x86_64}",
            ),
            (
                "Assets/Plugins/ARM64/libnative4.so",
                false,
                "Android=0{Is16KbAligned=false} Any=1 Editor=0{CPU=ARM64} Linux64=0{CPU=None} OSXUniversal=1{CPU=ARM64} Win=0{CPU=None} Win64=1{CPU=ARM64}",
            ),
            (
                "Assets/plugins/x86_64/native17.dll",
                false,
                "Any=1 Editor=0{CPU=x86_64} Linux64=1{CPU=x86_64} OSXUniversal=1{CPU=x86_64} Win=0{CPU=None} Win64=1{CPU=x86_64}",
            ),
            (
                "Assets/Plugins/arm64/native18.dll",
                false,
                "Any=1 Editor=0{CPU=ARM64} Linux64=0{CPU=None} OSXUniversal=1{CPU=ARM64} Win=0{CPU=None} Win64=1{CPU=ARM64}",
            ),
            (
                "Assets/Plugins/x64/native27.dll",
                false,
                "Any=1 Editor=0{CPU=x86_64} Linux64=1{CPU=x86_64} OSXUniversal=1{CPU=x86_64} Win=0{CPU=None} Win64=1{CPU=x86_64}",
            ),
            (
                "Assets/Plugins/Android/ManagedAndroid.dll",
                true,
                "Android=1 Any=0 Editor=0 WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/Android/libs/x86/libnative8.so",
                false,
                "Android=1{CPU=x86,Is16KbAligned=false} Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Android/libs/armeabi/libnative9.so",
                false,
                "Android=0{Is16KbAligned=false} Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Android/libs/mips/libnative24.so",
                false,
                "Android=1{CPU=ARMv7,Is16KbAligned=false} Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Android/libs/armeabi-v7a/x86_64/libnative26.so",
                false,
                "Android=1{CPU=x86_64,Is16KbAligned=false} Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Android/libs/arm64-v8a/libstatic.a",
                false,
                "Android=1 Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/android/libnative18.so",
                false,
                "Android=1{CPU=ARMv7,Is16KbAligned=false} Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/Android/More/bridge.jslib",
                false,
                "Android=1 Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/iOS/libmac6.dylib",
                false,
                "Any=0 Editor=0 iOS=1{AddToEmbeddedBinaries=true}",
            ),
            (
                "Assets/Plugins/iOS/libnative13.so",
                false,
                "Android=0{Is16KbAligned=false} Any=0 Editor=0 iOS=1{AddToEmbeddedBinaries=false}",
            ),
            (
                "Assets/Plugins/iOS/Kit.xcframework",
                false,
                "Any=0 Editor=0 iOS=1{AddToEmbeddedBinaries=false}",
            ),
            (
                "Assets/Plugins/iOS/More/bridge.jslib",
                false,
                "Any=0 Editor=0 iOS=1{AddToEmbeddedBinaries=false}",
            ),
            (
                "Assets/Plugins/iOS/More/library.aar",
                false,
                "Android=1 Any=0 Editor=0",
            ),
            (
                "Assets/Plugins/visionOS/native25.dll",
                false,
                "Any=0 Editor=0 VisionOS=1{AddToEmbeddedBinaries=false}",
            ),
            (
                "Assets/Plugins/visionOS/More/Kit.framework",
                false,
                "Any=0 Editor=0 VisionOS=1{AddToEmbeddedBinaries=true}",
            ),
            (
                "Assets/Plugins/WebGL/More/Bridge.mm",
                false,
                "Any=0 Editor=0 WebGL=1",
            ),
            (
                "Assets/Plugins/Metro/native16.dll",
                false,
                "Any=0 Editor=0 WindowsStoreApps=1{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/WSA/ManagedWSA.dll",
                true,
                "Any=0 Editor=0 WindowsStoreApps=1{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/tvOS/native24.dll",
                false,
                "Any=0 Editor=1{CPU=AnyCPU} Win=1{CPU=AnyCPU} Win64=1{CPU=AnyCPU} WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/Editor/libnative15.so",
                false,
                "Android=0{Is16KbAligned=false} Any=0 Editor=1",
            ),
            ("Assets/Plugins/Editor/library.jar", false, "Any=0 Editor=1"),
            (
                "Assets/Editor/Plugins/x86_64/native21.dll",
                false,
                "Any=0 Editor=1{CPU=x86_64}",
            ),
            (
                "Assets/Plugins/x86_64/Editor/native19.dll",
                false,
                "Any=0 Editor=1{CPU=x86_64}",
            ),
            (
                "Assets/Foo/Editor/x86_64/native31.dll",
                false,
                "Any=0 Editor=1{CPU=x86_64}",
            ),
            (
                "Assets/Plugins/Android/Editor/libnative21.so",
                false,
                "Android=0{Is16KbAligned=false} Any=0 Editor=1",
            ),
            (
                "Assets/EditorTools/native22.dll",
                false,
                "Any=0 Editor=1{CPU=AnyCPU} Win=1{CPU=AnyCPU} Win64=1{CPU=AnyCPU} WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/editor/ManagedLowerEditor.dll",
                true,
                "Any=0 Editor=1 WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Foo/Plugins/x86_64/native12.dll",
                false,
                "Any=1 Editor=0{CPU=x86_64} Linux64=1{CPU=x86_64} OSXUniversal=1{CPU=x86_64} Win=0{CPU=None} Win64=1{CPU=x86_64}",
            ),
            (
                "Assets/Foo/Plugins/Android/libnative22.so",
                false,
                "Android=0{Is16KbAligned=false} Any=1 Editor=0",
            ),
            (
                "Assets/Foo/Plugins/WSA/native33.dll",
                false,
                "Any=0 Editor=1{CPU=AnyCPU} Win=1{CPU=AnyCPU} Win64=1{CPU=AnyCPU} WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Plugins/Foo/x86_64/native14.dll",
                false,
                "Any=0 Editor=1{CPU=AnyCPU} Win=1{CPU=AnyCPU} Win64=1{CPU=AnyCPU} WindowsStoreApps=0{CPU=AnyCPU}",
            ),
            (
                "Assets/Libs/arm64-v8a/libnative12.so",
                false,
                "Android=0{Is16KbAligned=false} Any=1 Editor=0",
            ),
            (
                "Packages/com.unbound.plugindefaults/Runtime/ManagedPackage.dll",
                true,
                "Any=1 Editor=0 WindowsStoreApps=0{CPU=AnyCPU} overridable",
            ),
            (
                "Packages/com.unbound.plugindefaults/Plugins/x86_64/native38.dll",
                false,
                "Any=1 Editor=0{CPU=x86_64} Linux64=1{CPU=x86_64} OSXUniversal=1{CPU=x86_64} Win=0{CPU=None} Win64=1{CPU=x86_64} overridable",
            ),
            (
                "Packages/com.unbound.plugindefaults/Plugins/WebGL/native37.dll",
                false,
                "Any=0 Editor=1{CPU=AnyCPU} Win=1{CPU=AnyCPU} Win64=1{CPU=AnyCPU} WindowsStoreApps=0{CPU=AnyCPU} overridable",
            ),
            (
                "Packages/com.unbound.plugindefaults/Plugins/Android/libnative27.so",
                false,
                "Android=0{Is16KbAligned=false} Any=1 Editor=0 overridable",
            ),
        ];
        for (path, managed, expected) in cases {
            let found = defaults(path, managed, true).expect(path);
            assert_eq!(summary(&found), expected, "{path}");
            // Every plug-in's Editor entry says its defaults are initialized.
            let editor = found
                .entries
                .iter()
                .find(|e| e.platform == "Editor")
                .unwrap();
            assert!(
                editor
                    .settings
                    .contains(&("DefaultValueInitialized", "true")),
                "{path}"
            );
        }
    }

    #[test]
    fn the_section_is_written_as_the_editor_writes_it() {
        // tests/projects/plugindefaults `Assets/Plugins/x86/native2.dll.labelled`.
        let expected = "PluginImporter:\n  externalObjects: {}\n  serializedVersion: 3\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n    Any:\n      enabled: 1\n      settings: {}\n    Editor:\n      enabled: 0\n      settings:\n        CPU: x86\n        DefaultValueInitialized: true\n    Linux64:\n      enabled: 0\n      settings:\n        CPU: None\n    OSXUniversal:\n      enabled: 0\n      settings:\n        CPU: x86\n    Win:\n      enabled: 1\n      settings:\n        CPU: x86\n    Win64:\n      enabled: 0\n      settings:\n        CPU: None\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
        let found = defaults("Assets/Plugins/x86/native2.dll", false, true).unwrap();
        assert_eq!(found.section(), expected);
        // The reader's form of it.
        let importer = found.importer();
        let editor = importer
            .get("platformData")
            .and_then(|d| d.get("Editor"))
            .unwrap();
        assert_eq!(editor.get("enabled").and_then(Value::as_bool), Some(false));
        assert_eq!(
            editor
                .get("settings")
                .and_then(|s| s.get("CPU"))
                .and_then(Value::as_str),
            Some("x86")
        );
    }

    #[test]
    fn without_the_android_module_a_shared_object_has_no_android_setting() {
        let found = defaults("Assets/Plugins/libnative.so", false, false).unwrap();
        assert_eq!(summary(&found), "Any=1 Editor=0");
        let android = defaults("Assets/Plugins/Android/libnative.so", false, false).unwrap();
        assert_eq!(summary(&android), "Android=1{CPU=ARMv7} Any=0 Editor=0");
    }

    #[test]
    fn other_files_are_no_plugins() {
        assert_eq!(kind("Assets/Plugins/readme.txt", false), None);
        assert_eq!(kind("Assets/Plugins/Lib.pdb", false), None);
        assert!(defaults("Assets/Plugins/noextension", false, true).is_none());
        assert_eq!(kind("Assets/Plugins/Other.DLL", true), Some(Kind::Managed));
    }

    fn read(meta: &str, path: &str, managed: bool) -> Value {
        let mut importer = unbound_yaml::parse_value(meta)
            .unwrap()
            .get("PluginImporter")
            .cloned()
            .unwrap();
        read_settings(&mut importer, path, managed, true);
        importer
    }

    fn platform<'a>(importer: &'a Value, name: &str) -> Option<&'a Value> {
        importer.get("platformData")?.get(name)
    }

    /// tests/projects/plugindefaults `Assets/Controls/*`: what the importer answers for sections
    /// written by hand.
    #[test]
    fn a_section_is_read_with_its_kinds_settings_and_the_editors_architectures() {
        let any_only = "PluginImporter:\n  platformData:\n    Any:\n      enabled: 1\n      settings: {}\n    Editor:\n      enabled: 0\n      settings:\n        DefaultValueInitialized: true\n";
        // A managed library gets its Windows Store CPU, off.
        let managed = read(any_only, "Assets/Controls/AnyOnly.dll", true);
        let store = platform(&managed, "WindowsStoreApps").unwrap();
        assert_eq!(store.get("enabled").and_then(Value::as_bool), Some(false));
        assert_eq!(
            store
                .get("settings")
                .and_then(|s| s.get("CPU"))
                .and_then(Value::as_str),
            Some("AnyCPU")
        );
        // A shared object its Android alignment; a native library neither.
        let shared = read(any_only, "Assets/Controls/libsection.so", false);
        let android = platform(&shared, "Android").unwrap();
        assert_eq!(
            android
                .get("settings")
                .and_then(|s| s.get("Is16KbAligned"))
                .and_then(Value::as_str),
            Some("false")
        );
        assert!(platform(&shared, "WindowsStoreApps").is_none());
        // An Editor CPU of none of the Editor's architectures turns the Editor off and excludes it.
        let x86 = "PluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n      settings: {}\n    Editor:\n      enabled: 1\n      settings:\n        CPU: x86\n        DefaultValueInitialized: true\n";
        let x86 = read(x86, "Assets/Controls/EditorX86.dll", true);
        let editor = platform(&x86, "Editor").unwrap();
        assert_eq!(editor.get("enabled").and_then(Value::as_bool), Some(false));
        let any = platform(&x86, "Any").unwrap();
        assert_eq!(
            any.get("settings")
                .and_then(|s| s.get("Exclude Editor"))
                .and_then(Value::as_bool),
            Some(true)
        );
        // An OS setting and the Editor's own CPUs leave the Editor on.
        for settings in ["OS: Windows", "CPU: ARM64", "CPU: x86_64", "CPU: AnyCPU"] {
            let meta = format!(
                "PluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n      settings: {{}}\n    Editor:\n      enabled: 1\n      settings:\n        {settings}\n"
            );
            let importer = read(&meta, "Assets/Controls/EditorOnly.dll", true);
            let editor = platform(&importer, "Editor").unwrap();
            assert_eq!(
                editor.get("enabled").and_then(Value::as_bool),
                Some(true),
                "{settings}"
            );
        }
    }

    /// A section without platform settings keeps its own settings beside the defaults' platforms;
    /// the Editor's section takes its place in a write only when it holds nothing else.
    #[test]
    fn a_section_without_platform_settings_keeps_its_own() {
        let found = defaults("Assets/Plugins/Foo.dll", true, true).unwrap();
        let own = unbound_yaml::parse_value(
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nPluginImporter:\n  isExplicitlyReferenced: 1\n  defineConstraints:\n  - FOO\n",
        )
        .unwrap();
        let merged = with_defaults(&own, &found);
        assert_eq!(
            merged
                .get("isExplicitlyReferenced")
                .and_then(Value::as_bool),
            Some(true)
        );
        assert_eq!(
            merged
                .get("defineConstraints")
                .and_then(Value::as_seq)
                .map(|list| list.len()),
            Some(1)
        );
        let any = merged
            .get("platformData")
            .and_then(|d| d.get("Any"))
            .unwrap();
        assert_eq!(any.get("enabled").and_then(Value::as_bool), Some(true));
        assert!(!section_holds_only_defaults(&own, &found));
        // tests/projects/plugindefaults NoPlatforms.dll and DefaultImporter.dll: written with the
        // Editor's section.
        for meta in [
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 3\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef",
        ] {
            assert!(
                section_holds_only_defaults(&unbound_yaml::parse_value(meta).unwrap(), &found),
                "{meta}"
            );
        }
        let user_data = unbound_yaml::parse_value(
            "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nDefaultImporter:\n  userData: kept\n",
        )
        .unwrap();
        assert!(!section_holds_only_defaults(&user_data, &found));
    }
}
