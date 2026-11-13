//! RGBM source processing for measured 2D HDR Crunch imports.

use super::mipmaps::{linear_to_gamma, quantize};

/// Shared-multiplier range and gamma measured by texturecompression's 6000.0.71f1 controls.
const RANGE: f32 = 5.0;
const GAMMA: f32 = 2.2;
// The minimum radiance is linear sRGB 0.1. It keeps the quantized multiplier nonzero.
const MINIMUM_RADIANCE: f32 = 0.010_022_826;

/// Converts already-filtered linear RGBA floats to RGBM bytes. The multiplier replaces alpha;
/// nonfinite/negative colour is bounded before the shared maximum is selected.
pub(super) fn encode(linear: &[u8]) -> Vec<u8> {
    let mut encoded = Vec::with_capacity(linear.len() / 4);
    for pixel in linear.chunks_exact(16) {
        let rgb: [f32; 3] = std::array::from_fn(|c| {
            let value = f32::from_le_bytes(pixel[c * 4..c * 4 + 4].try_into().unwrap());
            value.max(0.0).min(RANGE)
        });
        let maximum = rgb.into_iter().fold(MINIMUM_RADIANCE, f32::max);
        let multiplier = (maximum.powf(1.0 / GAMMA) * (1.0 / RANGE) * 255.0).ceil() * (1.0 / 255.0);
        let scale = (multiplier * RANGE).powf(GAMMA);
        for value in rgb {
            encoded.push(quantize((linear_to_gamma(value / scale) * 255.0) as f64));
        }
        encoded.push(quantize((multiplier * 255.0) as f64));
    }
    encoded
}

#[cfg(test)]
mod tests {
    #[test]
    fn nonfinite_channels_are_bounded_and_source_alpha_is_replaced() {
        let bytes = |values: [f32; 4]| {
            values
                .into_iter()
                .flat_map(f32::to_le_bytes)
                .collect::<Vec<_>>()
        };
        let special = super::encode(&bytes([f32::NEG_INFINITY, f32::INFINITY, f32::NAN, -999.0]));
        let bounded = super::encode(&bytes([0.0, 5.0, 0.0, 1.0]));
        assert_eq!(special, bounded);
        assert_eq!(special.len(), 4);
        assert!(special[3] > 0);
        assert_eq!(
            super::encode(&bytes([0.0; 4])),
            super::encode(&bytes([0.0, 0.0, 0.0, f32::NAN]))
        );
    }
}
