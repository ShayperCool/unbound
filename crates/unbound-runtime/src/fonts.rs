//! The faces behind the managed engine's `Font` objects, by handle: project font files, Unity's
//! built-in `LegacyRuntime.ttf` and OS fonts. The managed side lays text out and keeps the glyph
//! atlas; it asks here for face metrics (`font-open`) and rasterized glyphs (`font_glyph`).
use anyhow::{Context, Result, bail};
use serde::Deserialize;
use serde_json::{Value as Json, json};
use std::{
    path::{Path, PathBuf},
    sync::{Arc, OnceLock, RwLock},
};
use unbound_assets::{
    AssetCache,
    font::{BuiltinFontImporter, FontFace, FontSettings, GlyphBitmap, LEGACY_RUNTIME_PATH_ID},
};
use unbound_yaml::Guid;

static FACES: RwLock<Vec<Arc<FontFace>>> = RwLock::new(Vec::new());

fn register(face: FontFace) -> u64 {
    let mut faces = FACES.write().unwrap();
    faces.push(Arc::new(face));
    faces.len() as u64
}

fn face(handle: u64) -> Option<Arc<FontFace>> {
    let faces = FACES.read().unwrap();
    faces.get((handle as usize).checked_sub(1)?).cloned()
}

/// A glyph of a face at `size` pixels per em; `style` is Unity's `FontStyle` (1 bold, 2 italic).
pub fn glyph(handle: u64, codepoint: u32, size: f32, style: i32) -> Option<GlyphBitmap> {
    let c = char::from_u32(codepoint)?;
    face(handle)?.render(c, size, style & 1 != 0, style & 2 != 0)
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct OpenRequest {
    /// A project font asset.
    pub guid: Option<String>,
    /// A font file.
    pub file: Option<String>,
    /// A built-in font: `LegacyRuntime`.
    pub builtin: Option<String>,
    /// OS font family names, in order of preference.
    #[serde(default)]
    pub names: Vec<String>,
}

/// Loads a face and describes it: `{handle, unitsPerEm, ascender, ..., settings}`.
pub fn open(request: &OpenRequest, unity_data: &Path) -> Result<Json> {
    let s = crate::state::state();
    let mut settings = None;
    let (face, source) = if let Some(guid) = &request.guid {
        let guid = Guid::parse(guid).context("bad guid")?;
        let asset = s.db.by_guid(guid).context("no such asset")?;
        let meta = std::fs::read_to_string(format!("{}.meta", asset.file.display()))
            .ok()
            .and_then(|text| unbound_yaml::parse_value(&text).ok())
            .unwrap_or(unbound_yaml::Value::Null);
        settings = Some(FontSettings::read(&meta));
        let data = std::fs::read(&asset.file)
            .with_context(|| format!("reading {}", asset.file.display()))?;
        (FontFace::from_bytes(data, 0)?, asset.file.clone())
    } else if let Some(file) = &request.file {
        let data = std::fs::read(file).with_context(|| format!("reading {file}"))?;
        (FontFace::from_bytes(data, 0)?, PathBuf::from(file))
    } else if let Some(name) = &request.builtin {
        if name != "LegacyRuntime" {
            bail!("unknown built-in font {name}");
        }
        builtin_face(unity_data)?
    } else {
        os_face(&request.names, unity_data)?
    };
    let m = face.metrics().clone();
    let handle = register(face);
    let mut info = json!({
        "handle": handle,
        "unitsPerEm": m.units_per_em,
        "ascender": m.ascender,
        "descender": m.descender,
        "lineGap": m.line_gap,
        "yMax": m.y_max,
        "yMin": m.y_min,
        "family": m.family,
        "style": m.style,
        "path": source,
    });
    if let Some(settings) = settings {
        info["settings"] = json!({
            "fontSize": settings.font_size,
            "textureCase": settings.texture_case,
            "characterSpacing": settings.character_spacing,
            "characterPadding": settings.character_padding,
            "fontNames": settings.font_names,
            "fallbackFonts": settings.fallback_fonts.iter().map(|g| g.to_string()).collect::<Vec<_>>(),
            "renderingMode": settings.rendering_mode,
            "ascentCalculationMode": settings.ascent_calculation_mode,
            "shouldRoundAdvanceValue": settings.should_round_advance_value,
        });
    }
    Ok(info)
}

pub fn has_char(handle: u64, codepoint: u32) -> bool {
    match (face(handle), char::from_u32(codepoint)) {
        (Some(face), Some(c)) => face.has_char(c),
        _ => false,
    }
}

/// `LegacyRuntime.ttf`: the Editor's own resources do not hold its data, an installed player's do.
/// The extracted font is cached globally, keyed by the content of the resource file.
fn builtin_face(unity_data: &Path) -> Result<(FontFace, PathBuf)> {
    static BUILTIN: OnceLock<Result<(Vec<u8>, PathBuf), String>> = OnceLock::new();
    let found = BUILTIN.get_or_init(|| {
        let cache = AssetCache::new(unbound_project::cache_root().join("builtin"));
        let importer = BuiltinFontImporter {
            path_id: LEGACY_RUNTIME_PATH_ID,
        };
        let mut errors = Vec::new();
        for file in default_resource_files(unity_data) {
            let meta = file.with_file_name("unity default resources.meta");
            match cache.get_or_import(&importer, &file, &meta) {
                Ok(font) => {
                    if let Err(e) = cache.save() {
                        tracing::debug!("saving the built-in cache index: {e:#}");
                    }
                    return Ok((font.data.clone(), file));
                }
                Err(e) => errors.push(format!("{}: {e:#}", file.display())),
            }
        }
        Err(errors.join("; "))
    });
    match found {
        Ok((data, file)) => Ok((FontFace::from_bytes(data.clone(), 0)?, file.clone())),
        Err(errors) => {
            // No player module installed: an OS font with the same metrics, if there is one.
            let names = ["Liberation Sans", "Arial", "Arimo", "DejaVu Sans"].map(String::from);
            if let Some(found) = find_os_font(&names) {
                static WARNED: OnceLock<()> = OnceLock::new();
                WARNED.get_or_init(|| {
                    tracing::warn!(
                        "LegacyRuntime.ttf is in no installed player's resources ({errors}); using {}",
                        found.display()
                    )
                });
                let data = std::fs::read(&found)?;
                return Ok((FontFace::from_bytes(data, 0)?, found));
            }
            bail!(
                "LegacyRuntime.ttf is not available: no installed Unity player module holds it ({errors})"
            )
        }
    }
}

/// The `unity default resources` files of an install: players first (they hold font data).
fn default_resource_files(unity_data: &Path) -> Vec<PathBuf> {
    let mut files: Vec<PathBuf> = walkdir::WalkDir::new(unity_data.join("PlaybackEngines"))
        .max_depth(8)
        .into_iter()
        .filter_map(|e| e.ok())
        .filter(|e| e.file_type().is_file() && e.file_name() == "unity default resources")
        .map(|e| e.into_path())
        .collect();
    files.sort();
    files.push(unity_data.join("Resources/unity default resources"));
    files
}

/// An OS font by family name; the built-in font when none is installed.
fn os_face(names: &[String], unity_data: &Path) -> Result<(FontFace, PathBuf)> {
    if let Some(path) = find_os_font(names) {
        let data = std::fs::read(&path)?;
        return Ok((FontFace::from_bytes(data, 0)?, path));
    }
    builtin_face(unity_data)
}

/// An installed font: family, style and file.
pub struct OsFont {
    pub family: String,
    pub style: String,
    pub path: PathBuf,
}

/// The OS's fonts, listed once (fontconfig when available, else the font directories).
pub fn os_fonts() -> &'static [OsFont] {
    static FONTS: OnceLock<Vec<OsFont>> = OnceLock::new();
    FONTS.get_or_init(|| {
        let mut fonts = fontconfig_list().unwrap_or_else(scan_font_dirs);
        fonts.sort_by(|a, b| a.family.cmp(&b.family).then(a.path.cmp(&b.path)));
        fonts
    })
}

fn find_os_font(names: &[String]) -> Option<PathBuf> {
    let fonts = os_fonts();
    let regular = |s: &str| {
        let s = s.to_ascii_lowercase();
        s == "regular" || s == "book" || s == "normal" || s == "roman"
    };
    for name in names {
        let candidates: Vec<&OsFont> = fonts
            .iter()
            .filter(|f| f.family.eq_ignore_ascii_case(name))
            .collect();
        if let Some(f) = candidates
            .iter()
            .find(|f| regular(&f.style))
            .or(candidates.first())
        {
            return Some(f.path.clone());
        }
    }
    None
}

fn fontconfig_list() -> Option<Vec<OsFont>> {
    let output = std::process::Command::new("fc-list")
        .args(["--format", "%{family[0]}\t%{style[0]}\t%{file}\n"])
        .output()
        .ok()?;
    if !output.status.success() {
        return None;
    }
    let text = String::from_utf8_lossy(&output.stdout);
    let fonts: Vec<OsFont> = text
        .lines()
        .filter_map(|line| {
            let mut parts = line.split('\t');
            let family = parts.next()?.to_string();
            let style = parts.next()?.to_string();
            let path = PathBuf::from(parts.next()?);
            let ext = path.extension()?.to_str()?.to_ascii_lowercase();
            (ext == "ttf" || ext == "otf").then_some(OsFont {
                family,
                style,
                path,
            })
        })
        .collect();
    (!fonts.is_empty()).then_some(fonts)
}

fn scan_font_dirs() -> Vec<OsFont> {
    let home = unbound_project::install::home();
    let dirs = [
        PathBuf::from("/usr/share/fonts"),
        PathBuf::from("/usr/local/share/fonts"),
        home.join(".local/share/fonts"),
        home.join(".fonts"),
        PathBuf::from("/System/Library/Fonts"),
        PathBuf::from("/Library/Fonts"),
        PathBuf::from("C:\\Windows\\Fonts"),
    ];
    let mut fonts = Vec::new();
    for dir in dirs {
        for entry in walkdir::WalkDir::new(dir)
            .into_iter()
            .filter_map(|e| e.ok())
        {
            let path = entry.path();
            let ext = path
                .extension()
                .and_then(|e| e.to_str())
                .map(str::to_ascii_lowercase);
            if !matches!(ext.as_deref(), Some("ttf" | "otf")) {
                continue;
            }
            let Ok(data) = std::fs::read(path) else {
                continue;
            };
            let Ok(face) = FontFace::from_bytes(data, 0) else {
                continue;
            };
            fonts.push(OsFont {
                family: face.metrics().family.clone(),
                style: face.metrics().style.clone(),
                path: path.to_path_buf(),
            });
        }
    }
    fonts
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn handles_start_at_one() {
        assert!(face(0).is_none());
        assert!(glyph(0, 'A' as u32, 12.0, 0).is_none());
    }

    #[test]
    fn finds_the_builtin_font_in_an_install() {
        let data = unbound_project::install::home().join("Unity/Hub/Editor/6000.0.71f1/Editor/Data");
        if !data.join("PlaybackEngines").is_dir() {
            return;
        }
        let files = default_resource_files(&data);
        assert!(
            files
                .last()
                .unwrap()
                .ends_with("Resources/unity default resources")
        );
    }
}
