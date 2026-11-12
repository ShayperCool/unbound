//! TextCore's font engine glyphs (TextMeshPro and UI Toolkit font assets) through the bundled
//! FreeType library, as the Unity 6000.0.71f1 Editor loads and rasterizes them
//! (tests/projects/textcore): faces sized in whole pixels, glyph metrics read from FreeType with
//! the caller's load flags, and atlas tiles per `GlyphRenderMode`, with its distance fields.
use crate::font_freetype::{FreeTypeFace, color_rgba, coverage};
use freetype_sys as ft;

/// `GlyphRenderMode` values of the TextCore API.
pub mod render_mode {
    pub const SMOOTH_HINTED: i32 = 4121;
    pub const SMOOTH: i32 = 4117;
    pub const COLOR_HINTED: i32 = 69656;
    pub const COLOR: i32 = 69652;
    pub const RASTER_HINTED: i32 = 4122;
    pub const RASTER: i32 = 4118;
    pub const SDF: i32 = 4134;
    pub const SDF8: i32 = 8230;
    pub const SDF16: i32 = 16422;
    pub const SDF32: i32 = 32806;
    pub const SDFAA_HINTED: i32 = 4169;
    pub const SDFAA: i32 = 4165;
}

/// A glyph's metrics in pixels and the size of its bitmap (its atlas rect).
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct TextCoreGlyph {
    pub width: f32,
    pub height: f32,
    pub bearing_x: f32,
    pub bearing_y: f32,
    pub advance: f32,
    pub rect_width: i32,
    pub rect_height: i32,
}

/// A glyph rendered for an atlas: its metrics and rect as the render mode loads it, and
/// `(rect_width + 2 padding) x (rect_height + 2 padding)` pixels, top row first: one coverage
/// byte each, or for a colour bitmap in a colour mode four (RGBA, premultiplied by alpha, as the
/// Unity 6000.0.71f1 Editor fills a colour atlas, tests/projects/textcolor). Only the distance
/// field modes fill a padding; the others have none.
#[derive(Debug, Clone, PartialEq)]
pub struct TextCoreTile {
    pub glyph: TextCoreGlyph,
    pub padding: i32,
    pub pixels: Vec<u8>,
}

/// A bitmap-only face's strike, as a request for a size selects it: its pixels per em and its
/// lines in pixels.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct TextCoreStrike {
    pub ppem: i32,
    pub ascender: f32,
    pub descender: f32,
    pub height: f32,
}

/// How a render mode makes its tile.
#[derive(Debug, Clone, Copy, PartialEq)]
enum Raster {
    /// The bitmap's coverage as it is.
    Coverage,
    /// The anti-aliased distance transform of the 8-bit coverage.
    AntiAliasedField,
    /// The distance field sampled from a monochrome bitmap rendered this many times larger.
    SampledField(i32),
}

/// FreeType load flags and rasterization of each render mode.
fn mode(render_mode: i32) -> Option<(ft::FT_Int32, Raster)> {
    use render_mode::*;
    const RENDER: ft::FT_Int32 = ft::FT_LOAD_RENDER;
    const SMOOTH_FLAGS: ft::FT_Int32 = ft::FT_LOAD_NO_HINTING | RENDER;
    const MONO_FLAGS: ft::FT_Int32 = ft::FT_LOAD_MONOCHROME | ft::FT_LOAD_NO_HINTING | RENDER;
    Some(match render_mode {
        SMOOTH => (SMOOTH_FLAGS, Raster::Coverage),
        SMOOTH_HINTED => (RENDER, Raster::Coverage),
        RASTER => (MONO_FLAGS, Raster::Coverage),
        RASTER_HINTED => (ft::FT_LOAD_MONOCHROME | RENDER, Raster::Coverage),
        COLOR => (ft::FT_LOAD_COLOR | SMOOTH_FLAGS, Raster::Coverage),
        COLOR_HINTED => (ft::FT_LOAD_COLOR | RENDER, Raster::Coverage),
        SDFAA => (SMOOTH_FLAGS, Raster::AntiAliasedField),
        SDFAA_HINTED => (RENDER, Raster::AntiAliasedField),
        SDF => (MONO_FLAGS, Raster::SampledField(1)),
        SDF8 => (MONO_FLAGS, Raster::SampledField(8)),
        SDF16 => (MONO_FLAGS, Raster::SampledField(16)),
        SDF32 => (MONO_FLAGS, Raster::SampledField(32)),
        _ => return None,
    })
}

impl FreeTypeFace {
    /// Sizes the face to `pixels` per em: a scalable face directly, a bitmap-only face to the
    /// strike nearest that size (scanning the strikes in order while they get closer). Unmeasured
    /// for bitmap-only faces: no probe font has only fixed sizes.
    fn set_pixel_size(&mut self, pixels: i32) -> bool {
        if pixels <= 0 {
            return false;
        }
        unsafe {
            ft::FT_Set_Transform(self.face, std::ptr::null_mut(), std::ptr::null_mut());
            let face = &*self.face;
            if face.face_flags & ft::FT_FACE_FLAG_SCALABLE as ft::FT_Long != 0 {
                return ft::FT_Set_Pixel_Sizes(self.face, 0, pixels as ft::FT_UInt) == 0;
            }
            if face.num_fixed_sizes <= 0 || face.available_sizes.is_null() {
                return false;
            }
            let strikes =
                std::slice::from_raw_parts(face.available_sizes, face.num_fixed_sizes as usize);
            let mut best = 0;
            let mut nearest = i64::MAX;
            for (i, strike) in strikes.iter().enumerate() {
                let distance = ((strike.size >> 6) as i64 - pixels as i64).abs();
                if distance > nearest {
                    break;
                }
                nearest = distance;
                best = i;
            }
            ft::FT_Select_Size(self.face, best as ft::FT_Int) == 0
        }
    }

    /// Loads glyph `glyph` at `pixels` per em with FreeType `flags`; the metrics are divided by
    /// `upsampling`, the bitmap's size is the rect's.
    fn load_textcore(
        &mut self,
        glyph: u32,
        pixels: i32,
        flags: ft::FT_Int32,
        upsampling: i32,
    ) -> Option<TextCoreGlyph> {
        if !self.set_pixel_size(pixels.checked_mul(upsampling)?) {
            return None;
        }
        unsafe {
            if ft::FT_Load_Glyph(self.face, glyph, flags) != 0 {
                return None;
            }
            let slot = &*(*self.face).glyph;
            let unit = (64 * upsampling) as f32;
            let m = &slot.metrics;
            Some(TextCoreGlyph {
                width: m.width as f32 / unit,
                height: m.height as f32 / unit,
                bearing_x: m.horiBearingX as f32 / unit,
                bearing_y: m.horiBearingY as f32 / unit,
                advance: m.horiAdvance as f32 / unit,
                rect_width: slot.bitmap.width,
                rect_height: slot.bitmap.rows,
            })
        }
    }

    /// The strike a bitmap-only face uses for `pixels` per em (`None` for a scalable face, or one
    /// without strikes): the face's size metrics there.
    pub(crate) fn textcore_strike(&mut self, pixels: i32) -> Option<TextCoreStrike> {
        unsafe {
            if (*self.face).face_flags & ft::FT_FACE_FLAG_SCALABLE as ft::FT_Long != 0 {
                return None;
            }
        }
        if !self.set_pixel_size(pixels) {
            return None;
        }
        unsafe {
            let size = (*self.face).size;
            if size.is_null() {
                return None;
            }
            let m = &(*size).metrics;
            Some(TextCoreStrike {
                ppem: i32::from(m.y_ppem),
                ascender: m.ascender as f32 / 64.0,
                descender: m.descender as f32 / 64.0,
                height: m.height as f32 / 64.0,
            })
        }
    }

    /// The metrics of glyph `glyph` at `pixels` per em loaded with TextCore's `GlyphLoadFlags`
    /// (FreeType's load flags). With `LOAD_NO_SCALE` FreeType reports font units, which are
    /// divided by 64 like pixels.
    pub(crate) fn textcore_glyph(
        &mut self,
        glyph: u32,
        pixels: i32,
        flags: i32,
    ) -> Option<TextCoreGlyph> {
        self.load_textcore(glyph, pixels, flags, 1)
    }

    /// Glyph `glyph` rendered at `pixels` per em in `render_mode`, with `padding` around the
    /// distance fields. `None` for an unknown mode or a glyph the face cannot load.
    pub(crate) fn textcore_render(
        &mut self,
        glyph: u32,
        pixels: i32,
        render_mode: i32,
        padding: i32,
    ) -> Option<TextCoreTile> {
        let (flags, raster) = mode(render_mode)?;
        let upsampling = match raster {
            Raster::SampledField(up) => up,
            _ => 1,
        };
        let mut loaded = self.load_textcore(glyph, pixels, flags, upsampling)?;
        let slot_bitmap = unsafe { &(*(*self.face).glyph).bitmap };
        if flags & ft::FT_LOAD_COLOR != 0
            && let Some(rgba) = unsafe { color_rgba(slot_bitmap) }
        {
            return Some(TextCoreTile {
                glyph: loaded,
                padding: 0,
                pixels: rgba,
            });
        }
        let bitmap = unsafe { coverage(self.library, slot_bitmap)? };
        let (width, height) = (loaded.rect_width, loaded.rect_height);
        let padding = padding.max(0);
        let (padding, pixels) = match raster {
            Raster::Coverage => (0, bitmap),
            Raster::AntiAliasedField => (
                padding,
                anti_aliased_distance_field(&bitmap, width, height, padding),
            ),
            Raster::SampledField(up) => {
                let (out_width, out_height) = ((width + up - 1) / up, (height + up - 1) / up);
                loaded.rect_width = out_width;
                loaded.rect_height = out_height;
                (
                    padding,
                    sampled_distance_field(
                        &bitmap, width, height, out_width, out_height, up, padding,
                    ),
                )
            }
        };
        Some(TextCoreTile {
            glyph: loaded,
            padding,
            pixels,
        })
    }
}

/// Distance field bytes: 127.5 at the outline, `255 / (2 (padding + 1))` per pixel of distance
/// (TextCore's gradient scale is `padding + 1`), clamped to the byte range and rounded.
fn encode_distance(distance: f32, inside: bool, per_pixel: f32) -> u8 {
    const MIDDLE: f32 = 127.5;
    let d = (distance * per_pixel).min(MIDDLE).max(0.0);
    let value = if inside { MIDDLE + d } else { MIDDLE - d };
    (value + 0.5) as i32 as u8
}

/// A pixel of the anti-aliased distance transform: its coverage (0..1), the vector to the edge
/// pixel nearest it, that vector's squared length, the distance from the edge pixel's center to
/// the edge inside it, and the sum of the two that neighbours compare.
#[derive(Clone, Copy, Default)]
struct EdtPixel {
    alpha: f32,
    metric: f32,
    length2: f32,
    edge: f32,
    gradient: (f32, f32),
    offset: (i32, i32),
}

/// The distance from a pixel's center to the edge crossing it, for an edge of direction
/// `(gx, gy)` and the pixel's coverage `alpha` (the edge's area model): linear when the
/// direction is axis-aligned, else from the triangle or band the covered area makes.
fn edge_distance(gx: f32, gy: f32, alpha: f32) -> f32 {
    if gx == 0.0 || gy == 0.0 {
        return 0.5 - alpha;
    }
    let length = (gy * gy + gx * gx).sqrt();
    let (x, y) = ((gx / length).abs(), (gy / length).abs());
    let (small, big) = (x.min(y), x.max(y));
    let corner = (small * 0.5) / big;
    let area = (big + big) * small;
    if alpha < corner {
        (big + small) * 0.5 - (alpha * area).sqrt()
    } else if 1.0 - corner <= alpha {
        ((1.0 - alpha) * area).sqrt() - (big + small) * 0.5
    } else {
        (0.5 - alpha) * big
    }
}

/// The normalized coverage gradient at flat index `k` of a `width`-wide coverage bitmap of `n`
/// bytes (a Sobel kernel with diagonal weights 1 and axis weights sqrt 2; bytes outside the
/// bitmap count as 0, and rows are not separated: the index steps across a row's end).
fn coverage_gradient(coverage: &[u8], k: i32, width: i32) -> (f32, f32) {
    let n = coverage.len() as i32;
    let at = |i: i32| {
        if (0..n).contains(&i) {
            coverage[i as usize] as f32
        } else {
            0.0
        }
    };
    let s = std::f32::consts::SQRT_2;
    let (below_left, below, below_right) =
        (at(k + width - 1), at(k + width) * s, at(k + width + 1));
    let (left, right) = (at(k - 1) * s, at(k + 1) * s);
    let (above_left, above, above_right) =
        (at(k - width - 1), at(k - width) * s, at(k - width + 1));
    let gx = (((((0.0 - below_left) + below_right) - left) + right) - above_left) + above_right;
    let gy = ((((below_left + below) + below_right) - above_left) - above) - above_right;
    let length = (gy * gy + gx * gx).sqrt();
    if length > 0.0 {
        (gx / length, gy / length)
    } else {
        (0.0, 0.0)
    }
}

/// One direction of the anti-aliased distance transform over the bitmap grown by `padding`:
/// distances from each pixel outside the shape (coverage 0) to the shape, or with `invert` from
/// each pixel inside it to the outside. Edge pixels start from their own edge distance; two
/// raster passes then carry the nearest edge pixel's offset over the 3 x 3 neighbourhood, each
/// candidate scored by its squared offset plus the edge distance at the pixel it points to.
fn distance_transform(
    coverage: &[u8],
    width: i32,
    height: i32,
    padding: i32,
    invert: bool,
) -> Vec<EdtPixel> {
    let (w, h) = (width + 2 * padding, height + 2 * padding);
    let far = (h * h + w * w + 1) as f32;
    let mut pixels = vec![EdtPixel::default(); (w * h) as usize];
    let candidate = |pixels: &[EdtPixel], from: usize, step: (i32, i32), here: i32| {
        let neighbour = pixels[from];
        if neighbour.metric == far {
            return None;
        }
        let (dx, dy) = (neighbour.offset.0 + step.0, neighbour.offset.1 + step.1);
        let target = pixels[(here + w * dy + dx) as usize].alpha;
        let edge = edge_distance(dx as f32, dy as f32, target);
        let length2 = (dy * dy + dx * dx) as f32;
        Some(EdtPixel {
            metric: edge + length2,
            length2,
            edge,
            offset: (dx, dy),
            ..neighbour
        })
    };
    let better = |best: &mut EdtPixel, found: Option<EdtPixel>, or_equal: bool| {
        if let Some(found) = found
            && (found.metric < best.metric || (or_equal && found.metric == best.metric))
        {
            best.metric = found.metric;
            best.length2 = found.length2;
            best.edge = found.edge;
            best.offset = found.offset;
        }
    };
    for row in 0..h {
        for column in 0..w {
            let here = row * w + column;
            let inner =
                (padding..h - padding).contains(&row) && (padding..w - padding).contains(&column);
            let source = (row - padding) * width + (column - padding);
            let mut alpha = if inner {
                coverage[source as usize] as f32 / 255.0
            } else {
                0.0
            };
            if invert {
                alpha = 1.0 - alpha;
            }
            let mut pixel = EdtPixel {
                alpha,
                ..EdtPixel::default()
            };
            if alpha >= 1.0 {
                // Inside: no distance.
            } else if alpha != 0.0 {
                pixel.gradient = coverage_gradient(coverage, source, width);
                pixel.edge = edge_distance(pixel.gradient.0, pixel.gradient.1, alpha);
                pixel.metric = pixel.edge;
            } else {
                let mut best = EdtPixel {
                    metric: far,
                    length2: far,
                    ..pixel
                };
                let p = here as usize;
                let wu = w as usize;
                if row > 0 {
                    better(&mut best, candidate(&pixels, p - wu, (0, -1), here), false);
                }
                if column > 0 {
                    better(&mut best, candidate(&pixels, p - 1, (-1, 0), here), false);
                    if row > 0 {
                        better(
                            &mut best,
                            candidate(&pixels, p - wu - 1, (-1, -1), here),
                            false,
                        );
                    }
                }
                if row > 0 && column < w - 1 {
                    better(
                        &mut best,
                        candidate(&pixels, p - wu + 1, (1, -1), here),
                        false,
                    );
                }
                pixel = best;
            }
            pixels[here as usize] = pixel;
        }
    }
    for row in (0..h).rev() {
        for column in (0..w).rev() {
            let here = row * w + column;
            let p = here as usize;
            let wu = w as usize;
            let mut best = pixels[p];
            let edge_pixel = best.alpha > 0.0 && best.alpha < 1.0;
            if edge_pixel || best.metric == 0.0 {
                continue;
            }
            if row + 1 < h {
                better(&mut best, candidate(&pixels, p + wu, (0, 1), here), true);
            }
            if column < w - 1 {
                better(&mut best, candidate(&pixels, p + 1, (1, 0), here), false);
                if row + 1 < h {
                    better(
                        &mut best,
                        candidate(&pixels, p + wu + 1, (1, 1), here),
                        false,
                    );
                }
            }
            if column > 0 && row + 1 < h {
                better(
                    &mut best,
                    candidate(&pixels, p + wu - 1, (-1, 1), here),
                    false,
                );
            }
            pixels[p] = best;
        }
    }
    pixels
}

/// SDFAA: the anti-aliased distance transform of 8-bit coverage (`width` x `height`, top row
/// first), outside and inside, as distance field bytes over the bitmap grown by `padding`.
fn anti_aliased_distance_field(coverage: &[u8], width: i32, height: i32, padding: i32) -> Vec<u8> {
    if width <= 0 || height <= 0 || coverage.len() < (width * height) as usize {
        return Vec::new();
    }
    let outside = distance_transform(coverage, width, height, padding, false);
    let inside = distance_transform(coverage, width, height, padding, true);
    let per_pixel = 255.0 / (padding * 2 + 2) as f32;
    outside
        .iter()
        .zip(&inside)
        .map(|(out, inn)| {
            // The direction with the smaller score holds the pixel: an inside pixel's outward
            // distance is 0, an outside pixel's inward one.
            let is_inside = out.metric <= inn.metric;
            let nearest = if is_inside { inn } else { out };
            encode_distance(nearest.length2.sqrt() + nearest.edge, is_inside, per_pixel)
        })
        .collect()
}

/// SDF, SDF8..SDF32: each pixel of the `out_width` x `out_height` glyph grown by `padding` takes
/// the distance from its center to the nearest sample center of the other kind (set or clear)
/// in the monochrome bitmap rendered `up` times larger, searched in the square of samples within
/// `padding + 1` pixels; samples outside the bitmap are clear, and a pixel is inside when its
/// nearest sample is set.
///
/// The square search is split by axis: per sample column and output row the squared row distance
/// to the nearest set and clear sample of the window (`nearest_rows`), then per pixel the minimum
/// over the window's columns of the squared column distance plus that. The squares are exact in
/// `f32` and rounding a sum is monotonic, so the minimum is the one of the full search.
fn sampled_distance_field(
    mono: &[u8],
    width: i32,
    height: i32,
    out_width: i32,
    out_height: i32,
    up: i32,
    padding: i32,
) -> Vec<u8> {
    /// No sample of the kind within reach.
    const FAR: f32 = 1e22;
    let per_pixel = 255.0 / ((padding * 2 + 2) * up) as f32;
    let (upf, reach) = (up as f32, padding + 1);
    let (columns, rows) = (out_width + 2 * padding, out_height + 2 * padding);
    let first_sample = (-padding - reach) * up;
    let samples = ((columns + 2 * reach) * up - up + 1) as usize;
    let (set_rows, clear_rows) = nearest_rows(
        mono,
        width,
        height,
        first_sample,
        samples,
        rows,
        up,
        padding,
    );
    let mut field = Vec::with_capacity((columns * rows) as usize);
    for row in 0..rows as usize {
        for x in -padding..out_width + padding {
            let center_x = (x as f32 + 0.5) * upf;
            let (mut nearest_set, mut nearest_clear) = (FAR, FAR);
            for sx in (x - reach) * up..=(x + reach) * up {
                let dx = center_x - (sx as f32 + 0.5);
                let at = row * samples + (sx - first_sample) as usize;
                let dx2 = dx * dx;
                nearest_set = nearest_set.min(dx2 + set_rows[at]);
                nearest_clear = nearest_clear.min(dx2 + clear_rows[at]);
            }
            let inside = nearest_set <= nearest_clear;
            let distance = if inside { nearest_clear } else { nearest_set }.sqrt();
            field.push(encode_distance(distance, inside, per_pixel));
        }
    }
    field
}

/// For [`sampled_distance_field`]: per output row (rows first) and sample column from
/// `first_sample`, the squared distance from the row's center to the nearest set and the nearest
/// clear sample row of the column within the row's window (infinite without one). Rows and
/// columns outside the bitmap are clear.
#[allow(clippy::too_many_arguments)]
fn nearest_rows(
    mono: &[u8],
    width: i32,
    height: i32,
    first_sample: i32,
    samples: usize,
    rows: i32,
    up: i32,
    padding: i32,
) -> (Vec<f32>, Vec<f32>) {
    let reach = padding + 1;
    let mut set_rows = vec![f32::INFINITY; rows as usize * samples];
    let mut clear_rows = vec![f32::INFINITY; rows as usize * samples];
    // Per column of the bitmap: the last set/clear row at or before each row and the first at or
    // after it (-1 / height when there is none).
    let h = height.max(0) as usize;
    let (mut set_before, mut set_after) = (vec![-1i32; h], vec![height; h]);
    let (mut clear_before, mut clear_after) = (vec![-1i32; h], vec![height; h]);
    for column in 0..samples {
        let sx = first_sample + column as i32;
        let inside = (0..width).contains(&sx);
        if inside {
            let is_set = |r: usize| mono[r * width as usize + sx as usize] != 0;
            let (mut last_set, mut last_clear) = (-1, -1);
            for r in 0..h {
                if is_set(r) {
                    last_set = r as i32
                } else {
                    last_clear = r as i32
                }
                set_before[r] = last_set;
                clear_before[r] = last_clear;
            }
            let (mut next_set, mut next_clear) = (height, height);
            for r in (0..h).rev() {
                if is_set(r) {
                    next_set = r as i32
                } else {
                    next_clear = r as i32
                }
                set_after[r] = next_set;
                clear_after[r] = next_clear;
            }
        }
        for row in 0..rows {
            let y = row - padding;
            let (low, high) = ((y - reach) * up, (y + reach) * up);
            let center = (y as f32 + 0.5) * up as f32;
            // The sample rows on either side of the center.
            let below = y * up + (up - 1) / 2;
            let above = below + 1;
            let squared = |sample: i32| {
                if sample < low || sample > high {
                    return f32::INFINITY;
                }
                let dy = center - (sample as f32 + 0.5);
                dy * dy
            };
            let pick = |before: &[i32], after: &[i32], outside_counts: bool| {
                // The nearest row of the kind at or before `below` and at or after `above`.
                let earlier = if below < 0 {
                    if outside_counts { below } else { -1 }
                } else if below >= height {
                    if outside_counts { below } else { before[h - 1] }
                } else {
                    let r = before[below as usize];
                    if r < 0 && outside_counts { -1 } else { r }
                };
                let later = if above >= height {
                    if outside_counts { above } else { height }
                } else if above < 0 {
                    if outside_counts { above } else { after[0] }
                } else {
                    after[above as usize]
                };
                let earlier = if earlier < 0 && !outside_counts {
                    f32::INFINITY
                } else {
                    squared(earlier)
                };
                let later = if later >= height && !outside_counts {
                    f32::INFINITY
                } else {
                    squared(later)
                };
                earlier.min(later)
            };
            let at = row as usize * samples + column;
            if inside && h > 0 {
                set_rows[at] = pick(&set_before, &set_after, false);
                clear_rows[at] = pick(&clear_before, &clear_after, true);
            } else {
                clear_rows[at] = squared(below).min(squared(above));
            }
        }
    }
    (set_rows, clear_rows)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::font::FontFace;

    const SANS: &[u8] =
        include_bytes!("../../../tests/projects/glyphraster/Assets/Resources/Synthetic.ttf");
    /// Three colour emoji with one 109 ppem strike (tests/projects/textcolor).
    const EMOJI: &[u8] =
        include_bytes!("../../../tests/projects/textcolor/Assets/Resources/Fonts/EmojiProbe.ttf");
    /// FreeType's `FT_LOAD_NO_HINTING`.
    const NO_HINTING: i32 = 2;

    // Editor: textcolor face/* (the strike at 12 to 150) and asset/* (COLOR atlases hold the
    // premultiplied colours; SMOOTH the coverage).
    #[test]
    fn bitmap_only_faces_take_their_strike_and_colour_modes_keep_colour() {
        let emoji = FontFace::from_bytes(EMOJI.to_vec(), 0).unwrap();
        assert!(emoji.has_color());
        for size in [12, 24, 150] {
            let strike = emoji.textcore_strike(size).unwrap();
            assert_eq!(strike.ppem, 109);
            assert_eq!(
                (strike.ascender, strike.descender, strike.height),
                (101.0, -27.0, 128.0)
            );
        }
        let sans = FontFace::from_bytes(SANS.to_vec(), 0).unwrap();
        assert!(sans.textcore_strike(24).is_none());
        assert!(!sans.has_color());

        let smile = u32::from(emoji.glyph_index('\u{1F600}'));
        let color = emoji
            .textcore_render(smile, 24, render_mode::COLOR, 4)
            .unwrap();
        let (w, h) = (
            color.glyph.rect_width as usize,
            color.glyph.rect_height as usize,
        );
        assert_eq!((w, h), (136, 128));
        assert_eq!(color.pixels.len(), w * h * 4);
        // Premultiplied: no channel above its alpha, and some partly covered pixels.
        assert!(
            color
                .pixels
                .chunks_exact(4)
                .all(|p| p[0] <= p[3] && p[1] <= p[3] && p[2] <= p[3])
        );
        assert!(color.pixels.chunks_exact(4).any(|p| p[3] > 0 && p[3] < 255));
        let smooth = emoji
            .textcore_render(smile, 24, render_mode::SMOOTH, 4)
            .unwrap();
        assert_eq!(smooth.pixels.len(), w * h);
    }

    /// The definition of [`super::sampled_distance_field`], searching the whole square: each pixel of the `out_width` x `out_height` glyph grown by `padding` takes
    /// the distance from its center to the nearest sample center of the other kind (set or clear)
    /// in the monochrome bitmap rendered `up` times larger, searched within `padding + 1` pixels;
    /// samples outside the bitmap are clear, and a pixel is inside when its nearest sample is set.
    fn sampled_distance_field_by_definition(
        mono: &[u8],
        width: i32,
        height: i32,
        out_width: i32,
        out_height: i32,
        up: i32,
        padding: i32,
    ) -> Vec<u8> {
        const FAR: f32 = 1e22;
        let per_pixel = 255.0 / ((padding * 2 + 2) * up) as f32;
        let (upf, reach) = (up as f32, padding + 1);
        let set = |x: i32, y: i32| {
            x >= 0 && y >= 0 && x < width && y < height && mono[(y * width + x) as usize] != 0
        };
        let mut field =
            Vec::with_capacity(((out_width + 2 * padding) * (out_height + 2 * padding)) as usize);
        for y in -padding..out_height + padding {
            let center_y = (y as f32 + 0.5) * upf;
            for x in -padding..out_width + padding {
                let center_x = (x as f32 + 0.5) * upf;
                let (mut nearest_set, mut nearest_clear) = (FAR, FAR);
                for sy in (y - reach) * up..=(y + reach) * up {
                    let dy = center_y - (sy as f32 + 0.5);
                    for sx in (x - reach) * up..=(x + reach) * up {
                        let dx = center_x - (sx as f32 + 0.5);
                        let d2 = dx * dx + dy * dy;
                        let nearest = if set(sx, sy) {
                            &mut nearest_set
                        } else {
                            &mut nearest_clear
                        };
                        if d2 < *nearest {
                            *nearest = d2;
                        }
                    }
                }
                let inside = nearest_set <= nearest_clear;
                let distance = if inside { nearest_clear } else { nearest_set }.sqrt();
                field.push(encode_distance(distance, inside, per_pixel));
            }
        }
        field
    }

    /// FreeType's `FT_LOAD_MONOCHROME | FT_LOAD_NO_HINTING | FT_LOAD_RENDER`.
    const MONOCHROME_RENDER: i32 = 0x1006;
    /// FreeType's `FT_LOAD_COMPUTE_METRICS` (hinting on).
    const COMPUTE_METRICS: i32 = 0x200000;

    fn face() -> FontFace {
        FontFace::from_bytes(SANS.to_vec(), 0).unwrap()
    }

    fn index(face: &FontFace, c: char) -> u32 {
        u32::from(face.glyph_index(c))
    }

    #[test]
    fn edge_distance_follows_the_covered_area() {
        // An axis-aligned edge: linear in the coverage.
        assert_eq!(edge_distance(1.0, 0.0, 0.25), 0.25);
        assert_eq!(edge_distance(0.0, -1.0, 0.75), -0.25);
        // A diagonal edge through the center at half coverage, either side symmetric.
        assert!(edge_distance(1.0, 1.0, 0.5).abs() < 1e-6);
        let near = edge_distance(1.0, 1.0, 0.1);
        let far = edge_distance(1.0, 1.0, 0.9);
        assert!(near > 0.0 && (near + far).abs() < 1e-6);
        // Mirrored and transposed directions give the same distance.
        assert_eq!(edge_distance(2.0, 1.0, 0.3), edge_distance(-1.0, 2.0, 0.3));
    }

    #[test]
    fn anti_aliased_field_measures_both_sides_of_a_square() {
        // A 6 x 6 square, its left column half covered, in a padding of 4.
        let (w, h, padding) = (6, 6, 4);
        let mut coverage = vec![255u8; (w * h) as usize];
        for row in 0..h {
            coverage[(row * w) as usize] = 128;
        }
        let field = anti_aliased_distance_field(&coverage, w, h, padding);
        let width = w + 2 * padding;
        assert_eq!(field.len(), (width * (h + 2 * padding)) as usize);
        let row: Vec<u8> = (0..width)
            .map(|x| field[((padding + h / 2) * width + x) as usize])
            .collect();
        // The half covered column sits on the edge; values climb across it, by 255 / 10 a pixel.
        let edge = row[padding as usize];
        assert!((126..=129).contains(&edge), "{row:?}");
        assert!(
            row.windows(2)
                .take((padding + 3) as usize)
                .all(|p| p[0] <= p[1]),
            "{row:?}"
        );
        let step = row[padding as usize - 1] as f32 - row[padding as usize - 2] as f32;
        assert!((step - 25.5).abs() < 1.5, "{row:?}");
        // The padding's corner is the farthest outside: beyond the field's reach.
        assert_eq!(field[0], 0);
        // Empty or short input gives no field rather than a panic.
        assert!(anti_aliased_distance_field(&coverage, 0, h, padding).is_empty());
        assert!(anti_aliased_distance_field(&coverage[..5], w, h, padding).is_empty());
    }

    #[test]
    fn sampled_field_takes_the_nearest_sample_of_the_other_kind() {
        // A 2 x 2 output of an 8 x 8 monochrome bitmap (4 times larger), its left half set.
        let (up, padding) = (4, 1);
        let mut mono = vec![0u8; 64];
        for y in 0..8 {
            for x in 0..4 {
                mono[y * 8 + x] = 255;
            }
        }
        let field = sampled_distance_field(&mono, 8, 8, 2, 2, up, padding);
        assert_eq!(field.len(), 16);
        let per_pixel = 255.0 / ((2 * padding + 2) * up) as f32;
        // Output pixel (0, 0) is centered at (2, 2): the nearest clear sample centers are
        // (0.5, 2.5) away, above the bitmap (outside it counts as clear).
        let nearest = 6.5f32.sqrt() * per_pixel;
        assert_eq!(field[5], (127.5 + nearest + 0.5) as u8);
        // Output pixel (1, 0) is centered at (6, 2): the nearest set sample centers as far.
        assert_eq!(field[6], (127.5 - nearest + 0.5) as u8);
        // A pixel deep inside the set half is beyond the reach of every clear sample but the
        // bitmap's outside, a pixel beyond the reach of set samples is fully outside.
        let far = sampled_distance_field(&[0; 64], 8, 8, 2, 2, up, padding);
        assert!(far.iter().all(|&v| v == 0));
    }

    #[test]
    fn sampled_field_search_by_axis_is_the_square_search() {
        // Noisy, solid, empty and one-pixel bitmaps, several upsamplings and paddings, sizes that
        // do and do not divide by the upsampling.
        let mut state = 0x2545_f491u32;
        let mut noise = |n: usize, density: u32| {
            (0..n)
                .map(|_| {
                    state = state.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
                    if (state >> 24) % 100 < density {
                        255
                    } else {
                        0
                    }
                })
                .collect::<Vec<u8>>()
        };
        for (width, height, up, padding, density) in [
            (13, 9, 1, 3, 50),
            (16, 24, 8, 1, 30),
            (17, 31, 8, 2, 70),
            (40, 33, 16, 1, 50),
            (5, 7, 4, 0, 100),
            (6, 6, 2, 4, 0),
            (1, 1, 1, 2, 100),
            (70, 45, 32, 2, 50),
            (0, 0, 32, 4, 50),
            (40, 0, 8, 0, 50),
        ] {
            let mono = noise((width * height) as usize, density);
            let (out_width, out_height) = ((width + up - 1) / up, (height + up - 1) / up);
            let fast =
                sampled_distance_field(&mono, width, height, out_width, out_height, up, padding);
            let slow = sampled_distance_field_by_definition(
                &mono, width, height, out_width, out_height, up, padding,
            );
            assert_eq!(fast, slow, "{width}x{height} up {up} padding {padding}");
        }
    }

    #[test]
    fn render_modes_load_and_rasterize_as_their_flags_say() {
        let face = face();
        let a = index(&face, 'A');
        let smooth = face.textcore_render(a, 32, render_mode::SMOOTH, 5).unwrap();
        let (w, h) = (smooth.glyph.rect_width, smooth.glyph.rect_height);
        assert!(w > 10 && h > 10);
        // Coverage modes have no padding.
        assert_eq!(smooth.padding, 0);
        assert_eq!(smooth.pixels.len(), (w * h) as usize);
        assert!(smooth.pixels.iter().any(|&v| v != 0 && v != 255));
        // The metrics are those of an unhinted load.
        assert_eq!(
            Some(smooth.glyph),
            face.textcore_glyph(a, 32, NO_HINTING | 4)
        );
        let raster = face.textcore_render(a, 32, render_mode::RASTER, 5).unwrap();
        assert!(raster.pixels.iter().all(|&v| v == 0 || v == 255));
        let sdfaa = face.textcore_render(a, 32, render_mode::SDFAA, 5).unwrap();
        assert_eq!((sdfaa.padding, sdfaa.glyph), (5, smooth.glyph));
        assert_eq!(sdfaa.pixels.len(), ((w + 10) * (h + 10)) as usize);
        // An upsampled SDF reports the metrics of the larger load scaled back, and a rect the
        // larger bitmap divided (rounded up).
        let sdf8 = face.textcore_render(a, 32, render_mode::SDF8, 2).unwrap();
        let large = face.textcore_glyph(a, 256, MONOCHROME_RENDER).unwrap();
        assert_eq!(sdf8.glyph.advance, large.advance / 8.0);
        assert_eq!(sdf8.glyph.rect_width, (large.rect_width + 7) / 8);
        assert_eq!(sdf8.glyph.rect_height, (large.rect_height + 7) / 8);
        let tile = (sdf8.glyph.rect_width + 4) * (sdf8.glyph.rect_height + 4);
        assert_eq!(sdf8.pixels.len(), tile as usize);
        // Unknown modes and sizes render nothing.
        assert!(face.textcore_render(a, 32, 12345, 5).is_none());
        assert!(face.textcore_render(a, 0, render_mode::SMOOTH, 5).is_none());
    }

    #[test]
    fn hinted_loads_snap_heights_to_whole_pixels() {
        let face = face();
        let h = index(&face, 'H');
        let hinted = face.textcore_glyph(h, 37, COMPUTE_METRICS).unwrap();
        assert_eq!(hinted.bearing_y, hinted.bearing_y.round());
        assert_eq!(hinted.advance, hinted.advance.round());
        let unhinted = face.textcore_glyph(h, 37, NO_HINTING).unwrap();
        assert!((hinted.bearing_y - unhinted.bearing_y).abs() <= 1.0);
        // A space has an advance and no bitmap.
        let space = face
            .textcore_glyph(index(&face, ' '), 37, NO_HINTING)
            .unwrap();
        assert!(space.advance > 0.0 && space.rect_width == 0);
        let tile = face
            .textcore_render(index(&face, ' '), 37, render_mode::SDFAA, 4)
            .unwrap();
        assert!(tile.pixels.is_empty());
    }
}
