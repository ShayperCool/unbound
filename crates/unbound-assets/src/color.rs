//! Color conversions shared by importers, the renderer and lighting.

/// Unity's gamma-to-linear conversion of one color channel (Mathf.GammaToLinearSpace): the sRGB
/// curve up to 1 and a 2.2 power above it (HDR values).
pub fn gamma_to_linear(c: f32) -> f32 {
    if c <= 0.04045 {
        c / 12.92
    } else if c < 1.0 {
        ((c + 0.055) / 1.055).powf(2.4)
    } else {
        c.powf(2.2)
    }
}
