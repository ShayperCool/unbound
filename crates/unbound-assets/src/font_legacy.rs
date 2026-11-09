//! Legacy dynamic-font rendering (Font, UI Text, TextMesh, IMGUI) through the bundled FreeType
//! library, configured as the Unity 6000.0.71f1 Editor renders dynamic fonts
//! (tests/projects/glyphraster). TextCore's outline and distance-field path stays independent.
use super::font::GlyphBitmap;
use freetype_sys as ft;
use std::sync::Arc;

/// Synthetic italic: a horizontal shear of 0.25 (16.16 fixed point), applied only when the face
/// is not italic itself.
const ITALIC_SHEAR: ft::FT_Fixed = 0x4000;
/// Synthetic bold: the rendered bitmap grows by `max(pixel size / 55, 1)` pixels in both
/// directions; the advance stays the face's and the glyph's top moves up by the same amount.
const BOLD_SIZE_DIVISOR: f32 = 55.0;
const BOLD_MIN_STRENGTH: f32 = 1.0;
/// Rows of converted bitmaps are aligned to 4 bytes, as FreeType's conversion is asked to.
const CONVERT_ALIGNMENT: ft::FT_Int = 4;

/// Load flags per `FontRenderingMode` (Smooth, HintedSmooth, HintedRaster, OSDefault); the
/// render mode is the load target's. Unknown modes render as Smooth.
const LOAD_FLAGS: [ft::FT_Int32; 4] = [
    ft::FT_LOAD_NO_HINTING,
    ft::FT_LOAD_DEFAULT,
    ft::FT_LOAD_TARGET_MONO,
    ft::FT_LOAD_NO_HINTING,
];

unsafe extern "C" {
    fn FT_Bitmap_Embolden(
        library: ft::FT_Library,
        bitmap: *mut ft::FT_Bitmap,
        x_strength: ft::FT_Pos,
        y_strength: ft::FT_Pos,
    ) -> ft::FT_Error;
    fn FT_Bitmap_Init(bitmap: *mut ft::FT_Bitmap);
    fn FT_Bitmap_Convert(
        library: ft::FT_Library,
        source: *const ft::FT_Bitmap,
        target: *mut ft::FT_Bitmap,
        alignment: ft::FT_Int,
    ) -> ft::FT_Error;
    fn FT_Bitmap_Done(library: ft::FT_Library, bitmap: *mut ft::FT_Bitmap) -> ft::FT_Error;
}

pub(crate) struct LegacyFace {
    face: ft::FT_Face,
    library: ft::FT_Library,
    _data: Arc<[u8]>,
}

// Each instance owns its library and face and is only accessed through FontFace's mutex.
unsafe impl Send for LegacyFace {}

impl Drop for LegacyFace {
    fn drop(&mut self) {
        unsafe {
            ft::FT_Done_Face(self.face);
            ft::FT_Done_FreeType(self.library);
        }
    }
}

impl LegacyFace {
    pub(crate) fn new(data: Arc<[u8]>, index: u32) -> anyhow::Result<Self> {
        let data_length = data.len().try_into()?;
        unsafe {
            let mut library = std::ptr::null_mut();
            anyhow::ensure!(
                ft::FT_Init_FreeType(&mut library) == 0,
                "initializing font rendering"
            );
            let mut face = std::ptr::null_mut();
            let result = ft::FT_New_Memory_Face(
                library,
                data.as_ptr(),
                data_length,
                index.into(),
                &mut face,
            );
            if result != 0 {
                ft::FT_Done_FreeType(library);
                anyhow::bail!("opening font for legacy rendering: FreeType error {result}");
            }
            Ok(Self {
                face,
                library,
                _data: data,
            })
        }
    }

    /// The face's own style as a Unity `FontStyle`: 1 bold, 2 italic.
    pub(crate) fn style(&self) -> i32 {
        let flags = unsafe { (*self.face).style_flags };
        i32::from(flags & ft::FT_STYLE_FLAG_BOLD != 0)
            | (i32::from(flags & ft::FT_STYLE_FLAG_ITALIC != 0) << 1)
    }

    pub(crate) fn render(
        &mut self,
        character: char,
        size: f32,
        bold: bool,
        italic: bool,
        mode: i32,
    ) -> Option<GlyphBitmap> {
        if !size.is_finite() || size <= 0.0 {
            return None;
        }
        unsafe {
            let style_flags = (*self.face).style_flags;
            let shear = if italic && style_flags & ft::FT_STYLE_FLAG_ITALIC == 0 {
                ITALIC_SHEAR
            } else {
                0
            };
            let mut matrix = ft::FT_Matrix {
                xx: 0x10000,
                xy: shear,
                yx: 0,
                yy: 0x10000,
            };
            ft::FT_Set_Transform(self.face, &mut matrix, std::ptr::null_mut());
            if ft::FT_Set_Char_Size(self.face, 0, (size * 64.0) as ft::FT_F26Dot6, 0, 72) != 0 {
                return None;
            }
            let index = ft::FT_Get_Char_Index(self.face, character as ft::FT_ULong);
            if index == 0 {
                return None;
            }
            let flags = usize::try_from(mode)
                .ok()
                .and_then(|mode| LOAD_FLAGS.get(mode))
                .copied()
                .unwrap_or(LOAD_FLAGS[0]);
            if ft::FT_Load_Glyph(self.face, index, flags) != 0 {
                return None;
            }
            let slot = &mut *(*self.face).glyph;
            let mut control = ft::FT_BBox {
                xMin: 0,
                yMin: 0,
                xMax: 0,
                yMax: 0,
            };
            ft::FT_Outline_Get_CBox(&slot.outline, &mut control);
            if slot.format != ft::FT_GLYPH_FORMAT_BITMAP
                && ft::FT_Render_Glyph(slot, ((flags >> 16) & 15) as ft::FT_Render_Mode) != 0
            {
                return None;
            }
            let strength = if bold && style_flags & ft::FT_STYLE_FLAG_BOLD == 0 {
                (size / BOLD_SIZE_DIVISOR).max(BOLD_MIN_STRENGTH)
            } else {
                0.0
            };
            if strength > 0.0 && slot.bitmap.width > 0 && slot.bitmap.rows > 0 {
                let pixels = (strength * 64.0) as ft::FT_Pos;
                if FT_Bitmap_Embolden(self.library, &mut slot.bitmap, pixels, pixels) != 0 {
                    return None;
                }
            }
            let coverage = coverage(self.library, &slot.bitmap)?;
            Some(GlyphBitmap {
                advance: slot.metrics.horiAdvance as f32 / 64.0,
                left: slot.bitmap_left,
                top: slot.bitmap_top,
                top_offset: strength,
                width: slot.bitmap.width,
                height: slot.bitmap.rows,
                bounds: (
                    control.xMin as f32 / 64.0,
                    control.yMin as f32 / 64.0,
                    control.xMax as f32 / 64.0,
                    control.yMax as f32 / 64.0,
                ),
                coverage,
            })
        }
    }
}

/// A rendered bitmap as 8-bit coverage, top row first. An 8-bit gray bitmap is taken as it is
/// (also one that emboldening widened from fewer gray levels); other layouts (monochrome, 2/4-bit
/// gray, colour) go through FreeType's conversion, and fewer than 256 levels are then stretched
/// to 0..255 as `level * (1 / (levels - 1)) * 255`, truncated.
unsafe fn coverage(library: ft::FT_Library, bitmap: &ft::FT_Bitmap) -> Option<Vec<u8>> {
    if bitmap.pixel_mode as u32 == ft::FT_PIXEL_MODE_GRAY {
        return unsafe { copy_rows(bitmap, 1.0) };
    }
    unsafe {
        let mut converted = std::mem::zeroed::<ft::FT_Bitmap>();
        FT_Bitmap_Init(&mut converted);
        let result = if FT_Bitmap_Convert(library, bitmap, &mut converted, CONVERT_ALIGNMENT) == 0 {
            let levels = converted.num_grays as f32;
            let scale = if converted.num_grays == 256 || levels < 2.0 {
                1.0
            } else {
                (1.0 / (levels - 1.0)) * 255.0
            };
            copy_rows(&converted, scale)
        } else {
            None
        };
        FT_Bitmap_Done(library, &mut converted);
        result
    }
}

/// The rows of an 8-bit bitmap, each value multiplied by `scale` (1 keeps it).
unsafe fn copy_rows(bitmap: &ft::FT_Bitmap, scale: f32) -> Option<Vec<u8>> {
    let width = usize::try_from(bitmap.width).ok()?;
    let height = usize::try_from(bitmap.rows).ok()?;
    let length = width.checked_mul(height)?;
    let mut coverage = Vec::new();
    coverage.try_reserve_exact(length).ok()?;
    if length == 0 {
        return Some(coverage);
    }
    if bitmap.buffer.is_null() {
        return None;
    }
    let stride = bitmap.pitch.unsigned_abs() as usize;
    if stride < width {
        return None;
    }
    for y in 0..height {
        let row = if bitmap.pitch >= 0 { y } else { height - 1 - y };
        let source = unsafe { std::slice::from_raw_parts(bitmap.buffer.add(row * stride), width) };
        if scale == 1.0 {
            coverage.extend_from_slice(source);
        } else {
            coverage.extend(source.iter().map(|&v| (v as f32 * scale) as i32 as u8));
        }
    }
    Some(coverage)
}

#[cfg(test)]
mod tests {
    use crate::font::FontFace;

    fn face(bytes: &[u8]) -> FontFace {
        FontFace::from_bytes(bytes.to_vec(), 0).unwrap()
    }
    const SANS: &[u8] =
        include_bytes!("../../../tests/projects/glyphraster/Assets/Resources/Synthetic.ttf");
    const STYLED: &[u8] =
        include_bytes!("../../../tests/projects/glyphraster/Assets/Resources/Styled.ttf");

    #[test]
    fn empty_glyph_keeps_its_advance_and_bold_bearing() {
        let font = face(SANS);
        let normal = font.render(' ', 13.0, false, false).unwrap();
        let bold = font.render(' ', 13.0, true, false).unwrap();
        assert!(normal.coverage.is_empty() && bold.coverage.is_empty());
        assert_eq!(normal.advance, bold.advance);
        assert!(normal.advance > 3.0);
        assert_eq!(bold.top_offset, normal.top_offset + 1.0);
    }

    #[test]
    fn styles_in_the_face_are_not_synthesized_twice() {
        let font = face(STYLED);
        let normal = font.render('W', 32.0, false, false).unwrap();
        let styled = font.render('W', 32.0, true, true).unwrap();
        assert_eq!(normal, styled);
    }

    #[test]
    fn synthetic_styles_preserve_advance_and_large_size_bearings() {
        let font = face(SANS);
        let normal = font.render('A', 64.0, false, false).unwrap();
        let bold = font.render('A', 64.0, true, false).unwrap();
        let italic = font.render('A', 64.0, false, true).unwrap();
        assert_eq!(normal.advance, bold.advance);
        assert_eq!(normal.advance, italic.advance);
        assert_eq!(
            bold.top as f32 + bold.top_offset,
            normal.top as f32 + 64.0 / 55.0
        );
        assert!(bold.coverage != normal.coverage);
        assert!(italic.coverage != normal.coverage);
    }

    #[test]
    fn the_face_reports_its_own_style() {
        assert_eq!(face(SANS).legacy_style(), 0);
        assert_eq!(face(STYLED).legacy_style(), 3);
    }

    #[test]
    fn hinted_raster_glyphs_are_fully_covered_or_empty() {
        let font = face(SANS);
        let raster = font.render_legacy('W', 24.0, false, false, 2).unwrap();
        let smooth = font.render_legacy('W', 24.0, false, false, 0).unwrap();
        assert!(raster.coverage.iter().all(|&v| v == 0 || v == 255));
        assert!(raster.coverage.contains(&255));
        assert!(smooth.coverage.iter().any(|&v| v != 0 && v != 255));
        assert_eq!(
            raster.coverage.len(),
            (raster.width * raster.height) as usize
        );
        // Synthetic bold widens monochrome bitmaps too.
        let bold = font.render_legacy('W', 24.0, true, false, 2).unwrap();
        assert!(bold.coverage.iter().all(|&v| v == 0 || v == 255));
        assert_eq!(bold.width, raster.width + 1);
    }

    #[test]
    fn unknown_rendering_modes_render_smooth() {
        let font = face(SANS);
        let smooth = font.render_legacy('g', 13.0, false, false, 0);
        assert_eq!(font.render_legacy('g', 13.0, false, false, 7), smooth);
        assert_eq!(font.render_legacy('g', 13.0, false, false, -1), smooth);
        assert_eq!(font.render_legacy('g', 13.0, false, false, 3), smooth);
    }

    #[test]
    fn invalid_sizes_and_missing_characters_are_rejected() {
        let font = face(SANS);
        for size in [0.0, -1.0, f32::NAN, f32::INFINITY] {
            assert!(font.render('A', size, false, false).is_none());
        }
        assert!(font.render('\u{10ffff}', 13.0, false, false).is_none());
    }
}
