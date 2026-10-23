//! Fonts: TrueType/OpenType faces rasterized the way Unity's FreeType-based dynamic fonts are, the
//! `TrueTypeFontImporter` settings of a `.meta`, and the font data of Unity's built-in
//! `LegacyRuntime.ttf`, which lives inside a player's `unity default resources`.
//!
//! Metrics follow FreeType's conventions for an unhinted face ("Smooth" rendering mode, Unity's
//! default): the pixel size is the em size, a glyph's bitmap covers its control box rounded out to
//! whole pixels (`left`/`top` are FreeType's `bitmap_left`/`bitmap_top`), and the advance is the
//! scaled `hmtx` advance. Synthetic bold and italic follow `FT_GlyphSlot_Embolden` (the outline
//! grows by em/24 to the right and upwards, and so does the advance) and `FT_GlyphSlot_Oblique`
//! (a 0x0366A/0x10000 shear).
use crate::{
    Importer,
    meta::{importer, int},
};
use ab_glyph_rasterizer::{Rasterizer, point};
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::path::Path;
use ttf_parser::{Face, OutlineBuilder};
use unbound_yaml::{Guid, Value};

/// File extensions Unity's TrueTypeFontImporter handles.
pub const EXTENSIONS: &[&str] = &["ttf", "otf", "ttc", "dfont"];

/// The fileIDs of an imported font's objects.
pub const FONT_FILE_ID: i64 = 12_800_000;
pub const FONT_MATERIAL_FILE_ID: i64 = 2_100_000;
pub const FONT_TEXTURE_FILE_ID: i64 = 2_800_000;

/// The path id of the `LegacyRuntime` Font in `unity default resources`.
pub const LEGACY_RUNTIME_PATH_ID: i64 = 10102;

/// FreeType's oblique shear.
const OBLIQUE_SHEAR: f32 = 0x0366A as f32 / 65536.0;

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

/// A rasterized glyph. `coverage` holds `width * height` bytes, top row first.
#[derive(Debug, Clone, PartialEq)]
pub struct GlyphBitmap {
    /// In pixels, unrounded.
    pub advance: f32,
    /// Pixels from the pen position to the bitmap's left edge.
    pub left: i32,
    /// Pixels from the baseline up to the bitmap's top edge.
    pub top: i32,
    pub width: i32,
    pub height: i32,
    pub coverage: Vec<u8>,
}

/// A loaded face: the font file's bytes and the face index in it.
pub struct FontFace {
    data: Vec<u8>,
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
            data,
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
        let face = self.face();
        let glyph = face.glyph_index(c).filter(|g| g.0 != 0)?;
        let scale = size / self.metrics.units_per_em.max(1) as f32;
        let mut advance = face.glyph_hor_advance(glyph).unwrap_or(0) as f32 * scale;
        let shear = if italic { OBLIQUE_SHEAR } else { 0.0 };
        let mut outline = Outline {
            scale,
            shear,
            segments: Vec::new(),
            start: (0.0, 0.0),
            last: (0.0, 0.0),
        };
        face.outline_glyph(glyph, &mut outline);
        // FT_GlyphSlot_Embolden: em/24 pixels, for an empty glyph too (its advance grows).
        let strength = if bold { size / 24.0 } else { 0.0 };
        advance += strength;
        if outline.segments.is_empty() {
            return Some(GlyphBitmap {
                advance,
                left: 0,
                top: 0,
                width: 0,
                height: 0,
                coverage: Vec::new(),
            });
        }
        // The control box in pixels (26.6 precision, as FreeType scales the points).
        let (mut x_min, mut y_min, mut x_max, mut y_max) = (f32::MAX, f32::MAX, f32::MIN, f32::MIN);
        for segment in &outline.segments {
            for (x, y) in segment.points() {
                x_min = x_min.min(x);
                y_min = y_min.min(y);
                x_max = x_max.max(x);
                y_max = y_max.max(y);
            }
        }
        x_max += strength;
        y_max += strength;
        let left = x_min.floor() as i32;
        let bottom = y_min.floor() as i32;
        let right = x_max.ceil() as i32;
        let top = y_max.ceil() as i32;
        let width = (right - left).max(1);
        let height = (top - bottom).max(1);
        let mut raster = Rasterizer::new(width as usize, height as usize);
        let to = |(x, y): (f32, f32)| point(x - left as f32, top as f32 - y);
        for segment in &outline.segments {
            match *segment {
                Segment::Line(a, b) => raster.draw_line(to(a), to(b)),
                Segment::Quad(a, b, c) => raster.draw_quad(to(a), to(b), to(c)),
                Segment::Cubic(a, b, c, d) => raster.draw_cubic(to(a), to(b), to(c), to(d)),
            }
        }
        let mut coverage = vec![0u8; (width * height) as usize];
        raster.for_each_pixel_2d(|x, y, alpha| {
            coverage[(y * width as u32 + x) as usize] =
                (alpha.clamp(0.0, 1.0) * 255.0).round() as u8;
        });
        if strength > 0.0 {
            embolden(&mut coverage, width as usize, height as usize, strength);
        }
        Some(GlyphBitmap {
            advance,
            left,
            top,
            width,
            height,
            coverage,
        })
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
    let mut family = name(ttf_parser::name_id::TYPOGRAPHIC_FAMILY);
    if family.is_empty() {
        family = name(ttf_parser::name_id::FAMILY);
    }
    FaceMetrics {
        units_per_em: face.units_per_em(),
        ascender,
        descender,
        line_gap,
        y_max: bbox.y_max,
        y_min: bbox.y_min,
        family,
        style: name(ttf_parser::name_id::SUBFAMILY),
    }
}

/// Grows coverage by `strength` pixels to the right and upwards (rows are top first), like
/// FreeType's emboldening moves the right and top edges of an outline.
fn embolden(coverage: &mut [u8], width: usize, height: usize, strength: f32) {
    // The bitmap was made `ceil(strength)` larger on those sides; dilate into that space.
    let whole = strength.floor() as usize;
    let fraction = strength - whole as f32;
    let source = coverage.to_vec();
    let at = |x: isize, y: isize| -> f32 {
        if x < 0 || y < 0 || x >= width as isize || y >= height as isize {
            0.0
        } else {
            source[y as usize * width + x as usize] as f32
        }
    };
    // Horizontal pass: max over shifts 0..=whole, plus the fractional next shift.
    let mut horizontal = vec![0f32; width * height];
    for y in 0..height as isize {
        for x in 0..width as isize {
            let mut v = 0f32;
            for s in 0..=whole as isize {
                v = v.max(at(x - s, y));
            }
            v = v.max(at(x - whole as isize - 1, y) * fraction);
            horizontal[y as usize * width + x as usize] = v;
        }
    }
    let at_h = |x: usize, y: isize| -> f32 {
        if y < 0 || y >= height as isize {
            0.0
        } else {
            horizontal[y as usize * width + x]
        }
    };
    for y in 0..height as isize {
        for x in 0..width {
            let mut v = 0f32;
            for s in 0..=whole as isize {
                v = v.max(at_h(x, y + s));
            }
            v = v.max(at_h(x, y + whole as isize + 1) * fraction);
            coverage[y as usize * width + x] = v.round().min(255.0) as u8;
        }
    }
}

#[derive(Debug, Clone, Copy)]
enum Segment {
    Line((f32, f32), (f32, f32)),
    Quad((f32, f32), (f32, f32), (f32, f32)),
    Cubic((f32, f32), (f32, f32), (f32, f32), (f32, f32)),
}

impl Segment {
    /// Every point, control points included (FreeType's control box).
    fn points(&self) -> Vec<(f32, f32)> {
        match *self {
            Segment::Line(a, b) => vec![a, b],
            Segment::Quad(a, b, c) => vec![a, b, c],
            Segment::Cubic(a, b, c, d) => vec![a, b, c, d],
        }
    }
}

/// Collects a glyph outline in pixels (y up), quantized to 1/64 pixel as FreeType does.
struct Outline {
    scale: f32,
    shear: f32,
    segments: Vec<Segment>,
    start: (f32, f32),
    last: (f32, f32),
}

impl Outline {
    fn map(&self, x: f32, y: f32) -> (f32, f32) {
        let q = |v: f32| (v * 64.0).round() / 64.0;
        let (x, y) = (q(x * self.scale), q(y * self.scale));
        (x + y * self.shear, y)
    }
}

impl OutlineBuilder for Outline {
    fn move_to(&mut self, x: f32, y: f32) {
        self.start = self.map(x, y);
        self.last = self.start;
    }

    fn line_to(&mut self, x: f32, y: f32) {
        let p = self.map(x, y);
        self.segments.push(Segment::Line(self.last, p));
        self.last = p;
    }

    fn quad_to(&mut self, x1: f32, y1: f32, x: f32, y: f32) {
        let c = self.map(x1, y1);
        let p = self.map(x, y);
        self.segments.push(Segment::Quad(self.last, c, p));
        self.last = p;
    }

    fn curve_to(&mut self, x1: f32, y1: f32, x2: f32, y2: f32, x: f32, y: f32) {
        let c1 = self.map(x1, y1);
        let c2 = self.map(x2, y2);
        let p = self.map(x, y);
        self.segments.push(Segment::Cubic(self.last, c1, c2, p));
        self.last = p;
    }

    fn close(&mut self) {
        if self.last != self.start {
            self.segments.push(Segment::Line(self.last, self.start));
        }
        self.last = self.start;
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

/// The objects of a serialized file (format 9 and later): path id → (offset, size) of its data.
fn serialized_objects(file: &[u8]) -> Result<Vec<(i64, usize, usize)>> {
    let be32 = |o: usize| -> Result<u32> {
        Ok(u32::from_be_bytes(
            file.get(o..o + 4).context("truncated")?.try_into()?,
        ))
    };
    let be64 = |o: usize| -> Result<u64> {
        Ok(u64::from_be_bytes(
            file.get(o..o + 8).context("truncated")?.try_into()?,
        ))
    };
    let version = be32(8)?;
    if !(9..=30).contains(&version) {
        bail!("unsupported serialized file version {version}");
    }
    let (data_offset, big_endian, mut off) = if version >= 22 {
        (
            be64(32)? as usize,
            file.get(16).copied().unwrap_or(0) != 0,
            48,
        )
    } else {
        (
            be32(12)? as usize,
            file.get(16).copied().unwrap_or(0) != 0,
            20,
        )
    };
    if big_endian {
        bail!("big-endian serialized files are not supported");
    }
    let le32 = |o: usize| -> Result<u32> {
        Ok(u32::from_le_bytes(
            file.get(o..o + 4).context("truncated")?.try_into()?,
        ))
    };
    let le64 = |o: usize| -> Result<u64> {
        Ok(u64::from_le_bytes(
            file.get(o..o + 8).context("truncated")?.try_into()?,
        ))
    };
    // Unity version string, target platform, type tree flag.
    off += file[off..]
        .iter()
        .position(|&b| b == 0)
        .context("truncated")?
        + 1;
    off += 4;
    let type_trees = *file.get(off).context("truncated")? != 0;
    off += 1;
    if type_trees {
        bail!("serialized files with type trees are not supported here");
    }
    let type_count = le32(off)? as usize;
    off += 4;
    for _ in 0..type_count {
        let class_id = le32(off)? as i32;
        off += 4 + 1 + 2;
        if class_id == 114 || class_id < 0 {
            off += 16;
        }
        off += 16;
    }
    let object_count = le32(off)? as usize;
    off += 4;
    let mut objects = Vec::with_capacity(object_count);
    for _ in 0..object_count {
        off = (off + 3) & !3;
        let path_id = le64(off)? as i64;
        off += 8;
        let start = if version >= 22 {
            off += 8;
            le64(off - 8)? as usize
        } else {
            off += 4;
            le32(off - 4)? as usize
        };
        let size = le32(off)? as usize;
        off += 4 + 4;
        objects.push((path_id, data_offset + start, size));
    }
    Ok(objects)
}

/// `m_FontData` of the Font object `path_id`: the length-prefixed byte array that holds an sfnt.
pub fn extract_font_data(file: &[u8], path_id: i64) -> Result<Vec<u8>> {
    let (_, start, size) = serialized_objects(file)?
        .into_iter()
        .find(|o| o.0 == path_id)
        .with_context(|| format!("no object {path_id}"))?;
    let object = file
        .get(start..start + size)
        .context("the object lies outside the file")?;
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
        assert!((bold.advance - regular.advance - 2.0).abs() < 1e-3);
        assert_eq!(bold.width, regular.width + 2);
        assert_eq!(bold.top, regular.top + 2);
        let italic = face.render('l', 48.0, false, true).unwrap();
        assert_eq!(italic.advance, regular.advance);
        assert!(italic.width > regular.width + 5);
    }

    /// LegacyRuntime.ttf from an installed player's `unity default resources`, when there is one.
    #[test]
    fn extracts_the_builtin_font_from_a_player() {
        let Some(home) = std::env::var_os("HOME") else {
            return;
        };
        let file = Path::new(&home).join(
            "Unity/Hub/Editor/6000.0.71f1/Editor/Data/PlaybackEngines/AndroidPlayer/Data/Resources/unity default resources",
        );
        let Ok(bytes) = std::fs::read(&file) else {
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
