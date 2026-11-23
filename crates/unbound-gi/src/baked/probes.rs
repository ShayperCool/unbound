//! Baked light probes: the light at each probe's point as L2 spherical harmonics, the direct
//! light of the bake's lights and the light that reaches the point from other surfaces and the
//! sky (matches the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper,
//! tests/projects/bakedbuffers `Probes*`).

use super::direct::{MixedMode, bakes_direct};
use super::grid::Grid;
use super::light::{Light, Tables};
use super::trace::{Ray, Scene};

/// The coefficients of a probe's light: nine, each red, green and blue.
pub type Coefficients = [[f32; 3]; 9];

/// The nine real L2 spherical harmonics' constant factors.
const K0: f32 = 0.282_094_8;
const K1: f32 = 0.488_602_5;
const K2: f32 = 1.092_548_4;
const K20: f32 = 0.946_174_7;
const K20_OFFSET: f32 = 0.315_391_57;
const K22: f32 = 0.546_274_2;

/// Adds light of `color` arriving from `direction` (of unit length, towards where it comes
/// from) to `out`, projected on the harmonics and times pi.
pub fn add(out: &mut Coefficients, color: [f32; 3], direction: [f32; 3]) {
    let [x, y, z] = direction;
    let basis = [
        K0,
        x * K1,
        y * K1,
        z * K1,
        x * y * K2,
        y * z * K2,
        (z * z * K20) - K20_OFFSET,
        x * z * K2,
        (x * x - y * y) * K22,
    ];
    for (coefficient, b) in out.iter_mut().zip(basis) {
        for (o, c) in coefficient.iter_mut().zip(color) {
            *o += c * b * std::f32::consts::PI;
        }
    }
}

/// The direct light of the bake's lights at each probe's point: every directional light and
/// the lights of the point's region of the light grid whose direct light the bake keeps,
/// unshadowed from the point (which faces every way).
pub fn direct(
    positions: &[[f32; 3]],
    lights: &[Light],
    grid: &Grid,
    mixed: MixedMode,
    casters: &Scene,
) -> Vec<Coefficients> {
    let tables = Tables::new();
    positions
        .iter()
        .map(|&p| {
            let mut out = Coefficients::default();
            let mut take = |index: usize| {
                let light = &lights[index];
                if !bakes_direct(light, mixed) {
                    return;
                }
                let Some(arriving) = light.arriving(&tables, light.color, p, [0.0; 3]) else {
                    return;
                };
                let shadowed = light.casts_shadows
                    && casters.occluded(
                        &Ray {
                            origin: p,
                            direction: arriving.direction,
                        },
                        arriving.distance,
                    );
                if !shadowed {
                    add(&mut out, arriving.color, arriving.direction);
                }
            };
            for &light in &grid.directional {
                take(light);
            }
            if let Some(region) = grid.region(p) {
                for &light in &region.lights {
                    take(light);
                }
            }
            out
        })
        .collect()
}
