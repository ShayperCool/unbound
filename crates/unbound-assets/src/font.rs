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
use anyhow::{Result, bail};
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

/// Face metrics beyond [`FaceMetrics`], in font units: `(position, thickness)` of the underline and
/// strikeout, `(y offset, y size)` of super- and subscripts.
#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct FaceDetails {
    pub cap_height: i16,
    pub x_height: i16,
    pub underline: (i16, i16),
    pub strikeout: (i16, i16),
    pub superscript: (i16, i16),
    pub subscript: (i16, i16),
    pub space_advance: u16,
    /// Whether the font carries TrueType hinting programs (`fpgm`), which snap its heights to
    /// whole pixels where an unhinted font's are rounded up.
    pub hinted: bool,
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
    /// The outline's control box in pixels, `(x_min, y_min, x_max, y_max)` relative to the pen on
    /// the baseline, unrounded; zero for an empty glyph.
    pub bounds: (f32, f32, f32, f32),
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
        let glyph = self.face().glyph_index(c).filter(|g| g.0 != 0)?;
        self.render_glyph(glyph.0, size, bold, italic)
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
        let line = |m: Option<ttf_parser::LineMetrics>| m.map_or((0, 0), |m| (m.position, m.thickness));
        let script = |m: Option<ttf_parser::ScriptMetrics>| m.map_or((0, 0), |m| (m.y_offset, m.y_size));
        FaceDetails {
            cap_height: face.capital_height().unwrap_or(0),
            x_height: face.x_height().unwrap_or(0),
            underline: line(face.underline_metrics()),
            strikeout: line(face.strikeout_metrics()),
            superscript: script(face.superscript_metrics()),
            subscript: script(face.subscript_metrics()),
            space_advance: face
                .glyph_index(' ')
                .and_then(|g| face.glyph_hor_advance(g))
                .unwrap_or(0),
            hinted: face.raw_face().table(ttf_parser::Tag::from_bytes(b"fpgm")).is_some(),
        }
    }

    /// Horizontal kerning, in font units, of the pairs whose first glyph is in `firsts` and whose
    /// second is in `seconds` (`None`: any glyph of the face; `firsts` likewise): GPOS pair
    /// adjustments, else the `kern` table. Only non-zero pairs are listed, each once.
    pub fn kerning(&self, firsts: Option<&[u16]>, seconds: Option<&[u16]>) -> Vec<(u16, u16, i16)> {
        use ttf_parser::{GlyphId, gpos::{PairAdjustment, PositioningSubtable}};
        let face = self.face();
        let every: Vec<u16> = (0..face.number_of_glyphs()).collect();
        let firsts = firsts.unwrap_or(&every);
        let seconds = seconds.unwrap_or(&every);
        let mut pairs: Vec<(u16, u16, i16)> = Vec::new();
        let mut seen = std::collections::HashSet::new();
        if let Some(gpos) = face.tables().gpos {
            for lookup in gpos.lookups {
                for i in 0..lookup.subtables.len() {
                    let Some(PositioningSubtable::Pair(pair)) = lookup.subtables.get::<PositioningSubtable>(i) else {
                        continue;
                    };
                    for &a in firsts {
                        let first = GlyphId(a);
                        match &pair {
                            PairAdjustment::Format1 { coverage, sets } => {
                                let Some(set) = coverage.get(first).and_then(|index| sets.get(index)) else {
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
                            PairAdjustment::Format2 { coverage, classes, matrix } => {
                                if coverage.get(first).is_none() {
                                    continue;
                                }
                                let class = classes.0.get(first);
                                for &b in seconds {
                                    if let Some((v, _)) = matrix.get((class, classes.1.get(GlyphId(b))))
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

    /// Glyph `glyph` (an index) at `size` pixels per em.
    pub fn render_glyph(&self, glyph: u16, size: f32, bold: bool, italic: bool) -> Option<GlyphBitmap> {
        let face = self.face();
        let glyph = ttf_parser::GlyphId(glyph);
        if glyph.0 >= face.number_of_glyphs() {
            return None;
        }
        let scale = size / self.metrics.units_per_em.max(1) as f32;
        let mut advance = face.glyph_hor_advance(glyph).unwrap_or(0) as f32 * scale;
        let shear = if italic { OBLIQUE_SHEAR } else { 0.0 };
        let mut outline = Outline {
            scale,
            shear: 0.0,
            segments: Vec::new(),
            start: (0.0, 0.0),
            last: (0.0, 0.0),
        };
        outline_scaled(&face, glyph, &mut outline, 0);
        outline.shear(shear);
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
                bounds: (0.0, 0.0, 0.0, 0.0),
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
            bounds: (x_min, y_min, x_max, y_max),
            coverage,
        })
    }

    /// Glyph `glyph` at `size` pixels per em as a signed distance field, TextCore's SDF encoding:
    /// the bitmap of [`Self::render_glyph`] grown by `padding` pixels on every side (`coverage`
    /// holds `(width + 2 padding) * (height + 2 padding)` bytes, top row first), each pixel
    /// `0.5 + d / (2 (padding + 1))` where `d` is the distance in pixels from its center to the
    /// outline, positive inside. TextMeshPro's gradient scale is `padding + 1` to match.
    pub fn render_glyph_sdf(&self, glyph: u16, size: f32, padding: i32) -> Option<GlyphBitmap> {
        let mut bitmap = self.render_glyph(glyph, size, false, false)?;
        if bitmap.coverage.is_empty() {
            return Some(bitmap);
        }
        let face = self.face();
        let mut outline = Outline {
            scale: size / self.metrics.units_per_em.max(1) as f32,
            shear: 0.0,
            segments: Vec::new(),
            start: (0.0, 0.0),
            last: (0.0, 0.0),
        };
        outline_scaled(&face, ttf_parser::GlyphId(glyph), &mut outline, 0);
        let padding = padding.max(0);
        bitmap.coverage = signed_distance_field(
            &flatten(&outline.segments),
            bitmap.left - padding,
            bitmap.top + padding,
            bitmap.width + 2 * padding,
            bitmap.height + 2 * padding,
            (padding + 1) as f32,
        );
        Some(bitmap)
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

    /// Slants the outline (synthesized italic) about the baseline.
    fn shear(&mut self, shear: f32) {
        if shear != 0.0 {
            self.transform(|(x, y)| (x + y * shear, y));
        }
    }

    fn transform(&mut self, f: impl Fn((f32, f32)) -> (f32, f32)) {
        for segment in &mut self.segments {
            *segment = match *segment {
                Segment::Line(a, b) => Segment::Line(f(a), f(b)),
                Segment::Quad(a, b, c) => Segment::Quad(f(a), f(b), f(c)),
                Segment::Cubic(a, b, c, d) => Segment::Cubic(f(a), f(b), f(c), f(d)),
            };
        }
    }
}

/// A glyph's outline in pixels as FreeType scales it: a simple glyph's points each scaled and
/// rounded to 1/64 pixel; a composite glyph's components scaled on their own, transformed by
/// their matrix (each product rounded, as `FT_Vector_Transform`), then moved by their offset
/// scaled and rounded separately, so an accented letter's parts land where FreeType puts them
/// (scaling the composed outline in font units can be 1/64 pixel off).
fn outline_scaled(face: &Face, glyph: ttf_parser::GlyphId, out: &mut Outline, depth: u8) {
    let components = if depth < 8 { composite_components(face, glyph) } else { None };
    let Some(components) = components else {
        face.outline_glyph(glyph, out);
        return;
    };
    for component in components {
        let mut part = Outline {
            scale: out.scale,
            shear: 0.0,
            segments: Vec::new(),
            start: (0.0, 0.0),
            last: (0.0, 0.0),
        };
        outline_scaled(face, component.glyph, &mut part, depth + 1);
        let q = |v: f32| (v * 64.0).round() / 64.0;
        if let Some([xx, yx, xy, yy]) = component.matrix {
            part.transform(|(x, y)| (q(x * xx) + q(y * xy), q(x * yx) + q(y * yy)));
        }
        let (dx, dy) = (q(component.dx as f32 * out.scale), q(component.dy as f32 * out.scale));
        part.transform(|(x, y)| (x + dx, y + dy));
        out.segments.append(&mut part.segments);
    }
}

/// A component of a composite TrueType glyph.
struct Component {
    glyph: ttf_parser::GlyphId,
    /// Offset in font units.
    dx: i32,
    dy: i32,
    /// `[xx, yx, xy, yy]` when the component is scaled or transformed.
    matrix: Option<[f32; 4]>,
}

/// The components of a composite glyph placed by offsets; `None` for a simple glyph, a font
/// without a `glyf` table, or components placed by matching points (left to ttf-parser).
fn composite_components(face: &Face, glyph: ttf_parser::GlyphId) -> Option<Vec<Component>> {
    let raw = face.raw_face();
    let glyf = raw.table(ttf_parser::Tag::from_bytes(b"glyf"))?;
    let loca = raw.table(ttf_parser::Tag::from_bytes(b"loca"))?;
    let index = glyph.0 as usize;
    let u16_at = |data: &[u8], at: usize| data.get(at..at + 2).map(|b| u16::from_be_bytes([b[0], b[1]]));
    let (start, end) = match face.tables().head.index_to_location_format {
        ttf_parser::head::IndexToLocationFormat::Short => (u16_at(loca, index * 2)? as usize * 2, u16_at(loca, index * 2 + 2)? as usize * 2),
        ttf_parser::head::IndexToLocationFormat::Long => {
            let u32_at = |at: usize| loca.get(at..at + 4).map(|b| u32::from_be_bytes([b[0], b[1], b[2], b[3]]) as usize);
            (u32_at(index * 4)?, u32_at(index * 4 + 4)?)
        }
    };
    let data = glyf.get(start..end)?;
    if data.len() < 10 || (u16_at(data, 0)? as i16) >= 0 {
        return None;
    }
    let f2dot14 = |at: usize| u16_at(data, at).map(|v| v as i16 as f32 / 16384.0);
    let mut components = Vec::new();
    let mut at = 10;
    loop {
        let flags = u16_at(data, at)?;
        let glyph = ttf_parser::GlyphId(u16_at(data, at + 2)?);
        at += 4;
        let (dx, dy) = if flags & 0x0001 != 0 {
            let (a, b) = (u16_at(data, at)?, u16_at(data, at + 2)?);
            at += 4;
            (a as i16 as i32, b as i16 as i32)
        } else {
            let (a, b) = (*data.get(at)?, *data.get(at + 1)?);
            at += 2;
            (a as i8 as i32, b as i8 as i32)
        };
        if flags & 0x0002 == 0 {
            // ARGS_ARE_XY_VALUES unset: the arguments are points to match.
            return None;
        }
        let matrix = if flags & 0x0008 != 0 {
            let s = f2dot14(at)?;
            at += 2;
            Some([s, 0.0, 0.0, s])
        } else if flags & 0x0040 != 0 {
            let (sx, sy) = (f2dot14(at)?, f2dot14(at + 2)?);
            at += 4;
            Some([sx, 0.0, 0.0, sy])
        } else if flags & 0x0080 != 0 {
            let m = [f2dot14(at)?, f2dot14(at + 2)?, f2dot14(at + 4)?, f2dot14(at + 6)?];
            at += 8;
            Some(m)
        } else {
            None
        };
        components.push(Component { glyph, dx, dy, matrix });
        if flags & 0x0020 == 0 {
            break;
        }
    }
    Some(components)
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

/// The outline as line segments, curves split finely enough (0.02 px) for distances.
fn flatten(segments: &[Segment]) -> Vec<((f32, f32), (f32, f32))> {
    const TOLERANCE: f32 = 0.02;
    let steps = |deviation: f32| ((deviation / (8.0 * TOLERANCE)).sqrt().ceil() as usize).clamp(1, 64);
    let length = |(x, y): (f32, f32)| (x * x + y * y).sqrt();
    let mut lines = Vec::with_capacity(segments.len() * 4);
    let mut push_curve = |n: usize, at: &dyn Fn(f32) -> (f32, f32)| {
        let mut previous = at(0.0);
        for i in 1..=n {
            let point = at(i as f32 / n as f32);
            lines.push((previous, point));
            previous = point;
        }
    };
    for segment in segments {
        match *segment {
            Segment::Line(a, b) => push_curve(1, &|t| (a.0 + (b.0 - a.0) * t, a.1 + (b.1 - a.1) * t)),
            Segment::Quad(a, b, c) => {
                let n = steps(length((a.0 - 2.0 * b.0 + c.0, a.1 - 2.0 * b.1 + c.1)));
                push_curve(n, &|t| {
                    let u = 1.0 - t;
                    (u * u * a.0 + 2.0 * u * t * b.0 + t * t * c.0, u * u * a.1 + 2.0 * u * t * b.1 + t * t * c.1)
                });
            }
            Segment::Cubic(a, b, c, d) => {
                let first = length((a.0 - 2.0 * b.0 + c.0, a.1 - 2.0 * b.1 + c.1));
                let second = length((b.0 - 2.0 * c.0 + d.0, b.1 - 2.0 * c.1 + d.1));
                let n = steps(1.5 * first.max(second));
                push_curve(n, &|t| {
                    let u = 1.0 - t;
                    let (w0, w1, w2, w3) = (u * u * u, 3.0 * u * u * t, 3.0 * u * t * t, t * t * t);
                    (
                        w0 * a.0 + w1 * b.0 + w2 * c.0 + w3 * d.0,
                        w0 * a.1 + w1 * b.1 + w2 * c.1 + w3 * d.1,
                    )
                });
            }
        }
    }
    lines
}

/// Distances from the pixel centers of a `width` x `height` region whose top-left corner is at
/// (`left`, `top`) (pixels, y up) to the outline `lines`, encoded `0.5 + d / (2 spread)` as bytes,
/// top row first; `d` is positive inside (non-zero winding).
fn signed_distance_field(
    lines: &[((f32, f32), (f32, f32))],
    left: i32,
    top: i32,
    width: i32,
    height: i32,
    spread: f32,
) -> Vec<u8> {
    let (w, h) = (width.max(0) as usize, height.max(0) as usize);
    let center = |i: usize, j: usize| (left as f32 + i as f32 + 0.5, top as f32 - j as f32 - 0.5);
    // Squared distances, from each line to the pixels within `spread` of it.
    let mut nearest = vec![spread * spread; w * h];
    for &(a, b) in lines {
        let (x_min, x_max) = (a.0.min(b.0) - spread, a.0.max(b.0) + spread);
        let (y_min, y_max) = (a.1.min(b.1) - spread, a.1.max(b.1) + spread);
        let i0 = ((x_min - left as f32 - 0.5).floor().max(0.0)) as usize;
        let i1 = ((x_max - left as f32 - 0.5).ceil().max(-1.0) + 1.0).min(w as f32) as usize;
        let j0 = ((top as f32 - y_max - 0.5).floor().max(0.0)) as usize;
        let j1 = ((top as f32 - y_min - 0.5).ceil().max(-1.0) + 1.0).min(h as f32) as usize;
        let (dx, dy) = (b.0 - a.0, b.1 - a.1);
        let length2 = dx * dx + dy * dy;
        for j in j0..j1 {
            for i in i0..i1 {
                let (px, py) = center(i, j);
                let t = if length2 > 0.0 {
                    (((px - a.0) * dx + (py - a.1) * dy) / length2).clamp(0.0, 1.0)
                } else {
                    0.0
                };
                let (ex, ey) = (a.0 + dx * t - px, a.1 + dy * t - py);
                let d2 = ex * ex + ey * ey;
                let slot = &mut nearest[j * w + i];
                if d2 < *slot {
                    *slot = d2;
                }
            }
        }
    }
    let mut field = vec![0u8; w * h];
    let mut crossings: Vec<(f32, i32)> = Vec::new();
    for j in 0..h {
        // The row's winding numbers: where the outline crosses the pixel centers' line.
        let y = top as f32 - j as f32 - 0.5;
        crossings.clear();
        for &(a, b) in lines {
            if (a.1 <= y && b.1 > y) || (b.1 <= y && a.1 > y) {
                let x = a.0 + (y - a.1) / (b.1 - a.1) * (b.0 - a.0);
                crossings.push((x, if b.1 > a.1 { 1 } else { -1 }));
            }
        }
        crossings.sort_by(|p, q| p.0.total_cmp(&q.0));
        let mut next = 0;
        let mut winding = 0;
        for i in 0..w {
            let (x, _) = center(i, j);
            while next < crossings.len() && crossings[next].0 <= x {
                winding += crossings[next].1;
                next += 1;
            }
            let d = nearest[j * w + i].sqrt();
            let d = if winding != 0 { d } else { -d };
            field[j * w + i] = ((0.5 + d / (2.0 * spread)).clamp(0.0, 1.0) * 255.0).round() as u8;
        }
    }
    field
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
        let Some(face) = std::fs::read("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf")
            .ok()
            .and_then(|data| FontFace::from_bytes(data, 0).ok())
        else {
            return;
        };
        let glyph = face.render('ñ', 90.0, false, false).unwrap();
        let (x_min, y_min, x_max, y_max) = glyph.bounds;
        assert_eq!((x_max - x_min, y_max - y_min, x_min, y_max), (38.21875, 64.5625, 6.15625, 64.5625));
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

    #[test]
    fn sdf_encodes_distance_to_the_outline() {
        let Some(face) = system_face() else { return };
        let index = face.glyph_index('l');
        let raster = face.render_glyph(index, 64.0, false, false).unwrap();
        let padding = 5;
        let sdf = face.render_glyph_sdf(index, 64.0, padding).unwrap();
        let (w, h) = (raster.width + 2 * padding, raster.height + 2 * padding);
        assert_eq!((sdf.width, sdf.height), (raster.width, raster.height));
        assert_eq!(sdf.coverage.len(), (w * h) as usize);
        let at = |x: i32, y: i32| sdf.coverage[(y * w + x) as usize];
        // The padding's corner is far outside, the stem's middle inside.
        assert_eq!(at(0, 0), 0);
        let middle = at(w / 2, h / 2);
        assert!(middle > 128, "{middle}");
        // Across the stem's left edge the value climbs 255 / (2 (padding + 1)) a pixel.
        let row: Vec<u8> = (0..w).map(|x| at(x, h / 2)).collect();
        let edge = row.iter().position(|&v| v >= 128).unwrap();
        let step = row[edge] as f32 - row[edge - 1] as f32;
        assert!((step - 255.0 / 12.0).abs() < 1.5, "{step} in {row:?}");
        // The bounds are the outline's: the bitmap covers them.
        let (x_min, y_min, x_max, y_max) = sdf.bounds;
        assert!(x_min >= sdf.left as f32 && x_max <= (sdf.left + sdf.width) as f32);
        assert!(y_max <= sdf.top as f32 && y_min >= (sdf.top - sdf.height) as f32);
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
