//! PNG images as the Unity Editor writes the images it bakes (matches the Unity 6000.0.71f1
//! Editor, tests/projects/bakedbuffers `written/Lightmap-N_comp_dir.png` and
//! `_comp_shadowmask.png`): 8-bit RGBA, the 72 dpi pHYs chunk, each row filtered with the filter
//! (None, Sub or Paeth) whose bytes have the least sum of magnitudes, zlib's own deflate at its
//! default level 6 with the filtered strategy and, for small images, a window just covering the
//! rows, in IDAT chunks of 8192 bytes.

/// The pHYs chunk's pixels per metre (72 dots per inch).
const PIXELS_PER_METRE: u32 = 2835;
/// The deflate output per IDAT chunk.
const IDAT_SIZE: usize = 8192;
/// zlib's compression level and memory level the files have.
const LEVEL: i32 = 6;
const MEMORY_LEVEL: i32 = 8;
/// zlib's filtered strategy.
const FILTERED: i32 = 1;
/// Rows of at most this many bytes get a smaller window.
const SMALL: usize = 16384;

/// The file of an image of `width` × `height` RGBA texels, `rgba` rows from the top.
pub fn write(width: usize, height: usize, rgba: &[u8]) -> Vec<u8> {
    assert_eq!(
        rgba.len(),
        width * height * 4,
        "an RGBA image of {width} x {height}"
    );
    let rows = filtered(width, height, rgba);
    let mut deflated = deflate(&rows, window_bits(rows.len()));
    optimize_header(&mut deflated, rows.len());
    let mut out = vec![0x89, b'P', b'N', b'G', b'\r', b'\n', 0x1a, b'\n'];
    let mut header = Vec::new();
    header.extend((width as u32).to_be_bytes());
    header.extend((height as u32).to_be_bytes());
    header.extend([8, 6, 0, 0, 0]); // 8 bits, RGBA, deflate, adaptive filtering, no interlace
    chunk(&mut out, b"IHDR", &header);
    let mut physical = Vec::new();
    physical.extend(PIXELS_PER_METRE.to_be_bytes());
    physical.extend(PIXELS_PER_METRE.to_be_bytes());
    physical.push(1);
    chunk(&mut out, b"pHYs", &physical);
    for part in deflated.chunks(IDAT_SIZE) {
        chunk(&mut out, b"IDAT", part);
    }
    chunk(&mut out, b"IEND", &[]);
    out
}

fn chunk(out: &mut Vec<u8>, kind: &[u8; 4], data: &[u8]) {
    out.extend((data.len() as u32).to_be_bytes());
    let mut checked = kind.to_vec();
    checked.extend(data);
    out.extend(checked.iter().copied());
    // SAFETY: crc32 reads `checked.len()` bytes of a live buffer.
    let crc = unsafe { libz_sys::crc32(0, checked.as_ptr(), checked.len() as libz_sys::uInt) };
    out.extend((crc as u32).to_be_bytes());
}

/// The rows, each its filter's number and its filtered bytes: of None (0), Sub (1) and Paeth
/// (4), the one whose bytes have the least sum of min(v, 256 - v), the lower number on a tie
/// (the row above the first is zeros).
fn filtered(width: usize, height: usize, rgba: &[u8]) -> Vec<u8> {
    let stride = width * 4;
    let mut out = Vec::with_capacity(height * (stride + 1));
    let zeros = vec![0u8; stride];
    for y in 0..height {
        let row = &rgba[y * stride..(y + 1) * stride];
        let above = if y == 0 {
            &zeros[..]
        } else {
            &rgba[(y - 1) * stride..y * stride]
        };
        let left = |k: usize| if k >= 4 { row[k - 4] } else { 0 };
        let up_left = |k: usize| if k >= 4 { above[k - 4] } else { 0 };
        let paeth = |k: usize| {
            let (a, b, c) = (left(k) as i32, above[k] as i32, up_left(k) as i32);
            let (pa, pb, pc) = ((b - c).abs(), (a - c).abs(), (a + b - 2 * c).abs());
            if pa <= pb && pa <= pc {
                a
            } else if pb <= pc {
                b
            } else {
                c
            }
        };
        let candidates: [(u8, Vec<u8>); 3] = [
            (0, row.to_vec()),
            (
                1,
                (0..stride).map(|k| row[k].wrapping_sub(left(k))).collect(),
            ),
            (
                4,
                (0..stride)
                    .map(|k| row[k].wrapping_sub(paeth(k) as u8))
                    .collect(),
            ),
        ];
        let cost =
            |bytes: &[u8]| -> u32 { bytes.iter().map(|&v| (v as u32).min(256 - v as u32)).sum() };
        let (kind, bytes) = candidates
            .iter()
            .fold(None::<&(u8, Vec<u8>)>, |best, c| match best {
                Some(b) if cost(&b.1) <= cost(&c.1) => Some(b),
                _ => Some(c),
            })
            .unwrap();
        out.push(*kind);
        out.extend(bytes);
    }
    out
}

/// The deflate window for `size` bytes of rows: the smallest covering them (and 262 bytes
/// more) when they are few, 8 bits taken as 9.
fn window_bits(size: usize) -> i32 {
    let mut bits = 15;
    if size <= SMALL {
        let mut half = 1usize << (bits - 1);
        while size + 262 <= half {
            half >>= 1;
            bits -= 1;
        }
    }
    if bits == 8 { 9 } else { bits }
}

/// The zlib header of few rows' deflate output names the smallest window covering them.
fn optimize_header(z: &mut [u8], size: usize) {
    if size > SMALL || z[0] & 0x0f != 8 || z[0] & 0xf0 > 0x70 {
        return;
    }
    let mut info = z[0] >> 4;
    let mut half = 1usize << (info + 7);
    if size > half {
        return;
    }
    loop {
        half >>= 1;
        info -= 1;
        if !(info > 0 && size <= half) {
            break;
        }
    }
    z[0] = (z[0] & 0x0f) | (info << 4);
    let t = (z[1] & 0xe0) as u32;
    z[1] = (t + 0x1f - (((z[0] as u32) << 8) + t) % 0x1f) as u8;
}

unsafe extern "C" fn allocate(
    _: libz_sys::voidpf,
    items: libz_sys::uInt,
    size: libz_sys::uInt,
) -> libz_sys::voidpf {
    // SAFETY: zlib asks for `items` × `size` bytes.
    unsafe { libc::calloc(items as libc::size_t, size as libc::size_t) }
}

unsafe extern "C" fn release(_: libz_sys::voidpf, address: libz_sys::voidpf) {
    // SAFETY: `address` came from `allocate`.
    unsafe { libc::free(address) }
}

/// `data` deflated by zlib itself (its own sources: the bytes the Editor's files hold).
fn deflate(data: &[u8], window_bits: i32) -> Vec<u8> {
    let capacity = data.len() + data.len() / 100 + 1024;
    let mut out = vec![0u8; capacity];
    // SAFETY: the stream is initialized by deflateInit2_ before use and ended after; its buffers
    // outlive it.
    unsafe {
        let mut stream = libz_sys::z_stream {
            next_in: data.as_ptr() as *mut u8,
            avail_in: data.len() as libz_sys::uInt,
            total_in: 0,
            next_out: out.as_mut_ptr(),
            avail_out: capacity as libz_sys::uInt,
            total_out: 0,
            msg: std::ptr::null_mut(),
            state: std::ptr::null_mut(),
            zalloc: allocate,
            zfree: release,
            opaque: std::ptr::null_mut(),
            data_type: 0,
            adler: 0,
            reserved: 0,
        };
        let status = libz_sys::deflateInit2_(
            &mut stream,
            LEVEL,
            libz_sys::Z_DEFLATED,
            window_bits,
            MEMORY_LEVEL,
            FILTERED,
            libz_sys::zlibVersion(),
            std::mem::size_of::<libz_sys::z_stream>() as i32,
        );
        assert_eq!(status, libz_sys::Z_OK, "zlib's deflateInit2 failed");
        let status = libz_sys::deflate(&mut stream, libz_sys::Z_FINISH);
        assert_eq!(
            status,
            libz_sys::Z_STREAM_END,
            "zlib's deflate did not finish"
        );
        out.truncate(stream.total_out as usize);
        libz_sys::deflateEnd(&mut stream);
    }
    out
}

#[cfg(test)]
mod tests {
    #[test]
    fn the_editors_baked_images_are_written_again_byte_for_byte() {
        let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects");
        for path in [
            "urp/Assets/Scenes/Shadowmask/Lightmap-0_comp_dir.png",
            "urp/Assets/Scenes/Shadowmask/Lightmap-0_comp_shadowmask.png",
            "lightprobes/Assets/Scenes/Main/Lightmap-0_comp_shadowmask.png",
            "lightprobes/Assets/Scenes/Extra/Lightmap-0_comp_shadowmask.png",
            "indirectspec/Assets/Scenes/Main/Lightmap-0_comp_shadowmask.png",
            "indirectspec/Assets/Scenes/Extra/Lightmap-0_comp_shadowmask.png",
        ] {
            let bytes = std::fs::read(root.join(path)).unwrap();
            let image = image::load_from_memory(&bytes).unwrap().to_rgba8();
            let made = super::write(
                image.width() as usize,
                image.height() as usize,
                image.as_raw(),
            );
            let first = made.iter().zip(&bytes).position(|(a, b)| a != b);
            assert!(
                made == bytes,
                "{path}: {} bytes, expected {}; first difference at {first:?}",
                made.len(),
                bytes.len()
            );
        }
    }
}
