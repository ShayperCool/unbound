//! ASTC encoding and decoding through Arm astc-encoder 4.3.0; standard Fast LDR through the MIT-licensed ISPCTextureCompressor C interface packaged by
//! intel_tex_2. The dependency's Rust ASTC encoder is unfinished; its C interface is complete.
use super::BlockFormat;

#[repr(C)]
struct Surface {
    pixels: *const u8,
    width: i32,
    height: i32,
    stride: i32,
}

#[repr(C)]
struct Settings {
    block_width: i32,
    block_height: i32,
    channels: i32,
    fast_skip_threshold: i32,
    refine_iterations: i32,
}

unsafe extern "C" {
    #[link_name = "CompressBlocksASTC"]
    fn compress_blocks(surface: *const Surface, output: *mut u8, settings: *const Settings);
}

pub(super) fn compress(rgba: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<u8> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let side = format.footprint() as usize;
    let (pw, ph) = (w.next_multiple_of(side), h.next_multiple_of(side));
    // ASTC clamps padding, including sub-block mip levels (texturecompression q0 controls).
    let mut padded = vec![0; pw * ph * 4];
    for y in 0..ph {
        for x in 0..pw {
            let from = (y.min(h - 1) * w + x.min(w - 1)) * 4;
            padded[(y * pw + x) * 4..][..4].copy_from_slice(&rgba[from..][..4]);
        }
    }
    let opaque = rgba[..w * h * 4].chunks_exact(4).all(|p| p[3] == 255);
    let preset = if opaque {
        intel_tex_2::astc::opaque_fast_settings(side as u32, side as u32)
    } else {
        intel_tex_2::astc::alpha_fast_settings(side as u32, side as u32)
    };
    let settings = Settings {
        block_width: preset.block_width as i32,
        block_height: preset.block_height as i32,
        channels: preset.channels as i32,
        fast_skip_threshold: preset.fast_skip_threshold as i32,
        refine_iterations: preset.refine_iterations as i32,
    };
    let surface = Surface {
        pixels: padded.as_ptr(),
        width: pw as i32,
        height: ph as i32,
        stride: (pw * 4) as i32,
    };
    let mut output = vec![0; format.level_size(width, height)];
    // The padded surface contains whole blocks. The C interface writes exactly 16 bytes per
    // block; all working buffers and settings are local to this call.
    unsafe {
        compress_blocks(&surface, output.as_mut_ptr(), &settings);
    }
    output
}

// astc-encoder contexts are exclusive to one importer or one thread's decoder cache.
struct Context(std::ptr::NonNull<std::ffi::c_void>);

unsafe extern "C" {
    fn unbound_astc_context(
        side: u32,
        hdr: bool,
        quality: f32,
        decode: bool,
    ) -> *mut std::ffi::c_void;
    fn unbound_astc_free(context: *mut std::ffi::c_void);
    fn unbound_astc_encode(
        context: *mut std::ffi::c_void,
        pixels: *const std::ffi::c_void,
        hdr: bool,
        width: u32,
        height: u32,
        channel: u32,
        opaque: bool,
        output: *mut u8,
        size: usize,
    ) -> i32;
    fn unbound_astc_decode(
        context: *mut std::ffi::c_void,
        blocks: *const u8,
        size: usize,
        width: u32,
        height: u32,
        floats: bool,
        output: *mut std::ffi::c_void,
    ) -> i32;
}

impl Context {
    fn new(format: BlockFormat, quality: f32, decode: bool) -> Self {
        // Only supported square footprints reach this boundary. The codec reports allocation
        // failure by null; malformed block data is handled by the decoder itself.
        let ptr = unsafe {
            unbound_astc_context(format.footprint(), format.is_astc_hdr(), quality, decode)
        };
        Self(std::ptr::NonNull::new(ptr).expect("ASTC context allocation failed"))
    }
}
impl Drop for Context {
    fn drop(&mut self) {
        unsafe { unbound_astc_free(self.0.as_ptr()) };
    }
}

/// One encoder for a texture's mip chain; channel 0 RGBA, 1 Single Channel red, 2 alpha.
pub(super) struct Encoder {
    format: BlockFormat,
    channel: u32,
    context: Option<Context>,
}
impl Encoder {
    pub(super) fn new(format: BlockFormat, quality: i32, channel: u32) -> Self {
        let fast = quality == 0 && channel == 0 && !format.is_astc_hdr() && format.footprint() <= 8;
        // The measured importer quality boundaries select Arm's Fast, Thorough and Exhaustive
        // search settings (10, 60 and 98). See texturecompression's quality controls.
        let effort = if quality < 50 {
            10.0
        } else if quality == 50 {
            60.0
        } else {
            98.0
        };
        Self {
            format,
            channel,
            context: (!fast).then(|| Context::new(format, effort, false)),
        }
    }

    pub(super) fn compress(&self, pixels: &[u8], width: u32, height: u32) -> Vec<u8> {
        let Some(context) = &self.context else {
            return compress(pixels, width, height, self.format);
        };
        let hdr = self.format.is_astc_hdr();
        let count = width.max(1) as usize * height.max(1) as usize * 4;
        assert!(pixels.len() >= count * if hdr { 4 } else { 1 });
        // HDR compression consumes half-float texels, including non-finite and negative inputs.
        // A typed buffer guarantees the C interface's alignment and is shared by every block.
        let half: Vec<u16> = if hdr {
            pixels[..count * 4]
                .chunks_exact(4)
                .map(|p| super::bcn::f32_to_f16(f32::from_le_bytes(p.try_into().unwrap())))
                .collect()
        } else {
            Vec::new()
        };
        let source = if hdr {
            half.as_ptr().cast()
        } else {
            pixels.as_ptr().cast()
        };
        let opaque = !hdr && pixels[..count].chunks_exact(4).all(|p| p[3] == 255);
        let mut output = vec![0; self.format.level_size(width, height)];
        // The source contains the full level; the codec clamps partial edge blocks. It writes
        // exactly the allocated block count and resets its image state before the next mip.
        let status = unsafe {
            unbound_astc_encode(
                context.0.as_ptr(),
                source,
                hdr,
                width.max(1),
                height.max(1),
                self.channel,
                opaque,
                output.as_mut_ptr(),
                output.len(),
            )
        };
        assert_eq!(status, 0, "ASTC encoding failed");
        output
    }
}

thread_local! {
    // At most twelve contexts per participating thread, one per footprint and LDR/HDR profile.
    static DECODERS: std::cell::RefCell<[Option<Context>; 12]> = const {
        std::cell::RefCell::new([const { None }; 12])
    };
}

fn with_decoder<R>(format: BlockFormat, f: impl FnOnce(&Context) -> R) -> R {
    let side = format.footprint() as usize;
    let index = match side {
        4 => 0,
        5 => 1,
        6 => 2,
        8 => 3,
        10 => 4,
        12 => 5,
        _ => unreachable!(),
    } + usize::from(format.is_astc_hdr()) * 6;
    DECODERS.with(|cache| {
        let mut cache = cache.borrow_mut();
        f(cache[index].get_or_insert_with(|| Context::new(format, 0.0, true)))
    })
}

/// Decodes one HDR block into caller-owned storage without allocating per pixel read.
pub fn decode_hdr_block(format: BlockFormat, block: &[u8; 16], output: &mut [f32]) -> bool {
    format.is_astc_hdr() && decode_block_into(format, block, output)
}

pub(super) fn decode_byte_block(format: BlockFormat, block: &[u8; 16], output: &mut [u8]) -> bool {
    format.is_astc() && !format.is_astc_hdr() && decode_block_into(format, block, output)
}

fn decode_block_into<T: Pixel>(format: BlockFormat, block: &[u8; 16], output: &mut [T]) -> bool {
    let side = format.footprint();
    if output.len() != (side * side * 4) as usize {
        return false;
    }
    with_decoder(format, |context| {
        // Input is one complete block and output holds its full footprint in the declared type.
        unsafe {
            unbound_astc_decode(
                context.0.as_ptr(),
                block.as_ptr(),
                block.len(),
                side,
                side,
                T::FLOATS,
                output.as_mut_ptr().cast(),
            ) == 0
        }
    })
}

trait Pixel: Default + Copy {
    const FLOATS: bool;
}
impl Pixel for u8 {
    const FLOATS: bool = false;
}
impl Pixel for f32 {
    const FLOATS: bool = true;
}

fn decode_into<T: Pixel>(blocks: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<T> {
    let (w, h) = (width.max(1) as usize, height.max(1) as usize);
    let side = format.footprint() as usize;
    let mut output = vec![T::default(); w * h * 4];
    with_decoder(format, |context| {
        let decode = |bytes: &[u8], w: usize, h: usize, into: &mut [T]| {
            // The byte and float entry points supply an output element type matching `floats`.
            // Input and output are disjoint, and the thread owns this codec context exclusively.
            let status = unsafe {
                unbound_astc_decode(
                    context.0.as_ptr(),
                    bytes.as_ptr(),
                    bytes.len(),
                    w as u32,
                    h as u32,
                    T::FLOATS,
                    into.as_mut_ptr().cast(),
                )
            };
            assert_eq!(status, 0, "ASTC decoding failed");
        };
        let size = format.level_size(width, height);
        if blocks.len() >= size {
            decode(&blocks[..size], w, h, &mut output);
        } else {
            // Internal callers may provide a truncated level; retain transparent zero for
            // missing blocks without letting the codec read beyond the supplied bytes.
            let across = w.div_ceil(side);
            let mut texels = [T::default(); 12 * 12 * 4];
            for (i, block) in blocks.chunks_exact(16).enumerate() {
                decode(block, side, side, &mut texels[..side * side * 4]);
                let (bx, by) = (i % across * side, i / across * side);
                for y in 0..side.min(h - by) {
                    let len = side.min(w - bx) * 4;
                    output[((by + y) * w + bx) * 4..][..len]
                        .copy_from_slice(&texels[y * side * 4..][..len]);
                }
            }
        }
    });
    output
}

pub(super) fn decode(blocks: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<u8> {
    decode_into(blocks, width, height, format)
}

/// HDR texels as four full-precision floats, including alpha and values above one.
pub fn decode_f32(blocks: &[u8], width: u32, height: u32, format: BlockFormat) -> Vec<f32> {
    assert!(format.is_astc_hdr());
    decode_into(blocks, width, height, format)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn mixed_quality_contexts_keep_their_own_search_settings() {
        const CHILD: &str = "UNBOUND_ASTC_QUALITY_CONTROL";
        if std::env::var_os(CHILD).is_none() {
            // A separate test process makes the first encoder quality independent of the
            // test harness's scheduling. The reference control follows a different quality.
            let status = std::process::Command::new(std::env::current_exe().unwrap())
                .args([
                    "--exact",
                    "texture::astc::tests::mixed_quality_contexts_keep_their_own_search_settings",
                ])
                .env(CHILD, "1")
                .status()
                .unwrap();
            assert!(status.success());
            return;
        }
        let source = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/texturecompression/Assets/Textures/SoftSourceRGBA32.png");
        let mut image =
            super::super::decode(&source, &std::fs::read(&source).unwrap(), false).unwrap();
        image.flip_rows();
        let format = BlockFormat::Astc6x6;
        let rgba = &image.data;
        Encoder::new(format, 100, 0).compress(rgba, 64, 64);
        let expected = [
            162, 130, 211, 99, 28, 132, 101, 0, 176, 229, 2, 91, 33, 240, 155, 206,
        ];
        for _ in 0..2 {
            let blocks = Encoder::new(format, 50, 0).compress(rgba, 64, 64);
            // Unity 6000.0.71f1, texturecompression SoftASTC6Q50, top-level block 111.
            assert_eq!(&blocks[111 * 16..112 * 16], &expected);
            Encoder::new(format, 100, 0).compress(rgba, 64, 64);
        }
    }

    #[test]
    fn hdr_constants_keep_radiance_and_alpha_in_partial_blocks() {
        let block = [
            252, 255, 255, 255, 255, 255, 255, 255, 0, 62, 128, 64, 0, 67, 0, 56,
        ];
        for format in [
            BlockFormat::AstcHdr4x4,
            BlockFormat::AstcHdr5x5,
            BlockFormat::AstcHdr6x6,
            BlockFormat::AstcHdr8x8,
            BlockFormat::AstcHdr10x10,
            BlockFormat::AstcHdr12x12,
        ] {
            let blocks = block.repeat(format.level_size(13, 7) / 16);
            assert!(
                decode_f32(&blocks, 13, 7, format)
                    .chunks_exact(4)
                    .all(|p| p == [1.5, 2.25, 3.5, 0.5])
            );
            assert!(
                decode(&blocks, 13, 7, format)
                    .chunks_exact(4)
                    .all(|p| p == [255, 255, 255, 128])
            );
            let mut pixel_block = [0.0; 12 * 12 * 4];
            let size = (format.footprint() * format.footprint() * 4) as usize;
            assert!(decode_hdr_block(format, &block, &mut pixel_block[..size]));
            assert!(
                pixel_block[..size]
                    .chunks_exact(4)
                    .all(|p| p == [1.5, 2.25, 3.5, 0.5])
            );
            assert!(!decode_hdr_block(
                format,
                &block,
                &mut pixel_block[..size - 1]
            ));
        }
    }

    #[test]
    fn hdr_encoding_tolerates_non_finite_and_negative_inputs() {
        let mut values = [1.5f32, 2.25, 3.5, 0.5].repeat(3 * 7);
        values[..4].copy_from_slice(&[f32::NAN, f32::INFINITY, f32::NEG_INFINITY, -0.5]);
        let pixels: Vec<_> = values.into_iter().flat_map(f32::to_le_bytes).collect();
        for quality in [0, 50, 100] {
            let format = BlockFormat::AstcHdr6x6;
            let blocks = Encoder::new(format, quality, 0).compress(&pixels, 3, 7);
            assert_eq!(blocks.len(), format.level_size(3, 7));
            let decoded = decode_f32(&blocks, 3, 7, format);
            assert_eq!(decoded.len(), 3 * 7 * 4);
            assert!(decoded.iter().all(|v| v.is_finite()));
            assert!(decoded.chunks_exact(4).any(|p| p[0] > 1.0));
        }
    }

    #[test]
    fn malformed_payloads_do_not_abort_raw_block_operations() {
        for format in [
            BlockFormat::Astc4x4,
            BlockFormat::Astc5x5,
            BlockFormat::Astc6x6,
            BlockFormat::Astc8x8,
        ] {
            let side = format.footprint();
            assert_eq!(
                decode(&[167; 16], side, side, format),
                [255, 0, 255, 255].repeat((side * side) as usize)
            );
            let mut seed = 31u64;
            for _ in 0..4096 {
                let mut block = [0u8; 16];
                for byte in &mut block {
                    seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
                    *byte = (seed >> 32) as u8;
                }
                assert_eq!(
                    decode(&block, side, side, format).len(),
                    (side * side * 4) as usize
                );
            }
        }
    }
    #[test]
    fn encoded_rectangles_decode_without_error_colour() {
        let rgba: Vec<u8> = (0..9)
            .flat_map(|y| {
                (0..13).flat_map(move |x| {
                    [
                        (x * 17) as u8,
                        (y * 23) as u8,
                        ((x ^ y) * 11) as u8,
                        (x * y * 39) as u8,
                    ]
                })
            })
            .collect();
        for format in [
            BlockFormat::Astc4x4,
            BlockFormat::Astc5x5,
            BlockFormat::Astc6x6,
            BlockFormat::Astc8x8,
        ] {
            let encoded = compress(&rgba, 13, 9, format);
            assert!(
                decode(&encoded, 13, 9, format)
                    .chunks_exact(4)
                    .any(|p| p != [255, 0, 255, 255])
            );
        }
    }
    #[test]
    fn fast_rgba_block_matches_the_reference() {
        let rgba = [
            81, 234, 124, 196, 213, 15, 76, 226, 113, 35, 178, 174, 17, 227, 79, 39, 134, 230, 98,
            198, 92, 118, 21, 216, 31, 168, 187, 47, 11, 70, 196, 92, 198, 253, 52, 69, 174, 186,
            137, 9, 89, 201, 47, 172, 82, 5, 102, 77, 19, 49, 242, 64, 205, 218, 170, 118, 31, 134,
            16, 37, 228, 32, 51, 251,
        ];
        assert_eq!(
            compress(&rgba, 4, 4, BlockFormat::Astc4x4),
            [
                67, 132, 99, 156, 201, 37, 190, 35, 144, 187, 172, 40, 169, 29, 98, 51
            ]
        );
    }
    #[test]
    fn tiny_reference_uses_clamped_six_by_six_padding() {
        let rgba = [
            49, 161, 205, 19, 190, 18, 237, 66, 108, 78, 116, 146, 19, 37, 34, 46,
        ];
        assert_eq!(
            compress(&rgba, 2, 2, BlockFormat::Astc6x6),
            [
                98, 132, 249, 128, 12, 250, 32, 144, 32, 176, 34, 144, 32, 176, 238, 78
            ]
        );
    }
    #[test]
    fn small_and_rectangular_levels_keep_whole_blocks() {
        for format in [
            BlockFormat::Astc4x4,
            BlockFormat::Astc5x5,
            BlockFormat::Astc6x6,
            BlockFormat::Astc8x8,
        ] {
            assert!(!format.is_bc());
            assert_eq!(
                BlockFormat::from_unity_format(format.unity_format()),
                Some(format)
            );
            let rgba = [70, 130, 90, 61].repeat(3 * 7);
            let blocks = compress(&rgba, 3, 7, format);
            assert_eq!(blocks.len(), format.level_size(3, 7));
            assert_eq!(decode(&blocks, 3, 7, format).len(), rgba.len());
            assert_eq!(decode(&[], 3, 7, format), vec![0; rgba.len()]);
        }
    }
}
