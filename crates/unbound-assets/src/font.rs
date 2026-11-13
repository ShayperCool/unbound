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
    let (family, style) = face_names(face);
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

/// Bit 8 of the OS/2 table's `fsSelection` (at byte 62): the face's names are WWS-conformant.
const FS_SELECTION_WWS: u16 = 1 << 8;
const OS2_FS_SELECTION_OFFSET: usize = 62;
/// The `name` table's WWS family and subfamily ids.
const WWS_FAMILY: u16 = 21;
const WWS_SUBFAMILY: u16 = 22;

/// FreeType's `family_name` and `style_name` (`sfnt_load_face`): for a WWS-conformant face the
/// typographic names, else the WWS names first, then the typographic ones, then the plain
/// family and subfamily.
fn face_names(face: &Face) -> (String, String) {
    use ttf_parser::name_id::{FAMILY, SUBFAMILY, TYPOGRAPHIC_FAMILY, TYPOGRAPHIC_SUBFAMILY};
    let wws = face
        .raw_face()
        .table(ttf_parser::Tag::from_bytes(b"OS/2"))
        .and_then(|os2| os2.get(OS2_FS_SELECTION_OFFSET..OS2_FS_SELECTION_OFFSET + 2))
        .is_some_and(|b| u16::from_be_bytes([b[0], b[1]]) & FS_SELECTION_WWS != 0);
    let first = |ids: &[u16]| ids.iter().find_map(|&id| sfnt_name(face, id));
    if wws {
        (
            first(&[TYPOGRAPHIC_FAMILY, FAMILY]).unwrap_or_default(),
            first(&[TYPOGRAPHIC_SUBFAMILY, SUBFAMILY]).unwrap_or_default(),
        )
    } else {
        (
            first(&[WWS_FAMILY, TYPOGRAPHIC_FAMILY, FAMILY]).unwrap_or_default(),
            first(&[WWS_SUBFAMILY, TYPOGRAPHIC_SUBFAMILY, SUBFAMILY]).unwrap_or_default(),
        )
    }
}

/// The name record FreeType takes for `id` (`tt_face_get_name`): the last English Windows name,
/// else the first Windows name, unless a Macintosh name in English (else in Roman) exists and
/// the Windows one is not English; else the Macintosh name; else the last Unicode-platform one.
/// It is converted to ASCII as FreeType's names are, other characters becoming `?`.
fn sfnt_name(face: &Face, id: u16) -> Option<String> {
    use ttf_parser::PlatformId;
    const MAC_ENGLISH: u16 = 0;
    const MAC_ROMAN: u16 = 0;
    const WINDOWS_SYMBOL: u16 = 0;
    const WINDOWS_UNICODE_BMP: u16 = 1;
    const WINDOWS_UCS4: u16 = 10;
    let english = |language: u16| language & 0x3FF == 0x009;
    let (mut unicode, mut apple_english, mut apple_roman, mut windows) = (None, None, None, None);
    let mut windows_english = false;
    for name in face.names() {
        if name.name_id != id || name.name.is_empty() {
            continue;
        }
        match name.platform_id {
            PlatformId::Unicode | PlatformId::Iso => unicode = Some(name),
            PlatformId::Macintosh if name.language_id == MAC_ENGLISH => apple_english = Some(name),
            PlatformId::Macintosh if name.encoding_id == MAC_ROMAN => apple_roman = Some(name),
            PlatformId::Windows
                if (windows.is_none() || english(name.language_id))
                    && matches!(
                        name.encoding_id,
                        WINDOWS_SYMBOL | WINDOWS_UNICODE_BMP | WINDOWS_UCS4
                    ) =>
            {
                windows_english = english(name.language_id);
                windows = Some(name);
            }
            _ => {}
        }
    }
    let apple = apple_english.or(apple_roman);
    let ascii = |code: u32| match code {
        32..=127 => code as u8 as char,
        _ => '?',
    };
    let utf16 = |bytes: &[u8]| -> String {
        bytes
            .chunks_exact(2)
            .map(|pair| u16::from_be_bytes([pair[0], pair[1]]) as u32)
            .take_while(|&code| code != 0)
            .map(ascii)
            .collect()
    };
    match (windows, apple) {
        (Some(record), apple) if apple.is_none() || windows_english => Some(utf16(record.name)),
        (_, Some(record)) => Some(
            record
                .name
                .iter()
                .take_while(|&&b| b != 0)
                .map(|&b| ascii(b as u32))
                .collect(),
        ),
        _ => unicode.map(|record| utf16(record.name)),
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

/// The family and style FreeType gives face `index` of a font file (see [`FaceMetrics`]), or
/// None when it is not a font. Reads only the tables it needs, so a mapped file is not paged in.
pub fn face_family_style(data: &[u8], index: u32) -> Option<(String, String)> {
    Face::parse(data, index).ok().map(|face| face_names(&face))
}

/// The number of faces in a font file: those of a collection, else one.
pub fn face_count(data: &[u8]) -> u32 {
    ttf_parser::fonts_in_collection(data).unwrap_or(1)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A minimal sfnt with `name` records (platform, encoding, language, id, bytes) and, when
    /// given, an OS/2 table with that fsSelection.
    fn named_face(records: &[(u16, u16, u16, u16, Vec<u8>)], fs_selection: Option<u16>) -> Vec<u8> {
        let mut head = vec![0u8; 54];
        head[0..4].copy_from_slice(&0x0001_0000u32.to_be_bytes());
        head[12..16].copy_from_slice(&0x5F0F_3CF5u32.to_be_bytes());
        head[18..20].copy_from_slice(&1000u16.to_be_bytes());
        let mut hhea = vec![0u8; 36];
        hhea[0..4].copy_from_slice(&0x0001_0000u32.to_be_bytes());
        let mut maxp = vec![0u8; 6];
        maxp[0..4].copy_from_slice(&0x0000_5000u32.to_be_bytes());
        maxp[4..6].copy_from_slice(&1u16.to_be_bytes());
        let mut name = Vec::new();
        let mut strings = Vec::new();
        name.extend_from_slice(&0u16.to_be_bytes());
        name.extend_from_slice(&(records.len() as u16).to_be_bytes());
        name.extend_from_slice(&(6 + 12 * records.len() as u16).to_be_bytes());
        for (platform, encoding, language, id, bytes) in records {
            for v in [
                *platform,
                *encoding,
                *language,
                *id,
                bytes.len() as u16,
                strings.len() as u16,
            ] {
                name.extend_from_slice(&v.to_be_bytes());
            }
            strings.extend_from_slice(bytes);
        }
        name.extend_from_slice(&strings);
        let mut tables: Vec<(&[u8; 4], Vec<u8>)> = vec![
            (b"head", head),
            (b"hhea", hhea),
            (b"maxp", maxp),
            (b"name", name),
        ];
        if let Some(fs) = fs_selection {
            let mut os2 = vec![0u8; 78];
            os2[62..64].copy_from_slice(&fs.to_be_bytes());
            tables.push((b"OS/2", os2));
        }
        tables.sort_by_key(|(tag, _)| **tag);
        let mut font = Vec::new();
        font.extend_from_slice(&0x0001_0000u32.to_be_bytes());
        font.extend_from_slice(&(tables.len() as u16).to_be_bytes());
        font.extend_from_slice(&[0u8; 6]);
        let mut offset = 12 + 16 * tables.len();
        let mut body = Vec::new();
        for (tag, data) in &tables {
            font.extend_from_slice(*tag);
            font.extend_from_slice(&0u32.to_be_bytes());
            font.extend_from_slice(&(offset as u32).to_be_bytes());
            font.extend_from_slice(&(data.len() as u32).to_be_bytes());
            let mut padded = data.clone();
            padded.resize(data.len().div_ceil(4) * 4, 0);
            offset += padded.len();
            body.extend_from_slice(&padded);
        }
        font.extend_from_slice(&body);
        font
    }

    fn utf16(text: &str) -> Vec<u8> {
        text.encode_utf16().flat_map(u16::to_be_bytes).collect()
    }

    const WINDOWS: u16 = 3;
    const MAC: u16 = 1;
    const UNICODE: u16 = 0;

    /// FreeType's name choice: an English Windows name; a Macintosh English name before a
    /// Windows name in another language (an Arabic-only Windows family keeps its English
    /// Macintosh name, as the macOS Editor lists it); a non-English Windows name when nothing
    /// else exists, in ASCII with `?`; the Unicode platform last.
    #[test]
    fn face_names_follow_freetype() {
        let names = |records: &[(u16, u16, u16, u16, Vec<u8>)]| {
            face_family_style(&named_face(records, None), 0).unwrap()
        };
        let arabic = (WINDOWS, 1, 0x0C01, 1, utf16("النيل"));
        let mac = (MAC, 0, 0, 1, b"Al Nile".to_vec());
        assert_eq!(
            names(&[
                (UNICODE, 0, 0, 1, utf16("Al Nile U")),
                mac.clone(),
                arabic.clone()
            ])
            .0,
            "Al Nile"
        );
        assert_eq!(names(&[arabic.clone()]).0, "?????");
        assert_eq!(
            names(&[
                mac.clone(),
                arabic.clone(),
                (WINDOWS, 1, 0x0409, 1, utf16("Al Nile W"))
            ])
            .0,
            "Al Nile W"
        );
        assert_eq!(
            names(&[
                (WINDOWS, 1, 0x0409, 1, utf16("First")),
                (WINDOWS, 1, 0x0809, 1, utf16("Last"))
            ])
            .0,
            "Last"
        );
        assert_eq!(
            names(&[
                (UNICODE, 3, 0, 1, utf16("Plain")),
                (UNICODE, 3, 0, 1, utf16("Later"))
            ])
            .0,
            "Later"
        );
        assert_eq!(names(&[(MAC, 0, 0, 1, b"Caf\x8e".to_vec())]).0, "Caf?");
        // A Windows name in an unknown encoding is not taken.
        assert_eq!(
            names(&[
                (WINDOWS, 3, 0x0804, 1, utf16("PRC")),
                (UNICODE, 3, 0, 1, utf16("Fallback"))
            ])
            .0,
            "Fallback"
        );
    }

    /// The typographic family before the plain one; the WWS names first unless OS/2 says the
    /// names are WWS-conformant (fsSelection bit 8).
    #[test]
    fn face_names_take_typographic_and_wws_names_in_freetype_order() {
        let records = [
            (WINDOWS, 1, 0x0409, 1, utf16("Sans W3")),
            (WINDOWS, 1, 0x0409, 2, utf16("Regular")),
            (WINDOWS, 1, 0x0409, 16, utf16("Sans")),
            (WINDOWS, 1, 0x0409, 17, utf16("W3")),
            (WINDOWS, 1, 0x0409, 21, utf16("Sans WWS")),
        ];
        let names = |fs| face_family_style(&named_face(&records, fs), 0).unwrap();
        assert_eq!(names(None), ("Sans WWS".into(), "W3".into()));
        assert_eq!(names(Some(0)), ("Sans WWS".into(), "W3".into()));
        assert_eq!(names(Some(FS_SELECTION_WWS)), ("Sans".into(), "W3".into()));
        assert_eq!(face_count(&named_face(&records, None)), 1);
    }

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
