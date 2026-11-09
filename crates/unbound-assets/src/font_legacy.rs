//! Legacy dynamic-font rendering through the bundled FreeType library. TextCore's outline and
//! distance-field path remains independent. The configuration is measured by glyphraster.
use super::font::GlyphBitmap;
use freetype_sys as ft;
use std::sync::Arc;

// Legacy synthesis settings measured across normal/intrinsic/synthetic styles by glyphraster.
const ITALIC_SHEAR: ft::FT_Fixed = 0x4000;
const BOLD_SIZE_DIVISOR: f32 = 55.0;

unsafe extern "C" {
    fn FT_Bitmap_Embolden(
        library: ft::FT_Library,
        bitmap: *mut ft::FT_Bitmap,
        x_strength: ft::FT_Pos,
        y_strength: ft::FT_Pos,
    ) -> ft::FT_Error;
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
            let flags = match mode {
                1 => ft::FT_LOAD_DEFAULT,
                2 => ft::FT_LOAD_TARGET_MONO,
                _ => ft::FT_LOAD_NO_HINTING,
            };
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
            let render_mode = if mode == 2 {
                ft::FT_RENDER_MODE_MONO
            } else {
                ft::FT_RENDER_MODE_NORMAL
            };
            if ft::FT_Render_Glyph(slot, render_mode) != 0 {
                return None;
            }
            // Synthetic bold grows the bitmap, preserving the font's own advance. At small sizes
            // it adds one pixel; the larger-size slope is measured by glyphraster's 64/96 controls.
            let strength = if bold && style_flags & ft::FT_STYLE_FLAG_BOLD == 0 {
                (size / BOLD_SIZE_DIVISOR).max(1.0)
            } else {
                0.0
            };
            if strength > 0.0
                && slot.bitmap.width > 0
                && slot.bitmap.rows > 0
                && FT_Bitmap_Embolden(
                    self.library,
                    &mut slot.bitmap,
                    (strength * 64.0) as ft::FT_Pos,
                    (strength * 64.0) as ft::FT_Pos,
                ) != 0
            {
                return None;
            }
            let bitmap = &slot.bitmap;
            let (width, height) = (bitmap.width, bitmap.rows);
            let length = (width as usize).checked_mul(height as usize)?;
            let mut coverage = Vec::new();
            coverage.try_reserve_exact(length).ok()?;
            coverage.resize(length, 0);
            if length != 0 {
                if bitmap.buffer.is_null() {
                    return None;
                }
                let stride = bitmap.pitch.unsigned_abs() as usize;
                for y in 0..height as usize {
                    let row = if bitmap.pitch >= 0 {
                        y
                    } else {
                        height as usize - 1 - y
                    };
                    let source =
                        std::slice::from_raw_parts(bitmap.buffer.add(row * stride), stride);
                    for x in 0..width as usize {
                        coverage[y * width as usize + x] = match bitmap.pixel_mode as u32 {
                            ft::FT_PIXEL_MODE_GRAY => *source.get(x)?,
                            ft::FT_PIXEL_MODE_MONO => {
                                if source.get(x / 8)? & (0x80 >> (x % 8)) != 0 {
                                    255
                                } else {
                                    0
                                }
                            }
                            _ => {
                                tracing::warn!(
                                    "legacy font has unsupported bitmap layout {}",
                                    bitmap.pixel_mode
                                );
                                return None;
                            }
                        };
                    }
                }
            }
            let left = slot.bitmap_left;
            Some(GlyphBitmap {
                advance: slot.metrics.horiAdvance as f32 / 64.0,
                left,
                top: slot.bitmap_top,
                top_offset: strength,
                width,
                height,
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
    fn invalid_sizes_and_missing_characters_are_rejected() {
        let font = face(SANS);
        for size in [0.0, -1.0, f32::NAN, f32::INFINITY] {
            assert!(font.render('A', size, false, false).is_none());
        }
        assert!(font.render('\u{10ffff}', 13.0, false, false).is_none());
    }
}
