//! Fonts: TrueType/OpenType faces rasterized the way Unity's FreeType-based dynamic fonts are, the
//! `TrueTypeFontImporter` settings of a `.meta`, and the font data of Unity's built-in
//! `LegacyRuntime.ttf`, which lives inside a player's `unity default resources`.
//!
//! Legacy dynamic glyphs and TextCore's glyphs (by index, with their distance fields) both go
//! through the bundled FreeType library (`font_legacy`, `font_textcore`).
use crate::{
    Importer,
    meta::{importer, int},
};
use anyhow::{Result, bail};
use serde::{Deserialize, Serialize};
use std::{
    path::Path,
    sync::{Arc, Mutex, OnceLock},
};
use ttf_parser::Face;

pub use crate::font_textcore::{TextCoreGlyph, TextCoreStrike, TextCoreTile, render_mode};
use unbound_yaml::{Guid, Value};

/// File extensions Unity's TrueTypeFontImporter handles.
pub const EXTENSIONS: &[&str] = &["ttf", "otf", "ttc", "dfont"];

/// The fileIDs of an imported font's objects.
pub const FONT_FILE_ID: i64 = 12_800_000;
pub const FONT_MATERIAL_FILE_ID: i64 = 2_100_000;
pub const FONT_TEXTURE_FILE_ID: i64 = 2_800_000;

/// The path id of the `LegacyRuntime` Font in `unity default resources`.
pub const LEGACY_RUNTIME_PATH_ID: i64 = 10102;

/// The face-wide metrics, in font units.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FaceMetrics {
    pub units_per_em: u16,
    /// FreeType's `face->ascender`: `hhea`, else `OS/2` typographic, else `OS/2` windows values.
    pub ascender: i16,
    pub descender: i16,
    pub line_gap: i16,
    /// The `head` bounding box.
    pub y_max: i16,
    pub y_min: i16,
    pub family: String,
    pub style: String,
}

/// Face metrics beyond [`FaceMetrics`], in font units: `(position, thickness)` of the underline and
/// strikeout, `(y offset, y size)` of super- and subscripts.
#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct FaceDetails {
    pub underline: (i16, i16),
    pub strikeout: (i16, i16),
    pub superscript: (i16, i16),
    pub subscript: (i16, i16),
}

/// A rasterized glyph. `coverage` holds `width * height` bytes, top row first.
#[derive(Debug, Clone, PartialEq)]
pub struct GlyphBitmap {
    /// In pixels, unrounded.
    pub advance: f32,
    /// Pixels from the pen position to the bitmap's left edge.
    pub left: i32,
    /// Pixels from the baseline up to the bitmap's top edge.
    pub top: i32,
    /// Baseline shift after bitmap synthesis, separate from the outline control box.
    pub top_offset: f32,
    pub width: i32,
    pub height: i32,
    /// The outline's control box in pixels, `(x_min, y_min, x_max, y_max)` relative to the pen on
    /// the baseline, unrounded; zero for an empty glyph.
    pub bounds: (f32, f32, f32, f32),
    pub coverage: Vec<u8>,
}

/// A loaded face: the font file's bytes and the face index in it.
pub struct FontFace {
    data: Arc<[u8]>,
    freetype: OnceLock<Mutex<Option<crate::font_freetype::FreeTypeFace>>>,
    index: u32,
    metrics: FaceMetrics,
}

impl FontFace {
    pub fn from_bytes(data: Vec<u8>, index: u32) -> Result<Self> {
        let metrics = {
            let face = Face::parse(&data, index).map_err(|e| anyhow::anyhow!("not a font: {e}"))?;
            read_metrics(&face)
        };
        Ok(Self {
            data: data.into(),
            freetype: OnceLock::new(),
            index,
            metrics,
        })
    }

    pub fn metrics(&self) -> &FaceMetrics {
        &self.metrics
    }

    pub fn data(&self) -> &[u8] {
        &self.data
    }

    fn face(&self) -> Face<'_> {
        Face::parse(&self.data, self.index).expect("the face parsed when it was loaded")
    }

    pub fn has_char(&self, c: char) -> bool {
        self.face().glyph_index(c).is_some_and(|g| g.0 != 0)
    }

    /// The glyph of `c` at `size` pixels per em; `None` when the font lacks it.
    pub fn render(&self, c: char, size: f32, bold: bool, italic: bool) -> Option<GlyphBitmap> {
        self.render_legacy(c, size, bold, italic, 0)
    }

    /// The glyph of `c` as a legacy dynamic font renders it. `mode` is the requesting font's
    /// `FontRenderingMode` (smooth, hinted smooth, hinted raster, OS default).
    pub fn render_legacy(
        &self,
        c: char,
        size: f32,
        bold: bool,
        italic: bool,
        mode: i32,
    ) -> Option<GlyphBitmap> {
        self.with_freetype(|face| face.render_legacy(c, size, bold, italic, mode))?
    }

    /// The face's own style as a Unity `FontStyle` (1 bold, 2 italic), from FreeType's style
    /// flags: what a request for that style finds without synthesis.
    pub fn legacy_style(&self) -> i32 {
        self.with_freetype(|face| face.style()).unwrap_or(0)
    }

    /// TextCore: the metrics of glyph `glyph` at `pixels` per em, loaded with FreeType load
    /// `flags` (TextCore's `GlyphLoadFlags`).
    pub fn textcore_glyph(&self, glyph: u32, pixels: i32, flags: i32) -> Option<TextCoreGlyph> {
        self.with_freetype(|face| face.textcore_glyph(glyph, pixels, flags))?
    }

    /// TextCore: glyph `glyph` at `pixels` per em rendered for an atlas in `render_mode` (a
    /// `GlyphRenderMode`), the distance fields with `padding` around them.
    pub fn textcore_render(
        &self,
        glyph: u32,
        pixels: i32,
        render_mode: i32,
        padding: i32,
    ) -> Option<TextCoreTile> {
        self.with_freetype(|face| face.textcore_render(glyph, pixels, render_mode, padding))?
    }

    /// Whether the face has colour glyphs (TextCore draws it in a colour atlas by default).
    pub fn has_color(&self) -> bool {
        self.with_freetype(|face| face.has_color()).unwrap_or(false)
    }

    /// TextCore: the strike a bitmap-only face uses for `pixels` per em; `None` for a scalable face.
    pub fn textcore_strike(&self, pixels: i32) -> Option<TextCoreStrike> {
        self.with_freetype(|face| face.textcore_strike(pixels))?
    }

    fn with_freetype<T>(
        &self,
        f: impl FnOnce(&mut crate::font_freetype::FreeTypeFace) -> T,
    ) -> Option<T> {
        let backend = self.freetype.get_or_init(|| {
            let face = crate::font_freetype::FreeTypeFace::new(self.data.clone(), self.index)
                .map_err(|error| tracing::warn!("{error:#}"))
                .ok();
            Mutex::new(face)
        });
        Some(f(backend.lock().unwrap().as_mut()?))
    }

    /// The glyph index of `c`; 0 (the missing glyph) when the font lacks it.
    pub fn glyph_index(&self, c: char) -> u16 {
        self.face().glyph_index(c).map_or(0, |g| g.0)
    }

    /// The number of faces in the font file (a collection has several).
    pub fn face_count(&self) -> u32 {
        ttf_parser::fonts_in_collection(&self.data).unwrap_or(1)
    }

    pub fn glyph_count(&self) -> u16 {
        self.face().number_of_glyphs()
    }

    /// The metrics TextCore's FaceInfo reports beyond [`FaceMetrics`], in font units.
    pub fn details(&self) -> FaceDetails {
        let face = self.face();
        let line =
            |m: Option<ttf_parser::LineMetrics>| m.map_or((0, 0), |m| (m.position, m.thickness));
        let script =
            |m: Option<ttf_parser::ScriptMetrics>| m.map_or((0, 0), |m| (m.y_offset, m.y_size));
        FaceDetails {
            underline: line(face.underline_metrics()),
            strikeout: line(face.strikeout_metrics()),
            superscript: script(face.superscript_metrics()),
            subscript: script(face.subscript_metrics()),
        }
    }

    /// Horizontal kerning, in font units, of the pairs whose first glyph is in `firsts` and whose
    /// second is in `seconds` (`None`: any glyph of the face; `firsts` likewise): GPOS pair
    /// adjustments, else the `kern` table. Only non-zero pairs are listed, each once.
    pub fn kerning(&self, firsts: Option<&[u16]>, seconds: Option<&[u16]>) -> Vec<(u16, u16, i16)> {
        use ttf_parser::{
            GlyphId,
            gpos::{PairAdjustment, PositioningSubtable},
        };
        let face = self.face();
        let every: Vec<u16> = (0..face.number_of_glyphs()).collect();
        let firsts = firsts.unwrap_or(&every);
        let seconds = seconds.unwrap_or(&every);
        let mut pairs: Vec<(u16, u16, i16)> = Vec::new();
        let mut seen = std::collections::HashSet::new();
        if let Some(gpos) = face.tables().gpos {
            for lookup in gpos.lookups {
                for i in 0..lookup.subtables.len() {
                    let Some(PositioningSubtable::Pair(pair)) =
                        lookup.subtables.get::<PositioningSubtable>(i)
                    else {
                        continue;
                    };
                    for &a in firsts {
                        let first = GlyphId(a);
                        match &pair {
                            PairAdjustment::Format1 { coverage, sets } => {
                                let Some(set) =
                                    coverage.get(first).and_then(|index| sets.get(index))
                                else {
                                    continue;
                                };
                                for &b in seconds {
                                    if let Some((v, _)) = set.get(GlyphId(b))
                                        && v.x_advance != 0
                                        && seen.insert((a, b))
                                    {
                                        pairs.push((a, b, v.x_advance));
                                    }
                                }
                            }
                            PairAdjustment::Format2 {
                                coverage,
                                classes,
                                matrix,
                            } => {
                                if coverage.get(first).is_none() {
                                    continue;
                                }
                                let class = classes.0.get(first);
                                for &b in seconds {
                                    if let Some((v, _)) =
                                        matrix.get((class, classes.1.get(GlyphId(b))))
                                        && v.x_advance != 0
                                        && seen.insert((a, b))
                                    {
                                        pairs.push((a, b, v.x_advance));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        if pairs.is_empty()
            && let Some(kern) = face.tables().kern
        {
            for sub in kern.subtables {
                if !sub.horizontal {
                    continue;
                }
                for &a in firsts {
                    for &b in seconds {
                        if let Some(v) = sub.glyphs_kerning(GlyphId(a), GlyphId(b))
                            && v != 0
                            && seen.insert((a, b))
                        {
                            pairs.push((a, b, v));
                        }
                    }
                }
            }
        }
        pairs
    }
}

fn read_metrics(face: &Face) -> FaceMetrics {
    let hhea = face.tables().hhea;
    let (mut ascender, mut descender, mut line_gap) =
        (hhea.ascender, hhea.descender, hhea.line_gap);
    if ascender == 0
        && descender == 0
        && let Some(os2) = face.tables().os2
    {
        ascender = os2.typographic_ascender();
        descender = os2.typographic_descender();
        line_gap = os2.typographic_line_gap();
        if ascender == 0 && descender == 0 {
            ascender = os2.windows_ascender();
            descender = -os2.windows_descender();
            line_gap = 0;
        }
    }
    let bbox = face.global_bounding_box();
    let name = |id: u16| {
        face.names()
            .into_iter()
            .filter(|n| n.name_id == id && n.is_unicode())
            .find_map(|n| n.to_string())
            .unwrap_or_default()
    };
    // FreeType's family_name and style_name: the typographic names first.
    let mut family = name(ttf_parser::name_id::TYPOGRAPHIC_FAMILY);
    if family.is_empty() {
        family = name(ttf_parser::name_id::FAMILY);
    }
    let mut style = name(ttf_parser::name_id::TYPOGRAPHIC_SUBFAMILY);
    if style.is_empty() {
        style = name(ttf_parser::name_id::SUBFAMILY);
    }
    FaceMetrics {
        units_per_em: face.units_per_em(),
        ascender,
        descender,
        line_gap,
        y_max: bbox.y_max,
        y_min: bbox.y_min,
        family,
        style,
    }
}

/// The `TrueTypeFontImporter` settings of a `.meta`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct FontSettings {
    /// The size the font's metrics (`ascent`, `lineHeight`) are given at.
    pub font_size: i32,
    /// `forceTextureCase`: -2 dynamic, -1 Unicode, 0 ASCII, 1 upper, 2 lower, 3 custom set.
    pub texture_case: i32,
    pub character_spacing: i32,
    pub character_padding: i32,
    pub include_font_data: bool,
    pub font_names: Vec<String>,
    pub fallback_fonts: Vec<Guid>,
    pub custom_characters: String,
    /// 0 smooth, 1 hinted smooth, 2 hinted raster, 3 OS default.
    pub rendering_mode: i32,
    /// 0 legacy (bounding box, as Unity 2.x), 1 face ascender, 2 face bounding box.
    pub ascent_calculation_mode: i32,
    pub should_round_advance_value: bool,
}

impl Default for FontSettings {
    fn default() -> Self {
        Self {
            font_size: 16,
            texture_case: -2,
            character_spacing: 0,
            character_padding: 1,
            include_font_data: true,
            font_names: Vec::new(),
            fallback_fonts: Vec::new(),
            custom_characters: String::new(),
            rendering_mode: 0,
            ascent_calculation_mode: 1,
            should_round_advance_value: true,
        }
    }
}

impl FontSettings {
    pub fn read(meta: &Value) -> Self {
        let i = importer(meta, "TrueTypeFontImporter");
        let d = Self::default();
        let int_or = |path: &str, default: i32| int(i, path).map(|v| v as i32).unwrap_or(default);
        Self {
            font_size: int_or("fontSize", d.font_size),
            texture_case: int_or("forceTextureCase", d.texture_case),
            character_spacing: int_or("characterSpacing", d.character_spacing),
            character_padding: int_or("characterPadding", d.character_padding),
            include_font_data: int_or("includeFontData", 1) != 0,
            font_names: i
                .get("fontNames")
                .and_then(Value::as_seq)
                .unwrap_or_default()
                .iter()
                .filter_map(|v| v.as_str().map(str::to_string))
                .filter(|s| !s.is_empty())
                .collect(),
            fallback_fonts: i
                .get("fallbackFontReferences")
                .and_then(Value::as_seq)
                .unwrap_or_default()
                .iter()
                .filter_map(|v| v.as_ref())
                .filter(|r| !r.is_null() && !r.guid.is_zero())
                .map(|r| r.guid)
                .collect(),
            custom_characters: i
                .get("customCharacters")
                .and_then(Value::as_str)
                .unwrap_or("")
                .to_string(),
            rendering_mode: int_or("fontRenderingMode", d.rendering_mode),
            // Metas written before the setting existed used the legacy calculation.
            ascent_calculation_mode: int_or(
                "ascentCalculationMode",
                if matches!(i, Value::Null) {
                    d.ascent_calculation_mode
                } else {
                    0
                },
            ),
            should_round_advance_value: int_or("shouldRoundAdvanceValue", 1) != 0,
        }
    }
}

/// The font file inside one of Unity's resource files.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BuiltinFont {
    #[serde(with = "serde_bytes")]
    pub data: Vec<u8>,
}

/// Extracts `m_FontData` of a Font object from a serialized file (`unity default resources`).
pub struct BuiltinFontImporter {
    pub path_id: i64,
}

impl Importer for BuiltinFontImporter {
    type Output = BuiltinFont;
    const KIND: &'static str = "builtin-fonts";
    const VERSION: u32 = 1;

    fn import(&self, _source: &Path, bytes: &[u8], _meta: &Value) -> Result<BuiltinFont> {
        Ok(BuiltinFont {
            data: extract_font_data(bytes, self.path_id)?,
        })
    }

    fn options_key(&self) -> String {
        self.path_id.to_string()
    }
}

/// `m_FontData` of the Font object `path_id`: the length-prefixed byte array that holds an sfnt.
pub fn extract_font_data(file: &[u8], path_id: i64) -> Result<Vec<u8>> {
    let object = crate::serialized::object(file, path_id)?;
    for k in (0..object.len().saturating_sub(8)).step_by(4) {
        let length = u32::from_le_bytes(object[k..k + 4].try_into()?) as usize;
        let tag = &object[k + 4..k + 8];
        let sfnt = tag == [0, 1, 0, 0] || tag == b"OTTO" || tag == b"true" || tag == b"ttcf";
        if sfnt && length >= 12 && k + 4 + length <= object.len() {
            let data = object[k + 4..k + 4 + length].to_vec();
            if Face::parse(&data, 0).is_ok() {
                return Ok(data);
            }
        }
    }
    bail!("the font object {path_id} has no font data")
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A test face: DejaVu or Liberation from the system, when present.
    fn system_face() -> Option<FontFace> {
        [
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        ]
        .iter()
        .find_map(|p| std::fs::read(p).ok())
        .and_then(|data| FontFace::from_bytes(data, 0).ok())
    }

    /// A composite glyph's parts are scaled one by one, as FreeType does: Liberation Sans 'ñ'
    /// (n and a tilde moved by an offset) at 90 pixels is 38.21875 wide, where scaling the composed
    /// outline gives 38.234375 (FreeType 2.14's unhinted metrics, which Unity reports).
    #[test]
    fn composite_glyphs_scale_their_components() {
        let Some(face) =
            std::fs::read("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf")
                .ok()
                .and_then(|data| FontFace::from_bytes(data, 0).ok())
        else {
            return;
        };
        let glyph = face.render('ñ', 90.0, false, false).unwrap();
        let (x_min, y_min, x_max, y_max) = glyph.bounds;
        assert_eq!(
            (x_max - x_min, y_max - y_min, x_min, y_max),
            (38.21875, 64.5625, 6.15625, 64.5625)
        );
    }

    #[test]
    fn glyph_metrics_scale_font_units() {
        let Some(face) = system_face() else { return };
        let m = face.metrics().clone();
        let face_ref = face.face();
        let glyph = face_ref.glyph_index('H').unwrap();
        let units = face_ref.glyph_hor_advance(glyph).unwrap() as f32;
        let bitmap = face.render('H', 32.0, false, false).unwrap();
        assert!((bitmap.advance - units * 32.0 / m.units_per_em as f32).abs() < 1e-3);
        // 'H' sits on the baseline and reaches about the cap height.
        let rect = face_ref.glyph_bounding_box(glyph).unwrap();
        let scale = 32.0 / m.units_per_em as f32;
        assert_eq!(bitmap.top, (rect.y_max as f32 * scale).ceil() as i32);
        assert_eq!(bitmap.left, (rect.x_min as f32 * scale).floor() as i32);
        assert_eq!(
            bitmap.height,
            bitmap.top - (rect.y_min as f32 * scale).floor() as i32
        );
        assert_eq!(
            bitmap.coverage.len(),
            (bitmap.width * bitmap.height) as usize
        );
        // The stems are solid.
        assert!(bitmap.coverage.iter().filter(|&&a| a == 255).count() > 20);
    }

    #[test]
    fn space_has_an_advance_and_no_pixels() {
        let Some(face) = system_face() else { return };
        let space = face.render(' ', 20.0, false, false).unwrap();
        assert!(space.advance > 0.0);
        assert_eq!(space.width, 0);
        assert!(face.render('\u{10FFFD}', 20.0, false, false).is_none());
    }

    #[test]
    fn bold_widens_and_italic_shears() {
        let Some(face) = system_face() else { return };
        let regular = face.render('l', 48.0, false, false).unwrap();
        let bold = face.render('l', 48.0, true, false).unwrap();
        assert_eq!(bold.advance, regular.advance);
        assert_eq!(bold.width, regular.width + 1);
        assert_eq!(bold.top as f32 + bold.top_offset, regular.top as f32 + 1.0);
        let italic = face.render('l', 48.0, false, true).unwrap();
        assert_eq!(italic.advance, regular.advance);
        assert!(italic.width > regular.width + 5);
    }

    #[test]
    fn kerning_pairs_first_glyphs_with_any_second() {
        let Some(face) = system_face() else { return };
        let (a, v) = (face.glyph_index('A'), face.glyph_index('V'));
        let within = face.kerning(Some(&[a, v]), Some(&[a, v]));
        let Some(&(_, _, av)) = within.iter().find(|p| p.0 == a && p.1 == v) else {
            return; // a face without an A-V pair
        };
        assert!(av < 0);
        let from_a = face.kerning(Some(&[a]), None);
        assert!(from_a.contains(&(a, v, av)));
        assert!(from_a.iter().all(|p| p.0 == a));
    }

    /// LegacyRuntime.ttf from an installed player's `unity default resources`, when there is one.
    #[test]
    fn extracts_the_builtin_font_from_a_player() {
        let Some(data) = unbound_project::install::installed_data("6000.0.71f1") else {
            return;
        };
        let Some(bytes) = unbound_project::install::playback_engines(&data)
            .iter()
            .find_map(|dir| {
                std::fs::read(dir.join("AndroidPlayer/Data/Resources/unity default resources")).ok()
            })
        else {
            return;
        };
        let data = extract_font_data(&bytes, LEGACY_RUNTIME_PATH_ID).unwrap();
        let face = FontFace::from_bytes(data, 0).unwrap();
        let m = face.metrics();
        // Unity serializes m_Ascent 11.7685546875 and m_LineSpacing 14.94873046875 at size 13.
        assert_eq!(m.units_per_em, 2048);
        assert_eq!(m.ascender as f32 * 13.0 / 2048.0, 11.768_555);
        assert_eq!(
            (m.ascender - m.descender + m.line_gap) as f32 * 13.0 / 2048.0,
            14.948_73
        );
        assert!(face.has_char('A'));
    }

    #[test]
    fn reads_font_importer_settings() {
        let meta = unbound_yaml::parse_value(
            "TrueTypeFontImporter:\n  fontSize: 20\n  forceTextureCase: -2\n  characterPadding: 1\n  fontNames:\n  - PT Sans\n  fallbackFontReferences:\n  - {fileID: 12800000, guid: 18f083d3640180e439e643e7931d7933, type: 3}\n  ascentCalculationMode: 2\n",
        )
        .unwrap();
        let s = FontSettings::read(&meta);
        assert_eq!(s.font_size, 20);
        assert_eq!(s.font_names, ["PT Sans"]);
        assert_eq!(s.fallback_fonts.len(), 1);
        assert_eq!(s.ascent_calculation_mode, 2);
        assert_eq!(FontSettings::read(&Value::Null), FontSettings::default());
    }
}
