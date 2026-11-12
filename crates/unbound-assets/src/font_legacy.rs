//! Legacy dynamic-font rendering (Font, UI Text, TextMesh, IMGUI) through the bundled FreeType
//! library, configured as the Unity 6000.0.71f1 Editor renders dynamic fonts
//! (tests/projects/glyphraster). TextCore's glyphs use the same faces (`font_textcore`).
use super::font::GlyphBitmap;
use crate::font_freetype::{FT_Bitmap_Embolden, FreeTypeFace, coverage};
use freetype_sys as ft;

/// Synthetic italic: a horizontal shear of 0.25 (16.16 fixed point), applied only when the face
/// is not italic itself.
const ITALIC_SHEAR: ft::FT_Fixed = 0x4000;
/// Synthetic bold: the rendered bitmap grows by `max(pixel size / 55, 1)` pixels in both
/// directions; the advance stays the face's and the glyph's top moves up by the same amount.
const BOLD_SIZE_DIVISOR: f32 = 55.0;
const BOLD_MIN_STRENGTH: f32 = 1.0;
/// Load flags per `FontRenderingMode` (Smooth, HintedSmooth, HintedRaster, OSDefault); the
/// render mode is the load target's. Unknown modes render as Smooth.
const LOAD_FLAGS: [ft::FT_Int32; 4] = [
    ft::FT_LOAD_NO_HINTING,
    ft::FT_LOAD_DEFAULT,
    ft::FT_LOAD_TARGET_MONO,
    ft::FT_LOAD_NO_HINTING,
];

impl FreeTypeFace {
    /// The glyph of a face that cannot take the requested size (a bitmap-only face without that
    /// strike): no pixels, the advance in font units over 64, whatever the size (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/textcolor: a colour emoji face's glyph keeps advance 40
    /// at 32 and 64 pixels). `None` for a scalable face, whose size request does not fail.
    unsafe fn unsized_legacy_glyph(&mut self, index: ft::FT_UInt) -> Option<GlyphBitmap> {
        unsafe {
            if (*self.face).face_flags & ft::FT_FACE_FLAG_SCALABLE as ft::FT_Long != 0 {
                return None;
            }
            if ft::FT_Load_Glyph(self.face, index, ft::FT_LOAD_NO_SCALE as ft::FT_Int32) != 0 {
                return None;
            }
            let slot = &*(*self.face).glyph;
            Some(GlyphBitmap {
                advance: slot.metrics.horiAdvance as f32 / 64.0,
                left: 0,
                top: 0,
                top_offset: 0.0,
                width: 0,
                height: 0,
                bounds: (0.0, 0.0, 0.0, 0.0),
                coverage: Vec::new(),
            })
        }
    }

    /// A legacy dynamic-font glyph of `character` at `size` pixels per em.
    pub(crate) fn render_legacy(
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
            let sized =
                ft::FT_Set_Char_Size(self.face, 0, (size * 64.0) as ft::FT_F26Dot6, 0, 72) == 0;
            let index = ft::FT_Get_Char_Index(self.face, character as ft::FT_ULong);
            if index == 0 {
                return None;
            }
            if !sized {
                return self.unsized_legacy_glyph(index);
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

    // Editor: textcolor legacy/emoji. A bitmap-only face cannot take a size it has no strike for:
    // its glyph has no pixels and the advance in font units over 64 at every size.
    #[test]
    fn a_bitmap_only_face_gives_empty_glyphs_with_unscaled_advances() {
        let emoji = face(include_bytes!(
            "../../../tests/projects/textcolor/Assets/Resources/Fonts/EmojiProbe.ttf"
        ));
        for size in [32.0, 64.0] {
            let heart = emoji.render('\u{2764}', size, false, false).unwrap();
            assert!(heart.coverage.is_empty());
            assert_eq!((heart.width, heart.height), (0, 0));
            assert_eq!(heart.advance, 39.84375);
        }
        // A scalable face is unchanged: its glyph has pixels and a scaled advance.
        let sans = face(SANS).render('W', 32.0, false, false).unwrap();
        assert!(!sans.coverage.is_empty());
    }

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
