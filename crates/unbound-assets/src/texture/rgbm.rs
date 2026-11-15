//! HDR radiance stored as bytes: RGBM (measured Android 2D HDR imports, lightmaps and HDR
//! cubemaps) and double LDR (lightmaps and HDR cubemaps; tests/projects/lightmapencoding).

use super::mipmaps::{linear_to_gamma, quantize};

/// Shared-multiplier range and gamma measured by texturecompression's 6000.0.71f1 controls.
const RANGE: f32 = 5.0;
const GAMMA: f32 = 2.2;
// The minimum radiance is linear sRGB 0.1. It keeps the quantized multiplier nonzero.
const MINIMUM_RADIANCE: f32 = 0.010_022_826;

// The right operand wins unordered comparisons; byte conversion handles nonfinite results.
fn maximum(left: f32, right: f32) -> f32 {
    if left > right { left } else { right }
}

/// The radiance of one RGBA float pixel; a linear-flagged source is gamma-decoded first.
fn radiance(pixel: &[u8], decode_gamma: bool) -> [f32; 3] {
    std::array::from_fn(|c| {
        let value = f32::from_le_bytes(pixel[c * 4..c * 4 + 4].try_into().unwrap());
        if decode_gamma {
            super::mipmaps::gamma_to_linear(value)
        } else {
            value
        }
    })
}

/// Converts filtered RGBA floats to double LDR bytes: half of each channel's gamma-space value,
/// clamped to one, with opaque alpha. Shaders decode two times the stored value (in gamma) or
/// the sRGB-sampled value times 2^2.2 (in linear).
pub(super) fn encode_double_ldr(pixels: &[u8], decode_gamma: bool) -> Vec<u8> {
    let mut encoded = Vec::with_capacity(pixels.len() / 4);
    for pixel in pixels.chunks_exact(16) {
        for value in radiance(pixel, decode_gamma) {
            encoded.push(quantize((linear_to_gamma(value) * 0.5 * 255.0) as f64));
        }
        encoded.push(255);
    }
    encoded
}

/// Converts filtered RGBA floats to RGBM bytes. A linear-flagged source applies gamma decoding
/// before RGBM conversion. The quantized multiplier replaces source alpha.
pub(super) fn encode(pixels: &[u8], decode_gamma: bool) -> Vec<u8> {
    let mut encoded = Vec::with_capacity(pixels.len() / 4);
    for pixel in pixels.chunks_exact(16) {
        let rgb = radiance(pixel, decode_gamma);
        let maximum = maximum(maximum(MINIMUM_RADIANCE, rgb[2]), maximum(rgb[1], rgb[0]));
        let multiplier = (maximum.powf(1.0 / GAMMA) * (1.0 / RANGE) * 255.0).ceil() / 255.0;
        let scale = (multiplier * RANGE).powf(GAMMA);
        for value in rgb {
            encoded.push(quantize(
                (linear_to_gamma((value / scale).clamp(0.0, 1.0)) * 255.0) as f64,
            ));
        }
        encoded.push(quantize((multiplier * 255.0) as f64));
    }
    encoded
}

#[cfg(test)]
mod tests {
    fn floats(values: &[f32]) -> Vec<u8> {
        values.iter().flat_map(|v| v.to_le_bytes()).collect()
    }

    #[test]
    fn double_ldr_halves_gamma_space_values() {
        // Measured texels (lightmapencoding, Android Low): 0.002, 0.03, 1, 2, 2.5 and beyond 2^2.2.
        let pixel = floats(&[0.002, 0.03, 1.0, 1.0, 2.0, 2.5, 9.0, 1.0]);
        assert_eq!(
            super::encode_double_ldr(&pixel, false),
            [3, 24, 128, 255, 175, 193, 255, 255]
        );
        // Ordinary imperfections: nonfinite and negative radiance store bytes, not errors.
        let odd = floats(&[f32::NAN, f32::INFINITY, -1.0, 0.0]);
        assert_eq!(super::encode_double_ldr(&odd, false), [0, 255, 0, 255]);
    }

    #[test]
    fn multiplier_division_preserves_the_half_channel() {
        let pixel: Vec<u8> = [1.0_f32, 0.5, 0.1875, 0.25]
            .into_iter()
            .flat_map(f32::to_le_bytes)
            .collect();
        assert_eq!(super::encode(&pixel, true), [255, 128, 48, 51]);
    }

    #[test]
    fn nonfinite_pixels_are_tolerated_and_source_alpha_is_replaced() {
        let bytes = |values: [f32; 4]| {
            values
                .into_iter()
                .flat_map(f32::to_le_bytes)
                .collect::<Vec<_>>()
        };
        for decode_gamma in [false, true] {
            assert_eq!(
                super::encode(
                    &bytes([f32::NAN, f32::INFINITY, f32::NEG_INFINITY, -0.5]),
                    decode_gamma
                ),
                [0; 4]
            );
            assert_eq!(
                super::encode(&bytes([f32::INFINITY, 0.0, 0.0, 1.0]), decode_gamma),
                [0, 0, 0, 255]
            );
            let black = super::encode(&bytes([0.0; 4]), decode_gamma);
            assert!(black[3] > 0);
            assert_eq!(
                black,
                super::encode(&bytes([0.0, 0.0, 0.0, f32::NAN]), decode_gamma)
            );
        }
    }
}
