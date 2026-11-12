//! A font face opened in the bundled FreeType library, shared by the two rasterizers built on it:
//! legacy dynamic fonts (`font_legacy`) and TextCore's font engine (`font_textcore`). Each face
//! owns its library, so faces can be used from different threads; callers serialize access to
//! one face (`FontFace` keeps it behind a mutex) and set the size and transform per request.
use freetype_sys as ft;
use std::sync::Arc;

unsafe extern "C" {
    pub(crate) fn FT_Bitmap_Embolden(
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

/// Rows of converted bitmaps are aligned to 4 bytes, as FreeType's conversion is asked to.
const CONVERT_ALIGNMENT: ft::FT_Int = 4;

pub(crate) struct FreeTypeFace {
    pub(crate) face: ft::FT_Face,
    pub(crate) library: ft::FT_Library,
    _data: Arc<[u8]>,
}

// Each instance owns its library and face and is only accessed through FontFace's mutex.
unsafe impl Send for FreeTypeFace {}

impl Drop for FreeTypeFace {
    fn drop(&mut self) {
        unsafe {
            ft::FT_Done_Face(self.face);
            ft::FT_Done_FreeType(self.library);
        }
    }
}

impl FreeTypeFace {
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
                anyhow::bail!("opening font for rendering: FreeType error {result}");
            }
            Ok(Self {
                face,
                library,
                _data: data,
            })
        }
    }

    /// Whether the face has colour glyphs (FreeType's colour face flag: colour bitmaps or layers).
    pub(crate) fn has_color(&self) -> bool {
        unsafe { (*self.face).face_flags & ft::FT_FACE_FLAG_COLOR as ft::FT_Long != 0 }
    }

    /// The face's own style as a Unity `FontStyle`: 1 bold, 2 italic.
    pub(crate) fn style(&self) -> i32 {
        let flags = unsafe { (*self.face).style_flags };
        i32::from(flags & ft::FT_STYLE_FLAG_BOLD != 0)
            | (i32::from(flags & ft::FT_STYLE_FLAG_ITALIC != 0) << 1)
    }
}

/// A rendered bitmap as 8-bit coverage, top row first. An 8-bit gray bitmap is taken as it is;
/// other layouts (monochrome, 2/4-bit gray, colour) go through FreeType's conversion, and fewer
/// than 256 levels are then stretched to 0..255 as `level * (1 / (levels - 1)) * 255`, truncated
/// (a monochrome bitmap becomes 0 and 255).
/// Unmeasured: synthetic bold on an embedded 2/4-bit strike leaves an 8-bit bitmap with its few
/// levels, which is taken unstretched (no probe font has such strikes).
pub(crate) unsafe fn coverage(library: ft::FT_Library, bitmap: &ft::FT_Bitmap) -> Option<Vec<u8>> {
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

/// A colour (BGRA) bitmap as RGBA bytes, top row first, its colours premultiplied by alpha as
/// FreeType gives them (`None` for other layouts).
pub(crate) unsafe fn color_rgba(bitmap: &ft::FT_Bitmap) -> Option<Vec<u8>> {
    if bitmap.pixel_mode as u32 != ft::FT_PIXEL_MODE_BGRA {
        return None;
    }
    let width = usize::try_from(bitmap.width).ok()?;
    let height = usize::try_from(bitmap.rows).ok()?;
    let mut rgba = Vec::new();
    rgba.try_reserve_exact(width.checked_mul(height)?.checked_mul(4)?)
        .ok()?;
    if width == 0 || height == 0 {
        return Some(rgba);
    }
    if bitmap.buffer.is_null() {
        return None;
    }
    let stride = bitmap.pitch.unsigned_abs() as usize;
    if stride < width * 4 {
        return None;
    }
    for y in 0..height {
        let row = if bitmap.pitch >= 0 { y } else { height - 1 - y };
        let source =
            unsafe { std::slice::from_raw_parts(bitmap.buffer.add(row * stride), width * 4) };
        for bgra in source.chunks_exact(4) {
            rgba.extend_from_slice(&[bgra[2], bgra[1], bgra[0], bgra[3]]);
        }
    }
    Some(rgba)
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
